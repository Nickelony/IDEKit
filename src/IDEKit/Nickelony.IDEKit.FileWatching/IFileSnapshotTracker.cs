namespace Nickelony.IDEKit.FileWatching;

/// <summary>Defines the tracked-snapshot engine that a watch supervisor drives for one workspace root.</summary>
/// <remarks>
/// <para>
/// A test replaces the tracked-snapshot engine by returning an implementation of this interface from a <see cref="FileSnapshotTrackerFactory"/>.
/// </para>
/// <para>
/// The tracker records the subset of workspace paths the owner mirrors so that watcher recovery can reconcile the file changes
/// missed while the watcher was unavailable. A tracker that captures a different file view than the watcher produces a delta that does not match
/// the forwarded changes. A test that replaces one engine should therefore replace the other.
/// </para>
/// </remarks>
public interface IFileSnapshotTracker
{
	/// <summary>Captures the current workspace state and replaces the tracked snapshot with it.</summary>
	void CaptureTrackedSnapshot();

	/// <summary>Creates a stable clone of the currently tracked workspace snapshot.</summary>
	/// <returns>The cloned snapshot, keyed by normalized path.</returns>
	Dictionary<string, FileSnapshotEntry> CloneTrackedSnapshot();

	/// <summary>Captures the current workspace state, replaces the tracked snapshot, and returns a stable clone of the fresh snapshot.</summary>
	/// <returns>The newly captured snapshot as a caller-owned clone that later tracked changes cannot mutate.</returns>
	Dictionary<string, FileSnapshotEntry> ReplaceTrackedSnapshotWithCurrent();

	/// <summary>Applies a set of already forwarded file changes to the tracked snapshot.</summary>
	/// <param name="changes">The normalized forwarded file changes.</param>
	void ApplyChanges(IReadOnlyList<FileChange> changes);

	/// <summary>Builds the normalized batch of changes that reconciles two snapshots of this tracker's workspace root.</summary>
	/// <param name="previousSnapshot">The baseline snapshot captured before watcher disruption.</param>
	/// <param name="currentSnapshot">The replacement snapshot captured after watcher recovery.</param>
	/// <returns>The sorted change batch needed to reconcile the snapshots.</returns>
	FileChangeBatch BuildDeltaBatch(
		IReadOnlyDictionary<string, FileSnapshotEntry> previousSnapshot,
		IReadOnlyDictionary<string, FileSnapshotEntry> currentSnapshot
	);
}
