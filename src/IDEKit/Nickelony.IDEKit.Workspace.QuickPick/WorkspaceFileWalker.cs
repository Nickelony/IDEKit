using System.IO.Enumeration;

namespace Nickelony.IDEKit.Workspace.QuickPick;

/// <summary>Enumerates the files below a folder of a workspace root, skipping the folders and files that the filter excludes.</summary>
/// <remarks>
/// The walk includes hidden and system files, and skips reparse points (symbolic links and junctions), so it cannot leave the root or loop.
/// A folder that cannot be read, or that vanishes while the walk runs, contributes no files and does not stop the walk. Each folder is read whole
/// before its files are returned, and its subfolders are walked afterwards, so the files of one folder come out together.
/// </remarks>
internal static class WorkspaceFileWalker
{
	private static readonly EnumerationOptions s_options = new()
	{
		IgnoreInaccessible = true,
		ReturnSpecialDirectories = false,
		AttributesToSkip = FileAttributes.ReparsePoint,
	};

	/// <summary>Lists the files below a folder.</summary>
	/// <param name="directory">The folder to list.</param>
	/// <param name="root">The workspace root that the filter measures paths from.</param>
	/// <param name="filter">Decides which files and folders stay out of the list.</param>
	/// <param name="cancellationToken">A token that stops the enumeration.</param>
	/// <returns>The full paths of the files, folder by folder.</returns>
	internal static IEnumerable<string> List(string directory, string root, WorkspaceFileFilter filter, CancellationToken cancellationToken)
	{
		Stack<string> folders = new();

		folders.Push(directory);

		while (folders.Count > 0)
		{
			cancellationToken.ThrowIfCancellationRequested();

			foreach ((string path, string name, bool isDirectory) in ReadEntries(folders.Pop()))
			{
				if (filter.Excludes(name, Path.GetRelativePath(root, path).Replace('\\', '/')))
					continue;

				if (isDirectory)
					folders.Push(path);
				else
					yield return path;
			}
		}
	}

	private static List<(string Path, string Name, bool IsDirectory)> ReadEntries(string folder)
	{
		try
		{
			return
			[
				.. new FileSystemEnumerable<(string Path, string Name, bool IsDirectory)>(
					folder,
					static (ref FileSystemEntry entry) => (entry.ToFullPath(), entry.FileName.ToString(), entry.IsDirectory),
					s_options
				),
			];
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// A folder that vanished or that the process may not read has no files to list; the next walk or the watcher sees it again.
			return [];
		}
	}
}
