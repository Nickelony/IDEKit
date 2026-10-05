using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.FileWatching;

namespace Nickelony.Testing;

/// <summary>
/// Counts the calls a watch supervisor makes to its snapshot tracker, records the changes applied to it, and answers scripted snapshots.
/// </summary>
/// <remarks>
/// Each snapshot call returns a copy of the scripted dictionary, as the real tracker returns a caller-owned clone.
/// </remarks>
internal sealed class RecordingFileSnapshotTracker : IFileSnapshotTracker
{
	private readonly object _syncRoot = new();
	private readonly List<FileChange> _appliedChanges = [];

	public int CaptureCount;
	public int CloneCount;
	public int ReplaceCount;
	public int ApplyCount;

	/// <summary>The snapshot <see cref="CloneTrackedSnapshot"/> returns a copy of: the workspace as it was tracked before a recovery.</summary>
	public IReadOnlyDictionary<string, FileSnapshotEntry> TrackedSnapshot { get; set; } = new Dictionary<string, FileSnapshotEntry>();

	/// <summary>The snapshot <see cref="ReplaceTrackedSnapshotWithCurrent"/> returns a copy of: the workspace as a recovery captures it.</summary>
	public IReadOnlyDictionary<string, FileSnapshotEntry> CurrentSnapshot { get; set; } = new Dictionary<string, FileSnapshotEntry>();

	/// <summary>
	/// The paths that exist on the scripted file system. A path that the current snapshot lacks counts as deleted
	/// unless it is listed here, so the delta never reads the real file system.
	/// </summary>
	public HashSet<string> ExistingPaths { get; } = new(LocalPathComparisonPolicy.ForCurrentPlatform.Comparer);

	/// <summary>An action that runs inside <see cref="CaptureTrackedSnapshot"/>, so a test can hold the baseline capture open.</summary>
	public Action? OnCapture { get; set; }

	/// <summary>An action that runs inside <see cref="ReplaceTrackedSnapshotWithCurrent"/>, so a test can hold the recovery capture open.</summary>
	public Action? OnReplace { get; set; }

	/// <summary>A stable snapshot of the changes passed to <see cref="ApplyChanges"/>, in call order.</summary>
	public IReadOnlyList<FileChange> AppliedChanges
	{
		get
		{
			lock (_syncRoot)
				return [.. _appliedChanges];
		}
	}

	/// <inheritdoc/>
	public void CaptureTrackedSnapshot()
	{
		Interlocked.Increment(ref CaptureCount);
		OnCapture?.Invoke();
	}

	/// <inheritdoc/>
	public Dictionary<string, FileSnapshotEntry> CloneTrackedSnapshot()
	{
		Interlocked.Increment(ref CloneCount);
		return CopyOf(TrackedSnapshot);
	}

	/// <inheritdoc/>
	public Dictionary<string, FileSnapshotEntry> ReplaceTrackedSnapshotWithCurrent()
	{
		Interlocked.Increment(ref ReplaceCount);
		OnReplace?.Invoke();

		return CopyOf(CurrentSnapshot);
	}

	/// <inheritdoc/>
	public void ApplyChanges(IReadOnlyList<FileChange> changes)
	{
		Interlocked.Increment(ref ApplyCount);

		lock (_syncRoot)
			_appliedChanges.AddRange(changes);
	}

	/// <inheritdoc/>
	public FileChangeBatch BuildDeltaBatch(
		IReadOnlyDictionary<string, FileSnapshotEntry> previousSnapshot,
		IReadOnlyDictionary<string, FileSnapshotEntry> currentSnapshot
	)
	{
		return FileSnapshotDiffer.Build(
			previousSnapshot,
			currentSnapshot,
			pathComparison: LocalPathComparisonPolicy.ForCurrentPlatform,
			isPathMissing: path => !ExistingPaths.Contains(path)
		);
	}

	private static Dictionary<string, FileSnapshotEntry> CopyOf(IReadOnlyDictionary<string, FileSnapshotEntry> snapshot) =>
		new(snapshot, LocalPathComparisonPolicy.ForCurrentPlatform.Comparer);
}
