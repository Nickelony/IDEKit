#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Documents;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Documents;
#endif
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Editing;
#else
namespace Nickelony.IDEKit.AvalonEdit.Editing;
#endif

/// <summary>
/// Provides line-based editing operations for the editor's <see cref="TextEditor"/>.
/// </summary>
/// <remarks>
/// <para>
/// The replacement operations edit the editor's document directly by default; pass a host-owned
/// <see cref="ITextEditTarget"/> to route the replacement through it instead. A supplied target must
/// satisfy the edit-target contract described by <see cref="ITextEditTarget"/>.
/// </para>
/// <para>
/// The selection and caret helpers never use an edit target, so a host that owns document authority
/// must observe those calls and keep its own content in sync.
/// </para>
/// <para>
/// <see cref="TryReplaceFirstMatchingLine"/> is the selector-driven member: it scans the document one line
/// at a time and edits only the first line the caller's selector accepts.
/// </para>
/// </remarks>
public static class TextEditorLineOperations
{
	/// <summary>
	/// Selects a document line's content, excluding its line terminator.
	/// </summary>
	/// <param name="textEditor">The editor whose selection is updated.</param>
	/// <param name="line">The line whose content is selected.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/> or <paramref name="line"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	/// <exception cref="ArgumentException">
	/// The line was deleted or belongs to another document; the line is resolved through
	/// <see cref="TextDocumentExtensions.GetLiveLine(TextDocument, DocumentLine)"/> first.
	/// </exception>
	public static void SelectLine(this TextEditor textEditor, DocumentLine line)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(line);

		if (textEditor.Document is null)
			throw new InvalidOperationException("The editor has no document assigned.");

		DocumentLine liveLine = textEditor.Document.GetLiveLine(line);

		textEditor.Select(liveLine.Offset, liveLine.Length);
	}

	/// <summary>
	/// Replaces a document line's content and places the caret immediately after the replacement text.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The replaced line's terminator is preserved, so the caret ends up in front of it when one is present.
	/// The replacement may span multiple lines; the caret is always placed at the end of the replacement text,
	/// which is the end of the last replacement line.
	/// </para>
	/// <para>
	/// When <paramref name="selectReplacement"/> is <see langword="true"/>, the replacement stays selected
	/// instead of collapsing the selection, and the caret remains at the end of the selection.
	/// </para>
	/// <para>
	/// Replacing a line with identical text leaves the document and its undo stack untouched;
	/// only the selection or caret is updated.
	/// </para>
	/// <para>
	/// When <paramref name="editTarget"/> is supplied, the replacement is applied through it under the
	/// edit-target contract described by <see cref="ITextEditTarget"/>, and the post-edit selection
	/// is applied to the editor's document.
	/// </para>
	/// </remarks>
	/// <param name="textEditor">The editor whose document is updated.</param>
	/// <param name="line">The line whose content is replaced.</param>
	/// <param name="replacement">The replacement text.</param>
	/// <param name="selectReplacement">Whether the replacement stays selected afterwards.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the
	/// editor's document directly.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/>, <paramref name="line"/>, or <paramref name="replacement"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	/// <exception cref="ArgumentException">
	/// The line was deleted or belongs to another document; the line is resolved through
	/// <see cref="TextDocumentExtensions.GetLiveLine(TextDocument, DocumentLine)"/> first.
	/// </exception>
	public static void ReplaceLine(
		this TextEditor textEditor,
		DocumentLine line,
		string replacement,
		bool selectReplacement = false,
		ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(line);
		ArgumentNullException.ThrowIfNull(replacement);

		if (textEditor.Document is null)
			throw new InvalidOperationException("The editor has no document assigned.");

		DocumentLine liveLine = textEditor.Document.GetLiveLine(line);

		// The skip-identical policy and the post-edit selection clamp are shared with
		// TryReplaceFirstMatchingLine.
		TextEditorEditOperations.ReplaceRangeSkippingIdentical(
			textEditor,
			liveLine.Offset,
			textEditor.Document.GetText(liveLine),
			replacement,
			selectReplacement,
			editTarget);
	}

	/// <summary>
	/// Replaces the first document line for which <paramref name="replacementSelector"/> returns replacement text.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The scan inspects the document one line at a time and modifies only the first matching line's range,
	/// optionally scrolling to it; the matching logic is supplied by the caller, so the operation stays
	/// domain-agnostic. The replacement selector must not mutate the document: the scan enumerates the
	/// document's lines and captures the matching line's offset, text, and number, and a mutation during the
	/// scan invalidates those captures (line numbers and offsets shift).
	/// </para>
	/// <para>
	/// When the selected replacement text equals the current line text, the document and its undo stack are
	/// left untouched: the matching line is still reported as found, and the caret is still placed at the end
	/// of the resulting line text. A multi-line replacement spans the line's range, so the caret ends after the
	/// last replacement line. When an edit target that does not update the editor's document is supplied, the
	/// caret is still applied against the editor's document; see <see cref="ITextEditTarget"/> for the
	/// canonical edit-target contract.
	/// </para>
	/// </remarks>
	/// <param name="textEditor">The editor containing the lines to inspect.</param>
	/// <param name="replacementSelector">
	/// Returns replacement text for a line, or <see langword="null"/> to skip that line.
	/// </param>
	/// <param name="scrollToLine">Whether to scroll to the replaced line.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when a matching line was found; otherwise, <see langword="false"/>. An editor
	/// without a document yields <see langword="false"/> instead of throwing.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/> or <paramref name="replacementSelector"/> is <see langword="null"/>.
	/// </exception>
	public static bool TryReplaceFirstMatchingLine(
		this TextEditor textEditor,
		Func<string, string?> replacementSelector,
		bool scrollToLine = true,
		ITextEditTarget? editTarget = null)
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
		TextEditorEditOperations.ReplaceRangeSkippingIdentical(
			textEditor,
			matchedLineOffset,
			matchedLineText,
			matchedReplacementText,
			selectReplacement: false,
			editTarget: editTarget);

		if (scrollToLine)
			textEditor.ScrollToLine(matchedLineNumber);

		return true;
	}

	/// <summary>
	/// Replaces the entire document content and places the caret at the end of the resulting document.
	/// </summary>
	/// <remarks>
	/// Setting identical content leaves the document and its undo stack untouched;
	/// the caret still moves to the document end. When <paramref name="editTarget"/> is supplied,
	/// the replacement is applied through it under the edit-target contract described by
	/// <see cref="ITextEditTarget"/>, and the caret is placed against the editor's document.
	/// </remarks>
	/// <param name="textEditor">The editor whose document is updated.</param>
	/// <param name="newContent">The content to set.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the
	/// editor's document directly.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/> or <paramref name="newContent"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	public static void ReplaceContent(this TextEditor textEditor, string newContent, ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(newContent);

		if (textEditor.Document is null)
			throw new InvalidOperationException("The editor has no document assigned.");

		if (!string.Equals(textEditor.Document.Text, newContent, StringComparison.Ordinal))
		{
			TextEditorEditOperations.ApplyOperation(
				textEditor,
				new TextEditOperation(0, textEditor.Document.TextLength, newContent, 0),
				editTarget);
		}

		textEditor.Select(textEditor.Document.TextLength, 0);
	}
}
