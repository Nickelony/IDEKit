using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.Core.Notifications;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures;

/// <summary>Gives the shared feature popup its Avalonia calls: the popup chrome, its placement and the events that close it.</summary>
/// <remarks>
/// <para>
/// An Avalonia popup outside the logical tree takes no control themes, so a code block of the content, which is an editor,
/// would render as an empty frame; the popup therefore takes the text area as its logical parent. Its window is opaque, so the frame draws no shadow
/// that would need a transparent margin. The toolkit closes the popup by itself when the text area leaves the visual tree, which the shared presenter
/// counts as a close on its own. The bindings declare the same members, so the shared presenter reads one set of names.
/// </para>
/// <para>
/// A text area that sits in a popup of its own has a popup root as its top level, which is never active, so the window that counts
/// is the one that hosts the popup chain.
/// </para>
/// </remarks>
internal static class PopupSeam
{
	/// <include file="../../../../../shared/docs/PopupSeam.xml" path="doc/members/member[@name='CreatePopup']/*"/>
	internal static Popup CreatePopup(TextArea textArea, Border frame)
	{
		var popup = new Popup
		{
			PlacementTarget = textArea,
			Placement = PlacementMode.BottomEdgeAlignedLeft,
			IsLightDismissEnabled = false,
			Child = frame,
		};

		((ISetLogicalParent)popup).SetParent(textArea);
		return popup;
	}

	/// <include file="../../../../../shared/docs/PopupSeam.xml" path="doc/members/member[@name='ReleasePopup']/*"/>
	internal static void ReleasePopup(Popup popup)
	{
		((ISetLogicalParent)popup).SetParent(null);
		popup.PlacementTarget = null;
	}

	/// <include file="../../../../../shared/docs/PopupSeam.xml" path="doc/members/member[@name='SetAnchor']/*"/>
	internal static void SetAnchor(Popup popup, Rect anchor) => popup.PlacementRect = anchor;

	/// <include file="../../../../../shared/docs/PopupSeam.xml" path="doc/members/member[@name='IsWindowInactive']/*"/>
	internal static bool IsWindowInactive(TextArea textArea) => OwningWindow(textArea) is { IsActive: false };

	/// <include file="../../../../../shared/docs/PopupSeam.xml" path="doc/members/member[@name='SubscribeClosing']/*"/>
	internal static IDisposable SubscribeClosing(TextArea textArea, Action close)
	{
		WindowBase? window = OwningWindow(textArea);

		void keyHandler(object? sender, KeyEventArgs e)
		{
			if (e.Key != Key.Escape || e.Handled)
				return;

			e.Handled = true;
			close();
		}

		void focusHandler(object? sender, AvaloniaPropertyChangedEventArgs e)
		{
			if (e.Property == InputElement.IsKeyboardFocusWithinProperty && e.NewValue is false)
				close();
		}

		void deactivatedHandler(object? sender, EventArgs e) => close();

		textArea.AddHandler(InputElement.KeyDownEvent, keyHandler, RoutingStrategies.Tunnel);
		textArea.PropertyChanged += focusHandler;

		if (window is not null)
			window.Deactivated += deactivatedHandler;

		return new ActionSubscription(() =>
		{
			textArea.RemoveHandler(InputElement.KeyDownEvent, keyHandler);
			textArea.PropertyChanged -= focusHandler;

			if (window is not null)
				window.Deactivated -= deactivatedHandler;
		});
	}

	// The window whose activation counts for the text area: its own top level, or, for a text area inside a popup, the top level that hosts
	// the popup, followed up the chain of nested popups.
	private static WindowBase? OwningWindow(TextArea textArea)
	{
		var topLevel = TopLevel.GetTopLevel(textArea);

		// A popup root takes the popup that opened it as its logical parent, and that popup sits in the tree of the top level that hosts it.
		while (topLevel is PopupRoot { Parent: Visual popup } && TopLevel.GetTopLevel(popup) is { } hostTopLevel && hostTopLevel != topLevel)
			topLevel = hostTopLevel;

		return topLevel as WindowBase;
	}
}
