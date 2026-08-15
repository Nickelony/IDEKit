#if AVALONIAEDIT
using AvaloniaEdit;
#else
using ICSharpCode.AvalonEdit;
#endif
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Formatting;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Editing;
#else
namespace Nickelony.IDEKit.AvalonEdit.Editing;
#endif

/// <summary>
/// Provides whole-document formatting for an individual editor.
/// </summary>
/// <remarks>
/// <see cref="TextDocumentFormattingService"/> is the default implementation. It is stateless, so a host can
/// compose one instance per editor and substitute its own implementation through the interface. The
/// formatter runs on the editor's thread, like all editor document access.
/// </remarks>
public interface ITextDocumentFormattingService
{
	/// <summary>
	/// Formats the current document content and applies the difference as minimal edits; the built-in
	/// target applies them as one undo step.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The replacement covers only the range between the first and the last changed characters. Content
	/// outside that range is not rewritten; the caret and a selection that do not overlap the range keep
	/// their positions, and offsets after the range shift by the length difference of the replacement. The
	/// scroll position is preserved as well.
	/// </para>
	/// <para>
	/// When the changed range covers the caret or the selection, their exact positions cannot be mapped: the
	/// selection is collapsed to the end of the line with the original caret line number, and the column
	/// within that line is not preserved. If the caret line does not exist in the result, the caret is placed
	/// at the end of the resulting document.
	/// </para>
	/// <para>
	/// A supplied target must satisfy the edit-target contract described by
	/// <see cref="ITextEditTarget"/>. It is the apply route for the batch, not an alternative
	/// content source: the formatter receives the editor's current document text, and the caret, the
	/// selection, and the scroll offset are read from and restored to that same document, so a target whose
	/// content differs from it would receive a batch prepared for another text. That mismatch is rejected
	/// with an <see cref="InvalidOperationException"/> before the formatter runs.
	/// </para>
	/// </remarks>
	/// <param name="editor">The editor to update.</param>
	/// <param name="formatter">
	/// The formatter to apply. It receives the editor's current document text; returning
	/// <see langword="null"/> declines the formatting and applies no changes.
	/// </param>
	/// <param name="editTarget">
	/// The host-owned target to which the edit is applied, or <see langword="null"/> to edit the editor's document directly.
	/// A supplied target must hold the same content as the editor's document.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="editor"/> or <paramref name="formatter"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The editor has no document assigned, or a supplied target's content does not match the editor's document.
	/// </exception>
	void FormatDocument(
		TextEditor editor,
		ITextDocumentFormatter formatter,
		ITextEditTarget? editTarget = null);
}
