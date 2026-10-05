using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>
/// Covers the activation that a feature popup reads for a text area that no WPF window owns, as in an <c>ElementHost</c>: the Win32 top-level window
/// that hosts it, its foreground state and the foreground changes that deactivate it.
/// </summary>
[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed class PopupWindowInteropTests
{
	private const uint EventSystemForeground = 0x0003;
	private const int WindowStylePopup = unchecked((int)0x80000000);

	[TestMethod]
	public void TextAreaOutsideAWpfWindow_IsInactiveWhileItsHostIsNotTheForegroundWindow()
	{
		TextEditor editor = WpfTestHost.CreateEditor("sample");
		using HwndSource source = CreateHiddenSource(editor);

		Assert.IsNull(Window.GetWindow(editor.TextArea));
		Assert.AreEqual(source.Handle, PopupWindowInterop.GetRootWindow(editor.TextArea));

		// A hidden window is never the foreground window, so a popup over its text area would cover the application in the foreground.
		Assert.IsTrue(PopupSeam.IsWindowInactive(editor.TextArea));
	}

	[TestMethod]
	public void TextAreaInNoWindowOrHandle_IsNeverInactive()
	{
		TextEditor editor = WpfTestHost.CreateEditor("sample");

		Assert.AreEqual(IntPtr.Zero, PopupWindowInterop.GetRootWindow(editor.TextArea));
		Assert.IsFalse(PopupSeam.IsWindowInactive(editor.TextArea));
	}

	// The hook is system-wide, so a foreground change elsewhere on the desktop may add a report; the test asserts
	// only that this change is reported, and the owner-chain tests below pin which windows keep the root active.
	[TestMethod]
	public void SubscribeDeactivation_AnotherWindowTakingTheForeground_ReportsIt()
	{
		using HwndSource root = CreateHiddenSource(new TextBlock());
		using HwndSource other = CreateHiddenSource(new TextBlock());

		int deactivations = 0;

		using (PopupWindowInterop.SubscribeDeactivation(root.Handle, () => deactivations++))
		{
			NotifyWinEvent(eventType: EventSystemForeground, window: other.Handle, objectId: 0, childId: 0);
			DispatcherTestHelpers.PumpUntil(() => deactivations > 0);
		}

		Assert.IsGreaterThan(0, deactivations);
	}

	[TestMethod]
	public void IsSelfOrOwner_TheWindowItself_KeepsItActive()
	{
		using HwndSource root = CreateHiddenSource(new TextBlock());
		Assert.IsTrue(PopupWindowInterop.IsSelfOrOwner(root.Handle, root.Handle));
	}

	[TestMethod]
	public void IsSelfOrOwner_TheOwnerOfAPopup_KeepsThePopupActive()
	{
		using HwndSource owner = CreateHiddenSource(new TextBlock());
		using HwndSource popup = CreateHiddenSource(new TextBlock(), owner.Handle);

		// The content of an owned popup counts as active while the window that owns the popup is the foreground window.
		Assert.IsTrue(PopupWindowInterop.IsSelfOrOwner(popup.Handle, owner.Handle));
	}

	[TestMethod]
	public void IsSelfOrOwner_AnotherWindowOrNoWindow_Deactivates()
	{
		using HwndSource root = CreateHiddenSource(new TextBlock());
		using HwndSource other = CreateHiddenSource(new TextBlock());

		Assert.IsFalse(PopupWindowInterop.IsSelfOrOwner(root.Handle, other.Handle));
		Assert.IsFalse(PopupWindowInterop.IsSelfOrOwner(root.Handle, IntPtr.Zero));
	}

	// A hidden top-level window, or one owned by the given window, that hosts the content without a WPF window.
	private static HwndSource CreateHiddenSource(Visual content, IntPtr owner = default)
	{
		return new(new HwndSourceParameters("popup-window-interop-test", 200, 100) { WindowStyle = WindowStylePopup, ParentWindow = owner })
		{
			RootVisual = content,
		};
	}

	[DllImport("user32.dll")]
	private static extern void NotifyWinEvent(uint eventType, IntPtr window, int objectId, int childId);
}
