namespace Nickelony.LanguageServer.Client;

/// <summary>Builds the collaborators of one client and wires them to each other.</summary>
/// <remarks>
/// <para>
/// The client creates one composition from the values its constructor validated, and reaches every collaborator through it. A collaborator test
/// builds the same composition around its own owner callbacks, so the wiring exists once.
/// </para>
/// <para>
/// The collaborators call back into the owner through the three delegates, which read the owner when they run, not while this constructor does.
/// </para>
/// </remarks>
internal sealed class ClientComposition
{
	/// <param name="owner">The client that raises the server-message events, passed to every handler as its sender.</param>
	/// <param name="hooks">The host delegates that receive the server messages the client does not surface by itself.</param>
	/// <param name="options">The client options, already validated.</param>
	/// <param name="workspaceRoots">The normalized workspace roots that the client hosts.</param>
	/// <param name="transport">The transport that opens each connection.</param>
	/// <param name="testHooks">The hooks that expose nondeterministic points to a test; the shared no-hook instance in production.</param>
	/// <param name="serverMessageGate">
	/// Replaces the connection-generation gate of the server-message router and of every RPC target, so a test can queue payloads
	/// without an active session, or <see langword="null"/> for the gate of the connection state.
	/// </param>
	/// <param name="tryMarkConnectionUnhealthy">Marks one observed connection generation unhealthy and reports whether it was still current.</param>
	/// <param name="raiseConnectionUnavailable">Raises the connection-unavailable event for a lost ready connection.</param>
	/// <param name="throwIfDisposed">Throws when the owner is disposed and the call does not allow disposed access.</param>
	/// <param name="logger">The logger instance.</param>
	internal ClientComposition(
		object owner,
		LanguageServerClientHooks hooks,
		LanguageServerClientOptions options,
		WorkspaceRoots workspaceRoots,
		ILanguageServerTransport transport,
		LanguageServerClientTestHooks testHooks,
		Func<long, bool>? serverMessageGate,
		Func<long, bool> tryMarkConnectionUnhealthy,
		Action<long> raiseConnectionUnavailable,
		Action<bool> throwIfDisposed,
		ILogger logger
	)
	{
		State = new ConnectionState(options.RequireTextDocumentSynchronization);
		Lifetime = new ClientLifetime(options.DisposeWaitTimeout, workspaceRoots.DisplayText, logger);

		Context = new ConnectionCollaboratorContext(
			State,
			logger,
			CanAcceptServerMessagesForGeneration: serverMessageGate ?? State.CanAcceptServerMessagesForGeneration,
			tryMarkConnectionUnhealthy,
			raiseConnectionUnavailable,
			WorkspaceFolders: workspaceRoots.Folders,
			WorkspaceRootsDisplayText: workspaceRoots.DisplayText,
			Lifetime,
			hooks
		);

		Observer = new BackgroundTaskObserver(
			Lifetime,
			markConnectionUnhealthy: () => Context.TryMarkConnectionUnhealthy(State.ConnectionGeneration),
			logger
		);

		SettingsCache = new SettingsSnapshotCache(options.SettingsProvider, logger);

		Router = new ServerMessageRouter(
			eventSender: owner,
			canAcceptServerMessagesForGeneration: Context.CanAcceptServerMessagesForGeneration,
			testHooks,
			backgroundTaskObserver: Observer,
			logger,
			lifetimeToken: Lifetime.Token
		);

		Sender = new ProtocolSender(Context, throwIfDisposed, SettingsCache, backgroundTaskObserver: Observer);

		StandardErrorReader = new StandardErrorReader(Context);

		SessionTeardown = new SessionTeardown(
			Context,
			protocolSender: Sender,
			StandardErrorReader,
			backgroundTaskObserver: Observer,
			shutdownRequestTimeout: options.ShutdownRequestTimeout
		);

		FailedSessionDispatcher = new FailedSessionDispatcher(disposeSession: SessionTeardown.DisposeSessionAsync, logger);

		Supervisor = new ConnectionSessionSupervisor(Context, SessionTeardown, StandardErrorReader, FailedSessionDispatcher);

		SessionFactory = new ConnectionSessionFactory(
			Context,
			serverMessageRouter: Router,
			settingsSnapshotCache: SettingsCache,
			StandardErrorReader,
			handleProcessExited: Supervisor.HandleProcessExited,
			handleJsonRpcDisconnected: Supervisor.HandleJsonRpcDisconnected
		);

		Handshake = new HandshakeRunner(
			Context,
			protocolSender: Sender,
			SettingsCache,

			initializeParamsBuilder: new InitializeParamsBuilder(
				roots: workspaceRoots,
				initializationOptionsProvider: options.InitializationOptionsProvider,
				clientCapabilitiesProvider: options.ClientCapabilitiesProvider,
				hooks,
				logger
			),

			sessionStartNotificationBuilder: new SessionStartNotificationBuilder(workspaceRoots, options.SessionStartNotificationsProvider, logger)
		);

		StartLock = new SemaphoreSlim(1, 1);

		Startup = new StartupCoordinator(
			Context,
			options,
			transport,
			StartLock,
			throwIfDisposed,
			testHooks,
			serverMessageRouter: Router,
			SessionFactory,
			sessionSupervisor: Supervisor,
			SessionTeardown,
			StandardErrorReader,
			Handshake
		);

		Teardown = new ClientTeardown(
			Lifetime,
			sessionSupervisor: Supervisor,
			FailedSessionDispatcher,
			serverMessageRouter: Router,
			backgroundTaskObserver: Observer,
			StartLock,
			testHooks,
			logger
		);
	}

	/// <summary>The published connection state behind the public capability surface.</summary>
	internal ConnectionState State { get; }

	/// <summary>The disposed state, cancellation token and disposal completion of the client.</summary>
	internal ClientLifetime Lifetime { get; }

	/// <summary>The state and owner callbacks that every connection collaborator shares.</summary>
	internal ConnectionCollaboratorContext Context { get; }

	/// <summary>The observer of the background tasks of the client.</summary>
	internal BackgroundTaskObserver Observer { get; }

	/// <summary>The cache of the host settings snapshot.</summary>
	internal SettingsSnapshotCache SettingsCache { get; }

	/// <summary>The router that dispatches the server messages to the public events.</summary>
	internal ServerMessageRouter Router { get; }

	/// <summary>The sender of requests and notifications.</summary>
	internal ProtocolSender Sender { get; }

	/// <summary>The reader of the standard error of the server process.</summary>
	internal StandardErrorReader StandardErrorReader { get; }

	/// <summary>The ordered shutdown of one connection session.</summary>
	internal SessionTeardown SessionTeardown { get; }

	/// <summary>The queue that disposes each failed connection session in the background.</summary>
	internal FailedSessionDispatcher FailedSessionDispatcher { get; }

	/// <summary>The detacher of a failed connection session, which hands it to the failed-session queue.</summary>
	internal ConnectionSessionSupervisor Supervisor { get; }

	/// <summary>The assembler of the connection sessions.</summary>
	internal ConnectionSessionFactory SessionFactory { get; }

	/// <summary>The handshake that follows the activation of a session.</summary>
	internal HandshakeRunner Handshake { get; }

	/// <summary>The gate that serializes the startup attempts and gates disposal cleanup.</summary>
	internal SemaphoreSlim StartLock { get; }

	/// <summary>The sequence of one startup attempt.</summary>
	internal StartupCoordinator Startup { get; }

	/// <summary>The ordered shutdown of the collaborators of the client.</summary>
	internal ClientTeardown Teardown { get; }
}
