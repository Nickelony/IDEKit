namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Creates the workspace file watcher for one workspace root.
/// </summary>
/// <remarks>
/// This is the injection seam for the watching engine: assign a factory to
/// <see cref="LanguageServerProviderOptions.WorkspaceFileWatcherFactory"/> to replace the framework default for a
/// host environment (for example a host file API) without subclassing the provider. The framework starts the
/// returned watcher and reports a startup failure through the provider's workspace-watcher-failed event, so the
/// factory must return an unstarted watcher and must not throw for a valid root.
/// </remarks>
/// <param name="workspaceRootDirectoryPath">The normalized workspace root directory to watch.</param>
/// <param name="dispatchAsync">The callback that forwards coalesced file changes to the owning coordinator.</param>
/// <param name="onWatcherFailed">The callback that reports a watcher failure to the owning coordinator.</param>
/// <returns>The workspace file watcher for the supplied root.</returns>
public delegate IWorkspaceFileWatcher WorkspaceFileWatcherFactory(
	string workspaceRootDirectoryPath,
	Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
	Action<IWorkspaceFileWatcher, Exception?> onWatcherFailed);
