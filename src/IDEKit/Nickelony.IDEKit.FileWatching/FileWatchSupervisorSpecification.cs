namespace Nickelony.IDEKit.FileWatching;

/// <summary>The per-root inputs a <see cref="FileWatchSupervisor"/> is built from.</summary>
/// <param name="WorkspaceRootDirectoryPath">The normalized workspace root directory the supervisor owns.</param>
/// <param name="DisplayName">The name of the owner in log text.</param>
/// <param name="WatchSpecifications">The file patterns that the watcher and the tracker follow.</param>
/// <param name="FileWatcherFactory">Builds the file watcher for this root.</param>
/// <param name="FileSnapshotTrackerFactory">Builds the snapshot tracker for this root.</param>
public sealed record FileWatchSupervisorSpecification(
	string WorkspaceRootDirectoryPath,
	string DisplayName,
	IReadOnlyList<FileWatchSpecification> WatchSpecifications,
	FileWatcherFactory FileWatcherFactory,
	FileSnapshotTrackerFactory FileSnapshotTrackerFactory
);
