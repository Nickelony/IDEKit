using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Formatting;
#if AVALONIAEDIT
using AvaloniaEdit;
#else
using ICSharpCode.AvalonEdit;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Formatting;

#else

namespace Nickelony.IDEKit.AvalonEdit.Formatting;

#endif

/// <summary>Provides whole-document formatting for an individual editor.</summary>
/// <remarks>
/// <see cref="DocumentFormatterService"/> is the default implementation. It is stateless, so a host can compose one instance per editor
/// and substitute its own implementation through the interface. The formatter runs on the editor's thread, like all editor document access.
/// </remarks>
public interface IDocumentFormatterService
{
	/// <summary>
	/// Formats the current document content and applies the difference as minimal edits; the built-in target applies them as one undo step.
	/// </summary>
	/// <param name="editor">The editor to update.</param>
	/// <param name="formatter">
	/// The formatter to apply. It receives the editor's current document text; returning <see langword="null"/> declines the formatting and applies
	/// no changes.
	/// </param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly. A supplied target
	/// must hold the same content as the editor's document.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="editor"/> or <paramref name="formatter"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">
	/// The editor has no document assigned, or a supplied target's content does not match the editor's document.
	/// </exception>
	/// <remarks>
	/// Only the range between the first and last changed characters is rewritten, so a caret or selection outside that range
	/// stays on the same text, offsets after the range shift by the length difference of the replacement, and the scroll position is preserved.
	/// When the changed range covers the caret or the selection, the selection collapses to the end of the line with the original
	/// caret line number (the column is not preserved), or to the end of the document when that line is absent from the result. A supplied target
	/// is the apply route for the batch, not an alternative content source, so a target whose content differs from the editor's document is rejected
	/// with an <see cref="InvalidOperationException"/> before the formatter runs.
	/// </remarks>
	void FormatDocument(TextEditor editor, IDocumentFormatter formatter, ITextEditTarget? editTarget = null);

	/// <summary>
	/// Applies a prepared edit batch to the editor's current document and keeps the caret, the selection, and the scroll position where the edits
	/// leave them intact.
	/// </summary>
	/// <param name="editor">The editor to update.</param>
	/// <param name="edits">The prepared batch, whose offsets belong to the editor's current document text.</param>
	/// <param name="editTarget">
	/// The host-owned target to which the batch is applied, or <see langword="null"/> to edit the editor's document directly. A supplied target
	/// must hold the same content as the editor's document.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="editor"/> or <paramref name="edits"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">
	/// The editor has no document assigned, or a supplied target's content does not match the editor's document.
	/// </exception>
	/// <remarks>
	/// This is the apply half of <see cref="FormatDocument"/>, for a caller that produced the batch itself, such as one built
	/// from a formatting provider's edits. The caret and selection follow the same rule as <see cref="FormatDocument"/>.
	/// </remarks>
	void ApplyEdits(TextEditor editor, PreparedTextEdits edits, ITextEditTarget? editTarget = null);
}
