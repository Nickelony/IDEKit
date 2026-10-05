using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.AutoClosing;
using Nickelony.IDEKit.Core.AutoClosing;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

/// <summary>Verifies the auto-closing wiring that the package readme shows, through the headless input pipeline.</summary>
/// <remarks>
/// The two handler lines of <c>Wire</c> are a copy of the readme's, so the test fails when that wiring stops removing the pair, and an edit of the
/// readme's lines needs the same edit here.
/// </remarks>
[AvaloniaTestClass]
public sealed class ReadmeAutoClosingWiringTests
{
	[TestMethod]
	public void ReadmeWiring_OpeningParenthesisThenBackspace_RemovesTheWholePair()
	{
		var editor = new TextEditor { Document = new TextDocument("ab") };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Wire(editor);

		editor.TextArea.Focus();
		editor.CaretOffset = 2;

		hostWindow.KeyTextInput("(");

		Assert.AreEqual("ab()", editor.Text);
		Assert.AreEqual(3, editor.CaretOffset);

		hostWindow.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);

		Assert.AreEqual("ab", editor.Text);
		Assert.AreEqual(2, editor.CaretOffset);
	}

	private static void Wire(TextEditor editor)
	{
		var autoClosing = new AutoClosingService();
		var options = AutoClosingOptions.Default;

		editor.TextArea.TextEntering += (_, e) => autoClosing.ApplyTextEntering(editor, e, options);
		editor.AddHandler(InputElement.KeyDownEvent, (_, e) => autoClosing.TryDeletePair(editor, e, options), RoutingStrategies.Tunnel);
	}
}
