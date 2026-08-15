using Nickelony.IDEKit.IntelliSense.Diagnostics;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Provides the language-neutral provider lifecycle, document synchronization, workspace watching, and
/// request machinery that a language-server provider builds on.
/// </summary>
/// <remarks>
/// <para>
/// Derive from this class, implement the protected hook members, and pass the language's tracked-document store,
/// its watch specifications, and an optional <see cref="ILanguageServerClient"/> to the constructor. The base owns
/// the client and disposes it with the provider; passing <see langword="null"/> means the language server is
/// unavailable and every request returns its documented fallback value after the provider reports a persistent
/// startup failure. Diagnostics and semantic tokens are optional features: their hooks
/// (<see cref="HandleDiagnosticsPayload"/>, <see cref="GetTrackedDiagnostics"/>,
/// <see cref="GetTrackedSemanticTokens"/>, and <see cref="TryStoreSemanticTokens"/>) have no-op defaults, so a
/// provider implements only the hooks for the features it enables. The remaining hooks are required, but the
/// feature members (completion, hover, definition, references, rename, formatting, signature help, document
/// symbols, and code actions) carry the standard LSP implementation in this base and are overridden only when a
/// language needs different behavior.
/// </para>
/// <para>
/// No hook runs during construction: the language-specific values the framework needs up front are constructor
/// arguments, and every remaining hook runs lazily on the thread that triggers it (including transport, watcher,
/// and scheduler threads). Events are raised synchronously on the thread that reports the change, which can be a
/// thread that holds the framework's internal startup lock; subscribers should return promptly and must not
/// synchronously wait on provider operations from a handler.
/// </para>
/// <para>
/// Document paths and workspace roots are absolute local file paths. A path that cannot be normalized to one makes
/// the affected document member a no-op or returns its documented fallback value and is logged at debug level.
/// Because relative input is anchored to the process working directory, passing a workspace-relative path silently
/// tracks a different file, and diagnostics published for non-file URIs are dropped. Path identity (casing) follows
/// the shared <see cref="LanguageServerPaths"/> policy, which is case-insensitive on Windows and macOS.
/// </para>
/// <para>
/// Disposal is idempotent; <see cref="Dispose"/> and <see cref="IAsyncDisposable.DisposeAsync"/> share one teardown.
/// Whichever overload is called first performs it, and a later caller waits for it to finish instead of returning
/// while it continues. Disposal stops new callbacks, cancels provider-owned work, disposes the workspace watcher and
/// the owned language-server client (running the <see cref="OnDisposing"/> hook before the client is disposed so
/// language-specific subscriptions can detach first), and leaves the provider unusable. The synchronous
/// <see cref="Dispose"/> blocks the calling thread for as long as that teardown takes, so prefer
/// <see cref="IAsyncDisposable.DisposeAsync"/> on a thread with a synchronization context, and do not call
/// <see cref="Dispose"/> reentrantly from a callback raised inside the teardown: such a call waits on the teardown
/// it is itself part of and cannot complete.
/// </para>
/// </remarks>
public abstract partial class LanguageServerIntelliSenseProviderBase : ILanguageServerIntelliSenseProvider
{
	private readonly ILogger _logger;

	private readonly string _workspaceRootsDisplayText;
	private readonly ILanguageServerClient? _client;
	private readonly IReadOnlyList<WorkspaceWatchSpecification> _workspaceWatchSpecifications;
	private readonly WorkspaceChangeCoordinator _workspaceChanges;
	private readonly RequestDispatcher _requestDispatcher;
	private readonly StartupState _startupState;
	private readonly LanguageServerProviderOptions _options;

	private readonly DocumentOperationScheduler _documentScheduler = new();
	private readonly TrackedDocumentStore _documents;

	// Snapshots whose post-restart reopen did not run to completion. Written under the startup lock; document
	// slots also read it lock-free to skip local content commits while a replay owns the tracked records, so the
	// field is volatile.
	private volatile IReadOnlyList<DocumentSnapshot>? _pendingReopenDocuments;

	private readonly object _callbackAdmissionSyncRoot = new();

	// Deliberately not disposed: SemaphoreSlim allocates a kernel handle only when AvailableWaitHandle is
	// requested, and skipping disposal avoids racing in-flight startup leases that may still release it.
	private readonly SemaphoreSlim _startLock = new(1, 1);

	/// <summary>
	/// Gets the test-only hooks that expose nondeterministic points in the provider's startup path.
	/// </summary>
	internal ProviderTestHooks TestHooks { get; } = new();

	private readonly CancellationTokenSource _disposeCts = new();
	private readonly CancellationToken _disposeToken;

	private EventHandler<DiagnosticsUpdatedEventArgs>? _diagnosticsUpdated;
	private EventHandler? _capabilitiesChanged;
	private EventHandler<StartupFailedEventArgs>? _startupFailed;
	private EventHandler<WorkspaceWatcherFailedEventArgs>? _workspaceWatcherFailed;

	private int _disposeStarted;
	private volatile bool _isDisposed;
	private bool _callbackAdmissionClosed;

	// Close requests that arrived while a temporary request reference kept the document tracked. The close is
	// retried when the request reference is released so the server copy does not stay open indefinitely.
	private readonly ConcurrentDictionary<string, byte> _pendingDocumentCloses = new(LanguageServerPaths.LocalPathComparer);

	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerIntelliSenseProviderBase"/> class.
	/// </summary>
	/// <param name="workspaceRootDirectoryPaths">
	/// The root directories of the current workspace; every entry is watched for external changes. Entries may be
	/// nested; duplicates are rejected by normalized-path identity (case-insensitive on Windows and macOS, ordinal
	/// elsewhere). Pass absolute local directory paths; relative entries are anchored to the process working
	/// directory. An empty list models a folderless provider, matching a folderless client session; no workspace
	/// root is watched, so external-change discovery is the host's responsibility.
	/// </param>
	/// <param name="documentStore">
	/// The tracked-document store that owns the language's document state. The provider owns the store and mediates
	/// all synchronization through its request pipeline.
	/// </param>
	/// <param name="watchSpecifications">
	/// The file patterns mirrored to the language server and tracked for recovery snapshots, or an empty list to
	/// disable workspace watching for every workspace root. The accepted pattern grammar is defined by
	/// <see cref="WorkspaceWatchSpecification"/> (a file-name filter without directory separators); casing follows
	/// the configured local-path comparison policy.
	/// </param>
	/// <param name="client">The language-server client owned by this provider, or <see langword="null"/> when the language server is unavailable.</param>
	/// <param name="options">The provider tunables, or <see langword="null"/> for <see cref="LanguageServerProviderOptions.Default"/>.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="workspaceRootDirectoryPaths"/> contains an empty, whitespace-only, or duplicate
	/// entry; or <paramref name="watchSpecifications"/> contains an entry whose filter is invalid (empty,
	/// whitespace-only, rooted, or containing a directory separator).
	/// </exception>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="workspaceRootDirectoryPaths"/>, <paramref name="documentStore"/>, or
	/// <paramref name="watchSpecifications"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="PathTooLongException">A workspace root directory path exceeds the platform-specific maximum length.</exception>
	/// <exception cref="NotSupportedException">A workspace root directory path contains a colon that is not part of a volume identifier.</exception>
	/// <exception cref="ArgumentOutOfRangeException">One of the <paramref name="options"/> values is outside its supported range.</exception>
	/// <remarks>
	/// The constructor performs no virtual dispatch: the document store and the watch specifications are constructor
	/// arguments, so a derived provider can build them from its own initialized state. Ownership of
	/// <paramref name="client"/> transfers to the provider as the constructor's first action, so any construction
	/// failure - including argument validation - disposes the client as it unwinds and a failed construction never
	/// leaks or strands it.
	/// </remarks>
	protected LanguageServerIntelliSenseProviderBase(
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		TrackedDocumentStore documentStore,
		IReadOnlyList<WorkspaceWatchSpecification> watchSpecifications,
		ILanguageServerClient? client,
		LanguageServerProviderOptions? options = null,
		ILogger? logger = null)
	{
		// Take ownership of the client before any validation runs: the caller handed the client to this
		// provider, so every failure below must dispose it rather than strand it (disposal is idempotent).
		_client = client;

		try
		{
			ArgumentNullException.ThrowIfNull(documentStore);
			ArgumentNullException.ThrowIfNull(watchSpecifications);

			_logger = logger ?? NullLogger.Instance;
			_options = options ?? LanguageServerProviderOptions.Default;

			_options.Validate();

			string[] normalizedWorkspaceRootDirectoryPaths = LanguageServerPaths.NormalizeWorkspaceRoots(workspaceRootDirectoryPaths);

			// An empty root list models a folderless provider, matching the folderless client session the shared
			// path helper also supports; the workspace coordinator then creates no watch scope, so no external
			// change is discovered unless the host bridges file events itself. Log text uses a folderless label so
			// the workspace-root display string stays non-empty for the logger and the request dispatcher.
			_workspaceRootsDisplayText = normalizedWorkspaceRootDirectoryPaths.Length == 0
				? "<folderless>"
				: string.Join(", ", normalizedWorkspaceRootDirectoryPaths);

			_documents = documentStore;
			_workspaceWatchSpecifications = watchSpecifications;

			// Cache the token, and deliberately never dispose the source: request paths can still link
			// their timeout tokens against a canceled source, but linking against the token of a disposed
			// source throws ObjectDisposedException, which would escape to the caller instead of surfacing
			// the documented disposal fallback.
			_disposeToken = _disposeCts.Token;

			// The dispatcher validates the same timeout values again as part of its own public contract.
			_requestDispatcher = new RequestDispatcher(
				_workspaceRootsDisplayText,
				_client,
				_options,
				new RequestDispatcherContext(
					IsDisposedAccessor: () => _isDisposed,
					EnsureTransportStartedAsync: EnsureTransportStartedAsync,
					DisposeToken: _disposeToken,
					Logger: _logger));
			_startupState = new StartupState(
				_client,
				initialState: LanguageServerProviderState.Unavailable,
				readyState: LanguageServerProviderState.Ready,
				disposedState: LanguageServerProviderState.Disposed,
				isDisposedAccessor: () => _isDisposed);
			_workspaceChanges = new WorkspaceChangeCoordinator(
				new WorkspaceChangeCoordinatorContext(
					normalizedWorkspaceRootDirectoryPaths,
					_workspaceRootsDisplayText,
					ProviderDisplayNameAccessor: () => ProviderDisplayName,
					_workspaceWatchSpecifications,
					CreateWorkspaceFileWatcher,
					IsConfigurationPath,
					CreateSettingsPayload,
					new WorkspaceChangeHooks(
						ClientAccessor: () => _client,
						IsDisposedAccessor: () => _isDisposed,
						EnsureTransportStartedAsync: EnsureTransportStartedAsync,
						TryMarkTransportUnhealthy: TryMarkWorkspaceTransportUnhealthy,
						RaiseWorkspaceWatcherFailed: RaiseWorkspaceWatcherFailed),
					_logger));

			if (_client is not null)
			{
				_client.DiagnosticsPublished += HandleDiagnosticsPublished;
				_client.SemanticTokensRefreshRequested += HandleSemanticTokensRefreshRequested;
				_client.DiagnosticRefreshRequested += HandleDiagnosticRefreshRequested;
				_client.TransportUnavailable += HandleTransportUnavailable;
			}
		}
		catch
		{
			// Ownership of the client passed to the provider when it was taken above; dispose it so a
			// construction failure cannot leak it (disposal is idempotent).
			_client?.Dispose();
			throw;
		}
	}

	/// <inheritdoc/>
	public bool IsAvailable => State == LanguageServerProviderState.Ready && IsTransportReady;

	/// <summary>
	/// Gets a value indicating whether the transport is ready to carry a document operation, as a lock-free
	/// snapshot.
	/// </summary>
	/// <remarks>
	/// A present, ready client whose most recent startup succeeded on the current transport generation, with the
	/// provider not disposed. This is the one readiness rule: <see cref="IsAvailable"/> adds the provider-state
	/// check on top, and the per-document scheduler slots observe this snapshot instead of starting the transport
	/// themselves. The conjunction deliberately repeats conditions that look implied by the provider state, because
	/// TryMarkWorkspaceTransportUnhealthy invalidates the startup-succeeded flag before the state transition runs,
	/// so availability must not be reported during that transient window.
	/// </remarks>
	private bool IsTransportReady
		=> !_isDisposed
			&& _client is not null
			&& _client.IsReady
			&& _startupState.StartupSucceeded;

	/// <inheritdoc/>
	public LanguageServerProviderState State
		=> _startupState.State;

	/// <inheritdoc/>
	public bool SupportsReferences => ClientSupports(client => client.SupportsReferences);

	/// <inheritdoc/>
	public bool SupportsRename => ClientSupports(client => client.SupportsRename);

	/// <inheritdoc/>
	public bool SupportsFormatting => ClientSupports(client => client.SupportsFormatting);

	/// <inheritdoc/>
	public bool SupportsDocumentSymbols => ClientSupports(client => client.SupportsDocumentSymbols);

	/// <inheritdoc/>
	public bool SupportsCodeActions => ClientSupports(client => client.SupportsCodeActions);

	/// <summary>
	/// Determines whether the provider is available and the client advertises a capability, reading the
	/// capability from the non-null client that an availability check guarantees, so no capability
	/// property needs a null-forgiving suppression.
	/// </summary>
	/// <param name="capability">Reads a capability flag from the active client.</param>
	/// <returns><see langword="true"/> when the provider is available and the client advertises the capability; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The capability reads deliberately go through <see cref="IsAvailable"/> (which takes the startup-state lock)
	/// rather than a cached availability snapshot: the availability conjunction must observe the transient window in
	/// which the startup-succeeded flag is invalidated before the provider-state transition runs, and a cache would
	/// widen that window by serving a stale <see langword="true"/>. The lock acquisition is the correctness
	/// mechanism, so it is not traded for a cached value.
	/// </remarks>
	private bool ClientSupports(Func<ILanguageServerClient, bool> capability)
		=> IsAvailable && _client is { } client && capability(client);

	/// <inheritdoc/>
	/// <remarks>
	/// The event is raised synchronously on the client's serialized diagnostics-delivery path: the language
	/// payload hook (<see cref="HandleDiagnosticsPayload"/>) and every subscriber handler run before the next
	/// payload for the same client is dispatched, so a slow hook or handler applies back-pressure to
	/// diagnostics delivery for that client. Handlers must therefore be cheap and must not block.
	/// </remarks>
	public event EventHandler<DiagnosticsUpdatedEventArgs>? DiagnosticsUpdated
	{
		add => AddAdmittedCallback(ref _diagnosticsUpdated, value);
		remove => RemoveCallback(ref _diagnosticsUpdated, value);
	}

	/// <inheritdoc/>
	public event EventHandler? CapabilitiesChanged
	{
		add => AddAdmittedCallback(ref _capabilitiesChanged, value);
		remove => RemoveCallback(ref _capabilitiesChanged, value);
	}

	/// <inheritdoc/>
	public event EventHandler<StartupFailedEventArgs>? StartupFailed
	{
		add => AddAdmittedCallback(ref _startupFailed, value);
		remove => RemoveCallback(ref _startupFailed, value);
	}

	/// <inheritdoc/>
	public event EventHandler<WorkspaceWatcherFailedEventArgs>? WorkspaceWatcherFailed
	{
		add => AddAdmittedCallback(ref _workspaceWatcherFailed, value);
		remove => RemoveCallback(ref _workspaceWatcherFailed, value);
	}

	/// <summary>
	/// Gets the owned language-server client, or <see langword="null"/> when the provider was constructed without one.
	/// </summary>
	protected ILanguageServerClient? Client => _client;

	/// <summary>
	/// Sends a language-server request through the provider's timeout, retry, and restart policy.
	/// </summary>
	/// <typeparam name="TResponse">The response payload type.</typeparam>
	/// <param name="method">The LSP method name.</param>
	/// <param name="parameters">The request parameters serialized as the method payload.</param>
	/// <param name="fallbackValue">The value returned when the request fails or is canceled by disposal.</param>
	/// <param name="cancellationToken">A token that cancels the request.</param>
	/// <returns>The server response, or <paramref name="fallbackValue"/> when the request cannot be completed.</returns>
	/// <remarks>
	/// Most requests should route through <see cref="SendDocumentRequestAsync{TResponse, TResult}"/> so the
	/// document is synchronized, the capability gate runs, and the temporary request reference is released. Use
	/// this member directly only for requests that do not operate on a tracked document (for example
	/// <c>completionItem/resolve</c>); the semantic-token and pull-diagnostics requests reach the request
	/// dispatcher directly for the same reason, sharing the same timeout and restart policy.
	/// </remarks>
	/// <exception cref="ArgumentException"><paramref name="method"/> is empty or whitespace-only.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="method"/> or <paramref name="parameters"/> is <see langword="null"/>.</exception>
	protected Task<TResponse> SendRequestAsync<TResponse>(string method, object parameters, TResponse fallbackValue,
		CancellationToken cancellationToken)
		=> _requestDispatcher.SendAsync(method, parameters, fallbackValue, cancellationToken);

	/// <summary>
	/// Gets the logger instance supplied to the constructor, or a no-op logger when none was supplied.
	/// </summary>
	protected ILogger Logger => _logger;

	/// <summary>
	/// Gets a value indicating whether the provider has started disposing.
	/// </summary>
	protected bool IsDisposed => _isDisposed;

	/// <inheritdoc/>
	public IReadOnlyList<TextDiagnostic> GetDiagnostics(string filePath)
	{
		ArgumentNullException.ThrowIfNull(filePath);

		if (_isDisposed)
			return [];

		if (!TryNormalizeDocumentEntryPath(filePath, "Diagnostics read", out string? normalizedFilePath))
			return [];

		return InvokeContainedHook(() => GetTrackedDiagnostics(normalizedFilePath), [], "tracked diagnostics");
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The document is tracked until its last open reference is closed. The current content is synchronized through
	/// the language-server document lifecycle. An open whose synchronization would land while the transport is
	/// starting or a restart replay owns the tracked records is deferred: the provider waits for that flow to
	/// settle and applies the open again. When the startup ultimately fails, the open stays unapplied like every
	/// other failed-startup operation. Compare <see cref="UpdateDocument"/> for the update behavior, where a
	/// skipped change is re-established by the next operation that supplies document content.
	/// </remarks>
	public void OpenDocument(string filePath, string documentText)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(documentText);

		if (_isDisposed)
			return;

		if (!TryNormalizeDocumentEntryPath(filePath, "Document open", out string? normalizedFilePath))
			return;

		CancelQueuedDocumentUpdate(normalizedFilePath);

		ObserveBackgroundTask(OpenDocumentAsync(normalizedFilePath, documentText), $"Document open '{normalizedFilePath}'");
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The change is coalesced with other pending updates for the document and synchronized as the next document
	/// change. An update for a document that is not tracked yet creates an idle tracked record (the store mirrors an
	/// open) that is closed again once it is trimmed or explicitly closed with <see cref="CloseDocument"/>.
	/// <para>
	/// While the transport is starting or a restart replay owns the tracked records, an update is skipped: the
	/// tracked content is not committed and nothing is sent, so the change is re-established by the next
	/// operation that supplies document content (every request does).
	/// </para>
	/// </remarks>
	public void UpdateDocument(string filePath, string documentText)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(documentText);

		if (_isDisposed)
			return;

		if (!TryNormalizeDocumentEntryPath(filePath, "Document update", out string? normalizedFilePath))
			return;

		ObserveBackgroundTask(UpdateDocumentAsync(normalizedFilePath, documentText), $"Document change '{normalizedFilePath}'");
	}

	/// <inheritdoc/>
	/// <remarks>
	/// <para>
	/// Each call releases one open reference. The server copy is closed when the last reference is released,
	/// including a close that is deferred because a temporary request reference still uses the document; the
	/// deferred close is completed when that reference is released.
	/// </para>
	/// <para>
	/// A close never starts the transport: the server copy is closed only while a session is ready. The
	/// startup-succeeded flag is deliberately not consulted here, so a close that lands during a restart replay
	/// still closes a document the replay already reopened instead of leaving a phantom server-open copy behind.
	/// </para>
	/// </remarks>
	public void CloseDocument(string filePath)
	{
		ArgumentNullException.ThrowIfNull(filePath);

		if (_isDisposed || _client is null)
			return;

		if (!TryNormalizeDocumentEntryPath(filePath, "Document close", out string? normalizedFilePath))
			return;

		CancelQueuedDocumentUpdate(normalizedFilePath);

		ObserveBackgroundTask(CloseDocumentAsync(normalizedFilePath, CancellationToken.None), $"Document close '{normalizedFilePath}'");
	}

	/// <inheritdoc/>
	/// <remarks>
	/// <para>
	/// A case-only rename refers to the same document under this provider's local-path identity (for example on
	/// Windows and macOS) and is therefore a deliberate no-op. Because path identity is case-insensitive on those
	/// platforms, a directory rename that only changes casing cannot be expressed through this member.
	/// </para>
	/// <para>
	/// The move is evaluated asynchronously, so its outcome is resolved against the tracked state its exclusive
	/// slot observes: a move whose source is not tracked, or whose destination is already tracked, is a no-op that
	/// leaves every queued update in place. Only a completed rekey cancels a stale update queued for the vacated
	/// old path; updates for the destination path stay queued because they were requested after this call and
	/// carry newer content for the document's new identity.
	/// </para>
	/// </remarks>
	public void MoveDocument(string oldFilePath, string newFilePath, string documentText)
	{
		ArgumentNullException.ThrowIfNull(oldFilePath);
		ArgumentNullException.ThrowIfNull(newFilePath);
		ArgumentNullException.ThrowIfNull(documentText);

		if (_isDisposed)
			return;

		if (!TryNormalizeDocumentEntryPath(oldFilePath, "Document move", out string? normalizedOldFilePath))
			return;

		if (!TryNormalizeDocumentEntryPath(newFilePath, "Document move", out string? normalizedNewFilePath))
			return;

		// A case-only rename refers to the same document on case-insensitive file systems, so it stays a no-op
		// before any queued update could be touched for it.
		if (LanguageServerPaths.AreLocalPathsEqual(normalizedOldFilePath, normalizedNewFilePath))
			return;

		ObserveBackgroundTask(
			MoveDocumentAsync(normalizedOldFilePath, normalizedNewFilePath, documentText, CancellationToken.None),
			$"Document move '{normalizedOldFilePath}' to '{normalizedNewFilePath}'");
	}

	/// <summary>
	/// Observes a provider-owned background task so faults are logged instead of surfacing as unobserved task exceptions.
	/// </summary>
	/// <param name="task">The background task to observe.</param>
	/// <param name="operation">The operation description used in log text.</param>
	/// <exception cref="ArgumentException"><paramref name="operation"/> is empty or whitespace-only.</exception>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="task"/> or <paramref name="operation"/> is <see langword="null"/>.
	/// </exception>
	protected void ObserveBackgroundTask(Task task, string operation)
	{
		ArgumentNullException.ThrowIfNull(task);
		ArgumentNullException.ThrowIfNull(operation);

		BackgroundTaskObserver.Observe(_logger, task, operation);
	}

	/// <summary>
	/// Adds one handler to a callback field unless callback admission has already closed.
	/// </summary>
	/// <typeparam name="TDelegate">The callback delegate type.</typeparam>
	/// <param name="callbackField">The callback field to combine the handler into.</param>
	/// <param name="value">The handler to add.</param>
	protected void AddAdmittedCallback<TDelegate>(ref TDelegate? callbackField, TDelegate? value)
		where TDelegate : Delegate
	{
		lock (_callbackAdmissionSyncRoot)
		{
			if (!_callbackAdmissionClosed)
				callbackField = (TDelegate?)Delegate.Combine(callbackField, value);
		}
	}

	/// <summary>
	/// Removes one handler from a callback field.
	/// </summary>
	/// <typeparam name="TDelegate">The callback delegate type.</typeparam>
	/// <param name="callbackField">The callback field to remove the handler from.</param>
	/// <param name="value">The handler to remove.</param>
	protected void RemoveCallback<TDelegate>(ref TDelegate? callbackField, TDelegate? value)
		where TDelegate : Delegate
	{
		lock (_callbackAdmissionSyncRoot)
			callbackField = (TDelegate?)Delegate.Remove(callbackField, value);
	}

	/// <summary>
	/// Raises the subscribers of one callback field with handler isolation and admission checks between handlers.
	/// </summary>
	/// <typeparam name="TDelegate">The callback delegate type of the raised event.</typeparam>
	/// <param name="callbackAccessor">Returns the current callback list for the event.</param>
	/// <param name="invoke">Invokes one handler.</param>
	/// <param name="subscriberDescription">The subscriber description used in failure logs.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="callbackAccessor"/>, <paramref name="invoke"/>, or <paramref name="subscriberDescription"/> is
	/// <see langword="null"/>.
	/// </exception>
	protected void RaiseSubscribers<TDelegate>(Func<TDelegate?> callbackAccessor, Action<TDelegate> invoke, string subscriberDescription)
		where TDelegate : Delegate
	{
		ArgumentNullException.ThrowIfNull(callbackAccessor);
		ArgumentNullException.ThrowIfNull(invoke);
		ArgumentNullException.ThrowIfNull(subscriberDescription);

		TDelegate? handlers;

		lock (_callbackAdmissionSyncRoot)
		{
			if (_callbackAdmissionClosed || _isDisposed)
				return;

			handlers = callbackAccessor();
		}

		if (handlers is null)
			return;

		foreach (Delegate handler in handlers.GetInvocationList())
		{
			if (!TryAdmitCallback())
				return;

			try
			{
				invoke((TDelegate)handler);
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "{SubscriberDescription} threw; later subscribers will still be notified.", subscriberDescription);
			}
		}
	}

	private bool TryAdmitCallback()
	{
		lock (_callbackAdmissionSyncRoot)
			return !_callbackAdmissionClosed && !_isDisposed;
	}

	private void RaiseDiagnosticsUpdated(string filePath, IReadOnlyList<TextDiagnostic> diagnostics)
		=> RaiseSubscribers<EventHandler<DiagnosticsUpdatedEventArgs>>(
			() => _diagnosticsUpdated,
			handler => handler(this, new DiagnosticsUpdatedEventArgs(filePath, diagnostics)),
			"Diagnostics subscriber");

	private void RaiseCapabilitiesChanged()
		=> RaiseSubscribers<EventHandler>(
			() => _capabilitiesChanged,
			handler => handler(this, EventArgs.Empty),
			"Capability-change subscriber");

	private void RaiseStartupFailed(LanguageServerStartupFailure failure)
		=> RaiseSubscribers<EventHandler<StartupFailedEventArgs>>(
			() => _startupFailed,
			handler => handler(this, new StartupFailedEventArgs(failure)),
			"Startup-failure subscriber");

	private void RaiseWorkspaceWatcherFailed(WorkspaceWatcherFailure failure)
		=> RaiseSubscribers<EventHandler<WorkspaceWatcherFailedEventArgs>>(
			() => _workspaceWatcherFailed,
			handler => handler(this, new WorkspaceWatcherFailedEventArgs(failure)),
			"Workspace-watcher subscriber");

	/// <summary>
	/// Attempts a provider-state transition and raises the capabilities-changed event when the state actually
	/// changed and the transition requested a notification.
	/// </summary>
	/// <param name="state">The requested provider state.</param>
	/// <param name="notifyCapabilitiesChanged"><see langword="true"/> to request a capabilities-changed notification when the state actually changed.</param>
	private void SetProviderState(LanguageServerProviderState state, bool notifyCapabilitiesChanged = false)
	{
		if (_startupState.TrySetState(state, notifyCapabilitiesChanged))
			RaiseCapabilitiesChanged();
	}

	private void TryMarkWorkspaceTransportUnhealthy(long transportGeneration)
	{
		ILanguageServerClient? client = _client;

		if (client is null)
			return;

		try
		{
			if (client.TryMarkTransportUnhealthy(transportGeneration))
				MarkStartupTransportUnavailable();
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Failed to mark the {DisplayName} language server transport unhealthy after a workspace-watcher send failure.",
				ProviderDisplayName);
		}
	}

	private void MarkStartupTransportUnavailable()
	{
		_startupState.InvalidateStartupSucceeded();

		// The hard-failure latch is not downgraded: a transport fault observed after the provider reported a
		// persistent startup failure must not report the provider as retrying again.
		if (State == LanguageServerProviderState.Failed)
			return;

		SetProviderState(LanguageServerProviderState.Unavailable, notifyCapabilitiesChanged: true);
	}

	private bool TryCompleteSuccessfulStart(long transportGeneration)
	{
		bool started = _startupState.TryCompleteSuccessfulStart(transportGeneration, out LanguageServerProviderState previousState);

		if (started
			&& previousState != LanguageServerProviderState.Ready
			&& State == LanguageServerProviderState.Ready)
		{
			RaiseCapabilitiesChanged();
		}

		return started;
	}

	private void HandleTransportUnavailable(object? sender, TransportUnavailableEventArgs eventArgs)
	{
		// The client clears its current capability snapshot before raising this event. Match the
		// event against the generation that most recently reached Ready, not the reset snapshot.
		if (_startupState.OnClientTransportUnavailable(eventArgs.Generation))
			SetProviderState(LanguageServerProviderState.Unavailable, notifyCapabilitiesChanged: true);
	}

	/// <summary>
	/// Runs a void language hook and contains an unexpected hook fault so it cannot escape framework entry points.
	/// </summary>
	/// <param name="hook">The hook invocation.</param>
	/// <param name="hookDescription">The hook description used in failure logs.</param>
	private void InvokeContainedHook(Action hook, string hookDescription)
		=> HookContainment.Invoke(_logger, ProviderDisplayName, hook, hookDescription);

	/// <summary>
	/// Runs a language hook and contains an unexpected hook fault, returning the fallback value instead.
	/// </summary>
	/// <typeparam name="TResult">The hook result type.</typeparam>
	/// <param name="hook">The hook invocation.</param>
	/// <param name="fallbackValue">The value returned when the hook throws.</param>
	/// <param name="hookDescription">The hook description used in failure logs.</param>
	/// <returns>The hook result, or <paramref name="fallbackValue"/> when the hook throws.</returns>
	private TResult InvokeContainedHook<TResult>(Func<TResult> hook, TResult fallbackValue, string hookDescription)
		=> HookContainment.Invoke(_logger, ProviderDisplayName, hook, fallbackValue, hookDescription);

	/// <summary>
	/// Runs an asynchronous language hook and contains an unexpected hook fault; cancellation is treated as expected
	/// teardown and stays silent.
	/// </summary>
	/// <param name="hook">The asynchronous hook invocation.</param>
	/// <param name="hookDescription">The hook description used in failure logs.</param>
	private Task InvokeContainedHookAsync(Func<Task> hook, string hookDescription)
		=> HookContainment.InvokeAsync(_logger, ProviderDisplayName, hook, hookDescription);

	/// <summary>
	/// Logs a document entry point rejection for a path that cannot be normalized to an absolute local file path.
	/// </summary>
	/// <param name="filePath">The rejected caller-supplied path.</param>
	/// <param name="operation">The operation description used in log text.</param>
	private void LogUnusableDocumentPath(string filePath, string operation)
	{
		_logger.LogDebug("{DisplayName} {Operation} ignored the path '{FilePath}' because it cannot be normalized to an absolute local file path.",
			ProviderDisplayName, operation, filePath);
	}

	/// <summary>
	/// Tries to normalize a caller-supplied document path for one entry point, logging the rejection when the
	/// path cannot be used.
	/// </summary>
	/// <param name="filePath">The caller-supplied path.</param>
	/// <param name="operation">The operation description used in log text.</param>
	/// <param name="normalizedFilePath">Receives the normalized path when the path is usable.</param>
	/// <returns><see langword="true"/> when the path was normalized; otherwise, <see langword="false"/>.</returns>
	private bool TryNormalizeDocumentEntryPath(string filePath, string operation, [NotNullWhen(true)] out string? normalizedFilePath)
	{
		if (LanguageServerPaths.TryNormalizeLocalPath(filePath, out normalizedFilePath))
			return true;

		LogUnusableDocumentPath(filePath, operation);
		return false;
	}

	/// <summary>
	/// Logs a best-effort notification failure with the package's severity policy: transport and disposal failures
	/// are expected outcomes logged at debug level, and anything else is logged as a warning.
	/// </summary>
	/// <param name="operation">The operation description used in log text.</param>
	/// <param name="filePath">The document path the failed operation targeted.</param>
	/// <param name="exception">The failure to log.</param>
	private void LogBestEffortFailure(string operation, string filePath, Exception exception)
	{
		if (exception is IOException or ObjectDisposedException)
		{
			_logger.LogDebug(exception, "{DisplayName} best-effort {Operation} failed for '{FilePath}' because the transport is unavailable or the provider is racing disposal.",
				ProviderDisplayName, operation, filePath);
		}
		else
		{
			_logger.LogWarning(exception, "{DisplayName} best-effort {Operation} failed unexpectedly for '{FilePath}'.",
				ProviderDisplayName, operation, filePath);
		}
	}

	/// <summary>
	/// Creates the workspace file watcher for one workspace root.
	/// </summary>
	/// <param name="workspaceRootDirectoryPath">The normalized workspace root directory to watch.</param>
	/// <param name="dispatchAsync">The callback that forwards coalesced file changes to the owning coordinator.</param>
	/// <param name="onWatcherFailed">The callback that reports a watcher failure to the owning coordinator.</param>
	/// <returns>The workspace file watcher for the supplied root.</returns>
	/// <remarks>
	/// The resolved watcher engine is the factory assigned to
	/// <see cref="LanguageServerProviderOptions.WorkspaceFileWatcherFactory"/> when one is set; otherwise, the default
	/// implementation watches through this package's <see cref="WorkspaceFileWatcher"/> over the specifications passed
	/// to the constructor. Override to substitute the watching engine directly, for example with a host file API;
	/// return an unstarted watcher, because the framework starts it and reports a startup failure through
	/// <see cref="WorkspaceWatcherFailed"/> instead of letting the failure escape request APIs.
	/// </remarks>
	protected virtual IWorkspaceFileWatcher CreateWorkspaceFileWatcher(
		string workspaceRootDirectoryPath,
		Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
		Action<IWorkspaceFileWatcher, Exception?> onWatcherFailed)
	{
		return _options.WorkspaceFileWatcherFactory?.Invoke(workspaceRootDirectoryPath, dispatchAsync, onWatcherFailed)
			?? new DefaultWorkspaceFileWatcherAdapter(workspaceRootDirectoryPath, dispatchAsync, _workspaceWatchSpecifications, onWatcherFailed, _logger);
	}
}
