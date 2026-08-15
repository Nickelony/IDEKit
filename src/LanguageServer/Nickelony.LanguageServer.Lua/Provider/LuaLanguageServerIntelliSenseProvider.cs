namespace Nickelony.LanguageServer.Lua;

/// <summary>
/// Implements the Lua IntelliSense provider by synchronizing text documents with LuaLS and caching its diagnostics and semantic tokens.
/// </summary>
/// <remarks>
/// The provider owns the language-server client supplied to its constructor and the workspace watcher created by the
/// provider framework; dispose the provider when the host no longer needs it. The disposal order and callback
/// rules are defined by the base class.
/// </remarks>
public sealed partial class LuaLanguageServerIntelliSenseProvider : LanguageServerIntelliSenseProviderBase
{
	private readonly LuaLanguageServerOptions _options;
	private readonly WorkspaceFileWatcherFactory? _workspaceFileWatcherFactoryOverride;
	private readonly ServerPayloadDocumentStore _documentStore;

	/// <summary>
	/// Initializes a new instance of the <see cref="LuaLanguageServerIntelliSenseProvider"/> class.
	/// </summary>
	/// <param name="workspaceRootDirectoryPaths">
	/// The root directories of the current Lua script workspace. The first entry is the primary root of the
	/// language-server session; every entry is watched for external changes. Entries may be nested; duplicates are
	/// rejected by local-path identity after normalization.
	/// </param>
	/// <param name="serverExecutablePath">The LuaLS executable path, or <see langword="null"/> when unavailable.</param>
	/// <param name="options">
	/// The LuaLS settings overrides to apply for this workspace, or <see langword="null"/> for the defaults.
	/// </param>
	/// <param name="providerOptions">The provider tunables, or <see langword="null"/> for the framework defaults.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="workspaceRootDirectoryPaths"/> contains an empty, whitespace-only, or duplicate entry.
	/// </exception>
	/// <exception cref="ArgumentNullException"><paramref name="workspaceRootDirectoryPaths"/> is <see langword="null"/>.</exception>
	/// <exception cref="PathTooLongException">A workspace root directory path exceeds the platform-specific maximum length.</exception>
	/// <exception cref="NotSupportedException">A workspace root directory path contains a colon that is not part of a volume identifier.</exception>
	public LuaLanguageServerIntelliSenseProvider(IReadOnlyList<string> workspaceRootDirectoryPaths, string? serverExecutablePath,
		LuaLanguageServerOptions? options = null, LanguageServerProviderOptions? providerOptions = null, ILogger? logger = null)
		: this(workspaceRootDirectoryPaths,
			CreateClient(workspaceRootDirectoryPaths, serverExecutablePath, options ?? LuaLanguageServerOptions.Default),
			new ServerPayloadDocumentStore(),
			providerOptions: providerOptions,
			workspaceFileWatcherFactory: null,
			logger: logger,
			luaOptions: options)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="LuaLanguageServerIntelliSenseProvider"/> class for testing and dependency injection.
	/// </summary>
	/// <param name="workspaceRootDirectoryPaths">
	/// The root directories of the current Lua script workspace, in caller order. Entries may be nested;
	/// duplicates are rejected by local-path identity after normalization.
	/// </param>
	/// <param name="client">The language-server client, or <see langword="null"/> when unavailable.</param>
	/// <param name="providerOptions">The provider tunables, or <see langword="null"/> for the framework defaults.</param>
	/// <param name="workspaceFileWatcherFactory">Overrides workspace watcher creation, or <see langword="null"/> for the framework default.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <param name="luaOptions">The LuaLS settings overrides, or <see langword="null"/> for the defaults.</param>
	/// <remarks>
	/// Ownership of <paramref name="client"/> transfers to the provider. The client is disposed when this provider is
	/// disposed; when construction fails after the transfer, the base class disposes the client as construction
	/// unwinds.
	/// </remarks>
	internal LuaLanguageServerIntelliSenseProvider(IReadOnlyList<string> workspaceRootDirectoryPaths, ILanguageServerClient? client,
		LanguageServerProviderOptions? providerOptions = null,
		WorkspaceFileWatcherFactory? workspaceFileWatcherFactory = null,
		ILogger? logger = null,
		LuaLanguageServerOptions? luaOptions = null)
		: this(workspaceRootDirectoryPaths, client, new ServerPayloadDocumentStore(), providerOptions, workspaceFileWatcherFactory, logger, luaOptions)
	{ }

	/// <summary>
	/// Initializes the provider with a caller-supplied document store, so the provider keeps the same reference for
	/// typed access instead of downcasting the store the framework owns.
	/// </summary>
	private LuaLanguageServerIntelliSenseProvider(IReadOnlyList<string> workspaceRootDirectoryPaths, ILanguageServerClient? client,
		ServerPayloadDocumentStore documentStore,
		LanguageServerProviderOptions? providerOptions,
		WorkspaceFileWatcherFactory? workspaceFileWatcherFactory,
		ILogger? logger,
		LuaLanguageServerOptions? luaOptions)
		: base(workspaceRootDirectoryPaths, documentStore, LuaWorkspaceConventions.WatchSpecifications, client, providerOptions, logger)
	{
		_documentStore = documentStore;
		_options = luaOptions ?? LuaLanguageServerOptions.Default;
		_workspaceFileWatcherFactoryOverride = workspaceFileWatcherFactory;
	}

	/// <inheritdoc/>
	protected override IWorkspaceFileWatcher CreateWorkspaceFileWatcher(
		string workspaceRootDirectoryPath,
		Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
		Action<IWorkspaceFileWatcher, Exception?> onWatcherFailed)
	{
		return _workspaceFileWatcherFactoryOverride?.Invoke(workspaceRootDirectoryPath, dispatchAsync, onWatcherFailed)
			?? base.CreateWorkspaceFileWatcher(workspaceRootDirectoryPath, dispatchAsync, onWatcherFailed);
	}

	/// <summary>
	/// Gets the tracked-document store as the framework's shared payload store.
	/// </summary>
	/// <remarks>
	/// The instance is created by the chaining constructor and passed to the base class, which owns it; the provider
	/// keeps the same reference for typed access, so no downcast of the base store is needed. Every constructor
	/// routes through the one that assigns this field, so the reference is never null.
	/// </remarks>
	private ServerPayloadDocumentStore DocumentStore => _documentStore;

	private static LanguageServerClient? CreateClient(IReadOnlyList<string> workspaceRootDirectoryPaths, string? serverExecutablePath, LuaLanguageServerOptions options)
	{
		// The base constructor validates the roots only after this factory has run, so normalize them here
		// first: an invalid root list must fail before a client exists. An empty list is valid and models a
		// folderless provider, matching a folderless client session.
		string[] normalizedRoots = LanguageServerPaths.NormalizeWorkspaceRoots(workspaceRootDirectoryPaths);

		if (string.IsNullOrWhiteSpace(serverExecutablePath))
			return null;

		return new LanguageServerClient(normalizedRoots, serverExecutablePath, new LanguageServerClientOptions
		{
			SettingsProvider = () => SettingsFactory.Create(options),
			ClientCapabilitiesProvider = _ => ClientCapabilitiesFactory.Create(),
			InitializationOptionsProvider = _ => InitializationOptionsFactory.Create()
		});
	}
}
