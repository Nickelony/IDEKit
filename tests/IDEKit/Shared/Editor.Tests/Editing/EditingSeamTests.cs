#if AVALONIAEDIT
using Avalonia.Threading;
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.Editing;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.Editing;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

/// <summary>Covers the scroll call that the binding gives the shared editing and navigation code.</summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class EditingSeamTests
{
	// Every line is wide, because the horizontal extent follows the lines in view and a scroll down would otherwise shrink it.
	private static string WideLongText() => string.Join("\r\n", Enumerable.Range(1, 300).Select(line => $"line {line} " + new string('x', 400)));

	[TestMethod]
	public void SetScrollOffset_ScrollsTheTextViewToTheGivenOffsets()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(WideLongText());
		using HostWindow _ = hostWindow;

		EditingSeam.SetScrollOffset(editor, 120, 200);
		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(120.0, editor.TextArea.TextView.ScrollOffset.X, 0.5);
		Assert.AreEqual(200.0, editor.TextArea.TextView.ScrollOffset.Y, 0.5);
	}

	[TestMethod]
	public void SetScrollOffset_Zero_ScrollsBackToTheOrigin()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(WideLongText());
		using HostWindow _ = hostWindow;

		EditingSeam.SetScrollOffset(editor, 120, 200);
		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);
		EditingSeam.SetScrollOffset(editor, 0, 0);
		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(0.0, editor.TextArea.TextView.ScrollOffset.X, 0.5);
		Assert.AreEqual(0.0, editor.TextArea.TextView.ScrollOffset.Y, 0.5);
	}
}
