namespace Nickelony.IDEKit.FileWatching;

/// <summary>Creates the file watcher for one workspace root.</summary>
/// <param name="workspaceRootDirectoryPath">The normalized workspace root directory to watch.</param>
/// <param name="dispatchAsync">The callback that forwards coalesced file changes to the owner.</param>
/// <param name="onWatcherFailed">The callback that reports a watcher failure to the owner.</param>
/// <remarks>
/// This is the injection seam for the watching engine: a supervisor starts the watcher that the factory returns and reports a startup
/// failure through <see cref="FileWatchSupervisorHooks.ReportFailure"/>, so the factory must return an unstarted watcher; a throw is contained
/// and reported as that startup failure.
/// </remarks>
public delegate IFileWatcher FileWatcherFactory(
	string workspaceRootDirectoryPath,
	Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
	Action<IFileWatcher, Exception?> onWatcherFailed
);
