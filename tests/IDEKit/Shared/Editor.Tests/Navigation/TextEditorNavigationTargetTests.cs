#if AVALONIAEDIT
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Navigation;
using Nickelony.IDEKit.Core.Text;
using static Nickelony.IDEKit.AvaloniaEdit.Tests.DocumentTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Navigation;
using Nickelony.IDEKit.Core.Text;
using static Nickelony.IDEKit.AvalonEdit.Tests.DocumentTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

/// <summary>
/// Covers the editor navigation target in its binding: what it reads of the document, the caret move and the range selection
/// with their clamps and scrolling, and the view that a preview saves and restores.
/// </summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class TextEditorNavigationTargetTests
{
	private static string LongText(int lineCount) => string.Join("\r\n", Enumerable.Range(1, lineCount).Select(line => $"line {line}"));

	private static string WideText(int lineCount) =>
		string.Join("\r\n", Enumerable.Range(1, lineCount).Select(line => line == 1 ? new string('x', 3_000) : $"line {line}"));

	[TestMethod]
	public void Constructor_NullEditor_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => new TextEditorNavigationTarget(null!));

	[TestMethod]
	public void Members_ReadTheDocumentAndTheCaret()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		var target = new TextEditorNavigationTarget(editor);

		editor.CaretOffset = 6;

		Assert.AreEqual(3, target.LineCount);
		Assert.AreEqual(3, target.GetLineLength(1));
		Assert.AreEqual(3, target.GetLineLength(2));
		Assert.AreEqual(5, target.GetLineLength(3));
		Assert.AreEqual(2, target.CaretLineNumber);
		Assert.AreEqual(2, target.CaretColumn);
	}

	[TestMethod]
	public void GetLineLength_ALineOutsideTheDocument_ReadsTheNearestLine()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\nthree");
		var target = new TextEditorNavigationTarget(editor);

		Assert.AreEqual(3, target.GetLineLength(0));
		Assert.AreEqual(5, target.GetLineLength(9));
	}

	[TestMethod]
	public void MoveCaret_PutsTheCaretAtTheLineAndTheColumnAndClearsTheSelection()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var target = new TextEditorNavigationTarget(editor);

		editor.Select(0, 3);
		target.MoveCaret(3, 2);

		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(11, editor.CaretOffset);
		Assert.AreEqual(3, target.CaretLineNumber);
		Assert.AreEqual(2, target.CaretColumn);
		Assert.AreEqual(0, editor.SelectionLength);
	}

	[TestMethod]
	public void MoveCaret_AColumnPastTheEndOfTheLine_PutsTheCaretAtTheEndOfTheLine()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var target = new TextEditorNavigationTarget(editor);

		target.MoveCaret(1, 99);

		Assert.AreEqual(3, editor.CaretOffset);
	}

	[TestMethod]
	[DataRow(0, 0, 0)]
	[DataRow(-5, -5, 0)]
	[DataRow(99, 1, 10)]
	[DataRow(99, 99, 15)]
	[DataRow(2, 0, 5)]
	public void MoveCaret_APositionOutsideTheDocument_ClampsToTheNearestPosition(int line, int column, int expectedOffset)
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var target = new TextEditorNavigationTarget(editor);

		target.MoveCaret(line, column);

		Assert.AreEqual(expectedOffset, editor.CaretOffset);
	}

	[TestMethod]
	public void MoveCaret_ALineFarBelow_ScrollsItIntoView()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(LongText(300));
		using HostWindow _ = hostWindow;
		var target = new TextEditorNavigationTarget(editor);

		Assert.IsFalse(IsLineVisible(editor, 250));

		target.MoveCaret(250, 1);

		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.IsTrue(IsLineVisible(editor, 250));
	}

	[TestMethod]
	public void MoveCaret_AColumnBeyondTheRightEdgeOfTheView_ScrollsHorizontallyAndBack()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(WideText(3));
		using HostWindow _ = hostWindow;
		var target = new TextEditorNavigationTarget(editor);

		Assert.AreEqual(0.0, editor.TextArea.TextView.ScrollOffset.X);

		target.MoveCaret(1, 900);
		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.IsGreaterThan(0.0, editor.TextArea.TextView.ScrollOffset.X);

		target.MoveCaret(1, 1);
		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(0.0, editor.TextArea.TextView.ScrollOffset.X);
	}

	[TestMethod]
	public void SelectRange_SelectsTheRangeWithTheCaretAtItsEnd()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var target = new TextEditorNavigationTarget(editor);

		editor.Select(0, 3);
		target.SelectRange(new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 3)));

		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual("two", editor.SelectedText);
		Assert.AreEqual(5, editor.SelectionStart);
		Assert.AreEqual(8, editor.CaretOffset);
	}

	[TestMethod]
	public void SelectRange_ARangeBeyondTheDocument_ClampsToTheDocument()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var target = new TextEditorNavigationTarget(editor);

		target.SelectRange(new TextPositionRange(new TextPosition(0, 1), new TextPosition(99, 99)));

		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(1, editor.SelectionStart);
		Assert.AreEqual(14, editor.SelectionLength);
		Assert.AreEqual(15, editor.CaretOffset);
	}

	[TestMethod]
	public void SelectRange_AReversedRange_SelectsTheSameTextAsTheOrderedRange()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var target = new TextEditorNavigationTarget(editor);

		target.SelectRange(new TextPositionRange(new TextPosition(1, 3), new TextPosition(0, 1)));

		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(1, editor.SelectionStart);
		Assert.AreEqual(7, editor.SelectionLength);
		Assert.AreEqual(8, editor.CaretOffset);
	}

	[TestMethod]
	public void SelectRange_ARangeFarBelow_ScrollsItsStartIntoView()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(LongText(300));
		using HostWindow _ = hostWindow;
		var target = new TextEditorNavigationTarget(editor);

		Assert.IsFalse(IsLineVisible(editor, 250));

		target.SelectRange(new TextPositionRange(new TextPosition(249, 0), new TextPosition(249, 4)));

		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.IsTrue(IsLineVisible(editor, 250));
		Assert.AreEqual("line", editor.SelectedText);
	}

	[TestMethod]
	public void RestoreView_PutsBackTheCaretAndTheSelection()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var target = new TextEditorNavigationTarget(editor);

		editor.Select(5, 3);
		editor.CaretOffset = 8;
		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		target.SaveView();
		target.MoveCaret(3, 1);
		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(0, editor.SelectionLength);

		target.RestoreView();
		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual("two", editor.SelectedText);
		Assert.AreEqual(5, editor.SelectionStart);
		Assert.AreEqual(8, editor.CaretOffset);
	}

	[TestMethod]
	public void RestoreView_PutsBackTheScrollPosition()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(LongText(300));
		using HostWindow _ = hostWindow;
		var target = new TextEditorNavigationTarget(editor);

		editor.ScrollToLine(150);
		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		double scrolledTo = editor.TextArea.TextView.ScrollOffset.Y;

		Assert.IsGreaterThan(0.0, scrolledTo);

		target.SaveView();
		target.MoveCaret(1, 1);
		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreNotEqual(scrolledTo, editor.TextArea.TextView.ScrollOffset.Y);

		target.RestoreView();
		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(scrolledTo, editor.TextArea.TextView.ScrollOffset.Y, 0.5);
	}

	[TestMethod]
	public void RestoreView_PutsBackTheHorizontalScrollPosition()
	{
		(TextEditor editor, HostWindow hostWindow) = TestHost.ShowHostedEditor(WideText(3));
		using HostWindow _ = hostWindow;
		var target = new TextEditorNavigationTarget(editor);

		target.MoveCaret(1, 900);
		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		double scrolledTo = editor.TextArea.TextView.ScrollOffset.X;

		Assert.IsGreaterThan(0.0, scrolledTo);

		target.SaveView();
		target.MoveCaret(1, 1);
		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(0.0, editor.TextArea.TextView.ScrollOffset.X);

		target.RestoreView();
		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(scrolledTo, editor.TextArea.TextView.ScrollOffset.X, 0.5);
	}

	[TestMethod]
	public void RestoreView_WithoutASavedView_DoesNothing()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var target = new TextEditorNavigationTarget(editor);

		target.MoveCaret(2, 2);
		target.RestoreView();
		target.SaveView();

		target.RestoreView();
		target.RestoreView();

		Assert.AreEqual(6, editor.CaretOffset);
	}

	[TestMethod]
	public void Members_AnEditorWithoutADocument_CountOneLineAndMoveNothing()
	{
		TextEditor editor = TestHost.CreateEditor("one");
		var target = new TextEditorNavigationTarget(editor);

		editor.Document = null!;

		Assert.AreEqual(1, target.LineCount);
		Assert.AreEqual(0, target.GetLineLength(1));

		target.SaveView();
		target.MoveCaret(1, 1);
		target.SelectRange(new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 1)));
		target.RestoreView();
	}
}
