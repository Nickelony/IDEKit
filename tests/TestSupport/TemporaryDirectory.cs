using System.IO;

namespace Nickelony.Testing;

/// <summary>
/// Creates one unique directory under the system temporary path and deletes it recursively on
/// <see cref="Dispose"/>, so every file-based test shares one fixture policy instead of hand-rolling it.
/// </summary>
/// <param name="prefix">The directory name prefix, which keeps related fixtures grouped.</param>
/// <remarks>
/// This is the single temporary-directory fixture for both package families; every test project links
/// this file instead of declaring its own copy.
/// </remarks>
internal sealed class TemporaryDirectory(string prefix = "test-") : IDisposable
{
	/// <summary>
	/// Gets the absolute path of the created directory.
	/// </summary>
	public string Path { get; } = CreateDirectory(prefix);

	/// <summary>
	/// Gets the absolute path of <paramref name="fileName"/> inside the directory.
	/// </summary>
	/// <param name="fileName">The file name, without a directory part.</param>
	/// <returns>The absolute file path.</returns>
	public string GetFilePath(string fileName) => System.IO.Path.Combine(Path, fileName);

	/// <summary>
	/// Deletes the directory and everything inside it.
	/// </summary>
	public void Dispose() => DeleteBestEffort(Path);

	/// <summary>
	/// Deletes <paramref name="directoryPath"/> recursively when it exists, swallowing the file-system
	/// exceptions a best-effort teardown should not surface.
	/// </summary>
	/// <param name="directoryPath">The directory to delete.</param>
	/// <remarks>
	/// A lingering file handle, a locked directory, or an antivirus scan can briefly block the recursive
	/// delete; cleanup must not fail a test that already ran. The operating system reaps the leftover.
	/// </remarks>
	public static void DeleteBestEffort(string directoryPath)
	{
		try
		{
			if (Directory.Exists(directoryPath))
				Directory.Delete(directoryPath, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort cleanup only; the operating system reaps the temporary directory.
		}
		catch (UnauthorizedAccessException)
		{
			// Best-effort cleanup only; the operating system reaps the temporary directory.
		}
	}

	private static string CreateDirectory(string prefix)
	{
		string path = System.IO.Path.Combine(
			System.IO.Path.GetTempPath(),
			prefix + Guid.NewGuid().ToString("N"));

		Directory.CreateDirectory(path);
		return path;
	}
}
