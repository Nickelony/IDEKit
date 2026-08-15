using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

[TestClass]
public sealed class CompletionWindowInteropTests
{
	[TestMethod]
	public void CompletionWindowWndProc_MouseActivate_ReturnsNoActivateAndHandlesMessage()
	{
		const int WindowMessageMouseActivate = 0x0021;
		bool handled = false;

		IntPtr result = CompletionWindowInterop.CompletionWindowWndProc(
			IntPtr.Zero,
			WindowMessageMouseActivate,
			IntPtr.Zero,
			IntPtr.Zero,
			ref handled);

		// The mouse-activate message is answered with "no activate" so clicking the popup cannot steal focus.
		Assert.IsTrue(handled);
		Assert.AreEqual((IntPtr)3, result);
	}

	[TestMethod]
	public void CompletionWindowWndProc_OtherMessage_ReturnsZeroWithoutHandling()
	{
		const int WindowMessageNull = 0x0000;
		bool handled = false;

		IntPtr result = CompletionWindowInterop.CompletionWindowWndProc(
			IntPtr.Zero,
			WindowMessageNull,
			IntPtr.Zero,
			IntPtr.Zero,
			ref handled);

		Assert.IsFalse(handled);
		Assert.AreEqual(IntPtr.Zero, result);
	}
}
