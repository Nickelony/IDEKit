using System.Windows;
using System.Windows.Interop;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

/// <summary>
/// Applies the Win32 activation behavior of a completion window.
/// </summary>
/// <remarks>
/// AvalonEdit's own <c>ShowActivated = false</c> default still lets a click on the completion list activate the
/// popup window, which would steal focus from the editor. Handling the mouse-activate message as "no activate"
/// keeps clicks in the list from activating the window, matching the behavior of non-activating WPF popups.
/// The hook is registered through <see cref="Window.SourceInitialized"/> and therefore must be installed
/// before the window's source is initialized (before it is shown).
/// </remarks>
internal static class CompletionWindowInterop
{
	private const int MouseActivateNoActivate = 3;
	private const int WindowMessageMouseActivate = 0x0021;

	/// <summary>
	/// Keeps the given window from activating when it is clicked by handling the mouse-activate message
	/// as "no activate". Call this before the window is shown, while its source is not initialized yet.
	/// </summary>
	/// <param name="window">The window to make non-activatable.</param>
	internal static void MakeNonActivatable(Window window)
	{
		window.SourceInitialized += static (sender, e) =>
		{
			if (sender is Window sourceWindow && PresentationSource.FromVisual(sourceWindow) is HwndSource source)
				source.AddHook(CompletionWindowWndProc);
		};
	}

	/// <summary>
	/// Processes the window messages needed to keep a completion window non-activatable.
	/// </summary>
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
