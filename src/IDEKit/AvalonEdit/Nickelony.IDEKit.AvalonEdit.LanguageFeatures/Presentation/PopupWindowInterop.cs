using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Nickelony.IDEKit.Core.Notifications;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures;

/// <summary>Reads and follows the activation of the Win32 top-level window that hosts WPF content outside a WPF window.</summary>
/// <remarks>
/// A text area in an <c>ElementHost</c> or another <see cref="HwndSource"/> child has no WPF <see cref="Window"/>,
/// so no window reports its activation. The top-level window of the hosting handle counts as active when it, or a window
/// that owns it, is the foreground window, so the content of an owned popup follows the window that owns the popup. A foreground change
/// to any other window deactivates it; a system-wide event hook reports that change on the thread that installs it.
/// </remarks>
internal static class PopupWindowInterop
{
	private const uint EventSystemForeground = 0x0003;
	private const uint WinEventOutOfContext = 0x0000;
	private const uint GetAncestorRoot = 2;
	private const uint GetWindowOwner = 4;

	/// <summary>
	/// Returns the top-level window that hosts the element, or <see cref="IntPtr.Zero"/> when the element is not shown in a handle.
	/// </summary>
	internal static IntPtr GetRootWindow(Visual element)
	{
		return PresentationSource.FromVisual(element) is HwndSource { Handle: var handle } && handle != IntPtr.Zero
			? GetAncestor(handle, GetAncestorRoot)
			: IntPtr.Zero;
	}

	/// <summary>Reports whether the top-level window, or a window that owns it, is the foreground window.</summary>
	internal static bool IsForeground(IntPtr rootWindow) => IsSelfOrOwner(rootWindow, GetForegroundWindow());

	/// <summary>
	/// Invokes <paramref name="deactivated"/> when a window other than <paramref name="rootWindow"/> and its owners takes the foreground.
	/// </summary>
	/// <returns>A subscription that removes the hook when it is disposed; it must be disposed on the thread that subscribed.</returns>
	/// <remarks>
	/// The hook reports through the message loop of the subscribing thread, so the callback runs on that thread.
	/// </remarks>
	internal static IDisposable SubscribeDeactivation(IntPtr rootWindow, Action deactivated)
	{
		// The hook keeps only a native pointer to the callback, so the subscription holds the delegate until it unhooks.
		WinEventProc callback = (_, _, window, _, _, _, _) =>
		{
			if (!IsSelfOrOwner(rootWindow, window))
				deactivated();
		};

		IntPtr hook = SetWinEventHook(
			eventMin: EventSystemForeground,
			eventMax: EventSystemForeground,
			module: IntPtr.Zero,
			callback,
			processId: 0,
			threadId: 0,
			flags: WinEventOutOfContext
		);

		return new ActionSubscription(() =>
		{
			if (hook != IntPtr.Zero)
				UnhookWinEvent(hook);

			GC.KeepAlive(callback);
		});
	}

	// Whether the candidate is the window or a window up its owner chain.
	internal static bool IsSelfOrOwner(IntPtr window, IntPtr candidate)
	{
		if (candidate == IntPtr.Zero)
			return false;

		for (IntPtr current = window; current != IntPtr.Zero; current = GetWindow(current, GetWindowOwner))
		{
			if (current == candidate)
				return true;
		}

		return false;
	}

	private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint eventThread, uint eventTime);

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern IntPtr GetAncestor(IntPtr window, uint flags);

	[DllImport("user32.dll")]
	private static extern IntPtr GetWindow(IntPtr window, uint command);

	[DllImport("user32.dll")]
	private static extern IntPtr SetWinEventHook(
		uint eventMin,
		uint eventMax,
		IntPtr module,
		WinEventProc callback,
		uint processId,
		uint threadId,
		uint flags
	);

	[DllImport("user32.dll")]
	private static extern bool UnhookWinEvent(IntPtr hook);
}
