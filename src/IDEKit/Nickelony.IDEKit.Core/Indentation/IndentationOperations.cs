using Nickelony.IDEKit.Core.Text;
using System.Text;

namespace Nickelony.IDEKit.Core.Indentation;

/// <summary>
/// Provides language-independent helpers for computing and manipulating indentation text.
/// </summary>
/// <remarks>
/// The line helpers (<see cref="SplitLines"/>, <see cref="ContainsLineBreak"/>, and
/// <see cref="IndentationTextLine"/>) share this slice because indentation consumers walk the lines
/// of inserted text to re-indent them, so they share the line shape that walk needs (content,
/// terminator, and start offset).
/// </remarks>
public static class IndentationOperations
{
	/// <summary>
	/// The number of spaces used when neither an indentation size nor a tab size is configured.
	/// </summary>
	private const int DefaultIndentationSize = 4;

	/// <summary>
	/// Creates the indentation unit that should be appended for one extra indent level.
	/// </summary>
	/// <remarks>
	/// When converting to spaces and <paramref name="indentationSize"/> is not positive, the unit
	/// falls back to <see cref="DefaultIndentationSize"/> spaces. A host that would otherwise resolve
	/// the size from a tab size passes that resolved value as <paramref name="indentationSize"/>.
	/// </remarks>
	/// <param name="convertTabsToSpaces">Whether indentation should use spaces instead of tabs.</param>
	/// <param name="indentationSize">The number of columns per indent level.</param>
	/// <returns>The indentation unit text.</returns>
	public static string CreateIndentationUnit(bool convertTabsToSpaces, int indentationSize)
	{
		if (!convertTabsToSpaces)
			return "\t";

		int size = indentationSize > 0
			? indentationSize
			: DefaultIndentationSize;

		return new string(' ', size);
	}

	/// <summary>
	/// Gets the leading whitespace prefix (spaces and tabs) for the supplied line.
	/// </summary>
	/// <param name="lineText">The line text to inspect.</param>
	/// <returns>The leading indentation of the line.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="lineText"/> is <see langword="null"/>.</exception>
	public static string GetLeadingWhitespace(string lineText)
	{
		ArgumentNullException.ThrowIfNull(lineText);

		return lineText[..GetLeadingWhitespaceLength(lineText)];
	}

	/// <summary>
	/// Gets the number of leading indentation characters (spaces and tabs) in the supplied line.
	/// </summary>
	/// <remarks>
	/// Only spaces and tabs count as indentation, matching
	/// <see cref="Formatting.WhitespaceConverter"/> and
	/// <see cref="Formatting.TrimTrailingWhitespaceFormatter"/>: any other whitespace character, such
	/// as a non-breaking space or a form feed, is content and ends the scan, so a host that replaces
	/// the leading whitespace never deletes such characters.
	/// </remarks>
	/// <param name="lineText">The line text to inspect.</param>
	/// <returns>The number of leading indentation characters.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="lineText"/> is <see langword="null"/>.</exception>
	public static int GetLeadingWhitespaceLength(string lineText)
	{
		ArgumentNullException.ThrowIfNull(lineText);

		int length = 0;

		while (length < lineText.Length && WhitespaceScan.IsSpaceOrTab(lineText[length]))
			length++;

		return length;
	}

	/// <summary>
	/// Truncates the supplied indentation by the length of one indentation unit.
	/// </summary>
	/// <remarks>
	/// The indentation is truncated by the unit length rather than matched against the unit, so an
	/// indentation that does not start with the unit still loses its trailing characters: removing a
	/// tab unit from two leading spaces yields one space. When <paramref name="indentationUnit"/> is
	/// empty, a single trailing character is removed, and when the indentation is shorter than the
	/// unit, the whole indentation is removed. Hosts that outdent by indentation columns should
	/// compute the column-aware result themselves.
	/// </remarks>
	/// <param name="indentation">The current indentation.</param>
	/// <param name="indentationUnit">The indentation unit for one level.</param>
	/// <returns>The truncated indentation.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="indentation"/> or <paramref name="indentationUnit"/> is <see langword="null"/>.
	/// </exception>
	public static string TruncateIndentationByUnitLength(string indentation, string indentationUnit)
	{
		ArgumentNullException.ThrowIfNull(indentation);
		ArgumentNullException.ThrowIfNull(indentationUnit);

		if (string.IsNullOrEmpty(indentation))
			return string.Empty;

		int removeLength = indentationUnit.Length > 0
			? Math.Min(indentationUnit.Length, indentation.Length)
			: 1;

		return indentation[..^removeLength];
	}

	/// <summary>
	/// Builds the indentation for a line at the given relative indent level.
	/// </summary>
	/// <remarks>
	/// A non-positive <paramref name="indentLevel"/> returns <paramref name="currentLineIndentation"/>
	/// unchanged instead of an empty string.
	/// </remarks>
	/// <param name="currentLineIndentation">The base indentation of the surrounding line.</param>
	/// <param name="indentationUnit">The indentation unit for one level.</param>
	/// <param name="indentLevel">The relative indent level above the base indentation.</param>
	/// <returns>The composed indentation text.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="currentLineIndentation"/> or <paramref name="indentationUnit"/> is <see langword="null"/>.
	/// </exception>
	public static string BuildIndentation(string currentLineIndentation, string indentationUnit, int indentLevel)
	{
		ArgumentNullException.ThrowIfNull(currentLineIndentation);
		ArgumentNullException.ThrowIfNull(indentationUnit);

		if (indentLevel <= 0)
			return currentLineIndentation;

		// Compute the capacity in long so a very large indent level cannot overflow into a negative
		// hint, and clamp it so the hint can never exceed the largest representable array.
		long capacity = currentLineIndentation.Length + ((long)indentationUnit.Length * indentLevel);
		var builder = new StringBuilder((int)Math.Min(capacity, Array.MaxLength));
		builder.Append(currentLineIndentation);

		for (int i = 0; i < indentLevel; i++)
			builder.Append(indentationUnit);

		return builder.ToString();
	}

	/// <summary>
	/// Determines whether the supplied text contains a line terminator.
	/// </summary>
	/// <param name="text">The text to inspect.</param>
	/// <returns><see langword="true"/> when the text contains a line terminator; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	public static bool ContainsLineBreak(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		return LineTerminators.ContainsTerminator(text);
	}

	/// <summary>
	/// Splits the supplied text into lines, preserving each line's terminator and source offset.
	/// </summary>
	/// <remarks>
	/// The returned list is a read-only wrapper over owned storage. Use
	/// <see cref="Text.TextLineSplitter.Split"/> when only the line contents are needed.
	/// </remarks>
	/// <param name="text">The text to split.</param>
	/// <returns>The lines of the text, including a final empty line when the text is empty or ends with a line terminator.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
	public static IReadOnlyList<IndentationTextLine> SplitLines(string text)
	{
		ArgumentNullException.ThrowIfNull(text);

		var lines = new List<IndentationTextLine>();
		var enumerator = new TextLineEnumerator(text);

		while (enumerator.MoveNext())
		{
			lines.Add(new IndentationTextLine(
				enumerator.Content.ToString(),
				enumerator.Terminator.ToString(),
				enumerator.StartOffset));
		}

		return lines.AsReadOnly();
	}
}
