using ICSharpCode.AvalonEdit;

namespace Nickelony.IDEKit.AvalonEdit.Editing;

/// <summary>Gives the shared editing and navigation code the WPF call that scrolls an editor to an exact offset.</summary>
/// <remarks>
/// The bindings declare the same members, so the shared code reads one set of names.
/// </remarks>
internal static class EditingSeam
{
	/// <include file="../../../../../shared/docs/EditingSeam.xml" path="doc/members/member[@name='SetScrollOffset']/*"/>
	internal static void SetScrollOffset(TextEditor editor, double horizontalOffset, double verticalOffset)
	{
		editor.ScrollToHorizontalOffset(horizontalOffset);
		editor.ScrollToVerticalOffset(verticalOffset);
	}
}
