using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using ICSharpCode.AvalonEdit.CodeCompletion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

/// <summary>Applies the Win32 activation behavior of a completion window.</summary>
/// <remarks>
/// The hook answers the mouse-activate message with <c>MA_NOACTIVATE</c>
/// for clicks on the window. The hook is installed once per window and can be requested before the window is shown or after: it is added directly
/// when the window's source already exists, and otherwise registered through <see cref="Window.SourceInitialized"/>. The hook alone does not stop
/// a click on a list item from activating the window, because the item takes focus; the item style therefore makes the items non-focusable.
/// A click then selects nothing, so the host must select the clicked item itself.
/// </remarks>
internal static class CompletionWindowInterop
{
	private const int MouseActivateNoActivate = 3;
	private const int WindowMessageMouseActivate = 0x0021;

	// The windows a hook was already installed on, so a repeat call (a second call before the source is
	// initialized, or a call after the window was shown) does not install a second hook. The table holds its keys
	// weakly, matching the window lifetime, and the UI thread is the only writer.
	private static readonly ConditionalWeakTable<Window, object> s_hookedWindows = [];
	private static readonly object s_hookedMarker = new();

	/// <summary>Keeps a click on the given completion window from activating it.</summary>
	/// <param name="window">The completion window to make non-activatable.</param>
	/// <remarks>
	/// The first call installs the hook and the item style; a repeat call is a no-op.
	/// </remarks>
	internal static void EnsureNonActivatable(CompletionWindow window)
	{
		if (!s_hookedWindows.TryAdd(window, s_hookedMarker))
			return;

		MakeItemsNonFocusable(window.CompletionList.ListBox);

		// The hook can be installed directly when the window's source already exists (a late call); otherwise it
		// is registered through SourceInitialized, which runs before the window is shown.
		if (PresentationSource.FromVisual(window) is HwndSource source)
		{
			InstallHook(source);
			return;
		}

		window.SourceInitialized += static (sender, e) =>
		{
			if (sender is Window sourceWindow && PresentationSource.FromVisual(sourceWindow) is HwndSource initializedSource)
				InstallHook(initializedSource);
		};
	}

	private static void InstallHook(HwndSource source) => source.AddHook(CompletionWindowWndProc);

	// The style is based on the list's own item style, which supplies the item template.
	private static void MakeItemsNonFocusable(ListBox listBox)
	{
		var style = new Style(typeof(ListBoxItem), listBox.ItemContainerStyle);
		style.Setters.Add(new Setter(UIElement.FocusableProperty, false));
		listBox.ItemContainerStyle = style;
	}

	/// <summary>Processes the mouse-activate message that keeps a completion window non-activatable.</summary>
	/// <param name="hwnd">The window handle.</param>
	/// <param name="msg">The window message.</param>
	/// <param name="wParam">The message's word parameter.</param>
	/// <param name="lParam">The message's long parameter.</param>
	/// <param name="handled">Set to <see langword="true"/> when the message was handled.</param>
	/// <returns>The message result for the mouse-activate message; otherwise, zero.</returns>
	internal static IntPtr CompletionWindowWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		if (msg == WindowMessageMouseActivate)
		{
			handled = true;
			return (IntPtr)MouseActivateNoActivate;
		}

		return IntPtr.Zero;
	}
}
