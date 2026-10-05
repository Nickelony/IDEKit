using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Nickelony.IDEKit.Core.Paths;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Holds the tracked documents of a store and the reservations that keep an in-flight open, move, delete or directory operation exclusive
/// against them.
/// </summary>
/// <remarks>
/// <para>
/// Whether a path is free reads the document table together with the open, destination and directory reservations,
/// so one type owns the table and every reservation kind. A delete reservation is keyed by document, not by path, and answers whether a tracked
/// document can take another operation. The type has no lock of its own: the store passes the lock that orders its other state, every member requires
/// the caller to hold it, and a debug build asserts that.
/// </para>
/// <para>
/// A member that removes a reservation releases its completion task, so a waiter that resumes finds the state after the operation.
/// </para>
/// </remarks>
internal sealed class WorkspaceDocumentTracker
{
	private readonly object _syncRoot;
	private readonly LocalPathComparisonPolicy _pathComparison;
	private readonly Dictionary<string, LogicalDocument> _documents;
	private readonly Dictionary<string, OpenReservation> _openReservations;
	private readonly Dictionary<string, DestinationReservation> _destinationReservations;
	private readonly Dictionary<LogicalDocument, DeleteReservation> _deleteReservations = [];
	private readonly List<DirectoryOperationReservation> _directoryOperations = [];

	/// <param name="syncRoot">The store's lock, held by the caller of every member.</param>
	/// <param name="options">The workspace options whose path comparison keys the document and reservation tables.</param>
	internal WorkspaceDocumentTracker(object syncRoot, WorkspaceOptions options)
	{
		LocalPathComparisonPolicy pathComparison = options.PathComparison;

		_syncRoot = syncRoot;
		_pathComparison = pathComparison;
		_documents = new Dictionary<string, LogicalDocument>(pathComparison.Comparer);
		_openReservations = new Dictionary<string, OpenReservation>(pathComparison.Comparer);
		_destinationReservations = new Dictionary<string, DestinationReservation>(pathComparison.Comparer);
	}

	/// <summary>The tracked documents, in no particular order.</summary>
	internal Dictionary<string, LogicalDocument>.ValueCollection Documents
	{
		get
		{
			AssertLockHeld();
			return _documents.Values;
		}
	}

	/// <summary>Looks up the tracked document of an id.</summary>
	/// <param name="documentId">The normalized document id.</param>
	/// <param name="document">Receives the tracked document.</param>
	/// <returns><see langword="true"/> when the id is tracked; otherwise, <see langword="false"/>.</returns>
	internal bool TryGetDocument(string documentId, [NotNullWhen(true)] out LogicalDocument? document)
	{
		AssertLockHeld();
		return _documents.TryGetValue(documentId, out document);
	}

	/// <summary>Starts tracking a document under an id.</summary>
	/// <param name="documentId">The normalized document id.</param>
	/// <param name="document">The document to track.</param>
	/// <exception cref="ArgumentException">The id is already tracked.</exception>
	internal void AddDocument(string documentId, LogicalDocument document)
	{
		AssertLockHeld();
		_documents.Add(documentId, document);
	}

	/// <summary>Stops tracking the document of an id.</summary>
	/// <param name="documentId">The normalized document id.</param>
	internal void RemoveDocument(string documentId)
	{
		AssertLockHeld();
		_documents.Remove(documentId);
	}

	/// <summary>Replaces the whole document table.</summary>
	/// <param name="documents">The documents to track, keyed by their new ids.</param>
	/// <remarks>
	/// A directory rename rebinds every document of a subtree at once, so the table never holds a half-renamed subtree.
	/// </remarks>
	internal void ReplaceDocuments(Dictionary<string, LogicalDocument> documents)
	{
		AssertLockHeld();

		_documents.Clear();

		foreach (KeyValuePair<string, LogicalDocument> entry in documents)
			_documents.Add(entry.Key, entry.Value);
	}

	/// <summary>Collects the tracked documents inside a directory subtree in deterministic id order.</summary>
	/// <param name="directoryId">The normalized directory id.</param>
	/// <returns>The documents at the directory id and below it, sorted by id under the path comparison.</returns>
	/// <remarks>
	/// The directory path itself and its descendants are included, matching the coverage the directory reservation provides.
	/// </remarks>
	internal List<LogicalDocument> CollectDescendants(string directoryId)
	{
		AssertLockHeld();

		List<LogicalDocument> descendants = [];

		foreach (LogicalDocument document in _documents.Values)
		{
			if (LocalPath.IsDirectoryOrDescendant(document.DocumentId, directoryId, _pathComparison))
				descendants.Add(document);
		}

		descendants.Sort((left, right) => _pathComparison.Comparer.Compare(left.DocumentId, right.DocumentId));
		return descendants;
	}

	/// <summary>Finds the open reservation of an id.</summary>
	/// <param name="documentId">The normalized document id.</param>
	/// <param name="reservation">Receives the reservation of the open that loads the path.</param>
	/// <returns><see langword="true"/> when an open holds the path; otherwise, <see langword="false"/>.</returns>
	internal bool TryGetOpenReservation(string documentId, [NotNullWhen(true)] out OpenReservation? reservation)
	{
		AssertLockHeld();
		return _openReservations.TryGetValue(documentId, out reservation);
	}

	/// <summary>Finds the destination reservation of an id.</summary>
	/// <param name="documentId">The normalized document id.</param>
	/// <param name="reservation">Receives the reservation of the move or save-as that targets the path.</param>
	/// <returns><see langword="true"/> when a move or save-as targets the path; otherwise, <see langword="false"/>.</returns>
	internal bool TryGetDestinationReservation(string documentId, [NotNullWhen(true)] out DestinationReservation? reservation)
	{
		AssertLockHeld();
		return _destinationReservations.TryGetValue(documentId, out reservation);
	}

	/// <summary>Reserves a path for the open that loads it.</summary>
	/// <param name="documentId">The normalized document id.</param>
	/// <returns>The reservation the loading open completes.</returns>
	/// <exception cref="ArgumentException">An open already reserves the path.</exception>
	internal OpenReservation ReserveOpen(string documentId)
	{
		AssertLockHeld();

		OpenReservation reservation = new();
		_openReservations.Add(documentId, reservation);

		return reservation;
	}

	/// <summary>Reserves a path as the destination of a move or save-as.</summary>
	/// <param name="destinationId">The normalized destination id.</param>
	/// <returns>The reservation the operation completes.</returns>
	/// <exception cref="ArgumentException">A move or save-as already reserves the path.</exception>
	internal DestinationReservation ReserveDestination(string destinationId)
	{
		AssertLockHeld();

		DestinationReservation reservation = new(destinationId);
		_destinationReservations.Add(destinationId, reservation);

		return reservation;
	}

	/// <summary>Reserves a document for the delete that is about to remove it.</summary>
	/// <param name="document">The tracked document.</param>
	/// <returns>The reservation the delete completes.</returns>
	/// <exception cref="ArgumentException">A delete already reserves the document.</exception>
	internal DeleteReservation ReserveDelete(LogicalDocument document)
	{
		AssertLockHeld();

		DeleteReservation reservation = new();
		_deleteReservations.Add(document, reservation);

		return reservation;
	}

	/// <summary>Finds the delete reservation of a document.</summary>
	/// <param name="document">The tracked document.</param>
	/// <param name="reservation">Receives the reservation of the delete in flight.</param>
	/// <returns><see langword="true"/> when a delete is in flight for the document; otherwise, <see langword="false"/>.</returns>
	internal bool TryGetDeleteReservation(LogicalDocument document, [NotNullWhen(true)] out DeleteReservation? reservation)
	{
		AssertLockHeld();
		return _deleteReservations.TryGetValue(document, out reservation);
	}

	/// <summary>Reports whether a delete is in flight for a document.</summary>
	/// <param name="document">The tracked document.</param>
	/// <returns><see langword="true"/> when a delete reserves the document; otherwise, <see langword="false"/>.</returns>
	internal bool IsDeleteActive(LogicalDocument document)
	{
		AssertLockHeld();
		return _deleteReservations.ContainsKey(document);
	}

	/// <summary>Reports whether an open loads the path or a move or save-as targets it.</summary>
	/// <param name="documentId">The normalized document id.</param>
	/// <returns><see langword="true"/> when either reservation holds the path; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// An open that already holds a reservation for a path would add its document while a directory move runs, so the move treats the path as busy.
	/// </remarks>
	internal bool HasOpenOrDestinationReservation(string documentId)
	{
		AssertLockHeld();
		return _destinationReservations.ContainsKey(documentId) || _openReservations.ContainsKey(documentId);
	}

	/// <summary>Reports whether a reservation or a directory operation keeps a path from receiving a move or save-as now.</summary>
	/// <param name="destinationId">The normalized destination id.</param>
	/// <param name="except">A directory operation the check ignores, which is the caller's own when it is a directory operation.</param>
	/// <returns>
	/// <see langword="true"/> when the path is reserved, or lies inside a directory whose rename or recursive delete
	/// is in flight; otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// A tracked document at the path is a different outcome, so the caller checks the document table itself.
	/// </remarks>
	internal bool IsDestinationUnavailable(string destinationId, DirectoryOperationReservation? except = null)
	{
		AssertLockHeld();
		return HasOpenOrDestinationReservation(destinationId) || FindDirectoryOperationUnder(destinationId, except) is not null;
	}

	/// <summary>Returns the active directory operation whose source subtree contains a path.</summary>
	/// <param name="documentId">The normalized document id.</param>
	/// <param name="except">A directory operation the search skips, which is the caller's own when it is a directory operation.</param>
	/// <returns>The covering reservation, or <see langword="null"/> when no rename or recursive delete covers the path.</returns>
	/// <remarks>
	/// The directory path and its descendants are covered, so an open or a move that targets the exact directory id is parked behind the in-flight
	/// operation instead of proceeding against a location about to change.
	/// </remarks>
	internal DirectoryOperationReservation? FindDirectoryOperationUnder(string documentId, DirectoryOperationReservation? except = null)
	{
		AssertLockHeld();

		foreach (DirectoryOperationReservation directoryOperation in _directoryOperations)
		{
			if (ReferenceEquals(directoryOperation, except))
				continue;

			if (LocalPath.IsDirectoryOrDescendant(documentId, directoryOperation.DirectoryId, _pathComparison))
				return directoryOperation;
		}

		return null;
	}

	/// <summary>Publishes a directory operation and collects the reservations that cover its source subtree.</summary>
	/// <param name="reservation">The directory operation.</param>
	/// <param name="pendingReservations">
	/// Receives the completion tasks of the opens that load a path in the subtree and of the moves or save-as operations that write into it.
	/// </param>
	/// <remarks>
	/// The directory operation awaits the collected tasks outside the lock before its physical move or delete, so no load captures
	/// a file whose location is about to change and no write lands in a vacated subtree. Publishing and collecting are one step: an open that arrives
	/// afterwards is parked behind the operation, and a file rename or save-as into the subtree is rejected as busy, so neither needs collecting.
	/// </remarks>
	internal void BeginDirectoryOperation(DirectoryOperationReservation reservation, List<Task> pendingReservations)
	{
		AssertLockHeld();

		_directoryOperations.Add(reservation);

		string directoryId = reservation.DirectoryId;

		foreach (KeyValuePair<string, OpenReservation> entry in _openReservations)
		{
			if (LocalPath.IsDirectoryOrDescendant(entry.Key, directoryId, _pathComparison))
				pendingReservations.Add(entry.Value.Completion.Task);
		}

		foreach (KeyValuePair<string, DestinationReservation> entry in _destinationReservations)
		{
			if (LocalPath.IsDirectoryOrDescendant(entry.Key, directoryId, _pathComparison))
				pendingReservations.Add(entry.Value.Completion.Task);
		}
	}

	/// <summary>Removes an open reservation and releases the opens that wait on it.</summary>
	/// <param name="documentId">The normalized document id.</param>
	/// <param name="reservation">The reservation the open held.</param>
	/// <param name="result">The result the waiting opens observe.</param>
	/// <remarks>
	/// A newer reservation for the same path stays registered.
	/// </remarks>
	internal void CompleteOpenReservation(string documentId, OpenReservation reservation, WorkspaceDocumentOpenResult result)
	{
		AssertLockHeld();

		if (_openReservations.TryGetValue(documentId, out OpenReservation? current) && ReferenceEquals(current, reservation))
			_openReservations.Remove(documentId);

		reservation.Completion.TrySetResult(result);
	}

	/// <summary>Removes a destination reservation and releases the operations that wait on it.</summary>
	/// <param name="reservation">The reservation the move or save-as held.</param>
	/// <remarks>
	/// A newer reservation for the same path stays registered and keeps its waiters.
	/// </remarks>
	internal void CompleteDestinationReservation(DestinationReservation reservation)
	{
		AssertLockHeld();

		if (
			_destinationReservations.TryGetValue(reservation.DestinationId, out DestinationReservation? current)
			&& ReferenceEquals(current, reservation)
		)
		{
			_destinationReservations.Remove(reservation.DestinationId);
			reservation.Completion.TrySetResult();
		}
	}

	/// <summary>Removes the delete reservation of a document and releases the opens that wait on it.</summary>
	/// <param name="document">The document the delete reserved.</param>
	/// <remarks>
	/// Nothing happens when the document holds no reservation, so a caller that cannot tell whether its delete got that far
	/// completes it unconditionally. The reservation is removed before its completion task is released, and the task completes once.
	/// </remarks>
	internal void CompleteDelete(LogicalDocument document)
	{
		AssertLockHeld();

		if (_deleteReservations.Remove(document, out DeleteReservation? reservation))
			reservation.Completion.TrySetResult();
	}

	/// <summary>Removes a directory operation and releases the opens that wait on it.</summary>
	/// <param name="reservation">The reservation the directory operation held.</param>
	/// <remarks>
	/// The reservation is removed before its completion task is released, so a resumed waiter re-evaluates the state after the operation.
	/// </remarks>
	internal void CompleteDirectoryOperation(DirectoryOperationReservation reservation)
	{
		AssertLockHeld();

		_directoryOperations.Remove(reservation);
		reservation.Completion.TrySetResult();
	}

	/// <summary>Returns the completion tasks of the opens in flight.</summary>
	/// <returns>A snapshot of the tasks, which disposal awaits.</returns>
	internal Task[] GetOpenReservationTasks()
	{
		AssertLockHeld();

		var tasks = new Task[_openReservations.Count];
		int index = 0;

		foreach (OpenReservation reservation in _openReservations.Values)
			tasks[index++] = reservation.Completion.Task;

		return tasks;
	}

	/// <summary>Disposes the disk gate of every tracked document and drops the documents and the open, destination and delete reservations.</summary>
	/// <remarks>
	/// Disposal calls this once every captured reservation and operation has completed, so no caller can still acquire a gate. A document that left
	/// tracking keeps its undisposed gate, because a removal-time dispose could race a late release still in flight. The directory reservations stay,
	/// because their operations remove them when they complete, which can be after this call.
	/// </remarks>
	internal void ClearAfterDisposal()
	{
		AssertLockHeld();

		foreach (LogicalDocument document in _documents.Values)
			document.DiskOperationGate.Dispose();

		_documents.Clear();
		_openReservations.Clear();
		_destinationReservations.Clear();
		_deleteReservations.Clear();
	}

	[Conditional("DEBUG")]
	private void AssertLockHeld() => Debug.Assert(Monitor.IsEntered(_syncRoot), "The caller must hold the store's lock.");
}
