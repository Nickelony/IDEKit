#if AVALONIAEDIT
using Avalonia;
using AvaloniaEdit;
#else
using ICSharpCode.AvalonEdit;
using System.Windows;
using System.Windows.Input;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Navigation;
#else
namespace Nickelony.IDEKit.AvalonEdit.Navigation;
#endif

/// <summary>
/// Provides helpers that map pointer input to document positions.
/// </summary>
/// <remarks>
/// The helpers require a hosted and laid out editor because they translate layout points
/// through the text view, and they must be called on the editor's thread. They extend
/// <see cref="TextEditor"/> rather than <c>TextArea</c> because mapping a point requires the editor's
/// point-to-position translation, which the editor composes with its text view.
/// </remarks>
public static class TextEditorNavigationOperations
{
	/// <summary>
	/// Tries to map a point in the editor to a zero-based document offset.
	/// </summary>
	/// <param name="textEditor">The editor to inspect.</param>
	/// <param name="point">The point in the editor's coordinate space to map.</param>
	/// <param name="offset">
	/// The mapped document offset when the method returns <see langword="true"/>; otherwise, <c>0</c>.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when the point maps to the document; otherwise, <see langword="false"/>.
	/// An editor without a document cannot map the point and yields <see langword="false"/> as well.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="textEditor"/> is <see langword="null"/>.</exception>
	public static bool TryGetOffsetFromPoint(this TextEditor textEditor, Point point, out int offset)
	{
		ArgumentNullException.ThrowIfNull(textEditor);

		offset = 0;

		// A view without a document has nothing to map against; the documented Try contract reports that as
		// a failed mapping instead of a null-reference failure.
		if (textEditor.Document is null)
			return false;

		// The editor resolves the point through the text view and reports a point outside the
		// rendered document as null.
		TextViewPosition? position = textEditor.GetPositionFromPoint(point);

		if (position is null)
			return false;

		// The document clamps the column to the line's bounds, so the offset is always within the document.
		offset = textEditor.Document.GetOffset(position.Value.Line, position.Value.Column);
		return true;
	}

#if !AVALONIAEDIT
	/// <summary>
	/// Moves the caret to the current mouse position when the mouse is over the editor.
	/// </summary>
	/// <remarks>
	/// The pointer position is only meaningful while the pointer is over the editor, so a call from another
	/// context (for example a command invoked while the pointer is elsewhere) leaves the caret unchanged
	/// instead of moving it to a stale pointer location. While the pointer rests over the editor this check
	/// passes, so a command-driven call moves the caret to wherever the pointer happens to be.
	/// </remarks>
	/// <param name="textEditor">The editor to update.</param>
	/// <returns>
	/// <see langword="true"/> when a document position is found and applied; otherwise, <see langword="false"/>.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="textEditor"/> is <see langword="null"/>.</exception>
	public static bool TryMoveCaretToMousePosition(this TextEditor textEditor)
	{
		ArgumentNullException.ThrowIfNull(textEditor);

		return TryMoveCaretToMousePosition(
			textEditor,
			static editor => editor.IsMouseOver ? Mouse.GetPosition(editor) : null);
	}

	/// <summary>
	/// Moves the caret to the position resolved by <paramref name="pointerPositionResolver"/> instead of
	/// the real mouse state.
	/// </summary>
	/// <remarks>
	/// A test seam: a headless test cannot rest the pointer over the editor, so it supplies a resolver
	/// that returns a deterministic position, or <see langword="null"/> for "the pointer is not over
	/// the editor". The resolver replaces the editor's mouse state entirely.
	/// </remarks>
	/// <param name="textEditor">The editor to update.</param>
	/// <param name="pointerPositionResolver">The resolver that provides the pointer position.</param>
	/// <returns>
	/// <see langword="true"/> when a document position is found and applied; otherwise, <see langword="false"/>.
	/// </returns>
	internal static bool TryMoveCaretToMousePosition(TextEditor textEditor, Func<TextEditor, Point?> pointerPositionResolver)
	{
		Point? pointerPosition = pointerPositionResolver(textEditor);
		return pointerPosition is Point position && textEditor.TryMoveCaretToPoint(position);
	}
#endif

	/// <summary>
	/// Moves the caret to the document position at <paramref name="point"/>.
	/// </summary>
	/// <remarks>
	/// The point is mapped with the same clamping as <see cref="TryGetOffsetFromPoint"/>, so a point past the end
	/// of a line places the caret at that line's end, and a point outside the rendered document is ignored.
	/// A non-empty selection is collapsed at the mapped position, matching the plain-click behavior of common
	/// editors; a caller that wants to preserve the selection must implement that gesture itself.
	/// </remarks>
	/// <param name="textEditor">The editor to update.</param>
	/// <param name="point">The point, in the editor's coordinate space, to map to a caret position.</param>
	/// <returns>
	/// <see langword="true"/> when a document position is found and applied; otherwise, <see langword="false"/>.
	/// An editor without a document cannot map the point and yields <see langword="false"/> as well.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="textEditor"/> is <see langword="null"/>.</exception>
	public static bool TryMoveCaretToPoint(this TextEditor textEditor, Point point)
	{
		ArgumentNullException.ThrowIfNull(textEditor);

		if (!textEditor.TryGetOffsetFromPoint(point, out int offset))
			return false;

		textEditor.Select(offset, 0);
		return true;
	}
}
