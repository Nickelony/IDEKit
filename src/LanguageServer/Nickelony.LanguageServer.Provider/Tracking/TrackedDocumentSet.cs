using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Holds the tracked document records by normalized path, orders them by access, and evicts the idle ones oldest first.</summary>
/// <param name="syncRoot">The tracker's lock, which guards every member of this set.</param>
/// <param name="pathComparison">The policy that decides which paths identify one document.</param>
/// <remarks>
/// <para>
/// The set is not thread-safe by itself: every member requires the caller to hold the tracker's lock, and a debug
/// build asserts it. The key of an entry equals the path of its record, so removing a record by its own path always finds it.
/// </para>
/// <para>
/// Each access takes the next stamp of one counter, so a larger stamp always means a more recent access.
/// </para>
/// </remarks>
internal sealed class TrackedDocumentSet(object syncRoot, LocalPathComparisonPolicy pathComparison)
{
	private readonly Dictionary<string, TrackedDocumentState> _documents = new(pathComparison.Comparer);
	private long _nextAccessStamp;

	/// <summary>Looks up the record of a document.</summary>
	/// <param name="normalizedFilePath">The normalized local file path of the document.</param>
	/// <param name="state">Receives the record when the document is tracked.</param>
	/// <returns><see langword="true"/> when the document is tracked; otherwise, <see langword="false"/>.</returns>
	internal bool TryGet(string normalizedFilePath, [NotNullWhen(true)] out TrackedDocumentState? state)
	{
		AssertLockHeld();
		return _documents.TryGetValue(normalizedFilePath, out state);
	}

	/// <summary>Indicates whether a document is tracked.</summary>
	/// <param name="normalizedFilePath">The normalized local file path of the document.</param>
	/// <returns><see langword="true"/> when the document is tracked; otherwise, <see langword="false"/>.</returns>
	internal bool Contains(string normalizedFilePath)
	{
		AssertLockHeld();
		return _documents.ContainsKey(normalizedFilePath);
	}

	/// <summary>Tracks a record under a path, replacing the record that the path held.</summary>
	/// <param name="normalizedFilePath">The normalized local file path of the document, which equals the path of <paramref name="state"/>.</param>
	/// <param name="state">The record to track.</param>
	internal void Set(string normalizedFilePath, TrackedDocumentState state)
	{
		AssertLockHeld();
		_documents[normalizedFilePath] = state;
	}

	/// <summary>Stops tracking a document.</summary>
	/// <param name="normalizedFilePath">The normalized local file path of the document.</param>
	/// <returns><see langword="true"/> when the document was tracked; otherwise, <see langword="false"/>.</returns>
	internal bool Remove(string normalizedFilePath)
	{
		AssertLockHeld();
		return _documents.Remove(normalizedFilePath);
	}

	/// <summary>Takes the next access stamp.</summary>
	/// <returns>A stamp larger than every stamp taken before.</returns>
	internal long NextAccessStamp()
	{
		AssertLockHeld();
		return ++_nextAccessStamp;
	}

	/// <summary>Returns snapshots of the records that are open on the server, ordered by file path.</summary>
	/// <returns>The open-document snapshots in ascending ordinal file-path order.</returns>
	/// <remarks>
	/// The result is a fresh list of freshly created snapshots, sorted by path for a deterministic fan-out order; a caller can neither observe
	/// nor mutate the set through it.
	/// </remarks>
	internal IReadOnlyList<DocumentSnapshot> GetOpenSnapshots()
	{
		AssertLockHeld();

		var documents = new List<DocumentSnapshot>();

		foreach (TrackedDocumentState state in _documents.Values)
		{
			if (state.IsOpen)
				documents.Add(state.CreateSnapshot());
		}

		documents.Sort(static (left, right) => string.CompareOrdinal(left.FilePath, right.FilePath));
		return documents;
	}

	/// <summary>Evicts the oldest idle records until at most <paramref name="maxCount"/> idle records remain tracked.</summary>
	/// <param name="maxCount">The maximum number of idle records to keep tracked.</param>
	/// <returns>The snapshots of the evicted records that were open on the server; an evicted record that was already closed has none.</returns>
	/// <remarks>
	/// Idle means the record has no open or request leases, so idle server-open records are also eligible. A record with a lease is never evicted,
	/// whatever its age.
	/// </remarks>
	internal IReadOnlyList<DocumentSnapshot> TrimIdle(int maxCount)
	{
		AssertLockHeld();

		if (_documents.Count <= maxCount)
			return [];

		var candidates = new List<TrackedDocumentState>();

		foreach (TrackedDocumentState state in _documents.Values)
		{
			if (state.Leases.IsIdle)
				candidates.Add(state);
		}

		if (candidates.Count <= maxCount)
			return [];

		candidates.Sort(static (left, right) => left.LastAccessStamp.CompareTo(right.LastAccessStamp));

		int removeCount = candidates.Count - maxCount;
		var documentsToClose = new List<DocumentSnapshot>(removeCount);

		for (int i = 0; i < removeCount; i++)
		{
			TrackedDocumentState state = candidates[i];
			_documents.Remove(state.FilePath);

			if (state.IsOpen)
				documentsToClose.Add(state.CreateSnapshot());
		}

		return documentsToClose;
	}

	/// <summary>Marks every record as closed on the server before a restart.</summary>
	/// <returns>The snapshots of the records that still hold an open lease, which the restart reopens.</returns>
	internal IReadOnlyList<DocumentSnapshot> MarkAllClosedForRestart()
	{
		AssertLockHeld();

		var documentsToReopen = new List<DocumentSnapshot>();

		foreach (TrackedDocumentState state in _documents.Values)
		{
			state.MarkDocumentClosed();

			if (state.Leases.HasOpenLeases)
				documentsToReopen.Add(state.CreateSnapshot());
		}

		return documentsToReopen;
	}

	[Conditional("DEBUG")]
	private void AssertLockHeld() => Debug.Assert(Monitor.IsEntered(syncRoot), "The tracker lock must be held.");
}
