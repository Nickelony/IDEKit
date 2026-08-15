namespace Nickelony.IDEKit.Workspace.Documents.FileSystem;

/// <summary>
/// Supplies the raw file operations used by <see cref="LocalWorkspaceFileSystem"/>: <c>Replace</c>
/// publishes a staged file over an existing destination, <c>MoveFile</c> moves a file, and
/// <c>MoveDirectory</c> moves a directory.
/// </summary>
/// <remarks>
/// The default operations use <see cref="File.Replace(string, string, string)"/>,
/// <see cref="File.Move(string, string, bool)"/> without overwrite, and
/// <see cref="Directory.Move(string, string)"/>. The type is internal: the test suite substitutes the
/// operations so the races and half-completed states around them become deterministic - a destination
/// that appears between the stamp check and the publish step, and a case-only rename whose rollback
/// fails and leaves the file at its intermediate path.
/// </remarks>
internal sealed record WorkspaceFileOperations(
	Action<string, string> Replace,
	Action<string, string> MoveFile,
	Action<string, string> MoveDirectory)
{
	/// <summary>
	/// Gets the operations that act on the local file system.
	/// </summary>
	public static WorkspaceFileOperations Default { get; } = new(
		static (sourcePath, targetPath) => File.Replace(sourcePath, targetPath, null),
		// overwrite: false keeps a concurrently created destination intact on every platform
		// instead of replacing it silently.
		static (sourcePath, targetPath) => File.Move(sourcePath, targetPath, overwrite: false),
		static (sourcePath, targetPath) => Directory.Move(sourcePath, targetPath));
}
