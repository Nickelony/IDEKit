using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nickelony.Testing;

/// <summary>Makes a test window report itself active or inactive, the way Windows does, without moving the real foreground.</summary>
/// <remarks>
/// <para>
/// The host window of a test never takes the foreground, so the helpers send the window the activation message
/// that Windows sends it; WPF updates the window's active state and raises its activation events from that message alone.
/// </para>
/// <para>
/// Once a helper set the state, Windows cannot change it: the window, and a popup over it that closes on a click outside it, drop the messages
/// by which Windows deactivates them or takes their mouse capture or keyboard focus, and the buttons of the real mouse. A foreground change
/// in another process, such as a parallel test run, sends those messages, and so does a user who clicks at the desktop, and the window
/// would deactivate or the popup close in the middle of a test.
/// </para>
/// </remarks>
internal static class TestWindowActivation
{
	private const int WindowMessageActivate = 0x0006;
	private const int WindowMessageKillFocus = 0x0008;
	private const int WindowMessageActivateApp = 0x001C;
	private const int WindowMessageCancelMode = 0x001F;
	private const int WindowMessageNonClientDestroy = 0x0082;
	private const int WindowMessageFirstMouseButton = 0x0201;
	private const int WindowMessageMouseWheel = 0x020A;
	private const int WindowMessageLastMouseButton = 0x020D;
	private const int WindowMessageCaptureChanged = 0x0215;
	private const int ActivateInactive = 0;
	private const int ActivateActive = 1;
	private const int WindowProcedureIndex = -4;

	// WPF closes such a popup through this callback when the application deactivates, and no public member names the callback of an operation.
	private const string PopupDeactivationCallback = "HandleDeactivateApp";

	private static readonly FieldInfo? s_operationCallback = typeof(DispatcherOperation).GetField(
		"_method",
		BindingFlags.Instance | BindingFlags.NonPublic
	);

	private static readonly ConditionalWeakTable<Window, object> s_heldWindows = [];
	private static readonly ConditionalWeakTable<Dispatcher, object> s_heldDispatchers = [];
	private static readonly object s_held = new();
	private static readonly Lazy<bool> s_captureHandler = new(RegisterCaptureHandler);

	// The procedure is static, so it outlives every window it is installed on.
	private static readonly WindowProcedure s_dropForeignDismissal = DropForeignDismissal;

	[ThreadStatic]
	private static bool t_isSending;

	// The procedure that each held window had before the helper installed its own, by window handle.
	[ThreadStatic]
	private static Dictionary<IntPtr, IntPtr>? t_procedures;

	private delegate IntPtr WindowProcedure(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

	/// <summary>Makes the window report itself active, as it does when it takes the foreground.</summary>
	/// <param name="window">The window to activate.</param>
	public static void Activate(Window window)
	{
		Send(window, ActivateActive);
		Assert.IsTrue(window.IsActive);
	}

	/// <summary>Makes the window report itself inactive, as it does when another application takes the foreground.</summary>
	/// <param name="window">The window to deactivate.</param>
	public static void Deactivate(Window window)
	{
		Send(window, ActivateInactive);
		Assert.IsFalse(window.IsActive);
	}

	private static void Send(Window window, int state)
	{
		IntPtr handle = new WindowInteropHelper(window).Handle;

		Hold(window, handle);

		t_isSending = true;

		try
		{
			SendMessage(hWnd: handle, msg: WindowMessageActivate, wParam: state, lParam: IntPtr.Zero);
		}
		finally
		{
			t_isSending = false;
		}
	}

	private static void Hold(Window window, IntPtr handle)
	{
		_ = s_captureHandler.Value;

		if (!s_heldWindows.TryGetValue(window, out _))
		{
			InstallProcedure(handle);
			s_heldWindows.Add(window, s_held);
		}

		if (s_heldDispatchers.TryGetValue(window.Dispatcher, out _))
			return;

		Assert.IsNotNull(s_operationCallback, "WPF no longer keeps the callback of a dispatcher operation where the test helper reads it.");

		window.Dispatcher.Hooks.OperationPosted += DropPopupDeactivation;
		s_heldDispatchers.Add(window.Dispatcher, s_held);
	}

	// WPF reads the input messages of a window before any hook, so the window gets a procedure of its own that runs ahead of WPF's.
	private static void InstallProcedure(IntPtr handle)
	{
		t_procedures ??= [];

		if (!t_procedures.ContainsKey(handle))
			t_procedures[handle] = SetWindowLongPtr(handle, WindowProcedureIndex, Marshal.GetFunctionPointerForDelegate(s_dropForeignDismissal));
	}

	// The deactivation can arrive while WPF still creates the popup window, before the helper's procedure is on it, so the helper drops the close
	// that WPF posts for it instead of the message.
	private static void DropPopupDeactivation(object? sender, DispatcherHookEventArgs e)
	{
		if (
			s_operationCallback?.GetValue(e.Operation) is Delegate { Method: { Name: PopupDeactivationCallback, DeclaringType: { } type } }
			&& type == typeof(Popup)
		)
		{
			e.Operation.Abort();
		}
	}

	// A popup that closes on a click outside it takes the mouse capture when it opens, so the capture tells which popup to hold.
	private static bool RegisterCaptureHandler()
	{
		EventManager.RegisterClassHandler(
			classType: typeof(UIElement),
			routedEvent: Mouse.GotMouseCaptureEvent,
			handler: new MouseEventHandler(HoldPopup),
			handledEventsToo: true
		);

		return true;
	}

	private static void HoldPopup(object sender, MouseEventArgs e)
	{
		if (
			sender is Visual visual
			&& PresentationSource.FromVisual(visual) is HwndSource source
			&& source.RootVisual is FrameworkElement { Parent: Popup { StaysOpen: false, PlacementTarget: { } target } }
			&& Window.GetWindow(target) is { } window
			&& s_heldWindows.TryGetValue(window, out _)
		)
		{
			InstallProcedure(source.Handle);
		}
	}

	// Windows deactivates a window or the application, or moves the capture or the keyboard focus to a window of another thread or to none; an
	// activation that a helper sends, and a capture or a focus that the test moves, stay on this thread. A test raises its clicks as WPF events,
	// so a button of the real mouse is a user at the desktop, whose click on a topmost popup would close it.
	private static IntPtr DropForeignDismissal(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
	{
		IntPtr previous = t_procedures![hWnd];

		bool isForeign = msg switch
		{
			WindowMessageActivate => !t_isSending,
			WindowMessageActivateApp => wParam == IntPtr.Zero,
			WindowMessageCancelMode => true,
			WindowMessageKillFocus => !IsOnThisThread(wParam),
			WindowMessageCaptureChanged => !IsOnThisThread(lParam),
			>= WindowMessageFirstMouseButton and <= WindowMessageLastMouseButton => msg != WindowMessageMouseWheel,
			_ => false,
		};

		if (isForeign)
			return IntPtr.Zero;

		if (msg == WindowMessageNonClientDestroy)
			t_procedures.Remove(hWnd);

		return CallWindowProc(previous, hWnd, msg, wParam, lParam);
	}

	private static bool IsOnThisThread(IntPtr window) => GetWindowThreadProcessId(window, IntPtr.Zero) == GetCurrentThreadId();

	[DllImport("user32.dll")]
	private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
	private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newLong);

	[DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
	private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentThreadId();
}
