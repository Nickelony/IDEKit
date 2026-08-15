using Nickelony.IDEKit.Core.Text;
using System.Text;

namespace Nickelony.IDEKit.Core.Formatting;

/// <summary>
/// Converts between spaces and tabs in text while preserving the original line terminators.
/// </summary>
/// <remarks>
/// Space-to-tab conversion changes leading indentation; tab-to-space conversion expands tabs
/// wherever they occur. Tab stops are computed from UTF-16 code units, so a surrogate pair counts as
/// two columns and text containing wide or zero-width characters is approximated. When nothing
/// converts (no tab to expand, or no leading space that reaches a tab stop), the original instance
/// is returned, matching <see cref="TrimTrailingWhitespaceFormatter"/>.
/// </remarks>
public static class WhitespaceConverter
{
	/// <summary>
	/// Converts leading space indentation to tabs using the specified tab size.
	/// </summary>
	/// <remarks>
	/// Only leading spaces and tabs are converted; other whitespace (for example a form feed or a
	/// non-breaking space) is treated as content and ends the indentation scan. Existing tabs,
	/// partial groups of spaces that do not reach a tab stop, and all non-indentation content are
	/// preserved.
	/// </remarks>
	/// <param name="text">The text to convert.</param>
	/// <param name="tabSize">The number of spaces per tab stop.</param>
	/// <returns>The converted text.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="tabSize"/> is less than or equal to zero.</exception>
	public static string ConvertIndentationToTabs(string text, int tabSize)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tabSize);

		return TransformLines(text, tabSize, expandTabs: false);
	}

	/// <summary>
	/// Expands every tab in the text to as many spaces as needed to reach the next tab stop.
	/// </summary>
	/// <remarks>
	/// Unlike <see cref="ConvertIndentationToTabs"/>, which changes leading indentation only, this method
	/// expands tabs wherever they occur, including inside line content.
	/// </remarks>
	/// <param name="text">The text to convert.</param>
	/// <param name="tabSize">The number of spaces per tab stop.</param>
	/// <returns>The converted text.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="tabSize"/> is less than or equal to zero.</exception>
	public static string ExpandTabs(string text, int tabSize)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tabSize);

		return TransformLines(text, tabSize, expandTabs: true);
	}

	private static string TransformLines(string text, int tabSize, bool expandTabs)
	{
		// The builder is created lazily on the first conversion, so a no-op input never allocates or
		// copies; until it exists, the output equals the input prefix that was already scanned. A
		// non-null builder is exactly the "something converted" signal the reference-identity
		// contract needs, so no separate change flag is required.
		StringBuilder? builder = null;
		var enumerator = new TextLineEnumerator(text);
		int lineStart = 0;

		while (enumerator.MoveNext())
		{
			if (expandTabs)
				AppendExpandedTabs(ref builder, text, lineStart, enumerator.Content, tabSize);
			else
				AppendLineIndentationAsTabs(ref builder, text, lineStart, enumerator.Content, tabSize);

			builder?.Append(enumerator.Terminator);
			lineStart += enumerator.Content.Length + enumerator.Terminator.Length;
		}

		// Return the original instance when nothing converted, so callers can detect a no-op by
		// reference like they can with TrimTrailingWhitespaceFormatter.
		return builder is null ? text : builder.ToString();
	}

	/// <summary>
	/// Appends one line with leading space runs converted to tabs, creating the output builder when
	/// the first conversion happens so the unchanged input prefix stays implicit.
	/// </summary>
	private static void AppendLineIndentationAsTabs(
		ref StringBuilder? builder,
		string text,
		int lineStart,
		ReadOnlySpan<char> line,
		int tabSize)
	{
		int indentLength = 0;

		while (indentLength < line.Length && WhitespaceScan.IsSpaceOrTab(line[indentLength]))
			indentLength++;

		ReadOnlySpan<char> indentation = line[..indentLength];
		int column = 0;

		for (int i = 0; i < indentation.Length;)
		{
			if (indentation[i] == '\t')
			{
				builder?.Append('\t');

				// An existing tab advances to the next tab stop rather than by tabSize columns.
				column = ((column / tabSize) + 1) * tabSize;
				i++;

				continue;
			}

			int runStart = i;

			while (i < indentation.Length && indentation[i] == ' ')
				i++;

			int spaceCount = i - runStart;

			// Replace groups of spaces that reach a tab stop with a single tab.
			// Remaining spaces that would not reach a tab stop stay as spaces.
			while (spaceCount > 0)
			{
				int spacesToNextStop = tabSize - (column % tabSize);

				if (spaceCount < spacesToNextStop)
					break;

				EnsureBuilder(ref builder, text, lineStart + runStart).Append('\t');
				column += spacesToNextStop;
				spaceCount -= spacesToNextStop;
			}

			if (spaceCount > 0)
			{
				builder?.Append(' ', spaceCount);
				column += spaceCount;
			}
		}

		builder?.Append(line[indentLength..]);
	}

	/// <summary>
	/// Appends one line with every tab expanded to the next tab stop, creating the output builder
	/// when the first tab is expanded so the unchanged input prefix stays implicit.
	/// </summary>
	private static void AppendExpandedTabs(
		ref StringBuilder? builder,
		string text,
		int lineStart,
		ReadOnlySpan<char> line,
		int tabSize)
	{
		int column = 0;

		for (int index = 0; index < line.Length; index++)
		{
			char character = line[index];

			if (character == '\t')
			{
				// Expand each tab only as far as the next tab stop for this line.
				int spaces = tabSize - (column % tabSize);

				EnsureBuilder(ref builder, text, lineStart + index).Append(' ', spaces);
				column += spaces;
			}
			else
			{
				builder?.Append(character);
				column++;
			}
		}
	}

	/// <summary>
	/// Ensures the output builder exists and already contains the unchanged input prefix, so the
	/// caller can append converted output from <paramref name="prefixLength"/> on.
	/// </summary>
	private static StringBuilder EnsureBuilder(ref StringBuilder? builder, string text, int prefixLength)
	{
		if (builder is not null)
			return builder;

		builder = new StringBuilder(text.Length);
		builder.Append(text.AsSpan(0, prefixLength));

		return builder;
	}
}
