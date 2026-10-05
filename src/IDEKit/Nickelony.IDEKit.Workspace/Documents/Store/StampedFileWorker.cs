using System.Text;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Performs the store's lock-free file work, resolving the stamp that each read, write or move leaves behind.</summary>
/// <remarks>
/// <para>
/// Nothing here reads or changes document state, so the store calls it outside its state lock. The stamp of a write is captured
/// after the write returned. A move whose destination cannot be captured falls back to the stamp the caller expected, because the retarget
/// must still complete.
/// </para>
/// <para>
/// An open and the reload reads classify a failed read by one rule, <see cref="WorkspaceOperationFailureCodes.ClassifyReadFailure"/>.
/// A commit and a save-as classify a failed write by <see cref="WorkspaceOperationFailureCodes.ClassifyWriteFailure"/>, and the directory operations
/// by <see cref="WorkspaceOperationFailureCodes.ClassifyDirectoryOperationFailure"/>.
/// </para>
/// </remarks>
internal sealed class StampedFileWorker
{
	private readonly IWorkspaceFileSystem _fileSystem;
	private readonly StoreLifetime _lifetime;

	/// <param name="fileSystem">The file-system implementation used for reads, writes and stamp captures.</param>
	/// <param name="lifetime">The store lifetime, which a document read observes besides the caller's token.</param>
	internal StampedFileWorker(IWorkspaceFileSystem fileSystem, StoreLifetime lifetime)
	{
		_fileSystem = fileSystem;
		_lifetime = lifetime;
	}

	// Reads a tracked document's file under the lifetime-linked token, mapping a cancellation or a read
	// failure (including a path that is a directory) onto the outcome both read paths consult. The
	// caller decodes the read through DecodeContent so an encoding failure is reported after its own
	// pre-adoption checks, matching the order each caller documents.
	internal async Task<DocumentReadResult> ReadDocumentFileAsync(string documentId, CancellationToken cancellationToken)
	{
		try
		{
			using CancellationTokenSource linkedCancellation = _lifetime.CreateLinkedCancellation(cancellationToken);
			linkedCancellation.Token.ThrowIfCancellationRequested();

			WorkspaceFileReadResult file = await _fileSystem.ReadAsync(documentId, linkedCancellation.Token).ConfigureAwait(false);

			// A tracked file that was replaced by a directory is not a reloadable document: adopting it
			// as empty content would produce a document whose writes and deletes can never succeed.
			if (file.IsDirectory)
			{
				return DocumentReadResult.Failed(
					new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.IsDirectory, "The path exists as a directory, not a file.")
				);
			}

			return DocumentReadResult.Read(file);
		}
		catch (OperationCanceledException)
		{
			return DocumentReadResult.Canceled();
		}
		catch (Exception exception)
		{
			return DocumentReadResult.Failed(
				new WorkspaceOperationFailure(
					WorkspaceOperationFailureCodes.ClassifyReadFailure(exception, WorkspaceOperationFailureCodes.ReadFailed),
					exception.Message,
					exception
				)
			);
		}
	}

	// Decodes a read into the content and format to adopt. A decode failure is an encoding problem, not
	// a read problem: the code mirrors the open path so hosts branch on InvalidEncoding for the same
	// root cause, and an encoding that the process cannot resolve reports EncodingUnavailable as the
	// open path does. Returns null when the decode succeeded; otherwise, the failure to report.
	internal static WorkspaceOperationFailure? DecodeContent(
		WorkspaceFileReadResult file,
		TextFileFormat currentFormat,
		IReadOnlyList<TextEncoding> noBomEncodings,
		out string content,
		out TextFileFormat fileFormat
	)
	{
		try
		{
			content = file.ResolveContent(currentFormat, noBomEncodings, out fileFormat);
			return null;
		}
		catch (DecoderFallbackException exception)
		{
			content = string.Empty;
			fileFormat = default;
			return new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.InvalidEncoding, exception.Message, exception);
		}
		catch (EncodingUnavailableException exception)
		{
			content = string.Empty;
			fileFormat = default;
			return new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.EncodingUnavailable, exception.Message, exception);
		}
	}

	// Encodes the content and hands it to the single conditional write, which owns the temporary file
	// and its cleanup. Returns the replacement with its resolved stamp. The stamp and the failure are both
	// null unless the replacement succeeded; after a success, the stamp is null exactly when the failure
	// is present, and the callers map that to ReplacementStateUnknown, not a false baseline.
	internal async Task<(
		WorkspaceFileReplacementResult Replacement,
		FileStamp? ResolvedStamp,
		WorkspaceOperationFailure? StampFailure
	)> WriteReplacementAsync(
		string documentId,
		string content,
		TextFileFormat fileFormat,
		FileStamp expectedStamp,
		CancellationToken cancellationToken
	)
	{
		// The write destination is the normalized document id rather than the caller-supplied display
		// spelling, so the write stays on the document's volume regardless of how the host spelled the
		// path; the file-system implementation chooses how to stage the bytes.
		byte[] bytes = WorkspaceTextCodec.Encode(content, fileFormat);

		WorkspaceFileReplacementResult replacement = await _fileSystem
			.WriteFileAsync(destinationPath: documentId, content: bytes, expectedStamp, cancellationToken)
			.ConfigureAwait(false);

		FileStamp? resolvedStamp = null;
		WorkspaceOperationFailure? stampFailure = null;

		if (replacement.Outcome == WorkspaceFileReplacementOutcome.Replaced)
			(resolvedStamp, stampFailure) = await ResolveReplacementStampAsync(replacement, documentId, cancellationToken).ConfigureAwait(false);

		return (replacement, resolvedStamp, stampFailure);
	}

	// A move may report no stamp (IWorkspaceFileSystem.MoveAsync reports it only optionally).
	// Re-capture the destination stamp so the next write does not conflict with the store's own move;
	// a failed re-capture falls back to the expected stamp, because the retarget must still complete.
	internal async Task<FileStamp> ResolveMovedStampAsync(string path, FileStamp expectedStamp, CancellationToken cancellationToken)
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

	// Re-captures every descendant stamp and returns the first descendant whose observed stamp differs
	// from the stamp the store tracks, together with its document id; null when every
	// descendant is unchanged.
	internal async Task<(string DocumentId, FileStamp Stamp)?> FindChangedDescendantStampAsync(
		IReadOnlyList<string> documentIds,
		IReadOnlyList<FileStamp> expectedStamps,
		CancellationToken cancellationToken
	)
	{
		for (int index = 0; index < documentIds.Count; index++)
		{
			FileStamp observedStamp = await _fileSystem.CaptureStampAsync(documentIds[index], cancellationToken).ConfigureAwait(false);

			if (observedStamp != expectedStamps[index])
				return (documentIds[index], observedStamp);
		}

		return null;
	}

	// The replacement completed but its final state could not be established: callers report
	// ReplacementStateUnknown rather than installing a false baseline. The re-capture exception is
	// carried through so the indeterminate state can be diagnosed after the fact.
	private static WorkspaceOperationFailure CreateUnknownReplacementStampFailure(Exception captureFailure)
	{
		const string message = "The replacement completed but its resulting file stamp could not be captured:";

		return new WorkspaceOperationFailure(
			WorkspaceOperationFailureCodes.ReplacementStateUnknown,
			$"{message} {captureFailure.Message}",
			captureFailure
		);
	}

	// A replacement may report no stamp (IWorkspaceFileSystem.WriteFileAsync documents it as optional):
	// re-capture it so the next write does not conflict with the store's own replacement, and treat a
	// failed re-capture as state-unknown, the failure carried so the indeterminate state is diagnosable.
	private async Task<(FileStamp? Stamp, WorkspaceOperationFailure? StampFailure)> ResolveReplacementStampAsync(
		WorkspaceFileReplacementResult replacement,
		string path,
		CancellationToken cancellationToken
	)
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
}
