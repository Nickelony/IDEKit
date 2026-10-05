namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Creates the file watcher and the snapshot tracker of a workspace root: from the replacement factory when one is given,
/// otherwise the framework default.
/// </summary>
/// <param name="options">The provider options the default implementations follow.</param>
/// <param name="watchSpecifications">The file patterns the default watcher follows.</param>
/// <param name="workspaceFileWatcherFactory">The factory that replaces the default watcher, or <see langword="null"/> for the default.</param>
/// <param name="workspaceSnapshotTrackerFactory">The factory that replaces the default tracker, or <see langword="null"/> for the default.</param>
/// <param name="logger">The logger the default implementations report to.</param>
internal sealed class WorkspaceWatchingFactory(
	LanguageServerFeatureProviderOptions options,
	IReadOnlyList<FileWatchSpecification> watchSpecifications,
	FileWatcherFactory? workspaceFileWatcherFactory,
	FileSnapshotTrackerFactory? workspaceSnapshotTrackerFactory,
	ILogger logger
)
{
	/// <summary>Creates the workspace file watcher for one workspace root.</summary>
	/// <param name="workspaceRootDirectoryPath">The normalized workspace root directory to watch.</param>
	/// <param name="dispatchAsync">The callback that forwards coalesced file changes to the owning coordinator.</param>
	/// <param name="onWatcherFailed">The callback that reports a watcher failure to the owning coordinator.</param>
	/// <returns>The watcher from the replacement factory when one was given; otherwise, the default.</returns>
	internal IFileWatcher CreateFileWatcher(
		string workspaceRootDirectoryPath,
		Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
		Action<IFileWatcher, Exception?> onWatcherFailed
	)
	{
		return workspaceFileWatcherFactory?.Invoke(workspaceRootDirectoryPath, dispatchAsync, onWatcherFailed)
			?? new FileWatcher(
				workspaceRootDirectoryPath,
				dispatchAsync,
				watchSpecifications,
				onWatcherFailed,
				pathComparison: options.PathComparison,
				logger
			);
	}

	/// <summary>Creates the workspace snapshot tracker for one workspace root.</summary>
	/// <param name="workspaceRootDirectoryPath">The normalized workspace root directory whose files are captured.</param>
	/// <param name="trackedSpecifications">The file patterns that participate in the tracked snapshot.</param>
	/// <returns>The tracker from the replacement factory when one was given; otherwise, the default.</returns>
	internal IFileSnapshotTracker CreateSnapshotTracker(
		string workspaceRootDirectoryPath,
		IReadOnlyList<FileWatchSpecification> trackedSpecifications
	)
	{
		return workspaceSnapshotTrackerFactory?.Invoke(workspaceRootDirectoryPath, trackedSpecifications)
			?? new FileSnapshotTracker(
				workspaceRootDirectoryPath,
				watchSpecifications: trackedSpecifications,
				pathComparison: options.PathComparison,
				logger
			);
	}
}
