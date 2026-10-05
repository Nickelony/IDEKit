using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Indentation;

/// <summary>Guesses from a text whether it indents with tabs or spaces, and by how many columns.</summary>
/// <remarks>
/// <para>
/// The guess follows the one of VS Code: each line's leading whitespace is compared with the previous non-blank line's, the line pairs vote
/// for the size of one level, and the lines indented with tabs are weighed against the lines indented with spaces. Blank lines and lines that hold
/// only spaces and tabs are ignored, and only the first <see cref="MaximumScannedLineCount"/> lines are read.
/// </para>
/// <para>
/// The style and the size are detected independently. A half the text does not show
/// takes the default, and its flag in the result stays <see langword="false"/>. The style is tabs when more lines indent with tabs than with spaces,
/// and spaces when more do and there are at least two, because one space line can be an alignment; a tie shows none. The size needs two votes,
/// and it is guessed when the style is spaces, detected or default; a tab text keeps the default size, which then is the width of a tab.
/// The candidates are 2, 4, 6 and 8 first, then 3, 5 and 7, and a size of 2 wins over a size of 4 when it has at least two thirds of its votes.
/// A line pair that aligns after a comma does not vote unless the default is spaces of that size, because the alignment says nothing about the size
/// of a level.
/// </para>
/// </remarks>
public static class IndentationDetector
{
	/// <summary>The number of lines at the start of a text that the detector reads.</summary>
	public const int MaximumScannedLineCount = 10_000;

	private const int MinimumEvidenceCount = 2;
	private const int MaximumSize = 8;

	// Even sizes come first, so a tie between an even and an odd size goes to the even one.
	private static readonly int[] s_candidateSizes = [2, 4, 6, 8, 3, 5, 7];

	/// <summary>Detects the indentation a text uses, and takes the defaults for what the text does not show.</summary>
	/// <param name="text">The text to inspect.</param>
	/// <param name="defaultInsertSpaces">The style to report when the text does not show one.</param>
	/// <param name="defaultSize">The size to report when the text does not show one.</param>
	/// <returns>The indentation of the text, with a flag for each half that the text showed.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="defaultSize"/> is less than or equal to zero or greater than <see cref="IndentationOperations.MaximumSize"/>.
	/// </exception>
	/// <remarks>
	/// The scan reads the text in place, so it allocates nothing per line and a host can run it on every open.
	/// </remarks>
	public static IndentationDetection Detect(ReadOnlySpan<char> text, bool defaultInsertSpaces, int defaultSize)
	{
		LeadingIndentation.ValidateSize(defaultSize);

		int tabIndentedLineCount = 0;
		int spaceIndentedLineCount = 0;

		Span<int> sizeVotes = stackalloc int[MaximumSize + 1];

		ReadOnlySpan<char> previousLine = default;

		int previousIndentLength = 0;
		int scannedLineCount = 0;

		var lines = new TextLineEnumerator(text);

		while (scannedLineCount < MaximumScannedLineCount && lines.MoveNext())
		{
			scannedLineCount++;

			ReadOnlySpan<char> line = lines.Content;
			int indentLength = CountIndentation(line, out int tabCount, out int spaceCount);

			if (indentLength == line.Length)
				continue;

			if (tabCount > 0)
				tabIndentedLineCount++;
			else if (spaceCount > 1)
				spaceIndentedLineCount++;

			int sizeDifference = GetSizeDifference(previousLine, previousIndentLength, line, indentLength, out bool looksLikeAlignment);

			if (looksLikeAlignment && !(defaultInsertSpaces && defaultSize == sizeDifference))
				continue;

			if (sizeDifference <= MaximumSize)
				sizeVotes[sizeDifference]++;

			previousLine = line;
			previousIndentLength = indentLength;
		}

		bool insertSpaces = defaultInsertSpaces;
		bool insertSpacesDetected = false;

		// A lone tab line stands because nothing contradicts it, while a lone space line can be an alignment, so spaces need two lines.
		bool tabsWin = tabIndentedLineCount > spaceIndentedLineCount;
		bool spacesWin = spaceIndentedLineCount > tabIndentedLineCount && spaceIndentedLineCount >= MinimumEvidenceCount;

		if (tabsWin || spacesWin)
		{
			insertSpaces = spacesWin;
			insertSpacesDetected = true;
		}

		int size = defaultSize;
		bool sizeDetected = false;

		if (insertSpaces)
		{
			int bestVoteCount = MinimumEvidenceCount - 1;

			foreach (int candidateSize in s_candidateSizes)
			{
				if (sizeVotes[candidateSize] > bestVoteCount)
				{
					bestVoteCount = sizeVotes[candidateSize];
					size = candidateSize;
					sizeDetected = true;
				}
			}

			// Two wins over four with at least two thirds of its votes, as VS Code compares them, which helps a 2-space file with a few 4-space jumps.
			if (sizeDetected && size == 4 && sizeVotes[2] * 3 >= sizeVotes[4] * 2)
				size = 2;
		}

		return new IndentationDetection(insertSpaces, size, insertSpacesDetected, sizeDetected);
	}

	/// <summary>Counts the leading spaces and tabs of a line and returns the length of that run.</summary>
	private static int CountIndentation(ReadOnlySpan<char> line, out int tabCount, out int spaceCount)
	{
		tabCount = 0;
		spaceCount = 0;

		int index = 0;

		for (; index < line.Length; index++)
		{
			if (line[index] == '\t')
				tabCount++;
			else if (line[index] == ' ')
				spaceCount++;
			else
				break;
		}

		return index;
	}

	/// <summary>
	/// Returns the number of columns by which the indentation of <paramref name="line"/> differs from that of <paramref name="previousLine"/>,
	/// or zero when the difference is not a whole number of levels or one side mixes tabs and spaces.
	/// </summary>
	/// <remarks>
	/// The common prefix of both indentations is skipped, so a tab against a tab and four spaces counts
	/// the four spaces. <paramref name="looksLikeAlignment"/> is set for a line that aligns with a token of a previous line that ends in a comma,
	/// because such a difference says nothing about the size of a level.
	/// </remarks>
	internal static int GetSizeDifference(
		ReadOnlySpan<char> previousLine,
		int previousIndentLength,
		ReadOnlySpan<char> line,
		int indentLength,
		out bool looksLikeAlignment
	)
	{
		looksLikeAlignment = false;

		int commonLength = 0;

		while (commonLength < previousIndentLength && commonLength < indentLength && previousLine[commonLength] == line[commonLength])
			commonLength++;

		CountSpacesAndTabs(previousLine[commonLength..previousIndentLength], out int previousSpaceCount, out int previousTabCount);
		CountSpacesAndTabs(line[commonLength..indentLength], out int spaceCount, out int tabCount);

		if ((previousSpaceCount > 0 && previousTabCount > 0) || (spaceCount > 0 && tabCount > 0))
			return 0;

		int tabDifference = Math.Abs(previousTabCount - tabCount);
		int spaceDifference = Math.Abs(previousSpaceCount - spaceCount);

		if (tabDifference == 0)
		{
			// Folks align continuation lines with a token of the line above, which must not count as a level.
			if (spaceDifference > 0 && spaceCount >= 1 && spaceCount - 1 < previousLine.Length && spaceCount < line.Length)
				looksLikeAlignment = line[spaceCount] != ' ' && previousLine[spaceCount - 1] == ' ' && previousLine[^1] == ',';

			return spaceDifference;
		}

		return spaceDifference % tabDifference == 0 ? spaceDifference / tabDifference : 0;
	}

	private static void CountSpacesAndTabs(ReadOnlySpan<char> indentation, out int spaceCount, out int tabCount)
	{
		spaceCount = 0;
		tabCount = 0;

		foreach (char character in indentation)
		{
			if (character == ' ')
				spaceCount++;
			else
				tabCount++;
		}
	}
}
