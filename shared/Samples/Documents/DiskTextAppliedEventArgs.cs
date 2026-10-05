namespace Nickelony.Samples;

/// <summary>The path and the text of a file whose change on disk replaced the editor text.</summary>
/// <param name="path">The local path of the file.</param>
/// <param name="text">The text now on disk.</param>
internal sealed class DiskTextAppliedEventArgs(string path, string text) : EventArgs
{
	internal string Path { get; } = path;
	internal string Text { get; } = text;
}
