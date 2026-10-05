using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;

namespace Nickelony.IDEKit.AvalonEdit.Navigation;

// The WPF partial of the shared TextEditorNavigationExtensions. WPF exposes the ambient mouse position, so
// this part adds the current-mouse caret move and the resolver overload it builds on.
public static partial class TextEditorNavigationExtensions
{
	/// <summary>Moves the caret to the current mouse position when the mouse is over the editor.</summary>
	/// <param name="textEditor">The editor to update.</param>
	/// <returns><see langword="true"/> when a document position is found and applied; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="textEditor"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The pointer position is only meaningful while the pointer is over the editor, so a call from another context (a command run
	/// while the pointer is elsewhere) leaves the caret unchanged instead of moving it to a stale location. While the pointer rests over the editor
	/// the check passes, so a command-driven call moves the caret to the pointer.
	/// </remarks>
	public static bool TryMoveCaretToMousePosition(this TextEditor textEditor)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		return TryMoveCaretToPointerPosition(textEditor, static editor => editor.IsMouseOver ? Mouse.GetPosition(editor) : null);
	}

	/// <summary>Moves the caret to the position resolved by <paramref name="pointerPositionResolver"/> instead of the real pointer state.</summary>
	/// <param name="textEditor">The editor to update.</param>
	/// <param name="pointerPositionResolver">The resolver that provides the pointer position.</param>
	/// <returns><see langword="true"/> when a document position is found and applied; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// <see cref="TryMoveCaretToMousePosition"/> builds on this member with a resolver that reads the mouse state. A test passes a resolver
	/// of its own, which supplies the position directly, or <see langword="null"/> when the pointer is not over the editor, so the mouse state plays
	/// no part.
	/// </remarks>
	internal static bool TryMoveCaretToPointerPosition(TextEditor textEditor, Func<TextEditor, Point?> pointerPositionResolver)
	{
		Point? pointerPosition = pointerPositionResolver(textEditor);
		return pointerPosition is Point position && textEditor.TryMoveCaretToPoint(position);
	}
}
