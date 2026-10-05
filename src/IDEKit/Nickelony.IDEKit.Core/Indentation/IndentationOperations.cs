using System.Text;

namespace Nickelony.IDEKit.Core.Indentation;

/// <summary>Provides language-independent helpers for computing and manipulating indentation text.</summary>
public static class IndentationOperations
{
	/// <summary>The largest number of columns that one indentation level or one tab stop may have.</summary>
	/// <remarks>
	/// It is the limit of the <c>IndentationSize</c> option of both editor engines, so a size within it is always applicable
	/// to an editor. <see cref="WhitespaceOperations"/>, <see cref="IndentationPlanner"/> and <see cref="IndentationDetector"/> reject a larger size.
	/// </remarks>
	public const int MaximumSize = 1000;

	/// <summary>The number of spaces used when spaces are requested and the indentation size is not positive.</summary>
	private const int DefaultIndentationSize = 4;

	/// <summary>Creates the indentation unit that should be appended for one extra indent level.</summary>
	/// <remarks>
	/// When converting to spaces, a non-positive <paramref name="indentationSize"/> falls back to a fixed default width instead of producing
	/// an empty unit; a host that resolves the size from a tab size passes that resolved value.
	/// </remarks>
	public static string CreateIndentationUnit(bool convertTabsToSpaces, int indentationSize)
	{
		if (!convertTabsToSpaces)
			return "\t";

		int size = indentationSize > 0 ? indentationSize : DefaultIndentationSize;

		return new string(' ', size);
	}

	/// <summary>Truncates the supplied indentation by the length of one indentation unit.</summary>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="indentation"/> or <paramref name="indentationUnit"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// Truncates by the unit length rather than matching the unit, so an indentation that does not start with the unit still loses its trailing
	/// characters (a tab unit removes one character from two leading spaces). An empty unit removes a single trailing character, and an indentation
	/// shorter than the unit is removed entirely; callers that outdent by columns compute the column-aware result themselves.
	/// </remarks>
	public static string TruncateIndentationByUnitLength(string indentation, string indentationUnit)
	{
		ArgumentNullException.ThrowIfNull(indentation);
		ArgumentNullException.ThrowIfNull(indentationUnit);

		if (string.IsNullOrEmpty(indentation))
			return string.Empty;

		int removeLength = indentationUnit.Length > 0 ? Math.Min(indentationUnit.Length, indentation.Length) : 1;

		return indentation[..^removeLength];
	}

	/// <summary>Builds the indentation for a line at the given relative indent level.</summary>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="currentLineIndentation"/> or <paramref name="indentationUnit"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// A non-positive <paramref name="indentLevel"/> returns <paramref name="currentLineIndentation"/> unchanged rather than an empty string.
	/// </remarks>
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
}
