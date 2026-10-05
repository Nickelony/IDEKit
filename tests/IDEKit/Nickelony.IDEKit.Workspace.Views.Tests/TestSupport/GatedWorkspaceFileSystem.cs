using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

/// <summary>
/// Holds a file write and a file delete until a test releases them, so the test can inspect the coordinator while the operation is in flight.
/// </summary>
/// <remarks>
/// The decorator base forwards every other member to the real file system. Each gate reports when an operation entered it, and the test releases
/// it once.
/// </remarks>
internal sealed class GatedWorkspaceFileSystem(LocalWorkspaceFileSystem inner) : WorkspaceFileSystemDecorator(inner)
{
	private readonly TaskCompletionSource _writeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource _writeRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource _deleteEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource _deleteRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public Task WriteEntered => _writeEntered.Task;

	public Task DeleteEntered => _deleteEntered.Task;

	public void ReleaseWrite() => _writeRelease.TrySetResult();

	public void ReleaseDelete() => _deleteRelease.TrySetResult();

	public override async Task<WorkspaceFileReplacementResult> WriteFileAsync(
		string destinationPath,
		ReadOnlyMemory<byte> content,
		FileStamp expectedStamp,
		CancellationToken cancellationToken = default
	)
	{
		_writeEntered.TrySetResult();
		await _writeRelease.Task.ConfigureAwait(false);
		return await Inner.WriteFileAsync(destinationPath, content, expectedStamp, cancellationToken).ConfigureAwait(false);
	}

	public override async Task<WorkspaceFileDeleteResult> DeleteAsync(string path, FileStamp expectedStamp, CancellationToken cancellationToken)
	{
		_deleteEntered.TrySetResult();
		await _deleteRelease.Task.ConfigureAwait(false);
		return await Inner.DeleteAsync(path, expectedStamp, cancellationToken).ConfigureAwait(false);
	}
}
