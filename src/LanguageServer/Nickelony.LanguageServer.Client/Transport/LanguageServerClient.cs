using System.Collections.ObjectModel;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Hosts a language-server process, performs the LSP handshake, and transports JSON-RPC requests and notifications.
/// </summary>
/// <remarks>
/// The thread-safety, event, restart, and disposal contracts are defined by <see cref="ILanguageServerClient"/>.
/// </remarks>
public sealed partial class LanguageServerClient : ILanguageServerClient
{
	private readonly ILogger _logger;

	// Host configuration and startup options.
	private readonly IReadOnlyList<string> _workspaceRootDirectoryPaths;
	private readonly ReadOnlyCollection<WorkspaceFolder> _workspaceFolders;
	private readonly string _workspaceRootsDisplayText;
	private readonly string _serverExecutablePath;
	private readonly IReadOnlyList<string> _serverArguments;
	private readonly string? _serverWorkingDirectory;
	private readonly IReadOnlyDictionary<string, string> _environmentVariables;
	private readonly Func<IReadOnlyList<string>, object?> _clientCapabilitiesProvider;
	private readonly Func<IReadOnlyList<string>, object?> _initializationOptionsProvider;
	private readonly TimeSpan _initializeTimeout;
	private readonly TimeSpan _disposeWaitTimeout;

	// The transport that opens every connection to the language server; stdio by default.
	private readonly ILanguageServerTransport _transport;

	// Optional factory that replaces the platform-default child-process lifetime guard per startup attempt.
	private readonly Func<IChildProcessLifetimeGuard>? _childProcessLifetimeGuardFactory;

	// Test-only hooks; a production client installs the shared no-hook instance.
	private readonly ClientTestHooks _testHooks;

	// Transport lifetime coordination.
	private readonly SemaphoreSlim _startLock = new(1, 1);
	private readonly CancellationTokenSource _lifetimeCts = new();

	// Cached so lifetime checks never read from a disposed source after teardown completes.
	private readonly CancellationToken _lifetimeToken;

	// Transport collaborators.
	private readonly DiagnosticsRouter _diagnosticsRouter;
	private readonly ProtocolForwarder _protocolForwarder;
	private readonly TransportHost _transportHost;

	// Published transport state read by the public capability surface.
	private readonly CapabilityStore _capabilityStore;

	private volatile bool _isDisposed;
	private int _disposeStarted;

	// Written by startup attempts and read by the diagnostic surface.
	private Exception? _lastStartupException;

	// Internal test-seam accessors: each exposes one collaborator or the startup gate so the test suite can drive a
	// nondeterministic point (a concurrent startup, teardown, or background-loop termination) directly. Each is used
	// by the test suite and documented; no new accessor is added without the same justification.

	/// <summary>
	/// Gets the published transport capability store backing the public capability surface.
	/// </summary>
	internal CapabilityStore CapabilityStore => _capabilityStore;

	/// <summary>
	/// Gets the diagnostics router backing the public diagnostics and semantic-token refresh events.
	/// </summary>
	internal DiagnosticsRouter DiagnosticsRouter => _diagnosticsRouter;

	/// <summary>
	/// Gets the protocol forwarder backing the public send surface and the cached settings snapshot.
	/// </summary>
	internal ProtocolForwarder ProtocolForwarder => _protocolForwarder;

	/// <summary>
	/// Gets the transport host owning session lifecycle, failure detection, and teardown.
	/// </summary>
	internal TransportHost TransportHost => _transportHost;

	/// <summary>
	/// Gets the startup gate serializing concurrent startup attempts and gating disposal cleanup.
	/// </summary>
	internal SemaphoreSlim StartLock => _startLock;

	/// <inheritdoc/>
	public bool IsReady => _capabilityStore.IsReady;

	/// <inheritdoc/>
	public long TransportGeneration => _capabilityStore.TransportGeneration;

	/// <inheritdoc/>
	public TextDocumentSyncKind TextDocumentSyncKind => _capabilityStore.TextDocumentSyncKind;

	/// <inheritdoc/>
	public IReadOnlyList<string> SemanticTokenTypes => _capabilityStore.SemanticTokenTypes;

	/// <inheritdoc/>
	public IReadOnlyList<string> SemanticTokenModifiers => _capabilityStore.SemanticTokenModifiers;

	/// <inheritdoc/>
	public bool SupportsCompletionResolve => IsSupported(_capabilityStore.SupportsCompletionResolve);

	/// <inheritdoc/>
	public bool SupportsDocumentSymbols => IsSupported(_capabilityStore.SupportsDocumentSymbols);

	/// <inheritdoc/>
	public bool SupportsCodeActions => IsSupported(_capabilityStore.SupportsCodeActions);

	/// <inheritdoc/>
	public bool SupportsCodeActionResolve => IsSupported(_capabilityStore.SupportsCodeActionResolve);

	/// <inheritdoc/>
	public bool SupportsReferences => IsSupported(_capabilityStore.SupportsReferences);

	/// <inheritdoc/>
	public bool SupportsRename => IsSupported(_capabilityStore.SupportsRename);

	/// <inheritdoc/>
	public bool SupportsFormatting => IsSupported(_capabilityStore.SupportsFormatting);

	/// <inheritdoc/>
	public bool SupportsHover => IsSupported(_capabilityStore.SupportsHover);

	/// <inheritdoc/>
	public bool SupportsDefinition => IsSupported(_capabilityStore.SupportsDefinition);

	/// <inheritdoc/>
	public bool SupportsSignatureHelp => IsSupported(_capabilityStore.SupportsSignatureHelp);

	/// <inheritdoc/>
	public bool SupportsSemanticTokensFull => IsSupported(_capabilityStore.SupportsSemanticTokensFull);

	/// <inheritdoc/>
	public bool SupportsPullDiagnostics => IsSupported(_capabilityStore.SupportsPullDiagnostics);

	// Every capability read shares one unknown-vs-false policy: a capability the server has not advertised, or that no
	// active generation has published yet, reads as false until it is negotiated.
	private static bool IsSupported(bool? capability) => capability == true;

	/// <summary>
	/// Gets the exception observed by the most recent startup attempt, or <see langword="null"/> when that attempt
	/// succeeded or failed without an exception.
	/// </summary>
	/// <remarks>
	/// <see cref="StartAsync"/> reports every failure by returning <see langword="false"/>; this property exposes the
	/// cause for diagnostics instead of requiring a log scrape. It is cleared when a new startup attempt begins, so a
	/// later successful attempt does not keep reporting an older failure.
	/// </remarks>
	public Exception? LastStartupException => Volatile.Read(ref _lastStartupException);

	/// <inheritdoc/>
	public event EventHandler<TransportUnavailableEventArgs>? TransportUnavailable;

	/// <inheritdoc/>
	public event EventHandler<DiagnosticsPublishedEventArgs>? DiagnosticsPublished
	{
		add => _diagnosticsRouter.AddDiagnosticsSubscriber(value);
		remove => _diagnosticsRouter.RemoveDiagnosticsSubscriber(value);
	}

	/// <inheritdoc/>
	public event EventHandler? SemanticTokensRefreshRequested
	{
		add => _diagnosticsRouter.AddSemanticTokensRefreshSubscriber(value);
		remove => _diagnosticsRouter.RemoveSemanticTokensRefreshSubscriber(value);
	}

	/// <inheritdoc/>
	public event EventHandler? DiagnosticRefreshRequested
	{
		add => _diagnosticsRouter.AddDiagnosticRefreshSubscriber(value);
		remove => _diagnosticsRouter.RemoveDiagnosticRefreshSubscriber(value);
	}

	/// <summary>
	/// Marks one specific transport generation unhealthy only when it is still the active generation.
	/// </summary>
	/// <param name="transportGeneration">The observed transport generation to invalidate, as reported by <see cref="TransportGeneration"/>.</param>
	/// <returns>
	/// <see langword="true"/> when the observed generation was still active and was marked unhealthy; otherwise, <see langword="false"/>.
	/// Transport generations start at <c>1</c>, so the pre-start generation <c>0</c> is never marked unhealthy.
	/// </returns>
	public bool TryMarkTransportUnhealthy(long transportGeneration)
	{
		// Generation 0 means that no transport exists yet, so there is nothing to invalidate.
		if (_isDisposed || transportGeneration <= 0)
			return false;

		if (!_capabilityStore.TryMarkTransportUnhealthy(transportGeneration, out bool wasReady))
			return false;

		if (wasReady)
		{
			_logger.LogWarning("Marked language server transport generation {Generation} unhealthy; the host will restart it before the next public request.", transportGeneration);
			RaiseTransportUnavailable(transportGeneration);
		}
		else
		{
			_logger.LogDebug(
				"Marked language server transport generation {Generation} unhealthy before it published readiness; the generation stays invalidated so a late startup completion cannot republish it as ready.",
				transportGeneration);
		}

		return true;
	}

	/// <inheritdoc/>
	public async Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(method);

		await _protocolForwarder.SendNotificationAsync(method, parameters, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public async Task<TResult> SendRequestAsync<TResult>(string method, object parameters, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(method);
		ArgumentNullException.ThrowIfNull(parameters);

		return await _protocolForwarder.SendRequestAsync<TResult>(method, parameters, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Notifies subscribers that one active ready transport was lost.
	/// </summary>
	/// <param name="transportGeneration">The generation that was lost.</param>
	private void RaiseTransportUnavailable(long transportGeneration)
	{
		if (_isDisposed)
			return;

		EventHandler<TransportUnavailableEventArgs>? handlers = TransportUnavailable;

		if (handlers is null)
			return;

		var eventArgs = new TransportUnavailableEventArgs(transportGeneration);

		foreach (EventHandler<TransportUnavailableEventArgs> handler in handlers.GetInvocationList())
		{
			if (_isDisposed)
				return;

			try
			{
				handler(this, eventArgs);
			}
			catch (Exception exception)
			{
				_logger.LogWarning(
					exception,
					"Language server transport-unavailable subscriber threw; later subscribers will still be notified.");
			}
		}
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerClient"/> class.
	/// </summary>
	/// <param name="workspaceRootDirectoryPaths">
	/// The workspace root directories to host, or an empty list for a folderless session. The first entry is the
	/// primary root published as the initialize <c>rootUri</c>; every entry is advertised as a workspace folder in
	/// the caller's order. Entries may be nested; duplicates are rejected by comparing normalized paths with the
	/// configured local-path identity (case-insensitive on Windows and macOS, ordinal elsewhere).
	/// </param>
	/// <param name="serverExecutablePath">The language-server executable path.</param>
	/// <param name="options">Provides the client settings, capabilities, and initialization payload factories.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="workspaceRootDirectoryPaths"/> contains a <see langword="null"/>, empty, whitespace-only, or
	/// duplicate entry, or <paramref name="serverExecutablePath"/> is empty or whitespace-only.
	/// </exception>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="workspaceRootDirectoryPaths"/>, <paramref name="serverExecutablePath"/>, or
	/// <paramref name="options"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="NotSupportedException">
	/// A workspace root directory path or <paramref name="serverExecutablePath"/> uses a path form the host cannot
	/// normalize.
	/// </exception>
	/// <exception cref="PathTooLongException">
	/// A workspace root directory path or <paramref name="serverExecutablePath"/> exceeds the host path-length limit.
	/// </exception>
	public LanguageServerClient(IReadOnlyList<string> workspaceRootDirectoryPaths, string serverExecutablePath, LanguageServerClientOptions options, ILogger? logger = null)
		: this(workspaceRootDirectoryPaths, serverExecutablePath, options, logger, testHooks: null)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerClient"/> class.
	/// </summary>
	/// <param name="workspaceRootDirectoryPaths">
	/// The workspace root directories to host, or an empty list for a folderless session. The first entry is the
	/// primary root published as the initialize <c>rootUri</c>; every entry is advertised as a workspace folder in
	/// the caller's order. Entries may be nested; duplicates are rejected by comparing normalized paths with the
	/// configured local-path identity (case-insensitive on Windows and macOS, ordinal elsewhere).
	/// </param>
	/// <param name="serverExecutablePath">The language-server executable path.</param>
	/// <param name="options">Provides the client settings, capabilities, and initialization payload factories.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <param name="testHooks">The test-only hooks the client exposes to its test suite, or <see langword="null"/> to install none.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="workspaceRootDirectoryPaths"/> contains a <see langword="null"/>, empty, whitespace-only, or
	/// duplicate entry, or <paramref name="serverExecutablePath"/> is empty or whitespace-only.
	/// </exception>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="workspaceRootDirectoryPaths"/>, <paramref name="serverExecutablePath"/>, or
	/// <paramref name="options"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="NotSupportedException">
	/// A workspace root directory path or <paramref name="serverExecutablePath"/> uses a path form the host cannot
	/// normalize.
	/// </exception>
	/// <exception cref="PathTooLongException">
	/// A workspace root directory path or <paramref name="serverExecutablePath"/> exceeds the host path-length limit.
	/// </exception>
	internal LanguageServerClient(IReadOnlyList<string> workspaceRootDirectoryPaths, string serverExecutablePath, LanguageServerClientOptions options,
		ILogger? logger,
		ClientTestHooks? testHooks = null)
	{
		ArgumentNullException.ThrowIfNull(workspaceRootDirectoryPaths);
		ArgumentException.ThrowIfNullOrWhiteSpace(serverExecutablePath);
		ArgumentNullException.ThrowIfNull(options);

		string[] normalizedWorkspaceRootDirectoryPaths = LanguageServerPaths.NormalizeWorkspaceRoots(workspaceRootDirectoryPaths);
		WorkspaceFolder[] workspaceFolders = new WorkspaceFolder[normalizedWorkspaceRootDirectoryPaths.Length];

		for (int i = 0; i < normalizedWorkspaceRootDirectoryPaths.Length; i++)
		{
			workspaceFolders[i] = new WorkspaceFolder(
				LanguageServerPaths.CreateFileUri(normalizedWorkspaceRootDirectoryPaths[i]),
				GetWorkspaceFolderName(normalizedWorkspaceRootDirectoryPaths[i]));
		}

		_logger = logger ?? NullLogger.Instance;

		_workspaceRootDirectoryPaths = Array.AsReadOnly(normalizedWorkspaceRootDirectoryPaths);
		_workspaceFolders = Array.AsReadOnly(workspaceFolders);
		_workspaceRootsDisplayText = string.Join(", ", normalizedWorkspaceRootDirectoryPaths);
		_serverExecutablePath = serverExecutablePath;
		_serverArguments = options.ServerArguments;
		_serverWorkingDirectory = options.ServerWorkingDirectory;
		_environmentVariables = options.EnvironmentVariables;
		_clientCapabilitiesProvider = options.ClientCapabilitiesProvider;
		_initializationOptionsProvider = options.InitializationOptionsProvider;
		_initializeTimeout = options.InitializeTimeout;
		_disposeWaitTimeout = options.DisposeWaitTimeout;
		_transport = options.Transport;
		_childProcessLifetimeGuardFactory = options.ChildProcessLifetimeGuardFactory;
		_testHooks = testHooks ?? ClientTestHooks.None;

		_capabilityStore = new CapabilityStore(options.RequireTextDocumentSynchronization);
		_lifetimeToken = _lifetimeCts.Token;

		_diagnosticsRouter = new DiagnosticsRouter(
			_logger,
			this,
			_testHooks.CanAcceptServerCallbacksForGeneration ?? _capabilityStore.CanAcceptServerCallbacksForGeneration,
			() => _isDisposed,
			() => TryMarkTransportUnhealthy(_capabilityStore.TransportGeneration),
			_testHooks,
			_lifetimeToken);

		_protocolForwarder = new ProtocolForwarder(
			_capabilityStore,
			_logger,
			_workspaceRootsDisplayText,
			() => _isDisposed,
			ThrowIfDisposed,
			TryMarkTransportUnhealthy,
			options.SettingsProvider);

		_transportHost = new TransportHost(
			_capabilityStore,
			_diagnosticsRouter,
			_protocolForwarder,
			_logger,
			_workspaceFolders,
			_workspaceRootsDisplayText,
			() => _isDisposed,
			RaiseTransportUnavailable,
			options.ShutdownRequestTimeout,
			options.DisposeWaitTimeout,
			_lifetimeToken);
	}
}
