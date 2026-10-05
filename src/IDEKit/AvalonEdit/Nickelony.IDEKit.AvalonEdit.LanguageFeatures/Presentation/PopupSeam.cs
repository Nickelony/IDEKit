using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Effects;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.Core.Notifications;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures;

/// <summary>Gives the shared feature popup its WPF calls: the popup chrome, its placement and the events that close it.</summary>
/// <remarks>
/// <para>
/// A WPF popup is a topmost window of its own, so it draws a drop shadow inside a transparent margin, stays open until it is told to close,
/// and learns of the editor window's deactivation only through the window's event. The bindings declare the same members, so the shared presenter
/// reads one set of names.
/// </para>
/// <para>
/// A text area that no WPF <see cref="Window"/> owns, such as one in an <c>ElementHost</c>, reads the activation of the Win32 top-level window
/// that hosts it through <see cref="PopupWindowInterop"/>. A text area outside any window or handle is never inactive.
/// </para>
/// </remarks>
internal static class PopupSeam
{
	/// <include file="../../../../../shared/docs/PopupSeam.xml" path="doc/members/member[@name='CreatePopup']/*"/>
	internal static Popup CreatePopup(TextArea textArea, Border frame)
	{
		// The margin leaves room inside the transparent popup for the shadow the frame casts down and to the right.
		frame.Margin = new Thickness(left: 0, top: 2, right: 8, bottom: 8);

		frame.Effect = new DropShadowEffect
		{
			BlurRadius = 8,
			ShadowDepth = 2,
			Opacity = 0.3,
		};

		return new Popup
		{
			PlacementTarget = textArea,
			Placement = PlacementMode.Bottom,
			StaysOpen = true,
			AllowsTransparency = true,
			Child = frame,
		};
	}

	/// <include file="../../../../../shared/docs/PopupSeam.xml" path="doc/members/member[@name='ReleasePopup']/*"/>
	/// <remarks>
	/// A popup without a parent registers with its placement target, which then holds it, so clearing the target releases that registration.
	/// </remarks>
	internal static void ReleasePopup(Popup popup) => popup.PlacementTarget = null;

	/// <include file="../../../../../shared/docs/PopupSeam.xml" path="doc/members/member[@name='SetAnchor']/*"/>
	internal static void SetAnchor(Popup popup, Rect anchor) => popup.PlacementRectangle = anchor;

	/// <include file="../../../../../shared/docs/PopupSeam.xml" path="doc/members/member[@name='IsWindowInactive']/*"/>
	internal static bool IsWindowInactive(TextArea textArea)
	{
		if (Window.GetWindow(textArea) is { } window)
			return !window.IsActive;

		IntPtr rootWindow = PopupWindowInterop.GetRootWindow(textArea);
		return rootWindow != IntPtr.Zero && !PopupWindowInterop.IsForeground(rootWindow);
	}

	/// <include file="../../../../../shared/docs/PopupSeam.xml" path="doc/members/member[@name='SubscribeClosing']/*"/>
	/// <remarks>
	/// The popup also closes when the text area is unloaded, because a text area that leaves the tree without holding the keyboard focus
	/// would otherwise keep its popup over the place it left.
	/// </remarks>
	internal static IDisposable SubscribeClosing(TextArea textArea, Action close)
	{
		var window = Window.GetWindow(textArea);
		IntPtr rootWindow = window is null ? PopupWindowInterop.GetRootWindow(textArea) : IntPtr.Zero;

		void keyHandler(object sender, KeyEventArgs e)
		{
			if (e.Key != Key.Escape || e.Handled)
				return;

			e.Handled = true;
			close();
		}

		void focusHandler(object sender, DependencyPropertyChangedEventArgs e)
		{
			if (e.NewValue is false)
				close();
		}

		void closeHandler(object? sender, EventArgs e) => close();
		void unloadedHandler(object sender, RoutedEventArgs e) => close();

		textArea.PreviewKeyDown += keyHandler;
		textArea.IsKeyboardFocusWithinChanged += focusHandler;
		textArea.Unloaded += unloadedHandler;

		if (window is not null)
			window.Deactivated += closeHandler;

		IDisposable? hostDeactivation = rootWindow != IntPtr.Zero ? PopupWindowInterop.SubscribeDeactivation(rootWindow, close) : null;

		return new ActionSubscription(() =>
		{
			textArea.PreviewKeyDown -= keyHandler;
			textArea.IsKeyboardFocusWithinChanged -= focusHandler;
			textArea.Unloaded -= unloadedHandler;

			if (window is not null)
				window.Deactivated -= closeHandler;

			hostDeactivation?.Dispose();
		});
	}
}
