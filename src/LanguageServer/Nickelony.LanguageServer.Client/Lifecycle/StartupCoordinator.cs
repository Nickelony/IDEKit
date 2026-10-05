namespace Nickelony.LanguageServer.Client;

/// <summary>Sequences each startup attempt of one client from the transport's connect call to a ready session.</summary>
/// <remarks>
/// <para>
/// The start gate serializes the attempts, and an attempt that finds the client ready returns without touching the transport.
/// An attempt that is canceled, interrupted by disposal, times out or fails disposes the session it created and leaves the client not ready.
/// </para>
/// <para>
/// A caller that waits at the gate has no attempt, so cancellation or disposal ends its wait and leaves the session and the recorded failure to the attempt that holds the gate.
/// </para>
/// <para>
/// The coordinator keeps the failure of the most recent attempt for the diagnostic surface, and clears it when the next attempt begins.
/// </para>
/// </remarks>
internal sealed class StartupCoordinator
{
	private readonly ConnectionState _state;
	private readonly ILogger _logger;
	private readonly ClientLifetime _lifetime;
	private readonly string _workspaceRootsDisplayText;
	private readonly LanguageServerClientOptions _options;
	private readonly ILanguageServerTransport _transport;
	private readonly SemaphoreSlim _startLock;
	private readonly Action<bool> _throwIfDisposed;
	private readonly LanguageServerClientTestHooks _testHooks;
	private readonly ServerMessageRouter _serverMessageRouter;
	private readonly ConnectionSessionFactory _sessionFactory;
	private readonly ConnectionSessionSupervisor _sessionSupervisor;
	private readonly SessionTeardown _sessionTeardown;
	private readonly StandardErrorReader _standardErrorReader;
	private readonly HandshakeRunner _handshake;

	// Written by startup attempts and read by the diagnostic surface.
	private Exception? _lastStartupException;

	/// <param name="context">The shared connection-collaborator context (state, logger, lifetime and workspace display text).</param>
	/// <param name="options">The client options, which supply the initialize timeout.</param>
	/// <param name="transport">The transport that opens the connection of each attempt.</param>
	/// <param name="startLock">The gate that serializes the startup attempts.</param>
	/// <param name="throwIfDisposed">Throws when the owning client is disposed and the call does not allow disposed access.</param>
	/// <param name="testHooks">The observers of the points after activation and before the initialize request.</param>
	/// <param name="serverMessageRouter">The router whose loops the first attempt starts.</param>
	/// <param name="sessionFactory">Creates, configures and starts the session.</param>
	/// <param name="sessionSupervisor">Handles the exit of a process that exited early, and disposes the active session.</param>
	/// <param name="sessionTeardown">Disposes a session that never became active.</param>
	/// <param name="standardErrorReader">Reports the recent standard error of a session that failed to start.</param>
	/// <param name="handshake">Completes the handshake of the activated session.</param>
	internal StartupCoordinator(
		ConnectionCollaboratorContext context,
		LanguageServerClientOptions options,
		ILanguageServerTransport transport,
		SemaphoreSlim startLock,
		Action<bool> throwIfDisposed,
		LanguageServerClientTestHooks testHooks,
		ServerMessageRouter serverMessageRouter,
		ConnectionSessionFactory sessionFactory,
		ConnectionSessionSupervisor sessionSupervisor,
		SessionTeardown sessionTeardown,
		StandardErrorReader standardErrorReader,
		HandshakeRunner handshake
	)
	{
		_state = context.State;
		_logger = context.Logger;
		_lifetime = context.Lifetime;
		_workspaceRootsDisplayText = context.WorkspaceRootsDisplayText;
		_options = options;
		_transport = transport;
		_startLock = startLock;
		_throwIfDisposed = throwIfDisposed;
		_testHooks = testHooks;
		_serverMessageRouter = serverMessageRouter;
		_sessionFactory = sessionFactory;
		_sessionSupervisor = sessionSupervisor;
		_sessionTeardown = sessionTeardown;
		_standardErrorReader = standardErrorReader;
		_handshake = handshake;
	}

	/// <summary>The failure of the most recent startup attempt, or <see langword="null"/> when that attempt did not fail.</summary>
	internal Exception? LastStartupException => Volatile.Read(ref _lastStartupException);

	/// <summary>Starts the connection session when the client is not ready, and reports whether the client is ready afterwards.</summary>
	/// <param name="cancellationToken">A token that can cancel the startup attempt.</param>
	/// <returns><see langword="true"/> when the client is ready; otherwise, <see langword="false"/>.</returns>
	internal async Task<bool> EnsureStartedAsync(CancellationToken cancellationToken)
	{
		_throwIfDisposed(false);

		if (_state.IsReady)
			return true;

		// Start the server-message dispatcher with the first startup attempt instead of from the constructor, so merely
		// constructing a client does not leave a background loop running until disposal.
		_serverMessageRouter.EnsureServerMessageDispatcherRunning();

		using var disposeAwareStartupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);

		CancellationToken effectiveCancellationToken = disposeAwareStartupCts.Token;

		// A caller that waits here owns no attempt, so an interrupted wait leaves the active session and the recorded failure to
		// the attempt that holds the gate. Disposal returns false because the teardown owns every session by then.
		try
		{
			await _startLock.WaitAsync(effectiveCancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (_lifetime.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
		{
			return false;
		}
		catch (ObjectDisposedException) when (_lifetime.Token.IsCancellationRequested)
		{
			return false;
		}

		// Everything from here on runs with the gate held, so the finally block releases it exactly once.
		ConnectionSession? startedSession = null;
		bool sessionActivated = false;
		CancellationTokenSource? initializeTimeout = null;

		try
		{
			_throwIfDisposed(false);

			if (_state.IsReady)
				return true;

			// The diagnostic surface reports only the most recent attempt; clear it under the start lock so a
			// concurrent pair cannot leave an earlier attempt's failure in place after a later attempt succeeds.
			Volatile.Write(ref _lastStartupException, null);

			ConnectionSession? previousSession = _state.DetachActiveSession();

			if (previousSession is not null)
			{
				_logger.LogInformation(
					"Restarting the language server connection by replacing generation {Generation}.",
					previousSession.ConnectionGeneration
				);

				await _sessionTeardown.DisposeSessionAsync(previousSession).ConfigureAwait(false);
			}

			ILanguageServerConnection connection =
				await _transport.ConnectAsync(new LanguageServerConnectRequest { Logger = _logger }, effectiveCancellationToken).ConfigureAwait(false)
				?? throw new InvalidOperationException("The language-server transport returned no connection for the startup attempt.");

			ConnectionSession session;

			try
			{
				session = _sessionFactory.Create(connection);
			}
			catch
			{
				// The session owns the connection; if it cannot be created, the opened connection has no owner and must
				// be disposed here or it leaks the process and the streams ConnectAsync opened.
				await connection.DisposeAsync().ConfigureAwait(false);
				throw;
			}

			startedSession = session;

			// Disposal can win the race after the entry checks and the startup gate wait and before the session is
			// activated. Without this re-check the session would become active after DisposeAsync returned and its
			// process would only be torn down once the canceled lifetime token aborted the handshake.
			if (_lifetime.IsDisposed || _lifetime.Token.IsCancellationRequested)
			{
				await _sessionTeardown.DisposeSessionAsync(session).ConfigureAwait(false);
				return false;
			}

			_sessionFactory.Configure(session);
			_state.SetActiveSession(session);
			sessionActivated = true;

			// The transport started the process before this session subscribed to its exit event, so an exit during
			// startup can be missed. Surface it now that the session is active instead of leaving a dead server ready.
			if (session.HasProcessExited)
			{
				_sessionSupervisor.HandleProcessExited(session);
				await _sessionSupervisor.DisposeActiveSessionAsync().ConfigureAwait(false);
				return false;
			}

			_sessionFactory.Start(session);

			_logger.LogInformation(
				"Activated language server connection generation {Generation} for workspace '{Workspace}'; completing initialization handshake.",
				session.ConnectionGeneration,
				_workspaceRootsDisplayText
			);

			if (_testHooks.SessionActivated is not null)
				await _testHooks.SessionActivated(effectiveCancellationToken).ConfigureAwait(false);

			_serverMessageRouter.EnsureServerMessageAndDiagnosticsLoopsRunning();

			var initializeTimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(effectiveCancellationToken);
			initializeTimeout = initializeTimeoutSource;
			initializeTimeoutSource.CancelAfter(_options.InitializeRequestTimeout);

			if (_testHooks.BeforeInitializeRequest is not null)
				await _testHooks.BeforeInitializeRequest(initializeTimeoutSource.Token).ConfigureAwait(false);

			if (!await _handshake.RunAsync(session, initializeTimeoutSource.Token, effectiveCancellationToken).ConfigureAwait(false))
			{
				await _sessionSupervisor.DisposeActiveSessionAsync().ConfigureAwait(false);
				return false;
			}

			return true;
		}
		catch (OperationCanceledException exception)
			when (sessionActivated && initializeTimeout is { IsCancellationRequested: true } && !effectiveCancellationToken.IsCancellationRequested)
		{
			Volatile.Write(ref _lastStartupException, exception);

			_logger.LogWarning(
				"Language server connection generation {Generation} did not complete initialization within {TimeoutMs} ms for workspace '{Workspace}'; tearing down the session and leaving the client not ready.",
				startedSession?.ConnectionGeneration ?? 0,
				(int)_options.InitializeRequestTimeout.TotalMilliseconds,
				_workspaceRootsDisplayText
			);

			_standardErrorReader.LogRecentContext(startedSession, "Initialization timeout");

			await _sessionSupervisor.DisposeActiveSessionAsync().ConfigureAwait(false);
			return false;
		}
		catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
		{
			Volatile.Write(ref _lastStartupException, exception);

			if (!sessionActivated && startedSession is not null)
				await _sessionTeardown.DisposeSessionAsync(startedSession).ConfigureAwait(false);

			await _sessionSupervisor.DisposeActiveSessionAsync().ConfigureAwait(false);
			throw;
		}
		catch (OperationCanceledException exception) when (_lifetime.IsDisposed)
		{
			Volatile.Write(ref _lastStartupException, exception);

			if (!sessionActivated && startedSession is not null)
				await _sessionTeardown.DisposeSessionAsync(startedSession).ConfigureAwait(false);

			await _sessionSupervisor.DisposeActiveSessionAsync().ConfigureAwait(false);
			return false;
		}
		catch (Exception exception)
		{
			Volatile.Write(ref _lastStartupException, exception);

			_logger.LogWarning(
				exception,
				"Failed to start the language server (transport={Transport}, workspace='{Workspace}', generation={Generation}, stage='{Stage}').",
				_transport,
				_workspaceRootsDisplayText,
				startedSession?.ConnectionGeneration ?? 0,
				sessionActivated ? "initialization" : "startup"
			);

			_standardErrorReader.LogRecentContext(startedSession, sessionActivated ? "Initialization failure" : "Startup failure");

			if (!sessionActivated && startedSession is not null)
				await _sessionTeardown.DisposeSessionAsync(startedSession).ConfigureAwait(false);

			await _sessionSupervisor.DisposeActiveSessionAsync().ConfigureAwait(false);
			return false;
		}
		finally
		{
			initializeTimeout?.Dispose();
			_startLock.Release();
		}
	}
}
