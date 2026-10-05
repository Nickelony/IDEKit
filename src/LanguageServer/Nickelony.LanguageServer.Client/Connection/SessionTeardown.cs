using System.Diagnostics;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Tears down one connection session: the graceful shutdown sequence, forced termination, resource release, and the wait for its background loops.
/// </summary>
/// <remarks>
/// The stages run in a fixed order, and a failure
/// in one stage never skips the release of the session's resources or the wait for its background loops. The order and its rationale live
/// in the <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/LanguageServerInternals.md">language-server internals guide</see>.
/// </remarks>
internal sealed class SessionTeardown
{
	private readonly ProtocolSender _protocolSender;
	private readonly StandardErrorReader _standardErrorReader;
	private readonly BackgroundTaskObserver _backgroundTaskObserver;
	private readonly ILogger _logger;
	private readonly string _workspaceRootsDisplayText;

	private readonly TimeSpan _shutdownRequestTimeout;
	private readonly TimeSpan _disposeWaitTimeout;

	// A server that follows the protocol exits after receiving the exit notification; the grace period gives an
	// orderly exit a chance to complete instead of racing forced termination, while a server that ignores the
	// sequence is still terminated promptly.
	private static readonly TimeSpan s_serverExitGracePeriod = TimeSpan.FromMilliseconds(500);

	/// <param name="context">The shared connection-collaborator context (logger, workspace display text, and lifetime).</param>
	/// <param name="protocolSender">Sends the graceful shutdown and exit messages.</param>
	/// <param name="standardErrorReader">Reports the recent standard error of a session when a teardown stage fails.</param>
	/// <param name="backgroundTaskObserver">Observes the stages that disposal abandons and reports the loops that ended during disposal.</param>
	/// <param name="shutdownRequestTimeout">How long graceful shutdown waits for the server to answer the <c>shutdown</c> request.</param>
	internal SessionTeardown(
		ConnectionCollaboratorContext context,
		ProtocolSender protocolSender,
		StandardErrorReader standardErrorReader,
		BackgroundTaskObserver backgroundTaskObserver,
		TimeSpan shutdownRequestTimeout
	)
	{
		_protocolSender = protocolSender;
		_standardErrorReader = standardErrorReader;
		_backgroundTaskObserver = backgroundTaskObserver;
		_logger = context.Logger;
		_workspaceRootsDisplayText = context.WorkspaceRootsDisplayText;
		_shutdownRequestTimeout = shutdownRequestTimeout;
		_disposeWaitTimeout = context.Lifetime.DisposeWaitTimeout;
	}

	/// <summary>Disposes one connection session and all of its associated resources.</summary>
	/// <param name="session">The session to dispose.</param>
	internal async Task DisposeSessionAsync(ConnectionSession session)
	{
		Task? rpcCompletionTask = session.RpcCompletionTask;
		Task? standardErrorLoopTask = session.StandardErrorLoopTask;

		// The exit dispatch and the outbound drain share one shutdown request timeout that starts at the dispatch, so the
		// graceful sequence costs at most two timeouts and the exit grace period.
		long? exitDispatchTimestamp = null;

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
			// process that is still running, or a connection without a process, which the client treats as live
			// until it disposes it.
			bool hasLiveChannel = serverProcess is null || !serverProcess.HasExited;

			if (hasLiveChannel)
			{
				if (session.JsonRpc is not null)
				{
					_logger.LogInformation(
						"Attempting graceful shutdown for language server connection generation {Generation} in workspace '{Workspace}'.",
						session.ConnectionGeneration,
						_workspaceRootsDisplayText
					);

					await SendShutdownAsync(session).ConfigureAwait(false);

					exitDispatchTimestamp = Stopwatch.GetTimestamp();
					await SendExitNotificationAsync(session).ConfigureAwait(false);

					// Re-read the process after the awaits above: the local keeps the null-state analysis sound
					// and the kill path cannot dereference a process that a concurrent teardown cleared.
					Process? liveProcess = session.Process;

					if (liveProcess is not null && !await WaitForServerExitAsync(liveProcess).ConfigureAwait(false))
					{
						_logger.LogWarning(
							"Language server connection generation {Generation} in workspace '{Workspace}' did not exit after graceful shutdown; forcing process termination.",
							session.ConnectionGeneration,
							_workspaceRootsDisplayText
						);

						_standardErrorReader.LogRecentContext(session, "Forced process termination");

						liveProcess.Kill(true);
					}
				}
				else if (serverProcess is not null)
				{
					// A session disposed before its JSON-RPC instance was attached has no channel for the graceful
					// shutdown/exit sequence, so the live process is terminated directly; otherwise, it would keep
					// running until host exit (Windows job object) or indefinitely.
					_logger.LogInformation(
						"Terminating language server connection generation {Generation} in workspace '{Workspace}' because disposal reached the session before its JSON-RPC instance was attached.",
						session.ConnectionGeneration,
						_workspaceRootsDisplayText
					);

					_standardErrorReader.LogRecentContext(session, "Forced process termination");

					serverProcess.Kill(true);
				}
			}
		}
		catch (Exception exception)
		{
			_logger.LogWarning(
				exception,
				"Disposing language server connection generation {Generation} raised exceptions while stopping the server process.",
				session.ConnectionGeneration
			);
		}
		finally
		{
			Task outboundDrained = session.MessageHandler?.OutboundDrained ?? Task.CompletedTask;

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

			// Disposal asked the handler to complete its outbound pipe, so the drain ends once the queued messages reach the
			// stream that the release closes.
			await WaitForOutboundDrainAsync(session, outboundDrained, GetRemainingExitBudget(exitDispatchTimestamp)).ConfigureAwait(false);
			await ReleaseSessionResourcesAsync(session).ConfigureAwait(false);
		}

		await WaitForBackgroundLoopsAsync(rpcCompletionTask, standardErrorLoopTask).ConfigureAwait(false);
	}

	/// <summary>Releases the resources owned by a connection session and clears its connection state.</summary>
	/// <param name="session">The session whose resources should be released.</param>
	/// <remarks>
	/// The connection owns the streams and the process, so disposing it releases them.
	/// </remarks>
	private async Task ReleaseSessionResourcesAsync(ConnectionSession session)
	{
		try
		{
			await session.Connection.DisposeAsync().ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Disposing the connection of generation {Generation} failed.", session.ConnectionGeneration);
		}

		session.RpcCompletionTask = null;
		session.StandardErrorLoopTask = null;
		session.JsonRpc = null;
		session.MessageHandler = null;
		session.RpcTarget = null;
	}

	/// <summary>Returns what remains of the shutdown request timeout that the exit dispatch started.</summary>
	/// <param name="exitDispatchTimestamp">
	/// The timestamp of the exit dispatch, or <see langword="null"/> when no exit notification was dispatched.
	/// </param>
	/// <returns>The remaining budget, never negative; the whole timeout when no exit notification was dispatched.</returns>
	private TimeSpan GetRemainingExitBudget(long? exitDispatchTimestamp)
	{
		if (exitDispatchTimestamp is not long startTimestamp)
			return _shutdownRequestTimeout;

		TimeSpan remaining = _shutdownRequestTimeout - Stopwatch.GetElapsedTime(startTimestamp);
		return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
	}

	/// <summary>Waits for the messages written to the session's handler to reach the connection's write stream.</summary>
	/// <param name="session">The session being torn down.</param>
	/// <param name="outboundDrained">
	/// The handler's <see cref="TolerantHeaderDelimitedMessageHandler.OutboundDrained"/> task, read before the release clears the handler.
	/// </param>
	/// <param name="timeout">What remains of the exit budget.</param>
	/// <remarks>
	/// When the bound elapses, the release goes on and closes the stream, which drops the messages that the copy has not written yet.
	/// </remarks>
	private async Task WaitForOutboundDrainAsync(ConnectionSession session, Task outboundDrained, TimeSpan timeout)
	{
		try
		{
			await outboundDrained.WaitAsync(timeout).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			_backgroundTaskObserver.ObserveAbandoned(outboundDrained);

			_logger.LogWarning(
				"The outbound messages of language server connection generation {Generation} in workspace '{Workspace}' did not drain to its input stream within the {TimeoutMs} ms shutdown budget during teardown; releasing the connection.",
				session.ConnectionGeneration,
				_workspaceRootsDisplayText,
				(int)_shutdownRequestTimeout.TotalMilliseconds
			);

			_standardErrorReader.LogRecentContext(session, "Outbound drain timeout");
		}
		catch (Exception exception)
		{
			// The copy faults only when logging its own write failure threw, and the release that follows must still run.
			_logger.LogDebug(
				exception,
				"The outbound copy of language server connection generation {Generation} failed during teardown.",
				session.ConnectionGeneration
			);
		}
	}

	/// <summary>Tries to send a graceful shutdown request to the server.</summary>
	/// <param name="session">The session being shut down.</param>
	private async Task SendShutdownAsync(ConnectionSession session)
	{
		using var shutdownTimeout = new CancellationTokenSource(_shutdownRequestTimeout);

		Task<object?>? shutdownTask = null;

		try
		{
			shutdownTask = _protocolSender.SendRequestCoreAsync<object?>(
				session,
				method: LspMethodNames.Shutdown,
				parameters: new EmptyParams(),
				disposedAccess: DisposedAccessPolicy.Allow,
				cancellationToken: shutdownTimeout.Token
			);

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
			_logger.LogWarning(
				exception,
				"Sending the language server shutdown request during disposal failed for connection generation {Generation} in workspace '{Workspace}'; continuing teardown.",
				session.ConnectionGeneration,
				_workspaceRootsDisplayText
			);

			_standardErrorReader.LogRecentContext(session, "Shutdown failure");
		}
	}

	/// <summary>Logs that the server did not acknowledge the shutdown request and observes the late task so its failure is not unobserved.</summary>
	/// <param name="session">The session that timed out.</param>
	/// <param name="shutdownTask">The still-running shutdown request, when it was issued.</param>
	private void HandleShutdownTimeout(ConnectionSession session, Task<object?>? shutdownTask)
	{
		if (shutdownTask is not null)
			_backgroundTaskObserver.ObserveAbandoned(shutdownTask);

		_logger.LogWarning(
			"Language server connection generation {Generation} in workspace '{Workspace}' did not acknowledge shutdown within {TimeoutMs} ms; continuing teardown.",
			session.ConnectionGeneration,
			_workspaceRootsDisplayText,
			(int)_shutdownRequestTimeout.TotalMilliseconds
		);

		_standardErrorReader.LogRecentContext(session, "Shutdown timeout");
	}

	/// <summary>Tries to queue the exit notification to the server within the shutdown request timeout.</summary>
	/// <param name="session">The session being shut down.</param>
	/// <remarks>
	/// The dispatch is bounded so no teardown call site can wait forever on a server that stopped draining its standard input;
	/// when the bound elapses, teardown continues and relies on forced process termination.
	/// </remarks>
	private async Task SendExitNotificationAsync(ConnectionSession session)
	{
		Task exitNotificationTask = _protocolSender.SendNotificationCoreAsync(
			session,
			method: LspMethodNames.Exit,
			disposedAccess: DisposedAccessPolicy.Allow,
			cancellationToken: CancellationToken.None
		);

		try
		{
			await exitNotificationTask.WaitAsync(_shutdownRequestTimeout).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			_backgroundTaskObserver.ObserveAbandoned(exitNotificationTask);

			_logger.LogWarning(
				"The language server exit notification did not complete within {TimeoutMs} ms during teardown for connection generation {Generation} in workspace '{Workspace}'; continuing teardown and relying on process termination.",
				(int)_shutdownRequestTimeout.TotalMilliseconds,
				session.ConnectionGeneration,
				_workspaceRootsDisplayText
			);

			_standardErrorReader.LogRecentContext(session, "Exit notification timeout");
		}
		catch (Exception exception)
		{
			_logger.LogDebug(
				exception,
				"Sending the language server exit notification during disposal failed for connection generation {Generation}; continuing teardown.",
				session.ConnectionGeneration
			);
		}
	}

	/// <summary>Waits for the server process to exit after the graceful shutdown sequence, within a short grace period.</summary>
	/// <param name="serverProcess">The server process that should exit on its own.</param>
	/// <returns>
	/// <see langword="true"/> when the process exited within the grace period; otherwise, <see langword="false"/> so the caller forces termination.
	/// </returns>
	/// <remarks>
	/// The exit notification dispatch completing only proves that the message reached the handler's outbound pipe, not that the server
	/// read it or finished its own teardown; without the wait an orderly exit races forced termination and its final standard error output is lost.
	/// The grace period is deliberately short because a server that ignores the shutdown sequence must still be terminated promptly.
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
			_backgroundTaskObserver.ObserveAbandoned(waitForExitTask);
			return false;
		}
	}

	/// <summary>Waits for the background read and standard-error loops to finish within the dispose timeout.</summary>
	/// <param name="rpcCompletionTask">The main JSON-RPC completion task.</param>
	/// <param name="standardErrorLoopTask">The standard-error read loop task.</param>
	internal async Task WaitForBackgroundLoopsAsync(Task? rpcCompletionTask, Task? standardErrorLoopTask)
	{
		var combined = Task.WhenAll(rpcCompletionTask ?? Task.CompletedTask, standardErrorLoopTask ?? Task.CompletedTask);

		try
		{
			await combined.WaitAsync(_disposeWaitTimeout).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			_backgroundTaskObserver.ObserveAbandoned(combined);

			_logger.LogWarning(
				"Language server background loops did not complete within {TimeoutMs} ms during disposal.",
				(int)_disposeWaitTimeout.TotalMilliseconds
			);
		}
		catch (Exception exception)
		{
			bool loggedSpecificLoop = false;
			loggedSpecificLoop |= _backgroundTaskObserver.TryLogCanceledLoop(rpcCompletionTask, "JSON-RPC completion");
			loggedSpecificLoop |= _backgroundTaskObserver.TryLogCanceledLoop(standardErrorLoopTask, "stderr read");
			loggedSpecificLoop |= _backgroundTaskObserver.TryLogFaultedLoop(rpcCompletionTask, "JSON-RPC completion");
			loggedSpecificLoop |= _backgroundTaskObserver.TryLogFaultedLoop(standardErrorLoopTask, "stderr read");

			if (!loggedSpecificLoop)
				_logger.LogWarning(exception, "Language server background loop failed during disposal.");
		}
	}
}
