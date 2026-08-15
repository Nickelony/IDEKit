using StreamJsonRpc;
using System.Diagnostics;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Owns the language-server transport sessions of <see cref="LanguageServerClient"/>: process and JSON-RPC
/// construction, failure detection, graceful teardown, and detached-session cleanup.
/// </summary>
/// <remarks>
/// Exactly one session is active at a time: the host reports process exits and JSON-RPC disconnects, detaches the
/// affected session, and queues its cleanup so transport callback threads are never blocked by teardown. A session
/// disposed before its transport was attached has no graceful channel, so its live process is terminated directly.
/// The session lifecycle and its rationale live in <c>docs/LanguageServerInternals.md</c>.
/// </remarks>
internal sealed class TransportHost
{
	private readonly CapabilityStore _capabilityStore;
	private readonly DiagnosticsRouter _diagnosticsRouter;
	private readonly ProtocolForwarder _protocolForwarder;
	private readonly ILogger _logger;
	private readonly IReadOnlyList<WorkspaceFolder> _workspaceFolders;
	private readonly string _workspaceRootsDisplayText;
	private readonly Func<bool> _isDisposed;
	private readonly Action<long> _raiseTransportUnavailable;
	private readonly CancellationToken _lifetimeToken;
	private readonly TimeSpan _shutdownRequestTimeout;
	private readonly TimeSpan _disposeWaitTimeout;

	// A server that follows the protocol exits after receiving the exit notification; the grace period gives an
	// orderly exit a chance to complete instead of racing forced termination, while a server that ignores the
	// sequence is still terminated promptly.
	private static readonly TimeSpan s_serverExitGracePeriod = TimeSpan.FromMilliseconds(500);

	private readonly object _failedSessionDisposalSyncRoot = new();
	private Task _queuedFailedSessionDisposal = Task.CompletedTask;

	/// <summary>
	/// Initializes a new instance of the <see cref="TransportHost"/> class.
	/// </summary>
	/// <param name="capabilityStore">Owns session publication and transport-generation fencing.</param>
	/// <param name="diagnosticsRouter">Receives server callbacks for the created session targets.</param>
	/// <param name="protocolForwarder">Sends the graceful shutdown and exit messages during teardown.</param>
	/// <param name="logger">The logger used for transport lifecycle diagnostics.</param>
	/// <param name="workspaceFolders">The workspace folder descriptors advertised to the language server and used in callback payloads.</param>
	/// <param name="workspaceRootsDisplayText">The comma-joined normalized workspace root paths used in diagnostics.</param>
	/// <param name="isDisposed">Reports whether the owning client started disposal.</param>
	/// <param name="raiseTransportUnavailable">Raises the client transport-unavailable event for a lost ready transport.</param>
	/// <param name="shutdownRequestTimeout">How long graceful shutdown waits for the server to answer the <c>shutdown</c> request.</param>
	/// <param name="disposeWaitTimeout">How long disposal waits for background transport work to quiesce.</param>
	/// <param name="lifetimeToken">Signals client disposal to the standard-error read loop.</param>
	internal TransportHost(
		CapabilityStore capabilityStore,
		DiagnosticsRouter diagnosticsRouter,
		ProtocolForwarder protocolForwarder,
		ILogger logger,
		IReadOnlyList<WorkspaceFolder> workspaceFolders,
		string workspaceRootsDisplayText,
		Func<bool> isDisposed,
		Action<long> raiseTransportUnavailable,
		TimeSpan shutdownRequestTimeout,
		TimeSpan disposeWaitTimeout,
		CancellationToken lifetimeToken)
	{
		_capabilityStore = capabilityStore;
		_diagnosticsRouter = diagnosticsRouter;
		_protocolForwarder = protocolForwarder;
		_logger = logger;
		_workspaceFolders = workspaceFolders;
		_workspaceRootsDisplayText = workspaceRootsDisplayText;
		_isDisposed = isDisposed;
		_raiseTransportUnavailable = raiseTransportUnavailable;
		_lifetimeToken = lifetimeToken;
		_shutdownRequestTimeout = shutdownRequestTimeout;
		_disposeWaitTimeout = disposeWaitTimeout;
	}

	/// <summary>
	/// Gets or replaces the most recently queued detached-session cleanup task.
	/// </summary>
	internal Task QueuedFailedSessionCleanupTask
	{
		get
		{
			lock (_failedSessionDisposalSyncRoot)
				return _queuedFailedSessionDisposal;
		}

		set
		{
			lock (_failedSessionDisposalSyncRoot)
				_queuedFailedSessionDisposal = value;
		}
	}

	/// <summary>
	/// Creates a transport session that owns an opened transport connection.
	/// </summary>
	/// <param name="connection">The open connection returned by the client's transport.</param>
	/// <returns>The transport session owning the connection.</returns>
	internal TransportSession CreateTransportSession(ILanguageServerConnection connection)
	{
		ArgumentNullException.ThrowIfNull(connection);

		long generation = _capabilityStore.NextTransportGeneration();

		return new TransportSession(generation,
			connection.Process,
			connection.ReadStream,
			connection.WriteStream,
			connection.ErrorStream)
		{
			Connection = connection
		};
	}

	/// <summary>
	/// Configures the transport objects and process callbacks for one session before it becomes active.
	/// </summary>
	/// <param name="session">The session to configure.</param>
	internal void ConfigureTransportSession(TransportSession session)
	{
		Process? process = session.Process;

		session.MessageHandler = CreateMessageHandler(session.WriteStream, session.ReadStream);
		session.RpcTarget = CreateRpcTarget(session.Generation);
		session.JsonRpc = CreateJsonRpc(session);
		session.RpcCompletionTask = session.JsonRpc.Completion;

		session.ProcessExitedHandler = (_, _) => HandleProcessExited(session);

		if (process is not null)
		{
			// Exit detection requires the raising-events flag; the built-in transport sets it, but a custom
			// transport that returns a Process without it would otherwise lose exit detection and the client could
			// stay "ready" after the server died.
			process.EnableRaisingEvents = true;
			process.Exited += session.ProcessExitedHandler;
		}
	}

	/// <summary>
	/// Creates the callback target exposed to the language server for one transport generation.
	/// </summary>
	/// <param name="transportGeneration">The transport generation that owns the callback target.</param>
	/// <returns>The configured callback target.</returns>
	internal ClientRpcTarget CreateRpcTarget(long transportGeneration)
		=> new(_capabilityStore, _diagnosticsRouter, _protocolForwarder, transportGeneration, _workspaceFolders, _logger);

	/// <summary>
	/// Starts the JSON-RPC listener and background stderr loop for one configured session.
	/// </summary>
	/// <param name="session">The configured session to start.</param>
	internal void StartTransportSession(TransportSession session)
	{
		JsonRpc jsonRpc = session.JsonRpc
			?? throw new InvalidOperationException("The language server transport session is missing a JSON-RPC transport.");

		jsonRpc.StartListening();
		session.StderrLoopTask = Task.Run(() => ReadStandardErrorLoopAsync(session), CancellationToken.None);
	}

	/// <summary>
	/// Creates the JSON-RPC message handler for the transport streams.
	/// </summary>
	/// <remarks>
	/// The host logger is passed to every tolerant converter so the payload problems they recover from are reported
	/// through the client's own logging pipeline instead of being lost.
	/// </remarks>
	/// <param name="writeStream">The writable stream carrying host requests to the server.</param>
	/// <param name="readStream">The readable stream carrying server responses back to the host.</param>
	/// <returns>The configured message handler.</returns>
	internal HeaderDelimitedMessageHandler CreateMessageHandler(Stream writeStream, Stream readStream)
	{
		var serializerOptions = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		};

		serializerOptions.Converters.Add(new CompletionResponseJsonConverter(_logger));
		serializerOptions.Converters.Add(new DefinitionResponseJsonConverter(_logger));
		serializerOptions.Converters.Add(new HoverResponseJsonConverter(_logger));
		serializerOptions.Converters.Add(new PublishDiagnosticsParamsJsonConverter(_logger));

		// List-valued responses whose entries are not wrapped by a feature response type: one malformed entry must
		// not discard the remaining usable entries. Edit-bearing collections require an array root: an empty
		// collection is a legal outcome for them ("nothing to apply"), so degrading a malformed root to an empty
		// collection would turn a broken payload into a successful no-op.
		serializerOptions.Converters.Add(new TolerantCollectionJsonConverter<ReferenceLocationPayload>(_logger));
		serializerOptions.Converters.Add(new TolerantCollectionJsonConverter<TextEditPayload>(_logger, requireArrayRoot: true));
		serializerOptions.Converters.Add(new TolerantCollectionJsonConverter<WorkspaceDocumentChangePayload>(_logger, requireArrayRoot: true));

		// Server-request arrays preserve their request positions: the configuration request/response pair is
		// positional, so a null or malformed entry keeps its slot as a placeholder instead of shifting every
		// later answer. The registration arrays are order-insensitive and keep the skip-with-a-warning policy.
		serializerOptions.Converters.Add(new WorkspaceConfigurationItemsJsonConverter(_logger));
		serializerOptions.Converters.Add(new TolerantCollectionJsonConverter<CapabilityRegistrationPayload>(_logger));
		serializerOptions.Converters.Add(new TolerantCollectionJsonConverter<CapabilityUnregistrationPayload>(_logger));

		// Register the tolerant feature converters with the host logger as well, so the skipped-entry warnings they
		// document are not lost when they are instantiated through their parameterless (attribute) constructors.
		serializerOptions.Converters.Add(new CodeActionsResponseJsonConverter(_logger));
		serializerOptions.Converters.Add(new DocumentSymbolsResponseJsonConverter(_logger));
		serializerOptions.Converters.Add(new SignatureHelpResponseJsonConverter(_logger));
		serializerOptions.Converters.Add(new SignatureHelpSignaturePayloadJsonConverter(_logger));

		var formatter = new SystemTextJsonFormatter
		{
			JsonSerializerOptions = serializerOptions
		};

		return new HeaderDelimitedMessageHandler(writeStream, readStream, formatter);
	}

	/// <summary>
	/// Creates the JSON-RPC transport bound to the supplied session.
	/// </summary>
	/// <param name="session">The transport session.</param>
	/// <returns>The configured JSON-RPC instance.</returns>
	internal JsonRpc CreateJsonRpc(TransportSession session)
	{
		HeaderDelimitedMessageHandler messageHandler = session.MessageHandler
			?? throw new InvalidOperationException("The language server transport session is missing a JSON-RPC message handler.");

		ClientRpcTarget rpcTarget = session.RpcTarget
			?? throw new InvalidOperationException("The language server transport session is missing a JSON-RPC callback target.");

		var jsonRpc = new JsonRpc(messageHandler, rpcTarget);

		jsonRpc.Disconnected += (_, eventArgs) => HandleJsonRpcDisconnected(session, eventArgs);
		return jsonRpc;
	}

	/// <summary>
	/// Handles unexpected server process exit for one transport session.
	/// </summary>
	/// <param name="session">The session whose process exited.</param>
	internal void HandleProcessExited(TransportSession session)
	{
		// Read the exit code before the detached-session cleanup can dispose the process, and before the
		// session detach decides whether this exit belongs to the active transport.
		int? exitCode = TryReadProcessExitCode(session.Process);

		if (!_capabilityStore.TryDetachSpecificActiveSession(session, out bool wasReady))
		{
			// The session is no longer the active one: either it never became active, or a concurrent disconnect,
			// restart, or disposal already detached it. The startup path reports startup failures with stderr
			// context, so keep this at debug level but preserve the exit code evidence.
			if (exitCode is not null && !_isDisposed())
			{
				_logger.LogDebug("Language server process for transport generation {Generation} in workspace '{Workspace}' exited with code {ExitCode} while its transport session was not the active one (it never became active or was already detached).",
					session.Generation,
					_workspaceRootsDisplayText,
					exitCode.Value);
			}

			return;
		}

		if (wasReady)
			_raiseTransportUnavailable(session.Generation);

		DisposeFailedSessionInBackground(session, "process exit");

		if (!_isDisposed())
		{
			_logger.LogWarning("Language server process for transport generation {Generation} in workspace '{Workspace}' exited unexpectedly{ExitCodeSuffix}; the host will recreate the session on the next startup attempt.",
				session.Generation,
				_workspaceRootsDisplayText,
				exitCode is not null ? $" with code {exitCode.Value}" : string.Empty);

			LogRecentStandardErrorContext(session, "Unexpected process exit");
		}
	}

	/// <summary>
	/// Handles JSON-RPC transport disconnection for the active session.
	/// </summary>
	/// <param name="session">The disconnected session.</param>
	/// <param name="eventArgs">The disconnect event arguments.</param>
	internal void HandleJsonRpcDisconnected(TransportSession session, JsonRpcDisconnectedEventArgs eventArgs)
	{
		if (!_capabilityStore.TryDetachSpecificActiveSession(session, out bool wasReady))
		{
			_logger.LogDebug("Ignoring disconnect from stale language server transport generation {Generation}: {Description}",
				session.Generation,
				eventArgs.Description);

			return;
		}

		DisposeFailedSessionInBackground(session, "transport disconnect");

		if (_isDisposed())
		{
			_logger.LogDebug("Language server JSON-RPC transport generation {Generation} for workspace '{Workspace}' disconnected during client disposal: {Description}",
				session.Generation,
				_workspaceRootsDisplayText,
				eventArgs.Description);

			return;
		}

		// Local disposal of a still-active session is only reachable through the handler contract (every production
		// dispose path detaches first), but it is a deliberate state: report it as expected instead of as a loss.
		if (eventArgs.Reason == DisconnectedReason.LocallyDisposed)
		{
			_logger.LogInformation("Language server JSON-RPC transport generation {Generation} for workspace '{Workspace}' disconnected during expected local shutdown: {Description}",
				session.Generation,
				_workspaceRootsDisplayText,
				eventArgs.Description);

			return;
		}

		if (wasReady)
			_raiseTransportUnavailable(session.Generation);

		Exception? exception = eventArgs.Exception;

		if (exception is not null)
		{
			_logger.LogWarning(exception,
				"Language server JSON-RPC transport generation {Generation} for workspace '{Workspace}' disconnected unexpectedly (reason={Reason}); the host will recreate the session on the next startup attempt: {Description}",
				session.Generation,
				_workspaceRootsDisplayText,
				eventArgs.Reason,
				eventArgs.Description);

			LogRecentStandardErrorContext(session, "Unexpected transport disconnect");

			return;
		}

		_logger.LogWarning("Language server JSON-RPC transport generation {Generation} for workspace '{Workspace}' disconnected unexpectedly (reason={Reason}); the host will recreate the session on the next startup attempt: {Description}",
			session.Generation,
			_workspaceRootsDisplayText,
			eventArgs.Reason,
			eventArgs.Description);

		LogRecentStandardErrorContext(session, "Unexpected transport disconnect");
	}

	/// <summary>
	/// Disposes a detached failed session without blocking the disconnect or process-exit callback thread.
	/// </summary>
	/// <param name="session">The detached failed session.</param>
	/// <param name="reason">The failure reason used for diagnostics when cleanup itself fails.</param>
	private void DisposeFailedSessionInBackground(TransportSession session, string reason)
	{
		lock (_failedSessionDisposalSyncRoot)
		{
			Task previousDisposal = _queuedFailedSessionDisposal;

			_queuedFailedSessionDisposal = Task.Run(
				() => DisposeFailedSessionAfterAsync(previousDisposal, session, reason));
		}
	}

	private async Task DisposeFailedSessionAfterAsync(Task previousDisposal, TransportSession session, string reason)
	{
		try
		{
			await previousDisposal.ConfigureAwait(false);
		}
		catch
		{
			// Later cleanup should still run even if earlier detached cleanup failed.
		}

		await DisposeFailedSessionAsync(session, reason).ConfigureAwait(false);
	}

	private async Task DisposeFailedSessionAsync(TransportSession session, string reason)
	{
		try
		{
			await DisposeSessionAsync(session).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception,
				"Failed to dispose detached language server transport generation {Generation} after {Reason}.",
				session.Generation,
				reason);
		}
	}

	/// <summary>
	/// Disposes one transport session and all of its associated resources.
	/// </summary>
	/// <param name="session">The session to dispose.</param>
	internal async Task DisposeSessionAsync(TransportSession session)
	{
		Task? rpcCompletionTask = session.RpcCompletionTask;
		Task? stderrLoopTask = session.StderrLoopTask;

		try
		{
			if (session.Process is not null && session.ProcessExitedHandler is not null)
				session.Process.Exited -= session.ProcessExitedHandler;
		}
		catch
		{
			// Ignore event detach failures.
		}

		try
		{
			Process? serverProcess = session.Process;

			// The graceful sequence is only attempted on a channel whose liveness the client can observe: a spawned
			// process that is still running, or a transport-supplied connection, which the client treats as live
			// until it disposes it. A session built directly from raw streams has no liveness signal, so it is torn
			// down without the protocol handshake.
			bool hasLiveChannel = serverProcess is null ? session.Connection is not null : !serverProcess.HasExited;

			if (hasLiveChannel)
			{
				if (session.JsonRpc is not null)
				{
					_logger.LogInformation("Attempting graceful shutdown for language server transport generation {Generation} in workspace '{Workspace}'.",
						session.Generation,
						_workspaceRootsDisplayText);

					await TrySendShutdownAsync(session).ConfigureAwait(false);
					await TrySendExitNotificationAsync(session).ConfigureAwait(false);

					// Re-read the process after the awaits above: the local keeps the null-state analysis sound
					// and the kill path cannot dereference a process that a concurrent teardown cleared.
					Process? liveProcess = session.Process;

					if (liveProcess is not null && !await WaitForServerExitAsync(liveProcess).ConfigureAwait(false))
					{
						_logger.LogWarning("Language server transport generation {Generation} in workspace '{Workspace}' did not exit after graceful shutdown; forcing process termination.",
							session.Generation,
							_workspaceRootsDisplayText);

						LogRecentStandardErrorContext(session, "Forced process termination");

						liveProcess.Kill(true);
					}
				}
				else if (serverProcess is not null)
				{
					// A session disposed before its JSON-RPC transport was attached has no channel for the graceful
					// shutdown/exit sequence, so the live process is terminated directly; otherwise, it would keep
					// running until host exit (Windows job object) or indefinitely.
					_logger.LogInformation("Terminating language server transport generation {Generation} in workspace '{Workspace}' because disposal reached the session before its transport was attached.",
						session.Generation,
						_workspaceRootsDisplayText);

					LogRecentStandardErrorContext(session, "Forced process termination");

					serverProcess.Kill(true);
				}
			}
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Disposing language server transport generation {Generation} raised exceptions while stopping the server process.", session.Generation);
		}
		finally
		{
			try
			{
				session.JsonRpc?.Dispose();
			}
			catch
			{
				// Ignore JSON-RPC disposal failures.
			}

			try
			{
				if (session.MessageHandler is not null)
					await session.MessageHandler.DisposeAsync().ConfigureAwait(false);
			}
			catch
			{
				// Ignore message-handler disposal failures.
			}

			await ReleaseSessionResourcesAsync(session).ConfigureAwait(false);
		}

		await WaitForBackgroundLoopsAsync(rpcCompletionTask, stderrLoopTask).ConfigureAwait(false);
	}

	/// <summary>
	/// Releases the resources owned by a transport session and clears its transport state.
	/// </summary>
	/// <param name="session">The session whose resources should be released.</param>
	/// <remarks>
	/// A session created from a transport connection releases that connection, which owns the streams and the
	/// process; a session built directly from raw streams or a process owns them itself.
	/// </remarks>
	private async Task ReleaseSessionResourcesAsync(TransportSession session)
	{
		ILanguageServerConnection? connection = session.Connection;

		if (connection is not null)
		{
			try
			{
				await connection.DisposeAsync().ConfigureAwait(false);
			}
			catch (Exception exception)
			{
				_logger.LogDebug(exception, "Disposing the transport connection of language server transport generation {Generation} failed.", session.Generation);
			}
		}
		else
		{
			// Each resource is released independently: a throw from one disposal must not skip the remaining
			// streams or the process.
			DisposeIgnoringFailure(session.WriteStream);
			DisposeIgnoringFailure(session.ReadStream);
			DisposeIgnoringFailure(session.ErrorStream);
			DisposeIgnoringFailure(session.Process);
		}

		session.RpcCompletionTask = null;
		session.StderrLoopTask = null;
		session.JsonRpc = null;
		session.MessageHandler = null;
		session.RpcTarget = null;
	}

	/// <summary>
	/// Disposes one session resource while ignoring disposal failures.
	/// </summary>
	/// <param name="resource">The resource to dispose, or <see langword="null"/> when the session has none.</param>
	private static void DisposeIgnoringFailure(IDisposable? resource)
	{
		try
		{
			resource?.Dispose();
		}
		catch
		{
			// The session is being torn down anyway; one failed disposal must not block the others.
		}
	}

	/// <summary>
	/// Tries to send a graceful shutdown request to the server.
	/// </summary>
	/// <param name="session">The session being shut down.</param>
	private async Task TrySendShutdownAsync(TransportSession session)
	{
		using var shutdownTimeout = new CancellationTokenSource(_shutdownRequestTimeout);

		Task<object?>? shutdownTask = null;

		try
		{
			shutdownTask = _protocolForwarder.SendRequestCoreAsync<object?>(session, LspMethodNames.Shutdown, new EmptyParams(), allowDisposed: true, shutdownTimeout.Token);
			await shutdownTask.WaitAsync(_shutdownRequestTimeout).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			shutdownTimeout.Cancel();
			HandleShutdownTimeout(session, shutdownTask);
		}
		catch (OperationCanceledException) when (shutdownTimeout.IsCancellationRequested)
		{
			HandleShutdownTimeout(session, shutdownTask);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception,
				"Sending the language server shutdown request during disposal failed for transport generation {Generation} in workspace '{Workspace}'; continuing teardown.",
				session.Generation,
				_workspaceRootsDisplayText);

			LogRecentStandardErrorContext(session, "Shutdown failure");
		}
	}

	/// <summary>
	/// Logs that the server did not acknowledge the shutdown request and observes the late task so its failure is not unobserved.
	/// </summary>
	/// <param name="session">The session that timed out.</param>
	/// <param name="shutdownTask">The still-running shutdown request, when it was issued.</param>
	private void HandleShutdownTimeout(TransportSession session, Task<object?>? shutdownTask)
	{
		if (shutdownTask is not null)
			_protocolForwarder.ObserveAbandonedTask(shutdownTask);

		_logger.LogWarning("Language server transport generation {Generation} in workspace '{Workspace}' did not acknowledge shutdown within {TimeoutMs} ms; continuing teardown.",
			session.Generation,
			_workspaceRootsDisplayText,
			(int)_shutdownRequestTimeout.TotalMilliseconds);

		LogRecentStandardErrorContext(session, "Shutdown timeout");
	}

	/// <summary>
	/// Tries to queue the exit notification to the server within the shutdown request timeout.
	/// </summary>
	/// <param name="session">The session being shut down.</param>
	/// <remarks>
	/// The dispatch is bounded so no teardown call site can wait forever on a server that stopped draining its
	/// standard input; when the bound elapses, teardown continues and relies on forced process termination.
	/// </remarks>
	private async Task TrySendExitNotificationAsync(TransportSession session)
	{
		Task exitNotificationTask = _protocolForwarder.SendNotificationCoreAsync(session, LspMethodNames.Exit, allowDisposed: true, CancellationToken.None);

		try
		{
			await exitNotificationTask.WaitAsync(_shutdownRequestTimeout).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			_protocolForwarder.ObserveAbandonedTask(exitNotificationTask);

			_logger.LogWarning("The language server exit notification did not complete within {TimeoutMs} ms during teardown for transport generation {Generation} in workspace '{Workspace}'; continuing teardown and relying on process termination.",
				(int)_shutdownRequestTimeout.TotalMilliseconds,
				session.Generation,
				_workspaceRootsDisplayText);

			LogRecentStandardErrorContext(session, "Exit notification timeout");
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Sending the language server exit notification during disposal failed for transport generation {Generation}; continuing teardown.", session.Generation);
		}
	}

	/// <summary>
	/// Waits for the server process to exit after the graceful shutdown sequence, within a short grace period.
	/// </summary>
	/// <param name="serverProcess">The server process that should exit on its own.</param>
	/// <returns>
	/// <see langword="true"/> when the process exited within the grace period; otherwise, <see langword="false"/>
	/// so the caller forces termination.
	/// </returns>
	/// <remarks>
	/// The exit notification dispatch completing only proves that the bytes reached the stream, not that the
	/// server finished its own teardown; without the wait an orderly exit races forced termination and its final
	/// standard error output is lost. The grace period is deliberately short because a server that ignores the
	/// shutdown sequence must still be terminated promptly.
	/// </remarks>
	private async Task<bool> WaitForServerExitAsync(Process serverProcess)
	{
		Task waitForExitTask = serverProcess.WaitForExitAsync();

		try
		{
			await waitForExitTask.WaitAsync(s_serverExitGracePeriod).ConfigureAwait(false);
			return true;
		}
		catch (TimeoutException)
		{
			// The abandoned wait keeps running; observe it so a later fault is logged instead of surfacing as an
			// unobserved task exception, matching every other abandoned wait in this type.
			_protocolForwarder.ObserveAbandonedTask(waitForExitTask);
			return false;
		}
	}

	/// <summary>
	/// Waits for the background read and stderr loops to finish within the dispose timeout.
	/// </summary>
	/// <param name="readLoopTask">The main JSON-RPC completion task.</param>
	/// <param name="stderrLoopTask">The standard-error read loop task.</param>
	internal async Task WaitForBackgroundLoopsAsync(Task? readLoopTask, Task? stderrLoopTask)
	{
		Task combined = Task.WhenAll(
			readLoopTask ?? Task.CompletedTask,
			stderrLoopTask ?? Task.CompletedTask);

		try
		{
			await combined.WaitAsync(_disposeWaitTimeout).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			_protocolForwarder.ObserveAbandonedTask(combined);

			_logger.LogWarning("Language server background loops did not complete within {TimeoutMs} ms during disposal.",
				(int)_disposeWaitTimeout.TotalMilliseconds);
		}
		catch (Exception exception)
		{
			bool loggedSpecificLoop = false;
			loggedSpecificLoop |= TryLogCanceledBackgroundLoop(readLoopTask, "JSON-RPC completion");
			loggedSpecificLoop |= TryLogCanceledBackgroundLoop(stderrLoopTask, "stderr read");
			loggedSpecificLoop |= TryLogFaultedBackgroundLoop(readLoopTask, "JSON-RPC completion");
			loggedSpecificLoop |= TryLogFaultedBackgroundLoop(stderrLoopTask, "stderr read");

			if (!loggedSpecificLoop)
				_logger.LogWarning(exception, "Language server background loop failed during disposal.");
		}
	}

	/// <summary>
	/// Logs cancellation for one background loop when disposal canceled it intentionally.
	/// </summary>
	/// <param name="task">The loop task to inspect.</param>
	/// <param name="loopName">The logical loop name for diagnostics.</param>
	/// <returns><see langword="true"/> when cancellation was logged; otherwise, <see langword="false"/>.</returns>
	private bool TryLogCanceledBackgroundLoop(Task? task, string loopName)
	{
		if (task?.IsCanceled != true)
			return false;

		_logger.LogDebug("Language server background loop '{LoopName}' was canceled during disposal.", loopName);
		return true;
	}

	/// <summary>
	/// Logs the failure for one background loop when it faulted during disposal.
	/// </summary>
	/// <param name="task">The loop task to inspect.</param>
	/// <param name="loopName">The logical loop name for diagnostics.</param>
	/// <returns><see langword="true"/> when a fault was logged; otherwise, <see langword="false"/>.</returns>
	private bool TryLogFaultedBackgroundLoop(Task? task, string loopName)
	{
		if (task?.IsFaulted != true || task.Exception is not { } aggregateException)
			return false;

		Exception loggedException = aggregateException.Flatten().InnerExceptions.Count == 1
			? aggregateException.Flatten().InnerExceptions[0]
			: aggregateException.Flatten();

		_logger.LogWarning(loggedException, "Language server background loop '{LoopName}' failed during disposal.", loopName);
		return true;
	}

	/// <summary>
	/// Logs recent stderr context for one session when a transport failure path needs more diagnostics.
	/// </summary>
	/// <param name="session">The session whose recent stderr should be reported.</param>
	/// <param name="context">The failure context label.</param>
	internal void LogRecentStandardErrorContext(TransportSession? session, string context)
	{
		string? recentStandardError = session?.GetRecentStandardErrorSummary();

		if (string.IsNullOrWhiteSpace(recentStandardError))
			return;

		_logger.LogWarning("{Context} recent language server stderr: {RecentStandardError}", context, recentStandardError);
	}

	/// <summary>
	/// Reads standard-error output from the language-server process and logs non-empty lines.
	/// </summary>
	/// <param name="session">The transport session whose process stderr should be read.</param>
	private async Task ReadStandardErrorLoopAsync(TransportSession session)
	{
		try
		{
			// Read to end-of-stream instead of checking HasExited between reads: when the process exits with lines
			// still buffered in the pipe, those lines are the crash diagnostics the ring exists for. The loop stays
			// bounded by the lifetime token when a grandchild keeps the inherited error handle open.
			while (!_isDisposed())
			{
				Process? process = session.Process;

				if (process is null)
					break;

				string? line = await process.StandardError.ReadLineAsync(_lifetimeToken).ConfigureAwait(false);

				if (line is null)
					break;

				if (!string.IsNullOrWhiteSpace(line))
				{
					session.RecordStandardErrorLine(line);
					_logger.LogDebug("[LS stderr] {Line}", line);
				}
			}
		}
		catch (OperationCanceledException)
		{ }
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Language server stderr read loop failed.");
		}
	}

	/// <summary>
	/// Tries to read the exit code for a process that may already be disposed.
	/// </summary>
	/// <param name="process">The process to inspect.</param>
	/// <returns>The exit code, or <see langword="null"/> when unavailable.</returns>
	private static int? TryReadProcessExitCode(Process? process)
	{
		if (process is null)
			return null;

		try
		{
			return process.HasExited ? process.ExitCode : null;
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}
}
