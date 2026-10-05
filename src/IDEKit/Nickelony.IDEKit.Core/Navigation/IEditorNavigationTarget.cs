using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Navigation;

/// <summary>Gives a quick-pick mode the editor it moves the caret in, so it can preview a position and put the view back.</summary>
/// <remarks>
/// <para>
/// Lines and columns are one-based, as the editors count them: the column of the caret at the start of a line is <c>1</c>, and the column
/// after the last character of a line is the length of the line plus one. A range is a <see cref="TextPositionRange"/>, which counts lines
/// and characters from zero. A member that takes a position clamps it to the document, so a caller may pass what a user typed.
/// </para>
/// <para>
/// <see cref="SaveView"/> and <see cref="RestoreView"/> work as a pair that a mode calls around a run
/// of <see cref="MoveCaret"/> and <see cref="SelectRange"/> calls: the first saves the caret, the selection and the scroll position, and the second
/// puts them back. Every member runs on the dispatcher thread.
/// </para>
/// </remarks>
public interface IEditorNavigationTarget
{
	/// <summary>The number of lines of the document, which is at least one.</summary>
	int LineCount { get; }

	/// <summary>The one-based line of the caret.</summary>
	int CaretLineNumber { get; }

	/// <summary>The one-based column of the caret.</summary>
	int CaretColumn { get; }

	/// <summary>Gets the length of a line without its line terminator.</summary>
	/// <param name="lineNumber">The one-based line, from one to <see cref="LineCount"/>.</param>
	/// <returns>The number of UTF-16 code units in the line.</returns>
	int GetLineLength(int lineNumber);

	/// <summary>Moves the caret to a position, clears the selection and scrolls the position into view, without taking the keyboard focus.</summary>
	/// <param name="lineNumber">The one-based line; a line outside the document clamps to it.</param>
	/// <param name="column">The one-based column; a column outside the line clamps to it.</param>
	void MoveCaret(int lineNumber, int column);

	/// <summary>
	/// Selects a range with the caret at its end and scrolls the line of its start and then the caret into view, without taking the keyboard focus.
	/// </summary>
	/// <param name="range">
	/// The range to select; an endpoint outside the document clamps to it, and a reversed range selects the same text as the ordered one.
	/// </param>
	void SelectRange(TextPositionRange range);

	/// <summary>Remembers the caret, the selection and the scroll position for a later <see cref="RestoreView"/>.</summary>
	void SaveView();

	/// <summary>Puts back what <see cref="SaveView"/> remembered, or does nothing when nothing is remembered.</summary>
	void RestoreView();
}
