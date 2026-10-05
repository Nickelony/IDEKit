using Nickelony.IDEKit.Core.Comments;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Scanning;
using Nickelony.IDEKit.Core.Text;
#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Text;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Text;
using Nickelony.IDEKit.AvalonEdit.Editing;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Comments;

#else

namespace Nickelony.IDEKit.AvalonEdit.Comments;

#endif

/// <summary>Creates and applies line-comment edits for selected lines in the editor's <see cref="TextDocument"/>.</summary>
/// <remarks>
/// The edit computation is editor-neutral and lives in the Core <see cref="LineCommentPlanner"/>, which <see cref="TryCreateEdit"/> delegates
/// to over a <see cref="TextDocumentSnapshot"/>. <see cref="ApplyEdit"/> edits the editor's document directly by default; pass a host-owned
/// <see cref="ITextEditTarget"/> to route the edit through it instead.
/// </remarks>
public sealed class LineCommentService : ILineCommentService
{
	/// <inheritdoc/>
	public bool TryCreateEdit(
		TextDocument document,
		TextRange selection,
		CommentSyntax commentSyntax,
		LineCommentActionKind action,
		LineCommentSpacingStyle spacing,
		out LineCommentEdit edit
	)
	{
		ArgumentNullException.ThrowIfNull(document);

		return LineCommentPlanner.TryCreateEdit(
			new LineCommentRequest(Snapshot: new TextDocumentSnapshot(document), selection, Syntax: commentSyntax, action, spacing),
			out edit
		);
	}

	/// <inheritdoc/>
	public void ApplyEdit(
		TextEditor editor,
		CommentSyntax commentSyntax,
		LineCommentActionKind action,
		LineCommentSpacingStyle spacing = LineCommentSpacingStyle.SpaceAfterDelimiter,
		ITextEditTarget? editTarget = null
	)
	{
		ArgumentNullException.ThrowIfNull(editor);

		TextEditorValidation.RequireDocument(editor);

		if (
			!TryCreateEdit(
				document: editor.Document,
				selection: new TextRange(editor.SelectionStart, editor.SelectionLength),
				commentSyntax,
				action,
				spacing,
				out LineCommentEdit edit
			)
		)
		{
			return;
		}

		// A transformation that does not change any selected line would still replace the range with
		// identical text, which adds an undo step and marks the document changed, so it is skipped.
		string replacedText = editor.Document.GetText(edit.ReplaceRange.Offset, edit.ReplaceRange.Length);

		if (string.Equals(replacedText, edit.NewText, StringComparison.Ordinal))
			return;

		TextEditorEditApplier.ApplySingleOperation(
			editor,
			new TextEditOperation(range: edit.ReplaceRange, newText: edit.NewText, editIndex: 0),
			editTarget
		);

		// Restoring the recorded post-edit selection requires the target to apply synchronously.
		int documentLength = editor.Document.TextLength;
		int selectionStart = Math.Clamp(edit.Selection.Offset, 0, documentLength);
		int selectionLength = Math.Clamp(edit.Selection.Length, 0, documentLength - selectionStart);

		editor.Select(selectionStart, selectionLength);
	}
}
