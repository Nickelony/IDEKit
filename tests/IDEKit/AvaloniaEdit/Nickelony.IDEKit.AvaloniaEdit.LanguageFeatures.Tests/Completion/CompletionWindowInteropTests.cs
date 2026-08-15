using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

/// <summary>
/// Pins the completion window activation policy on AvaloniaEdit. There is no Win32 message hook to drive
/// (the Avalonia completion window is a popup hosted in the editor's own window, so a click cannot activate
/// a separate window), so these tests pin the documented graceful no-op behavior the mirror implements: the
/// call validates its argument, is idempotent, and leaves a shown window usable.
/// </summary>
[AvaloniaTestClass]
public sealed class CompletionWindowInteropTests
{
	[TestMethod]
	public void MakeNonActivatable_NullWindow_ThrowsArgumentNullException()
	{
		// The mirror keeps the engine-neutral argument guard even though the activation hook is a no-op.
		var exception = Assert.ThrowsExactly<ArgumentNullException>(() => CompletionWindowInterop.MakeNonActivatable(null!));

		Assert.AreEqual("control", exception.ParamName);
	}

	[TestMethod]
	public void MakeNonActivatable_ShownWindow_IsAnIdempotentNoOp()
	{
		(TextEditor editor, HostWindow hostWindow) = AvaloniaTestHost.ShowHostedEditor("sample");

		using (hostWindow)
		{
			var completionWindow = new CompletionWindow(editor.TextArea);

			// Installing twice must stay harmless (idempotent installation); the Avalonia engine installs
			// no OS hook, so every call is a documented no-op.
			CompletionWindowInterop.MakeNonActivatable(completionWindow);
			CompletionWindowInterop.MakeNonActivatable(completionWindow);
			completionWindow.Show();

			// The non-activating behavior is delegated to the popup itself, so the window shows normally and
			// there is no window handle to send a mouse-activate message to.
			Assert.IsTrue(completionWindow.IsOpen);

			completionWindow.Close();
		}
	}
}
