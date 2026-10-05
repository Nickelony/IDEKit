namespace Nickelony.Samples;

/// <summary>A file or a folder of the explorer tree.</summary>
/// <param name="Path">The full path of the entry.</param>
/// <param name="IsFolder">Whether the entry is a folder.</param>
/// <param name="Children">The entries of a folder, or an empty list for a file.</param>
internal sealed record SampleExplorerNode(string Path, bool IsFolder, IReadOnlyList<SampleExplorerNode> Children)
{
	/// <summary>The name the tree shows.</summary>
	internal string Name => System.IO.Path.GetFileName(Path);
}
