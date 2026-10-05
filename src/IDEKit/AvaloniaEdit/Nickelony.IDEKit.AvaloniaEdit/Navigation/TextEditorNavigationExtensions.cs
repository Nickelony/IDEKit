using Avalonia;
using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;

namespace Nickelony.IDEKit.AvaloniaEdit.Navigation;

// The Avalonia partial of the shared TextEditorNavigationExtensions. Avalonia exposes no ambient pointer position, so the
// current-pointer caret move takes the pointer event that carries the position.
public static partial class TextEditorNavigationExtensions
{
	/// <summary>Moves the caret to the position of a pointer event when the pointer is over the text view.</summary>
	/// <param name="textEditor">The editor to update.</param>
	/// <param name="e">The pointer event whose position is mapped to a document position.</param>
	/// <returns>
	/// <see langword="true"/> when a document position is found and applied; otherwise, <see langword="false"/>, which includes a pointer
	/// outside the text view, such as over a margin or a scroll bar, and a pointer past the last line.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="textEditor"/> or <paramref name="e"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The event position is translated into the text view of the editor and checked against its bounds before it maps to a document position,
	/// so a call from a handler on an ancestor of the editor never moves the caret for a press elsewhere. A non-empty selection is collapsed
	/// at the mapped position, as <see cref="TryMoveCaretToPoint"/> does.
	/// </remarks>
	public static bool TryMoveCaretToMousePosition(this TextEditor textEditor, PointerEventArgs e)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		ArgumentNullException.ThrowIfNull(e);

		TextView textView = textEditor.TextArea.TextView;

		if (!new Rect(textView.Bounds.Size).Contains(e.GetPosition(textView)))
			return false;

		return textEditor.TryMoveCaretToPoint(e.GetPosition(textEditor));
	}
}
