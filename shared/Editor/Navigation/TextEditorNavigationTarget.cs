using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.Core.Text;
#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Editing;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Navigation;

#else

namespace Nickelony.IDEKit.AvalonEdit.Navigation;

#endif

/// <summary>Lets a navigation mode move the caret of a <see cref="TextEditor"/>, select a range in it, and put its view back.</summary>
/// <remarks>
/// A move or a selection clears the old selection, clamps its position to the document, and scrolls the position into view,
/// vertically and horizontally, without taking the keyboard focus from the quick pick. <see cref="SaveView"/> remembers the caret,
/// the selection and both scroll offsets, and <see cref="RestoreView"/> puts them back, so a preview that scrolled the editor leaves no trace.
/// The target belongs to the thread of the editor, and one target serves one editor. An editor without a document counts one empty line and ignores
/// a move.
/// </remarks>
public sealed class TextEditorNavigationTarget : IEditorNavigationTarget
{
	private readonly TextEditor _editor;

	private SavedView? _saved;

	/// <param name="editor">The editor whose caret the mode moves.</param>
	/// <exception cref="ArgumentNullException"><paramref name="editor"/> is <see langword="null"/>.</exception>
	public TextEditorNavigationTarget(TextEditor editor)
	{
		ArgumentNullException.ThrowIfNull(editor);
		_editor = editor;
	}

	/// <inheritdoc/>
	public int LineCount => _editor.Document?.LineCount ?? 1;

	/// <inheritdoc/>
	public int CaretLineNumber => _editor.TextArea.Caret.Line;

	/// <inheritdoc/>
	public int CaretColumn => _editor.TextArea.Caret.Column;

	/// <inheritdoc/>
	public int GetLineLength(int lineNumber) =>
		_editor.Document is { } document ? document.GetLineByNumber(Math.Clamp(lineNumber, 1, document.LineCount)).Length : 0;

	/// <inheritdoc/>
	public void MoveCaret(int lineNumber, int column)
	{
		if (_editor.Document is not { } document)
			return;

		int line = Math.Clamp(lineNumber, 1, document.LineCount);
		int clampedColumn = Math.Clamp(column, 1, document.GetLineByNumber(line).Length + 1);

		NavigationLocation location = _editor.TextArea.CreateCaretLocation(documentId: null, new TextLocation(line, clampedColumn));

		_editor.TextArea.ApplyLocation(location, EditorFocusMode.LeaveUnchanged);

		// Applying the location reveals the line only, so the caret is revealed here for a column beyond the right edge of the view.
		_editor.TextArea.Caret.BringCaretToView();
	}

	/// <inheritdoc/>
	public void SelectRange(TextPositionRange range)
	{
		if (_editor.Document is not { } document)
			return;

		int start = OffsetOf(document, range.Start);
		int end = OffsetOf(document, range.End);

		if (end < start)
			(start, end) = (end, start);

		TextLocation startLocation = document.GetLocation(start);

		var location = new NavigationLocation(
			DocumentId: null,
			CaretOffset: end,
			Selection: new TextRange(start, end - start),
			PreferredDocumentLine: startLocation.Line
		);

		_editor.TextArea.ApplyLocation(location, EditorFocusMode.LeaveUnchanged);
		_editor.TextArea.Caret.BringCaretToView();
	}

	/// <inheritdoc/>
	public void SaveView()
	{
		_saved = _editor.Document is null
			? null
			: new SavedView(
				new NavigationLocation(
					DocumentId: null,
					CaretOffset: _editor.TextArea.Caret.Offset,
					Selection: new TextRange(_editor.SelectionStart, _editor.SelectionLength),
					PreferredDocumentLine: null
				),

				_editor.TextArea.TextView.ScrollOffset.X,
				_editor.TextArea.TextView.ScrollOffset.Y
			);
	}

	/// <inheritdoc/>
	public void RestoreView()
	{
		if (_saved is not { } saved || _editor.Document is null)
			return;

		_editor.TextArea.ApplyLocation(saved.Location, EditorFocusMode.LeaveUnchanged);

		// Applying the location scrolls its line into view, which can differ from where the editor was scrolled to.
		EditingSeam.SetScrollOffset(_editor, saved.HorizontalOffset, saved.VerticalOffset);

		_saved = null;
	}

	// A position is zero-based and may lie outside the document; the document clamps the column, so only the line needs clamping here.
	private static int OffsetOf(TextDocument document, TextPosition position) =>
		document.GetOffset(Math.Clamp(position.Line, 0, document.LineCount - 1) + 1, Math.Max(0, position.Character) + 1);

	private readonly record struct SavedView(NavigationLocation Location, double HorizontalOffset, double VerticalOffset);
}
