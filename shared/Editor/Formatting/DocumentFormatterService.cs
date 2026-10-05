using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Formatting;
using Nickelony.IDEKit.Core.Text;
#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.Editing;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Formatting;

#else

namespace Nickelony.IDEKit.AvalonEdit.Formatting;

#endif

/// <summary>
/// Applies formatted document changes to the editor's <see cref="TextEditor"/> as minimal edits; the built-in target applies them as one undo step.
/// </summary>
/// <remarks>
/// The replacement is computed from the common prefix and suffix of the formatting result,
/// so content outside the exchanged range is not rewritten. A supplied edit target is the apply route for the batch, not a second content source;
/// see <see cref="IDocumentFormatterService.FormatDocument"/> for the full contract.
/// </remarks>
public sealed class DocumentFormatterService : IDocumentFormatterService
{
	/// <inheritdoc/>
	public void FormatDocument(TextEditor editor, IDocumentFormatter formatter, ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(editor);
		ArgumentNullException.ThrowIfNull(formatter);

		TextEditorValidation.RequireDocument(editor);

		// The batch, the caret, the selection, and the scroll are all expressed in the editor's document,
		// so every offset belongs to one text. A supplied target is the apply route, not a second content
		// source: the edit-target contract requires it to hold the same content as the editor's document,
		// and applying a batch prepared here to a differing target would corrupt that content and move the
		// caret through offsets that do not belong to it. The mismatch is verified - the whole-document
		// comparison is negligible next to the formatting - because an unverified mismatch is silent.
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
			.Prepare([new TextEditOperation(range: change.Range, newText: change.NewText, editIndex: 0)])
			.Edits;

		ViewPreservingEditApplier.Apply(editor, preparedEdits, editTarget);
	}

	/// <inheritdoc/>
	public void ApplyEdits(TextEditor editor, PreparedTextEdits edits, ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(editor);
		ArgumentNullException.ThrowIfNull(edits);

		TextEditorValidation.RequireDocument(editor);

		if (editTarget is not null && !string.Equals(editTarget.Text, editor.Text, StringComparison.Ordinal))
			throw new InvalidOperationException("The edit target's content does not match the editor's document.");

		ViewPreservingEditApplier.Apply(editor, edits, editTarget);
	}
}
