namespace Nickelony.LanguageServer.Provider;

/// <summary>Composes one language-server session's collaborators behind the aggregate provider contract.</summary>
/// <remarks>
/// <para>
/// A <see cref="LanguageServerProfile"/> carries what differs between languages,
/// so one sealed class serves every language. The host passes the profile, the workspace roots and the server executable path;
/// the provider creates and owns the <see cref="ILanguageServerClient"/>, and disposes it with the provider. A <see langword="null"/> or blank
/// executable path means the language server is unavailable, and every request returns its documented fallback value after the provider
/// reports a persistent startup failure. <see cref="Create"/> adopts a client that the host built itself. The client also owns the settings:
/// when the profile reports a changed file as a configuration file, the provider asks the client to push them. Diagnostics are an optional feature:
/// a profile that supplies a diagnostic-mapping policy exposes them, whereas a profile without one exposes none. The provider decodes and caches
/// semantic tokens itself, and the feature members (completion, hover, definition, references, rename, formatting, signature help, document symbols,
/// workspace symbols and code actions) carry the standard LSP implementation.
/// </para>
/// <para>
/// A <see cref="DiagnosticsUpdated"/> or <see cref="SemanticTokensUpdated"/> event names its document by the id the host last
/// opened or renamed it with, matched under <see cref="LanguageServerFeatureProviderOptions.PathComparison"/>; a document tracked only for a request
/// or an update keeps the id that call carried. A host that compares ids ordinally therefore finds its own document however the server spells
/// the URI.
/// </para>
/// <para>
/// No profile delegate runs during construction: the language-specific values the framework needs up front are data, and every delegate
/// runs lazily on the thread that triggers it (including connection, watcher and scheduler threads). Events are raised synchronously
/// on the thread that reports the change, which can be a thread that holds the framework's internal startup lock; subscribers should return promptly
/// and must not synchronously wait on provider operations from a handler.
/// </para>
/// <para>
/// Disposal is idempotent and closes callback admission before releasing provider-owned resources;
/// the repository's <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/ConsumerIntegration.md">consumer integration guide</see>
/// describes the lifecycle, threading, and disposal contract end to end.
/// </para>
/// </remarks>
public sealed partial class LanguageServerFeatureProvider : ILanguageServerFeatureProvider
{
	private readonly ILanguageServerClient? _client;

	/// <param name="profile">The data and rules of the language this provider serves.</param>
	/// <param name="workspaceRootDirectoryPaths">
	/// The root directories of the current workspace; every entry is watched for external changes. Entries may be nested; duplicates are rejected
	/// by normalized-path identity under <see cref="LanguageServerFeatureProviderOptions.PathComparison"/>. Every entry must be a fully
	/// qualified local directory path; a relative entry is rejected. The first entry is the primary root of the language-server session.
	/// An empty list models a folderless provider, matching a folderless client session; no workspace root is watched, so external-change discovery
	/// is the host's responsibility.
	/// </param>
	/// <param name="serverExecutablePath">
	/// The language server executable path. A <see langword="null"/> or whitespace-only path leaves the language features unavailable.
	/// </param>
	/// <param name="hooks">
	/// The delegates that receive the messages the server sends besides the answers to requests,
	/// or <see langword="null"/> for none; they apply to the client that this constructor creates.
	/// </param>
	/// <param name="options">The provider tunables, or <see langword="null"/> for <see cref="LanguageServerFeatureProviderOptions.Default"/>.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="workspaceRootDirectoryPaths"/> contains a <see langword="null"/>, empty, whitespace-only, relative, or duplicate entry;
	/// or a watch specification of <paramref name="profile"/> has an invalid filter (<see langword="null"/>, empty, whitespace-only,
	/// <c>.</c> or <c>..</c>, rooted, or containing a directory separator).
	/// </exception>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="profile"/> or <paramref name="workspaceRootDirectoryPaths"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="PathTooLongException">A workspace root directory path exceeds the platform-specific maximum length.</exception>
	/// <exception cref="NotSupportedException">A workspace root directory path contains a colon that is not part of a volume identifier.</exception>
	public LanguageServerFeatureProvider(
		LanguageServerProfile profile,
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		string? serverExecutablePath,
		LanguageServerFeatureProviderHooks? hooks = null,
		LanguageServerFeatureProviderOptions? options = null,
		ILogger? logger = null
	)
		: this(
			profile,
			workspaceRootDirectoryPaths,
			created: CreateClient(profile, workspaceRootDirectoryPaths, serverExecutablePath, hooks, providerOptions: options, logger),
			options,
			logger
		) { }

	/// <param name="profile">The data and rules of the language this provider serves.</param>
	/// <param name="workspaceRootDirectoryPaths">The workspace roots, in caller order; see the public constructor for the accepted entries.</param>
	/// <param name="created">The client that <see cref="CreateClient"/> built and the specification it was built from.</param>
	/// <param name="options">The provider tunables, or <see langword="null"/> for <see cref="LanguageServerFeatureProviderOptions.Default"/>.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	private LanguageServerFeatureProvider(
		LanguageServerProfile profile,
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		(LanguageServerClient? Client, ClientSpecification? Specification) created,
		LanguageServerFeatureProviderOptions? options,
		ILogger? logger
	)
		: this(profile, workspaceRootDirectoryPaths, client: created.Client, options, logger) => ClientSpecification = created.Specification;

	/// <param name="profile">The data and rules of the language this provider serves.</param>
	/// <param name="workspaceRootDirectoryPaths">The workspace roots, in caller order; see the public constructor for the accepted entries.</param>
	/// <param name="client">
	/// The language-server client owned by this provider, or <see langword="null"/> when the language server is unavailable.
	/// </param>
	/// <param name="options">The provider tunables, or <see langword="null"/> for <see cref="LanguageServerFeatureProviderOptions.Default"/>.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <param name="workspaceFileWatcherFactory">
	/// The factory that replaces the workspace file watcher of every root, or <see langword="null"/> for the default.
	/// </param>
	/// <param name="workspaceSnapshotTrackerFactory">
	/// The factory that replaces the snapshot tracker of every root, or <see langword="null"/> for the default.
	/// </param>
	/// <param name="testHooks">The observers of the interleaving points, or <see langword="null"/> for none.</param>
	/// <remarks>
	/// Ownership of <paramref name="client"/> transfers to the provider as the constructor's first action, so any construction failure -
	/// including argument validation - disposes the client as it unwinds and a failed construction never leaks or strands it.
	/// </remarks>
	internal LanguageServerFeatureProvider(
		LanguageServerProfile profile,
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		ILanguageServerClient? client,
		LanguageServerFeatureProviderOptions? options = null,
		ILogger? logger = null,
		FileWatcherFactory? workspaceFileWatcherFactory = null,
		FileSnapshotTrackerFactory? workspaceSnapshotTrackerFactory = null,
		LanguageServerFeatureProviderTestHooks? testHooks = null
	)
	{
		// Take ownership of the client before any validation runs: the caller handed the client to this
		// provider, so every failure below must dispose it rather than strand it (disposal is idempotent).
		_client = client;

		try
		{
			ArgumentNullException.ThrowIfNull(profile);

			ProviderDisplayName = profile.LanguageDisplayName;
			Logger = logger ?? NullLogger.Instance;
			TestHooks = testHooks ?? new LanguageServerFeatureProviderTestHooks();

			LanguageServerFeatureProviderOptions providerOptions = options ?? LanguageServerFeatureProviderOptions.Default;

			IReadOnlyList<string> normalizedWorkspaceRootDirectoryPaths = LocalPath.NormalizeRoots(
				workspaceRootDirectoryPaths,
				providerOptions.PathComparison
			);

			// Validate the specification content here, not only in the tracker: the tracker is created lazily, so a
			// constructor argument contract must be enforced by the constructor that documents it.
			for (int i = 0; i < profile.WatchSpecifications.Count; i++)
				FileWatchSpecification.Validate(profile.WatchSpecifications[i], nameof(profile));

			Composition = new ProviderComposition(
				owner: this,
				client: _client,
				workspaceRootDirectoryPaths: normalizedWorkspaceRootDirectoryPaths,
				profile,
				options: providerOptions,
				TestHooks,
				workspaceFileWatcherFactory,
				workspaceSnapshotTrackerFactory,
				Logger
			);

			if (_client is not null)
			{
				_client.DiagnosticsPublished += Composition.DiagnosticsSource.HandlePublished;
				_client.SemanticTokensRefreshRequested += Composition.SemanticTokensSource.HandleRefreshRequested;
				_client.DiagnosticRefreshRequested += Composition.DiagnosticsSource.HandleRefreshRequested;
				_client.ConnectionUnavailable += HandleConnectionUnavailable;
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

	/// <summary>Creates a provider that drives a client the host built itself instead of launching a server.</summary>
	/// <param name="profile">The data and rules of the language this provider serves.</param>
	/// <param name="workspaceRootDirectoryPaths">The workspace roots, in caller order; see the public constructor for the accepted entries.</param>
	/// <param name="client">The client the provider drives; ownership transfers to the provider.</param>
	/// <param name="options">The provider tunables, or <see langword="null"/> for <see cref="LanguageServerFeatureProviderOptions.Default"/>.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <returns>The provider.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="profile"/>, <paramref name="workspaceRootDirectoryPaths"/> or <paramref name="client"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// The provider uses <paramref name="client"/> as supplied, so the host builds it with the transport, capabilities and settings it needs;
	/// the launch settings, client capabilities and payload builders of <paramref name="profile"/> apply only to a client
	/// that the provider creates itself. The client is disposed when this provider is disposed, and when construction fails after the transfer
	/// the constructor disposes it as construction unwinds.
	/// </remarks>
	public static LanguageServerFeatureProvider Create(
		LanguageServerProfile profile,
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		ILanguageServerClient client,
		LanguageServerFeatureProviderOptions? options = null,
		ILogger? logger = null
	)
	{
		ArgumentNullException.ThrowIfNull(client);
		return new(profile, workspaceRootDirectoryPaths, client, options, logger);
	}

	/// <inheritdoc/>
	public bool IsAvailable => Composition.Lifecycle.State == LanguageServerFeatureProviderState.Ready && Composition.Lifecycle.IsConnectionReady;

	/// <summary>The provider's lifecycle state.</summary>
	/// <remarks>
	/// <see cref="IsAvailable"/> is the readiness decision a host takes; a test reads the state to observe the provider's own transitions.
	/// </remarks>
	internal LanguageServerFeatureProviderState State => Composition.Lifecycle.State;

	/// <inheritdoc/>
	/// <remarks>
	/// The snapshot is <see cref="LanguageServerFeatureProviderCapabilities.None"/> while the provider is not available.
	/// The read goes through <see cref="IsAvailable"/>, which takes the startup-state lock, rather than a cached availability snapshot:
	/// the availability conjunction must observe the transient window in which the startup-succeeded flag is invalidated
	/// before the provider-state transition runs, and a cache would widen that window by serving a stale advertisement. The client's negotiated
	/// capabilities are one atomic read, so the flags of one snapshot never mix two connection generations.
	/// </remarks>
	public LanguageServerFeatureProviderCapabilities Capabilities
	{
		get
		{
			if (!IsAvailable || _client is not { } client)
				return LanguageServerFeatureProviderCapabilities.None;

			NegotiatedServerCapabilities negotiated = client.NegotiatedCapabilities;

			return new LanguageServerFeatureProviderCapabilities
			{
				SupportsHover = negotiated.SupportsHover,
				SupportsDefinition = negotiated.SupportsDefinition,
				SupportsSignatureHelp = negotiated.SupportsSignatureHelp,
				SupportsReferences = negotiated.SupportsReferences,
				SupportsRename = negotiated.SupportsRename,
				CompletionTriggerCharacters = negotiated.CompletionTriggerCharacters,
				SignatureHelpTriggerCharacters = negotiated.SignatureHelpTriggerCharacters,
				SignatureHelpRetriggerCharacters = negotiated.SignatureHelpRetriggerCharacters,
			};
		}
	}

	/// <inheritdoc/>
	public event EventHandler? CapabilitiesChanged
	{
		add => Composition.Events.CapabilitiesChanged += value;
		remove => Composition.Events.CapabilitiesChanged -= value;
	}

	/// <inheritdoc/>
	public event EventHandler<StartupFailedEventArgs>? StartupFailed
	{
		add => Composition.Events.StartupFailed += value;
		remove => Composition.Events.StartupFailed -= value;
	}

	/// <inheritdoc/>
	public event EventHandler<WorkspaceFileWatchFailedEventArgs>? WorkspaceFileWatchFailed
	{
		add => Composition.Events.WorkspaceFileWatchFailed += value;
		remove => Composition.Events.WorkspaceFileWatchFailed -= value;
	}

	/// <summary>The observers of the interleaving points in the provider's startup path and the signals it announces.</summary>
	internal LanguageServerFeatureProviderTestHooks TestHooks { get; }

	/// <summary>The collaborators of this session, for tests that read tracked state or drive one collaborator directly.</summary>
	internal ProviderComposition Composition { get; }

	/// <summary>The specification of the client that the public constructor created, or <see langword="null"/> when it created none.</summary>
	/// <remarks>
	/// A test reads what the constructor handed the client; the provider never reads it.
	/// </remarks>
	internal ClientSpecification? ClientSpecification { get; }

	/// <summary>The language display name of the profile, for log and diagnostic text.</summary>
	private string ProviderDisplayName { get; }

	/// <summary>The logger instance supplied to the constructor, or a no-op logger when none was supplied.</summary>
	private ILogger Logger { get; }

	private bool IsDisposed => Composition.Disposal.IsDisposed;

	/// <inheritdoc/>
	/// <remarks>
	/// Each call acquires one open lease, so repeated opens of a document need as many <see cref="CloseDocument"/> calls, and the document
	/// is tracked until its last open lease is closed. The current content is synchronized through the language-server document lifecycle.
	/// An open whose synchronization would land while the connection is starting or a restart replay owns the tracked records is deferred:
	/// the provider waits for that flow to settle and applies the open again, for a bounded number of attempts. When the startup ultimately fails,
	/// the open stays unapplied like every other failed-startup operation. Compare <see cref="UpdateDocument"/> for the update behavior,
	/// where a skipped change is re-established by the next operation that supplies document content. The call applies after every earlier call
	/// on the same document, also while one of them waits for the connection to start, and it ends an update of the document that still waits.
	/// </remarks>
	public void OpenDocument(string documentId, string documentText)
	{
		ArgumentNullException.ThrowIfNull(documentId);
		ArgumentNullException.ThrowIfNull(documentText);

		if (IsDisposed)
			return;

		if (!Composition.Context.TryNormalizeDocumentEntryPath(documentId, "Document open", out string? normalizedFilePath))
			return;

		Composition.Synchronizer.Open(normalizedFilePath, documentText, documentId);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// <para>
	/// The change is coalesced with other pending updates for the document and synchronized as the next document change. An update for a document
	/// that is not tracked yet creates an idle tracked record (the tracker mirrors an open) that is closed again once it is trimmed
	/// or explicitly closed with <see cref="CloseDocument"/>, because an update acquires no open lease. An update after a close or a move
	/// is serialized against the affected document and can reopen or update the resulting tracked document. The update applies after every earlier
	/// call on the same document, also while one of them waits for the connection to start, and a later close or open of the document ends an update
	/// that still waits.
	/// </para>
	/// <para>
	/// While the connection is starting or a restart replay owns the tracked records, an update is skipped: the tracked content is not committed
	/// and nothing is sent, so the change is re-established by the next operation that supplies document content (every request does).
	/// </para>
	/// </remarks>
	public void UpdateDocument(string documentId, string documentText)
	{
		ArgumentNullException.ThrowIfNull(documentId);
		ArgumentNullException.ThrowIfNull(documentText);

		if (IsDisposed)
			return;

		if (!Composition.Context.TryNormalizeDocumentEntryPath(documentId, "Document update", out string? normalizedFilePath))
			return;

		Composition.Synchronizer.Update(normalizedFilePath, documentText, documentId);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// <para>
	/// Each call releases one open lease. The server copy is closed when the last lease is released, including a close
	/// that is deferred because a temporary request lease still uses the document; the deferred close is completed when that lease is released.
	/// A call once the document is untracked is a no-op. The close applies after every earlier call on the same document, so it closes the document
	/// an earlier open opened even while that open waits for the connection to start.
	/// </para>
	/// <para>
	/// A close never starts the connection: the server copy is closed only while a session is ready. The startup-succeeded flag is deliberately
	/// not consulted here, so a close that lands during a restart replay still closes a document the replay already reopened instead of leaving
	/// a phantom server-open copy behind.
	/// </para>
	/// </remarks>
	public void CloseDocument(string documentId)
	{
		ArgumentNullException.ThrowIfNull(documentId);

		if (IsDisposed || _client is null)
			return;

		if (!Composition.Context.TryNormalizeDocumentEntryPath(documentId, "Document close", out string? normalizedFilePath))
			return;

		Composition.Synchronizer.Close(normalizedFilePath);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// <para>
	/// A case-only rename refers to the same document under this provider's path comparison (by default on Windows and macOS)
	/// and is therefore a deliberate no-op. Because path identity is case-insensitive under that comparison, a directory rename that only changes
	/// casing cannot be expressed through this member.
	/// </para>
	/// <para>
	/// The rename is evaluated asynchronously, so its outcome is resolved against the tracked state its exclusive slot observes:
	/// a rename whose source is not tracked, or whose destination is already tracked, is a no-op that leaves every pending
	/// update in place. Only a completed rename cancels a stale update queued for the vacated old path; updates for the destination path stay queued
	/// because they were requested after this call and carry newer content for the document's new identity. A completed rename keeps the open leases
	/// and the supplied content, and it keeps cached results valid for unchanged content; a content change invalidates them. The rename applies
	/// after every earlier call on either document, also while one of them waits for the connection to start.
	/// </para>
	/// </remarks>
	public void RenameDocument(string oldDocumentId, string newDocumentId, string documentText)
	{
		ArgumentNullException.ThrowIfNull(oldDocumentId);
		ArgumentNullException.ThrowIfNull(newDocumentId);
		ArgumentNullException.ThrowIfNull(documentText);

		if (IsDisposed)
			return;

		if (!Composition.Context.TryNormalizeDocumentEntryPath(oldDocumentId, "Document rename", out string? normalizedOldFilePath))
			return;

		if (!Composition.Context.TryNormalizeDocumentEntryPath(newDocumentId, "Document rename", out string? normalizedNewFilePath))
			return;

		// A case-only rename refers to the same document on case-insensitive file systems, so it stays a no-op
		// before any pending update could be touched for it.
		if (Composition.Context.Options.PathComparison.Comparer.Equals(normalizedOldFilePath, normalizedNewFilePath))
			return;

		Composition.Synchronizer.Rename(oldFilePath: normalizedOldFilePath, newFilePath: normalizedNewFilePath, content: documentText, newDocumentId);
	}

	/// <summary>Creates the client of a provider that launches its server, or <see langword="null"/> when no executable is configured.</summary>
	/// <param name="profile">The profile whose launch settings, capabilities and builders configure the client.</param>
	/// <param name="workspaceRootDirectoryPaths">
	/// The workspace roots, in caller order. They are normalized before any client exists, so an invalid list fails first; an empty list models
	/// a folderless session.
	/// </param>
	/// <param name="serverExecutablePath">The language server executable path. A blank path yields no client.</param>
	/// <param name="hooks">The host hooks that the client's server-message hooks hand their messages to, or <see langword="null"/> for none.</param>
	/// <param name="providerOptions">
	/// The provider tunables, or <see langword="null"/> for the defaults; the roots are checked under their path comparison, so a duplicate fails
	/// here as it would in the provider constructor.
	/// </param>
	/// <param name="logger">
	/// The logger the client reports its connection and server-message diagnostics to, or <see langword="null"/> for none.
	/// </param>
	/// <returns>
	/// The unstarted client with the specification it was built from, or <see langword="null"/> for both when <paramref name="serverExecutablePath"/>
	/// is blank.
	/// </returns>
	/// <remarks>
	/// The constructor calls this in its chained argument list, so the client exists before the constructor takes ownership of it.
	/// </remarks>
	private static (LanguageServerClient? Client, ClientSpecification? Specification) CreateClient(
		LanguageServerProfile profile,
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		string? serverExecutablePath,
		LanguageServerFeatureProviderHooks? hooks,
		LanguageServerFeatureProviderOptions? providerOptions,
		ILogger? logger
	)
	{
		ArgumentNullException.ThrowIfNull(profile);
		ArgumentNullException.ThrowIfNull(workspaceRootDirectoryPaths);

		// The provider constructor validates the roots only after the client factory has run, so normalize them here
		// first: an invalid root list must fail before a client exists. An empty list is valid and models a
		// folderless provider, matching a folderless client session.
		IReadOnlyList<string> normalizedRoots = LocalPath.NormalizeRoots(
			workspaceRootDirectoryPaths,
			LanguageServerFeatureProviderOptions.PathComparisonOf(providerOptions)
		);

		if (string.IsNullOrWhiteSpace(serverExecutablePath))
			return (null, null);

		var specification = ClientSpecification.From(profile);

		LanguageServerClientHooks? clientHooks = hooks is null
			? null
			: ClientHooksFactory.Create(hooks, LanguageServerFeatureProviderOptions.PathComparisonOf(providerOptions), logger ?? NullLogger.Instance);

		var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: normalizedRoots,
			transport: new StdioLanguageServerTransport(serverExecutablePath, specification.Launch),
			hooks: clientHooks,
			options: specification.Options,
			logger
		);

		return (client, specification);
	}

	private void HandleConnectionUnavailable(object? sender, ConnectionUnavailableEventArgs eventArgs) =>
		Composition.Lifecycle.HandleClientConnectionUnavailable(eventArgs.ConnectionGeneration);
}
