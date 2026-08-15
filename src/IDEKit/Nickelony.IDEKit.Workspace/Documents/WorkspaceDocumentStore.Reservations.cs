using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentResultFactory;

namespace Nickelony.IDEKit.Workspace.Documents;

public sealed partial class WorkspaceDocumentStore
{
	// Test accessor: groups the store's test-only seams. The type is internal and reachable only
	// through InternalsVisibleTo; production callers leave every hook null and skip its branches.
	internal WorkspaceDocumentStoreTestHooks TestHooks { get; }

	// Snapshot creation reads several LogicalDocument fields that are mutated under _stateLock.
	// Call sites that do not already hold the lock use these wrappers so a concurrent mutation
	// cannot produce a torn snapshot; the lock is re-entrant, so in-lock callers can keep using
	// the factory directly.
	private WorkspaceDocumentSnapshot CreateSnapshotUnderLock(LogicalDocument document)
	{
		lock (_stateLock)
			return CreateSnapshot(document);
	}

	private IReadOnlyList<WorkspaceDocumentSnapshot> CreateSnapshotsUnderLock(IEnumerable<LogicalDocument> documents)
	{
		lock (_stateLock)
			return CreateSnapshots(documents);
	}

	// Requires _stateLock. Used by the stale-reload path, where the document id no longer resolves but
	// the instance may still be tracked under a new id after a rename or save-as.
	private LogicalDocument? FindDocumentByKey(WorkspaceDocumentKey documentKey)
	{
		foreach (LogicalDocument document in _documents.Values)
		{
			if (document.DocumentKey == documentKey)
				return document;
		}

		return null;
	}

	private void CompleteDestinationReservation(DestinationReservation reservation)
	{
		lock (_stateLock)
		{
			if (_destinationReservations.TryGetValue(reservation.DocumentId, out DestinationReservation? current)
				&& ReferenceEquals(current, reservation))
			{
				_destinationReservations.Remove(reservation.DocumentId);
				reservation.Completion.TrySetResult();
			}
		}
	}

	// Requires _stateLock. Returns the active directory operation whose source subtree contains the
	// path, or null when no rename or recursive delete currently covers it. The directory path itself
	// is covered as well as its descendants, so an open or a move targeting the exact directory id is
	// parked behind the in-flight operation instead of proceeding against a location about to change.
	private DirectoryOperationReservation? FindDirectoryOperationUnder(string documentId)
	{
		foreach (DirectoryOperationReservation directoryOperation in _directoryOperations)
		{
			if (WorkspaceDocumentPath.IsDirectoryOrDescendant(
				documentId,
				directoryOperation.DirectoryId,
				directoryOperation.SourcePrefix,
				_pathComparison.Comparison))
				return directoryOperation;
		}

		return null;
	}

	// Requires _stateLock. Collects the completion tasks of the reservations that currently cover a
	// path inside the source directory subtree: opens whose load is in flight and file moves or
	// save-as operations that write into the subtree. The directory path itself is covered as well as
	// its descendants. A directory rename or delete awaits them outside the state lock before the
	// physical move or delete, so no load can capture a file whose location is about to change and no
	// write can land in a subtree that is being vacated.
	private void CollectSubtreeReservations(string directoryId, List<Task> reservations)
	{
		string directoryPrefix = WorkspaceDocumentPath.GetDirectoryPrefix(directoryId);

		foreach (KeyValuePair<string, OpenReservation> entry in _openReservations)
		{
			if (WorkspaceDocumentPath.IsDirectoryOrDescendant(entry.Key, directoryId, directoryPrefix, _pathComparison.Comparison))
				reservations.Add(entry.Value.Completion.Task);
		}

		foreach (KeyValuePair<string, DestinationReservation> entry in _destinationReservations)
		{
			if (WorkspaceDocumentPath.IsDirectoryOrDescendant(entry.Key, directoryId, directoryPrefix, _pathComparison.Comparison))
				reservations.Add(entry.Value.Completion.Task);
		}
	}

	private void CompleteDirectoryOperation(DirectoryOperationReservation reservation)
	{
		lock (_stateLock)
		{
			_directoryOperations.Remove(reservation);
			reservation.Completion.TrySetResult();
		}
	}
}
