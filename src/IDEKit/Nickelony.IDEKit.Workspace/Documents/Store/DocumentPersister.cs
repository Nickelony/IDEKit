using Nickelony.IDEKit.Workspace.FileSystem;
using static Nickelony.IDEKit.Workspace.Documents.WorkspaceDocumentResultFactory;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Brings a tracked document and its file into agreement: a commit writes the content, a reload and a conflict resolution adopt the disk.
/// </summary>
/// <remarks>
/// Every operation runs through the <see cref="AdmittedOperationRunner"/>, so a document has at most one file operation in flight. Reads and writes
/// run outside the store lock, and the tracked document changes only under it.
/// </remarks>
internal sealed class DocumentPersister
{
	private readonly object _syncRoot;
	private readonly OperationGate _operationGate;
	private readonly AdmittedOperationRunner _runner;
	private readonly IWorkspaceFileSystem _fileSystem;
	private readonly StampedFileWorker _io;
	private readonly StoreLifetime _lifetime;

	/// <param name="syncRoot">The store's lock, which orders the tracked documents.</param>
	/// <param name="operationGate">The gate that reports whether the store began stopping.</param>
	/// <param name="runner">The runner that admits each operation.</param>
	/// <param name="fileSystem">The file system a dirty reload captures its stamp from.</param>
	/// <param name="io">The file work that reads and writes a document's file.</param>
	/// <param name="lifetime">The store lifetime, which a dirty reload observes besides the caller's token.</param>
	internal DocumentPersister(
		object syncRoot,
		OperationGate operationGate,
		AdmittedOperationRunner runner,
		IWorkspaceFileSystem fileSystem,
		StampedFileWorker io,
		StoreLifetime lifetime
	)
	{
		_syncRoot = syncRoot;
		_operationGate = operationGate;
		_runner = runner;
		_fileSystem = fileSystem;
		_io = io;
		_lifetime = lifetime;
	}

	/// <summary>Writes a document's logical content to its file.</summary>
	/// <param name="request">The commit request.</param>
	/// <param name="forceWrite">
	/// Whether to write even when the document is clean and its file exists, as a conflict resolved for the logical content does.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the commit.</param>
	/// <returns>The outcome and the snapshot of the document after the commit.</returns>
	/// <exception cref="ObjectDisposedException">The store began stopping.</exception>
	internal Task<WorkspaceDocumentCommitResult> CommitAsync(
		WorkspaceDocumentCommitRequest request,
		bool forceWrite,
		CancellationToken cancellationToken
	)
	{
		return _runner.RunAsync(
			new AdmittedOperationDescriptor<WorkspaceDocumentCommitResult>
			{
				Identity = request.Identity,

				CreateRefusal = begin =>
					WorkspaceOutcomeConversion.ToFailureResult(
						begin,
						documentNotFound: WorkspaceDocumentCommitOutcome.DocumentNotFound,
						staleDocumentInstance: WorkspaceDocumentCommitOutcome.StaleDocumentInstance,
						staleDocument: WorkspaceDocumentCommitOutcome.StaleDocument,
						operationInProgress: WorkspaceDocumentCommitOutcome.OperationInProgress,
						factory: (outcome, snapshot) => CreateCommitResult(request, outcome, snapshot)
					),

				ExecuteAsync = (operation, token) => WriteAsync(request, forceWrite, operation, cancellationToken: token),

				CreateCanceled = document =>
				{
					lock (_syncRoot)
						return CreateCommitResult(request, WorkspaceDocumentCommitOutcome.Canceled, CreateSnapshot(document));
				},

				CreateFaulted = (document, exception) =>
				{
					lock (_syncRoot)
					{
						return CreateCommitResult(
							request,
							outcome: WorkspaceDocumentCommitOutcome.WriteFailed,
							snapshot: CreateSnapshot(document),

							failure: new WorkspaceOperationFailure(
								WorkspaceOperationFailureCodes.ClassifyWriteFailure(exception),
								exception.Message,
								exception
							)
						);
					}
				},
			},

			cancellationToken
		);
	}

	/// <summary>Adopts the file's content into a document, or reports the conflict when the document is dirty.</summary>
	/// <param name="request">The reload request.</param>
	/// <param name="cancellationToken">A token that can cancel the reload.</param>
	/// <returns>The outcome and the snapshot of the document after the reload.</returns>
	/// <exception cref="ObjectDisposedException">The store began stopping.</exception>
	internal Task<WorkspaceDocumentReloadResult> ReloadAsync(WorkspaceDocumentReloadRequest request, CancellationToken cancellationToken)
	{
		return _runner.RunAsync(
			new AdmittedOperationDescriptor<WorkspaceDocumentReloadResult>
			{
				Identity = request.Identity,
				Cancellation = AdmittedOperationCancellationMode.Caller,

				CreateRefusal = begin =>
					WorkspaceOutcomeConversion.ToFailureResult(
						begin,
						documentNotFound: WorkspaceDocumentReloadOutcome.DocumentNotFound,
						staleDocumentInstance: WorkspaceDocumentReloadOutcome.StaleDocumentInstance,
						staleDocument: WorkspaceDocumentReloadOutcome.StaleDocument,
						operationInProgress: WorkspaceDocumentReloadOutcome.OperationInProgress,
						factory: (outcome, snapshot) => CreateReloadResult(request, outcome, snapshot)
					),

				ExecuteAsync = (operation, token) =>
					operation.CapturedSnapshot.IsDirty ? ReloadDirtyAsync(request, operation, token) : ReloadCleanAsync(request, operation, token),
			},

			cancellationToken
		);
	}

	/// <summary>Resolves an external conflict in favor of the logical content or of the disk.</summary>
	/// <param name="request">The resolution request.</param>
	/// <param name="cancellationToken">A token that can cancel the resolution.</param>
	/// <returns>The outcome and the snapshot of the document after the resolution.</returns>
	/// <exception cref="ObjectDisposedException">The store began stopping.</exception>
	/// <exception cref="ArgumentOutOfRangeException">The request names a choice that is not defined.</exception>
	internal async Task<WorkspaceDocumentConflictResolutionResult> ResolveExternalConflictAsync(
		WorkspaceDocumentConflictResolutionRequest request,
		CancellationToken cancellationToken
	)
	{
		// An undefined choice must not silently take the UseDisk branch, which would discard the
		// caller's unsaved logical content without an explicit decision.
		if (
			request.Mode
			is not (WorkspaceDocumentConflictResolutionMode.ResolveWithLogical or WorkspaceDocumentConflictResolutionMode.ResolveWithDisk)
		)
		{
			throw new ArgumentOutOfRangeException(nameof(request), request.Mode, "The conflict resolution mode is not a defined value.");
		}

		if (request.Mode == WorkspaceDocumentConflictResolutionMode.ResolveWithLogical)
			return await ResolveWithLogicalAsync(request, cancellationToken).ConfigureAwait(false);

		return await _runner
			.RunAsync(
				new AdmittedOperationDescriptor<WorkspaceDocumentConflictResolutionResult>
				{
					Identity = request.Identity,
					Cancellation = AdmittedOperationCancellationMode.Caller,

					CreateRefusal = begin =>
						WorkspaceOutcomeConversion.ToFailureResult(
							begin,
							documentNotFound: WorkspaceDocumentConflictResolutionOutcome.DocumentNotFound,
							staleDocumentInstance: WorkspaceDocumentConflictResolutionOutcome.StaleDocumentInstance,
							staleDocument: WorkspaceDocumentConflictResolutionOutcome.StaleDocument,
							operationInProgress: WorkspaceDocumentConflictResolutionOutcome.OperationInProgress,
							factory: (outcome, snapshot) => CreateConflictResolutionResult(request, outcome, snapshot)
						),

					ExecuteAsync = (operation, token) => AdoptDiskAsync(request, operation, token),
				},

				cancellationToken
			)
			.ConfigureAwait(false);
	}

	private async Task<WorkspaceDocumentCommitResult> WriteAsync(
		WorkspaceDocumentCommitRequest request,
		bool forceWrite,
		AdmittedOperation operation,
		CancellationToken cancellationToken
	)
	{
		LogicalDocument document = operation.Document;
		WorkspaceDocumentSnapshot capturedSnapshot = operation.CapturedSnapshot;

		// The conditional replacement validates the caller's expected stamp against its own capture,
		// so an accepted commit that writes reads and hashes the file once. A clean tracked document
		// whose file exists is a no-op, validated against the tracked stamp rather than a fresh capture.
		if (!forceWrite && !capturedSnapshot.IsDirty && capturedSnapshot.ExistsOnDisk)
			return ResolveCleanCommitNoOp(request, document, capturedSnapshot);

		(WorkspaceFileReplacementResult? replacement, FileStamp? resolvedStamp, WorkspaceOperationFailure? stampFailure) =
			await _io.WriteReplacementAsync(
					documentId: capturedSnapshot.DocumentId,
					content: capturedSnapshot.Content,
					fileFormat: capturedSnapshot.FileFormat,
					expectedStamp: request.ExpectedOnDiskStamp,
					cancellationToken
				)
				.ConfigureAwait(false);

		if (replacement.Outcome == WorkspaceFileReplacementOutcome.Replaced)
		{
			if (resolvedStamp is null)
			{
				return CreateCurrentCommitResult(
					request,
					document,
					outcome: WorkspaceDocumentCommitOutcome.ReplacementStateUnknown,
					observedOnDiskStamp: null,
					failure: stampFailure
				);
			}

			return InstallCommittedBaseline(request, document, capturedSnapshot, onDiskStamp: resolvedStamp.Value);
		}

		return replacement.Outcome switch
		{
			WorkspaceFileReplacementOutcome.ExternalFileConflict => UpdateObservedConflict(request, document, replacement.ObservedOnDiskStamp),

			WorkspaceFileReplacementOutcome.Canceled => CreateCommitResult(
				request,
				WorkspaceDocumentCommitOutcome.Canceled,
				CreateSnapshotUnderLock(_syncRoot, document)
			),

			WorkspaceFileReplacementOutcome.DestinationExists => CreateCurrentCommitResult(
				request,
				document,
				outcome: WorkspaceDocumentCommitOutcome.WriteFailed,
				observedOnDiskStamp: replacement.ObservedOnDiskStamp,
				failure: replacement.Failure
			),

			WorkspaceFileReplacementOutcome.ReplacementStateUnknown => CreateCurrentCommitResult(
				request,
				document,
				outcome: WorkspaceDocumentCommitOutcome.ReplacementStateUnknown,
				observedOnDiskStamp: replacement.ObservedOnDiskStamp,
				failure: replacement.Failure
			),

			_ => CreateCurrentCommitResult(
				request,
				document,
				outcome: WorkspaceDocumentCommitOutcome.WriteFailed,
				observedOnDiskStamp: replacement.ObservedOnDiskStamp,
				failure: replacement.Failure
			),
		};
	}

	// Reloads a dirty document by comparing the file's stamp with the tracked one, under the per-document disk gate.
	// The gate keeps a commit, a move or a delete of the same document from replacing the file while the stamp is read,
	// and the registration makes disposal wait for the capture, which the lifetime's cancellation ends.
	private async Task<WorkspaceDocumentReloadResult> ReloadDirtyAsync(
		WorkspaceDocumentReloadRequest request,
		AdmittedOperation operation,
		CancellationToken cancellationToken
	)
	{
		LogicalDocument document = operation.Document;
		WorkspaceDocumentSnapshot capturedSnapshot = operation.CapturedSnapshot;

		using CancellationTokenSource linkedCancellation = _lifetime.CreateLinkedCancellation(cancellationToken);

		try
		{
			// A requested encoding asks for a re-decode that unsaved changes forbid, so the file is not touched for it.
			FileStamp? observedStamp = request.Encoding is null
				? await _fileSystem.CaptureStampAsync(capturedSnapshot.DocumentId, linkedCancellation.Token).ConfigureAwait(false)
				: null;

			lock (_syncRoot)
				return ResolveDirtyReload(request, document, observedStamp);
		}
		catch (OperationCanceledException)
		{
			return CreateReloadResult(request, WorkspaceDocumentReloadOutcome.Canceled, capturedSnapshot);
		}
		catch (Exception exception)
		{
			return CreateReloadResult(
				request,
				outcome: WorkspaceDocumentReloadOutcome.ReadFailed,
				snapshot: capturedSnapshot,

				failure: new WorkspaceOperationFailure(
					WorkspaceOperationFailureCodes.ClassifyReadFailure(exception, WorkspaceOperationFailureCodes.ReadFailed),
					exception.Message,
					exception
				)
			);
		}
	}

	// Requires the store lock. Maps the stamp captured for a dirty reload to the reload outcome. The reload holds the
	// document's disk gate, so no file operation changed its file, its stamp or its tracked identity since the capture
	// began; a stop and an edit of the logical content are the changes left to re-check.
	private WorkspaceDocumentReloadResult ResolveDirtyReload(
		WorkspaceDocumentReloadRequest request,
		LogicalDocument document,
		FileStamp? observedStamp
	)
	{
		// Disposal cancels the capture, but a capture that finished first resolves here, so a stop that began meanwhile
		// reports the canceled outcome instead of a comparison that disposal is about to discard.
		if (_operationGate.IsStopping)
		{
			return CreateReloadResult(
				request,
				outcome: WorkspaceDocumentReloadOutcome.Canceled,
				snapshot: CreateSnapshot(document),
				observedOnDiskStamp: observedStamp
			);
		}

		// An edit takes no disk gate, so the logical content can change while the stamp is captured.
		if (document.Version != request.Identity.Version)
		{
			return CreateReloadResult(
				request,
				outcome: WorkspaceDocumentReloadOutcome.StaleDocument,
				snapshot: CreateSnapshot(document),
				observedOnDiskStamp: observedStamp
			);
		}

		// The caller asked for a re-decode, which adopting the disk would need and unsaved changes forbid. Reporting the stamp
		// comparison instead would tell the caller that the file was read with the encoding.
		if (request.Encoding is not null)
			return CreateReloadResult(request, outcome: WorkspaceDocumentReloadOutcome.UnsavedChanges, snapshot: CreateSnapshot(document));

		// Nothing changed externally since the store last observed the file: a dirty document has
		// nothing to reload, and a spurious watcher event must not raise a conflict prompt.
		if (observedStamp == document.OnDiskStamp)
		{
			return CreateReloadResult(
				request,
				outcome: WorkspaceDocumentReloadOutcome.Unchanged,
				snapshot: CreateSnapshot(document),
				observedOnDiskStamp: observedStamp
			);
		}

		// The observed stamp is not installed on the document; callers resolve the conflict with the stamp carried by this result.
		return CreateReloadResult(
			request,
			outcome: WorkspaceDocumentReloadOutcome.ExternalFileConflict,
			snapshot: CreateSnapshot(document),
			observedOnDiskStamp: observedStamp
		);
	}

	// Reloads a clean document by reading its content under the per-document disk gate and installing
	// the read as the new persisted baseline.
	private async Task<WorkspaceDocumentReloadResult> ReloadCleanAsync(
		WorkspaceDocumentReloadRequest request,
		AdmittedOperation operation,
		CancellationToken cancellationToken
	)
	{
		LogicalDocument document = operation.Document;

		DocumentReadResult read = await _io.ReadDocumentFileAsync(operation.CapturedSnapshot.DocumentId, cancellationToken).ConfigureAwait(false);

		if (read.IsCanceled)
		{
			lock (_syncRoot)
				return CreateReloadResult(request, WorkspaceDocumentReloadOutcome.Canceled, CreateSnapshot(document));
		}

		if (!read.HasFile)
		{
			lock (_syncRoot)
			{
				return CreateReloadResult(
					request,
					outcome: WorkspaceDocumentReloadOutcome.ReadFailed,
					snapshot: CreateSnapshot(document),
					failure: read.Failure
				);
			}
		}

		WorkspaceFileReadResult file = read.File;

		// A reopen decodes the file's bytes. A file system that decodes inside its read leaves nothing for the requested encoding to decode,
		// so the reload fails instead of reporting a decode that did not happen.
		if (request.Encoding is not null && file.RawBytes is null && file.OnDiskStamp.Exists)
		{
			lock (_syncRoot)
			{
				return CreateReloadResult(
					request,
					outcome: WorkspaceDocumentReloadOutcome.ReadFailed,
					snapshot: CreateSnapshot(document),

					failure: new WorkspaceOperationFailure(
						WorkspaceOperationFailureCodes.EncodingNotApplicable,
						"The file system returned decoded content, so the requested encoding has no bytes to decode."
					)
				);
			}
		}

		// A plain reload of a file whose stamp the store tracks adopts nothing, so it reports Unchanged without decoding: an encoding that
		// does not fit the bytes must not fail a reload that has nothing to read.
		if (request.Encoding is null)
		{
			lock (_syncRoot)
			{
				WorkspaceDocumentReloadResult? settled = ResolveUnadoptedReload(request, document, file, content: null, fileFormat: default);

				if (settled is not null)
					return settled;
			}
		}

		// A requested encoding replaces the document's candidates for this read only.
		IReadOnlyList<TextEncoding> candidates = request.Encoding is { } requestedEncoding ? [requestedEncoding] : document.NoBomEncodings;

		// A missing file has no on-disk format to adopt: the document keeps its current format.
		WorkspaceOperationFailure? decodeFailure = StampedFileWorker.DecodeContent(
			file,
			currentFormat: document.FileFormat,
			noBomEncodings: candidates,
			out string content,
			out TextFileFormat fileFormat
		);

		if (decodeFailure is not null)
		{
			lock (_syncRoot)
			{
				return CreateReloadResult(
					request,
					outcome: WorkspaceDocumentReloadOutcome.ReadFailed,
					snapshot: CreateSnapshot(document),
					failure: decodeFailure
				);
			}
		}

		lock (_syncRoot)
		{
			WorkspaceDocumentReloadResult? unadopted = ResolveUnadoptedReload(request, document, file, content, fileFormat);

			if (unadopted is not null)
				return unadopted;

			document.AdoptDiskContent(content, fileFormat, file.OnDiskStamp);
			RememberRequestedEncoding(document, request, fileFormat);

			return CreateReloadResult(
				request,
				outcome: WorkspaceDocumentReloadOutcome.Reloaded,
				snapshot: CreateSnapshot(document),
				observedOnDiskStamp: file.OnDiskStamp
			);
		}
	}

	// Requires the store lock. Returns the outcome of a read that adopts nothing, which is canceled, stale or unchanged, and null when the
	// read must be adopted. Disposal normally surfaces as the lifetime token's cancellation, but a file system that does not observe the
	// token could let the read complete, so disposal is re-checked before the document changes: the documented Canceled outcome must not
	// depend on the file system. The unchanged outcome is defined by the tracked stamp, not the caller's expectation: a file whose stamp
	// matches the store's record needs no adoption, and an expectation that matches the disk while the tracked stamp does not means the
	// file changed since the store last observed it. A requested encoding re-reads an unchanged file, so it leaves the reload a no-op only
	// when the content it decoded (null before any decode) equals what the document already holds.
	private WorkspaceDocumentReloadResult? ResolveUnadoptedReload(
		WorkspaceDocumentReloadRequest request,
		LogicalDocument document,
		WorkspaceFileReadResult file,
		string? content,
		TextFileFormat fileFormat
	)
	{
		if (_operationGate.IsStopping)
		{
			return CreateReloadResult(
				request,
				outcome: WorkspaceDocumentReloadOutcome.Canceled,
				snapshot: CreateSnapshot(document),
				observedOnDiskStamp: file.OnDiskStamp
			);
		}

		if (document.Version != request.Identity.Version)
		{
			return CreateReloadResult(
				request,
				outcome: WorkspaceDocumentReloadOutcome.StaleDocument,
				snapshot: CreateSnapshot(document),
				observedOnDiskStamp: file.OnDiskStamp
			);
		}

		if (file.OnDiskStamp != document.OnDiskStamp)
			return null;

		if (request.Encoding is not null && (content is null || !DecodesToDocument(document, content, fileFormat)))
			return null;

		RememberRequestedEncoding(document, request, fileFormat);

		return CreateReloadResult(
			request,
			outcome: WorkspaceDocumentReloadOutcome.Unchanged,
			snapshot: CreateSnapshot(document),
			observedOnDiskStamp: file.OnDiskStamp
		);
	}

	// Handles a clean, on-disk commit that has nothing to write: the caller's expectation is validated
	// against the tracked stamp instead of a fresh capture, and a match commits without re-reading the
	// file.
	private WorkspaceDocumentCommitResult ResolveCleanCommitNoOp(
		WorkspaceDocumentCommitRequest request,
		LogicalDocument document,
		WorkspaceDocumentSnapshot capturedSnapshot
	)
	{
		if (request.ExpectedOnDiskStamp != capturedSnapshot.OnDiskStamp)
		{
			return CreateCommitResult(
				request,
				outcome: WorkspaceDocumentCommitOutcome.ExternalFileConflict,
				snapshot: capturedSnapshot,
				observedOnDiskStamp: capturedSnapshot.OnDiskStamp
			);
		}

		lock (_syncRoot)
			return CreateCommitResult(request, WorkspaceDocumentCommitOutcome.Committed, CreateSnapshot(document));
	}

	// Resolves a conflict in favor of the logical content by committing it over the disk state,
	// forcing the write past the clean-document no-op path.
	private async Task<WorkspaceDocumentConflictResolutionResult> ResolveWithLogicalAsync(
		WorkspaceDocumentConflictResolutionRequest request,
		CancellationToken cancellationToken
	)
	{
		WorkspaceDocumentCommitResult commitResult = await CommitAsync(
				new WorkspaceDocumentCommitRequest(request.Identity, request.ObservedOnDiskStamp),
				forceWrite: true,
				cancellationToken
			)
			.ConfigureAwait(false);

		return CreateConflictResolutionFromCommit(request, commitResult);
	}

	private async Task<WorkspaceDocumentConflictResolutionResult> AdoptDiskAsync(
		WorkspaceDocumentConflictResolutionRequest request,
		AdmittedOperation operation,
		CancellationToken cancellationToken
	)
	{
		LogicalDocument document = operation.Document;

		// The stamp carried by the read describes exactly the bytes that are about to be adopted,
		// so it is the only evidence the adoption needs. Re-capturing the stamp would double the
		// I/O for a large file without closing the race: the file can still change before the
		// adoption below takes the state lock.
		DocumentReadResult read = await _io.ReadDocumentFileAsync(operation.CapturedSnapshot.DocumentId, cancellationToken).ConfigureAwait(false);

		if (read.IsCanceled)
		{
			lock (_syncRoot)
				return CreateConflictResolutionResult(request, WorkspaceDocumentConflictResolutionOutcome.Canceled, CreateSnapshot(document));
		}

		if (!read.HasFile)
		{
			lock (_syncRoot)
			{
				return CreateConflictResolutionResult(
					request,
					outcome: WorkspaceDocumentConflictResolutionOutcome.ReadFailed,
					snapshot: CreateSnapshot(document),
					failure: read.Failure
				);
			}
		}

		WorkspaceFileReadResult file = read.File;

		if (file.OnDiskStamp != request.ObservedOnDiskStamp)
		{
			return CreateConflictResolutionResult(
				request,
				outcome: WorkspaceDocumentConflictResolutionOutcome.ExternalFileConflict,
				snapshot: CreateSnapshotUnderLock(_syncRoot, document),
				observedOnDiskStamp: file.OnDiskStamp,

				failure: new WorkspaceOperationFailure(
					WorkspaceOperationFailureCodes.ExternalFileConflict,
					"The disk file changed again after the conflict was observed."
				)
			);
		}

		WorkspaceOperationFailure? decodeFailure = StampedFileWorker.DecodeContent(
			file,
			currentFormat: document.FileFormat,
			noBomEncodings: document.NoBomEncodings,
			out string content,
			out TextFileFormat fileFormat
		);

		if (decodeFailure is not null)
		{
			lock (_syncRoot)
			{
				return CreateConflictResolutionResult(
					request,
					outcome: WorkspaceDocumentConflictResolutionOutcome.ReadFailed,
					snapshot: CreateSnapshot(document),
					failure: decodeFailure
				);
			}
		}

		lock (_syncRoot)
		{
			if (document.Version != request.Identity.Version)
			{
				return CreateConflictResolutionResult(
					request,
					outcome: WorkspaceDocumentConflictResolutionOutcome.StaleDocument,
					snapshot: CreateSnapshot(document),
					observedOnDiskStamp: file.OnDiskStamp
				);
			}

			document.AdoptDiskContent(content, fileFormat, file.OnDiskStamp);

			return CreateConflictResolutionResult(
				request,
				outcome: WorkspaceDocumentConflictResolutionOutcome.ResolvedWithDisk,
				snapshot: CreateSnapshot(document),
				observedOnDiskStamp: file.OnDiskStamp
			);
		}
	}

	// Requires the store lock. The encoding a reload was asked for becomes the only candidate for the document's later reloads, so a
	// watcher reload keeps the user's choice, but only when that encoding decoded the bytes: a byte-order mark that decided instead leaves
	// the candidates as they were, because the user's choice never read the file. A later decode failure fails that reload instead of
	// trying the candidates the document was opened with, because a fallback would show the file in an encoding the user did not choose,
	// and a save would write that text back.
	private static void RememberRequestedEncoding(LogicalDocument document, WorkspaceDocumentReloadRequest request, TextFileFormat decodedFormat)
	{
		if (request.Encoding is { } requestedEncoding && !decodedFormat.HasBom && decodedFormat.Encoding == requestedEncoding)
			document.RestrictNoBomEncodings(requestedEncoding);
	}

	private static bool DecodesToDocument(LogicalDocument document, string content, TextFileFormat fileFormat) =>
		fileFormat == document.FileFormat && string.Equals(content, document.Content, StringComparison.Ordinal);

	private WorkspaceDocumentCommitResult InstallCommittedBaseline(
		WorkspaceDocumentCommitRequest request,
		LogicalDocument document,
		WorkspaceDocumentSnapshot capturedSnapshot,
		FileStamp onDiskStamp
	)
	{
		lock (_syncRoot)
		{
			document.InstallBaseline(
				content: capturedSnapshot.Content,
				fileFormat: capturedSnapshot.FileFormat,
				version: capturedSnapshot.Version,
				onDiskStamp
			);

			return CreateCommitResult(
				request,
				outcome: WorkspaceDocumentCommitOutcome.Committed,
				snapshot: CreateSnapshot(document),
				observedOnDiskStamp: onDiskStamp
			);
		}
	}

	private WorkspaceDocumentCommitResult UpdateObservedConflict(
		WorkspaceDocumentCommitRequest request,
		LogicalDocument document,
		FileStamp? observedOnDiskStamp
	)
	{
		lock (_syncRoot)
		{
			if (observedOnDiskStamp.HasValue)
				document.ObserveOnDiskStamp(observedOnDiskStamp.Value);

			return CreateCommitResult(
				request,
				outcome: WorkspaceDocumentCommitOutcome.ExternalFileConflict,
				snapshot: CreateSnapshot(document),
				observedOnDiskStamp
			);
		}
	}

	private WorkspaceDocumentCommitResult CreateCurrentCommitResult(
		WorkspaceDocumentCommitRequest request,
		LogicalDocument document,
		WorkspaceDocumentCommitOutcome outcome,
		FileStamp? observedOnDiskStamp,
		WorkspaceOperationFailure? failure
	)
	{
		lock (_syncRoot)
		{
			if (observedOnDiskStamp.HasValue)
				document.ObserveOnDiskStamp(observedOnDiskStamp.Value);

			return CreateCommitResult(request, outcome, snapshot: CreateSnapshot(document), observedOnDiskStamp, failure);
		}
	}
}
