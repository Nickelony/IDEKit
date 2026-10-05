using Nickelony.IDEKit.Core.Paths;

namespace Nickelony.IDEKit.Workspace.FileSystem;

/// <summary>Moves a local file or directory, and routes a case-only move on a case-insensitive target through an intermediate path.</summary>
/// <remarks>
/// A file move checks its expected stamp and a free destination before it runs, and again before each retry. A case-only move on a case-insensitive
/// target cannot be rejected as an existing destination, because the destination resolves to the source file, so the item is moved to a temporary
/// intermediate path first; when the rollback of that move also fails, the item can stay at the intermediate path and the failure is reported
/// as an unknown move state.
/// </remarks>
internal sealed class FileMover
{
	private readonly LocalPathComparisonPolicy _pathComparison;
	private readonly WorkspaceFileSystemPrimitives _primitives;

	internal FileMover(LocalPathComparisonPolicy pathComparison, WorkspaceFileSystemPrimitives primitives)
	{
		_pathComparison = pathComparison;
		_primitives = primitives;
	}

	internal async Task<WorkspaceFileMoveResult> MoveAsync(
		string sourcePath,
		string destinationPath,
		FileStamp expectedSourceStamp,
		CancellationToken cancellationToken
	)
	{
		bool isCaseOnlyMove = false;

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			isCaseOnlyMove = IsCaseOnlyMove(sourcePath, destinationPath);

			WorkspaceFileMoveResult? rejected = await CheckFileMovePreconditionsAsync(
					sourcePath,
					destinationPath,
					expectedSourceStamp,
					isCaseOnlyMove,
					isRetry: false,
					cancellationToken
				)
				.ConfigureAwait(false);

			if (rejected is not null)
				return rejected;

			cancellationToken.ThrowIfCancellationRequested();

			WorkspaceFileMoveResult? rejectedOnRetry = await MoveWithCaseOnlyHandlingAsync(
					sourcePath,
					destinationPath,
					isCaseOnlyMove,
					move: _primitives.MoveFile,
					pathExists: File.Exists,

					checkPreconditionsAsync: token =>
						CheckFileMovePreconditionsAsync(
							sourcePath,
							destinationPath,
							expectedSourceStamp,
							isCaseOnlyMove,
							isRetry: true,
							cancellationToken: token
						),

					cancellationToken
				)
				.ConfigureAwait(false);

			if (rejectedOnRetry is not null)
				return rejectedOnRetry;

			// The moved file keeps its bytes, so its content hash is the source stamp's, which the precondition
			// check proved current; only the length and last-write time are read back from the destination.
			// Reporting the resulting stamp lets the store install the true baseline instead of echoing the
			// pre-move stamp, which a cross-volume move's new write time would turn into a spurious conflict on
			// the next write.
			FileStamp movedStamp = expectedSourceStamp with
			{
				Length = new FileInfo(destinationPath).Length,
				LastWriteTimeUtc = File.GetLastWriteTimeUtc(destinationPath),
			};

			return new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved, movedStamp);
		}
		catch (Exception exception)
		{
			return MapMoveFailure(exception, isCaseOnlyMove, () => File.Exists(destinationPath) || Directory.Exists(destinationPath));
		}
	}

	// Checks a file move before it runs, and again before each retry: the source must still match its expected
	// stamp, and a destination other than the source's own case variant must be free. A retry reads the hash of
	// the source only when its metadata changed, because the holder whose sharing violation the retry waits out
	// may deny reading the file as well; a change that keeps the length and the last-write time goes unseen on a
	// retry, as it does for the staged check of a write. A null return means the move may run.
	private static async Task<WorkspaceFileMoveResult?> CheckFileMovePreconditionsAsync(
		string sourcePath,
		string destinationPath,
		FileStamp expectedSourceStamp,
		bool isCaseOnlyMove,
		bool isRetry,
		CancellationToken cancellationToken
	)
	{
		if (!isRetry || FileStampReader.ShapeDiffers(sourcePath, expectedSourceStamp))
		{
			FileStamp actualSourceStamp = await FileStampReader.CaptureAsync(sourcePath, cancellationToken).ConfigureAwait(false);

			if (actualSourceStamp != expectedSourceStamp)
				return new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.ExternalFileConflict, actualSourceStamp);
		}

		if (!isCaseOnlyMove && (File.Exists(destinationPath) || Directory.Exists(destinationPath)))
			return new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.DestinationExists);

		return null;
	}

	internal async Task<WorkspaceFileMoveResult> MoveDirectoryAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
	{
		bool isCaseOnlyMove = false;

		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			isCaseOnlyMove = IsCaseOnlyMove(sourcePath, destinationPath);

			if (CheckDirectoryMovePreconditions(sourcePath, destinationPath, isCaseOnlyMove) is { } rejected)
				return rejected;

			cancellationToken.ThrowIfCancellationRequested();

			WorkspaceFileMoveResult? rejectedOnRetry = await MoveWithCaseOnlyHandlingAsync(
					sourcePath,
					destinationPath,
					isCaseOnlyMove,
					move: _primitives.MoveDirectory,
					pathExists: Directory.Exists,
					checkPreconditionsAsync: _ => Task.FromResult(CheckDirectoryMovePreconditions(sourcePath, destinationPath, isCaseOnlyMove)),
					cancellationToken
				)
				.ConfigureAwait(false);

			return rejectedOnRetry ?? new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved);
		}
		catch (Exception exception)
		{
			return MapMoveFailure(exception, isCaseOnlyMove, () => Directory.Exists(destinationPath) || File.Exists(destinationPath));
		}
	}

	// Checks a directory move before it runs, and again before each retry: the source directory must exist, and a
	// destination other than the source's own case variant must be free. A null return means the move may run.
	private static WorkspaceFileMoveResult? CheckDirectoryMovePreconditions(string sourcePath, string destinationPath, bool isCaseOnlyMove)
	{
		if (!Directory.Exists(sourcePath))
		{
			return new WorkspaceFileMoveResult(
				WorkspaceFileMoveOutcome.MoveFailed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.MoveFailed, "The source directory does not exist.")
			);
		}

		if (!isCaseOnlyMove && (Directory.Exists(destinationPath) || File.Exists(destinationPath)))
			return new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.DestinationExists);

		return null;
	}

	// Maps a move failure onto the shared move result. An I/O failure is a destination conflict only
	// for a move that verified the destination absent: a case-only move resolves to the source
	// itself, so an I/O failure there - or any failure with no destination present - is a real move
	// failure.
	private static WorkspaceFileMoveResult MapMoveFailure(Exception exception, bool isCaseOnlyMove, Func<bool> destinationExists)
	{
		if (exception is OperationCanceledException)
			return new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Canceled);

		if (exception is IOException && !isCaseOnlyMove && destinationExists())
		{
			return new WorkspaceFileMoveResult(
				WorkspaceFileMoveOutcome.DestinationExists,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.DestinationExists, exception.Message, exception)
			);
		}

		if (exception is UnauthorizedAccessException)
		{
			return new WorkspaceFileMoveResult(
				WorkspaceFileMoveOutcome.MoveFailed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.AccessDenied, exception.Message, exception)
			);
		}

		if (exception is AggregateException)
		{
			return new WorkspaceFileMoveResult(
				WorkspaceFileMoveOutcome.MoveStateUnknown,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.MoveStateUnknown, exception.Message, exception)
			);
		}

		return new WorkspaceFileMoveResult(
			WorkspaceFileMoveOutcome.MoveFailed,
			Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.MoveFailed, exception.Message, exception)
		);
	}

	// A case-only move targets a path that differs from the source only in casing. On a
	// case-insensitive file system the destination resolves to the source file, so the move needs the
	// intermediate-path strategy instead of being rejected as an existing destination; on a
	// case-sensitive file system it is an ordinary move and an existing variant is a real collision.
	// Separator spelling and a trailing separator are normalized before the comparison so the same
	// move is detected even when the caller spelled the two paths differently.
	private bool IsCaseOnlyMove(string sourcePath, string destinationPath)
	{
		if (!_pathComparison.IgnoreCase)
			return false;

		string normalizedSource = NormalizeSeparatorSpelling(sourcePath);
		string normalizedDestination = NormalizeSeparatorSpelling(destinationPath);

		return string.Equals(normalizedSource, normalizedDestination, StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(normalizedSource, normalizedDestination, StringComparison.Ordinal);
	}

	private static string NormalizeSeparatorSpelling(string path) =>
		Path.TrimEndingDirectorySeparator(path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));

	// Moves a file or directory, routing a case-only move on a case-insensitive target through a
	// temporary intermediate path so the destination can be recreated with its new spelling. The move
	// and exists delegates keep the file and directory variants on the same implementation. The first
	// move runs under the precondition check and the token, and returns the check's result when it rejects
	// a retry. Once the file sits at the intermediate path, the move to the destination and the rollback
	// each retry a sharing or lock violation in place without observing the token, because a cancellation
	// there would strand the file under the intermediate name.
	private static async Task<TResult?> MoveWithCaseOnlyHandlingAsync<TResult>(
		string sourcePath,
		string destinationPath,
		bool isCaseOnlyMove,
		Action<string, string> move,
		Func<string, bool> pathExists,
		Func<CancellationToken, Task<TResult?>> checkPreconditionsAsync,
		CancellationToken cancellationToken
	)
		where TResult : class
	{
		if (!isCaseOnlyMove)
		{
			return await TransientFailureRetry
				.RunAsync(() => move(sourcePath, destinationPath), checkPreconditionsAsync, cancellationToken)
				.ConfigureAwait(false);
		}

		string intermediatePath = sourcePath + "." + Guid.NewGuid().ToString("N") + ".rename";

		TResult? rejected = await TransientFailureRetry
			.RunAsync(() => move(sourcePath, intermediatePath), checkPreconditionsAsync, cancellationToken)
			.ConfigureAwait(false);

		if (rejected is not null)
			return rejected;

		try
		{
			await TransientFailureRetry
				.RunAsync<TResult>(() => move(intermediatePath, destinationPath), checkPreconditionsAsync: null, CancellationToken.None)
				.ConfigureAwait(false);

			return null;
		}
		catch (Exception moveException)
		{
			if (!pathExists(intermediatePath))
			{
				// The intermediate path is gone: the move may have completed before throwing, so the final
				// location cannot be established. The aggregate maps to MoveStateUnknown.
				throw new AggregateException("The case-only move failed and the file's final location could not be established.", moveException);
			}

			try
			{
				await TransientFailureRetry
					.RunAsync<TResult>(() => move(intermediatePath, sourcePath), checkPreconditionsAsync: null, CancellationToken.None)
					.ConfigureAwait(false);
			}
			catch (Exception rollbackException)
			{
				// The rollback failure must not replace the original move failure: the aggregate keeps
				// both causes, and a caller that wants the classification reads the move exception.
				throw new AggregateException(
					"The case-only move failed and the source path could not be restored.",
					moveException,
					rollbackException
				);
			}

			throw;
		}
	}
}
