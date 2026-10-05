using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Text;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Text;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Editing;

#else

namespace Nickelony.IDEKit.AvalonEdit.Editing;

#endif

/// <summary>Provides programmatic text insertion, replacement, and line-based editing for the editor's <see cref="TextEditor"/>.</summary>
/// <remarks>
/// <para>
/// When an edit target is supplied, the edit is applied to it instead of the editor's document, and the helpers must be called
/// on the editor's thread, like all editor document access; editing is synchronous, so a host that tracks content changes updates its own state
/// after the call. The caret is always clamped to the current length of the editor's document, which does not reflect a target's post-edit content
/// until the host applies the change: a target that returns without updating the document leaves the requested caret clamped to that stale length.
/// </para>
/// <para>
/// The line replacement operations edit the editor's document directly by default; pass a host-owned
/// <see cref="ITextEditTarget"/> to route the replacement through it instead, under the contract that type describes. The selection and caret
/// helpers never use an edit target, so a host that owns document authority must observe those calls and keep its own content in sync.
/// <see cref="TryReplaceFirstMatchingLine"/> is the selector-driven member: it scans the document one line at a time and edits only the first line
/// the caller's selector accepts.
/// </para>
/// </remarks>
public static class TextEditorEditingExtensions
{
	/// <summary>
	/// Applies a single text edit described by <paramref name="request"/> and places the caret at the request's caret offset,
	/// or immediately after the inserted text when no caret offset is set.
	/// </summary>
	/// <param name="textEditor">
	/// The editor whose caret is updated and whose document is edited when <paramref name="editTarget"/> is <see langword="null"/>.
	/// </param>
	/// <param name="request">The edit to apply.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="textEditor"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The edit target (or the editor's document when no target is supplied) rejects the requested range.
	/// </exception>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	/// <remarks>
	/// With no edit target, the default target records the edit as one undo step. A request with an empty <see cref="TextEditRequest.Range"/> inserts
	/// <see cref="TextEditRequest.NewText"/> at the offset of that range.
	/// </remarks>
	public static void ApplyEdit(this TextEditor textEditor, TextEditRequest request, ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(textEditor);

		TextEditorEditApplier.ApplySingleOperation(
			textEditor,
			new TextEditOperation(range: request.Range, newText: request.NewText, editIndex: 0),
			editTarget
		);

		// The default caret offset is computed in 64-bit arithmetic so a large insert cannot overflow.
		long requestedCaretOffset = request.CaretOffsetAfterEdit ?? ((long)request.Range.Offset + request.NewText.Length);

		textEditor.CaretOffset = (int)Math.Clamp(requestedCaretOffset, 0L, textEditor.Document.TextLength);
	}

	/// <summary>
	/// Inserts <paramref name="newText"/> at <paramref name="insertOffset"/> as a single edit operation and places the caret immediately
	/// after the inserted text.
	/// </summary>
	/// <param name="textEditor">
	/// The editor whose caret is updated and whose document is edited when <paramref name="editTarget"/> is <see langword="null"/>.
	/// </param>
	/// <param name="insertOffset">The zero-based offset at which to insert the text.</param>
	/// <param name="newText">The text to insert.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="textEditor"/> or <paramref name="newText"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="insertOffset"/> is negative, or the edit target (or the editor's document when no target is supplied)
	/// rejects the requested range.
	/// </exception>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	/// <remarks>
	/// Replacing a range or setting the caret explicitly goes through <see cref="ApplyEdit(TextEditor, TextEditRequest, ITextEditTarget?)"/>.
	/// </remarks>
	public static void InsertText(this TextEditor textEditor, int insertOffset, string newText, ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(newText);

		ArgumentOutOfRangeException.ThrowIfNegative(insertOffset);

		ApplyEdit(textEditor, new TextEditRequest(new TextRange(insertOffset, 0), newText), editTarget);
	}

	/// <summary>Selects a document line's content, excluding its line terminator.</summary>
	/// <param name="textEditor">The editor whose selection is updated.</param>
	/// <param name="line">The line whose content is selected.</param>
	/// <exception cref="ArgumentNullException"><paramref name="textEditor"/> or <paramref name="line"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	/// <exception cref="ArgumentException">
	/// The line was deleted or belongs to another document; the line is resolved
	/// through <see cref="TextDocumentExtensions.GetLiveLine(TextDocument, DocumentLine)"/> first.
	/// </exception>
	public static void SelectLine(this TextEditor textEditor, DocumentLine line)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(line);

		TextDocument document = TextEditorValidation.RequireDocument(textEditor);
		DocumentLine liveLine = document.GetLiveLine(line);

		textEditor.Select(liveLine.Offset, liveLine.Length);
	}

	/// <summary>Replaces a document line's content and places the caret immediately after the replacement text.</summary>
	/// <param name="textEditor">The editor whose document is updated.</param>
	/// <param name="line">The line whose content is replaced.</param>
	/// <param name="replacement">The replacement text.</param>
	/// <param name="selection">What happens to the selection after the replacement.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/>, <paramref name="line"/>, or <paramref name="replacement"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	/// <exception cref="ArgumentException">
	/// The line was deleted or belongs to another document; the line is resolved
	/// through <see cref="TextDocumentExtensions.GetLiveLine(TextDocument, DocumentLine)"/> first.
	/// </exception>
	/// <remarks>
	/// The replaced line's terminator is preserved, so the caret ends up in front
	/// of it when one is present, and the caret is always placed at the end of the replacement text even when that text spans multiple lines.
	/// When <paramref name="selection"/> is <see cref="LineReplacementSelectionMode.Select"/>, the replacement stays selected with the caret
	/// at its end. Replacing a line with identical text leaves the document and its undo stack untouched, and when <paramref name="editTarget"/>
	/// is supplied the replacement is applied through it under the edit-target contract while the post-edit selection is applied
	/// to the editor's document.
	/// </remarks>
	public static void ReplaceLine(
		this TextEditor textEditor,
		DocumentLine line,
		string replacement,
		LineReplacementSelectionMode selection = LineReplacementSelectionMode.Collapse,
		ITextEditTarget? editTarget = null
	)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(line);
		ArgumentNullException.ThrowIfNull(replacement);

		TextDocument document = TextEditorValidation.RequireDocument(textEditor);
		DocumentLine liveLine = document.GetLiveLine(line);

		// The skip-identical policy and the post-edit selection clamp are shared with
		// TryReplaceFirstMatchingLine.
		TextEditorEditApplier.ReplaceRangeSkippingIdentical(
			textEditor,
			new RangeReplacementRequest(StartOffset: liveLine.Offset, CurrentText: document.GetText(liveLine), replacement, selection),
			editTarget
		);
	}

	/// <summary>Replaces the first document line for which <paramref name="replacementSelector"/> returns replacement text.</summary>
	/// <param name="textEditor">The editor containing the lines to inspect.</param>
	/// <param name="replacementSelector">Returns replacement text for a line, or <see langword="null"/> to skip that line.</param>
	/// <param name="scrollToLine">
	/// <see cref="LineScrollingMode.ScrollToLine"/> to scroll to the replaced line; otherwise, <see cref="LineScrollingMode.LeaveUnchanged"/>.
	/// </param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when a matching line was found; otherwise, <see langword="false"/>. An editor without a document yields
	/// <see langword="false"/> instead of throwing.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/> or <paramref name="replacementSelector"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// The scan inspects the document one line at a time and modifies only the first matching line's range,
	/// optionally scrolling to it, so the matching logic stays with the caller's selector; the selector must not mutate the document,
	/// because the scan captures the matching line's offset, text, and number and a mutation during the scan invalidates those captures.
	/// When the replacement text equals the current line text the document and its undo stack are left untouched, but the line is still reported
	/// found and the caret still moves to the end of the resulting line text, including after a multi-line replacement. When an edit target
	/// that does not update the editor's document is supplied, the caret is still applied against the editor's document.
	/// </remarks>
	public static bool TryReplaceFirstMatchingLine(
		this TextEditor textEditor,
		Func<string, string?> replacementSelector,
		LineScrollingMode scrollToLine = LineScrollingMode.ScrollToLine,
		ITextEditTarget? editTarget = null
	)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(replacementSelector);

		// A Try member reports failure instead of throwing: an editor without a document has no lines
		// to scan.
		if (textEditor.Document is null)
			return false;

		bool matched = false;
		int matchedLineNumber = 0;

		string matchedLineText = string.Empty;
		string matchedReplacementText = string.Empty;

		int matchedLineOffset = 0;

		// Find the match before editing: the document must not be mutated while its lines are being
		// enumerated, and the captured line data must describe the document as it was scanned. The offset
		// and the line number are captured during the scan, so the edit and the scroll use the scanned
		// range and line even though the handle could resolve differently after a mutation-performing
		// selector.
		foreach (DocumentLine line in textEditor.Document.Lines)
		{
			string lineText = textEditor.Document.GetText(line);
			string? replacementText = replacementSelector(lineText);

			if (replacementText is null)
				continue;

			matched = true;
			matchedLineNumber = line.LineNumber;
			matchedLineText = lineText;
			matchedReplacementText = replacementText;
			matchedLineOffset = line.Offset;
			break;
		}

		if (!matched)
			return false;

		// The scanned range goes through the shared skip-identical core, so an identical replacement
		// leaves the document and undo stack untouched while the caret still moves, and the caret is
		// clamped by the same rule as ReplaceLine.
		TextEditorEditApplier.ReplaceRangeSkippingIdentical(
			textEditor,

			new RangeReplacementRequest(
				StartOffset: matchedLineOffset,
				CurrentText: matchedLineText,
				Replacement: matchedReplacementText,
				Selection: LineReplacementSelectionMode.Collapse
			),

			editTarget
		);

		if (scrollToLine == LineScrollingMode.ScrollToLine)
			textEditor.ScrollToLine(matchedLineNumber);

		return true;
	}

	/// <summary>Replaces the entire document content with minimal edits, keeping the caret, the selection and the scroll position.</summary>
	/// <param name="textEditor">The editor whose document is updated.</param>
	/// <param name="newContent">The content to set.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="textEditor"/> or <paramref name="newContent"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">
	/// The editor has no document assigned, or the content of <paramref name="editTarget"/> differs from the editor's document.
	/// </exception>
	/// <remarks>
	/// The edits are the ones <see cref="TextIncrementalEditCalculator.ComputeHunkEdits"/> computes: one per changed
	/// run of lines, each trimmed to the characters that differ, and applied as one batch. A caret or a selection that no edit replaces
	/// keeps its place, also between two edits, and one inside a replaced span moves to the end of the line the caret was on. A change that rewrites
	/// many lines in place, such as an undo of a conversion of the indentation, therefore leaves the caret and the selection on their text.
	/// Identical content leaves the document, its undo stack and the selection untouched. When <paramref name="editTarget"/> is supplied,
	/// the edits are applied through it under the edit-target contract described by <see cref="ITextEditTarget"/>.
	/// </remarks>
	public static void ReplaceContent(this TextEditor textEditor, string newContent, ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(newContent);

		TextEditorValidation.RequireDocument(textEditor);

		string currentContent = textEditor.Document.Text;

		// The edit is computed against the editor's document, so a target that holds other text would receive offsets that do not belong to it.
		if (editTarget is not null && !string.Equals(editTarget.Text, currentContent, StringComparison.Ordinal))
			throw new InvalidOperationException("The edit target's content does not match the editor's document.");

		IReadOnlyList<TextIncrementalEdit> changes = TextIncrementalEditCalculator.ComputeHunkEdits(currentContent, newContent);

		if (changes.Count == 0)
			return;

		PreparedTextEdits edits = TextEditKernel
			.Prepare([.. changes.Select((change, index) => new TextEditOperation(range: change.Range, newText: change.NewText, editIndex: index))])
			.Edits;

		ViewPreservingEditApplier.Apply(textEditor, edits, editTarget);
	}
}
