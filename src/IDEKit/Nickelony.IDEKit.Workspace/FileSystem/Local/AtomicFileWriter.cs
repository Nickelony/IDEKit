using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Nickelony.IDEKit.Workspace.FileSystem;

/// <summary>
/// Replaces a local file through a staged temporary file, so a reader never sees a partial file and a failed write keeps the original.
/// </summary>
/// <remarks>
/// The bytes are staged beside the destination and flushed to the physical device before the destination is touched, and the destination is published
/// by a replace or a move, so the destination holds either the old content or the complete new content. The temporary file is removed on every path,
/// except when it holds the only copy of the new content or another process keeps it open past the cleanup's retries. A stale destination is rejected
/// before staging by a metadata probe, and the probe runs again after staging and before each retry of the publish.
/// </remarks>
internal sealed class AtomicFileWriter
{
	// The Windows error codes ERROR_UNABLE_TO_MOVE_REPLACEMENT and ERROR_UNABLE_TO_MOVE_REPLACEMENT_2 as HRESULT values.
	private const int UnableToMoveReplacementHResult = unchecked((int)0x80070498);
	private const int UnableToMoveReplacement2HResult = unchecked((int)0x80070499);

	private readonly WorkspaceFileSystemPrimitives _primitives;

	internal AtomicFileWriter(WorkspaceFileSystemPrimitives primitives) => _primitives = primitives;

	internal async Task<WorkspaceFileReplacementResult> WriteAsync(
		string destinationPath,
		ReadOnlyMemory<byte> content,
		FileStamp expectedStamp,
		CancellationToken cancellationToken
	)
	{
		// The temporary file is created next to the destination so the publish step never crosses
		// volumes, which File.Replace and File.Move do not support. A destination that is itself a
		// volume root has no parent directory; the root is its own directory.
		string directory =
			Path.GetDirectoryName(destinationPath)
			?? Path.GetPathRoot(destinationPath)
			?? throw new InvalidOperationException($"The destination path '{destinationPath}' has no directory.");

		string temporaryPath = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");

		// The hash of the bytes that are about to be staged is computed up front: the success stamp and
		// the post-failure classification both compare it against the destination's re-captured stamp.
		string contentHash = Convert.ToHexString(SHA256.HashData(content.Span));

		// Set once the publish ran, so a sharing violation thrown after it is not reported as a write that
		// published nothing.
		bool published = false;

		// Set when the temporary file holds the only copy of the new content, so the cleanup keeps it.
		bool keepsTemporaryFile = false;

		try
		{
			cancellationToken.ThrowIfCancellationRequested();

			// Reject a destination whose cheap metadata shape already disagrees with the expected stamp
			// before staging. The full stamps cannot be equal when the kind, length, or last-write time
			// differs, so the common stale-expectation path never writes and durably flushes a temporary file
			// that it would immediately discard. The same probe is re-run after staging: a shape that still
			// agrees reuses the expected stamp, and only a metadata difference captures the content hash that
			// decides the conflict.
			if (FileStampReader.ShapeDiffers(destinationPath, expectedStamp))
				return await RejectStaleDestinationAsync(destinationPath, expectedStamp, cancellationToken).ConfigureAwait(false);

			await WriteTemporaryFileAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);

			cancellationToken.ThrowIfCancellationRequested();

			if (await CheckStagedDestinationAsync(destinationPath, expectedStamp, cancellationToken).ConfigureAwait(false) is { } rejected)
				return rejected;

			cancellationToken.ThrowIfCancellationRequested();

			// A write publishes the temporary file, so the destination's file mode (for example
			// the executable bit) is captured and re-applied on Unix-like systems; ACLs, extended
			// attributes, and ownership are not carried across the new file and are not preserved.
			UnixFileMode? preservedMode = CaptureUnixFileMode(destinationPath, expectedStamp.Exists);

			Action publish = expectedStamp.Exists
				? () => _primitives.Replace(temporaryPath, destinationPath)
				: () => _primitives.MoveFile(temporaryPath, destinationPath);

			WorkspaceFileReplacementResult? rejectedOnRetry = await TransientFailureRetry
				.RunAsync(publish, token => CheckStagedDestinationAsync(destinationPath, expectedStamp, token), cancellationToken)
				.ConfigureAwait(false);

			if (rejectedOnRetry is not null)
				return rejectedOnRetry;

			published = true;
			ApplyUnixFileMode(destinationPath, preservedMode);

			DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(destinationPath);
			FileStamp replacementStamp = new(Exists: true, Length: content.Length, lastWriteTimeUtc, contentHash);

			return new WorkspaceFileReplacementResult(WorkspaceFileReplacementOutcome.Replaced, replacementStamp);
		}
		catch (OperationCanceledException)
		{
			return new WorkspaceFileReplacementResult(WorkspaceFileReplacementOutcome.Canceled);
		}
		catch (UnauthorizedAccessException exception)
		{
			return new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementOutcome.Failed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.AccessDenied, exception.Message, exception)
			);
		}
		catch (IOException exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
		{
			// A FileNotFoundException means the file the caller expected at the destination was removed
			// between the stamp check and the publish; a write that expected the destination to be free
			// instead has no free path once a file occupies it. Both are destination changes the host
			// re-baselines against, so they are reported as an external conflict with the re-captured
			// destination stamp. A missing destination directory is a genuine write failure - nothing was
			// published and no tracked destination changed - so it keeps the WriteFailed code.
			FileStamp? observedOnDiskStamp = await FileStampReader.CaptureOrNullAsync(destinationPath).ConfigureAwait(false);

			if (exception is FileNotFoundException || (!expectedStamp.Exists && observedOnDiskStamp is { Exists: true }))
				return new WorkspaceFileReplacementResult(WorkspaceFileReplacementOutcome.ExternalFileConflict, observedOnDiskStamp);

			return new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementOutcome.Failed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.WriteFailed, exception.Message, exception)
			);
		}
		catch (IOException exception) when (!published && TransientFailureRetry.IsSharingFailure(exception))
		{
			// Every attempt, or the precondition check's own capture, met a sharing or lock violation, which
			// leaves both files under their own names, so nothing was published. The re-capture is evidence
			// only: a holder that denies reading makes it fail, and the outcome stays a failed write.
			FileStamp? observedOnDiskStamp = await FileStampReader.CaptureOrNullAsync(destinationPath).ConfigureAwait(false);

			return new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementOutcome.Failed,
				observedOnDiskStamp,
				new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.WriteFailed, exception.Message, exception)
			);
		}
		catch (IOException exception) when (LeftTheReplacementUnmoved(exception, destinationPath, temporaryPath))
		{
			(WorkspaceFileReplacementResult result, keepsTemporaryFile) = await PublishUnmovedReplacementAsync(
					temporaryPath,
					destinationPath,
					contentLength: content.Length,
					contentHash,
					replaceException: exception
				)
				.ConfigureAwait(false);

			return result;
		}
		catch (IOException exception)
		{
			FileStamp? observedOnDiskStamp = await FileStampReader.CaptureOrNullAsync(destinationPath).ConfigureAwait(false);

			return ResolveFailedReplacement(
				temporaryLength: content.Length,
				temporaryContentHash: contentHash,
				destinationPath,
				expectedStamp,
				observedOnDiskStamp,
				exception
			);
		}
		catch (PlatformNotSupportedException exception)
		{
			FileStamp? observedOnDiskStamp = await FileStampReader.CaptureOrNullAsync(destinationPath).ConfigureAwait(false);

			return ResolveFailedReplacement(
				temporaryLength: content.Length,
				temporaryContentHash: contentHash,
				destinationPath,
				expectedStamp,
				observedOnDiskStamp,
				exception
			);
		}
		finally
		{
			if (!keepsTemporaryFile)
				await DeleteTemporaryFileAsync(temporaryPath).ConfigureAwait(false);
		}
	}

	// File.Replace without a backup reports ERROR_UNABLE_TO_MOVE_REPLACEMENT after it removed the replaced file,
	// and ERROR_UNABLE_TO_MOVE_REPLACEMENT_2 after it renamed the replaced file aside; either way the new content
	// survives only in the temporary file, and the destination path is free.
	private static bool LeftTheReplacementUnmoved(IOException exception, string destinationPath, string temporaryPath)
	{
		return exception.HResult is UnableToMoveReplacementHResult or UnableToMoveReplacement2HResult
			&& !File.Exists(destinationPath)
			&& File.Exists(temporaryPath);
	}

	// Moves the temporary file that a failed File.Replace left behind onto the free destination, so the new content
	// is not deleted with the temporary file. The move does not observe the caller's token, because a cancellation
	// here would leave the destination missing. When the move fails, the temporary file is kept and its path is
	// reported in the failure.
	private async Task<(WorkspaceFileReplacementResult Result, bool KeepsTemporaryFile)> PublishUnmovedReplacementAsync(
		string temporaryPath,
		string destinationPath,
		long contentLength,
		string contentHash,
		IOException replaceException
	)
	{
		try
		{
			await TransientFailureRetry
				.RunAsync<WorkspaceFileReplacementResult>(
					() => _primitives.MoveFile(temporaryPath, destinationPath),
					checkPreconditionsAsync: null,
					CancellationToken.None
				)
				.ConfigureAwait(false);

			FileStamp replacementStamp = new(
				Exists: true,
				Length: contentLength,
				LastWriteTimeUtc: File.GetLastWriteTimeUtc(destinationPath),
				contentHash
			);

			return (new WorkspaceFileReplacementResult(WorkspaceFileReplacementOutcome.Replaced, replacementStamp), false);
		}
		catch (Exception moveException) when (moveException is IOException or UnauthorizedAccessException)
		{
			FileStamp? observedOnDiskStamp = await FileStampReader.CaptureOrNullAsync(destinationPath).ConfigureAwait(false);

			var failure = new WorkspaceOperationFailure(
				WorkspaceOperationFailureCodes.ReplacementStateUnknown,
				$"The replace removed '{destinationPath}', and the new content stays in '{temporaryPath}' because moving it into place failed.",
				new AggregateException(replaceException, moveException)
			);

			return (new WorkspaceFileReplacementResult(WorkspaceFileReplacementOutcome.ReplacementStateUnknown, observedOnDiskStamp, failure), true);
		}
	}

	// Removes the temporary file, retrying a sharing or lock violation such as a scanner's brief hold. The cleanup
	// does not observe the caller's token, and it is best effort: a holder that outlasts the retries leaves the
	// file behind rather than masking the operation's result.
	private async Task DeleteTemporaryFileAsync(string temporaryPath)
	{
		try
		{
			await TransientFailureRetry
				.RunAsync<object>(() => _primitives.DeleteFile(temporaryPath), checkPreconditionsAsync: null, CancellationToken.None)
				.ConfigureAwait(false);
		}
		catch (Exception)
		{
			// Cleanup is best effort and must not mask the original failure.
		}
	}

	// Stages the bytes in the caller-chosen temporary path and flushes them to the physical device.
	// Only the durable flush is needed: it flushes the managed buffer and the operating system buffers
	// to the physical device. Without it a power loss can leave a zero-length destination behind an
	// operation that already reported a completed replacement.
	private static async Task WriteTemporaryFileAsync(string temporaryPath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
	{
		FileStream stream = new(
			path: temporaryPath,
			mode: FileMode.CreateNew,
			access: FileAccess.Write,
			share: FileShare.None,
			bufferSize: 64 * 1024,
			options: FileOptions.Asynchronous | FileOptions.SequentialScan
		);

		await using ConfiguredAsyncDisposable streamDisposal = stream.ConfigureAwait(false);

		await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
		stream.Flush(flushToDisk: true);
	}

	// Checks the destination once the temporary file is staged, and again before each retry of the publish. A
	// shape that still agrees with the expected stamp means the destination was not written, so the successful
	// write never reads and hashes the file it is replacing. A metadata difference - the only kind of change a
	// metadata probe can witness - falls back to the full capture that decides the conflict. A null return means
	// the publish may run.
	private static async Task<WorkspaceFileReplacementResult?> CheckStagedDestinationAsync(
		string destinationPath,
		FileStamp expectedStamp,
		CancellationToken cancellationToken
	)
	{
		if (FileStampReader.ShapeDiffers(destinationPath, expectedStamp))
		{
			FileStamp actualStamp = await FileStampReader.CaptureAsync(destinationPath, cancellationToken).ConfigureAwait(false);

			if (EvaluateExpectedStamp(actualStamp, expectedStamp) is { } rejected)
				return rejected;
		}

		// The destination is expected to be absent; a directory occupying the path cannot receive the
		// temporary file, and the missing-file stamp shape does not distinguish it from a free path.
		if (!expectedStamp.Exists && Directory.Exists(destinationPath))
			return CreateDestinationDirectoryResult();

		return null;
	}

	// The shared expected-stamp decision: a directory where the caller expected no file is the
	// deterministic destination-exists result, and every other difference is an external conflict that
	// carries the observed stamp. A null return means the destination still matches the expected stamp,
	// so the write may publish.
	private static WorkspaceFileReplacementResult? EvaluateExpectedStamp(FileStamp actualStamp, FileStamp expectedStamp)
	{
		if (actualStamp.IsDirectory && !expectedStamp.Exists)
			return CreateDestinationDirectoryResult();

		if (actualStamp != expectedStamp)
			return new WorkspaceFileReplacementResult(WorkspaceFileReplacementOutcome.ExternalFileConflict, actualStamp);

		return null;
	}

	// Reports the conflict for a destination the shape probe already proved different, without having
	// staged a temporary file. The full capture carries the observed stamp the host re-baselines against.
	// A destination that changed back to the expected stamp between the probe and the capture is still
	// reported as an external conflict, because it did change under the write.
	private static async Task<WorkspaceFileReplacementResult> RejectStaleDestinationAsync(
		string destinationPath,
		FileStamp expectedStamp,
		CancellationToken cancellationToken
	)
	{
		FileStamp actualStamp = await FileStampReader.CaptureAsync(destinationPath, cancellationToken).ConfigureAwait(false);

		return EvaluateExpectedStamp(actualStamp, expectedStamp)
			?? new WorkspaceFileReplacementResult(WorkspaceFileReplacementOutcome.ExternalFileConflict, actualStamp);
	}

	// The destination path is occupied by a directory where a missing file was expected: the temporary
	// file cannot be moved onto it, and the conflict is deterministic.
	private static WorkspaceFileReplacementResult CreateDestinationDirectoryResult()
	{
		return new(
			WorkspaceFileReplacementOutcome.DestinationExists,
			Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.IsDirectory, "The destination path exists as a directory.")
		);
	}

	// Returns the destination file mode when it can be preserved across a replacement: only an
	// existing file on a Unix-like system has one, and a failed probe falls back to the default mode
	// of the replacement file instead of failing the write.
	private static UnixFileMode? CaptureUnixFileMode(string destinationPath, bool destinationExists)
	{
		if (!destinationExists || OperatingSystem.IsWindows())
			return null;

		try
		{
			return File.GetUnixFileMode(destinationPath);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
		{
			return null;
		}
	}

	// Re-applies the captured mode best-effort: the replacement itself already succeeded, so a mode
	// restore failure must not turn a completed write into a reported failure.
	private static void ApplyUnixFileMode(string destinationPath, UnixFileMode? mode)
	{
		if (mode is not { } fileMode || OperatingSystem.IsWindows())
			return;

		try
		{
			File.SetUnixFileMode(destinationPath, fileMode);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { }
	}

	// Resolves the result of a failed or partially applied replacement from the re-captured destination stamp,
	// which is the only evidence available after the exception. A directory that occupies the expected
	// free destination is a deterministic conflict; a destination that appeared after the stamp check
	// reported it missing is a deterministic conflict; an unchanged destination means the failed
	// operation did not alter it; a destination that already carries the intended content means the
	// replacement completed. Only a failed capture or an unrecognized state stays indeterminate.
	private static WorkspaceFileReplacementResult ResolveFailedReplacement(
		long temporaryLength,
		string temporaryContentHash,
		string destinationPath,
		FileStamp expectedStamp,
		FileStamp? observedOnDiskStamp,
		Exception exception
	)
	{
		if (!expectedStamp.Exists && Directory.Exists(destinationPath))
			return CreateDestinationDirectoryResult();

		if (!expectedStamp.Exists && observedOnDiskStamp is { Exists: true })
			return new WorkspaceFileReplacementResult(WorkspaceFileReplacementOutcome.ExternalFileConflict, observedOnDiskStamp);

		if (observedOnDiskStamp is { } stamp)
		{
			if (stamp == expectedStamp)
			{
				return new WorkspaceFileReplacementResult(
					WorkspaceFileReplacementOutcome.Failed,
					observedOnDiskStamp,
					new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.WriteFailed, exception.Message, exception)
				);
			}

			if (stamp.Length == temporaryLength && string.Equals(stamp.ContentHash, temporaryContentHash, StringComparison.Ordinal))
				return new WorkspaceFileReplacementResult(WorkspaceFileReplacementOutcome.Replaced, observedOnDiskStamp);
		}

		return new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementOutcome.ReplacementStateUnknown,
			observedOnDiskStamp,
			Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ReplacementStateUnknown, exception.Message, exception)
		);
	}
}
