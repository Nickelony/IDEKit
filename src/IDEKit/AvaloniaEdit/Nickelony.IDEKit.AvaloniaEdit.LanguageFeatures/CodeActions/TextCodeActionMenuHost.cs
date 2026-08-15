using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using AvaloniaEdit.Editing;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;

/// <summary>
/// Applies placement, visibility, and focus handling to a code-action menu through the Avalonia members.
/// </summary>
/// <remarks>
/// The menu presenter is shared binding source, and the menu model is the one spot where the two engines
/// disagree on shape: WPF places a context menu at a relative point and keeps it open with a flag, while
/// Avalonia anchors a top-left point through the shared popup properties and light-dismisses it. This type is
/// the Avalonia half of that seam. Both bindings declare the same members, so the shared presenter reads one
/// name.
/// </remarks>
internal static class TextCodeActionMenuHost
{
	/// <include file="../../../../../shared/docs/TextCodeActionMenuHost.xml" path="doc/members/member[@name='ApplyPlacement']/*"/>
	public static void ApplyPlacement(ContextMenu menu, Control placementTarget, Point anchor)
	{
		menu.PlacementTarget = placementTarget;
		menu.Placement = PlacementMode.AnchorAndGravity;
		menu.PlacementAnchor = PopupAnchor.TopLeft;
		menu.PlacementGravity = PopupGravity.BottomRight;
		menu.HorizontalOffset = anchor.X;
		menu.VerticalOffset = anchor.Y;
	}

	/// <summary>
	/// Opens the menu against the placement target.
	/// </summary>
	/// <remarks>
	/// The menu is anchored explicitly: it is never attached to a control's context-menu property, so the
	/// parameterless overload would have no attached control to fall back to.
	/// </remarks>
	/// <param name="menu">The menu to open.</param>
	/// <param name="placementTarget">The element the menu is placed against.</param>
	public static void Open(ContextMenu menu, Control placementTarget)
		=> menu.Open(placementTarget);

	/// <include file="../../../../../shared/docs/TextCodeActionMenuHost.xml" path="doc/members/member[@name='Close']/*"/>
	public static void Close(ContextMenu menu)
		=> menu.Close();

	/// <summary>
	/// Gets the element that currently holds keyboard focus in the text area's top level, if any.
	/// </summary>
	/// <param name="textArea">The text area the menu belongs to.</param>
	/// <returns>The focused element, or <see langword="null"/> when nothing is focused.</returns>
	public static IInputElement? GetFocusedElement(TextArea textArea)
		=> TopLevel.GetTopLevel(textArea)?.FocusManager?.GetFocusedElement();

	/// <include file="../../../../../shared/docs/TextCodeActionMenuHost.xml" path="doc/members/member[@name='TryFocus']/*"/>
	public static bool TryFocus(object? element)
	{
		if (element is InputElement inputElement && inputElement.IsVisible && inputElement.IsEffectivelyEnabled && inputElement.Focusable)
		{
			inputElement.Focus();
			return true;
		}

		return false;
	}
}
