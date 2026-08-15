namespace Nickelony.LanguageServer.VisualBasic;

/// <summary>
/// Implements the Visual Basic IntelliSense provider by driving Microsoft's Roslyn language server with the
/// <c>vb</c> document language identifier.
/// </summary>
/// <remarks>
/// <para>
/// The provider is a thin language package over <see cref="RoslynLanguageServerIntelliSenseProvider"/>: there is no
/// standalone Visual Basic language server, because the Roslyn language server serves C# and Visual Basic from one
/// process, so the handshake, the launch profile, the tracked-document payload caching, and the pull-diagnostics
/// loop live in the shared core. This package declares only the identity and the source and project file patterns
/// Visual Basic documents need; the server settings are expressed through <see cref="RoslynLanguageServerOptions"/>.
/// </para>
/// <para>
/// The provider owns the language-server client supplied to its constructor and the workspace watcher the
/// framework creates; dispose the provider when the host no longer needs it. The disposal order and callback
/// rules are defined by the base class.
/// </para>
/// </remarks>
public sealed class VisualBasicLanguageServerIntelliSenseProvider : RoslynLanguageServerIntelliSenseProvider
{
	private const string VisualBasicDocumentLanguageId = "vb";

	private const string VisualBasicProviderDisplayName = "Visual Basic";

	/// <summary>
	/// Initializes a new instance of the <see cref="VisualBasicLanguageServerIntelliSenseProvider"/> class.
	/// </summary>
	/// <param name="workspaceRootDirectoryPaths">
	/// The root directories of the current Visual Basic workspace. The first entry is the primary root of the
	/// language-server session; every entry is watched for external changes. Entries may be nested; duplicates are
	/// rejected by local-path identity after normalization.
	/// </param>
	/// <param name="serverExecutablePath">The Roslyn language server executable path, or <see langword="null"/> when unavailable.</param>
	/// <param name="options">The Roslyn session options to apply, or <see langword="null"/> for the defaults.</param>
	/// <param name="providerOptions">The provider tunables, or <see langword="null"/> for the framework defaults.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="workspaceRootDirectoryPaths"/> contains an empty, whitespace-only, or duplicate entry.
	/// </exception>
	/// <exception cref="ArgumentNullException"><paramref name="workspaceRootDirectoryPaths"/> is <see langword="null"/>.</exception>
	/// <exception cref="PathTooLongException">A workspace root directory path exceeds the platform-specific maximum length.</exception>
	/// <exception cref="NotSupportedException">A workspace root directory path contains a colon that is not part of a volume identifier.</exception>
	public VisualBasicLanguageServerIntelliSenseProvider(
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		string? serverExecutablePath,
		RoslynLanguageServerOptions? options = null,
		LanguageServerProviderOptions? providerOptions = null,
		ILogger? logger = null)
		: base(workspaceRootDirectoryPaths,
			VisualBasicWorkspaceConventions.WatchSpecifications,
			serverExecutablePath,
			options,
			providerOptions,
			logger)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="VisualBasicLanguageServerIntelliSenseProvider"/> class for testing and dependency injection.
	/// </summary>
	/// <param name="workspaceRootDirectoryPaths">
	/// The root directories of the current Visual Basic workspace, in caller order. Entries may be nested;
	/// duplicates are rejected by local-path identity after normalization.
	/// </param>
	/// <param name="client">The language-server client, or <see langword="null"/> when unavailable.</param>
	/// <param name="options">The Roslyn session options to apply, or <see langword="null"/> for the defaults.</param>
	/// <param name="providerOptions">The provider tunables, or <see langword="null"/> for the framework defaults.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <remarks>
	/// Ownership of <paramref name="client"/> transfers to the provider. The client is disposed when this provider is
	/// disposed; when construction fails after the transfer, the base class disposes the client as construction
	/// unwinds.
	/// </remarks>
	internal VisualBasicLanguageServerIntelliSenseProvider(
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		ILanguageServerClient? client,
		RoslynLanguageServerOptions? options = null,
		LanguageServerProviderOptions? providerOptions = null,
		ILogger? logger = null)
		: base(workspaceRootDirectoryPaths,
			VisualBasicWorkspaceConventions.WatchSpecifications,
			client,
			options,
			providerOptions,
			logger)
	{ }

	/// <inheritdoc/>
	protected override string ProviderDisplayName => VisualBasicProviderDisplayName;

	/// <inheritdoc/>
	protected override string LanguageId => VisualBasicDocumentLanguageId;
}
