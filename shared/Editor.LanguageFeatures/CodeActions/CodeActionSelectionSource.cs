using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;
#if AVALONIAEDIT
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Text;
#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Text;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;

#endif

/// <summary>Reports the caret, selection, and document of a text area to a <see cref="CodeActionController"/>.</summary>
/// <remarks>
/// The source listens to the caret, the selection, the text area's document, and the content of the document it currently shows.
/// A document replacement attaches to the new document first and then raises <see cref="DocumentReplaced"/>, so the controller that reacts finds
/// the new document in place. The events are raised on the text area's thread.
/// </remarks>
internal sealed class CodeActionSelectionSource : ICodeActionSelectionSource
{
	private readonly TextArea _textArea;

	private TextDocument? _document;

	/// <param name="textArea">The text area whose context the source reports.</param>
	internal CodeActionSelectionSource(TextArea textArea)
	{
		_textArea = textArea;

		textArea.Caret.PositionChanged += HandleContextChanged;
		textArea.SelectionChanged += HandleContextChanged;
		textArea.DocumentChanged += HandleDocumentChanged;
		AttachDocument(textArea.Document);
	}

	/// <inheritdoc/>
	public event EventHandler? Changed;

	/// <inheritdoc/>
	public event EventHandler? DocumentReplaced;

	/// <inheritdoc/>
	public int? CaretLineNumber
	{
		get
		{
			TextDocument? document = _textArea.Document;

			if (document is null)
				return null;

			int offset = document.ClampOffset(_textArea.Caret.Offset);
			return document.GetLineByOffset(offset).LineNumber;
		}
	}

	/// <inheritdoc/>
	public CodeActionSelection? Capture()
	{
		TextDocument? document = _textArea.Document;

		if (document is null)
			return null;

		// The context carries a snapshot of the document: creating it is amortized constant time and copies
		// no text, so only a request builder that reads the text pays for the copy.
		var snapshot = new TextDocumentSnapshot(document);
		int caretOffset = document.ClampOffset(_textArea.Caret.Offset);

		TextRange selection = GetSelection(caretOffset);

		return new CodeActionSelection(snapshot, caretOffset, selection);
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		_textArea.Caret.PositionChanged -= HandleContextChanged;
		_textArea.SelectionChanged -= HandleContextChanged;
		_textArea.DocumentChanged -= HandleDocumentChanged;
		AttachDocument(null);
	}

	private void HandleContextChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

	private void HandleDocumentChanged(object? sender, EventArgs e)
	{
		AttachDocument(_textArea.Document);
		DocumentReplaced?.Invoke(this, EventArgs.Empty);
	}

	private void HandleDocumentContentChanged(object? sender, DocumentChangeEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

	private void AttachDocument(TextDocument? document)
	{
		if (ReferenceEquals(_document, document))
			return;

		if (_document is not null)
			_document.Changed -= HandleDocumentContentChanged;

		_document = document;

		if (_document is not null)
			_document.Changed += HandleDocumentContentChanged;
	}

	/// <summary>Resolves the selection range the request context carries.</summary>
	/// <param name="caretOffset">The clamped caret offset, used when no non-empty selection is present.</param>
	/// <returns>
	/// The selected range; an empty range at <paramref name="caretOffset"/> when the selection is empty or its segment is unavailable.
	/// </returns>
	/// <remarks>
	/// The editor exposes a selection as one surrounding <see cref="ISegment"/> even when the user selected several disjoint ranges, so a multi-range
	/// selection is collapsed to a single ordered range here. The context therefore cannot distinguish the individual ranges; a host that must act
	/// on each range separately has to read the editor's selection itself.
	/// </remarks>
	private TextRange GetSelection(int caretOffset)
	{
		Selection? selection = _textArea.Selection;

		if (selection is null || selection.IsEmpty)
			return new TextRange(caretOffset, 0);

		ISegment? segment = selection.SurroundingSegment;

		// A segment starts at its lower offset whichever way the user dragged, so a backward selection needs no reordering.
		return segment is null ? new TextRange(caretOffset, 0) : TextRange.FromBounds(segment.Offset, segment.EndOffset);
	}
}
