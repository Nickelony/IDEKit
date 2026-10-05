using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Editing;

/// <summary>Maps the caret and the selection of an editor through a batch of prepared edits.</summary>
/// <remarks>
/// An editor that applies a batch of edits keeps the caret and the selection where they were in the text, so a formatter that rewrites only the lines
/// it changes does not move the user's place. Where an edit replaced the caret or the selection there is no exact position to keep, and the caret
/// moves to the end of the line it was on.
/// </remarks>
public static class EditorSelectionMapper
{
	/// <summary>
	/// Maps the caret and the selection through the edits, or moves them to the end of the caret's line when an edit replaced them.
	/// </summary>
	/// <param name="edits">The edits the editor applied.</param>
	/// <param name="before">The caret and the selection in the text the edits were prepared against.</param>
	/// <param name="caretLineNumber">The one-based number of the line that held the caret before the edits.</param>
	/// <param name="editedText">The text after the edits.</param>
	/// <returns>The caret and the selection in the edited text.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="edits"/> or <paramref name="editedText"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="caretLineNumber"/> is less than 1.</exception>
	/// <remarks>
	/// The caret keeps its place when no edit replaces
	/// the text it sits inside, and a non-empty selection keeps its place when no edit overlaps it; an empty selection never forces the fallback.
	/// A kept position is mapped with <see cref="PreparedTextEdits.MapOffset(int)"/>, clamped into the edited text, and the caret is clamped
	/// into the mapped selection. The fallback is an empty selection at the end of the caret's line in the edited text, or at the end of the text
	/// when the edits removed that line.
	/// </remarks>
	public static EditorSelection Map(PreparedTextEdits edits, EditorSelection before, int caretLineNumber, ITextSnapshot editedText)
	{
		ArgumentNullException.ThrowIfNull(edits);
		ArgumentNullException.ThrowIfNull(editedText);

		ArgumentOutOfRangeException.ThrowIfLessThan(caretLineNumber, 1);

		TextRange selection = before.Selection;

		bool caretSurvives = true;
		bool selectionSurvives = true;

		foreach (TextEditOperation operation in edits.Operations)
		{
			TextRange replaced = operation.Range;

			caretSurvives &= before.CaretOffset <= replaced.Offset || before.CaretOffset >= replaced.EndOffset;
			selectionSurvives &= selection.EndOffset <= replaced.Offset || selection.Offset >= replaced.EndOffset;
		}

		if (caretSurvives && (selectionSurvives || selection.IsEmpty))
		{
			int textLength = editedText.TextLength;
			int caretOffset = edits.MapOffset(before.CaretOffset);
			int selectionStart = Math.Clamp(edits.MapOffset(selection.Offset), 0, textLength);
			int selectionEnd = Math.Clamp(edits.MapOffset(selection.EndOffset), 0, textLength);

			if (selectionEnd < selectionStart)
				(selectionStart, selectionEnd) = (selectionEnd, selectionStart);

			// The selection already lies inside the edited text, so clamping the caret into it keeps the caret there too.
			return new EditorSelection(Math.Clamp(caretOffset, selectionStart, selectionEnd), TextRange.FromBounds(selectionStart, selectionEnd));
		}

		int lineEnd = caretLineNumber <= editedText.LineCount ? editedText.GetLineByNumber(caretLineNumber).EndOffset : editedText.TextLength;

		return new EditorSelection(lineEnd, new TextRange(lineEnd, 0));
	}
}
