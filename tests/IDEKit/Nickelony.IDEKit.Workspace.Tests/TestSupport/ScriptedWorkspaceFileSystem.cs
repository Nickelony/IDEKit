using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>A file-system double that logs every call and answers each one from a handler the test sets.</summary>
/// <remarks>
/// Each member appends <c>Member:path</c> to <see cref="Calls"/> before it runs its handler, so a test checks the order of the calls.
/// A handler that is not set answers as an empty workspace: a read finds a missing file, a capture reports a missing stamp, and a write,
/// move or delete succeeds without reporting a stamp.
/// </remarks>
internal sealed class ScriptedWorkspaceFileSystem : IWorkspaceFileSystem
{
	private readonly object _gate = new();
	private readonly List<string> _calls = [];

	/// <summary>The calls so far, oldest first, each as <c>Member:path</c>.</summary>
	public IReadOnlyList<string> Calls
	{
		get
		{
			lock (_gate)
				return [.. _calls];
		}
	}

	public Func<string, CancellationToken, Task<WorkspaceFileReadResult>> OnRead { get; set; } =
		static (_, _) => Task.FromResult(WorkspaceFileReadResult.Missing);

	public Func<string, CancellationToken, Task<FileStamp>> OnCaptureStamp { get; set; } = static (_, _) => Task.FromResult(FileStamp.Missing);

	public Func<string, ReadOnlyMemory<byte>, FileStamp, CancellationToken, Task<WorkspaceFileReplacementResult>> OnWrite { get; set; } =
		static (_, _, _, _) => Task.FromResult(new WorkspaceFileReplacementResult(WorkspaceFileReplacementOutcome.Replaced));

	public Func<string, string, FileStamp, CancellationToken, Task<WorkspaceFileMoveResult>> OnMove { get; set; } =
		static (_, _, _, _) => Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved));

	public Func<string, string, CancellationToken, Task<WorkspaceFileMoveResult>> OnMoveDirectory { get; set; } =
		static (_, _, _) => Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved));

	public Func<string, FileStamp, CancellationToken, Task<WorkspaceFileDeleteResult>> OnDelete { get; set; } =
		static (_, _, _) => Task.FromResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted));

	public Func<string, CancellationToken, Task<WorkspaceFileDeleteResult>> OnDeleteDirectory { get; set; } =
		static (_, _) => Task.FromResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted));

	public Task<WorkspaceFileReadResult> ReadAsync(string path, CancellationToken cancellationToken = default)
	{
		Record("Read", path);
		return OnRead(path, cancellationToken);
	}

	public Task<FileStamp> CaptureStampAsync(string path, CancellationToken cancellationToken = default)
	{
		Record("Capture", path);
		return OnCaptureStamp(path, cancellationToken);
	}

	public Task<WorkspaceFileReplacementResult> WriteFileAsync(
		string destinationPath,
		ReadOnlyMemory<byte> content,
		FileStamp expectedStamp,
		CancellationToken cancellationToken = default
	)
	{
		Record("Write", destinationPath);
		return OnWrite(destinationPath, content, expectedStamp, cancellationToken);
	}

	public Task<WorkspaceFileMoveResult> MoveAsync(
		string sourcePath,
		string destinationPath,
		FileStamp expectedSourceStamp,
		CancellationToken cancellationToken = default
	)
	{
		Record("Move", sourcePath);
		return OnMove(sourcePath, destinationPath, expectedSourceStamp, cancellationToken);
	}

	public Task<WorkspaceFileMoveResult> MoveDirectoryAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
	{
		Record("MoveDirectory", sourcePath);
		return OnMoveDirectory(sourcePath, destinationPath, cancellationToken);
	}

	public Task<WorkspaceFileDeleteResult> DeleteAsync(string path, FileStamp expectedStamp, CancellationToken cancellationToken = default)
	{
		Record("Delete", path);
		return OnDelete(path, expectedStamp, cancellationToken);
	}

	public Task<WorkspaceFileDeleteResult> DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
	{
		Record("DeleteDirectory", path);
		return OnDeleteDirectory(path, cancellationToken);
	}

	private void Record(string member, string path)
	{
		lock (_gate)
			_calls.Add($"{member}:{path}");
	}
}
