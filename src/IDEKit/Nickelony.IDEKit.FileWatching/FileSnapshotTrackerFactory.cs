namespace Nickelony.IDEKit.FileWatching;

/// <summary>Creates the snapshot tracker for one workspace root.</summary>
/// <param name="workspaceRootDirectoryPath">The normalized workspace root directory whose files are captured.</param>
/// <param name="watchSpecifications">The file patterns that participate in the tracked snapshot.</param>
/// <remarks>
/// This is the injection seam for the tracked-snapshot engine. A supervisor invokes the factory once per workspace root, lazily when the tracker
/// is first used (usually the initial capture after watching starts).
/// </remarks>
public delegate IFileSnapshotTracker FileSnapshotTrackerFactory(
	string workspaceRootDirectoryPath,
	IReadOnlyList<FileWatchSpecification> watchSpecifications
);
