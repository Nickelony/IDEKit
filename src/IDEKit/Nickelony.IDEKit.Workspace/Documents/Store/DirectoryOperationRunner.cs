using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Workspace.FileSystem;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentPath;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentResultFactory;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Runs a directory rename or a recursive delete over every tracked document under a directory.</summary>
/// <remarks>
/// <para>
/// An operation publishes its directory reservation before it validates anything, so an open for a path in the subtree waits
/// and a file rename into the subtree reports a busy destination from then on. It acquires the disk gate of every tracked descendant without waiting,
/// registers as one admitted operation that holds them, and checks each descendant's stamp before the file system moves or deletes the tree.
/// </para>
/// <para>
/// Every exit releases what the operation published, in the order the gates require: the plan's own reservations, then the operation and its gates,
/// then the directory reservation.
/// </para>
/// </remarks>
internal sealed class DirectoryOperationRunner
{
	private readonly object _syncRoot;
	private readonly WorkspaceDocumentTracker _tracker;
	private readonly OperationGate _operationGate;
	private readonly WorkspaceDocumentStoreTestHooks _testHooks;
	private readonly LocalPathComparisonPolicy _pathComparison;
	private readonly IWorkspaceFileSystem _fileSystem;
	private readonly StampedFileWorker _io;
	private readonly StoreLifetime _lifetime;

	/// <param name="syncRoot">The store's lock, which also orders the tracker and the operation gate.</param>
	/// <param name="tracker">The tracker of the documents and the reservations.</param>
	/// <param name="operationGate">The gate that registers the operation and releases the reservations.</param>
	/// <param name="testHooks">The hooks a test uses to hold the operation at its interleaving points.</param>
	/// <param name="options">The workspace options whose path comparison decides whether two ids name one path.</param>
	/// <param name="fileSystem">The file system that moves and deletes the directory.</param>
	/// <param name="io">The file work that scans the descendant stamps and resolves the moved stamps.</param>
	/// <param name="lifetime">The store lifetime, which an operation observes besides the caller's token.</param>
	internal DirectoryOperationRunner(
		object syncRoot,
		WorkspaceDocumentTracker tracker,
		OperationGate operationGate,
		WorkspaceDocumentStoreTestHooks testHooks,
		WorkspaceOptions options,
		IWorkspaceFileSystem fileSystem,
		StampedFileWorker io,
		StoreLifetime lifetime
	)
	{
		_syncRoot = syncRoot;
		_tracker = tracker;
		_operationGate = operationGate;
		_testHooks = testHooks;
		_pathComparison = options.PathComparison;
		_fileSystem = fileSystem;
		_io = io;
		_lifetime = lifetime;
	}

	/// <summary>Renames a directory and rebases every tracked document under it.</summary>
	/// <param name="request">The rename request.</param>
	/// <param name="cancellationToken">A token that can cancel the rename.</param>
	/// <returns>The outcome and the snapshots of the affected documents.</returns>
	/// <exception cref="ObjectDisposedException">The store began stopping.</exception>
	internal async Task<WorkspaceDocumentDirectoryRenameResult> RenameAsync(
		WorkspaceDocumentDirectoryRenameRequest request,
		CancellationToken cancellationToken
	)
	{
		// Only unnormalizable paths are invalid. A destination that normalizes to the source directory
		// is a no-op that reports NoChange, compared on the normalized ids so equivalent spellings -
		// a trailing separator or a "." segment - are covered. A case-only difference is a real rename
		// handed to the file system, which completes it on a case-insensitive target via an intermediate rename.
		if (
			!LocalPath.TryNormalize(request.SourceDirectoryPath, out string sourceDirectoryId)
			|| !LocalPath.TryNormalize(request.DestinationDirectoryPath, out string destinationDirectoryId)
		)
		{
			return CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameOutcome.InvalidPath, []);
		}

		// A destination that normalizes to the source directory is already at the requested location.
		// The result mirrors the file rename NoChange outcome and carries the current snapshots; it
		// needs no directory reservation because nothing moves.
		lock (_syncRoot)
		{
			_operationGate.ThrowIfStopping();

			if (string.Equals(sourceDirectoryId, destinationDirectoryId, StringComparison.Ordinal))
			{
				return CreateDirectoryRenameResult(
					request,
					WorkspaceDocumentDirectoryRenameOutcome.NoChange,
					CreateSnapshots(_tracker.CollectDescendants(sourceDirectoryId))
				);
			}
		}

		// A canceled call returns before it publishes a reservation; the result carries the current
		// descendant snapshots like the in-progress cancellation paths below.
		if (cancellationToken.IsCancellationRequested)
		{
			lock (_syncRoot)
			{
				_operationGate.ThrowIfStopping();

				return CreateDirectoryRenameResult(
					request,
					WorkspaceDocumentDirectoryRenameOutcome.Canceled,
					CreateSnapshots(_tracker.CollectDescendants(sourceDirectoryId))
				);
			}
		}

		List<DestinationReservation> reservations = [];

		Plan<WorkspaceDocumentDirectoryRenameResult> plan = new()
		{
			Validate = (documents, ownOperation) =>
			{
				List<string> destinationIds = ComputeRebasedDestinationIds(documents, sourceDirectoryId, destinationDirectoryId);

				// The move relocates the whole subtree, so the destination is scoped as a subtree, not
				// per rebased id: any tracked document outside the moving set at or below the destination
				// directory is a collision. Rejecting before any gate is acquired keeps every document untouched.
				HashSet<LogicalDocument> movingDocuments = [.. documents];

				foreach (LogicalDocument tracked in _tracker.Documents)
				{
					if (
						!movingDocuments.Contains(tracked)
						&& LocalPath.IsDirectoryOrDescendant(tracked.DocumentId, destinationDirectoryId, _pathComparison)
					)
					{
						return CreateDirectoryRenameResult(
							request,
							WorkspaceDocumentDirectoryRenameOutcome.DestinationInUse,
							CreateSnapshots(documents)
						);
					}
				}

				// Another rename or recursive delete that covers the destination directory, or a rebased
				// path, collected its subtree before this rename reserved anything, so both operations
				// would run against the same subtree. Only this rename's own reservation is excluded: it
				// covers the destination when the destination lies inside the source directory.
				if (_tracker.FindDirectoryOperationUnder(destinationDirectoryId, except: ownOperation) is not null)
					return CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameOutcome.DestinationBusy, CreateSnapshots(documents));

				foreach (string destinationId in destinationIds)
				{
					// A reserved destination, and an open that already holds a reservation for a destination descendant, would add a document while the
					// directory move runs; the reservations created below exclude later opens.
					if (_tracker.IsDestinationUnavailable(destinationId, except: ownOperation))
					{
						return CreateDirectoryRenameResult(
							request,
							WorkspaceDocumentDirectoryRenameOutcome.DestinationBusy,
							CreateSnapshots(documents)
						);
					}
				}

				return null;
			},

			PostGateSetup = documents =>
			{
				foreach (
					string destinationId in ComputeRebasedDestinationIds(documents, sourceDirectoryId, destinationDirectoryId)
						.Distinct(_pathComparison.Comparer)
				)
				{
					DestinationReservation reservation = _tracker.ReserveDestination(destinationId);
					reservations.Add(reservation);
				}
			},

			ExecuteAsync = (documents, linkedCancellation) =>
				ExecuteDirectoryRenameAsync(
					request,
					sourceDirectoryId,
					destinationDirectoryId,
					destinationIds: ComputeRebasedDestinationIds(documents, sourceDirectoryId, destinationDirectoryId),
					documents,
					linkedCancellation
				),

			CreateAbortResult = (documents, reason, conflictDocumentId, exception) =>
				reason switch
				{
					AbortReason.Disposed => CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameOutcome.Canceled, []),

					AbortReason.Canceled => CreateDirectoryRenameResult(
						request,
						WorkspaceDocumentDirectoryRenameOutcome.Canceled,
						CreateCancellationSnapshots(documents, sourceDirectoryId)
					),

					AbortReason.OperationInProgress => CreateDirectoryRenameResult(
						request,
						WorkspaceDocumentDirectoryRenameOutcome.OperationInProgress,
						CreateSnapshots(documents)
					),

					AbortReason.ExternalFileConflict => CreateDirectoryRenameResult(
						request,
						outcome: WorkspaceDocumentDirectoryRenameOutcome.ExternalFileConflict,
						snapshots: CreateSnapshotsUnderLock(documents),

						failure: new WorkspaceOperationFailure(
							WorkspaceOperationFailureCodes.ExternalFileConflict,
							$"The tracked descendant '{conflictDocumentId}' changed before the directory rename."
						)
					),

					_ => CreateDirectoryRenameResult(
						request,
						outcome: WorkspaceDocumentDirectoryRenameOutcome.MoveFailed,
						snapshots: CreateSnapshotsUnderLock(documents),

						failure: new WorkspaceOperationFailure(
							WorkspaceOperationFailureCodes.ClassifyDirectoryOperationFailure(exception!, WorkspaceOperationFailureCodes.MoveFailed),
							exception!.Message,
							exception
						)
					),
				},

			ReleaseReservations = () =>
			{
				foreach (DestinationReservation reservation in reservations)
					_operationGate.CompleteDestinationReservation(reservation);

				reservations.Clear();
			},
		};

		return await RunAsync(sourceDirectoryId, plan, cancellationToken).ConfigureAwait(false);
	}

	// Computes the rebase targets for a directory rename in document order. The runner validates, sets
	// up reservations, and executes with the same document list, so each step computes the targets from
	// its own list instead of sharing a mutated capture; the computation is pure, so a repeated
	// validation produces the same list instead of accumulating duplicates.
	private List<string> ComputeRebasedDestinationIds(List<LogicalDocument> documents, string sourceDirectoryId, string destinationDirectoryId)
	{
		List<string> destinationIds = new(documents.Count);

		foreach (LogicalDocument document in documents)
		{
			destinationIds.Add(
				RebasePath(documentId: document.DocumentId, sourceDirectoryId, destinationDirectoryId, pathComparison: _pathComparison)
			);
		}

		return destinationIds;
	}

	/// <summary>Deletes a directory recursively and stops tracking every document under it.</summary>
	/// <param name="request">The delete request.</param>
	/// <param name="cancellationToken">A token that can cancel the delete.</param>
	/// <returns>The outcome and the snapshots of the removed documents.</returns>
	/// <exception cref="ObjectDisposedException">The store began stopping.</exception>
	internal async Task<WorkspaceDocumentDirectoryDeleteResult> DeleteAsync(
		WorkspaceDocumentDirectoryDeleteRequest request,
		CancellationToken cancellationToken
	)
	{
		if (!LocalPath.TryNormalize(request.DirectoryPath, out string directoryId))
			return CreateDirectoryDeleteResult(request, WorkspaceDocumentDirectoryDeleteOutcome.InvalidPath, []);

		// A canceled call returns before it publishes a reservation; the result carries the current
		// descendant snapshots like the in-progress cancellation paths below.
		if (cancellationToken.IsCancellationRequested)
		{
			lock (_syncRoot)
			{
				_operationGate.ThrowIfStopping();

				return CreateDirectoryDeleteResult(
					request,
					WorkspaceDocumentDirectoryDeleteOutcome.Canceled,
					CreateSnapshots(_tracker.CollectDescendants(directoryId))
				);
			}
		}

		List<LogicalDocument> reservedDocuments = [];

		Plan<WorkspaceDocumentDirectoryDeleteResult> plan = new()
		{
			Validate = (_, _) => null,

			PostGateSetup = documents =>
			{
				// The delete reservations are published only after every gate is acquired, so a failed gate acquisition
				// cannot leave an earlier document rejecting Replace with OperationInProgress. The loop runs under the
				// state lock, so Replace never observes a partially reserved batch.
				foreach (LogicalDocument document in documents)
				{
					_tracker.ReserveDelete(document);
					reservedDocuments.Add(document);
				}
			},

			ExecuteAsync = (documents, linkedCancellation) => ExecuteDirectoryDeleteAsync(request, directoryId, documents, linkedCancellation),

			CreateAbortResult = (documents, reason, conflictDocumentId, exception) =>
				reason switch
				{
					AbortReason.Disposed => CreateDirectoryDeleteResult(request, WorkspaceDocumentDirectoryDeleteOutcome.Canceled, []),

					AbortReason.Canceled => CreateDirectoryDeleteResult(
						request,
						WorkspaceDocumentDirectoryDeleteOutcome.Canceled,
						CreateCancellationSnapshots(documents, directoryId)
					),

					AbortReason.OperationInProgress => CreateDirectoryDeleteResult(
						request,
						WorkspaceDocumentDirectoryDeleteOutcome.OperationInProgress,
						CreateSnapshots(documents)
					),

					AbortReason.ExternalFileConflict => CreateDirectoryDeleteResult(
						request,
						outcome: WorkspaceDocumentDirectoryDeleteOutcome.ExternalFileConflict,
						snapshots: CreateSnapshotsUnderLock(documents),

						failure: new WorkspaceOperationFailure(
							WorkspaceOperationFailureCodes.ExternalFileConflict,
							$"The tracked descendant '{conflictDocumentId}' changed before the directory deletion."
						)
					),

					_ => CreateDirectoryDeleteResult(
						request,
						outcome: WorkspaceDocumentDirectoryDeleteOutcome.DeleteFailed,
						snapshots: CreateSnapshotsUnderLock(documents),

						failure: new WorkspaceOperationFailure(
							WorkspaceOperationFailureCodes.ClassifyDirectoryOperationFailure(exception!, WorkspaceOperationFailureCodes.DeleteFailed),
							exception!.Message,
							exception
						)
					),
				},

			ReleaseReservations = () =>
			{
				// Only the reservations this call published are completed. A collected descendant that was never
				// reserved belongs to a concurrent operation that owns its reservation, and completing that
				// reservation here would release the operation's parked open callers early.
				lock (_syncRoot)
				{
					foreach (LogicalDocument document in reservedDocuments)
						_tracker.CompleteDelete(document);

					reservedDocuments.Clear();
				}
			},
		};

		return await RunAsync(directoryId, plan, cancellationToken).ConfigureAwait(false);
	}

	// Owns the reservation/gate/registration skeleton shared by the directory rename and delete
	// operations. Each stop reason (cancellation, disposal, a held gate, a changed descendant, a
	// faulted step) maps through the plan, so no operation-specific result shape appears here.
	private async Task<TResult> RunAsync<TResult>(string directoryId, Plan<TResult> plan, CancellationToken cancellationToken)
		where TResult : class
	{
		DirectoryOperationReservation directoryOperation = new(directoryId);
		List<LogicalDocument> documents = [];
		List<FileStamp> expectedStamps = [];
		List<Task> pendingReservations = [];
		List<SemaphoreSlim> acquiredGates = [];
		OperationLease? operation = null;

		lock (_syncRoot)
		{
			_operationGate.ThrowIfStopping();

			// While the reservation is active, an open for a path inside the subtree waits for this
			// operation instead of loading a file whose location is about to change, and a file rename
			// or save-as treats the subtree as a busy destination. The subtree reservations collected
			// here are awaited below, before the physical move or delete.
			_tracker.BeginDirectoryOperation(directoryOperation, pendingReservations);
		}

		try
		{
			_testHooks.DirectoryOperationBeforeLifetimeLink?.Invoke();

			// The operation is not registered until after its post-gate setup, so disposal can dispose
			// the lifetime while this branch starts. Linking it throws ObjectDisposedException,
			// which would map to Faulted; it reports Disposed so the outcome is Canceled instead.
			using CancellationTokenSource? linkedCancellation = _lifetime.CreateLinkedCancellationOrNull(cancellationToken);

			if (linkedCancellation is null)
				return plan.CreateAbortResult(documents, AbortReason.Disposed, null, null);

			linkedCancellation.Token.ThrowIfCancellationRequested();

			// In-flight loads and writes inside the subtree must resolve before the physical move or
			// delete: a load that finished after it could capture the vacated path as a new document,
			// and a write into the subtree would target a path the operation is about to vacate.
			if (pendingReservations.Count > 0)
				await Task.WhenAll(pendingReservations).WaitAsync(linkedCancellation.Token).ConfigureAwait(false);

			lock (_syncRoot)
			{
				// Disposal cancels the lifetime token while this operation waits above; the documents
				// are already released in that case, so the operation reports Canceled instead of
				// moving or deleting a directory the store has stopped tracking.
				if (_operationGate.IsStopping)
					return plan.CreateAbortResult(documents, AbortReason.Disposed, null, null);

				// The operation applies to the whole directory on disk, so every tracked document in the
				// subtree is validated against its tracked stamp and, for a move, rebound onto the destination.
				// The subtree is collected in id order so failure and snapshot order stay deterministic; the
				// directory reservation and drained subtree reservations prove no document can appear meanwhile.
				List<LogicalDocument> trackedDescendants = _tracker.CollectDescendants(directoryId);

				foreach (LogicalDocument trackedDocument in trackedDescendants)
				{
					documents.Add(trackedDocument);
					expectedStamps.Add(trackedDocument.OnDiskStamp);
				}

				if (plan.Validate(documents, directoryOperation) is { } validationResult)
					return validationResult;

				if (!TryAcquireGates(documents, acquiredGates))
				{
					ReleaseGates(acquiredGates);
					return plan.CreateAbortResult(documents, AbortReason.OperationInProgress, null, null);
				}

				try
				{
					_testHooks.DirectoryOperationPostGateSetup?.Invoke();
					plan.PostGateSetup?.Invoke(documents);
				}
				catch
				{
					// Ownership of the acquired gates transfers to the registration only below, so a
					// throwing setup would leave every descendant's gate held and wedge it with
					// OperationInProgress for the life of the store. Release the reservations the setup
					// published and then the gates, in the order every other exit follows; the original
					// failure still propagates.
					plan.ReleaseReservations?.Invoke();
					ReleaseGates(acquiredGates);
					throw;
				}

				operation = _operationGate.Admit(acquiredGates);
			}

			List<string> documentIds = [.. documents.Select(document => document.DocumentId)];

			(string DocumentId, FileStamp Stamp)? changedDescendant = await _io.FindChangedDescendantStampAsync(
					documentIds,
					expectedStamps,
					linkedCancellation.Token
				)
				.ConfigureAwait(false);

			if (changedDescendant is { } conflict)
				return plan.CreateAbortResult(documents, AbortReason.ExternalFileConflict, conflict.DocumentId, null);

			return await plan.ExecuteAsync(documents, linkedCancellation).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			return plan.CreateAbortResult(documents, AbortReason.Canceled, null, null);
		}
		catch (Exception exception)
		{
			return plan.CreateAbortResult(documents, AbortReason.Faulted, null, exception);
		}
		finally
		{
			plan.ReleaseReservations?.Invoke();

			if (operation is not null)
				_operationGate.Complete(operation);

			_operationGate.CompleteDirectoryOperation(directoryOperation);
		}
	}

	// The operation-specific steps of one directory operation, supplied to the shared runner. The
	// runner owns the reservation/gate/registration skeleton; the plan owns only what differs between
	// a rename and a delete.
	private sealed class Plan<TResult>
		where TResult : class
	{
		/// <summary>Validates the captured descendants under the state lock and before the disk gates are taken.</summary>
		/// <remarks>
		/// Receives the operation's own directory reservation, which a check against the other active directory operations excludes.
		/// Returns the result that stops the operation, or <see langword="null"/> to proceed.
		/// </remarks>
		internal required Func<List<LogicalDocument>, DirectoryOperationReservation, TResult?> Validate { get; init; }

		/// <summary>Runs under the state lock after the disk gates are acquired, to publish the operation's reservations.</summary>
		internal Action<List<LogicalDocument>>? PostGateSetup { get; init; }

		/// <summary>Runs the operation's file-system step and tracking update and returns its result.</summary>
		internal required Func<List<LogicalDocument>, CancellationTokenSource, Task<TResult>> ExecuteAsync { get; init; }

		/// <summary>Builds the result for one of the runner's shared stop reasons.</summary>
		internal required Func<List<LogicalDocument>, AbortReason, string?, Exception?, TResult> CreateAbortResult { get; init; }

		/// <summary>Releases the operation-specific reservations before the operation completes and releases its gates.</summary>
		/// <remarks>
		/// A repeated call releases nothing more, because the skeleton calls it once for a throwing setup and once for every exit.
		/// </remarks>
		internal Action? ReleaseReservations { get; init; }
	}

	// A shared stop reason the directory runner maps through the operation's own result shape.
	private enum AbortReason
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
		CancellationTokenSource linkedCancellation
	)
	{
		// The normalized directory ids are handed to the file system, so a case-sensitive file
		// system is not at the mercy of the caller's display spelling; this batch move passes both
		// normalized directory ids, and a single rename moves to its normalized destination id the
		// same way. The result and the rebased snapshots still echo the caller-supplied paths.
		WorkspaceFileMoveResult move = await _fileSystem
			.MoveDirectoryAsync(sourceDirectoryId, destinationDirectoryId, linkedCancellation.Token)
			.ConfigureAwait(false);

		if (move.Outcome != WorkspaceFileMoveOutcome.Moved)
		{
			return CreateDirectoryRenameResult(
				request,
				outcome: WorkspaceOutcomeConversion.ToDirectoryRenameOutcome(move.Outcome),
				snapshots: CreateSnapshotsUnderLock(documents),
				failure: move.Failure
			);
		}

		// A cross-volume move may rewrite each file, so the destination stamps are re-captured before the
		// rebase; keeping the pre-move stamp would make the next write of a descendant conflict against
		// the store's own move. A capture failure falls back to the tracked stamp.
		var movedStamps = new FileStamp[documents.Count];

		for (int index = 0; index < documents.Count; index++)
		{
			movedStamps[index] = await _io.ResolveMovedStampAsync(destinationIds[index], documents[index].OnDiskStamp, linkedCancellation.Token)
				.ConfigureAwait(false);
		}

		lock (_syncRoot)
		{
			// The captured batch is complete: the directory reservation blocked new loads and writes
			// inside the subtree for the whole operation, and in-flight subtree reservations were
			// drained before the physical move. The rebased set is prepared before tracking is
			// touched, so the identity update cannot fail midway and leave a partial rebase.
			Dictionary<string, LogicalDocument> rebasedDocuments = new(_pathComparison.Comparer);

			foreach (LogicalDocument trackedDocument in _tracker.Documents)
				rebasedDocuments[trackedDocument.DocumentId] = trackedDocument;

			foreach (LogicalDocument document in documents)
				rebasedDocuments.Remove(document.DocumentId);

			for (int index = 0; index < documents.Count; index++)
			{
				LogicalDocument document = documents[index];
				string relativePath = GetRelativePathUnderDirectory(document.DocumentId, sourceDirectoryId, _pathComparison);

				document.Retarget(destinationIds[index], Path.Combine(request.DestinationDirectoryPath, relativePath), movedStamps[index]);
				rebasedDocuments[document.DocumentId] = document;
			}

			_tracker.ReplaceDocuments(rebasedDocuments);

			return CreateDirectoryRenameResult(request, WorkspaceDocumentDirectoryRenameOutcome.Renamed, CreateSnapshots(documents));
		}
	}

	private async Task<WorkspaceDocumentDirectoryDeleteResult> ExecuteDirectoryDeleteAsync(
		WorkspaceDocumentDirectoryDeleteRequest request,
		string directoryId,
		List<LogicalDocument> documents,
		CancellationTokenSource linkedCancellation
	)
	{
		// The normalized directory id is handed to the file system, matching the file delete path.
		WorkspaceFileDeleteResult deletion = await _fileSystem.DeleteDirectoryAsync(directoryId, linkedCancellation.Token).ConfigureAwait(false);

		if (deletion.Outcome != WorkspaceFileDeleteOutcome.Deleted)
		{
			return CreateDirectoryDeleteResult(
				request,
				outcome: WorkspaceOutcomeConversion.ToDirectoryDeleteOutcome(deletion.Outcome),
				snapshots: CreateSnapshotsUnderLock(documents),
				failure: deletion.Failure
			);
		}

		lock (_syncRoot)
		{
			// The captured batch is complete: the directory reservation blocked new loads and writes
			// inside the directory for the whole operation, and in-flight subtree reservations were
			// drained before the physical delete.
			foreach (LogicalDocument document in documents)
				_tracker.RemoveDocument(document.DocumentId);

			return CreateDirectoryDeleteResult(request, WorkspaceDocumentDirectoryDeleteOutcome.Deleted, CreateSnapshots(documents));
		}
	}

	private IReadOnlyList<WorkspaceDocumentSnapshot> CreateSnapshotsUnderLock(IEnumerable<LogicalDocument> documents)
	{
		lock (_syncRoot)
			return CreateSnapshots(documents);
	}

	private static void ReleaseGates(IEnumerable<SemaphoreSlim> gates)
	{
		foreach (SemaphoreSlim gate in gates.Reverse())
			gate.Release();
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

	// A cancellation that arrived before the descendant capture reports the currently tracked
	// descendants, matching the pre-canceled path; a cancellation after the capture reports the
	// captured batch like the operation's own failure paths.
	private IReadOnlyList<WorkspaceDocumentSnapshot> CreateCancellationSnapshots(List<LogicalDocument> documents, string directoryId)
	{
		lock (_syncRoot)
			return documents.Count > 0 ? CreateSnapshots(documents) : CreateSnapshots(_tracker.CollectDescendants(directoryId));
	}
}
