using ICSharpCode.AvalonEdit.Editing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;

/// <summary>
/// Applies placement, visibility, and focus handling to a code-action menu through the WPF members.
/// </summary>
/// <remarks>
/// The menu presenter is shared binding source, and the menu model is the one spot where the two engines
/// disagree on shape: WPF places a context menu at a relative point and keeps it open with a flag, while
/// Avalonia anchors a top-left point through the shared popup properties and light-dismisses it. This type is
/// the WPF half of that seam. Both bindings declare the same members, so the shared presenter reads one name.
/// </remarks>
internal static class TextCodeActionMenuHost
{
	/// <include file="../../../../../shared/docs/TextCodeActionMenuHost.xml" path="doc/members/member[@name='ApplyPlacement']/*"/>
	public static void ApplyPlacement(ContextMenu menu, UIElement placementTarget, Point anchor)
	{
		menu.PlacementTarget = placementTarget;
		menu.Placement = PlacementMode.RelativePoint;
		menu.HorizontalOffset = anchor.X;
		menu.VerticalOffset = anchor.Y;
		menu.StaysOpen = false;
	}

	/// <summary>
	/// Opens the menu against the placement target.
	/// </summary>
	/// <param name="menu">The menu to open.</param>
	/// <param name="placementTarget">The element the menu is placed against.</param>
	public static void Open(ContextMenu menu, UIElement placementTarget)
		=> menu.IsOpen = true;

	/// <include file="../../../../../shared/docs/TextCodeActionMenuHost.xml" path="doc/members/member[@name='Close']/*"/>
	public static void Close(ContextMenu menu)
		=> menu.IsOpen = false;

	/// <summary>
	/// Gets the element that currently holds keyboard focus, if any.
	/// </summary>
	/// <param name="textArea">
	/// The text area the menu belongs to; the WPF focus source is process-wide, so the argument is unused.
	/// </param>
	/// <returns>The focused element, or <see langword="null"/> when nothing is focused.</returns>
	public static IInputElement? GetFocusedElement(TextArea textArea)
		=> Keyboard.FocusedElement;

	/// <include file="../../../../../shared/docs/TextCodeActionMenuHost.xml" path="doc/members/member[@name='TryFocus']/*"/>
	public static bool TryFocus(object? element)
	{
		if (element is UIElement uiElement && uiElement.IsVisible && uiElement.IsEnabled && uiElement.Focusable)
		{
			Keyboard.Focus(uiElement);
			return true;
		}

		return false;
	}
}
