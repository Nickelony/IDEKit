namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Groups the per-root inputs a <see cref="WorkspaceWatchScope"/> needs, so the scope constructor stays readable
/// and positional mistakes between the several delegate parameters are impossible.
/// </summary>
/// <param name="WorkspaceRootDirectoryPath">The normalized workspace root directory the scope owns.</param>
/// <param name="ProviderDisplayNameAccessor">
/// Returns the provider display name used in log and diagnostic text; the scope reads the name on demand so no
/// derived member runs while the provider base constructor is executing.
/// </param>
/// <param name="WatchSpecifications">The file patterns that should be mirrored to the language server.</param>
/// <param name="WorkspaceFileWatcherFactory">Builds the workspace file watcher for this root.</param>
/// <param name="DispatchAsync">Forwards coalesced file changes of this root to the owning coordinator, naming the
/// delivering scope so a nested root's own watcher stays the single forwarder for its subtree.</param>
/// <param name="Logger">The logger instance.</param>
internal sealed record WorkspaceWatchScopeContext(
	string WorkspaceRootDirectoryPath,
	Func<string> ProviderDisplayNameAccessor,
	IReadOnlyList<WorkspaceWatchSpecification> WatchSpecifications,
	WorkspaceFileWatcherFactory WorkspaceFileWatcherFactory,
	Func<WorkspaceWatchScope, FileChangeBatch, CancellationToken, Task> DispatchAsync,
	ILogger Logger);
