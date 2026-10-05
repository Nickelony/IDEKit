using System.Text;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Indentation;

/// <summary>Converts between spaces and tabs in text while preserving the original line terminators.</summary>
/// <remarks>
/// Tab stops are computed from UTF-16 code units, so a surrogate pair counts as two columns and text containing wide or zero-width
/// characters is approximated. When nothing converts, the original instance is returned. A size above <see cref="IndentationOperations.MaximumSize"/>
/// is rejected.
/// </remarks>
public static class WhitespaceOperations
{
	/// <summary>Converts leading space indentation to tabs using the specified tab size.</summary>
	/// <param name="text">The text to convert.</param>
	/// <param name="tabSize">The number of spaces per tab stop.</param>
	/// <returns>The converted text.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="tabSize"/> is less than or equal to zero or greater than <see cref="IndentationOperations.MaximumSize"/>.
	/// </exception>
	/// <remarks>
	/// Only leading spaces and tabs are converted; other whitespace (a form feed, a non-breaking space) ends the indentation scan as content.
	/// Existing tabs and partial space groups that do not reach a tab stop are preserved.
	/// </remarks>
	public static string ConvertIndentationToTabs(string text, int tabSize)
	{
		ArgumentNullException.ThrowIfNull(text);
		LeadingIndentation.ValidateSize(tabSize);

		return TransformLines(text, kind: LineConversionKind.IndentationToTabs, size: tabSize, newSize: 0);
	}

	/// <summary>Converts leading tab indentation to spaces using the specified tab size.</summary>
	/// <param name="text">The text to convert.</param>
	/// <param name="tabSize">The number of spaces per tab stop.</param>
	/// <returns>The converted text.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="tabSize"/> is less than or equal to zero or greater than <see cref="IndentationOperations.MaximumSize"/>.
	/// </exception>
	/// <remarks>
	/// Each tab of a line's leading spaces and tabs advances to the next tab stop, so the indentation keeps its width. Whatever follows the first
	/// other character of a line stays as it is, which keeps a tab inside a string literal or after code. Use <see cref="ExpandTabs"/> to expand
	/// every tab.
	/// </remarks>
	public static string ConvertIndentationToSpaces(string text, int tabSize)
	{
		ArgumentNullException.ThrowIfNull(text);
		LeadingIndentation.ValidateSize(tabSize);

		return TransformLines(text, kind: LineConversionKind.IndentationToSpaces, size: tabSize, newSize: 0);
	}

	/// <summary>Changes the number of spaces of one indentation level in space-indented text.</summary>
	/// <param name="text">The text to convert.</param>
	/// <param name="currentSize">The number of spaces of one level in <paramref name="text"/>.</param>
	/// <param name="newSize">The number of spaces of one level in the result.</param>
	/// <returns>The converted text.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="currentSize"/> or <paramref name="newSize"/> is less than or equal to zero or greater
	/// than <see cref="IndentationOperations.MaximumSize"/>.
	/// </exception>
	/// <remarks>
	/// Only leading spaces change: a run of whole levels takes the new size, and a remainder that is not a whole level stays as it is, so an aligned
	/// continuation line keeps its offset from its level. A line whose leading whitespace holds a tab is left as it is, because the width of a tab
	/// is the tab size of the editor and not a count of spaces; convert such text with <see cref="ConvertIndentationToSpaces"/> first.
	/// </remarks>
	public static string ResizeIndentation(string text, int currentSize, int newSize)
	{
		ArgumentNullException.ThrowIfNull(text);

		LeadingIndentation.ValidateSize(currentSize);
		LeadingIndentation.ValidateSize(newSize);

		return TransformLines(text, kind: LineConversionKind.IndentationSize, size: currentSize, newSize);
	}

	/// <summary>Expands every tab in the text to as many spaces as needed to reach the next tab stop.</summary>
	/// <param name="text">The text to convert.</param>
	/// <param name="tabSize">The number of spaces per tab stop.</param>
	/// <returns>The converted text.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="tabSize"/> is less than or equal to zero or greater than <see cref="IndentationOperations.MaximumSize"/>.
	/// </exception>
	/// <remarks>
	/// A tab after the first other character of a line expands too, including one inside a string literal, so a host that converts indentation calls
	/// <see cref="ConvertIndentationToSpaces"/> instead.
	/// </remarks>
	public static string ExpandTabs(string text, int tabSize)
	{
		ArgumentNullException.ThrowIfNull(text);
		LeadingIndentation.ValidateSize(tabSize);

		return TransformLines(text, kind: LineConversionKind.AllTabsToSpaces, size: tabSize, newSize: 0);
	}

	private static string TransformLines(string text, LineConversionKind kind, int size, int newSize)
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
			ReadOnlySpan<char> line = enumerator.Content;

			if (kind == LineConversionKind.AllTabsToSpaces)
				AppendExpandedTabs(ref builder, text, lineStart, line, tabSize: size);
			else
				AppendConvertedIndentation(ref builder, text, lineStart, line, kind, size, newSize);

			builder?.Append(enumerator.Terminator);
			lineStart += enumerator.Content.Length + enumerator.Terminator.Length;
		}

		// Return the original instance when nothing converted, so callers can detect a no-op by
		// reference.
		return builder is null ? text : builder.ToString();
	}

	/// <summary>
	/// Appends one line with its leading whitespace converted, creating the output builder when the first line changes so the unchanged input prefix
	/// stays implicit.
	/// </summary>
	private static void AppendConvertedIndentation(
		ref StringBuilder? builder,
		string text,
		int lineStart,
		ReadOnlySpan<char> line,
		LineConversionKind kind,
		int size,
		int newSize
	)
	{
		int indentLength = LeadingIndentation.Measure(line);
		ReadOnlySpan<char> indentation = line[..indentLength];

		string? converted = kind switch
		{
			LineConversionKind.IndentationToTabs => LeadingIndentation.ToTabs(indentation, size),
			LineConversionKind.IndentationToSpaces => LeadingIndentation.ToSpaces(indentation, size),
			_ => LeadingIndentation.Resize(indentation, size, newSize),
		};

		if (converted is null)
			builder?.Append(line);
		else
			EnsureBuilder(ref builder, text, lineStart).Append(converted).Append(line[indentLength..]);
	}

	/// <summary>
	/// Appends one line with every tab expanded to the next tab stop, creating the output builder when the first tab is expanded so the unchanged
	/// input prefix stays implicit.
	/// </summary>
	private static void AppendExpandedTabs(ref StringBuilder? builder, string text, int lineStart, ReadOnlySpan<char> line, int tabSize)
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
	/// Ensures the output builder exists and already contains the unchanged input prefix, so the caller can append converted output
	/// from <paramref name="prefixLength"/> on.
	/// </summary>
	private static StringBuilder EnsureBuilder(ref StringBuilder? builder, string text, int prefixLength)
	{
		if (builder is not null)
			return builder;

		builder = new StringBuilder(text.Length);
		builder.Append(text.AsSpan(0, prefixLength));

		return builder;
	}

	private enum LineConversionKind
	{
		IndentationToTabs,
		IndentationToSpaces,
		IndentationSize,
		AllTabsToSpaces,
	}
}
