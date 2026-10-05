namespace Nickelony.IDEKit.Core.Bookmarks;

/// <summary>Abstracts the file operations the <see cref="SidecarLineFile"/> kernel performs.</summary>
/// <remarks>
/// The file system is injectable so the failure mapping is testable; production uses <see cref="SidecarFileSystem.Default"/>.
/// </remarks>
internal interface ISidecarFileSystem
{
	bool FileExists(string path);

	bool DirectoryExists(string path);

	/// <summary>Writes <paramref name="contents"/> to <paramref name="path"/>, replacing any previous file.</summary>
	void WriteAllText(string path, string contents);

	/// <summary>Replaces <paramref name="destinationPath"/> with <paramref name="sourcePath"/>.</summary>
	void Move(string sourcePath, string destinationPath);

	/// <summary>Deletes <paramref name="path"/>, treating a missing file as a no-op.</summary>
	void Delete(string path);

	string[] ReadAllLines(string path);
}
