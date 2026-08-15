using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>
/// Covers the effect of the completion window interop, not just its message switch: a window made
/// non-activatable answers the Win32 mouse-activate message with "no activate" once it is shown.
/// </summary>
[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed class CompletionWindowInteropEffectTests
{
	private const int WindowMessageMouseActivate = 0x0021;
	private const int MouseActivateNoActivate = 3;

	[TestMethod]
	public void MakeNonActivatable_ShownWindow_AnswersMouseActivateWithNoActivate()
	{
		var editor = WPFTestHost.CreateEditor("sample");
		using var hostWindow = WPFTestHost.ShowInHostWindow(editor);
		var completionWindow = new CompletionWindow(editor.TextArea);

		// Installing twice must stay harmless (idempotent installation), and the hook must be in place
		// before the window's source is initialized.
		CompletionWindowInterop.MakeNonActivatable(completionWindow);
		CompletionWindowInterop.MakeNonActivatable(completionWindow);
		completionWindow.Show();

		try
		{
			IntPtr handle = new WindowInteropHelper(completionWindow).Handle;
			IntPtr result = SendMessage(handle, WindowMessageMouseActivate, IntPtr.Zero, IntPtr.Zero);

			Assert.AreEqual((IntPtr)MouseActivateNoActivate, result);
		}
		finally
		{
			completionWindow.Close();
		}
	}

	[DllImport("user32.dll")]
	private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
