#if AVALONIAEDIT
using AvaloniaEdit;
#else
using ICSharpCode.AvalonEdit;
#endif
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
using System.Runtime.CompilerServices;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Editing;
#else
namespace Nickelony.IDEKit.AvalonEdit.Editing;
#endif

/// <summary>
/// Provides programmatic text insertion and replacement for the editor's <see cref="TextEditor"/>.
/// </summary>
/// <remarks>
/// <para>
/// When an edit target is supplied, the edit is applied to that target instead of the editor's document;
/// the target must satisfy the contract described by <see cref="ITextEditTarget"/>. The helpers must be
/// called on the editor's thread, like all editor document access, and editing is synchronous: the
/// editor's caret is updated separately, and a host that tracks content changes updates its own state
/// after the call.
/// </para>
/// <para>
/// The caret is always clamped to the current length of the editor's document, which does not reflect an edit
/// target's post-edit content until the host applies the change. If a target violates the contract above by
/// not updating the editor's document before returning, the requested caret offset is still applied but
/// clamped to that stale length, and the host must set the final caret after publishing its change.
/// </para>
/// </remarks>
public static class TextEditorEditOperations
{
	// The default target is cached per editor: this path runs on every keystroke (typing, auto-closing,
	// backspace), and an uncached target would allocate per call. The weak table keeps the target alive
	// exactly as long as its editor, and the target holds no event subscriptions of its own.
	private static readonly ConditionalWeakTable<TextEditor, TextEditorEditTarget> s_defaultEditTargets = new();

	/// <summary>
	/// Applies a single text edit described by <paramref name="request"/> and places the caret at the
	/// request's caret offset, or just after the inserted text when no caret offset is set.
	/// </summary>
	/// <remarks>
	/// With no edit target, the default target records the edit as one undo step. A request
	/// with a zero <see cref="TextEditRequest.Length"/> inserts <see cref="TextEditRequest.NewText"/> at
	/// <see cref="TextEditRequest.StartOffset"/>.
	/// </remarks>
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
	public static void ApplyEdit(
		this TextEditor textEditor,
		TextEditRequest request,
		ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(textEditor);

		ApplyOperation(
			textEditor,
			new TextEditOperation(request.StartOffset, request.StartOffset + request.Length, request.NewText, 0),
			editTarget);

		// The default caret offset is computed in 64-bit arithmetic so a large insert cannot overflow.
		long requestedCaretOffset = request.CaretOffsetAfterEdit ?? ((long)request.StartOffset + request.NewText.Length);

		textEditor.CaretOffset = (int)Math.Clamp(requestedCaretOffset, 0L, textEditor.Document.TextLength);
	}

	/// <summary>
	/// Inserts <paramref name="newText"/> at <paramref name="insertOffset"/> as a single edit operation
	/// and places the caret just after the inserted text.
	/// </summary>
	/// <remarks>
	/// This is the narrow convenience overload for the common insertion; use
	/// <see cref="ApplyEdit(TextEditor, TextEditRequest, ITextEditTarget?)"/> to replace a range or to set
	/// the caret explicitly.
	/// </remarks>
	/// <param name="textEditor">
	/// The editor whose caret is updated and whose document is edited when <paramref name="editTarget"/> is <see langword="null"/>.
	/// </param>
	/// <param name="insertOffset">The zero-based offset at which to insert the text.</param>
	/// <param name="newText">The text to insert.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="textEditor"/> or <paramref name="newText"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="insertOffset"/> is negative, or the edit target (or the editor's document when no
	/// target is supplied) rejects the requested range.
	/// </exception>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	public static void InsertText(
		this TextEditor textEditor,
		int insertOffset,
		string newText,
		ITextEditTarget? editTarget = null)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(newText);
		ArgumentOutOfRangeException.ThrowIfNegative(insertOffset);

		ApplyEdit(textEditor, new TextEditRequest(insertOffset, 0, newText), editTarget);
	}

	/// <summary>
	/// Applies a single <paramref name="operation"/> through <paramref name="editTarget"/>, or directly
	/// to the editor's document when no target is supplied.
	/// </summary>
	/// <remarks>
	/// This is the single-operation fast path the editing helpers use for the hot paths (typing,
	/// auto-closing, and backspace). For the built-in target it bypasses the
	/// <see cref="PreparedTextEdits"/> carrier (and its defensive copy); a host target keeps the
	/// documented batch contract, so the one operation travels as a one-operation batch.
	/// </remarks>
	/// <param name="textEditor">
	/// The editor whose document is edited when <paramref name="editTarget"/> is <see langword="null"/>.
	/// </param>
	/// <param name="operation">The validated operation to apply.</param>
	/// <param name="editTarget">
	/// The host-owned target to apply to, or <see langword="null"/> to edit the editor's document directly.
	/// </param>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	internal static void ApplyOperation(
		TextEditor textEditor,
		TextEditOperation operation,
		ITextEditTarget? editTarget)
	{
		ArgumentNullException.ThrowIfNull(operation);

		// Every editing helper funnels through here; an editor without a document cannot apply edits
		// (the caret and the default target both need it), so the missing document fails fast instead
		// of surfacing as a null-reference failure mid-edit.
		if (textEditor.Document is null)
			throw new InvalidOperationException("The editor has no document assigned.");

		ITextEditTarget target = editTarget
			?? s_defaultEditTargets.GetValue(textEditor, static editor => new TextEditorEditTarget(editor));

		if (target is TextEditorEditTarget defaultTarget)
			defaultTarget.ApplySingle(operation);
		else
			target.Apply(TextEditKernel.Prepare([operation]).Edits);
	}

	/// <summary>
	/// Applies an already prepared batch through <paramref name="editTarget"/>, or directly to the
	/// editor's document when no target is supplied.
	/// </summary>
	/// <param name="textEditor">
	/// The editor whose document is edited when <paramref name="editTarget"/> is <see langword="null"/>.
	/// </param>
	/// <param name="edits">The prepared batch to apply; the caller keeps owning it (for example for its
	/// offset mapping).</param>
	/// <param name="editTarget">
	/// The host-owned target to apply to, or <see langword="null"/> to edit the editor's document directly.
	/// </param>
	internal static void ApplyOperations(
		TextEditor textEditor,
		PreparedTextEdits edits,
		ITextEditTarget? editTarget)
	{
		// Every editing helper funnels through here; an editor without a document cannot apply edits
		// (the caret and the default target both need it), so the missing document fails fast instead
		// of surfacing as a null-reference failure mid-edit.
		if (textEditor.Document is null)
			throw new InvalidOperationException("The editor has no document assigned.");

		ITextEditTarget target = editTarget
			?? s_defaultEditTargets.GetValue(textEditor, static editor => new TextEditorEditTarget(editor));

		target.Apply(edits);
	}

	/// <summary>
	/// Replaces a single text range under the shared skip-identical policy used by the line-level
	/// editing helpers: when <paramref name="replacement"/> equals <paramref name="currentText"/> the
	/// document and its undo stack are left untouched, and otherwise the range is replaced as one edit.
	/// The caret is then placed at the end of the replacement text, clamped to the editor's current
	/// document length.
	/// </summary>
	/// <remarks>
	/// The caret offset is computed in 64-bit arithmetic, so a replacement that grows the document
	/// cannot overflow it. The clamp runs whether or not an edit was applied, because an identical
	/// replacement still moves the caret and a shrinking replacement must not leave it past the new
	/// document end.
	/// </remarks>
	/// <param name="textEditor">The editor whose document is edited and whose caret is updated.</param>
	/// <param name="startOffset">The zero-based start offset of the replaced range.</param>
	/// <param name="currentText">The range's current text, compared against <paramref name="replacement"/>.</param>
	/// <param name="replacement">The replacement text.</param>
	/// <param name="selectReplacement">
	/// Whether the replacement stays selected instead of collapsing to the caret.
	/// </param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the
	/// editor's document directly.
	/// </param>
	internal static void ReplaceRangeSkippingIdentical(
		TextEditor textEditor,
		int startOffset,
		string currentText,
		string replacement,
		bool selectReplacement,
		ITextEditTarget? editTarget)
	{
		if (!string.Equals(currentText, replacement, StringComparison.Ordinal))
			ApplyEdit(textEditor, new TextEditRequest(startOffset, currentText.Length, replacement), editTarget);

		// The final caret/selection is anchored to the editor's document: after a direct edit it is the
		// post-edit length, and after a target that does not publish to the document it is the stale
		// length the host must reconcile (see the type remarks).
		int documentLength = textEditor.Document.TextLength;
		int selectionStart = Math.Min(startOffset, documentLength);

		if (selectReplacement)
		{
			int selectionLength = Math.Min(replacement.Length, documentLength - selectionStart);
			textEditor.Select(selectionStart, selectionLength);
		}
		else
		{
			int caretOffset = (int)Math.Min((long)selectionStart + replacement.Length, documentLength);
			textEditor.Select(caretOffset, 0);
		}
	}
}
