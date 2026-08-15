namespace Nickelony.IDEKit.Core.Text;

/// <summary>
/// Builds the per-line start offsets and lengths for a text string.
/// </summary>
/// <remarks>
/// Shared by the snapshot and line-map types so both recognize the same LF, CRLF, and lone CR line
/// terminators and expose consistent line shapes. An empty text yields a single empty line.
/// </remarks>
internal static class TextLineTable
{
	/// <summary>
	/// Builds the ascending line start offsets and the line lengths for the supplied text.
	/// </summary>
	/// <remarks>
	/// The text is scanned once to count lines and once to fill exact-size arrays, so no list growth
	/// or array copying is needed.
	/// </remarks>
	/// <param name="text">The text to split into lines.</param>
	/// <returns>The start offset and the length of each line, both excluding line terminators.</returns>
	internal static (int[] StartOffsets, int[] Lengths) Build(string text)
	{
		int lineCount = CountLines(text);
		var startOffsets = new int[lineCount];
		var lengths = new int[lineCount];
		var enumerator = new TextLineEnumerator(text);
		int index = 0;

		while (enumerator.MoveNext())
		{
			startOffsets[index] = enumerator.StartOffset;
			lengths[index] = enumerator.Content.Length;
			index++;
		}

		return (startOffsets, lengths);
	}

	/// <summary>
	/// Counts the lines in the supplied text.
	/// </summary>
	/// <param name="text">The text to count the lines of.</param>
	/// <returns>The number of logical lines.</returns>
	internal static int CountLines(string text)
	{
		int lineCount = 0;
		var enumerator = new TextLineEnumerator(text);

		while (enumerator.MoveNext())
			lineCount++;

		return lineCount;
	}
}
