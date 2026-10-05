using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Indentation;

/// <summary>Plans the edits that convert the leading whitespace of every line of a snapshot between spaces and tabs.</summary>
/// <remarks>
/// <para>
/// Each plan holds one edit per line whose leading whitespace changes, and an edit replaces that whitespace and nothing else, so the content
/// and the line terminator of a line never change. The plans apply the rules of <see cref="WhitespaceOperations"/> to a snapshot
/// without copying its text, so a binding applies them to an editor document as one batch.
/// </para>
/// <para>
/// A size above <see cref="IndentationOperations.MaximumSize"/> is rejected. A plan for a snapshot that needs no change is empty.
/// </para>
/// </remarks>
public static class IndentationPlanner
{
	/// <summary>Plans the conversion of the leading tabs of every line to spaces.</summary>
	/// <param name="snapshot">The text to plan against.</param>
	/// <param name="tabSize">The number of spaces per tab stop.</param>
	/// <returns>One edit per line that has a tab in its leading whitespace, in line order.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="tabSize"/> is less than or equal to zero or greater than <see cref="IndentationOperations.MaximumSize"/>.
	/// </exception>
	public static IReadOnlyList<TextIncrementalEdit> PlanToSpaces(ITextSnapshot snapshot, int tabSize)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		LeadingIndentation.ValidateSize(tabSize);

		return Plan(snapshot, conversion: static (indentation, size, _) => LeadingIndentation.ToSpaces(indentation, size), size: tabSize, newSize: 0);
	}

	/// <summary>Plans the conversion of the leading groups of spaces that fill a tab stop of every line to tabs.</summary>
	/// <param name="snapshot">The text to plan against.</param>
	/// <param name="tabSize">The number of spaces per tab stop.</param>
	/// <returns>One edit per line whose leading spaces reach a tab stop, in line order.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="tabSize"/> is less than or equal to zero or greater than <see cref="IndentationOperations.MaximumSize"/>.
	/// </exception>
	public static IReadOnlyList<TextIncrementalEdit> PlanToTabs(ITextSnapshot snapshot, int tabSize)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		LeadingIndentation.ValidateSize(tabSize);

		return Plan(snapshot, conversion: static (indentation, size, _) => LeadingIndentation.ToTabs(indentation, size), size: tabSize, newSize: 0);
	}

	/// <summary>Plans the change of the number of spaces of one indentation level.</summary>
	/// <param name="snapshot">The text to plan against.</param>
	/// <param name="currentSize">The number of spaces of one level in <paramref name="snapshot"/>.</param>
	/// <param name="newSize">The number of spaces of one level after the edits.</param>
	/// <returns>One edit per line whose space indentation changes, in line order.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="currentSize"/> or <paramref name="newSize"/> is less than or equal to zero or greater
	/// than <see cref="IndentationOperations.MaximumSize"/>.
	/// </exception>
	/// <remarks>
	/// A line whose leading whitespace holds a tab has no edit, and a remainder that is not a whole level is kept,
	/// as <see cref="WhitespaceOperations.ResizeIndentation"/> describes.
	/// </remarks>
	public static IReadOnlyList<TextIncrementalEdit> PlanResize(ITextSnapshot snapshot, int currentSize, int newSize)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		LeadingIndentation.ValidateSize(currentSize);
		LeadingIndentation.ValidateSize(newSize);

		return Plan(
			snapshot,
			conversion: static (indentation, size, resized) => LeadingIndentation.Resize(indentation, size, resized),
			size: currentSize,
			newSize
		);
	}

	private delegate string? IndentationConversion(ReadOnlySpan<char> indentation, int size, int newSize);

	private static List<TextIncrementalEdit> Plan(ITextSnapshot snapshot, IndentationConversion conversion, int size, int newSize)
	{
		List<TextIncrementalEdit> edits = [];

		foreach (ITextLine line in snapshot.Lines)
		{
			int indentLength = 0;

			while (indentLength < line.Length && WhitespaceScan.IsSpaceOrTab(snapshot.GetCharAt(line.Offset + indentLength)))
				indentLength++;

			if (indentLength == 0)
				continue;

			string? converted = conversion(snapshot.GetText(line.Offset, indentLength), size, newSize);

			if (converted is not null)
				edits.Add(new TextIncrementalEdit(new TextRange(line.Offset, indentLength), converted));
		}

		return edits;
	}
}
