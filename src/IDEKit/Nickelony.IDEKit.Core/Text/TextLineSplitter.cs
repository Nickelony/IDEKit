namespace Nickelony.IDEKit.Core.Text;

/// <summary>Splits text into lines using the same LF, CRLF, and lone CR rules as the other text primitives.</summary>
/// <remarks>
/// Line terminators are not part of the returned contents, and the final line is always returned: empty text yields a single empty entry,
/// and text ending with a line terminator yields a final empty line.
/// </remarks>
public static class TextLineSplitter
{
	/// <summary>Splits the supplied text into its line contents.</summary>
	/// <param name="text">The text to split. A <see langword="null"/> value is treated as empty.</param>
	/// <returns>The line contents without their line terminators.</returns>
	public static string[] Split(string? text)
	{
		string source = text ?? string.Empty;
		(int[] startOffsets, int[] lengths) = TextLineIndexBuilder.Build(source);
		string[] lines = new string[startOffsets.Length];

		for (int index = 0; index < lines.Length; index++)
			lines[index] = source.Substring(startOffsets[index], lengths[index]);

		return lines;
	}

	/// <summary>Splits the supplied text into its lines, each with the line terminator that ends it, so the lines concatenate to the text.</summary>
	/// <param name="text">The text to split. A <see langword="null"/> value is treated as empty.</param>
	/// <returns>The lines with their line terminators; the final line has none.</returns>
	public static string[] SplitIncludingTerminators(string? text)
	{
		string source = text ?? string.Empty;
		int[] startOffsets = TextLineIndexBuilder.Build(source).StartOffsets;
		string[] lines = new string[startOffsets.Length];

		for (int index = 0; index < lines.Length; index++)
		{
			int end = index + 1 < startOffsets.Length ? startOffsets[index + 1] : source.Length;
			lines[index] = source[startOffsets[index]..end];
		}

		return lines;
	}
}
