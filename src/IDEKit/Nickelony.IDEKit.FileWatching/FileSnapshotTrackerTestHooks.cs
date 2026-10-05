namespace Nickelony.IDEKit.FileWatching;

/// <summary>Holds the hooks that interleave capture and apply operations on <see cref="FileSnapshotTracker"/>.</summary>
/// <remarks>
/// The type is internal and each hook defaults to <see langword="null"/>, so the default configuration installs no hook and every hook-guarded
/// branch is skipped. Every hook is an observer: production code calls it only through <c>?.Invoke()</c> at a preemption point and branches
/// on nothing it returns.
/// </remarks>
internal sealed class FileSnapshotTrackerTestHooks
{
	/// <summary>The shared hooks instance that installs no hook.</summary>
	internal static FileSnapshotTrackerTestHooks None { get; } = new();

	/// <summary>
	/// The hook invoked after the ticket was claimed and before <see cref="FileSnapshotTracker.ApplyChanges"/> stamps the version its observation
	/// is fenced against, so a replacement can be interleaved deterministically at the preemption point between the two, or <see langword="null"/>.
	/// </summary>
	internal Action? BeforeSnapshotVersionRead { get; init; }

	/// <summary>
	/// The hook invoked after the changed-file updates were computed and before they are committed, so a competing apply
	/// can be interleaved deterministically, or <see langword="null"/>.
	/// </summary>
	internal Action? BeforeApplyCommit { get; init; }

	/// <summary>
	/// The hook invoked after a capture walked the file system and before its result is published as the tracked snapshot, so a competing apply
	/// can be interleaved deterministically, or <see langword="null"/>.
	/// </summary>
	internal Action? BeforeCapturePublish { get; init; }
}
