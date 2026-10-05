namespace Nickelony.Samples;

/// <summary>The path and the text of a file that the editor text replaced on disk.</summary>
/// <param name="path">The local path of the file.</param>
/// <param name="text">The text now on disk.</param>
internal sealed class EditorTextWrittenEventArgs(string path, string text) : EventArgs
{
	internal string Path { get; } = path;
	internal string Text { get; } = text;
}
