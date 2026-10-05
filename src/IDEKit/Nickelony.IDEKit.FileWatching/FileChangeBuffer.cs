namespace Nickelony.IDEKit.FileWatching;

/// <summary>
/// Holds the file-change coalescers a buffering owner drains together: one holds changes pending the next dispatch, and one holds drained changes
/// that must be replayed before newer pending changes.
/// </summary>
/// <param name="pathComparison">
/// The comparison that decides which paths coalesce into one change, or <see langword="null"/> for the current platform's.
/// </param>
/// <remarks>
/// <para>
/// The debouncer (which owns the dispatch timer) and the owner's forwarder buffer changes this way, so the ordering
/// rule lives here once: the replayed buffer is drained first so a batch that failed a dispatch attempt keeps its original order ahead of the changes
/// buffered while the failed attempt was in flight. Draining the buffers into one batch preserves the replacement semantics of a re-buffered delete
/// followed by a newer create for the same path - the two entries are forwarded in order instead of canceling each other out.
/// </para>
/// <para>
/// The buffer is not thread-safe; its owner serializes the calls.
/// </para>
/// </remarks>
public sealed class FileChangeBuffer(LocalPathComparisonPolicy? pathComparison = null)
{
	private readonly FileChangeCoalescer _pendingChanges = new(pathComparison);

	private readonly FileChangeCoalescer _replayedChanges = new(pathComparison);

	/// <summary>Indicates whether no change is buffered.</summary>
	public bool IsEmpty => _pendingChanges.IsEmpty && _replayedChanges.IsEmpty;

	/// <summary>The number of distinct paths buffered, counting a path held in both coalescers twice.</summary>
	/// <remarks>
	/// A path whose delete and create are both kept counts once, although draining yields two changes for it.
	/// </remarks>
	public int Count => _pendingChanges.Count + _replayedChanges.Count;

	/// <summary>Adds or merges one pending change.</summary>
	/// <param name="filePath">The normalized file path.</param>
	/// <param name="kind">The change kind.</param>
	public void AddPending(string filePath, FileChangeKind kind)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		_pendingChanges.Add(filePath, kind);
	}

	/// <summary>Adds or merges a sequence of pending changes.</summary>
	/// <param name="changes">The changes to merge.</param>
	public void AddPendingRange(IReadOnlyList<FileChange> changes)
	{
		ArgumentNullException.ThrowIfNull(changes);
		_pendingChanges.AddRange(changes);
	}

	/// <summary>Requeues a drained batch into the replay buffer, preserving its drained order.</summary>
	/// <param name="batch">The batch to requeue.</param>
	public void AddReplayed(FileChangeBatch batch)
	{
		ArgumentNullException.ThrowIfNull(batch);

		for (int i = 0; i < batch.Count; i++)
			_replayedChanges.Add(batch.Entries[i].Path, batch.Entries[i].Kind);
	}

	/// <summary>Adds or merges a sequence of changes into the replay buffer.</summary>
	/// <param name="changes">The changes to merge.</param>
	public void AddReplayedRange(IReadOnlyList<FileChange> changes)
	{
		ArgumentNullException.ThrowIfNull(changes);
		_replayedChanges.AddRange(changes);
	}

	/// <summary>Drains the coalescers into the next batch, replayed changes first.</summary>
	/// <returns>The buffered changes in dispatch order; no changes remain buffered afterwards.</returns>
	public FileChangeBatch DrainBatch()
	{
		if (_replayedChanges.IsEmpty)
			return _pendingChanges.DrainBatch();

		FileChangeBatch replayedBatch = _replayedChanges.DrainBatch();

		if (_pendingChanges.IsEmpty)
			return replayedBatch;

		FileChangeBatch pendingBatch = _pendingChanges.DrainBatch();

		var combinedChanges = new List<FileChange>(replayedBatch.Count + pendingBatch.Count);

		combinedChanges.AddRange(replayedBatch.Entries);
		combinedChanges.AddRange(pendingBatch.Entries);

		return new FileChangeBatch(combinedChanges);
	}
}
