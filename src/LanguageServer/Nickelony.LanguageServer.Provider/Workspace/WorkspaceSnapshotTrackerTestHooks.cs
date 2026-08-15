namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Holds the hooks that interleave capture and apply operations on <see cref="WorkspaceSnapshotTracker"/>.
/// </summary>
/// <remarks>
/// The type is internal and each hook defaults to <see langword="null"/>, so the default configuration installs no
/// hook and every hook-guarded branch is skipped.
/// </remarks>
internal sealed class WorkspaceSnapshotTrackerTestHooks
{
	/// <summary>
	/// Gets the shared hooks instance that installs no hook.
	/// </summary>
	public static WorkspaceSnapshotTrackerTestHooks None { get; } = new();

	/// <summary>
	/// Gets the hook invoked after the version that <see cref="WorkspaceSnapshotTracker.ApplyChanges"/> read was
	/// captured and before the updates are computed, so a test can replace the tracked snapshot deterministically in
	/// between, or <see langword="null"/>.
	/// </summary>
	public Action? SnapshotVersionRead { get; init; }

	/// <summary>
	/// Gets the hook invoked after the changed-file updates were computed and before they are committed, so a test
	/// can interleave a competing apply deterministically, or <see langword="null"/>.
	/// </summary>
	public Action? BeforeApplyCommit { get; init; }

	/// <summary>
	/// Gets the hook invoked after a capture walked the file system and before its result is published as the tracked
	/// snapshot, so a test can interleave a competing apply deterministically, or <see langword="null"/>.
	/// </summary>
	public Action? BeforeCapturePublish { get; init; }
}
