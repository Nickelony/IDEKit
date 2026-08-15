#if AVALONIAEDIT
using Avalonia;
using AvaloniaEdit;
using AvaloniaEdit.Document;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using System.Windows;
#endif
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Formatting;
using Nickelony.IDEKit.Core.Text;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Editing;
#else
namespace Nickelony.IDEKit.AvalonEdit.Editing;
#endif

/// <summary>
/// Applies formatted document changes to the editor's <see cref="TextEditor"/> as minimal edits; the
/// built-in target applies them as one undo step.
/// </summary>
/// <remarks>
/// <para>
/// The replacement is computed from the common prefix and suffix of the formatting result, so content
/// outside the exchanged range is not rewritten; offsets after that range shift by the length
/// difference of the replacement, and positions that survive the change are preserved.
/// </para>
/// <para>
/// A supplied edit target is the apply route for the batch, not a second content source: it must hold
/// the same content as the editor's document, which is both what the formatter receives and where the
/// caret, selection, and scroll are read and restored. A target that reports other content is rejected
/// with an <see cref="InvalidOperationException"/> before the formatter runs.
/// </para>
/// </remarks>
public sealed class TextDocumentFormattingService : ITextDocumentFormattingService
{
	/// <inheritdoc/>
	public void FormatDocument(
		TextEditor editor,
		ITextDocumentFormatter formatter,
		ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(editor);
		ArgumentNullException.ThrowIfNull(formatter);

		if (editor.Document is null)
			throw new InvalidOperationException("The editor has no document assigned.");

		// The batch, the caret, the selection, and the scroll are all expressed in the editor's document,
		// so every offset in the operation belongs to one text. A supplied target is the apply route and
		// not a second content source: the edit-target contract requires it to hold the same content as
		// the editor's document, and applying a batch prepared here to a target whose content differs
		// would corrupt that content and move the editor's caret through offsets that do not belong to it.
		// The mismatch is verified - the whole-document comparison is negligible next to the formatting -
		// because an unverified mismatch is otherwise silent.
		string originalContent = editor.Text;

		if (editTarget is not null && !string.Equals(editTarget.Text, originalContent, StringComparison.Ordinal))
			throw new InvalidOperationException("The edit target's content does not match the editor's document.");

		string? formattedContent = formatter.FormatDocument(originalContent);

		// A formatter that declines (null) produced no changes, so nothing is applied.
		if (formattedContent is null)
			return;

		TextIncrementalEdit change = TextIncrementalEditCalculator.Compute(originalContent, formattedContent);

		// Equal content produces an empty range with no replacement text.
		if (change.Range.Length == 0 && change.NewText.Length == 0)
			return;

		// The batch is built before the apply so its offset mapping resolves the post-edit caret and
		// selection through the same core implementation that validates the operations.
		PreparedTextEdits preparedEdits = TextEditKernel
			.Prepare([new TextEditOperation(change.Range.Offset, change.Range.EndOffset, change.NewText, 0)])
			.Edits;

		int caretOffset = editor.CaretOffset;
		int selectionStart = editor.SelectionStart;
		int selectionEnd = selectionStart + editor.SelectionLength;
		bool hadSelection = editor.SelectionLength > 0;
		int caretLineNumber = editor.Document.GetLineByOffset(caretOffset).LineNumber;

		bool caretSurvives = caretOffset <= change.Range.Offset || caretOffset >= change.Range.EndOffset;
		bool selectionSurvives = selectionEnd <= change.Range.Offset || selectionStart >= change.Range.EndOffset;

		Vector scrollOffset = editor.TextArea.TextView.ScrollOffset;

		TextEditorEditOperations.ApplyOperations(editor, preparedEdits, editTarget);

		if (caretSurvives && (selectionSurvives || !hadSelection))
		{
			// The caret and the selection lie outside the changed range, so both can be mapped exactly.
			int documentLength = editor.Document.TextLength;
			int mappedCaret = Math.Clamp(preparedEdits.MapOffset(caretOffset), 0, documentLength);
			int mappedSelectionStart = Math.Clamp(preparedEdits.MapOffset(selectionStart), 0, documentLength);
			int mappedSelectionEnd = Math.Clamp(preparedEdits.MapOffset(selectionEnd), 0, documentLength);

			if (mappedSelectionEnd < mappedSelectionStart)
				(mappedSelectionStart, mappedSelectionEnd) = (mappedSelectionEnd, mappedSelectionStart);

			editor.Select(mappedSelectionStart, mappedSelectionEnd - mappedSelectionStart);
			editor.CaretOffset = Math.Clamp(mappedCaret, mappedSelectionStart, mappedSelectionEnd);
		}
		else if (caretLineNumber <= editor.Document.LineCount)
		{
			// The changed range covers the caret or the selection, so their exact positions cannot be
			// mapped; fall back to the end of the line the caret was on.
			DocumentLine line = editor.Document.GetLineByNumber(caretLineNumber);
			editor.Select(line.EndOffset, 0);
		}
		else
		{
			editor.Select(editor.Document.TextLength, 0);
		}

		editor.ScrollToHorizontalOffset(scrollOffset.X);
		editor.ScrollToVerticalOffset(scrollOffset.Y);
	}
}
