namespace Nickelony.IDEKit.Core.Persistence;

/// <summary>
/// Abstracts the file operations the <see cref="SidecarLineFile"/> kernel performs.
/// </summary>
/// <remarks>
/// The kernel maps a path, file-system, or authorization error to a failed operation rather than an
/// exception. Exercising that mapping through the real file system needs a failure the platform can
/// actually produce - on Windows a destination another handle holds open without sharing, which an
/// otherwise healthy machine cannot be made to raise on every operating system - so the operations
/// are injectable. Production uses <see cref="SidecarFileOperations.Default"/>; tests supply a double
/// that fails on demand, which pins the same branches everywhere. The abstraction is internal because
/// no consumer is expected to supply one.
/// </remarks>
internal interface ISidecarFileOperations
{
	/// <summary>Reports whether a file exists at <paramref name="path"/>.</summary>
	bool FileExists(string path);

	/// <summary>Reports whether a directory exists at <paramref name="path"/>.</summary>
	bool DirectoryExists(string path);

	/// <summary>Writes <paramref name="contents"/> to <paramref name="path"/>, replacing any previous file.</summary>
	void WriteAllText(string path, string contents);

	/// <summary>Replaces <paramref name="destinationPath"/> with <paramref name="sourcePath"/>.</summary>
	void Move(string sourcePath, string destinationPath);

	/// <summary>Deletes <paramref name="path"/>, treating a missing file as a no-op.</summary>
	void Delete(string path);

	/// <summary>Reads every line of <paramref name="path"/>.</summary>
	string[] ReadAllLines(string path);
}

/// <summary>
/// The production <see cref="ISidecarFileOperations"/> implementation, backed by
/// <see cref="File"/> and <see cref="Directory"/>.
/// </summary>
internal sealed class SidecarFileOperations : ISidecarFileOperations
{
	/// <summary>Gets the shared implementation the sidecar kernel uses outside tests.</summary>
	internal static SidecarFileOperations Default { get; } = new();

	public bool FileExists(string path) => File.Exists(path);

	public bool DirectoryExists(string path) => Directory.Exists(path);

	public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);

	public void Move(string sourcePath, string destinationPath) => File.Move(sourcePath, destinationPath, overwrite: true);

	public void Delete(string path) => File.Delete(path);

	public string[] ReadAllLines(string path) => File.ReadAllLines(path);
}
