using Nickelony.IDEKit.Workspace.Documents.FileSystem;
using System.Text;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentPath;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentResultFactory;

namespace Nickelony.IDEKit.Workspace.Documents;

public sealed partial class WorkspaceDocumentStore
{
	/// <inheritdoc/>
	public async Task<WorkspaceDocumentRenameResult> RenameAsync(
		WorkspaceDocumentRenameRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();

		// The request identity is validated before the destination path so the documented
		// ArgumentException holds even when the destination is also unnormalizable; TryBeginOperation
		// repeats the check for the shared operation preamble.
		ArgumentException.ThrowIfNullOrWhiteSpace(request.Identity.DocumentId);

		if (!TryNormalizePath(request.DestinationPath, out string destinationId))
			return CreateRenameResult(request, WorkspaceDocumentRenameOutcome.InvalidPath, null);

		DestinationReservation? reservation = null;
		OperationBegin begin = TryBeginOperation(
			request.Identity,
			captureSnapshot: false,
			preGateCheck: document =>
			{
				// The destination normalizes to the document's current id: an exactly repeated spelling is
				// a no-op, while a differently-cased spelling keeps the display-path update path below.
				if (string.Equals(document.DocumentId, destinationId, StringComparison.Ordinal))
					return OperationPreGateOutcome.NoChange;

				if (_documents.TryGetValue(destinationId, out LogicalDocument? destinationDocument)
					&& !ReferenceEquals(destinationDocument, document))
				{
					return OperationPreGateOutcome.DestinationInUse;
				}

				// A destination that is reserved, open, or inside a directory whose rename or recursive
				// delete is in flight cannot receive the move while that operation runs.
				if (_destinationReservations.ContainsKey(destinationId)
					|| _openReservations.ContainsKey(destinationId)
					|| FindDirectoryOperationUnder(destinationId) is not null)
				{
					return OperationPreGateOutcome.DestinationBusy;
				}

				return OperationPreGateOutcome.Proceed;
			},
			postGateSetup: _ =>
			{
				reservation = new DestinationReservation(destinationId);
				_destinationReservations.Add(destinationId, reservation);
			});

		if (begin is not { Succeeded: true, Document: { } document, Operation: { } operation })
		{
			if (begin.PreGateOutcome is { } preGateOutcome)
				return CreateRenameResult(request, MapRenamePreGateOutcome(preGateOutcome), begin.FailureSnapshot);

			return MapOperationBeginFailure(
				begin,
				WorkspaceDocumentRenameOutcome.DocumentNotFound,
				WorkspaceDocumentRenameOutcome.StaleDocumentInstance,
				WorkspaceDocumentRenameOutcome.StaleDocument,
				WorkspaceDocumentRenameOutcome.OperationInProgress,
				(outcome, snapshot) => CreateRenameResult(request, outcome, snapshot));
		}

		try
		{
			using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				_lifetimeCancellation.Token);
			linkedCancellation.Token.ThrowIfCancellationRequested();

			// A rename whose expected source stamp is missing retargets the document identity without a
			// file-system move: there are no bytes to move, and the destination keeps the missing stamp
			// so a later commit creates the file at the new path like any not-yet-written document.
			if (!request.ExpectedOnDiskStamp.Exists)
				return await RenameMissingSourceAsync(request, document, destinationId, linkedCancellation.Token).ConfigureAwait(false);

			WorkspaceFileMoveResult move = await _fileSystem
				.MoveAsync(
					document.DocumentId,
					destinationId,
					request.ExpectedOnDiskStamp,
					linkedCancellation.Token)
				.ConfigureAwait(false);

			if (move.Outcome != WorkspaceFileMoveOutcome.Moved)
				return CreateRenameResult(request, MapRenameOutcome(move.Outcome), CreateSnapshotUnderLock(document), move.ObservedOnDiskStamp, move.Failure);

			FileStamp movedStamp = move.ObservedOnDiskStamp
				?? await ResolveMovedStampAsync(destinationId, request.ExpectedOnDiskStamp, linkedCancellation.Token).ConfigureAwait(false);
			return CompleteRename(request, document, destinationId, movedStamp);
		}
		catch (OperationCanceledException)
		{
			return CreateRenameResult(request, WorkspaceDocumentRenameOutcome.Canceled, CreateSnapshotUnderLock(document));
		}
		catch (Exception exception)
		{
			return CreateRenameResult(
				request,
				WorkspaceDocumentRenameOutcome.MoveFailed,
				CreateSnapshotUnderLock(document),
				failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.MoveFailed, exception.Message, exception));
		}
		finally
		{
			if (reservation is not null)
				CompleteDestinationReservation(reservation);

			CompleteOperation(operation);
		}
	}

	// Retargets a document whose source file does not exist. The source is re-checked first: a file
	// that appeared since the caller's snapshot means the move has real bytes to move, and reporting
	// a conflict keeps that file from being stranded under the vacated source path. A directory that
	// now occupies the source path is likewise a conflict - it is not the missing file the retarget
	// expects, and retargeting onto the destination would strand the directory under the source id.
	// The destination must be as free as the file-system move would require it to be: an existing file
	// or a directory is a destination collision rather than a retarget onto an occupied path.
	private async Task<WorkspaceDocumentRenameResult> RenameMissingSourceAsync(
		WorkspaceDocumentRenameRequest request,
		LogicalDocument document,
		string destinationId,
		CancellationToken cancellationToken)
	{
		FileStamp sourceStamp = await _fileSystem
			.CaptureStampAsync(document.DocumentId, cancellationToken)
			.ConfigureAwait(false);
		if (sourceStamp.Exists)
			return CreateRenameResult(
				request,
				WorkspaceDocumentRenameOutcome.ExternalFileConflict,
				CreateSnapshotUnderLock(document),
				sourceStamp,
				new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ExternalFileConflict, "The source file appeared before the rename."));

		if (sourceStamp.IsDirectory)
			return CreateRenameResult(
				request,
				WorkspaceDocumentRenameOutcome.ExternalFileConflict,
				CreateSnapshotUnderLock(document),
				sourceStamp,
				new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ExternalFileConflict, "The source path exists as a directory."));

		FileStamp destinationStamp = await _fileSystem
			.CaptureStampAsync(destinationId, cancellationToken)
			.ConfigureAwait(false);
		if (destinationStamp.Exists || destinationStamp.IsDirectory)
			return CreateRenameResult(request, WorkspaceDocumentRenameOutcome.DestinationExists, CreateSnapshotUnderLock(document), destinationStamp);

		return CompleteRename(request, document, destinationId, FileStamp.Missing);
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
		FileStamp onDiskStamp)
	{
		lock (_stateLock)
		{
			_documents.Remove(document.DocumentId);
			document.DocumentId = destinationId;
			document.DisplayPath = request.DestinationPath;
			document.OnDiskStamp = onDiskStamp;
			document.Version++;
			_documents.Add(destinationId, document);

			return CreateRenameResult(
				request,
				WorkspaceDocumentRenameOutcome.Renamed,
				CreateSnapshot(document),
				document.OnDiskStamp);
		}
	}

	/// <inheritdoc/>
	public async Task<WorkspaceDocumentSaveAsResult> SaveAsAsync(
		WorkspaceDocumentSaveAsRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();

		// See RenameAsync: the identity is validated before the destination path so the documented
		// ArgumentException holds for every input combination.
		ArgumentException.ThrowIfNullOrWhiteSpace(request.Identity.DocumentId);

		if (!TryNormalizePath(request.DestinationPath, out string destinationId))
			return CreateSaveAsResult(request, WorkspaceDocumentSaveAsOutcome.InvalidPath, null);

		DestinationReservation? reservation = null;
		bool savesInPlace = false;
		OperationBegin begin = TryBeginOperation(
			request.Identity,
			captureSnapshot: true,
			preGateCheck: document =>
			{
				if (_documents.TryGetValue(destinationId, out LogicalDocument? destinationDocument))
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
				if (_destinationReservations.ContainsKey(destinationId)
					|| _openReservations.ContainsKey(destinationId)
					|| FindDirectoryOperationUnder(destinationId) is not null)
				{
					return OperationPreGateOutcome.DestinationBusy;
				}

				return OperationPreGateOutcome.Proceed;
			},
			postGateSetup: _ =>
			{
				// A save in place writes to the tracked file itself, so no destination identity is reserved
				// and no later open waits behind this operation.
				if (savesInPlace)
					return;

				reservation = new DestinationReservation(destinationId);
				_destinationReservations.Add(destinationId, reservation);
			});

		if (begin is not { Succeeded: true, Document: { } document, CapturedSnapshot: { } capturedSnapshot, Operation: { } operation })
		{
			if (begin.PreGateOutcome is { } preGateOutcome)
				return CreateSaveAsResult(request, MapSaveAsPreGateOutcome(preGateOutcome), begin.FailureSnapshot);

			return MapOperationBeginFailure(
				begin,
				WorkspaceDocumentSaveAsOutcome.DocumentNotFound,
				WorkspaceDocumentSaveAsOutcome.StaleDocumentInstance,
				WorkspaceDocumentSaveAsOutcome.StaleDocument,
				WorkspaceDocumentSaveAsOutcome.OperationInProgress,
				(outcome, snapshot) => CreateSaveAsResult(request, outcome, snapshot));
		}

		try
		{
			using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				_lifetimeCancellation.Token);
			linkedCancellation.Token.ThrowIfCancellationRequested();

			FileStamp sourceStamp = await _fileSystem
				.CaptureStampAsync(document.DocumentId, linkedCancellation.Token)
				.ConfigureAwait(false);
			if (sourceStamp != request.ExpectedOnDiskStamp)
				return CreateSaveAsResult(
					request,
					WorkspaceDocumentSaveAsOutcome.ExternalFileConflict,
					CreateSnapshotUnderLock(document),
					sourceStamp,
					new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ExternalFileConflict, "The source file changed before the save-as."));

			// A save in place replaces the source file itself, so the validated source stamp is the
			// replacement's expectation and there is no separate destination to probe. A save to a second
			// path requires the destination to be absent.
			FileStamp replacementExpectation = sourceStamp;
			if (!savesInPlace)
			{
				FileStamp destinationStamp = await _fileSystem
					.CaptureStampAsync(destinationId, linkedCancellation.Token)
					.ConfigureAwait(false);
				if (destinationStamp.Exists)
					return CreateSaveAsResult(request, WorkspaceDocumentSaveAsOutcome.DestinationExists, CreateSnapshotUnderLock(document), destinationStamp);

				replacementExpectation = FileStamp.Missing;
			}

			var (replacement, resolvedStamp, stampFailure) = await WriteReplacementAsync(
				destinationId,
				capturedSnapshot.Content,
				capturedSnapshot.FileFormat,
				replacementExpectation,
				linkedCancellation.Token).ConfigureAwait(false);

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
					_ => WorkspaceDocumentSaveAsOutcome.WriteFailed
				};

				return CreateSaveAsResult(
					request,
					failureOutcome,
					CreateSnapshotUnderLock(document),
					replacement.ObservedOnDiskStamp,
					replacement.Failure);
			}

			if (resolvedStamp is null)
			{
				return CreateSaveAsResult(
					request,
					WorkspaceDocumentSaveAsOutcome.ReplacementStateUnknown,
					CreateSnapshotUnderLock(document),
					failure: stampFailure);
			}

			return CommitSavedAs(request, document, capturedSnapshot, destinationId, resolvedStamp.Value);
		}
		catch (OperationCanceledException)
		{
			return CreateSaveAsResult(request, WorkspaceDocumentSaveAsOutcome.Canceled, CreateSnapshotUnderLock(document));
		}
		catch (Exception exception)
		{
			return CreateSaveAsResult(
				request,
				WorkspaceDocumentSaveAsOutcome.WriteFailed,
				CreateSnapshotUnderLock(document),
				failure: new WorkspaceOperationFailure(CreateWriteFailureCode(exception), exception.Message, exception));
		}
		finally
		{
			if (reservation is not null)
				CompleteDestinationReservation(reservation);

			CompleteOperation(operation);
		}
	}

	// Requires _stateLock. Retargets the document onto its save-as destination and records the written
	// baseline. For a save to a second path, the destination reservation and the pre-gate destination
	// checks prove destinationId is unoccupied for the whole operation, so the identity-path update
	// cannot orphan the document by failing the dictionary add; a save in place reuses the tracked id.
	private WorkspaceDocumentSaveAsResult CommitSavedAs(
		WorkspaceDocumentSaveAsRequest request,
		LogicalDocument document,
		WorkspaceDocumentSnapshot capturedSnapshot,
		string destinationId,
		FileStamp resolvedStamp)
	{
		_documents.Remove(document.DocumentId);
		document.DocumentId = destinationId;
		document.DisplayPath = request.DestinationPath;
		document.OnDiskStamp = resolvedStamp;
		document.PersistedContent = capturedSnapshot.Content;
		document.PersistedFileFormat = capturedSnapshot.FileFormat;
		document.Version++;

		// Follow the commit-path convention: the persisted version identifies the captured content
		// that was written and can lag the retargeted document version under a concurrent edit.
		document.PersistedVersion = capturedSnapshot.Version;
		_documents.Add(destinationId, document);
		return CreateSaveAsResult(
			request,
			WorkspaceDocumentSaveAsOutcome.SavedAs,
			CreateSnapshot(document),
			document.OnDiskStamp);
	}

	/// <inheritdoc/>
	public async Task<WorkspaceDocumentDeleteResult> DeleteAsync(
		WorkspaceDocumentDeleteRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();

		OperationBegin begin = TryBeginOperation(
			request.Identity,
			captureSnapshot: true,
			postGateSetup: document =>
			{
				document.DeleteOperationActive = true;
				document.DeleteCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			});

		if (begin is not { Succeeded: true, Document: { } document, CapturedSnapshot: { } capturedSnapshot, Operation: { } operation })
		{
			return MapOperationBeginFailure(
				begin,
				WorkspaceDocumentDeleteOutcome.DocumentNotFound,
				WorkspaceDocumentDeleteOutcome.StaleDocumentInstance,
				WorkspaceDocumentDeleteOutcome.StaleDocument,
				WorkspaceDocumentDeleteOutcome.OperationInProgress,
				(outcome, snapshot) => CreateDeleteResult(request, outcome, snapshot));
		}

		try
		{
			using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				_lifetimeCancellation.Token);
			linkedCancellation.Token.ThrowIfCancellationRequested();

			WorkspaceFileDeleteResult deletion = await _fileSystem
				.DeleteAsync(document.DocumentId, request.ExpectedOnDiskStamp, linkedCancellation.Token)
				.ConfigureAwait(false);

			if (deletion.Outcome != WorkspaceFileDeleteOutcome.Deleted)
				return CreateDeleteResult(
					request,
					deletion.Outcome == WorkspaceFileDeleteOutcome.ExternalFileConflict
						? WorkspaceDocumentDeleteOutcome.ExternalFileConflict
						: deletion.Outcome == WorkspaceFileDeleteOutcome.Canceled
							? WorkspaceDocumentDeleteOutcome.Canceled
							: WorkspaceDocumentDeleteOutcome.DeleteFailed,
					CreateSnapshotUnderLock(document),
					deletion.ObservedOnDiskStamp,
					deletion.Failure);

			lock (_stateLock)
			{
				_documents.Remove(document.DocumentId);
				return CreateDeleteResult(request, WorkspaceDocumentDeleteOutcome.Deleted, capturedSnapshot, deletion.ObservedOnDiskStamp);
			}
		}
		catch (OperationCanceledException)
		{
			return CreateDeleteResult(request, WorkspaceDocumentDeleteOutcome.Canceled, CreateSnapshotUnderLock(document));
		}
		catch (Exception exception)
		{
			return CreateDeleteResult(
				request,
				WorkspaceDocumentDeleteOutcome.DeleteFailed,
				CreateSnapshotUnderLock(document),
				failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.DeleteFailed, exception.Message, exception));
		}
		finally
		{
			lock (_stateLock)
			{
				document.DeleteOperationActive = false;
				document.DeleteCompletion?.TrySetResult();
				document.DeleteCompletion = null;
			}

			CompleteOperation(operation);
		}
	}

	// Encodes the content and hands it to the single conditional write, which owns the temporary file
	// and its cleanup. The replacement result is returned together with its resolved stamp: a completed
	// replacement whose stamp could not be established reports a null stamp and the failure that left
	// it indeterminate, and the callers map that to ReplacementStateUnknown instead of installing a
	// false baseline. The stamp is null exactly when the failure is present.
	private async Task<(
		WorkspaceFileReplacementResult Replacement,
		FileStamp? ResolvedStamp,
		WorkspaceOperationFailure? StampFailure)> WriteReplacementAsync(
		string documentId,
		string content,
		TextFileFormat fileFormat,
		FileStamp expectedStamp,
		CancellationToken cancellationToken)
	{
		// The write destination is the normalized document id rather than the caller-supplied display
		// spelling, so the write stays on the document's volume regardless of how the host spelled the
		// path; the file-system implementation chooses how to stage the bytes.
		byte[] bytes = WorkspaceTextCodec.Encode(content, fileFormat);
		WorkspaceFileReplacementResult replacement = await _fileSystem
			.WriteFileAsync(documentId, bytes, expectedStamp, cancellationToken)
			.ConfigureAwait(false);

		FileStamp? resolvedStamp = null;
		WorkspaceOperationFailure? stampFailure = null;

		if (replacement.Outcome == WorkspaceFileReplacementOutcome.Replaced)
		{
			(resolvedStamp, stampFailure) = await ResolveReplacementStampAsync(
				replacement,
				documentId,
				cancellationToken).ConfigureAwait(false);
		}

		return (replacement, resolvedStamp, stampFailure);
	}

	// The replacement completed but its final state could not be established: callers report
	// ReplacementStateUnknown rather than installing a false baseline. The re-capture exception is
	// carried through so the indeterminate state can be diagnosed after the fact.
	private static WorkspaceOperationFailure CreateUnknownReplacementStampFailure(Exception captureFailure)
	{
		const string message =
			"The replacement completed but its resulting file stamp could not be captured:";

		return new WorkspaceOperationFailure(
			WorkspaceOperationFailureCodes.ReplacementStateUnknown,
			$"{message} {captureFailure.Message}",
			captureFailure);
	}

	// A file-system implementation may complete a replacement without reporting the resulting stamp
	// (IWorkspaceFileSystem.WriteFileAsync documents the observed stamp as optional). Recording the
	// pre-write stamp or a missing stamp in that case would be wrong: the first makes the next write
	// report a spurious conflict against the store's own replacement, and the second claims a file
	// that now exists does not. Re-capture the stamp instead; when even that fails, the caller treats
	// the replacement as state-unknown rather than installing a false baseline, and the failure
	// carries the re-capture exception so the indeterminate state can be diagnosed. The stamp is null
	// exactly when the failure is present.
	private async Task<(FileStamp? Stamp, WorkspaceOperationFailure? StampFailure)> ResolveReplacementStampAsync(
		WorkspaceFileReplacementResult replacement,
		string path,
		CancellationToken cancellationToken)
	{
		if (replacement.ObservedOnDiskStamp.HasValue)
			return (replacement.ObservedOnDiskStamp, null);

		try
		{
			return (await _fileSystem.CaptureStampAsync(path, cancellationToken).ConfigureAwait(false), null);
		}
		catch (Exception exception)
		{
			// The replacement itself already completed, so a failed or canceled re-capture means the
			// stamp is unknown - not that the write was canceled.
			return (null, CreateUnknownReplacementStampFailure(exception));
		}
	}

	// A file-system implementation may complete a move without reporting the resulting stamp
	// (IWorkspaceFileSystem.MoveAsync reports the observed stamp only optionally). Recording the
	// pre-move stamp in that case is wrong when the destination received a new one - for example a
	// cross-volume move that rewrote the file - because the next write would report a spurious
	// conflict against the store's own move. Re-capture the destination stamp to install the true
	// baseline; a failed re-capture falls back to the request's expected stamp, because the identity
	// retarget must still complete after a move that succeeded.
	private async Task<FileStamp> ResolveMovedStampAsync(
		string path,
		FileStamp expectedStamp,
		CancellationToken cancellationToken)
	{
		try
		{
			return await _fileSystem.CaptureStampAsync(path, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception)
		{
			// The move already completed, so an unreadable destination must not fail the rename; the
			// identity is retargeted and the baseline degrades to the request's expectation.
			return expectedStamp;
		}
	}

	// Encoding failures are a property of the content and the selected format, not a write problem:
	// the decode paths classify the equivalent condition as InvalidEncoding, so writes do too instead
	// of reporting an unfixable encoding mismatch as a generic write failure.
	private static string CreateWriteFailureCode(Exception exception)
		=> exception is EncoderFallbackException
			? WorkspaceOperationFailureCodes.InvalidEncoding
			: WorkspaceOperationFailureCodes.WriteFailed;
}
