using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Editing;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;

/// <summary>Applies placement, visibility, and focus handling to a code-action menu through the WPF members.</summary>
/// <remarks>
/// The menu presenter is shared binding source, and the menu model is the one spot where the engines disagree on shape: WPF places a context menu
/// at a relative point and closes it through the <c>StaysOpen</c> flag, while Avalonia anchors a top-left point through the shared popup properties
/// and light-dismisses it. This type is the WPF half of that seam. The bindings declare the same members, so the shared presenter reads one name.
/// </remarks>
internal static class CodeActionMenuSeam
{
	/// <include file="../../../../../shared/docs/CodeActionMenuSeam.xml" path="doc/members/member[@name='ApplyPlacement']/*"/>
	internal static void ApplyPlacement(ContextMenu menu, UIElement placementTarget, Point anchor)
	{
		menu.PlacementTarget = placementTarget;
		menu.Placement = PlacementMode.RelativePoint;
		menu.HorizontalOffset = anchor.X;
		menu.VerticalOffset = anchor.Y;
		menu.StaysOpen = false;
	}

	/// <summary>Opens the menu.</summary>
	/// <param name="menu">The menu to open.</param>
	/// <param name="placementTarget">The element the menu is placed against; the WPF menu is already positioned, so the argument is unused.</param>
	/// <remarks>
	/// Placement must already be applied by <see cref="ApplyPlacement"/>.
	/// </remarks>
	internal static void Open(ContextMenu menu, UIElement placementTarget) => menu.IsOpen = true;

	/// <include file="../../../../../shared/docs/CodeActionMenuSeam.xml" path="doc/members/member[@name='FocusFirstItem']/*"/>
	/// <remarks>
	/// The WPF context menu focuses its first item when it opens, so the member has nothing to do.
	/// </remarks>
	internal static void FocusFirstItem(ContextMenu menu) { }

	/// <include file="../../../../../shared/docs/CodeActionMenuSeam.xml" path="doc/members/member[@name='Close']/*"/>
	internal static void Close(ContextMenu menu) => menu.IsOpen = false;

	/// <summary>Returns the element that currently holds keyboard focus, if any.</summary>
	/// <param name="textArea">The text area the menu belongs to; the WPF focus source is process-wide, so the argument is unused.</param>
	/// <returns>The focused element, or <see langword="null"/> when nothing is focused.</returns>
	internal static IInputElement? GetFocusedElement(TextArea textArea) => Keyboard.FocusedElement;

	/// <include file="../../../../../shared/docs/CodeActionMenuSeam.xml" path="doc/members/member[@name='CanOpenMenu']/*"/>
	internal static bool CanOpenMenu(TextArea textArea) => PresentationSource.FromVisual(textArea) is not null;

	/// <include file="../../../../../shared/docs/CodeActionMenuSeam.xml" path="doc/members/member[@name='TryTranslatePoint']/*"/>
	internal static bool TryTranslatePoint(UIElement source, Point point, UIElement target, out Point translated)
	{
		translated = source.TranslatePoint(point, target);
		return true;
	}

	/// <include file="../../../../../shared/docs/CodeActionMenuSeam.xml" path="doc/members/member[@name='GetTextViewOrigin']/*"/>
	internal static Point GetTextViewOrigin(TextArea textArea) => textArea.TextView.TranslatePoint(new Point(0.0, 0.0), textArea);

	/// <include file="../../../../../shared/docs/CodeActionMenuSeam.xml" path="doc/members/member[@name='TryFocus']/*"/>
	internal static bool TryFocus(object? element)
	{
		if (element is UIElement uiElement && uiElement.IsVisible && uiElement.IsEnabled && uiElement.Focusable)
			return ReferenceEquals(Keyboard.Focus(uiElement), uiElement);

		return false;
	}
}
