#if AVALONIAEDIT
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Snippets;
#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Snippets;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

#endif

/// <summary>Ends the editor's snippet mode when the text area leaves the text that the snippet inserted.</summary>
/// <remarks>
/// <para>
/// The editor keeps its snippet input handler on the text area until Enter, Esc, a deleted snippet or another snippet ends it. A caret that moves
/// out of the inserted text, or a document that replaces the snippet's, would leave the handler taking Tab and Enter
/// from text it knows nothing about, and Tab would fail on anchors of the old document. The session deactivates the snippet's context in both cases,
/// and unsubscribes when the context deactivates for any reason.
/// </para>
/// <para>
/// The inserted range keeps its end after text typed at it, so a tabstop that ends the snippet can grow while the caret stays inside.
/// </para>
/// </remarks>
internal sealed class CompletionSnippetSession
{
	private readonly TextArea _textArea;
	private readonly InsertionContext _context;

	private readonly TextAnchor _start;
	private readonly TextAnchor _end;

	private CompletionSnippetSession(TextArea textArea, InsertionContext context, int startOffset, int endOffset)
	{
		_textArea = textArea;
		_context = context;

		_start = context.Document.CreateAnchor(startOffset);
		_start.MovementType = AnchorMovementType.BeforeInsertion;
		_start.SurviveDeletion = true;

		_end = context.Document.CreateAnchor(endOffset);
		_end.MovementType = AnchorMovementType.AfterInsertion;
		_end.SurviveDeletion = true;

		_textArea.Caret.PositionChanged += HandleCaretPositionChanged;
		_textArea.DocumentChanged += HandleDocumentChanged;
		_context.Deactivated += HandleDeactivated;
	}

	/// <summary>Starts watching a snippet that the editor has finished inserting.</summary>
	/// <param name="textArea">The text area that holds the snippet's input handler.</param>
	/// <param name="context">The context of the snippet, after <see cref="InsertionContext.RaiseInsertionCompleted"/>.</param>
	/// <param name="startOffset">The offset where the snippet's text starts.</param>
	/// <param name="endOffset">The offset where the snippet's text ends.</param>
	internal static void Start(TextArea textArea, InsertionContext context, int startOffset, int endOffset) =>
		_ = new CompletionSnippetSession(textArea, context, startOffset, endOffset);

	private void HandleCaretPositionChanged(object? sender, EventArgs e)
	{
		int offset = _textArea.Caret.Offset;

		if (offset < _start.Offset || offset > _end.Offset)
			End();
	}

	private void HandleDocumentChanged(object? sender, EventArgs e) => End();

	private void End() => _context.Deactivate(new SnippetEventArgs(DeactivateReason.Unknown));

	private void HandleDeactivated(object? sender, SnippetEventArgs e)
	{
		_textArea.Caret.PositionChanged -= HandleCaretPositionChanged;
		_textArea.DocumentChanged -= HandleDocumentChanged;
		_context.Deactivated -= HandleDeactivated;
	}
}
