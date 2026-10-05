using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit.Editing;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;

/// <summary>Applies placement, visibility, and focus handling to a code-action menu through the Avalonia members.</summary>
/// <remarks>
/// The menu model is the spot where the engines disagree on shape: Avalonia anchors a top-left point through the shared popup properties
/// and light-dismisses the menu. The bindings declare the same members, so the shared presenter reads one name.
/// </remarks>
internal static class CodeActionMenuSeam
{
	/// <include file="../../../../../shared/docs/CodeActionMenuSeam.xml" path="doc/members/member[@name='ApplyPlacement']/*"/>
	internal static void ApplyPlacement(ContextMenu menu, Control placementTarget, Point anchor)
	{
		menu.PlacementTarget = placementTarget;
		menu.Placement = PlacementMode.AnchorAndGravity;
		menu.PlacementAnchor = PopupAnchor.TopLeft;
		menu.PlacementGravity = PopupGravity.BottomRight;
		menu.HorizontalOffset = anchor.X;
		menu.VerticalOffset = anchor.Y;
	}

	/// <summary>Opens the menu against the placement target.</summary>
	/// <param name="menu">The menu to open.</param>
	/// <param name="placementTarget">The element the menu is placed against.</param>
	/// <remarks>
	/// The menu is anchored explicitly: it is never attached to a control's context-menu property, so the parameterless overload
	/// would have no attached control to fall back to.
	/// </remarks>
	internal static void Open(ContextMenu menu, Control placementTarget) => menu.Open(placementTarget);

	/// <include file="../../../../../shared/docs/CodeActionMenuSeam.xml" path="doc/members/member[@name='FocusFirstItem']/*"/>
	/// <remarks>
	/// The popup gives the focus to the menu itself once it shows, and the menu applies no item on Enter until an item holds the focus, so the first
	/// item takes the focus after that.
	/// </remarks>
	internal static void FocusFirstItem(ContextMenu menu)
	{
		Dispatcher.UIThread.Post(
			() =>
			{
				if (menu.IsOpen && menu.Items.Count > 0 && menu.Items[0] is MenuItem first)
					first.Focus(NavigationMethod.Directional);
			},

			DispatcherPriority.Loaded
		);
	}

	/// <include file="../../../../../shared/docs/CodeActionMenuSeam.xml" path="doc/members/member[@name='Close']/*"/>
	internal static void Close(ContextMenu menu) => menu.Close();

	/// <summary>Returns the element that currently holds keyboard focus in the text area's top level, if any.</summary>
	/// <param name="textArea">The text area the menu belongs to.</param>
	/// <returns>The focused element, or <see langword="null"/> when nothing is focused.</returns>
	internal static IInputElement? GetFocusedElement(TextArea textArea) => TopLevel.GetTopLevel(textArea)?.FocusManager?.GetFocusedElement();

	/// <include file="../../../../../shared/docs/CodeActionMenuSeam.xml" path="doc/members/member[@name='CanOpenMenu']/*"/>
	internal static bool CanOpenMenu(TextArea textArea) => TopLevel.GetTopLevel(textArea) is not null;

	/// <include file="../../../../../shared/docs/CodeActionMenuSeam.xml" path="doc/members/member[@name='TryTranslatePoint']/*"/>
	/// <remarks>
	/// A failed translation (the source is not connected to the target) means there is no anchor to open the menu at.
	/// </remarks>
	internal static bool TryTranslatePoint(Visual source, Point point, Visual target, out Point translated)
	{
		Point? result = source.TranslatePoint(point, target);
		translated = result.GetValueOrDefault();
		return result.HasValue;
	}

	/// <include file="../../../../../shared/docs/CodeActionMenuSeam.xml" path="doc/members/member[@name='GetTextViewOrigin']/*"/>
	internal static Point GetTextViewOrigin(TextArea textArea) => textArea.TextView.TranslatePoint(new Point(0.0, 0.0), textArea) ?? default;

	/// <include file="../../../../../shared/docs/CodeActionMenuSeam.xml" path="doc/members/member[@name='TryFocus']/*"/>
	internal static bool TryFocus(object? element)
	{
		if (element is InputElement inputElement && inputElement.IsVisible && inputElement.IsEffectivelyEnabled && inputElement.Focusable)
			return inputElement.Focus();

		return false;
	}
}
