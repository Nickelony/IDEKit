using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
#if AVALONIAEDIT
using Avalonia;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Text;
#else
using System.Windows;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Text;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Editing;

#else

namespace Nickelony.IDEKit.AvalonEdit.Editing;

#endif

/// <summary>Applies prepared edits to an editor and keeps its caret, selection and scroll position where the edits leave them intact.</summary>
/// <remarks>
/// <see cref="Apply"/> moves the caret to the end of its line where an edit replaced the caret or the selection, because no exact
/// position exists then. A rectangular selection has no offset range to restore, and neither does a batch of several edits, whose offset change map
/// lets the engine move the caret and the ends of the selection past each edit, so the engine's own mapping stands for both.
/// </remarks>
internal static class ViewPreservingEditApplier
{
	internal static void Apply(TextEditor editor, PreparedTextEdits preparedEdits, ITextEditTarget? editTarget)
	{
		var before = new EditorSelection(editor.CaretOffset, new TextRange(editor.SelectionStart, editor.SelectionLength));
		int caretLineNumber = editor.Document.GetLineByOffset(before.CaretOffset).LineNumber;

		Vector scrollOffset = editor.TextArea.TextView.ScrollOffset;
		bool isRectangle = editor.TextArea.Selection is RectangleSelection;

		TextEditorEditApplier.ApplyPreparedEdits(editor, preparedEdits, editTarget);

		// Select(start, length) would replace a rectangle with a stream selection, so the corners the engine moved stay. A batch of several
		// edits keeps the selection whole when an edit lies inside it, which the mapper would take for a replaced selection.
		if (!isRectangle && preparedEdits.Operations.Count == 1)
		{
			EditorSelection after = EditorSelectionMapper.Map(
				edits: preparedEdits,
				before,
				caretLineNumber,
				editedText: new TextDocumentSnapshot(editor.Document)
			);

			editor.Select(after.Selection.Offset, after.Selection.Length);
			editor.CaretOffset = after.CaretOffset;
		}

		EditingSeam.SetScrollOffset(editor, scrollOffset.X, scrollOffset.Y);
	}
}
