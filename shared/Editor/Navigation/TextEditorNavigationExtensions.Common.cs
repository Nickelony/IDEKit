#if AVALONIAEDIT
using Avalonia;
using AvaloniaEdit;
#else
using System.Windows;
using ICSharpCode.AvalonEdit;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Navigation;

#else

namespace Nickelony.IDEKit.AvalonEdit.Navigation;

#endif

/// <summary>Provides helpers that map pointer input to document positions.</summary>
/// <remarks>
/// The helpers require a hosted and laid out editor because they translate
/// layout points through the text view, and they must be called on the editor's thread. They map points in the editor's coordinate space
/// with the resolver of <see cref="TextAreaExtensions.TryGetOffsetFromPoint"/>, which maps points in the text area's coordinate space.
/// </remarks>
public static partial class TextEditorNavigationExtensions
{
	/// <summary>Tries to map a point in the editor to a zero-based document offset.</summary>
	/// <param name="textEditor">The editor to inspect.</param>
	/// <param name="point">The point in the editor's coordinate space to map.</param>
	/// <param name="offset">The mapped document offset when the method returns <see langword="true"/>; otherwise, <c>0</c>.</param>
	/// <returns>
	/// <see langword="true"/> when the point maps to the document; otherwise, <see langword="false"/>. An editor without a document, or whose text
	/// view was never loaded, cannot map the point and yields <see langword="false"/> as well.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="textEditor"/> is <see langword="null"/>.</exception>
	public static bool TryGetOffsetFromPoint(this TextEditor textEditor, Point point, out int offset)
	{
		ArgumentNullException.ThrowIfNull(textEditor);
		return TextAreaExtensions.TryMapPointToOffset(textArea: textEditor.TextArea, origin: textEditor, point, overTextOnly: false, out offset);
	}

	/// <summary>Moves the caret to the document position at <paramref name="point"/>.</summary>
	/// <param name="textEditor">The editor to update.</param>
	/// <param name="point">The point, in the editor's coordinate space, to map to a caret position.</param>
	/// <returns>
	/// <see langword="true"/> when a document position is found and applied; otherwise, <see langword="false"/>. An editor without a document
	/// cannot map the point and yields <see langword="false"/> as well.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="textEditor"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The point is mapped with the same clamping as <see cref="TryGetOffsetFromPoint"/>, so a point past the end of a line places
	/// the caret at that line's end, and a point outside the rendered document is ignored. A non-empty selection is collapsed at the mapped position;
	/// a caller that wants to preserve the selection must implement that gesture itself.
	/// </remarks>
	public static bool TryMoveCaretToPoint(this TextEditor textEditor, Point point)
	{
		ArgumentNullException.ThrowIfNull(textEditor);

		if (!textEditor.TryGetOffsetFromPoint(point, out int offset))
			return false;

		textEditor.Select(offset, 0);
		return true;
	}
}
