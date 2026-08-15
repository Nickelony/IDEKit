namespace Nickelony.LanguageServer.Roslyn;

/// <summary>
/// The shared Roslyn language-server provider core: it supplies the Roslyn handshake, the launch profile, the
/// tracked-document payload caching, and the standard provider hooks, so a language package derives from it and
/// declares only its identity, its source-file patterns, and its option passthrough.
/// </summary>
/// <remarks>
/// <para>
/// The Roslyn language server serves C# and Visual Basic from one process, so the handshake and the feature
/// plumbing are shared here and the language packages differ only in the <c>languageId</c> they open documents
/// with, the display name they report, and the source and project file patterns they watch. The configured
/// workspace roots are advertised in <c>initialize</c>, and the launch profile enables automatic project loading
/// so the server discovers the solutions and projects below them; a loose source file with no project falls back
/// to the server's miscellaneous-files behavior.
/// </para>
/// <para>
/// The provider owns the language-server client supplied to its constructor and the workspace watcher the
/// framework creates; dispose the provider when the host no longer needs it. The disposal order and callback
/// rules are defined by the base class.
/// </para>
/// </remarks>
public abstract partial class RoslynLanguageServerIntelliSenseProvider : LanguageServerIntelliSenseProviderBase
{
	private readonly RoslynLanguageServerOptions _options;
	private readonly ServerPayloadDocumentStore _documentStore;

	/// <summary>
	/// Initializes a new instance of the <see cref="RoslynLanguageServerIntelliSenseProvider"/> class for a host.
	/// </summary>
	/// <param name="workspaceRootDirectoryPaths">
	/// The root directories of the current workspace. The first entry is the primary root of the language-server
	/// session; every entry is watched for external changes. Entries may be nested; duplicates are rejected by
	/// local-path identity after normalization.
	/// </param>
	/// <param name="languageWatchSpecifications">
	/// The language-specific file patterns to mirror to the language server, for example a source-file pattern. The
	/// shared solution, project-asset, build-properties, and build-packages patterns are added by this provider.
	/// </param>
	/// <param name="serverExecutablePath">The Roslyn language server executable path, or <see langword="null"/> when unavailable.</param>
	/// <param name="options">The Roslyn session options to apply, or <see langword="null"/> for the defaults.</param>
	/// <param name="providerOptions">The provider tunables, or <see langword="null"/> for the framework defaults.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="workspaceRootDirectoryPaths"/> contains an empty, whitespace-only, or duplicate entry.
	/// </exception>
	/// <exception cref="ArgumentNullException"><paramref name="workspaceRootDirectoryPaths"/> or <paramref name="languageWatchSpecifications"/> is <see langword="null"/>.</exception>
	/// <exception cref="PathTooLongException">A workspace root directory path exceeds the platform-specific maximum length.</exception>
	/// <exception cref="NotSupportedException">A workspace root directory path contains a colon that is not part of a volume identifier.</exception>
	protected RoslynLanguageServerIntelliSenseProvider(
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		IReadOnlyList<WorkspaceWatchSpecification> languageWatchSpecifications,
		string? serverExecutablePath,
		RoslynLanguageServerOptions? options = null,
		LanguageServerProviderOptions? providerOptions = null,
		ILogger? logger = null)
		: this(workspaceRootDirectoryPaths,
			languageWatchSpecifications,
			CreateClient(workspaceRootDirectoryPaths, serverExecutablePath, options ?? RoslynLanguageServerOptions.Default),
			options,
			providerOptions,
			logger)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="RoslynLanguageServerIntelliSenseProvider"/> class for testing and dependency injection.
	/// </summary>
	/// <param name="workspaceRootDirectoryPaths">
	/// The root directories of the current workspace, in caller order. Entries may be nested; duplicates are
	/// rejected by local-path identity after normalization.
	/// </param>
	/// <param name="languageWatchSpecifications">The language-specific file patterns to mirror to the language server.</param>
	/// <param name="client">The language-server client, or <see langword="null"/> when unavailable.</param>
	/// <param name="options">The Roslyn session options to apply, or <see langword="null"/> for the defaults.</param>
	/// <param name="providerOptions">The provider tunables, or <see langword="null"/> for the framework defaults.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <remarks>
	/// Ownership of <paramref name="client"/> transfers to the provider. The client is disposed when this provider is
	/// disposed; when construction fails after the transfer, the base class disposes the client as construction
	/// unwinds.
	/// </remarks>
	internal RoslynLanguageServerIntelliSenseProvider(
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		IReadOnlyList<WorkspaceWatchSpecification> languageWatchSpecifications,
		ILanguageServerClient? client,
		RoslynLanguageServerOptions? options = null,
		LanguageServerProviderOptions? providerOptions = null,
		ILogger? logger = null)
		: this(workspaceRootDirectoryPaths,
			new ServerPayloadDocumentStore(),
			languageWatchSpecifications,
			client,
			options,
			providerOptions,
			logger)
	{ }

	/// <summary>
	/// Initializes the provider with a caller-supplied document store, so the provider keeps the same reference for
	/// typed access instead of downcasting the store the framework owns.
	/// </summary>
	private RoslynLanguageServerIntelliSenseProvider(
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		ServerPayloadDocumentStore documentStore,
		IReadOnlyList<WorkspaceWatchSpecification> languageWatchSpecifications,
		ILanguageServerClient? client,
		RoslynLanguageServerOptions? options,
		LanguageServerProviderOptions? providerOptions,
		ILogger? logger)
		: base(workspaceRootDirectoryPaths,
			documentStore,
			RoslynWorkspaceConventions.ComposeWatchSpecifications(languageWatchSpecifications),
			client,
			providerOptions,
			logger)
	{
		_documentStore = documentStore;
		_options = options ?? RoslynLanguageServerOptions.Default;
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

	private static LanguageServerClient? CreateClient(
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		string? serverExecutablePath,
		RoslynLanguageServerOptions options)
	{
		// The base constructor validates the roots only after this factory has run, so normalize them here
		// first: an invalid root list must fail before a client exists. An empty list is valid and models a
		// folderless provider, matching a folderless client session.
		string[] normalizedRoots = LanguageServerPaths.NormalizeWorkspaceRoots(workspaceRootDirectoryPaths);

		if (string.IsNullOrWhiteSpace(serverExecutablePath))
			return null;

		return new LanguageServerClient(normalizedRoots, serverExecutablePath, new LanguageServerClientOptions
		{
			ServerArguments = RoslynLaunchProfile.CreateServerArguments(options),
			SettingsProvider = () => SettingsFactory.Create(options),
			ClientCapabilitiesProvider = _ => ClientCapabilitiesFactory.Create(),
			InitializationOptionsProvider = _ => InitializationOptionsFactory.Create(options)
		});
	}
}
