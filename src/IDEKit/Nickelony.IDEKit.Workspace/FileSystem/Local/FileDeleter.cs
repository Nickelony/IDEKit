namespace Nickelony.IDEKit.Workspace.FileSystem;

/// <summary>Deletes a local file under its expected stamp, or a directory recursively.</summary>
/// <remarks>
/// A path that is absent after a matching stamp counts as deleted, because the requested end state already holds. A directory passed to a file delete
/// and a file passed to a directory delete fail instead of succeeding silently.
/// </remarks>
internal sealed class FileDeleter
{
	private readonly WorkspaceFileSystemPrimitives _primitives;

	internal FileDeleter(WorkspaceFileSystemPrimitives primitives) => _primitives = primitives;

	internal async Task<WorkspaceFileDeleteResult> DeleteAsync(string path, FileStamp expectedStamp, CancellationToken cancellationToken)
	{
		try
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (await CheckFileDeletePreconditionsAsync(path, expectedStamp, isRetry: false, cancellationToken).ConfigureAwait(false) is { } rejected)
				return rejected;

			cancellationToken.ThrowIfCancellationRequested();

			WorkspaceFileDeleteResult? rejectedOnRetry = await TransientFailureRetry
				.RunAsync(
					() =>
					{
						if (File.Exists(path))
							_primitives.DeleteFile(path);
					},

					token => CheckFileDeletePreconditionsAsync(path, expectedStamp, isRetry: true, cancellationToken: token),
					cancellationToken
				)
				.ConfigureAwait(false);

			return rejectedOnRetry ?? new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted);
		}
		catch (OperationCanceledException)
		{
			return new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Canceled);
		}
		catch (UnauthorizedAccessException exception)
		{
			return new WorkspaceFileDeleteResult(
				WorkspaceFileDeleteOutcome.DeleteFailed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.AccessDenied, exception.Message, exception)
			);
		}
		catch (Exception exception)
		{
			return new WorkspaceFileDeleteResult(
				WorkspaceFileDeleteOutcome.DeleteFailed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.DeleteFailed, exception.Message, exception)
			);
		}
	}

	// Checks a file delete before it runs, and again before each retry: the path must not be a directory, and the
	// file must still match its expected stamp. A retry reads the hash only when the metadata changed, for the
	// reason the move check gives. A null return means the delete may run.
	private static async Task<WorkspaceFileDeleteResult?> CheckFileDeletePreconditionsAsync(
		string path,
		FileStamp expectedStamp,
		bool isRetry,
		CancellationToken cancellationToken
	)
	{
		// A directory is not a file that is already absent: reporting it as deleted would claim a
		// completed delete while the directory still occupies the path.
		if (Directory.Exists(path))
		{
			return new WorkspaceFileDeleteResult(
				WorkspaceFileDeleteOutcome.DeleteFailed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.IsDirectory, "The path exists as a directory.")
			);
		}

		if (isRetry && !FileStampReader.ShapeDiffers(path, expectedStamp))
			return null;

		FileStamp actualStamp = await FileStampReader.CaptureAsync(path, cancellationToken).ConfigureAwait(false);

		if (actualStamp != expectedStamp)
			return new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.ExternalFileConflict, actualStamp);

		return null;
	}

	internal static Task<WorkspaceFileDeleteResult> DeleteDirectoryAsync(string path, CancellationToken cancellationToken)
	{
		try
		{
			cancellationToken.ThrowIfCancellationRequested();

			// A file is not a directory that is already absent: reporting it as deleted would claim a
			// completed delete while the file still occupies the path.
			if (File.Exists(path))
			{
				return Task.FromResult(
					new WorkspaceFileDeleteResult(
						WorkspaceFileDeleteOutcome.DeleteFailed,
						Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.NotDirectory, "The path exists as a file.")
					)
				);
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
			return Task.FromResult(
				new WorkspaceFileDeleteResult(
					WorkspaceFileDeleteOutcome.DeleteFailed,
					Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.AccessDenied, exception.Message, exception)
				)
			);
		}
		catch (Exception exception)
		{
			return Task.FromResult(
				new WorkspaceFileDeleteResult(
					WorkspaceFileDeleteOutcome.DeleteFailed,
					Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.DeleteFailed, exception.Message, exception)
				)
			);
		}
	}
}
