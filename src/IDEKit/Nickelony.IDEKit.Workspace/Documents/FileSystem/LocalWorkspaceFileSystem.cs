using Nickelony.IDEKit.Core.Pathing;
using System.Buffers;
using System.Security.Cryptography;

namespace Nickelony.IDEKit.Workspace.Documents.FileSystem;

/// <summary>
/// Provides the default local-disk <see cref="IWorkspaceFileSystem"/> implementation used by the document store.
/// </summary>
/// <remarks>
/// <para>
/// Deletion is permanent; see the <see cref="IWorkspaceFileSystem"/> remarks for how a host supplies
/// recoverable deletion such as a trash folder or the shell recycle bin. Text encoding is provided
/// separately by <see cref="WorkspaceTextCodec"/>.
/// </para>
/// <para>
/// A delete validates the path kind: a directory passed to <see cref="DeleteAsync"/> or a file passed
/// to <see cref="DeleteDirectoryAsync"/> is reported as a failed delete instead of a silent success.
/// On Unix-like systems a write preserves the destination's file mode (for example the
/// executable bit) on a best-effort basis; ACLs, extended attributes, and file ownership are not
/// preserved because the write publishes a new file. Writes and moves operate on the
/// path itself and do not follow symbolic links.
/// </para>
/// <para>
/// Permission failures are reported with the
/// <see cref="WorkspaceOperationFailureCodes.AccessDenied"/> failure code by the members whose results
/// carry a failure; <see cref="ReadAsync"/> and <see cref="CaptureStampAsync"/> surface
/// <see cref="UnauthorizedAccessException"/> because their results cannot carry one. The implementation
/// does not retry transient sharing violations on write or move operations; a host that needs
/// bounded retry behavior supplies a decorator.
/// </para>
/// </remarks>
public sealed class LocalWorkspaceFileSystem : IWorkspaceFileSystem
{
	private readonly LocalPathComparisonPolicy _pathComparison;
	private readonly WorkspaceFileOperations _fileOperations;

	/// <summary>
	/// Initializes a new instance of the <see cref="LocalWorkspaceFileSystem"/> class.
	/// </summary>
	/// <param name="options">
	/// The options shared with the document store and the reload coordinator; its comparison
	/// describes the target file system. It decides how a case-only rename (for example
	/// <c>document.txt</c> to <c>Document.txt</c>) is handled: a case-insensitive policy routes the
	/// move through a temporary intermediate path because the destination resolves to the source
	/// file, while a case-sensitive policy treats the destination spelling as a distinct path. The
	/// default follows the operating system; supply the same value that the rest of the layer uses
	/// when the file system's semantics differ from it.
	/// </param>
	public LocalWorkspaceFileSystem(WorkspaceDocumentOptions? options = null)
		: this(options, WorkspaceFileOperations.Default)
	{
	}

	internal LocalWorkspaceFileSystem(WorkspaceDocumentOptions? options, WorkspaceFileOperations fileOperations)
	{
		_pathComparison = (options ?? WorkspaceDocumentOptions.Default).PathComparison;
		_fileOperations = fileOperations;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// For an existing file, the result carries raw bytes in the <see cref="WorkspaceFileReadResult.FromBytes"/>
	/// state; decoding is deferred to the document store. A missing file returns
	/// <see cref="WorkspaceFileReadResult.Missing"/>.
	/// </remarks>
	public async Task<WorkspaceFileReadResult> ReadAsync(
		string path,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(path);

		cancellationToken.ThrowIfCancellationRequested();

		// A directory is not a missing file: reporting it as missing would let the store track a
		// directory path as a new empty document whose later writes and deletes silently fail.
		if (Directory.Exists(path))
			return WorkspaceFileReadResult.Directory;

		if (!File.Exists(path))
			return WorkspaceFileReadResult.Missing;

		byte[] bytes;
		string contentHash;
		try
		{
			(bytes, contentHash) = await ReadBytesAsync(path, cancellationToken).ConfigureAwait(false);
		}
		catch (FileNotFoundException)
		{
			// The file vanished between the existence probe and the open. The requested end state - no
			// file - still holds, so a missing file is reported instead of a load failure.
			return WorkspaceFileReadResult.Missing;
		}
		catch (DirectoryNotFoundException)
		{
			return WorkspaceFileReadResult.Missing;
		}

		FileStamp stamp = CreateStampFromBytes(path, bytes, contentHash, cancellationToken);
		return WorkspaceFileReadResult.FromBytes(stamp, bytes);
	}

	/// <inheritdoc/>
	/// <remarks>Reads and hashes the whole file once, so the capture cost is proportional to the file size.</remarks>
	public async Task<FileStamp> CaptureStampAsync(string path, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(path);

		cancellationToken.ThrowIfCancellationRequested();

		// A directory is not a missing file: reporting it as missing would let the store treat a path
		// occupied by a directory as absent and retarget a document onto it. The directory stamp mirrors
		// the directory read result so both capture paths surface the same state distinctly.
		if (Directory.Exists(path))
			return FileStamp.Directory;

		if (!File.Exists(path))
			return FileStamp.Missing;

		return await CaptureStampFromFileAsync(path, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// <para>
	/// The bytes are written to a temporary file beside the destination and flushed to the physical
	/// device before the destination is touched, so a crash after a reported write cannot expose a
	/// zero-length or partial destination. The temporary file is removed on every path, including
	/// cancellation and failure, so it never escapes this member.
	/// </para>
	/// <para>Existing files are replaced; missing destinations are created.</para>
	/// <para>
	/// The expected stamp is validated against a cheap metadata probe before the bytes are staged, so a
	/// destination that has already changed is rejected without writing or flushing a temporary file. The
	/// same probe is re-run after staging: a destination whose kind, length, and last-write time still agree
	/// is reused as the observed stamp instead of being read and hashed again, while a metadata difference
	/// falls back to the full capture that produces the observed stamp for the conflict.
	/// </para>
	/// <para>The outcome mapping for the non-replaced results:</para>
	/// <list type="bullet">
	/// <item><description>A destination that appears after the stamp check reported it missing is
	/// <see cref="WorkspaceFileReplacementOutcome.ExternalFileConflict"/>: the conflict is deterministic
	/// even though the write raced with it.</description></item>
	/// <item><description>A destination file that is removed between the stamp check and the publish is
	/// <see cref="WorkspaceFileReplacementOutcome.ExternalFileConflict"/>: the destination changed under
	/// the write, so the host re-baselines against the re-captured destination stamp.</description></item>
	/// <item><description>A directory that occupies a destination expected to be missing is
	/// <see cref="WorkspaceFileReplacementOutcome.DestinationExists"/>.</description></item>
	/// <item><description>A canceled token is observed before the destination is touched and reports
	/// <see cref="WorkspaceFileReplacementOutcome.Canceled"/>.</description></item>
	/// <item><description>A write that throws after the destination may have been touched can report
	/// <see cref="WorkspaceFileReplacementOutcome.ReplacementStateUnknown"/> because the final on-disk
	/// state cannot be established reliably.</description></item>
	/// </list>
	/// </remarks>
	public async Task<WorkspaceFileReplacementResult> WriteFileAsync(
		string destinationPath,
		ReadOnlyMemory<byte> content,
		FileStamp expectedStamp,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(destinationPath);

		// The temporary file is created next to the destination so the publish step never crosses
		// volumes, which File.Replace and File.Move do not support. A destination that is itself a
		// volume root has no parent directory; the root is its own directory.
		string directory = Path.GetDirectoryName(destinationPath)
			?? Path.GetPathRoot(destinationPath)
			?? throw new InvalidOperationException($"The destination path '{destinationPath}' has no directory.");
		string temporaryPath = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");

		// The hash of the bytes that are about to be staged is computed up front: the success stamp and
		// the post-failure classification both compare it against the destination's re-captured stamp.
		string contentHash = Convert.ToHexString(SHA256.HashData(content.Span));

		try
		{
			cancellationToken.ThrowIfCancellationRequested();

			// Reject a destination whose cheap metadata shape already disagrees with the expected stamp
			// before staging. The full stamps cannot be equal when the kind, length, or last-write time
			// differs, so the common stale-expectation path never writes and durably flushes a temporary file
			// that it would immediately discard. The same probe is re-run after staging: a shape that still
			// agrees reuses the expected stamp, and only a metadata difference captures the content hash that
			// decides the conflict.
			if (DestinationShapeDiffers(destinationPath, expectedStamp))
				return await RejectStaleDestinationAsync(destinationPath, expectedStamp, cancellationToken).ConfigureAwait(false);

			await WriteTemporaryFileAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);

			cancellationToken.ThrowIfCancellationRequested();

			// Re-probe the cheap metadata after staging. A shape that still agrees with the expected stamp
			// means the destination was not written, so the pre-published stamp is reused and the successful
			// write never reads and hashes the file it is replacing. A metadata difference - the only kind of
			// change a metadata probe can witness - falls back to the full capture that decides the conflict.
			if (DestinationShapeDiffers(destinationPath, expectedStamp))
			{
				FileStamp actualStamp = await CaptureStampAsync(destinationPath, cancellationToken).ConfigureAwait(false);

				if (EvaluateExpectedStamp(actualStamp, expectedStamp) is { } rejected)
					return rejected;
			}

			// The destination is expected to be absent; a directory occupying the path cannot receive the
			// temporary file, and the missing-file stamp shape does not distinguish it from a free path.
			if (!expectedStamp.Exists && Directory.Exists(destinationPath))
				return CreateDestinationDirectoryResult();

			cancellationToken.ThrowIfCancellationRequested();

			// A write publishes the temporary file, so the destination's file mode (for example
			// the executable bit) is captured and re-applied on Unix-like systems; ACLs, extended
			// attributes, and ownership are not carried across the new file and are not preserved.
			UnixFileMode? preservedMode = CaptureUnixFileMode(destinationPath, expectedStamp.Exists);

			if (expectedStamp.Exists)
				_fileOperations.Replace(temporaryPath, destinationPath);
			else
				_fileOperations.MoveFile(temporaryPath, destinationPath);

			ApplyUnixFileMode(destinationPath, preservedMode);

			DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(destinationPath);
			FileStamp replacementStamp = new(
				true,
				content.Length,
				lastWriteTimeUtc,
				contentHash);
			return new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementOutcome.Replaced,
				replacementStamp);
		}
		catch (OperationCanceledException)
		{
			return new WorkspaceFileReplacementResult(WorkspaceFileReplacementOutcome.Canceled);
		}
		catch (UnauthorizedAccessException exception)
		{
			return new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementOutcome.Failed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.AccessDenied, exception.Message, exception));
		}
		catch (IOException exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
		{
			// A FileNotFoundException means the file the caller expected at the destination was removed
			// between the stamp check and the publish; a write that expected the destination to be free
			// instead has no free path once a file occupies it. Both are destination changes the host
			// re-baselines against, so they are reported as an external conflict with the re-captured
			// destination stamp. A missing destination directory is a genuine write failure - nothing was
			// published and no tracked destination changed - so it keeps the WriteFailed code.
			FileStamp? observedOnDiskStamp = await TryCaptureStampAsync(destinationPath).ConfigureAwait(false);
			if (exception is FileNotFoundException || (!expectedStamp.Exists && observedOnDiskStamp is { Exists: true }))
				return new WorkspaceFileReplacementResult(
					WorkspaceFileReplacementOutcome.ExternalFileConflict,
					observedOnDiskStamp);

			return new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementOutcome.Failed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.WriteFailed, exception.Message, exception));
		}
		catch (IOException exception)
		{
			FileStamp? observedOnDiskStamp = await TryCaptureStampAsync(destinationPath).ConfigureAwait(false);
			return ClassifyReplacementFailure(content.Length, contentHash, destinationPath, expectedStamp, observedOnDiskStamp, exception);
		}
		catch (PlatformNotSupportedException exception)
		{
			FileStamp? observedOnDiskStamp = await TryCaptureStampAsync(destinationPath).ConfigureAwait(false);
			return ClassifyReplacementFailure(content.Length, contentHash, destinationPath, expectedStamp, observedOnDiskStamp, exception);
		}
		finally
		{
			TryDelete(temporaryPath);
		}
	}

	// Stages the bytes in the caller-chosen temporary path and flushes them to the physical device.
	// Only the durable flush is needed: it flushes the managed buffer and the operating system buffers
	// to the physical device. Without it a power loss can leave a zero-length destination behind an
	// operation that already reported a completed replacement.
	private static async Task WriteTemporaryFileAsync(
		string temporaryPath,
		ReadOnlyMemory<byte> content,
		CancellationToken cancellationToken)
	{
		await using FileStream stream = new(
			temporaryPath,
			FileMode.CreateNew,
			FileAccess.Write,
			FileShare.None,
			64 * 1024,
			FileOptions.Asynchronous | FileOptions.SequentialScan);
		await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
		stream.Flush(flushToDisk: true);
	}

	// A cheap metadata probe that only ever proves a mismatch: the destination's kind, length, and
	// last-write time already differ from the expected stamp, so the full stamps cannot be equal because
	// the content hash is the only field left out. An agreement proves nothing - the content hash still
	// has to be compared - so the caller still re-checks after staging. Reading metadata costs a few stat
	// calls instead of a full read and hash of the destination.
	private static bool DestinationShapeDiffers(string destinationPath, FileStamp expectedStamp)
	{
		if (Directory.Exists(destinationPath) != expectedStamp.IsDirectory)
			return true;

		if (expectedStamp.IsDirectory)
			return false;

		bool destinationExists = File.Exists(destinationPath);
		if (destinationExists != expectedStamp.Exists)
			return true;

		if (!destinationExists)
			return false;

		try
		{
			return new FileInfo(destinationPath).Length != expectedStamp.Length
				|| File.GetLastWriteTimeUtc(destinationPath) != expectedStamp.LastWriteTimeUtc;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// The destination changed or became unreadable between the existence probe and the metadata
			// read: report a difference so the rejection path captures the observed state.
			return true;
		}
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
			return new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementOutcome.ExternalFileConflict,
				actualStamp);

		return null;
	}

	// Reports the conflict for a destination the shape probe already proved different, without having
	// staged a temporary file. The full capture carries the observed stamp the host re-baselines against.
	// A destination that changed back to the expected stamp between the probe and the capture is still
	// reported as an external conflict, because it did change under the write.
	private async Task<WorkspaceFileReplacementResult> RejectStaleDestinationAsync(
		string destinationPath,
		FileStamp expectedStamp,
		CancellationToken cancellationToken)
	{
		FileStamp actualStamp = await CaptureStampAsync(destinationPath, cancellationToken).ConfigureAwait(false);
		return EvaluateExpectedStamp(actualStamp, expectedStamp)
			?? new WorkspaceFileReplacementResult(WorkspaceFileReplacementOutcome.ExternalFileConflict, actualStamp);
	}

	// The destination path is occupied by a directory where a missing file was expected: the temporary
	// file cannot be moved onto it, and the conflict is deterministic.
	private static WorkspaceFileReplacementResult CreateDestinationDirectoryResult()
		=> new(
			WorkspaceFileReplacementOutcome.DestinationExists,
			Failure: new WorkspaceOperationFailure(
				WorkspaceOperationFailureCodes.IsDirectory,
				"The destination path exists as a directory."));

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
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
		{
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// A case-only rename on a case-insensitive target uses a temporary intermediate path, so the file
	/// is briefly renamed before it receives the requested spelling. If the rollback move of that path
	/// also fails, the file can be left at the intermediate path and the failure is reported as
	/// <see cref="WorkspaceFileMoveOutcome.MoveStateUnknown"/> with an exception that aggregates the
	/// original move failure and the rollback failure; the document store keeps tracking the source
	/// identity. A successful move reports the resulting destination stamp, which the document store
	/// installs as its baseline.
	/// </remarks>
	public async Task<WorkspaceFileMoveResult> MoveAsync(
		string sourcePath,
		string destinationPath,
		FileStamp expectedSourceStamp,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(sourcePath);
		ArgumentNullException.ThrowIfNull(destinationPath);

		bool isCaseOnlyRename = false;

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			FileStamp actualSourceStamp = await CaptureStampAsync(sourcePath, cancellationToken).ConfigureAwait(false);
			if (actualSourceStamp != expectedSourceStamp)
				return new WorkspaceFileMoveResult(
					WorkspaceFileMoveOutcome.ExternalFileConflict,
					actualSourceStamp);

			isCaseOnlyRename = IsCaseOnlyRename(sourcePath, destinationPath);
			if (!isCaseOnlyRename && (File.Exists(destinationPath) || Directory.Exists(destinationPath)))
				return new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.DestinationExists);

			cancellationToken.ThrowIfCancellationRequested();
			MoveWithCaseOnlyRenameHandling(
				sourcePath,
				destinationPath,
				isCaseOnlyRename,
				_fileOperations.MoveFile,
				File.Exists);

			// The moved file keeps its bytes, so its content hash is the captured source stamp's; only the
			// length and last-write time are read back from the destination. Reporting the resulting stamp
			// lets the store install the true baseline instead of echoing the pre-move stamp, which a
			// cross-volume move's new write time would turn into a spurious conflict on the next write.
			FileStamp movedStamp = actualSourceStamp with
			{
				Length = new FileInfo(destinationPath).Length,
				LastWriteTimeUtc = File.GetLastWriteTimeUtc(destinationPath),
			};

			return new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved, movedStamp);
		}
		catch (Exception exception)
		{
			return MapMoveFailure(
				exception,
				isCaseOnlyRename,
				() => File.Exists(destinationPath) || Directory.Exists(destinationPath));
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The file is deleted permanently. A path that no longer exists after a matching stamp is reported
	/// as <see cref="WorkspaceFileDeleteOutcome.Deleted"/> because the requested end state already holds;
	/// a path that exists as a directory is reported as <see cref="WorkspaceFileDeleteOutcome.DeleteFailed"/>
	/// with <see cref="WorkspaceOperationFailureCodes.IsDirectory"/>.
	/// </remarks>
	public async Task<WorkspaceFileDeleteResult> DeleteAsync(
		string path,
		FileStamp expectedStamp,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(path);

		try
		{
			cancellationToken.ThrowIfCancellationRequested();

			// A directory is not a file that is already absent: reporting it as deleted would claim a
			// completed delete while the directory still occupies the path.
			if (Directory.Exists(path))
			{
				return new WorkspaceFileDeleteResult(
					WorkspaceFileDeleteOutcome.DeleteFailed,
					Failure: new WorkspaceOperationFailure(
						WorkspaceOperationFailureCodes.IsDirectory,
						"The path exists as a directory."));
			}

			FileStamp actualStamp = await CaptureStampAsync(path, cancellationToken).ConfigureAwait(false);
			if (actualStamp != expectedStamp)
				return new WorkspaceFileDeleteResult(
					WorkspaceFileDeleteOutcome.ExternalFileConflict,
					actualStamp);

			cancellationToken.ThrowIfCancellationRequested();
			if (File.Exists(path))
				File.Delete(path);

			return new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted);
		}
		catch (OperationCanceledException)
		{
			return new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Canceled);
		}
		catch (UnauthorizedAccessException exception)
		{
			return new WorkspaceFileDeleteResult(
				WorkspaceFileDeleteOutcome.DeleteFailed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.AccessDenied, exception.Message, exception));
		}
		catch (Exception exception)
		{
			return new WorkspaceFileDeleteResult(
				WorkspaceFileDeleteOutcome.DeleteFailed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.DeleteFailed, exception.Message, exception));
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The move runs synchronously, so the returned task is already completed and
	/// <paramref name="cancellationToken"/> is observed before the move starts. A case-only rename on
	/// a case-insensitive target uses a temporary intermediate path like <see cref="MoveAsync(string, string, FileStamp, CancellationToken)"/>.
	/// </remarks>
	public Task<WorkspaceFileMoveResult> MoveDirectoryAsync(
		string sourcePath,
		string destinationPath,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(sourcePath);
		ArgumentNullException.ThrowIfNull(destinationPath);

		bool isCaseOnlyRename = false;

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!Directory.Exists(sourcePath))
				return Task.FromResult(new WorkspaceFileMoveResult(
					WorkspaceFileMoveOutcome.MoveFailed,
					Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.MoveFailed, "The source directory does not exist.")));

			isCaseOnlyRename = IsCaseOnlyRename(sourcePath, destinationPath);
			if (!isCaseOnlyRename && (Directory.Exists(destinationPath) || File.Exists(destinationPath)))
				return Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.DestinationExists));

			cancellationToken.ThrowIfCancellationRequested();
			MoveWithCaseOnlyRenameHandling(
				sourcePath,
				destinationPath,
				isCaseOnlyRename,
				_fileOperations.MoveDirectory,
				Directory.Exists);

			return Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved));
		}
		catch (Exception exception)
		{
			return Task.FromResult(MapMoveFailure(
				exception,
				isCaseOnlyRename,
				() => Directory.Exists(destinationPath) || File.Exists(destinationPath)));
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The directory is deleted permanently. A path that no longer exists is reported as
	/// <see cref="WorkspaceFileDeleteOutcome.Deleted"/> because the requested end state already holds;
	/// a path that exists as a file is reported as <see cref="WorkspaceFileDeleteOutcome.DeleteFailed"/>
	/// with <see cref="WorkspaceOperationFailureCodes.NotDirectory"/>. The deletion runs synchronously,
	/// so the returned task is already completed and <paramref name="cancellationToken"/> is observed
	/// before the deletion starts.
	/// </remarks>
	public Task<WorkspaceFileDeleteResult> DeleteDirectoryAsync(
		string path,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(path);

		try
		{
			cancellationToken.ThrowIfCancellationRequested();

			// A file is not a directory that is already absent: reporting it as deleted would claim a
			// completed delete while the file still occupies the path.
			if (File.Exists(path))
			{
				return Task.FromResult(new WorkspaceFileDeleteResult(
					WorkspaceFileDeleteOutcome.DeleteFailed,
					Failure: new WorkspaceOperationFailure(
						WorkspaceOperationFailureCodes.NotDirectory,
						"The path exists as a file.")));
			}

			cancellationToken.ThrowIfCancellationRequested();
			if (Directory.Exists(path))
				Directory.Delete(path, recursive: true);

			return Task.FromResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted));
		}
		catch (OperationCanceledException)
		{
			return Task.FromResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Canceled));
		}
		catch (UnauthorizedAccessException exception)
		{
			return Task.FromResult(new WorkspaceFileDeleteResult(
				WorkspaceFileDeleteOutcome.DeleteFailed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.AccessDenied, exception.Message, exception)));
		}
		catch (Exception exception)
		{
			return Task.FromResult(new WorkspaceFileDeleteResult(
				WorkspaceFileDeleteOutcome.DeleteFailed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.DeleteFailed, exception.Message, exception)));
		}
	}

	// Maps a move failure onto the shared move result. An I/O failure is a destination conflict only
	// for a move that verified the destination absent: a case-only rename resolves to the source
	// itself, so an I/O failure there - or any failure with no destination present - is a real move
	// failure.
	private static WorkspaceFileMoveResult MapMoveFailure(
		Exception exception,
		bool isCaseOnlyRename,
		Func<bool> destinationExists)
	{
		if (exception is OperationCanceledException)
			return new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Canceled);

		if (exception is IOException && !isCaseOnlyRename && destinationExists())
		{
			return new WorkspaceFileMoveResult(
				WorkspaceFileMoveOutcome.DestinationExists,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.DestinationExists, exception.Message, exception));
		}

		if (exception is UnauthorizedAccessException)
		{
			return new WorkspaceFileMoveResult(
				WorkspaceFileMoveOutcome.MoveFailed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.AccessDenied, exception.Message, exception));
		}

		if (exception is AggregateException)
		{
			return new WorkspaceFileMoveResult(
				WorkspaceFileMoveOutcome.MoveStateUnknown,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.MoveStateUnknown, exception.Message, exception));
		}

		return new WorkspaceFileMoveResult(
			WorkspaceFileMoveOutcome.MoveFailed,
			Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.MoveFailed, exception.Message, exception));
	}

	private static void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception)
		{
			// Cleanup is best effort and must not mask the original failure.
		}
	}

	// Classifies a failed or partially applied replacement from the re-captured destination stamp,
	// which is the only evidence available after the exception. A directory that occupies the expected
	// free destination is a deterministic conflict; a destination that appeared after the stamp check
	// reported it missing is a deterministic conflict; an unchanged destination means the failed
	// operation did not alter it; a destination that already carries the intended content means the
	// replacement completed. Only a failed capture or an unrecognized state stays indeterminate.
	private static WorkspaceFileReplacementResult ClassifyReplacementFailure(
		long temporaryLength,
		string temporaryContentHash,
		string destinationPath,
		FileStamp expectedStamp,
		FileStamp? observedOnDiskStamp,
		Exception exception)
	{
		if (!expectedStamp.Exists && Directory.Exists(destinationPath))
			return CreateDestinationDirectoryResult();

		if (!expectedStamp.Exists && observedOnDiskStamp is { Exists: true })
			return new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementOutcome.ExternalFileConflict,
				observedOnDiskStamp);

		if (observedOnDiskStamp is { } stamp)
		{
			if (stamp == expectedStamp)
				return new WorkspaceFileReplacementResult(
					WorkspaceFileReplacementOutcome.Failed,
					observedOnDiskStamp,
					new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.WriteFailed, exception.Message, exception));

			if (stamp.Length == temporaryLength
				&& string.Equals(stamp.ContentHash, temporaryContentHash, StringComparison.Ordinal))
				return new WorkspaceFileReplacementResult(
					WorkspaceFileReplacementOutcome.Replaced,
					observedOnDiskStamp);
		}

		return new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementOutcome.ReplacementStateUnknown,
			observedOnDiskStamp,
			Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ReplacementStateUnknown, exception.Message, exception));
	}

	// A case-only rename targets a path that differs from the source only in casing. On a
	// case-insensitive file system the destination resolves to the source file, so the move needs the
	// intermediate-path strategy instead of being rejected as an existing destination; on a
	// case-sensitive file system it is an ordinary move and an existing variant is a real collision.
	// Separator spelling and a trailing separator are normalized before the comparison so the same
	// rename is detected even when the caller spelled the two paths differently.
	private bool IsCaseOnlyRename(string sourcePath, string destinationPath)
	{
		if (!_pathComparison.IgnoreCase)
			return false;

		string normalizedSource = NormalizeSeparatorSpelling(sourcePath);
		string normalizedDestination = NormalizeSeparatorSpelling(destinationPath);

		return string.Equals(normalizedSource, normalizedDestination, StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(normalizedSource, normalizedDestination, StringComparison.Ordinal);
	}

	private static string NormalizeSeparatorSpelling(string path)
		=> Path.TrimEndingDirectorySeparator(path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));

	// Moves a file or directory, routing a case-only rename on a case-insensitive target through a
	// temporary intermediate path so the destination can be recreated with its new spelling. The move
	// and exists delegates keep the file and directory variants on the same implementation.
	private static void MoveWithCaseOnlyRenameHandling(
		string sourcePath,
		string destinationPath,
		bool isCaseOnlyRename,
		Action<string, string> move,
		Func<string, bool> pathExists)
	{
		if (!isCaseOnlyRename)
		{
			move(sourcePath, destinationPath);
			return;
		}

		string intermediatePath = sourcePath + "." + Guid.NewGuid().ToString("N") + ".rename";
		move(sourcePath, intermediatePath);
		try
		{
			move(intermediatePath, destinationPath);
		}
		catch (Exception moveException)
		{
			if (!pathExists(intermediatePath))
			{
				// The intermediate path is gone: the move may have completed before throwing, so the final
				// location cannot be established. The aggregate maps to MoveStateUnknown.
				throw new AggregateException(
					"The case-only rename failed and the file's final location could not be established.",
					moveException);
			}

			try
			{
				move(intermediatePath, sourcePath);
			}
			catch (Exception rollbackException)
			{
				// The rollback failure must not replace the original move failure: the aggregate keeps
				// both causes, and a caller that wants the classification reads the move exception.
				throw new AggregateException(
					"The case-only rename failed and the source path could not be restored.",
					moveException,
					rollbackException);
			}

			throw;
		}
	}

	// Reads the file into a pre-sized buffer instead of growing a MemoryStream and copying it again
	// with ToArray, and hashes the bytes while reading so the content is scanned once; a concurrent
	// writer can make the final length differ from the initial one.
	private static async Task<(byte[] Bytes, string ContentHash)> ReadBytesAsync(string path, CancellationToken cancellationToken)
	{
		await using FileStream stream = new(
			path,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite | FileShare.Delete,
			64 * 1024,
			FileOptions.Asynchronous | FileOptions.SequentialScan);

		using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		long fileLength = stream.Length;
		if (fileLength == 0)
			return ([], Convert.ToHexString(hash.GetHashAndReset()));

		if (fileLength > int.MaxValue)
			throw new IOException($"The file '{path}' is {fileLength} bytes and is too large to read into memory.");

		byte[] bytes = new byte[(int)fileLength];
		int offset = 0;
		while (offset < bytes.Length)
		{
			int read = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken).ConfigureAwait(false);
			if (read == 0)
				break;

			hash.AppendData(bytes, offset, read);
			offset += read;
		}

		return (offset == bytes.Length ? bytes : bytes[..offset], Convert.ToHexString(hash.GetHashAndReset()));
	}

	// Streams the file once and hashes while reading so the length and hash describe the same byte
	// sequence without buffering the whole file in memory.
	private static async Task<FileStamp> CaptureStampFromFileAsync(string path, CancellationToken cancellationToken)
	{
		await using FileStream stream = new(
			path,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite | FileShare.Delete,
			64 * 1024,
			FileOptions.Asynchronous | FileOptions.SequentialScan);

		using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
		long length = 0;
		try
		{
			int read;
			while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
			{
				hash.AppendData(buffer, 0, read);
				length += read;
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}

		return new FileStamp(
			true,
			length,
			File.GetLastWriteTimeUtc(path),
			Convert.ToHexString(hash.GetHashAndReset()));
	}

	private static FileStamp CreateStampFromBytes(
		string path,
		ReadOnlyMemory<byte> bytes,
		string contentHash,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return new FileStamp(true, bytes.Length, File.GetLastWriteTimeUtc(path), contentHash);
	}

	private static async Task<FileStamp?> TryCaptureStampAsync(string path)
	{
		try
		{
			if (!File.Exists(path))
				return FileStamp.Missing;

			return await CaptureStampFromFileAsync(path, CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
			or NotSupportedException or ArgumentException)
		{
			return null;
		}
	}
}
