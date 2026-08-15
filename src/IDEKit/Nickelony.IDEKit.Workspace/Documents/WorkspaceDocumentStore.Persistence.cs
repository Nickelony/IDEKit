using Nickelony.IDEKit.Workspace.Documents.FileSystem;
using System.Text;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentResultFactory;

namespace Nickelony.IDEKit.Workspace.Documents;

public sealed partial class WorkspaceDocumentStore
{
	/// <inheritdoc/>
	public WorkspaceDocumentMutationResult Replace(WorkspaceDocumentReplaceRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();
		ArgumentException.ThrowIfNullOrWhiteSpace(request.Identity.DocumentId);
		ArgumentNullException.ThrowIfNull(request.Content);

		// The shared codec validation rejects an undefined encoding and the Windows-1252 and
		// byte-order mark combination here so an unencodable format never reaches the tracked document,
		// where every later commit would fail.
		WorkspaceTextCodec.EnsureEncodable(request.FileFormat, "request.FileFormat");

		lock (_stateLock)
		{
			ThrowIfDisposedUnderLock();

			if (!_documents.TryGetValue(request.Identity.DocumentId, out LogicalDocument? document))
				return CreateMutationResult(request, WorkspaceDocumentMutationOutcome.DocumentNotFound, null);

			if (document.DocumentKey != request.Identity.DocumentKey)
				return CreateMutationResult(
					request,
					WorkspaceDocumentMutationOutcome.StaleDocumentInstance,
					CreateSnapshot(document));

			if (document.Version != request.Identity.Version)
				return CreateMutationResult(
					request,
					WorkspaceDocumentMutationOutcome.StaleDocument,
					CreateSnapshot(document));

			if (document.DeleteOperationActive)
				return CreateMutationResult(
					request,
					WorkspaceDocumentMutationOutcome.OperationInProgress,
					CreateSnapshot(document));

			if (string.Equals(document.Content, request.Content, StringComparison.Ordinal)
				&& document.FileFormat == request.FileFormat)
			{
				return CreateMutationResult(
					request,
					WorkspaceDocumentMutationOutcome.NoChange,
					CreateSnapshot(document));
			}

			document.Content = request.Content;
			document.FileFormat = request.FileFormat;
			document.Version++;

			return CreateMutationResult(
				request,
				WorkspaceDocumentMutationOutcome.Changed,
				CreateSnapshot(document));
		}
	}

	/// <inheritdoc/>
	public WorkspaceDocumentMutationResult Discard(WorkspaceDocumentDiscardRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();
		ArgumentException.ThrowIfNullOrWhiteSpace(request.Identity.DocumentId);

		lock (_stateLock)
		{
			ThrowIfDisposedUnderLock();

			if (!_documents.TryGetValue(request.Identity.DocumentId, out LogicalDocument? document))
				return CreateMutationResult(request, WorkspaceDocumentMutationOutcome.DocumentNotFound, null);

			if (document.DocumentKey != request.Identity.DocumentKey)
				return CreateMutationResult(
					request,
					WorkspaceDocumentMutationOutcome.StaleDocumentInstance,
					CreateSnapshot(document));

			if (document.Version != request.Identity.Version)
				return CreateMutationResult(
					request,
					WorkspaceDocumentMutationOutcome.StaleDocument,
					CreateSnapshot(document));

			// Gate transitions happen under the state lock, so the gate's count is an exact probe and
			// the gate does not need to be acquired and released to observe it.
			if (document.DiskOperationGate.CurrentCount == 0)
				return CreateMutationResult(
					request,
					WorkspaceDocumentMutationOutcome.OperationInProgress,
					CreateSnapshot(document));

			if (!document.IsDirty)
				return CreateMutationResult(
					request,
					WorkspaceDocumentMutationOutcome.NoChange,
					CreateSnapshot(document));

			document.Content = document.PersistedContent;
			document.FileFormat = document.PersistedFileFormat;
			document.Version++;
			document.PersistedVersion = document.Version;

			return CreateMutationResult(
				request,
				WorkspaceDocumentMutationOutcome.Changed,
				CreateSnapshot(document));
		}
	}

	/// <inheritdoc/>
	public Task<WorkspaceDocumentCommitResult> CommitAsync(
		WorkspaceDocumentCommitRequest request,
		CancellationToken cancellationToken = default)
		=> CommitCoreAsync(request, forceWrite: false, cancellationToken);

	private async Task<WorkspaceDocumentCommitResult> CommitCoreAsync(
		WorkspaceDocumentCommitRequest request,
		bool forceWrite,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();

		OperationBegin begin = TryBeginOperation(
			request.Identity,
			captureSnapshot: true);

		if (begin is not { Succeeded: true, Document: { } document, CapturedSnapshot: { } capturedSnapshot, Operation: { } operation })
		{
			return MapOperationBeginFailure(
				begin,
				WorkspaceDocumentCommitOutcome.DocumentNotFound,
				WorkspaceDocumentCommitOutcome.StaleDocumentInstance,
				WorkspaceDocumentCommitOutcome.StaleDocument,
				WorkspaceDocumentCommitOutcome.OperationInProgress,
				(outcome, snapshot) => CreateCommitResult(request, outcome, snapshot));
		}

		try
		{
			using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				_lifetimeCancellation.Token);
			linkedCancellation.Token.ThrowIfCancellationRequested();

			// The expected on-disk stamp is the caller's precondition; the conditional replacement
			// validates it against its own capture of the destination, so an accepted commit that writes
			// reads and hashes the file once. A clean tracked document whose file exists has nothing to
			// write: the commit is a no-op without re-reading the file, so the caller's expectation is
			// validated against the tracked stamp instead - a comparison that catches an expectation
			// which disagrees with the state the store last observed. A later write that does occur
			// still validates the expectation against a fresh capture before replacing.
			if (!forceWrite && !capturedSnapshot.IsDirty && capturedSnapshot.ExistsOnDisk)
				return ResolveCleanCommitNoOp(request, document, capturedSnapshot);

			var (replacement, resolvedStamp, stampFailure) = await WriteReplacementAsync(
				capturedSnapshot.DocumentId,
				capturedSnapshot.Content,
				capturedSnapshot.FileFormat,
				request.ExpectedOnDiskStamp,
				linkedCancellation.Token).ConfigureAwait(false);

			if (replacement.Outcome == WorkspaceFileReplacementOutcome.Replaced)
			{
				if (resolvedStamp is null)
				{
					return CreateCurrentCommitResult(
						request,
						document,
						WorkspaceDocumentCommitOutcome.ReplacementStateUnknown,
						null,
						stampFailure);
				}

				return InstallCommittedBaseline(request, document, capturedSnapshot, resolvedStamp.Value);
			}

			return replacement.Outcome switch
			{
				WorkspaceFileReplacementOutcome.ExternalFileConflict => UpdateObservedConflict(
					request,
					document,
					replacement.ObservedOnDiskStamp),
				WorkspaceFileReplacementOutcome.Canceled => CreateCommitResult(
					request,
					WorkspaceDocumentCommitOutcome.Canceled,
					CreateSnapshotUnderLock(document)),
				WorkspaceFileReplacementOutcome.DestinationExists => CreateCurrentCommitResult(
					request,
					document,
					WorkspaceDocumentCommitOutcome.WriteFailed,
					replacement.ObservedOnDiskStamp,
					replacement.Failure),
				WorkspaceFileReplacementOutcome.ReplacementStateUnknown => CreateCurrentCommitResult(
					request,
					document,
					WorkspaceDocumentCommitOutcome.ReplacementStateUnknown,
					replacement.ObservedOnDiskStamp,
					replacement.Failure),
				_ => CreateCurrentCommitResult(
					request,
					document,
					WorkspaceDocumentCommitOutcome.WriteFailed,
					replacement.ObservedOnDiskStamp,
					replacement.Failure)
			};
		}
		catch (OperationCanceledException)
		{
			lock (_stateLock)
				return CreateCommitResult(
					request,
					WorkspaceDocumentCommitOutcome.Canceled,
					CreateSnapshot(document));
		}
		catch (Exception exception)
		{
			lock (_stateLock)
				return CreateCommitResult(
					request,
					WorkspaceDocumentCommitOutcome.WriteFailed,
					CreateSnapshot(document),
					failure: new WorkspaceOperationFailure(CreateWriteFailureCode(exception), exception.Message, exception));
		}
		finally
		{
			CompleteOperation(operation);
		}
	}

	/// <inheritdoc/>
	public async Task<WorkspaceDocumentReloadResult> ReloadAsync(
		WorkspaceDocumentReloadRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();

		// The dirty branch resolves the reload from file stamps alone, so it bypasses the
		// per-document disk gate instead of waiting behind an in-flight write.
		OperationBegin begin = TryBeginOperation(
			request.Identity,
			captureSnapshot: true,
			skipGateWhen: static (_, snapshot) => snapshot?.IsDirty == true);

		if (begin is not { Succeeded: true, Document: { } document, CapturedSnapshot: { } capturedSnapshot })
		{
			return MapOperationBeginFailure(
				begin,
				WorkspaceDocumentReloadOutcome.DocumentNotFound,
				WorkspaceDocumentReloadOutcome.StaleDocumentInstance,
				WorkspaceDocumentReloadOutcome.StaleDocument,
				WorkspaceDocumentReloadOutcome.OperationInProgress,
				(outcome, snapshot) => CreateReloadResult(request, outcome, snapshot));
		}

		if (capturedSnapshot.IsDirty)
		{
			// The dirty branch is not registered as an active operation, so disposal can complete
			// between the operation preamble and this point. Linking against the lifetime source of a
			// disposed store throws ObjectDisposedException; the documented outcome for an in-flight
			// dirty reload is cancellation, so an already-completed disposal reports that instead.
			using CancellationTokenSource? linkedCancellation = TryCreateLifetimeLinkedCancellation(cancellationToken);
			if (linkedCancellation is null)
				return CreateReloadResult(
					request,
					WorkspaceDocumentReloadOutcome.Canceled,
					capturedSnapshot);

			try
			{
				FileStamp observedStamp = await _fileSystem
					.CaptureStampAsync(capturedSnapshot.DocumentId, linkedCancellation.Token)
					.ConfigureAwait(false);

				lock (_stateLock)
					return ResolveDirtyReload(request, document, observedStamp);
			}
			catch (OperationCanceledException)
			{
				return CreateReloadResult(
					request,
					WorkspaceDocumentReloadOutcome.Canceled,
					capturedSnapshot);
			}
			catch (Exception exception)
			{
				return CreateReloadResult(
					request,
					WorkspaceDocumentReloadOutcome.ReadFailed,
					capturedSnapshot,
					failure: new WorkspaceOperationFailure(CreateReadFailureCode(exception), exception.Message, exception));
			}
		}

		return await ReloadCleanAsync(request, document, capturedSnapshot, begin.Operation, cancellationToken).ConfigureAwait(false);
	}

	// Requires _stateLock. Maps the stamp captured for a dirty reload to the reload outcome, re-validating
	// disposal and that the tracked instance is still the one the request named before it reports or
	// adopts the observation. The dirty branch holds no disk gate, so any of those states can change
	// while the stamp capture is in flight.
	private WorkspaceDocumentReloadResult ResolveDirtyReload(
		WorkspaceDocumentReloadRequest request,
		LogicalDocument document,
		FileStamp observedStamp)
	{
		// Dirty reloads bypass the per-document disk gate and are not registered as active operations,
		// so disposal can complete while the stamp capture is in flight. Re-check disposal
		// before mutating the (possibly detached) document or returning its snapshot.
		if (_disposed)
			return CreateReloadResult(
				request,
				WorkspaceDocumentReloadOutcome.Canceled,
				CreateSnapshot(document),
				observedStamp);

		// Deletion or replacement of the tracked instance can also complete while the stamp capture
		// is in flight because the dirty branch holds no gate. The captured instance is no longer
		// authoritative when the id maps to a different instance or to nothing at all.
		if (!_documents.TryGetValue(request.Identity.DocumentId, out LogicalDocument? trackedDocument))
		{
			// The instance can also be retargeted by a rename or save-as while the capture is in
			// flight: the id no longer resolves, but the instance key still does. Report the stale
			// instance with its current snapshot instead of a not-found that the caller cannot
			// distinguish from a deleted document.
			trackedDocument = FindDocumentByKey(request.Identity.DocumentKey);
			if (trackedDocument is null)
			{
				return CreateReloadResult(
					request,
					WorkspaceDocumentReloadOutcome.DocumentNotFound,
					null,
					observedStamp);
			}

			return CreateReloadResult(
				request,
				WorkspaceDocumentReloadOutcome.StaleDocumentInstance,
				CreateSnapshot(trackedDocument),
				observedStamp);
		}

		if (!ReferenceEquals(trackedDocument, document))
			return CreateReloadResult(
				request,
				WorkspaceDocumentReloadOutcome.StaleDocumentInstance,
				CreateSnapshot(trackedDocument),
				observedStamp);

		// A delete that started after the preamble check holds the gate and is about to remove
		// the document; the dirty branch holds no gate, so it re-checks the delete state before
		// comparing stamps and reporting an outcome that describes a document being removed.
		if (document.DeleteOperationActive)
			return CreateReloadResult(
				request,
				WorkspaceDocumentReloadOutcome.OperationInProgress,
				CreateSnapshot(document),
				observedStamp);

		if (document.Version != request.Identity.Version)
			return CreateReloadResult(
				request,
				WorkspaceDocumentReloadOutcome.StaleDocument,
				CreateSnapshot(document),
				observedStamp);

		// Nothing changed externally since the store last observed the file: a dirty document has
		// nothing to reload, and a spurious watcher event must not raise a conflict prompt.
		if (observedStamp == document.OnDiskStamp)
			return CreateReloadResult(
				request,
				WorkspaceDocumentReloadOutcome.Unchanged,
				CreateSnapshot(document),
				observedStamp);

		// The observed stamp is not installed on the document: a commit (including its failure
		// paths) can install a newer stamp without advancing the version, and this branch holds
		// no gate, so writing the observation here could replace a newer record with a stale one.
		// Callers resolve the conflict with the stamp carried by this result.
		return CreateReloadResult(
			request,
			WorkspaceDocumentReloadOutcome.ExternalFileConflict,
			CreateSnapshot(document),
			observedStamp);
	}

	// Reloads a clean document by reading its content under the per-document disk gate and installing
	// the read as the new persisted baseline.
	private async Task<WorkspaceDocumentReloadResult> ReloadCleanAsync(
		WorkspaceDocumentReloadRequest request,
		LogicalDocument document,
		WorkspaceDocumentSnapshot capturedSnapshot,
		OperationRegistration? operation,
		CancellationToken cancellationToken)
	{
		try
		{
			DocumentReadOutcome read = await ReadDocumentFileAsync(
				capturedSnapshot.DocumentId,
				cancellationToken).ConfigureAwait(false);

			if (read.IsCanceled)
			{
				lock (_stateLock)
					return CreateReloadResult(
						request,
						WorkspaceDocumentReloadOutcome.Canceled,
						CreateSnapshot(document));
			}

			if (read.Failure is not null)
			{
				lock (_stateLock)
					return CreateReloadResult(
						request,
						WorkspaceDocumentReloadOutcome.ReadFailed,
						CreateSnapshot(document),
						failure: read.Failure);
			}

			WorkspaceFileReadResult file = read.File!;

			// A missing file has no on-disk format to adopt: the document keeps its current format.
			WorkspaceOperationFailure? decodeFailure = TryDecodeContent(
				file,
				document,
				out string content,
				out TextFileFormat fileFormat);

			if (decodeFailure is not null)
			{
				lock (_stateLock)
					return CreateReloadResult(
						request,
						WorkspaceDocumentReloadOutcome.ReadFailed,
						CreateSnapshot(document),
						failure: decodeFailure);
			}

			lock (_stateLock)
			{
				// The clean branch registers its operation and reads through the lifetime-linked token, so
				// disposal normally surfaces as that token's cancellation; a file system that does not observe
				// the token would otherwise let the read complete and this branch adopt the content after the
				// store was disposed. Re-check disposal before mutating the document or returning its snapshot
				// so the documented Canceled outcome does not depend on the file system observing the token.
				if (_disposed)
					return CreateReloadResult(
						request,
						WorkspaceDocumentReloadOutcome.Canceled,
						CreateSnapshot(document),
						file.OnDiskStamp);

				if (document.Version != request.Identity.Version)
					return CreateReloadResult(
						request,
						WorkspaceDocumentReloadOutcome.StaleDocument,
						CreateSnapshot(document),
						file.OnDiskStamp);

				// The unchanged outcome is defined by the tracked stamp, not by the caller's expectation:
				// the tracked logical content corresponds to the stamp the store recorded, so a file whose
				// stamp matches it needs no adoption. An expectation that matches the disk while the tracked
				// stamp does not means the file changed after the store last observed it, and the new content
				// is adopted below.
				if (file.OnDiskStamp == document.OnDiskStamp)
				{
					return CreateReloadResult(
						request,
						WorkspaceDocumentReloadOutcome.Unchanged,
						CreateSnapshot(document),
						file.OnDiskStamp);
				}

				document.Content = content;
				document.PersistedContent = content;
				document.FileFormat = fileFormat;
				document.PersistedFileFormat = fileFormat;
				document.Version++;
				document.PersistedVersion = document.Version;
				document.OnDiskStamp = file.OnDiskStamp;

				return CreateReloadResult(
					request,
					WorkspaceDocumentReloadOutcome.Reloaded,
					CreateSnapshot(document),
					file.OnDiskStamp);
			}
		}
		finally
		{
			if (operation is not null)
				CompleteOperation(operation);
		}
	}

	// Handles a clean, on-disk commit that has nothing to write: the caller's expectation is validated
	// against the tracked stamp instead of a fresh capture, and a match commits without re-reading the
	// file.
	private WorkspaceDocumentCommitResult ResolveCleanCommitNoOp(
		WorkspaceDocumentCommitRequest request,
		LogicalDocument document,
		WorkspaceDocumentSnapshot capturedSnapshot)
	{
		if (request.ExpectedOnDiskStamp != capturedSnapshot.OnDiskStamp)
		{
			return CreateCommitResult(
				request,
				WorkspaceDocumentCommitOutcome.ExternalFileConflict,
				capturedSnapshot,
				capturedSnapshot.OnDiskStamp);
		}

		lock (_stateLock)
			return CreateCommitResult(
				request,
				WorkspaceDocumentCommitOutcome.Committed,
				CreateSnapshot(document));
	}

	// Resolves a conflict in favor of the logical content by committing it over the disk state,
	// forcing the write past the clean-document no-op path.
	private async Task<WorkspaceDocumentConflictResolutionResult> ResolveWithLogicalAsync(
		WorkspaceDocumentConflictResolutionRequest request,
		CancellationToken cancellationToken)
	{
		WorkspaceDocumentCommitResult commitResult = await CommitCoreAsync(
			new WorkspaceDocumentCommitRequest(
				request.Identity,
				request.ObservedOnDiskStamp),
			forceWrite: true,
			cancellationToken).ConfigureAwait(false);

		return CreateConflictResolutionFromCommit(request, commitResult);
	}

	/// <inheritdoc/>
	public async Task<WorkspaceDocumentConflictResolutionResult> ResolveExternalConflictAsync(
		WorkspaceDocumentConflictResolutionRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ThrowIfDisposed();

		// An undefined choice must not silently take the UseDisk branch, which would discard the
		// caller's unpublishable logical content without an explicit decision.
		if (request.Choice is not (WorkspaceDocumentConflictResolutionChoice.UseLogical
			or WorkspaceDocumentConflictResolutionChoice.UseDisk))
		{
			throw new ArgumentOutOfRangeException(
				nameof(request),
				request.Choice,
				"The conflict resolution choice is not a defined value.");
		}

		if (request.Choice == WorkspaceDocumentConflictResolutionChoice.UseLogical)
			return await ResolveWithLogicalAsync(request, cancellationToken).ConfigureAwait(false);

		OperationBegin begin = TryBeginOperation(
			request.Identity,
			captureSnapshot: true);

		if (begin is not { Succeeded: true, Document: { } document, CapturedSnapshot: { } capturedSnapshot, Operation: { } operation })
		{
			return MapOperationBeginFailure(
				begin,
				WorkspaceDocumentConflictResolutionOutcome.DocumentNotFound,
				WorkspaceDocumentConflictResolutionOutcome.StaleDocumentInstance,
				WorkspaceDocumentConflictResolutionOutcome.StaleDocument,
				WorkspaceDocumentConflictResolutionOutcome.OperationInProgress,
				(outcome, snapshot) => CreateConflictResolutionResult(request, outcome, snapshot));
		}

		try
		{
			// The stamp carried by the read describes exactly the bytes that are about to be adopted,
			// so it is the only evidence the adoption needs. Re-capturing the stamp would double the
			// I/O for a large file without closing the race: the file can still change before the
			// adoption below takes the state lock.
			DocumentReadOutcome read = await ReadDocumentFileAsync(
				capturedSnapshot.DocumentId,
				cancellationToken).ConfigureAwait(false);

			if (read.IsCanceled)
			{
				lock (_stateLock)
					return CreateConflictResolutionResult(
						request,
						WorkspaceDocumentConflictResolutionOutcome.Canceled,
						CreateSnapshot(document));
			}

			if (read.Failure is not null)
			{
				lock (_stateLock)
					return CreateConflictResolutionResult(
						request,
						WorkspaceDocumentConflictResolutionOutcome.ReadFailed,
						CreateSnapshot(document),
						failure: read.Failure);
			}

			WorkspaceFileReadResult file = read.File!;

			if (file.OnDiskStamp != request.ObservedOnDiskStamp)
				return CreateConflictResolutionResult(
					request,
					WorkspaceDocumentConflictResolutionOutcome.ExternalFileConflict,
					CreateSnapshotUnderLock(document),
					file.OnDiskStamp,
					new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ExternalFileConflict, "The disk file changed again after the conflict was observed."));

			WorkspaceOperationFailure? decodeFailure = TryDecodeContent(
				file,
				document,
				out string content,
				out TextFileFormat fileFormat);

			if (decodeFailure is not null)
			{
				lock (_stateLock)
					return CreateConflictResolutionResult(
						request,
						WorkspaceDocumentConflictResolutionOutcome.ReadFailed,
						CreateSnapshot(document),
						failure: decodeFailure);
			}

			lock (_stateLock)
			{
				if (document.Version != request.Identity.Version)
					return CreateConflictResolutionResult(
						request,
						WorkspaceDocumentConflictResolutionOutcome.StaleDocument,
						CreateSnapshot(document),
						file.OnDiskStamp);

				document.Content = content;
				document.PersistedContent = content;
				document.FileFormat = fileFormat;
				document.PersistedFileFormat = fileFormat;
				document.Version++;
				document.PersistedVersion = document.Version;
				document.OnDiskStamp = file.OnDiskStamp;

				return CreateConflictResolutionResult(
					request,
					WorkspaceDocumentConflictResolutionOutcome.ResolvedWithDisk,
					CreateSnapshot(document),
					file.OnDiskStamp);
			}
		}
		finally
		{
			CompleteOperation(operation);
		}
	}

	private WorkspaceDocumentCommitResult InstallCommittedBaseline(
		WorkspaceDocumentCommitRequest request,
		LogicalDocument document,
		WorkspaceDocumentSnapshot capturedSnapshot,
		FileStamp onDiskStamp)
	{
		lock (_stateLock)
		{
			document.PersistedContent = capturedSnapshot.Content;
			document.PersistedFileFormat = capturedSnapshot.FileFormat;
			document.PersistedVersion = capturedSnapshot.Version;
			document.OnDiskStamp = onDiskStamp;

			return CreateCommitResult(
				request,
				WorkspaceDocumentCommitOutcome.Committed,
				CreateSnapshot(document),
				onDiskStamp);
		}
	}

	private WorkspaceDocumentCommitResult UpdateObservedConflict(
		WorkspaceDocumentCommitRequest request,
		LogicalDocument document,
		FileStamp? observedOnDiskStamp)
	{
		lock (_stateLock)
		{
			if (observedOnDiskStamp.HasValue)
				document.OnDiskStamp = observedOnDiskStamp.Value;

			return CreateCommitResult(
				request,
				WorkspaceDocumentCommitOutcome.ExternalFileConflict,
				CreateSnapshot(document),
				observedOnDiskStamp);
		}
	}

	private WorkspaceDocumentCommitResult CreateCurrentCommitResult(
		WorkspaceDocumentCommitRequest request,
		LogicalDocument document,
		WorkspaceDocumentCommitOutcome outcome,
		FileStamp? observedOnDiskStamp,
		WorkspaceOperationFailure? failure)
	{
		lock (_stateLock)
		{
			if (observedOnDiskStamp.HasValue)
				document.OnDiskStamp = observedOnDiskStamp.Value;

			return CreateCommitResult(request, outcome, CreateSnapshot(document), observedOnDiskStamp, failure);
		}
	}

	// Reads a tracked document's file under the lifetime-linked token, mapping a cancellation or a read
	// failure (including a path that is a directory) onto the outcome both read paths consult. The
	// caller decodes the read through TryDecodeContent so an encoding failure is reported after its own
	// pre-adoption checks, matching the order each caller documents.
	private async Task<DocumentReadOutcome> ReadDocumentFileAsync(string documentId, CancellationToken cancellationToken)
	{
		try
		{
			using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken,
				_lifetimeCancellation.Token);
			linkedCancellation.Token.ThrowIfCancellationRequested();

			WorkspaceFileReadResult file = await _fileSystem
				.ReadAsync(documentId, linkedCancellation.Token)
				.ConfigureAwait(false);

			// A tracked file that was replaced by a directory is not a reloadable document: adopting it
			// as empty content would produce a document whose writes and deletes can never succeed.
			if (file.IsDirectory)
			{
				return DocumentReadOutcome.Failed(new WorkspaceOperationFailure(
					WorkspaceOperationFailureCodes.IsDirectory,
					"The path exists as a directory, not a file."));
			}

			return DocumentReadOutcome.Read(file);
		}
		catch (OperationCanceledException)
		{
			return DocumentReadOutcome.Canceled();
		}
		catch (Exception exception)
		{
			return DocumentReadOutcome.Failed(new WorkspaceOperationFailure(
				CreateReadFailureCode(exception),
				exception.Message,
				exception));
		}
	}

	// Decodes a read into the content and format to adopt. A decode failure is an encoding problem, not
	// a read problem: the code mirrors the open path so hosts branch on InvalidEncoding for the same
	// root cause. Returns null when the decode succeeded; otherwise, the failure to report.
	private static WorkspaceOperationFailure? TryDecodeContent(
		WorkspaceFileReadResult file,
		LogicalDocument document,
		out string content,
		out TextFileFormat fileFormat)
	{
		try
		{
			content = file.ResolveContent(document.FileFormat, document.NoBomEncoding, out fileFormat);
			return null;
		}
		catch (DecoderFallbackException exception)
		{
			content = string.Empty;
			fileFormat = default;
			return new WorkspaceOperationFailure(
				WorkspaceOperationFailureCodes.InvalidEncoding,
				exception.Message,
				exception);
		}
	}

	// A permission failure is a property of the path, not of the read: the reload and
	// conflict-resolution read paths report AccessDenied for it like the write paths, so a host can
	// tell an unfixable permission problem from a transient read failure. The open path applies the
	// same rule with its own LoadFailed fallback.
	private static string CreateReadFailureCode(Exception exception)
		=> exception is UnauthorizedAccessException
			? WorkspaceOperationFailureCodes.AccessDenied
			: WorkspaceOperationFailureCodes.ReadFailed;

	// The shared outcome of a document read: the read result, a cancellation, or a failure the caller
	// maps onto its own result shape. The reload and conflict-resolution paths read the same way and
	// differ only in how they map these states, so the read and its failure mapping live here.
	private readonly struct DocumentReadOutcome
	{
		private DocumentReadOutcome(WorkspaceFileReadResult? file, WorkspaceOperationFailure? failure, bool isCanceled)
		{
			File = file;
			Failure = failure;
			IsCanceled = isCanceled;
		}

		// The read result when the file was read; otherwise, null.
		public WorkspaceFileReadResult? File { get; }

		// The failure to report when the read failed; otherwise, null.
		public WorkspaceOperationFailure? Failure { get; }

		// Whether the read was canceled.
		public bool IsCanceled { get; }

		public static DocumentReadOutcome Read(WorkspaceFileReadResult file)
			=> new(file, failure: null, isCanceled: false);

		public static DocumentReadOutcome Failed(WorkspaceOperationFailure failure)
			=> new(file: null, failure, isCanceled: false);

		public static DocumentReadOutcome Canceled()
			=> new(file: null, failure: null, isCanceled: true);
	}
}
