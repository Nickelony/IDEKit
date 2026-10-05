using System.IO;
using Nickelony.IDEKit.Workspace;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.Samples;

/// <summary>
/// Moves a deleted file or folder into a trash folder instead of erasing it, as an editor that sends deletions to a recycle bin does.
/// </summary>
/// <param name="inner">The file system that reads, writes and moves.</param>
/// <param name="trashRoot">The folder that receives one subfolder per deletion.</param>
/// <remarks>
/// The trash lives beside the sample folder, so the explorer and the language servers never see it. A file that changed since the store read
/// it is not moved, which the stamp check of the inner file system reports as a conflict.
/// </remarks>
internal sealed class SampleTrashFileSystem(IWorkspaceFileSystem inner, string trashRoot) : WorkspaceFileSystemDecorator(inner)
{
	/// <summary>The folder that receives the deleted files and folders.</summary>
	internal string TrashRoot { get; } = trashRoot;

	public override async Task<WorkspaceFileDeleteResult> DeleteAsync(
		string path,
		FileStamp expectedStamp,
		CancellationToken cancellationToken = default
	)
	{
		FileStamp actual = await Inner.CaptureStampAsync(path, cancellationToken).ConfigureAwait(false);

		if (actual != expectedStamp)
			return new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.ExternalFileConflict, actual);

		WorkspaceFileMoveResult moved = await Inner
			.MoveAsync(sourcePath: path, destinationPath: NextTrashPath(path), expectedSourceStamp: expectedStamp, cancellationToken)
			.ConfigureAwait(false);

		return moved.Outcome == WorkspaceFileMoveOutcome.Moved
			? new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted)
			: new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.DeleteFailed, Failure: moved.Failure);
	}

	public override async Task<WorkspaceFileDeleteResult> DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
	{
		WorkspaceFileMoveResult moved = await Inner.MoveDirectoryAsync(path, NextTrashPath(path), cancellationToken).ConfigureAwait(false);

		return moved.Outcome == WorkspaceFileMoveOutcome.Moved
			? new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted)
			: new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.DeleteFailed, Failure: moved.Failure);
	}

	// Each deletion gets a folder of its own, so a file deleted twice keeps both copies.
	private string NextTrashPath(string path)
	{
		string folder = Path.Combine(TrashRoot, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}");
		Directory.CreateDirectory(folder);

		return Path.Combine(folder, Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
	}
}
