namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Splits text into lines using the same LF, CRLF, and lone CR rules as the other text primitives.
/// </summary>
/// <remarks>
/// Line terminators are not part of the returned line contents. The final line is always returned, so
/// empty text yields a single empty entry and text that ends with a line terminator yields a final
/// empty line. Use <see cref="Indentation.IndentationOperations.SplitLines"/> when each line's
/// delimiter and source offset are also needed.
/// </remarks>
public static class TextLineSplitter
{
	/// <summary>
	/// Splits the supplied text into its line contents.
	/// </summary>
	/// <param name="text">The text to split. A <see langword="null"/> value is treated as empty.</param>
	/// <returns>The line contents without their line terminators.</returns>
	public static string[] Split(string? text)
	{
		string source = text ?? string.Empty;
		(int[] startOffsets, int[] lengths) = TextLineTable.Build(source);
		var lines = new string[startOffsets.Length];

		for (int index = 0; index < lines.Length; index++)
			lines[index] = source.Substring(startOffsets[index], lengths[index]);

		return lines;
	}
}
