using System.Runtime.CompilerServices;
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

/// <summary>Applies prepared edits and line replacements to an editor through its edit target.</summary>
/// <remarks>
/// The editing extensions, the auto-closing, line-comment and formatting services share this applier, so a host-owned <see cref="ITextEditTarget"/>
/// and the editor's default target are resolved in one place.
/// </remarks>
internal static class TextEditorEditApplier
{
	// The default target is cached per editor: this path runs on every keystroke (typing, auto-closing,
	// backspace), and an uncached target would allocate per call. The weak table keeps the target alive
	// exactly as long as its editor, and the target holds no event subscriptions of its own.
	private static readonly ConditionalWeakTable<TextEditor, TextEditorEditTarget> s_defaultEditTargets = [];

	/// <summary>
	/// Applies a single <paramref name="operation"/> through <paramref name="editTarget"/>, or directly to the editor's document when no target
	/// is supplied.
	/// </summary>
	/// <param name="textEditor">The editor whose document is edited when <paramref name="editTarget"/> is <see langword="null"/>.</param>
	/// <param name="operation">The validated operation to apply.</param>
	/// <param name="editTarget">The host-owned target to apply to, or <see langword="null"/> to edit the editor's document directly.</param>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	/// <remarks>
	/// This is the single-operation fast path the editing helpers use for the hot paths (typing, auto-closing, and backspace). For the built-in
	/// target it bypasses the <see cref="PreparedTextEdits"/> carrier (and its defensive copy); a host target keeps the documented batch contract,
	/// so the one operation travels as a one-operation batch.
	/// </remarks>
	internal static void ApplySingleOperation(TextEditor textEditor, TextEditOperation operation, ITextEditTarget? editTarget)
	{
		// Every editing helper funnels through here; an editor without a document cannot apply edits
		// (the caret and the default target each need it), so the missing document fails fast instead
		// of surfacing as a null-reference failure mid-edit.
		TextEditorValidation.RequireDocument(textEditor);

		ITextEditTarget target = editTarget ?? s_defaultEditTargets.GetValue(textEditor, static editor => new TextEditorEditTarget(editor));

		if (target is TextEditorEditTarget defaultTarget)
			defaultTarget.ApplySingle(operation);
		else
			target.Apply(TextEditKernel.Prepare([operation]).Edits);
	}

	/// <summary>
	/// Applies an already prepared batch through <paramref name="editTarget"/>, or directly to the editor's document when no target is supplied.
	/// </summary>
	/// <param name="textEditor">The editor whose document is edited when <paramref name="editTarget"/> is <see langword="null"/>.</param>
	/// <param name="edits">The prepared batch to apply; the caller keeps owning it (for example for its offset mapping).</param>
	/// <param name="editTarget">The host-owned target to apply to, or <see langword="null"/> to edit the editor's document directly.</param>
	internal static void ApplyPreparedEdits(TextEditor textEditor, PreparedTextEdits edits, ITextEditTarget? editTarget)
	{
		// Every editing helper funnels through here; an editor without a document cannot apply edits
		// (the caret and the default target each need it), so the missing document fails fast instead
		// of surfacing as a null-reference failure mid-edit.
		TextEditorValidation.RequireDocument(textEditor);

		ITextEditTarget target = editTarget ?? s_defaultEditTargets.GetValue(textEditor, static editor => new TextEditorEditTarget(editor));

		target.Apply(edits);
	}

	/// <summary>Plans edits against a snapshot of the editor's document and applies them as one batch.</summary>
	/// <param name="textEditor">The editor whose document is edited.</param>
	/// <param name="plan">
	/// Plans the edits against the snapshot; the edits must lie inside it and not overlap, so preparation cannot reject them.
	/// </param>
	/// <param name="editTarget">
	/// The host-owned target to apply to, or <see langword="null"/> to edit the editor's document directly. A supplied target must hold
	/// the same content as the editor's document.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when the plan held an edit; otherwise, <see langword="false"/>, and the document and its undo stack are untouched.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// The editor has no document assigned, or a supplied target's content does not match the editor's document.
	/// </exception>
	internal static bool ApplyPlan(TextEditor textEditor, Func<ITextSnapshot, IReadOnlyList<TextIncrementalEdit>> plan, ITextEditTarget? editTarget)
	{
		TextDocument document = TextEditorValidation.RequireDocument(textEditor);

		if (editTarget is not null && !string.Equals(editTarget.Text, document.Text, StringComparison.Ordinal))
			throw new InvalidOperationException("The edit target's content does not match the editor's document.");

		var snapshot = new TextDocumentSnapshot(document);
		IReadOnlyList<TextIncrementalEdit> edits = plan(snapshot);

		if (edits.Count == 0)
			return false;

		ApplyPreparedEdits(textEditor, TextEditKernel.Prepare(snapshot, edits).Edits, editTarget);

		return true;
	}

	/// <summary>Replaces a single text range and then selects the replacement or places the caret at its end.</summary>
	/// <param name="textEditor">The editor whose document is edited and whose caret is updated.</param>
	/// <param name="request">The range, its current text, the replacement and the selection outcome.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly.
	/// </param>
	/// <remarks>
	/// The range is replaced under the shared skip-identical policy used by the line-level editing helpers: when the request's replacement
	/// equals its current text the document and its undo stack are left untouched, and otherwise the range is replaced as one edit. The caret offset
	/// is computed in 64-bit arithmetic so a replacement that grows the document cannot overflow it, and clamped to the editor's current document
	/// length whether or not an edit was applied, because an identical replacement still moves the caret and a shrinking replacement must not leave
	/// it past the new document end.
	/// </remarks>
	internal static void ReplaceRangeSkippingIdentical(TextEditor textEditor, RangeReplacementRequest request, ITextEditTarget? editTarget)
	{
		if (!string.Equals(request.CurrentText, request.Replacement, StringComparison.Ordinal))
		{
			TextEditorEditingExtensions.ApplyEdit(
				textEditor,
				new TextEditRequest(new TextRange(request.StartOffset, request.CurrentText.Length), request.Replacement),
				editTarget
			);
		}

		// The final caret/selection is anchored to the editor's document: after a direct edit it is the
		// post-edit length, and after a target that does not publish to the document it is the stale
		// length the host must reconcile (see the remarks of TextEditorEditingExtensions).
		int documentLength = textEditor.Document.TextLength;
		int selectionStart = Math.Min(request.StartOffset, documentLength);

		if (request.Selection == LineReplacementSelectionMode.Select)
		{
			int selectionLength = Math.Min(request.Replacement.Length, documentLength - selectionStart);
			textEditor.Select(selectionStart, selectionLength);
		}
		else
		{
			int caretOffset = (int)Math.Min((long)selectionStart + request.Replacement.Length, documentLength);
			textEditor.Select(caretOffset, 0);
		}
	}
}
