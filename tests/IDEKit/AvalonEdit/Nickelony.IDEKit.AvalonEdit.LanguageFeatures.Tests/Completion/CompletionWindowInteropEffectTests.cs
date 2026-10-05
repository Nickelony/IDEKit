using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>
/// Covers the effect of the completion window interop, reached through the seam the controller calls, not merely its message switch: a window made
/// non-activatable answers the Win32 mouse-activate message with "no activate" once it is shown.
/// </summary>
[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed class CompletionWindowInteropEffectTests
{
	private const int WindowMessageMouseActivate = 0x0021;
	private const int MouseActivateNoActivate = 3;

	[TestMethod]
	public void EnsureNonActivatable_ShownWindow_AnswersMouseActivateWithNoActivate()
	{
		TextEditor editor = WpfTestHost.CreateEditor("sample");
		using HostWindow hostWindow = WpfTestHost.ShowInHostWindow(editor);

		var completionWindow = new CompletionWindow(editor.TextArea);

		ListBox listBox = completionWindow.CompletionList.ListBox;
		Style? originalItemStyle = listBox.ItemContainerStyle;

		// Installing twice must stay harmless (idempotent installation), and the hook must be in place
		// before the window's source is initialized.
		CompletionSeam.EnsureNonActivatable(completionWindow);

		Style? installedItemStyle = listBox.ItemContainerStyle;

		CompletionSeam.EnsureNonActivatable(completionWindow);

		completionWindow.Show();

		try
		{
			IntPtr handle = new WindowInteropHelper(completionWindow).Handle;
			IntPtr result = SendMessage(hWnd: handle, msg: WindowMessageMouseActivate, wParam: IntPtr.Zero, lParam: IntPtr.Zero);

			Assert.AreEqual((IntPtr)MouseActivateNoActivate, result);

			// The first call wrapped the list's own item style once; the repeat returned through the hooked-window guard that also
			// guards the hook registration, so it neither wrapped the style again nor registered a second SourceInitialized handler.
			Assert.IsNotNull(installedItemStyle);
			Assert.AreNotSame(originalItemStyle, installedItemStyle);
			Assert.AreSame(originalItemStyle, installedItemStyle.BasedOn);
			Assert.AreSame(installedItemStyle, listBox.ItemContainerStyle);
		}
		finally
		{
			completionWindow.Close();
		}
	}

	[DllImport("user32.dll")]
	private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
