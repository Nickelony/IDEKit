namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Groups the workspace-change inputs a <see cref="WorkspaceChangeCoordinator"/> needs, so its constructor stays
/// readable and positional mistakes between the several delegate parameters are impossible.
/// </summary>
/// <param name="WorkspaceRootDirectoryPaths">The normalized workspace root directories to watch.</param>
/// <param name="WorkspaceRootsDisplayText">The comma-joined normalized workspace root paths used in log text.</param>
/// <param name="ProviderDisplayNameAccessor">
/// Returns the provider display name used in log text; the coordinator reads the name on demand so no derived
/// member runs while the provider base constructor is executing.
/// </param>
/// <param name="WatchSpecifications">
/// The file patterns that should be mirrored to the language server, or an empty list to disable workspace
/// watching for all roots.
/// </param>
/// <param name="WorkspaceFileWatcherFactory">Builds the workspace file watcher for one root.</param>
/// <param name="IsConfigurationPath">Reports whether a normalized changed path requires a settings refresh.</param>
/// <param name="CreateSettingsPayload">Creates the settings payload sent when a configuration file changes.</param>
/// <param name="Hooks">The owner hooks used for client access, disposal probing, startup, transport marking, and failure reporting.</param>
/// <param name="Logger">The logger instance.</param>
internal sealed record WorkspaceChangeCoordinatorContext(
	IReadOnlyList<string> WorkspaceRootDirectoryPaths,
	string WorkspaceRootsDisplayText,
	Func<string> ProviderDisplayNameAccessor,
	IReadOnlyList<WorkspaceWatchSpecification> WatchSpecifications,
	WorkspaceFileWatcherFactory WorkspaceFileWatcherFactory,
	Func<string, bool> IsConfigurationPath,
	Func<object> CreateSettingsPayload,
	WorkspaceChangeHooks Hooks,
	ILogger Logger);
