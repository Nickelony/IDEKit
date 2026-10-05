namespace Nickelony.IDEKit.Workspace.FileSystem;

/// <summary>Supplies the raw file primitives used by <see cref="LocalWorkspaceFileSystem"/>.</summary>
/// <remarks>
/// The default operations are <see cref="File.Replace(string, string, string)"/>,
/// <see cref="File.Move(string, string, bool)"/> without overwrite, <see cref="Directory.Move(string, string)"/>, and <see cref="File.Delete(string)"/>.
/// The delegates let a caller substitute the operations to make the races and half-completed states around them deterministic.
/// </remarks>
internal sealed record WorkspaceFileSystemPrimitives(
	Action<string, string> Replace,
	Action<string, string> MoveFile,
	Action<string, string> MoveDirectory,
	Action<string> DeleteFile
)
{
	/// <summary>The operations that act on the local file system.</summary>
	internal static WorkspaceFileSystemPrimitives Default { get; } =
		new(
			Replace: static (sourcePath, targetPath) => File.Replace(sourcePath, targetPath, null),

			// overwrite: false keeps a concurrently created destination intact on every platform
			// instead of replacing it silently.
			MoveFile: static (sourcePath, targetPath) => File.Move(sourcePath, targetPath, overwrite: false),
			MoveDirectory: static (sourcePath, targetPath) => Directory.Move(sourcePath, targetPath),
			DeleteFile: static path => File.Delete(path)
		);
}
