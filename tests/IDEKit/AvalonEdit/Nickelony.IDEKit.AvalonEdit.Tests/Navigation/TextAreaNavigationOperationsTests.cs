using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Navigation;
using Nickelony.IDEKit.Core.Identifiers;
using Nickelony.IDEKit.Core.Navigation;
using System.Windows.Controls;
using System.Windows.Threading;
using static Nickelony.IDEKit.AvalonEdit.Tests.DocumentTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed class TextAreaNavigationOperationsTests
{
	[TestMethod]
	public void GetWordFromOffset_OffsetInsideWord_ReturnsWord()
	{
		TextEditor editor = WPFTestHost.CreateEditor("one two three");

		Assert.AreEqual("two", editor.TextArea.GetWordFromOffset(5));
	}

	[TestMethod]
	public void GetWordFromOffset_OffsetInsideFirstWord_ReturnsFirstWord()
	{
		TextEditor editor = WPFTestHost.CreateEditor("one two three");

		Assert.AreEqual("one", editor.TextArea.GetWordFromOffset(1));
	}

	[TestMethod]
	public void GetWordFromOffset_CustomPolicy_UsesTheSuppliedCharacterRules()
	{
		TextEditor editor = WPFTestHost.CreateEditor("a-b");
		IdentifierCharacterPolicy policy = IdentifierCharacterPolicy.Create(
			static character => char.IsLetterOrDigit(character) || character == '-');

		// The custom policy treats the hyphen as a word character, so the whole token is returned
		// where the default policy would stop at the separator.
		Assert.AreEqual("a-b", editor.TextArea.GetWordFromOffset(2, policy));
		Assert.AreEqual("b", editor.TextArea.GetWordFromOffset(2));
	}

	[TestMethod]
	public void GetWordFromOffset_EmptyDocument_ReturnsNull()
	{
		TextEditor editor = WPFTestHost.CreateEditor("");

		Assert.IsNull(editor.TextArea.GetWordFromOffset(0));
	}

	[TestMethod]
	public void CreateCaretLocation_LineAndColumnBeyondDocument_ClampsToLastLineEnd()
	{
		TextEditor editor = WPFTestHost.CreateEditor("one\r\ntwo\r\nthree");

		NavigationLocation location = TextAreaNavigationOperations.CreateCaretLocation(editor.TextArea, "path.txt", new TextLocation(99, 99));

		Assert.AreEqual(15, location.CaretOffset);
		Assert.AreEqual(15, location.SelectionStart);
		Assert.AreEqual(0, location.SelectionLength);
		Assert.AreEqual(99, location.PreferredDocumentLine);
	}

	[TestMethod]
	public void CreateCaretLocation_ZeroBasedLineAndColumn_ClampsToFirstLineStart()
	{
		TextEditor editor = WPFTestHost.CreateEditor("one\r\ntwo\r\nthree");

		NavigationLocation location = TextAreaNavigationOperations.CreateCaretLocation(editor.TextArea, "path.txt", new TextLocation(0, 0));

		Assert.AreEqual(0, location.CaretOffset);
		Assert.AreEqual(0, location.SelectionStart);
		Assert.AreEqual(0, location.PreferredDocumentLine);
	}

	[TestMethod]
	public void CreateRangeLocation_ClampsEndpointsAndSpansLines()
	{
		TextEditor editor = WPFTestHost.CreateEditor("aaaa\r\nbbbb\r\ncccc");

		NavigationLocation location = TextAreaNavigationOperations.CreateRangeLocation(
			editor.TextArea, "path.txt", new TextLocation(2, 2), new TextLocation(3, 3));

		Assert.AreEqual(7, location.CaretOffset);
		Assert.AreEqual(7, location.SelectionStart);
		Assert.AreEqual(7, location.SelectionLength);
		Assert.AreEqual(2, location.PreferredDocumentLine);
	}

	[TestMethod]
	public void CreateRangeLocation_ReversedEndpoints_ProducesEmptySelection()
	{
		TextEditor editor = WPFTestHost.CreateEditor("aaaa\r\nbbbb\r\ncccc");

		NavigationLocation location = TextAreaNavigationOperations.CreateRangeLocation(
			editor.TextArea, "path.txt", new TextLocation(3, 3), new TextLocation(2, 2));

		Assert.AreEqual(14, location.CaretOffset);
		Assert.AreEqual(14, location.SelectionStart);
		Assert.AreEqual(0, location.SelectionLength);
	}

	[TestMethod]
	public void ApplyLocation_ClampsOutOfBoundsOffsets()
	{
		TextEditor editor = WPFTestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		var location = new NavigationLocation("path.txt", 99, 90, 50, 99);

		editor.TextArea.ApplyLocation(location);

		Assert.AreEqual(15, editor.CaretOffset);
		Assert.AreEqual(15, editor.SelectionStart);
		Assert.AreEqual(0, editor.SelectionLength);
	}

	[TestMethod]
	public void ApplyLocation_SelectionWithCaretAtSelectionStart_KeepsCaretAtSelectionStart()
	{
		TextEditor editor = WPFTestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		var location = new NavigationLocation("path.txt", 5, 5, 3, 2);

		editor.TextArea.ApplyLocation(location);

		// Let AvalonEdit's deferred selection validation run; the caret is inside the
		// selection, so the selection must survive.
		WPFTestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual("two", editor.SelectedText);
		Assert.AreEqual(5, editor.SelectionStart);
		Assert.AreEqual(3, editor.SelectionLength);
		Assert.AreEqual(5, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyLocation_SelectionWithCaretAtSelectionEnd_KeepsCaretAtSelectionEnd()
	{
		TextEditor editor = WPFTestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		var location = new NavigationLocation("path.txt", 8, 5, 3, 2);

		editor.TextArea.ApplyLocation(location);

		WPFTestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual("two", editor.SelectedText);
		Assert.AreEqual(5, editor.SelectionStart);
		Assert.AreEqual(3, editor.SelectionLength);
		Assert.AreEqual(8, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyLocation_CaretOutsideSelection_ClampsToNearestSelectionEdge()
	{
		TextEditor editor = WPFTestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		var location = new NavigationLocation("path.txt", 99, 5, 3, 2);

		editor.TextArea.ApplyLocation(location);

		WPFTestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		// The recorded caret is beyond the selection and clamps to its end.
		Assert.AreEqual("two", editor.SelectedText);
		Assert.AreEqual(8, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyLocation_CollapsedSelection_UsesRecordedCaretOffset()
	{
		TextEditor editor = WPFTestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		var location = new NavigationLocation("path.txt", 7, 0, 0, 2);

		editor.TextArea.ApplyLocation(location);

		WPFTestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(7, editor.CaretOffset);
		Assert.AreEqual(7, editor.SelectionStart);
		Assert.AreEqual(0, editor.SelectionLength);
	}

	[TestMethod]
	public void ApplyLocation_OffsetsInsideCrLfTerminators_SnapToThePrecedingLineEnd()
	{
		TextEditor editor = WPFTestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		// Offsets 4 and 9 sit between the '\r' and the '\n' of the two terminators, where a later
		// insertion would split the pair; each snaps to the end of its preceding line.
		editor.TextArea.ApplyLocation(new NavigationLocation("path.txt", 4, 4, 0, null));

		WPFTestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(3, editor.CaretOffset);
		Assert.AreEqual(3, editor.SelectionStart);
		Assert.AreEqual(0, editor.SelectionLength);

		editor.TextArea.ApplyLocation(new NavigationLocation("path.txt", 9, 9, 0, null));

		WPFTestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(8, editor.CaretOffset);
		Assert.AreEqual(8, editor.SelectionStart);
		Assert.AreEqual(0, editor.SelectionLength);
	}

	[TestMethod]
	public void ApplyLocation_RoundTripsCapturedReversedSelection()
	{
		TextEditor editor = WPFTestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		// A reversed selection location (the caret sits at the selection start) is captured by the
		// navigation source; disturb the editor and restore the captured location.
		var captured = new NavigationLocation("path.txt", 5, 5, 3, 2);

		editor.Select(0, 0);
		editor.CaretOffset = 0;

		editor.TextArea.ApplyLocation(captured);

		WPFTestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual("two", editor.SelectedText);
		Assert.AreEqual(5, editor.SelectionStart);
		Assert.AreEqual(3, editor.SelectionLength);
		Assert.AreEqual(5, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyLocation_SelectionWithCaretInsideSelection_KeepsRecordedCaret()
	{
		TextEditor editor = WPFTestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		var location = new NavigationLocation("path.txt", 6, 5, 3, 2);

		editor.TextArea.ApplyLocation(location);

		WPFTestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		// A caret within the selection bounds is kept where it was recorded.
		Assert.AreEqual("two", editor.SelectedText);
		Assert.AreEqual(6, editor.CaretOffset);
	}

	[TestMethod]
	public void GetWordFromOffset_NegativeOffset_ClampsToDocumentStart()
	{
		var editor = WPFTestHost.CreateEditor("alpha beta");

		Assert.AreEqual("alpha", editor.TextArea.GetWordFromOffset(2));
		Assert.AreEqual("alpha", editor.TextArea.GetWordFromOffset(0));
		Assert.AreEqual("alpha", editor.TextArea.GetWordFromOffset(-10));
	}

	[TestMethod]
	public void GetWordFromOffset_OffsetBeyondDocument_ClampsToDocumentEnd()
	{
		var editor = WPFTestHost.CreateEditor("alpha beta");

		Assert.AreEqual("beta", editor.TextArea.GetWordFromOffset(8));
		Assert.AreEqual("beta", editor.TextArea.GetWordFromOffset(editor.Document.TextLength));
		Assert.AreEqual("beta", editor.TextArea.GetWordFromOffset(1000));
	}

	[TestMethod]
	public void GetWordFromOffset_OffsetOnSeparator_ReturnsNull()
	{
		var editor = WPFTestHost.CreateEditor("foo(bar, baz);");

		Assert.AreEqual("bar", editor.TextArea.GetWordFromOffset(4));
		Assert.IsNull(editor.TextArea.GetWordFromOffset(3));
		Assert.IsNull(editor.TextArea.GetWordFromOffset(7));
		Assert.IsNull(editor.TextArea.GetWordFromOffset(8));
		Assert.IsNull(editor.TextArea.GetWordFromOffset(12));
		Assert.IsNull(editor.TextArea.GetWordFromOffset(13));
	}

	[TestMethod]
	public void GetWordFromOffset_OffsetInLeadingWhitespace_ReturnsNull()
	{
		var editor = WPFTestHost.CreateEditor("  alpha");

		Assert.IsNull(editor.TextArea.GetWordFromOffset(0));
		Assert.IsNull(editor.TextArea.GetWordFromOffset(1));
		Assert.AreEqual("alpha", editor.TextArea.GetWordFromOffset(2));
	}

	[TestMethod]
	public void GetWordFromOffset_WordWithDigitsAndUnderscores_ReturnsWholeWord()
	{
		var editor = WPFTestHost.CreateEditor("ident_1 v2");

		Assert.AreEqual("ident_1", editor.TextArea.GetWordFromOffset(1));
		Assert.AreEqual("ident_1", editor.TextArea.GetWordFromOffset(4));
		Assert.AreEqual("ident_1", editor.TextArea.GetWordFromOffset(6));
		Assert.AreEqual("v2", editor.TextArea.GetWordFromOffset(8));
		Assert.AreEqual("v2", editor.TextArea.GetWordFromOffset(editor.Document.TextLength));
		Assert.IsNull(editor.TextArea.GetWordFromOffset(7));
	}

	[TestMethod]
	public void GetWordFromOffset_RunStartingWithADigit_ReturnsTheWholeRun()
	{
		var editor = WPFTestHost.CreateEditor("123abc");

		// The walk consults only the part-character rule, so a run that could not start an identifier
		// under most language rules is still returned as one word.
		Assert.AreEqual("123abc", editor.TextArea.GetWordFromOffset(0));
	}

	[TestMethod]
	public void GetWordFromOffset_OffsetAtDocumentEndAfterTerminator_ReturnsNull()
	{
		var editor = WPFTestHost.CreateEditor("abc\n");

		// The probe lands on the trailing terminator, which cannot be part of a word.
		Assert.IsNull(editor.TextArea.GetWordFromOffset(editor.Document.TextLength));
	}

	[TestMethod]
	public void ApplyLocation_PreferredDocumentLineBeyondDocument_ClampsToDocumentEnd()
	{
		TextEditor editor = WPFTestHost.CreateEditor(string.Join("\r\n", Enumerable.Repeat("line", 400)));
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		editor.ScrollToLine(1);
		editor.UpdateLayout();

		double initialVerticalOffset = editor.TextArea.TextView.VerticalOffset;

		// Premise: the clamped scroll target must start out of view for the assertion to mean anything.
		Assert.IsFalse(IsLineVisible(editor, 400), "Expected the last line to be outside the visible range.");

		// A preferred line beyond the document is clamped to the last line instead of throwing.
		editor.TextArea.ApplyLocation(new NavigationLocation("path.txt", 0, 0, 0, 9999), focus: false);

		editor.UpdateLayout();

		Assert.IsGreaterThan(initialVerticalOffset, editor.TextArea.TextView.VerticalOffset);
		Assert.IsTrue(IsLineVisible(editor, 400), "Expected the clamped last line to be scrolled into view.");
	}

	[TestMethod]
	public void ApplyLocation_WithoutPreferredDocumentLine_ScrollsToTheSelectionStartLine()
	{
		TextEditor editor = WPFTestHost.CreateEditor(string.Join("\r\n", Enumerable.Repeat("line", 400)));
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		editor.ScrollToLine(1);
		editor.UpdateLayout();

		double initialVerticalOffset = editor.TextArea.TextView.VerticalOffset;

		Assert.IsFalse(IsLineVisible(editor, 200), "Expected line 200 to be outside the visible range.");

		// Without a preferred line, the line of the selection start is the scroll target.
		int selectionStart = editor.Document.GetLineByNumber(200).Offset;

		editor.TextArea.ApplyLocation(new NavigationLocation("path.txt", selectionStart, selectionStart, 0, null), focus: false);

		editor.UpdateLayout();

		Assert.IsGreaterThan(initialVerticalOffset, editor.TextArea.TextView.VerticalOffset);
		Assert.IsTrue(IsLineVisible(editor, 200), "Expected the selection start line to be scrolled into view.");
	}

	[TestMethod]
	public void ApplyLocation_WithoutFocus_StillScrollsToPreferredDocumentLine()
	{
		TextEditor editor = WPFTestHost.CreateEditor(string.Join("\r\n", Enumerable.Repeat("line", 400)));
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		editor.ScrollToLine(1);
		editor.UpdateLayout();

		double initialVerticalOffset = editor.TextArea.TextView.VerticalOffset;

		Assert.IsFalse(IsLineVisible(editor, 200), "Expected line 200 to be outside the visible range.");

		editor.TextArea.ApplyLocation(new NavigationLocation("path.txt", 0, 0, 0, 200), focus: false);

		editor.UpdateLayout();

		Assert.IsGreaterThan(initialVerticalOffset, editor.TextArea.TextView.VerticalOffset);
		Assert.AreEqual(0, editor.CaretOffset);
		Assert.IsTrue(IsLineVisible(editor, 200), "Expected the preferred line to be scrolled into view even without focus.");
	}

	[TestMethod]
	public void ApplyLocation_BareTextAreaHost_ScrollsThePreferredDocumentLineIntoView()
	{
		var textArea = new TextArea
		{
			Document = new TextDocument(string.Join("\r\n", Enumerable.Repeat(new string('x', 2000), 400)))
		};
		var scrollViewer = new ScrollViewer
		{
			Content = textArea,
			CanContentScroll = true,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
		};

		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(scrollViewer);

		scrollViewer.ScrollToHorizontalOffset(500);
		textArea.UpdateLayout();

		double initialVerticalOffset = textArea.TextView.VerticalOffset;
		double initialHorizontalOffset = textArea.TextView.HorizontalOffset;

		// Premise: the scroll target must start out of view and the view must be panned right for
		// the assertions to mean anything.
		Assert.IsFalse(IsLineVisible(textArea, 400), "Expected the last line to be outside the visible range.");
		Assert.IsGreaterThan(0.0, initialHorizontalOffset);

		// A text area without a TextEditor wrapper is a supported composition point when the host
		// supplies the scrolling - here a content-scrolling viewer; the location is applied and the
		// preferred line is scrolled into view without panning horizontally.
		textArea.ApplyLocation(new NavigationLocation("path.txt", 0, 0, 0, 400), focus: false);

		textArea.UpdateLayout();

		Assert.IsGreaterThan(initialVerticalOffset, textArea.TextView.VerticalOffset);
		Assert.IsTrue(IsLineVisible(textArea, 400), "Expected the preferred line to be scrolled into view.");
		Assert.AreEqual(initialHorizontalOffset, textArea.TextView.HorizontalOffset, 0.001);
	}

	[TestMethod]
	public void ApplyLocation_SelectionStartInsideCrLfInterior_SnapsStartOutOfThePair()
	{
		TextEditor editor = WPFTestHost.CreateEditor("one\r\ntwo");
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		// The selection start (offset 4) sits between the '\r' and the '\n'; it snaps back to the end
		// of the preceding line, so the selection begins before the terminator instead of inside it.
		editor.TextArea.ApplyLocation(new NavigationLocation("path.txt", 3, 4, 3, null));

		WPFTestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual("\r\nt", editor.SelectedText);
		Assert.AreEqual(3, editor.SelectionStart);
		Assert.AreEqual(3, editor.SelectionLength);
		Assert.AreEqual(3, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyLocation_SelectionEndInsideCrLfInterior_SnapsEndOutOfThePair()
	{
		TextEditor editor = WPFTestHost.CreateEditor("one\r\ntwo");
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		// The selection ends between the '\r' and the '\n'; the snapped end shortens the selection
		// to the preceding line's content.
		editor.TextArea.ApplyLocation(new NavigationLocation("path.txt", 0, 0, 4, null));

		WPFTestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual("one", editor.SelectedText);
		Assert.AreEqual(0, editor.SelectionStart);
		Assert.AreEqual(3, editor.SelectionLength);
		Assert.AreEqual(0, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyLocation_SelectionCollapsesWhenTheSnappedEndFallsBeforeTheStart()
	{
		TextEditor editor = WPFTestHost.CreateEditor("one\r\ntwo");
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		// The selection is the single '\r'; its end snaps out of the pair interior and lands on the
		// snapped start, so the selection collapses at the recorded caret.
		editor.TextArea.ApplyLocation(new NavigationLocation("path.txt", 3, 3, 1, null));

		WPFTestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(0, editor.SelectionLength);
		Assert.AreEqual(3, editor.SelectionStart);
		Assert.AreEqual(3, editor.CaretOffset);
	}

	[TestMethod]
	public void GetWordFromOffset_TextAreaWithoutDocument_Throws()
	{
		// A bare text area has no document assigned; a TextEditor always assigns one.
		var textArea = new TextArea();

		Assert.ThrowsExactly<InvalidOperationException>(() => textArea.GetWordFromOffset(0));
	}

	[TestMethod]
	public void CreateCaretLocation_TextAreaWithoutDocument_Throws()
	{
		var textArea = new TextArea();

		Assert.ThrowsExactly<InvalidOperationException>(
			() => TextAreaNavigationOperations.CreateCaretLocation(textArea, "path.txt", new TextLocation(1, 1)));
	}

	[TestMethod]
	public void CreateRangeLocation_TextAreaWithoutDocument_Throws()
	{
		var textArea = new TextArea();

		Assert.ThrowsExactly<InvalidOperationException>(
			() => TextAreaNavigationOperations.CreateRangeLocation(
				textArea, "path.txt", new TextLocation(1, 1), new TextLocation(1, 2)));
	}

	[TestMethod]
	public void ApplyLocation_TextAreaWithoutDocument_Throws()
	{
		var textArea = new TextArea();

		Assert.ThrowsExactly<InvalidOperationException>(
			() => textArea.ApplyLocation(new NavigationLocation("path.txt", 0, 0, 0, 1)));
	}

	[TestMethod]
	public void ApplyLocation_NegativeOffsets_ClampToTheDocumentStart()
	{
		TextEditor editor = WPFTestHost.CreateEditor("one\r\ntwo");
		using HostWindow hostWindow = WPFTestHost.ShowInHostWindow(editor);

		editor.TextArea.ApplyLocation(new NavigationLocation("path.txt", -5, -5, 0, null));

		WPFTestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(0, editor.CaretOffset);
		Assert.AreEqual(0, editor.SelectionStart);
		Assert.AreEqual(0, editor.SelectionLength);
	}
}