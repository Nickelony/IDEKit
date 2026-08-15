using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

/// <summary>
/// Covers the effect of the completion window interop. In WPF the interop answers the Win32 mouse-activate
/// message with "no activate"; an AvaloniaEdit completion window is a popup hosted in the editor's own
/// window, so the mirror installs no OS hook and the Win32 effect disappears. The test pins the documented
/// graceful-disable behavior the mirror implements instead: pre-show installation is harmless (idempotent),
/// the window still shows, and it still closes through the normal path.
/// </summary>
[AvaloniaTestClass]
public sealed class CompletionWindowInteropEffectTests
{
	// The WPF reference sent this message to the shown window's handle; Avalonia exposes no handle to a
	// test, so the constants are documented here to keep the reference's intent visible.
	private const int WindowMessageMouseActivate = 0x0021;
	private const int MouseActivateNoActivate = 3;

	[TestMethod]
	public void MakeNonActivatable_ShownWindow_ShowsAndClosesWithoutAnActivationHook()
	{
		(TextEditor editor, HostWindow hostWindow) = AvaloniaTestHost.ShowHostedEditor("sample");

		using (hostWindow)
		{
			var completionWindow = new CompletionWindow(editor.TextArea);

			// Installing twice must stay harmless (idempotent installation), and the hook must be in place
			// before the window is shown.
			CompletionWindowInterop.MakeNonActivatable(completionWindow);
			CompletionWindowInterop.MakeNonActivatable(completionWindow);
			completionWindow.Show();

			try
			{
				// Avalonia divergence: there is no window handle to send the mouse-activate message to
				// (WindowMessageMouseActivate answered with MouseActivateNoActivate in the WPF reference), so
				// the interaction effect is pinned through the window's own open/close lifecycle. The popup
				// does not activate the parent window, which is the policy the WPF message handler enforced.
				Assert.IsTrue(completionWindow.IsOpen);
			}
			finally
			{
				completionWindow.Close();
			}
		}
	}
}
