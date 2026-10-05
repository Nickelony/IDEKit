using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Workspace.FileSystem;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentResultFactory;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Changes where a tracked document lives: a rename moves its file, a save-as writes it to another path, and a delete removes it.</summary>
/// <remarks>
/// A rename and a save-as publish a destination reservation before they start, so an open or a move to the same path
/// waits or reports a busy destination, and a delete publishes a delete reservation for the document. Each operation releases its reservation
/// before its admission completes.
/// </remarks>
internal sealed class DocumentRelocator
{
	private readonly object _syncRoot;
	private readonly WorkspaceDocumentTracker _tracker;
	private readonly OperationGate _operationGate;
	private readonly AdmittedOperationRunner _runner;
	private readonly IWorkspaceFileSystem _fileSystem;
	private readonly StampedFileWorker _io;

	/// <param name="syncRoot">The store's lock, which orders the tracked documents.</param>
	/// <param name="tracker">The tracker of the documents and the reservations.</param>
	/// <param name="operationGate">The gate that releases the reservations an operation published.</param>
	/// <param name="runner">The runner that admits each operation.</param>
	/// <param name="fileSystem">The file system that moves, writes and deletes the files.</param>
	/// <param name="io">The file work that writes a replacement and resolves the stamp a move leaves behind.</param>
	internal DocumentRelocator(
		object syncRoot,
		WorkspaceDocumentTracker tracker,
		OperationGate operationGate,
		AdmittedOperationRunner runner,
		IWorkspaceFileSystem fileSystem,
		StampedFileWorker io
	)
	{
		_syncRoot = syncRoot;
		_tracker = tracker;
		_operationGate = operationGate;
		_runner = runner;
		_fileSystem = fileSystem;
		_io = io;
	}

	/// <summary>Moves a document's file to a destination path and retargets the document onto it.</summary>
	/// <param name="request">The rename request.</param>
	/// <param name="cancellationToken">A token that can cancel the rename.</param>
	/// <returns>The outcome and the snapshot of the document after the rename.</returns>
	/// <exception cref="ArgumentException">The request names no document id.</exception>
	/// <exception cref="ObjectDisposedException">The store began stopping.</exception>
	internal Task<WorkspaceDocumentRenameResult> RenameAsync(WorkspaceDocumentRenameRequest request, CancellationToken cancellationToken)
	{
		// The request identity is validated before the destination path so the documented
		// ArgumentException holds even when the destination is also unnormalizable; Begin
		// repeats the check for the shared operation preamble.
		ArgumentException.ThrowIfNullOrWhiteSpace(request.Identity.DocumentId);

		if (!LocalPath.TryNormalize(request.DestinationPath, out string destinationId))
			return Task.FromResult(CreateRenameResult(request, WorkspaceDocumentRenameOutcome.InvalidPath, null));

		DestinationReservation? reservation = null;

		return _runner.RunAsync(
			new AdmittedOperationDescriptor<WorkspaceDocumentRenameResult>
			{
				Identity = request.Identity,

				PreGateCheck = document =>
				{
					// The destination normalizes to the document's current id: an exactly repeated spelling is
					// a no-op, while a differently-cased spelling keeps the display-path update path below.
					if (string.Equals(document.DocumentId, destinationId, StringComparison.Ordinal))
						return OperationPreGateOutcome.NoChange;

					if (
						_tracker.TryGetDocument(destinationId, out LogicalDocument? destinationDocument)
						&& !ReferenceEquals(destinationDocument, document)
					)
					{
						return OperationPreGateOutcome.DestinationInUse;
					}

					// A destination that is reserved, open, or inside a directory whose rename or recursive
					// delete is in flight cannot receive the move while that operation runs.
					if (_tracker.IsDestinationUnavailable(destinationId))
						return OperationPreGateOutcome.DestinationBusy;

					return OperationPreGateOutcome.Proceed;
				},

				PostGateSetup = _ => reservation = _tracker.ReserveDestination(destinationId),

				CreateRefusal = begin =>
				{
					if (begin.PreGateOutcome is { } preGateOutcome)
						return CreateRenameResult(request, WorkspaceOutcomeConversion.ToRenameOutcome(preGateOutcome), begin.FailureSnapshot);

					return WorkspaceOutcomeConversion.ToFailureResult(
						begin,
						documentNotFound: WorkspaceDocumentRenameOutcome.DocumentNotFound,
						staleDocumentInstance: WorkspaceDocumentRenameOutcome.StaleDocumentInstance,
						staleDocument: WorkspaceDocumentRenameOutcome.StaleDocument,
						operationInProgress: WorkspaceDocumentRenameOutcome.OperationInProgress,
						factory: (outcome, snapshot) => CreateRenameResult(request, outcome, snapshot)
					);
				},

				ExecuteAsync = (operation, token) => RenameCoreAsync(request, document: operation.Document, destinationId, cancellationToken: token),

				CreateCanceled = document =>
					CreateRenameResult(request, WorkspaceDocumentRenameOutcome.Canceled, CreateSnapshotUnderLock(_syncRoot, document)),

				CreateFaulted = (document, exception) =>
					CreateRenameResult(
						request,
						outcome: WorkspaceDocumentRenameOutcome.MoveFailed,
						snapshot: CreateSnapshotUnderLock(_syncRoot, document),
						failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.MoveFailed, exception.Message, exception)
					),

				Release = _ =>
				{
					if (reservation is not null)
						_operationGate.CompleteDestinationReservation(reservation);
				},
			},

			cancellationToken
		);
	}

	/// <summary>Writes a document's content to a destination path and retargets the document onto it.</summary>
	/// <param name="request">The save-as request.</param>
	/// <param name="cancellationToken">A token that can cancel the save.</param>
	/// <returns>The outcome and the snapshot of the document after the save.</returns>
	/// <exception cref="ArgumentException">The request names no document id.</exception>
	/// <exception cref="ObjectDisposedException">The store began stopping.</exception>
	internal Task<WorkspaceDocumentSaveAsResult> SaveAsAsync(WorkspaceDocumentSaveAsRequest request, CancellationToken cancellationToken)
	{
		// See RenameAsync: the identity is validated before the destination path so the documented
		// ArgumentException holds for every input combination.
		ArgumentException.ThrowIfNullOrWhiteSpace(request.Identity.DocumentId);

		if (!LocalPath.TryNormalize(request.DestinationPath, out string destinationId))
			return Task.FromResult(CreateSaveAsResult(request, WorkspaceDocumentSaveAsOutcome.InvalidPath, null));

		DestinationReservation? reservation = null;
		bool savesInPlace = false;

		return _runner.RunAsync(
			new AdmittedOperationDescriptor<WorkspaceDocumentSaveAsResult>
			{
				Identity = request.Identity,

				PreGateCheck = document =>
				{
					if (_tracker.TryGetDocument(destinationId, out LogicalDocument? destinationDocument))
					{
						// A destination that resolves to the tracked document itself (including a case variant
						// under a case-insensitive policy) is a save in place: the source file is the destination,
						// so there is no second path and no destination collision.
						if (ReferenceEquals(destinationDocument, document))
						{
							savesInPlace = true;
							return OperationPreGateOutcome.Proceed;
						}

						return OperationPreGateOutcome.DestinationInUse;
					}

					// A destination that is reserved, open, or inside a directory whose rename or recursive
					// delete is in flight cannot receive the save while that operation runs.
					if (_tracker.IsDestinationUnavailable(destinationId))
						return OperationPreGateOutcome.DestinationBusy;

					return OperationPreGateOutcome.Proceed;
				},

				PostGateSetup = _ =>
				{
					// A save in place writes to the tracked file itself, so no destination identity is reserved
					// and no later open waits behind this operation.
					if (savesInPlace)
						return;

					reservation = _tracker.ReserveDestination(destinationId);
				},

				CreateRefusal = begin =>
				{
					if (begin.PreGateOutcome is { } preGateOutcome)
						return CreateSaveAsResult(request, WorkspaceOutcomeConversion.ToSaveAsOutcome(preGateOutcome), begin.FailureSnapshot);

					return WorkspaceOutcomeConversion.ToFailureResult(
						begin,
						documentNotFound: WorkspaceDocumentSaveAsOutcome.DocumentNotFound,
						staleDocumentInstance: WorkspaceDocumentSaveAsOutcome.StaleDocumentInstance,
						staleDocument: WorkspaceDocumentSaveAsOutcome.StaleDocument,
						operationInProgress: WorkspaceDocumentSaveAsOutcome.OperationInProgress,
						factory: (outcome, snapshot) => CreateSaveAsResult(request, outcome, snapshot)
					);
				},

				ExecuteAsync = (operation, token) =>
					WriteToDestinationAsync(request, operation, destinationId, savesInPlace, cancellationToken: token),

				CreateCanceled = document =>
					CreateSaveAsResult(request, WorkspaceDocumentSaveAsOutcome.Canceled, CreateSnapshotUnderLock(_syncRoot, document)),

				CreateFaulted = (document, exception) =>
					CreateSaveAsResult(
						request,
						outcome: WorkspaceDocumentSaveAsOutcome.WriteFailed,
						snapshot: CreateSnapshotUnderLock(_syncRoot, document),

						failure: new WorkspaceOperationFailure(
							WorkspaceOperationFailureCodes.ClassifyWriteFailure(exception),
							exception.Message,
							exception
						)
					),

				Release = _ =>
				{
					if (reservation is not null)
						_operationGate.CompleteDestinationReservation(reservation);
				},
			},

			cancellationToken
		);
	}

	/// <summary>Deletes a document's file and stops tracking the document.</summary>
	/// <param name="request">The delete request.</param>
	/// <param name="cancellationToken">A token that can cancel the delete.</param>
	/// <returns>The outcome and the snapshot of the document that was deleted.</returns>
	/// <exception cref="ArgumentException">The request names no document id.</exception>
	/// <exception cref="ObjectDisposedException">The store began stopping.</exception>
	internal Task<WorkspaceDocumentDeleteResult> DeleteAsync(WorkspaceDocumentDeleteRequest request, CancellationToken cancellationToken)
	{
		return _runner.RunAsync(
			new AdmittedOperationDescriptor<WorkspaceDocumentDeleteResult>
			{
				Identity = request.Identity,
				PostGateSetup = document => _tracker.ReserveDelete(document),

				CreateRefusal = begin =>
					WorkspaceOutcomeConversion.ToFailureResult(
						begin,
						documentNotFound: WorkspaceDocumentDeleteOutcome.DocumentNotFound,
						staleDocumentInstance: WorkspaceDocumentDeleteOutcome.StaleDocumentInstance,
						staleDocument: WorkspaceDocumentDeleteOutcome.StaleDocument,
						operationInProgress: WorkspaceDocumentDeleteOutcome.OperationInProgress,
						factory: (outcome, snapshot) => CreateDeleteResult(request, outcome, snapshot)
					),

				ExecuteAsync = (operation, token) => RemoveAsync(request, operation, token),

				CreateCanceled = document =>
					CreateDeleteResult(request, WorkspaceDocumentDeleteOutcome.Canceled, CreateSnapshotUnderLock(_syncRoot, document)),

				CreateFaulted = (document, exception) =>
					CreateDeleteResult(
						request,
						outcome: WorkspaceDocumentDeleteOutcome.DeleteFailed,
						snapshot: CreateSnapshotUnderLock(_syncRoot, document),
						failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.DeleteFailed, exception.Message, exception)
					),

				Release = document => _operationGate.CompleteDelete(document),
			},

			cancellationToken
		);
	}

	private async Task<WorkspaceDocumentRenameResult> RenameCoreAsync(
		WorkspaceDocumentRenameRequest request,
		LogicalDocument document,
		string destinationId,
		CancellationToken cancellationToken
	)
	{
		// A rename whose expected source stamp is missing retargets the document identity without a
		// file-system move: there are no bytes to move, and the destination keeps the missing stamp
		// so a later commit creates the file at the new path like any not-yet-written document.
		if (!request.ExpectedOnDiskStamp.Exists)
			return await RenameMissingSourceAsync(request, document, destinationId, cancellationToken).ConfigureAwait(false);

		WorkspaceFileMoveResult move = await _fileSystem
			.MoveAsync(
				sourcePath: document.DocumentId,
				destinationPath: destinationId,
				expectedSourceStamp: request.ExpectedOnDiskStamp,
				cancellationToken
			)
			.ConfigureAwait(false);

		if (move.Outcome != WorkspaceFileMoveOutcome.Moved)
		{
			return CreateRenameResult(
				request,
				outcome: WorkspaceOutcomeConversion.ToRenameOutcome(move.Outcome),
				snapshot: CreateSnapshotUnderLock(_syncRoot, document),
				observedOnDiskStamp: move.ObservedOnDiskStamp,
				failure: move.Failure
			);
		}

		FileStamp movedStamp =
			move.ObservedOnDiskStamp
			?? await _io.ResolveMovedStampAsync(destinationId, request.ExpectedOnDiskStamp, cancellationToken).ConfigureAwait(false);

		return CompleteRename(request, document, destinationId, onDiskStamp: movedStamp);
	}

	// Retargets a document whose source file does not exist. The source is re-checked: a file or
	// directory that appeared since the caller's snapshot is an ExternalFileConflict (retargeting
	// would strand it under the vacated id), and an occupied destination is a collision, not a target.
	private async Task<WorkspaceDocumentRenameResult> RenameMissingSourceAsync(
		WorkspaceDocumentRenameRequest request,
		LogicalDocument document,
		string destinationId,
		CancellationToken cancellationToken
	)
	{
		FileStamp sourceStamp = await _fileSystem.CaptureStampAsync(document.DocumentId, cancellationToken).ConfigureAwait(false);

		if (sourceStamp.Exists)
		{
			return CreateRenameResult(
				request,
				outcome: WorkspaceDocumentRenameOutcome.ExternalFileConflict,
				snapshot: CreateSnapshotUnderLock(_syncRoot, document),
				observedOnDiskStamp: sourceStamp,

				failure: new WorkspaceOperationFailure(
					WorkspaceOperationFailureCodes.ExternalFileConflict,
					"The source file appeared before the rename."
				)
			);
		}

		if (sourceStamp.IsDirectory)
		{
			return CreateRenameResult(
				request,
				outcome: WorkspaceDocumentRenameOutcome.ExternalFileConflict,
				snapshot: CreateSnapshotUnderLock(_syncRoot, document),
				observedOnDiskStamp: sourceStamp,
				failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ExternalFileConflict, "The source path exists as a directory.")
			);
		}

		FileStamp destinationStamp = await _fileSystem.CaptureStampAsync(destinationId, cancellationToken).ConfigureAwait(false);

		if (destinationStamp.Exists || destinationStamp.IsDirectory)
		{
			return CreateRenameResult(
				request,
				outcome: WorkspaceDocumentRenameOutcome.DestinationExists,
				snapshot: CreateSnapshotUnderLock(_syncRoot, document),
				observedOnDiskStamp: destinationStamp
			);
		}

		return CompleteRename(request, document, destinationId, onDiskStamp: FileStamp.Missing);
	}

	// Updates the tracked identity of a document whose file already reached the destination path; a
	// missing-source retarget arrives here with the missing stamp. The destination reservation,
	// created under the state lock before the operation, excluded tracked documents, destination
	// reservations, and in-flight open reservations for the destination, so the identity-path update
	// cannot orphan the document by failing the dictionary add.
	private WorkspaceDocumentRenameResult CompleteRename(
		WorkspaceDocumentRenameRequest request,
		LogicalDocument document,
		string destinationId,
		FileStamp onDiskStamp
	)
	{
		lock (_syncRoot)
		{
			_tracker.RemoveDocument(document.DocumentId);
			document.Retarget(destinationId, request.DestinationPath, onDiskStamp);
			_tracker.AddDocument(destinationId, document);

			return CreateRenameResult(
				request,
				outcome: WorkspaceDocumentRenameOutcome.Renamed,
				snapshot: CreateSnapshot(document),
				observedOnDiskStamp: document.OnDiskStamp
			);
		}
	}

	private async Task<WorkspaceDocumentSaveAsResult> WriteToDestinationAsync(
		WorkspaceDocumentSaveAsRequest request,
		AdmittedOperation operation,
		string destinationId,
		bool savesInPlace,
		CancellationToken cancellationToken
	)
	{
		LogicalDocument document = operation.Document;
		WorkspaceDocumentSnapshot capturedSnapshot = operation.CapturedSnapshot;

		FileStamp sourceStamp = await _fileSystem.CaptureStampAsync(document.DocumentId, cancellationToken).ConfigureAwait(false);

		if (sourceStamp != request.ExpectedOnDiskStamp)
		{
			return CreateSaveAsResult(
				request,
				outcome: WorkspaceDocumentSaveAsOutcome.ExternalFileConflict,
				snapshot: CreateSnapshotUnderLock(_syncRoot, document),
				observedOnDiskStamp: sourceStamp,

				failure: new WorkspaceOperationFailure(
					WorkspaceOperationFailureCodes.ExternalFileConflict,
					"The source file changed before the save-as."
				)
			);
		}

		// A save in place replaces the source file itself, so the validated source stamp is the
		// replacement's expectation and there is no separate destination to probe. A save to a second
		// path requires the destination to be absent.
		FileStamp replacementExpectation = sourceStamp;

		if (!savesInPlace)
		{
			FileStamp destinationStamp = await _fileSystem.CaptureStampAsync(destinationId, cancellationToken).ConfigureAwait(false);

			if (destinationStamp.Exists)
			{
				return CreateSaveAsResult(
					request,
					outcome: WorkspaceDocumentSaveAsOutcome.DestinationExists,
					snapshot: CreateSnapshotUnderLock(_syncRoot, document),
					observedOnDiskStamp: destinationStamp
				);
			}

			replacementExpectation = FileStamp.Missing;
		}

		(WorkspaceFileReplacementResult? replacement, FileStamp? resolvedStamp, WorkspaceOperationFailure? stampFailure) =
			await _io.WriteReplacementAsync(
					documentId: destinationId,
					content: capturedSnapshot.Content,
					fileFormat: capturedSnapshot.FileFormat,
					expectedStamp: replacementExpectation,
					cancellationToken
				)
				.ConfigureAwait(false);

		if (replacement.Outcome != WorkspaceFileReplacementOutcome.Replaced)
		{
			// A conflict from the conditional replacement is a destination conflict for a save to a
			// second path; a save in place re-validated the source file itself, so the same outcome
			// means the tracked file changed again.
			WorkspaceDocumentSaveAsOutcome failureOutcome = replacement.Outcome switch
			{
				WorkspaceFileReplacementOutcome.ExternalFileConflict => savesInPlace
					? WorkspaceDocumentSaveAsOutcome.ExternalFileConflict
					: WorkspaceDocumentSaveAsOutcome.DestinationExists,

				WorkspaceFileReplacementOutcome.DestinationExists => WorkspaceDocumentSaveAsOutcome.DestinationExists,
				WorkspaceFileReplacementOutcome.Canceled => WorkspaceDocumentSaveAsOutcome.Canceled,
				WorkspaceFileReplacementOutcome.ReplacementStateUnknown => WorkspaceDocumentSaveAsOutcome.ReplacementStateUnknown,
				_ => WorkspaceDocumentSaveAsOutcome.WriteFailed,
			};

			return CreateSaveAsResult(
				request,
				outcome: failureOutcome,
				snapshot: CreateSnapshotUnderLock(_syncRoot, document),
				observedOnDiskStamp: replacement.ObservedOnDiskStamp,
				failure: replacement.Failure
			);
		}

		if (resolvedStamp is null)
		{
			return CreateSaveAsResult(
				request,
				outcome: WorkspaceDocumentSaveAsOutcome.ReplacementStateUnknown,
				snapshot: CreateSnapshotUnderLock(_syncRoot, document),
				failure: stampFailure
			);
		}

		return CommitSavedAs(request, document, capturedSnapshot, destinationId, resolvedStamp: resolvedStamp.Value);
	}

	// Retargets the document onto its save-as destination and records the written baseline under the
	// state lock, because the write that precedes it ran outside the lock. For a save to a second path,
	// the destination reservation and the pre-gate destination checks prove destinationId is unoccupied
	// for the whole operation, so the identity-path update cannot orphan the document by failing the
	// dictionary add; a save in place reuses the tracked id.
	private WorkspaceDocumentSaveAsResult CommitSavedAs(
		WorkspaceDocumentSaveAsRequest request,
		LogicalDocument document,
		WorkspaceDocumentSnapshot capturedSnapshot,
		string destinationId,
		FileStamp resolvedStamp
	)
	{
		lock (_syncRoot)
		{
			_tracker.RemoveDocument(document.DocumentId);
			document.Retarget(destinationId, request.DestinationPath, resolvedStamp);

			document.InstallBaseline(
				content: capturedSnapshot.Content,
				fileFormat: capturedSnapshot.FileFormat,
				version: capturedSnapshot.Version,
				onDiskStamp: resolvedStamp
			);

			_tracker.AddDocument(destinationId, document);

			return CreateSaveAsResult(
				request,
				outcome: WorkspaceDocumentSaveAsOutcome.SavedAs,
				snapshot: CreateSnapshot(document),
				observedOnDiskStamp: document.OnDiskStamp
			);
		}
	}

	private async Task<WorkspaceDocumentDeleteResult> RemoveAsync(
		WorkspaceDocumentDeleteRequest request,
		AdmittedOperation operation,
		CancellationToken cancellationToken
	)
	{
		LogicalDocument document = operation.Document;

		WorkspaceFileDeleteResult deletion = await _fileSystem
			.DeleteAsync(document.DocumentId, request.ExpectedOnDiskStamp, cancellationToken)
			.ConfigureAwait(false);

		if (deletion.Outcome != WorkspaceFileDeleteOutcome.Deleted)
		{
			return CreateDeleteResult(
				request,

				outcome: deletion.Outcome == WorkspaceFileDeleteOutcome.ExternalFileConflict ? WorkspaceDocumentDeleteOutcome.ExternalFileConflict
					: deletion.Outcome == WorkspaceFileDeleteOutcome.Canceled ? WorkspaceDocumentDeleteOutcome.Canceled
					: WorkspaceDocumentDeleteOutcome.DeleteFailed,

				snapshot: CreateSnapshotUnderLock(_syncRoot, document),
				observedOnDiskStamp: deletion.ObservedOnDiskStamp,
				failure: deletion.Failure
			);
		}

		lock (_syncRoot)
		{
			_tracker.RemoveDocument(document.DocumentId);

			return CreateDeleteResult(
				request,
				outcome: WorkspaceDocumentDeleteOutcome.Deleted,
				snapshot: operation.CapturedSnapshot,
				observedOnDiskStamp: deletion.ObservedOnDiskStamp
			);
		}
	}
}
