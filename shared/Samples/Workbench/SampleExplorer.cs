using System.IO;

namespace Nickelony.Samples;

/// <summary>The file tree of the sample folder, which the explorer of a window shows and rebuilds after each change on disk.</summary>
/// <remarks>
/// Hidden entries, whose names start with a dot, stay out of the tree, the way an editor hides its tool folders.
/// </remarks>
internal sealed class SampleExplorer
{
	/// <param name="rootPath">The folder the tree shows.</param>
	internal SampleExplorer(string rootPath)
	{
		RootPath = rootPath;
		Root = Build(rootPath);
	}

	/// <summary>Raised after <see cref="Root"/> was rebuilt.</summary>
	internal event EventHandler? Changed;

	internal string RootPath { get; }

	/// <summary>The node of the sample folder, whose children are its folders first and then its files, each in name order.</summary>
	internal SampleExplorerNode Root { get; private set; }

	/// <summary>Reads the tree from disk again.</summary>
	internal void Refresh()
	{
		Root = Build(RootPath);
		Changed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>Returns the folders between the root and a path, outermost first, which the explorer opens to reveal it.</summary>
	/// <remarks>
	/// A path outside the root, such as a definition in a library of the language server, has none.
	/// </remarks>
	internal IReadOnlyList<string> FoldersAbove(string path)
	{
		string rootPrefix = RootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		List<string> folders = [];

		for (
			string? folder = Path.GetDirectoryName(path);
			folder?.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) == true;
			folder = Path.GetDirectoryName(folder)
		)
		{
			folders.Insert(0, folder);
		}

		return folders;
	}

	private static SampleExplorerNode Build(string path)
	{
		if (!Directory.Exists(path))
			return new SampleExplorerNode(path, IsFolder: true, []);

		IEnumerable<SampleExplorerNode> folders = Directory
			.EnumerateDirectories(path)
			.Where(IsVisible)
			.Order(StringComparer.OrdinalIgnoreCase)
			.Select(Build);

		IEnumerable<SampleExplorerNode> files = Directory
			.EnumerateFiles(path)
			.Where(IsVisible)
			.Order(StringComparer.OrdinalIgnoreCase)
			.Select(file => new SampleExplorerNode(file, IsFolder: false, []));

		return new SampleExplorerNode(path, IsFolder: true, [.. folders, .. files]);
	}

	private static bool IsVisible(string path) => !Path.GetFileName(path).StartsWith('.');
}
