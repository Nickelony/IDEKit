using Nickelony.IDEKit.Workspace.Documents.FileSystem;
using System.Diagnostics;
using System.Text;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentPath;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentResultFactory;

namespace Nickelony.IDEKit.Workspace.Documents;

public sealed partial class WorkspaceDocumentStore
{
	/// <inheritdoc/>
	public async Task<WorkspaceDocumentOpenResult> OpenAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();

		// The format values are validated up front so an unencodable format cannot be stored on a
		// document and then surface only when the document is first committed.
		WorkspaceTextCodec.EnsureDefinedEncoding(options.NoBomEncoding, "options.NoBomEncoding");
		WorkspaceTextCodec.EnsureEncodable(options.NewFileFormat, "options.NewFileFormat");

		if (filePath is null || string.IsNullOrWhiteSpace(filePath))
			return new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.InvalidPath, null);

		if (!TryNormalizePath(filePath, out string documentId))
			return new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.InvalidPath, null);

		// An iteration after the first has already claimed or joined a reservation, so a call that
		// observes disposal then is in flight: it reports Canceled like every other operation that
		// disposal interrupts. The first pass has started nothing yet and keeps the
		// ObjectDisposedException contract the entry check enforces.
		bool isInFlight = false;

		while (true)
		{
			OpenLoopDecision decision;
			lock (_stateLock)
				decision = DecideOpenStep(documentId, isInFlight);

			if (decision.Result is not null)
				return decision.Result;

			Task? pendingWait = decision.PendingDelete
				?? decision.DestinationReservation?.Completion.Task
				?? decision.DirectoryOperation?.Completion.Task;

			if (pendingWait is not null)
			{
				if (!await TryWaitForResolutionAsync(pendingWait, cancellationToken).ConfigureAwait(false))
					return new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.Canceled, null);

				isInFlight = true;
				continue;
			}

			// Every path that reaches this point holds a reservation: either one that was already
			// registered for the path or the one created above for this call.
			OpenReservation reservation = decision.Reservation
				?? throw new UnreachableException("An open reservation was expected for this path.");

			if (!decision.IsLoader)
			{
				// Another open owns the load for this path: wait for its result and re-evaluate the path in
				// this loop. The wait is iterative rather than recursive, so many competing callers cannot
				// build a call stack; a loader that failed or was canceled makes this call a fresh attempt.
				WorkspaceDocumentOpenResult ownerResult;
				try
				{
					ownerResult = await reservation.Completion.Task
						.WaitAsync(cancellationToken)
						.ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					return new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.Canceled, null);
				}

				if (ownerResult.Outcome == WorkspaceDocumentOpenOutcome.Opened)
				{
					// The owner's result is not returned directly: its snapshot was captured when the load
					// completed, and a delete or a new instance may have replaced the document since then.
					// The document is re-checked under the state lock, so the caller only receives AlreadyOpen
					// while the same instance is still tracked with no delete in flight; otherwise, the loop
					// re-evaluates the path like any other waiter.
					lock (_stateLock)
					{
						if (_documents.TryGetValue(documentId, out LogicalDocument? document)
							&& !document.DeleteOperationActive)
						{
							return new WorkspaceDocumentOpenResult(
								WorkspaceDocumentOpenOutcome.AlreadyOpen,
								CreateSnapshot(document));
						}
					}
				}

				isInFlight = true;
				continue;
			}

			return await LoadReservedDocumentAsync(
				documentId,
				filePath,
				options,
				reservation,
				cancellationToken).ConfigureAwait(false);
		}
	}

	// Waits for a pending store operation - a delete, a destination reservation, a directory
	// operation, or an earlier open of the same path - to resolve. Returns false when the caller's
	// token canceled the wait; the caller reports Canceled without inspecting the other outcome.
	private static async Task<bool> TryWaitForResolutionAsync(Task pendingOperation, CancellationToken cancellationToken)
	{
		try
		{
			await pendingOperation.WaitAsync(cancellationToken).ConfigureAwait(false);
			return true;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
	}

	// Requires _stateLock. Decides what a pass of the open loop does for a path: return a completed
	// result, wait for a pending operation, or hold the reservation that makes this call the loader or
	// joins it to the owner of an existing load. The pass flags disposal once it is in flight, because
	// a later pass has already claimed or joined a reservation.
	private OpenLoopDecision DecideOpenStep(string documentId, bool isInFlight)
	{
		if (isInFlight && _disposed)
			return new OpenLoopDecision { Result = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.Canceled, null) };

		ThrowIfDisposedUnderLock();

		if (_documents.TryGetValue(documentId, out LogicalDocument? document))
		{
			// A delete holds the document gate, so operations that need that gate report
			// OperationInProgress while it is in flight. Reporting the document as already open would
			// hand the caller an instance that the in-flight delete is about to remove, so wait for the
			// delete to resolve and re-evaluate the path instead.
			if (document.DeleteOperationActive)
			{
				return new OpenLoopDecision
				{
					PendingDelete = document.DeleteCompletion?.Task
						?? throw new UnreachableException("A delete-active document must carry a delete completion.")
				};
			}

			return new OpenLoopDecision
			{
				Result = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.AlreadyOpen, CreateSnapshot(document))
			};
		}

		// The path is reserved as the destination of an in-flight move or save-as: wait for that
		// operation to resolve and re-evaluate the path.
		if (_destinationReservations.TryGetValue(documentId, out DestinationReservation? destinationReservation))
			return new OpenLoopDecision { DestinationReservation = destinationReservation };

		// Another open owns the load for this path: wait for its result instead of loading
		// the same file twice.
		if (_openReservations.TryGetValue(documentId, out OpenReservation? existingReservation))
			return new OpenLoopDecision { Reservation = existingReservation };

		// The path is inside a directory whose rename or recursive delete is in flight: wait for
		// that operation to resolve and re-evaluate the path instead of loading a file whose
		// location is about to change. A load that resumes after the operation reads the
		// post-operation state of the path - the vacated source path after a rename, for example,
		// which the open creates as a new empty document when the file is gone.
		if (FindDirectoryOperationUnder(documentId) is { } directoryOperation)
			return new OpenLoopDecision { DirectoryOperation = directoryOperation };

		OpenReservation reservation = new();
		_openReservations.Add(documentId, reservation);
		return new OpenLoopDecision { Reservation = reservation, IsLoader = true };
	}

	// The decision a pass of the open loop reaches under _stateLock: a completed result to return, a
	// pending signal to wait on, and the reservation this call holds and whether it owns the load.
	private readonly struct OpenLoopDecision
	{
		public WorkspaceDocumentOpenResult? Result { get; init; }
		public Task? PendingDelete { get; init; }
		public DestinationReservation? DestinationReservation { get; init; }
		public DirectoryOperationReservation? DirectoryOperation { get; init; }
		public OpenReservation? Reservation { get; init; }
		public bool IsLoader { get; init; }
	}

	/// <inheritdoc/>
	public bool TryGetSnapshot(string? filePath, out WorkspaceDocumentSnapshot? snapshot)
	{
		// Disposal is checked before path validity so every member follows the documented
		// ObjectDisposedException contract for a disposed store, regardless of the supplied path.
		ThrowIfDisposed();

		if (filePath is null || string.IsNullOrWhiteSpace(filePath))
		{
			snapshot = null;
			return false;
		}

		if (!TryNormalizePath(filePath, out string documentId))
		{
			snapshot = null;
			return false;
		}

		lock (_stateLock)
		{
			ThrowIfDisposedUnderLock();

			if (_documents.TryGetValue(documentId, out LogicalDocument? document))
			{
				snapshot = CreateSnapshot(document);
				return true;
			}
		}

		snapshot = null;
		return false;
	}

	/// <inheritdoc/>
	public IReadOnlyList<WorkspaceDocumentSnapshot> GetSnapshotsUnderDirectory(string? directoryPath)
	{
		ThrowIfDisposed();

		// Null and blank directory paths follow the same convention as the other path-accepting
		// members: they are treated as invalid input and return no snapshots instead of throwing.
		if (string.IsNullOrWhiteSpace(directoryPath))
			return [];

		if (!TryNormalizePath(directoryPath, out string normalizedDirectoryPath))
			return [];

		string directoryPrefix = GetDirectoryPrefix(normalizedDirectoryPath);
		StringComparison comparison = _pathComparison.Comparison;
		StringComparer comparer = _pathComparison.Comparer;

		// One pass over the tracked documents under the lock builds the result without the
		// intermediate LINQ buffers of a filter-order-project chain; the sort then runs after the lock
		// is released because the comparison is O(n log n) and reads no store state. A document tracked
		// at the exact directory id is included, matching CollectTrackedDescendants and the directory
		// reservation: a subtree rename or delete reaches it, so the reader must report it too.
		List<WorkspaceDocumentSnapshot> snapshots = [];
		lock (_stateLock)
		{
			ThrowIfDisposedUnderLock();

			foreach (LogicalDocument document in _documents.Values)
			{
				if (IsDirectoryOrDescendant(document.DocumentId, normalizedDirectoryPath, directoryPrefix, comparison))
					snapshots.Add(CreateSnapshot(document));
			}
		}

		snapshots.Sort((left, right) => comparer.Compare(left.DocumentId, right.DocumentId));
		return snapshots;
	}

	private async Task<WorkspaceDocumentOpenResult> LoadReservedDocumentAsync(
		string documentId,
		string filePath,
		WorkspaceDocumentOpenOptions options,
		OpenReservation reservation,
		CancellationToken cancellationToken)
	{
		WorkspaceDocumentOpenResult result;

		using (CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
			cancellationToken,
			_lifetimeCancellation.Token))
		{
			try
			{
				linkedCancellation.Token.ThrowIfCancellationRequested();

				WorkspaceFileReadResult file = await _fileSystem
					.ReadAsync(documentId, linkedCancellation.Token)
					.ConfigureAwait(false);

				linkedCancellation.Token.ThrowIfCancellationRequested();

				if (file.IsDirectory)
				{
					// A directory is not a document: tracking it would produce a document whose writes and
					// deletes can never succeed. The path is rejected with a dedicated outcome instead of
					// being tracked as a new empty document for a missing file.
					result = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.IsDirectory, null);
					CompleteReservation(documentId, reservation, result);
					return result;
				}

				if (!file.OnDiskStamp.Exists && !options.CreateIfMissing)
				{
					// The caller requires an existing file; opening the path as new content would
					// silently turn a missing file into a tracked document.
					result = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.NotFound, null);
					CompleteReservation(documentId, reservation, result);
					return result;
				}

				string content = file.ResolveContent(options.NewFileFormat, options.NoBomEncoding, out TextFileFormat fileFormat);

				lock (_stateLock)
				{
					if (_disposed)
					{
						result = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.Canceled, null);
					}
					else
					{
						LogicalDocument document = new(
							new WorkspaceDocumentKey(Guid.NewGuid()),
							documentId,
							filePath,
							content,
							fileFormat,
							file.OnDiskStamp,
							options.NoBomEncoding);

						// The snapshot is created before the document becomes tracked: an exception while
						// snapshotting must not leave a tracked document behind a reported load failure.
						WorkspaceDocumentSnapshot snapshot = CreateSnapshot(document);

						_documents.Add(documentId, document);
						result = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.Opened, snapshot);
					}

					CompleteReservationUnderLock(documentId, reservation, result);
				}
			}
			catch (OperationCanceledException)
			{
				result = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.Canceled, null);
				CompleteReservation(documentId, reservation, result);
			}
			catch (Exception exception)
			{
				// A permission failure is a property of the path, not of the load: it is reported as
				// AccessDenied like the write paths, so a host can tell an unfixable permission problem
				// from a transient load failure.
				string failureCode = exception switch
				{
					DecoderFallbackException => WorkspaceOperationFailureCodes.InvalidEncoding,
					UnauthorizedAccessException => WorkspaceOperationFailureCodes.AccessDenied,
					_ => WorkspaceOperationFailureCodes.LoadFailed
				};

				result = new WorkspaceDocumentOpenResult(
					WorkspaceDocumentOpenOutcome.LoadFailed,
					null,
					new WorkspaceOperationFailure(failureCode, exception.Message, exception));
				CompleteReservation(documentId, reservation, result);
			}
		}

		return result;
	}

	private void CompleteReservation(
		string documentId,
		OpenReservation reservation,
		WorkspaceDocumentOpenResult result)
	{
		lock (_stateLock)
		{
			CompleteReservationUnderLock(documentId, reservation, result);
		}
	}

	private void CompleteReservationUnderLock(
		string documentId,
		OpenReservation reservation,
		WorkspaceDocumentOpenResult result)
	{
		if (_openReservations.TryGetValue(documentId, out OpenReservation? current)
			&& ReferenceEquals(current, reservation))
		{
			_openReservations.Remove(documentId);
		}

		reservation.Completion.TrySetResult(result);
	}
}
