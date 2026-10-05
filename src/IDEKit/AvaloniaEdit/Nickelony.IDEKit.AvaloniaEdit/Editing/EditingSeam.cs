using Avalonia;
using Avalonia.Controls.Primitives;
using AvaloniaEdit;

namespace Nickelony.IDEKit.AvaloniaEdit.Editing;

/// <summary>Gives the shared editing and navigation code the Avalonia call that scrolls an editor to an exact offset.</summary>
/// <remarks>
/// The bindings declare the same members, so the shared code reads one set of names.
/// </remarks>
internal static class EditingSeam
{
	/// <include file="../../../../../shared/docs/EditingSeam.xml" path="doc/members/member[@name='SetScrollOffset']/*"/>
	/// <remarks>
	/// The editor's own <c>ScrollToVerticalOffset</c> and <c>ScrollToHorizontalOffset</c> only apply its template and scroll nothing, so the offset
	/// goes to the text view, which is the scrollable the editor's scroll viewer shows.
	/// </remarks>
	internal static void SetScrollOffset(TextEditor editor, double horizontalOffset, double verticalOffset) =>
		((IScrollable)editor.TextArea.TextView).Offset = new Vector(horizontalOffset, verticalOffset);
}
