namespace Nickelony.IDEKit.Core.Bookmarks;

/// <summary>The production <see cref="ISidecarFileSystem"/> implementation, backed by <see cref="File"/> and <see cref="Directory"/>.</summary>
internal sealed class SidecarFileSystem : ISidecarFileSystem
{
	internal static SidecarFileSystem Default { get; } = new();

	public bool FileExists(string path) => File.Exists(path);

	public bool DirectoryExists(string path) => Directory.Exists(path);

	public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);

	public void Move(string sourcePath, string destinationPath) => File.Move(sourcePath, destinationPath, overwrite: true);

	public void Delete(string path) => File.Delete(path);

	public string[] ReadAllLines(string path) => File.ReadAllLines(path);
}
