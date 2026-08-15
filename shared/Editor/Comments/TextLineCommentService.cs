#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Documents;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Documents;
using Nickelony.IDEKit.AvalonEdit.Editing;
#endif
using Nickelony.IDEKit.Core.Comments;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Comments;
#else
namespace Nickelony.IDEKit.AvalonEdit.Comments;
#endif

/// <summary>
/// Creates and applies line-comment edits for selected lines in the editor's <see cref="TextDocument"/>.
/// </summary>
/// <remarks>
/// <para>
/// The edit computation is editor-neutral and lives in the Core
/// <see cref="TextLineCommentPlanner"/>: <see cref="TryCreateEdit"/> captures the document into a
/// <see cref="TextDocumentSnapshot"/> and delegates to it.
/// </para>
/// <para>
/// <see cref="ApplyEdit"/> edits the editor's document directly by default; pass a host-owned
/// <see cref="ITextEditTarget"/> to route the edit through it instead.
/// </para>
/// </remarks>
public sealed class TextLineCommentService : ITextLineCommentService
{
	/// <inheritdoc/>
	public bool TryCreateEdit(
		TextDocument document,
		TextRange selection,
		CommentSyntax commentSyntax,
		TextLineCommentAction action,
		bool insertSpaceAfterDelimiter,
		out TextLineCommentEdit edit)
	{
		ArgumentNullException.ThrowIfNull(document);

		return TextLineCommentPlanner.TryCreateEdit(
			new TextLineCommentRequest(
				new TextDocumentSnapshot(document),
				selection,
				commentSyntax,
				action,
				insertSpaceAfterDelimiter),
			out edit);
	}

	/// <inheritdoc/>
	public void ApplyEdit(
		TextEditor editor,
		CommentSyntax commentSyntax,
		TextLineCommentAction action,
		bool insertSpaceAfterDelimiter = true,
		ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(editor);

		if (editor.Document is null)
			throw new InvalidOperationException("The editor has no document assigned.");

		if (!TryCreateEdit(
			editor.Document,
			new TextRange(editor.SelectionStart, editor.SelectionLength),
			commentSyntax,
			action,
			insertSpaceAfterDelimiter,
			out TextLineCommentEdit edit))
		{
			return;
		}

		// A transformation that does not change any selected line would still replace the range with
		// identical text, which adds an undo step and marks the document changed, so it is skipped.
		string replacedText = editor.Document.GetText(edit.ReplaceRange.Offset, edit.ReplaceRange.Length);

		if (string.Equals(replacedText, edit.NewText, StringComparison.Ordinal))
			return;

		TextEditorEditOperations.ApplyOperation(
			editor,
			new TextEditOperation(edit.ReplaceRange.Offset, edit.ReplaceRange.EndOffset, edit.NewText, 0),
			editTarget);

		// Restoring the recorded post-edit selection requires the target to apply synchronously.
		int documentLength = editor.Document.TextLength;
		int selectionStart = Math.Clamp(edit.Selection.Offset, 0, documentLength);
		int selectionLength = Math.Clamp(edit.Selection.Length, 0, documentLength - selectionStart);

		editor.Select(selectionStart, selectionLength);
	}
}
