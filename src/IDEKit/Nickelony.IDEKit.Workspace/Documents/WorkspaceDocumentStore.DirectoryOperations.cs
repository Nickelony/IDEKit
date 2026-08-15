using Nickelony.IDEKit.Workspace.Documents.FileSystem;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentPath;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentResultFactory;

namespace Nickelony.IDEKit.Workspace.Documents;

public sealed partial class WorkspaceDocumentStore
{
	/// <inheritdoc/>
	public async Task<WorkspaceDocumentDirectoryRenameResult> RenameDirectoryAsync(
		WorkspaceDocumentDirectoryRenameRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();

		// Only unnormalizable paths are invalid here. A destination spelling that normalizes to the
		// source directory is a no-op and reports NoChange; the comparison happens on the normalized
		// ids so equivalent spellings (a trailing separator or a "." segment) are covered as well. A
		// case-only difference is a real rename and is handed to the file system, which completes it
		// on a case-insensitive target through an intermediate rename.
		if (!TryNormalizePath(request.SourceDirectoryPath, out string sourceDirectoryId)
			|| !TryNormalizePath(request.DestinationDirectoryPath, out string destinationDirectoryId))
			return CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameOutcome.InvalidPath, []);

		// A destination that normalizes to the source directory is already at the requested location.
		// The result mirrors the file rename NoChange outcome and carries the current snapshots; it
		// needs no directory reservation because nothing moves.
		lock (_stateLock)
		{
			ThrowIfDisposedUnderLock();

			if (string.Equals(sourceDirectoryId, destinationDirectoryId, StringComparison.Ordinal))
				return CreateDirectoryRenameResult(
					request,
					WorkspaceDocumentDirectoryRenameOutcome.NoChange,
					CreateSnapshots(CollectTrackedDescendants(sourceDirectoryId)));
		}

		// A canceled call returns before it publishes a reservation; the result carries the current
		// descendant snapshots like the in-progress cancellation paths below.
		if (cancellationToken.IsCancellationRequested)
		{
			lock (_stateLock)
			{
				ThrowIfDisposedUnderLock();
				return CreateDirectoryRenameResult(
					request,
					WorkspaceDocumentDirectoryRenameOutcome.Canceled,
					CreateSnapshots(CollectTrackedDescendants(sourceDirectoryId)));
			}
		}

		List<DestinationReservation> reservations = [];

		DirectoryOperationPlan<WorkspaceDocumentDirectoryRenameResult> plan = new()
		{
			Validate = documents =>
			{
				List<string> destinationIds = ComputeRebasedDestinationIds(documents, sourceDirectoryId, destinationDirectoryId);

				// The move relocates the whole subtree, so the destination is scoped as a subtree rather
				// than per rebased id: a tracked document outside the moving set that sits at the
				// destination directory or anywhere below it is a collision even when its path is not the
				// exact target of a rebased descendant. A tracked file occupying the destination path would
				// have its path turned into a directory by the move, and a tracked (possibly missing)
				// document left inside the destination subtree would describe a location the moved content
				// now shares. Rejecting before any gate is acquired keeps every tracked document untouched.
				HashSet<LogicalDocument> movingDocuments = [.. documents];
				string destinationPrefix = GetDirectoryPrefix(destinationDirectoryId);

				foreach (LogicalDocument tracked in _documents.Values)
				{
					if (!movingDocuments.Contains(tracked)
						&& IsDirectoryOrDescendant(tracked.DocumentId, destinationDirectoryId, destinationPrefix, _pathComparison.Comparison))
						return CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameOutcome.DestinationInUse, CreateSnapshots(documents));
				}

				foreach (string destinationId in destinationIds)
				{
					if (_destinationReservations.ContainsKey(destinationId))
						return CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameOutcome.DestinationBusy, CreateSnapshots(documents));

					// An open that already holds a reservation for a destination descendant would add its
					// document while the directory move runs; the reservations created below exclude later opens.
					if (_openReservations.ContainsKey(destinationId))
						return CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameOutcome.DestinationBusy, CreateSnapshots(documents));
				}

				return null;
			},
			PostGateSetup = documents =>
			{
				foreach (string destinationId in ComputeRebasedDestinationIds(documents, sourceDirectoryId, destinationDirectoryId).Distinct(_pathComparison.Comparer))
				{
					DestinationReservation reservation = new(destinationId);
					_destinationReservations.Add(destinationId, reservation);
					reservations.Add(reservation);
				}
			},
			ExecuteAsync = (documents, linkedCancellation) => ExecuteDirectoryRenameAsync(
				request,
				sourceDirectoryId,
				destinationDirectoryId,
				ComputeRebasedDestinationIds(documents, sourceDirectoryId, destinationDirectoryId),
				documents,
				linkedCancellation),
			CreateAbortResult = (documents, reason, conflictDocumentId, exception) => reason switch
			{
				DirectoryOperationAbortReason.Disposed => CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameOutcome.Canceled, []),
				DirectoryOperationAbortReason.Canceled => CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameOutcome.Canceled, CreateCancellationSnapshots(documents, sourceDirectoryId)),
				DirectoryOperationAbortReason.OperationInProgress => CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameOutcome.OperationInProgress, CreateSnapshots(documents)),
				DirectoryOperationAbortReason.ExternalFileConflict => CreateDirectoryRenameResult(
					request,
					WorkspaceDocumentDirectoryRenameOutcome.ExternalFileConflict,
					CreateSnapshotsUnderLock(documents),
					new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ExternalFileConflict, $"The tracked descendant '{conflictDocumentId}' changed before the directory rename.")),
				_ => CreateDirectoryRenameResult(
					request,
					WorkspaceDocumentDirectoryRenameOutcome.MoveFailed,
					CreateSnapshotsUnderLock(documents),
					new WorkspaceOperationFailure(CreateDirectoryOperationFailureCode(exception!, WorkspaceOperationFailureCodes.MoveFailed), exception!.Message, exception)),
			},
			ReleaseReservations = _ =>
			{
				foreach (DestinationReservation reservation in reservations)
					CompleteDestinationReservation(reservation);
			},
		};

		return await RunDirectoryOperationAsync(sourceDirectoryId, plan, cancellationToken).ConfigureAwait(false);
	}

	// Computes the rebase targets for a directory rename in document order. The driver validates, sets
	// up reservations, and executes with the same document list, so each step computes the targets from
	// its own list instead of sharing a mutated capture; the computation is pure, so a repeated
	// validation produces the same list instead of accumulating duplicates.
	private List<string> ComputeRebasedDestinationIds(
		List<LogicalDocument> documents,
		string sourceDirectoryId,
		string destinationDirectoryId)
	{
		List<string> destinationIds = new(documents.Count);
		foreach (LogicalDocument document in documents)
			destinationIds.Add(RebasePath(document.DocumentId, sourceDirectoryId, destinationDirectoryId, _pathComparison.Comparison));

		return destinationIds;
	}

	/// <inheritdoc/>
	public async Task<WorkspaceDocumentDirectoryDeleteResult> DeleteDirectoryAsync(
		WorkspaceDocumentDirectoryDeleteRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();

		if (!TryNormalizePath(request.DirectoryPath, out string directoryId))
			return CreateDirectoryDeleteResult(request, WorkspaceDocumentDirectoryDeleteOutcome.InvalidPath, []);

		// A canceled call returns before it publishes a reservation; the result carries the current
		// descendant snapshots like the in-progress cancellation paths below.
		if (cancellationToken.IsCancellationRequested)
		{
			lock (_stateLock)
			{
				ThrowIfDisposedUnderLock();
				return CreateDirectoryDeleteResult(
					request,
					WorkspaceDocumentDirectoryDeleteOutcome.Canceled,
					CreateSnapshots(CollectTrackedDescendants(directoryId)));
			}
		}

		bool deleteFlagsPublished = false;

		DirectoryOperationPlan<WorkspaceDocumentDirectoryDeleteResult> plan = new()
		{
			Validate = _ => null,
			PostGateSetup = documents =>
			{
				// The delete-active flag is published only after every gate is acquired. Setting it per
				// acquired gate would let an early return skip the finally that resets it and leave
				// earlier documents permanently rejecting Replace with OperationInProgress. Both loops
				// run under the state lock, so Replace cannot observe a partially flagged batch.
				//
				// The published flag is set before the batch loop so a setup that throws part-way still
				// resets every document through ReleaseReservations; the batch exclusively owns the
				// acquired gates, so resetting a document the loop had not reached yet is a no-op.
				deleteFlagsPublished = true;
				foreach (LogicalDocument document in documents)
				{
					document.DeleteOperationActive = true;
					document.DeleteCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				}
			},
			ExecuteAsync = (documents, linkedCancellation) => ExecuteDirectoryDeleteAsync(
				request,
				directoryId,
				documents,
				linkedCancellation),
			CreateAbortResult = (documents, reason, conflictDocumentId, exception) => reason switch
			{
				DirectoryOperationAbortReason.Disposed => CreateDirectoryDeleteResult(request, WorkspaceDocumentDirectoryDeleteOutcome.Canceled, []),
				DirectoryOperationAbortReason.Canceled => CreateDirectoryDeleteResult(request, WorkspaceDocumentDirectoryDeleteOutcome.Canceled, CreateCancellationSnapshots(documents, directoryId)),
				DirectoryOperationAbortReason.OperationInProgress => CreateDirectoryDeleteResult(request, WorkspaceDocumentDirectoryDeleteOutcome.OperationInProgress, CreateSnapshots(documents)),
				DirectoryOperationAbortReason.ExternalFileConflict => CreateDirectoryDeleteResult(
					request,
					WorkspaceDocumentDirectoryDeleteOutcome.ExternalFileConflict,
					CreateSnapshotsUnderLock(documents),
					new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ExternalFileConflict, $"The tracked descendant '{conflictDocumentId}' changed before the directory deletion.")),
				_ => CreateDirectoryDeleteResult(
					request,
					WorkspaceDocumentDirectoryDeleteOutcome.DeleteFailed,
					CreateSnapshotsUnderLock(documents),
					new WorkspaceOperationFailure(CreateDirectoryOperationFailureCode(exception!, WorkspaceOperationFailureCodes.DeleteFailed), exception!.Message, exception)),
			},
			ReleaseReservations = documents =>
			{
				if (!deleteFlagsPublished)
					return;

				// Only the flags this call published are reset. When gate acquisition failed, the
				// collected descendants belong to a concurrent operation that already owns their flags;
				// resetting them here would release its parked open callers early.
				lock (_stateLock)
				{
					foreach (LogicalDocument document in documents)
					{
						document.DeleteOperationActive = false;
						document.DeleteCompletion?.TrySetResult();
						document.DeleteCompletion = null;
					}
				}
			},
		};

		return await RunDirectoryOperationAsync(directoryId, plan, cancellationToken).ConfigureAwait(false);
	}

	// Owns the reservation/gate/registration skeleton shared by the directory rename and delete
	// operations: it publishes the directory reservation, drains the in-flight subtree reservations,
	// captures the tracked descendants in deterministic id order, runs the operation's pre-gate
	// validation, acquires every descendant's disk gate, runs the operation's post-gate setup,
	// registers the operation, verifies that no descendant changed, and then runs the operation's
	// file-system step. Cancellation, a store disposed mid-wait, a gate that is already held, a
	// changed descendant, and a faulted step all map through the plan, so no operation-specific
	// result shape appears here.
	private async Task<TResult> RunDirectoryOperationAsync<TResult>(
		string directoryId,
		DirectoryOperationPlan<TResult> plan,
		CancellationToken cancellationToken)
		where TResult : class
	{
		string directoryPrefix = GetDirectoryPrefix(directoryId);
		DirectoryOperationReservation directoryOperation = new(directoryId, directoryPrefix);
		List<LogicalDocument> documents = [];
		List<FileStamp> expectedStamps = [];
		List<Task> pendingReservations = [];
		List<SemaphoreSlim> acquiredGates = [];
		OperationRegistration? operation = null;

		lock (_stateLock)
		{
			ThrowIfDisposedUnderLock();

			// While the reservation is active, an open for a path inside the subtree waits for this
			// operation instead of loading a file whose location is about to change, and a file rename
			// or save-as treats the subtree as a busy destination. The subtree reservations collected
			// here are awaited below, before the physical move or delete.
			_directoryOperations.Add(directoryOperation);
			CollectSubtreeReservations(directoryId, pendingReservations);
		}

		try
		{
			TestHooks.DirectoryOperationBeforeLifetimeLink?.Invoke();

			// The directory operation is not registered as an active operation until after its
			// post-gate setup, so disposal can complete and release the lifetime source while this
			// branch starts. Linking to a disposed source throws ObjectDisposedException, which the
			// generic handler would map to Faulted; the documented outcome for an operation that
			// disposal interrupts is Canceled, so a released lifetime source reports Disposed instead.
			using CancellationTokenSource? linkedCancellation = TryCreateLifetimeLinkedCancellation(cancellationToken);
			if (linkedCancellation is null)
				return plan.CreateAbortResult(documents, DirectoryOperationAbortReason.Disposed, null, null);

			linkedCancellation.Token.ThrowIfCancellationRequested();

			// In-flight loads and writes inside the subtree must resolve before the physical move or
			// delete: a load that finished after it could capture the vacated path as a new document,
			// and a write into the subtree would target a path the operation is about to vacate.
			if (pendingReservations.Count > 0)
				await Task.WhenAll(pendingReservations).WaitAsync(linkedCancellation.Token).ConfigureAwait(false);

			lock (_stateLock)
			{
				// Disposal cancels the lifetime token while this operation waits above; the documents
				// are already released in that case, so the operation reports Canceled instead of
				// moving or deleting a directory the store no longer tracks.
				if (_disposed)
					return plan.CreateAbortResult(documents, DirectoryOperationAbortReason.Disposed, null, null);

				// The operation applies to the whole directory on disk, so every tracked document in the
				// subtree is part of it: each one is validated against the stamp the store currently tracks
				// and, for a move, rebound onto the destination. The subtree is collected in id order so
				// failure and snapshot order stay deterministic. The directory reservation added above
				// and the drained subtree reservations prove that no document can appear inside the
				// subtree while the operation runs.
				List<LogicalDocument> trackedDescendants = CollectTrackedDescendants(directoryId);

				foreach (LogicalDocument trackedDocument in trackedDescendants)
				{
					documents.Add(trackedDocument);
					expectedStamps.Add(trackedDocument.OnDiskStamp);
				}

				if (plan.Validate(documents) is { } validationResult)
					return validationResult;

				if (!TryAcquireGates(documents, acquiredGates))
				{
					ReleaseGates(acquiredGates);
					return plan.CreateAbortResult(documents, DirectoryOperationAbortReason.OperationInProgress, null, null);
				}

				try
				{
					TestHooks.DirectoryOperationPostGateSetup?.Invoke();
					plan.PostGateSetup?.Invoke(documents);
				}
				catch
				{
					// Ownership of the acquired gates transfers to the registration only below, so a
					// throwing setup would leave every descendant's gate held and wedge it with
					// OperationInProgress for the life of the store. Release them here; the original
					// failure still propagates and the finally still runs the plan's reservations.
					ReleaseGates(acquiredGates);
					throw;
				}

				operation = new OperationRegistration(acquiredGates);
				_activeOperations.Add(operation);
			}

			(string DocumentId, FileStamp Stamp)? changedDescendant = await FindChangedDescendantStampAsync(
				documents,
				expectedStamps,
				linkedCancellation.Token).ConfigureAwait(false);
			if (changedDescendant is { } conflict)
				return plan.CreateAbortResult(documents, DirectoryOperationAbortReason.ExternalFileConflict, conflict.DocumentId, null);

			return await plan.ExecuteAsync(documents, linkedCancellation).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return plan.CreateAbortResult(documents, DirectoryOperationAbortReason.Canceled, null, null);
		}
		catch (Exception exception)
		{
			return plan.CreateAbortResult(documents, DirectoryOperationAbortReason.Faulted, null, exception);
		}
		finally
		{
			plan.ReleaseReservations?.Invoke(documents);

			if (operation is not null)
				CompleteOperation(operation);

			CompleteDirectoryOperation(directoryOperation);
		}
	}

	// The operation-specific steps of one directory operation, supplied to the shared driver. The
	// driver owns the reservation/gate/registration skeleton; the plan owns only what differs between
	// a rename and a delete.
	private sealed class DirectoryOperationPlan<TResult>
		where TResult : class
	{
		/// <summary>
		/// Validates the captured descendants under the state lock and before the disk gates are taken.
		/// Returns the result that stops the operation, or <see langword="null"/> to proceed.
		/// </summary>
		public required Func<List<LogicalDocument>, TResult?> Validate { get; init; }

		/// <summary>
		/// Runs under the state lock after the disk gates are acquired, to publish the operation's
		/// reservations or its in-flight flags.
		/// </summary>
		public Action<List<LogicalDocument>>? PostGateSetup { get; init; }

		/// <summary>Runs the operation's file-system step and tracking update and returns its result.</summary>
		public required Func<List<LogicalDocument>, CancellationTokenSource, Task<TResult>> ExecuteAsync { get; init; }

		/// <summary>Builds the result for one of the driver's shared stop reasons.</summary>
		public required Func<List<LogicalDocument>, DirectoryOperationAbortReason, string?, Exception?, TResult> CreateAbortResult { get; init; }

		/// <summary>Releases the operation-specific reservations after the operation completes.</summary>
		public Action<List<LogicalDocument>>? ReleaseReservations { get; init; }
	}

	// A shared stop reason the directory driver maps through the operation's own result shape.
	private enum DirectoryOperationAbortReason
	{
		Disposed,
		Canceled,
		OperationInProgress,
		ExternalFileConflict,
		Faulted,
	}

	private async Task<WorkspaceDocumentDirectoryRenameResult> ExecuteDirectoryRenameAsync(
		WorkspaceDocumentDirectoryRenameRequest request,
		string sourceDirectoryId,
		string destinationDirectoryId,
		List<string> destinationIds,
		List<LogicalDocument> documents,
		CancellationTokenSource linkedCancellation)
	{
		// The normalized directory ids are handed to the file system, so a case-sensitive file
		// system is not at the mercy of the caller's display spelling; this batch move passes both
		// normalized directory ids, and a single rename moves to its normalized destination id the
		// same way. The result and the rebased snapshots still echo the caller-supplied paths.
		WorkspaceFileMoveResult move = await _fileSystem
			.MoveDirectoryAsync(sourceDirectoryId, destinationDirectoryId, linkedCancellation.Token)
			.ConfigureAwait(false);
		if (move.Outcome != WorkspaceFileMoveOutcome.Moved)
			return CreateDirectoryRenameResult(request, MapDirectoryRenameOutcome(move.Outcome), CreateSnapshotsUnderLock(documents), move.Failure);

		// The physical move may have given each file a new last-write time (a cross-volume move rewrites
		// it), so the destination stamps are re-captured before the rebase; keeping the pre-move stamp would
		// make the next write of a descendant report a spurious conflict against the store's own move. A
		// capture failure falls back to the tracked stamp, because the rebase must still complete after a
		// move that succeeded.
		FileStamp[] movedStamps = new FileStamp[documents.Count];
		for (int index = 0; index < documents.Count; index++)
			movedStamps[index] = await ResolveMovedStampAsync(destinationIds[index], documents[index].OnDiskStamp, linkedCancellation.Token).ConfigureAwait(false);

		lock (_stateLock)
		{
			// The captured batch is complete: the directory reservation blocked new loads and writes
			// inside the subtree for the whole operation, and in-flight subtree reservations were
			// drained before the physical move. The rebased set is prepared before tracking is
			// touched, so the identity update cannot fail midway and leave a partial rebase.
			Dictionary<string, LogicalDocument> rebasedDocuments = new(_pathComparison.Comparer);
			foreach (LogicalDocument trackedDocument in _documents.Values)
				rebasedDocuments[trackedDocument.DocumentId] = trackedDocument;
			foreach (LogicalDocument document in documents)
				rebasedDocuments.Remove(document.DocumentId);

			for (int index = 0; index < documents.Count; index++)
			{
				LogicalDocument document = documents[index];
				string relativePath = GetRelativePathUnderDirectory(
					document.DocumentId,
					sourceDirectoryId,
					_pathComparison.Comparison);
				document.DocumentId = destinationIds[index];
				document.DisplayPath = Path.Combine(request.DestinationDirectoryPath, relativePath);
				document.OnDiskStamp = movedStamps[index];
				document.Version++;
				rebasedDocuments[document.DocumentId] = document;
			}

			_documents.Clear();
			foreach (KeyValuePair<string, LogicalDocument> entry in rebasedDocuments)
				_documents.Add(entry.Key, entry.Value);

			return CreateDirectoryRenameResult(
				request,
				WorkspaceDocumentDirectoryRenameOutcome.Renamed,
				CreateSnapshots(documents));
		}
	}

	private async Task<WorkspaceDocumentDirectoryDeleteResult> ExecuteDirectoryDeleteAsync(
		WorkspaceDocumentDirectoryDeleteRequest request,
		string directoryId,
		List<LogicalDocument> documents,
		CancellationTokenSource linkedCancellation)
	{
		// The normalized directory id is handed to the file system, matching the file delete path.
		WorkspaceFileDeleteResult deletion = await _fileSystem
			.DeleteDirectoryAsync(directoryId, linkedCancellation.Token)
			.ConfigureAwait(false);
		if (deletion.Outcome != WorkspaceFileDeleteOutcome.Deleted)
			return CreateDirectoryDeleteResult(
				request,
				MapDirectoryDeleteOutcome(deletion.Outcome),
				CreateSnapshotsUnderLock(documents),
				deletion.Failure);

		lock (_stateLock)
		{
			// The captured batch is complete: the directory reservation blocked new loads and writes
			// inside the directory for the whole operation, and in-flight subtree reservations were
			// drained before the physical delete.
			foreach (LogicalDocument document in documents)
				_documents.Remove(document.DocumentId);

			return CreateDirectoryDeleteResult(
				request,
				WorkspaceDocumentDirectoryDeleteOutcome.Deleted,
				CreateSnapshots(documents));
		}
	}

	// Collects the tracked documents inside a directory subtree in deterministic id order. The
	// directory path itself is included as well as its descendants, matching the coverage the
	// directory reservation provides. Runs under _stateLock; the shared directory-operation driver
	// calls it before validating the batch.
	private List<LogicalDocument> CollectTrackedDescendants(string directoryId)
	{
		string directoryPrefix = GetDirectoryPrefix(directoryId);
		List<LogicalDocument> trackedDescendants = [];
		foreach (LogicalDocument trackedDocument in _documents.Values)
		{
			if (IsDirectoryOrDescendant(trackedDocument.DocumentId, directoryId, directoryPrefix, _pathComparison.Comparison))
				trackedDescendants.Add(trackedDocument);
		}

		trackedDescendants.Sort((left, right) => _pathComparison.Comparer.Compare(left.DocumentId, right.DocumentId));
		return trackedDescendants;
	}

	// Acquires every document's disk gate without waiting. On failure the caller releases the gates
	// recorded so far; both directory operations share the acquire-and-roll-back shape.
	private static bool TryAcquireGates(List<LogicalDocument> documents, List<SemaphoreSlim> acquiredGates)
	{
		foreach (LogicalDocument document in documents)
		{
			if (!document.DiskOperationGate.Wait(0, CancellationToken.None))
				return false;

			acquiredGates.Add(document.DiskOperationGate);
		}

		return true;
	}

	// Re-captures every descendant stamp and returns the first descendant whose observed stamp no
	// longer matches the stamp the store tracks, together with its document id; null when every
	// descendant is unchanged.
	private async Task<(string DocumentId, FileStamp Stamp)?> FindChangedDescendantStampAsync(
		List<LogicalDocument> documents,
		List<FileStamp> expectedStamps,
		CancellationToken cancellationToken)
	{
		for (int index = 0; index < documents.Count; index++)
		{
			FileStamp observedStamp = await _fileSystem
				.CaptureStampAsync(documents[index].DocumentId, cancellationToken)
				.ConfigureAwait(false);
			if (observedStamp != expectedStamps[index])
				return (documents[index].DocumentId, observedStamp);
		}

		return null;
	}

	// A directory operation that fails while capturing a descendant stamp reports a permission
	// failure as AccessDenied like the read paths; every other unexpected failure keeps the
	// operation's own code.
	private static string CreateDirectoryOperationFailureCode(Exception exception, string operationCode)
		=> exception is UnauthorizedAccessException
			? WorkspaceOperationFailureCodes.AccessDenied
			: operationCode;

	// A cancellation that arrived before the descendant capture reports the currently tracked
	// descendants, matching the pre-canceled path; a cancellation after the capture reports the
	// captured batch like the operation's own failure paths.
	private IReadOnlyList<WorkspaceDocumentSnapshot> CreateCancellationSnapshots(
		List<LogicalDocument> documents,
		string directoryId)
	{
		lock (_stateLock)
		{
			return documents.Count > 0
				? CreateSnapshots(documents)
				: CreateSnapshots(CollectTrackedDescendants(directoryId));
		}
	}
}
