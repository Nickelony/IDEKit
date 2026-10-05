#if AVALONIAEDIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Navigation;
using Nickelony.IDEKit.Core.Identifiers;
using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.Core.Text;
using static Nickelony.IDEKit.AvaloniaEdit.Tests.DocumentTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Navigation;
using Nickelony.IDEKit.Core.Identifiers;
using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.Core.Text;
using static Nickelony.IDEKit.AvalonEdit.Tests.DocumentTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class TextAreaExtensionsTests
{
	[TestMethod]
	public void GetWordFromOffset_OffsetInsideWord_ReturnsWord()
	{
		TextEditor editor = TestHost.CreateEditor("one two three");
		Assert.AreEqual("two", editor.TextArea.GetWordFromOffset(5));
	}

	[TestMethod]
	public void GetWordFromOffset_OffsetInsideFirstWord_ReturnsFirstWord()
	{
		TextEditor editor = TestHost.CreateEditor("one two three");
		Assert.AreEqual("one", editor.TextArea.GetWordFromOffset(1));
	}

	[TestMethod]
	public void GetWordFromOffset_CustomPolicy_UsesTheSuppliedCharacterRules()
	{
		TextEditor editor = TestHost.CreateEditor("a-b");

		var policy = IdentifierCharacterPolicy.Create(static character => char.IsLetterOrDigit(character) || character == '-');

		// The custom policy treats the hyphen as a word character, so the whole token is returned
		// where the default policy would stop at the separator.
		Assert.AreEqual("a-b", editor.TextArea.GetWordFromOffset(2, policy));
		Assert.AreEqual("b", editor.TextArea.GetWordFromOffset(2));
	}

	[TestMethod]
	public void GetWordFromOffset_EmptyDocument_ReturnsNull()
	{
		TextEditor editor = TestHost.CreateEditor("");
		Assert.IsNull(editor.TextArea.GetWordFromOffset(0));
	}

	[TestMethod]
	public void TryGetOffsetFromPoint_InsideVisualLine_MapsToThatLine()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		TextView textView = editor.TextArea.TextView;

		Assert.IsNotEmpty(textView.VisualLines);

		VisualLine secondLine = textView.VisualLines.First(line => line.FirstDocumentLine.LineNumber == 2);
		double lineTop = secondLine.GetTextLineVisualYPosition(secondLine.TextLines[0], VisualYPosition.TextTop);

		Point textAreaPoint = TestHost.TranslatePointTo(textView, new Point(2.0, lineTop + 2.0), editor.TextArea);

		bool resolved = editor.TextArea.TryGetOffsetFromPoint(textAreaPoint, out int offset);

		Assert.IsTrue(resolved);
		Assert.AreEqual(2, editor.Document.GetLineByOffset(offset).LineNumber);
	}

	[TestMethod]
	public void TryGetOffsetFromPoint_RightAfterARedraw_MapsToThatLine()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		TextView textView = editor.TextArea.TextView;

		Assert.IsNotEmpty(textView.VisualLines);

		VisualLine secondLine = textView.VisualLines.First(line => line.FirstDocumentLine.LineNumber == 2);
		double lineTop = secondLine.GetTextLineVisualYPosition(secondLine.TextLines[0], VisualYPosition.TextTop);

		Point textAreaPoint = TestHost.TranslatePointTo(textView, new Point(2.0, lineTop + 2.0), editor.TextArea);

		// A redraw, such as a semantic-token update, clears the visual lines until the next layout pass runs.
		textView.Redraw();

		Assert.IsFalse(textView.VisualLinesValid, "Premise: the redraw must leave the visual lines invalid.");

		bool resolved = editor.TextArea.TryGetOffsetFromPoint(textAreaPoint, out int offset);

		Assert.IsTrue(resolved);
		Assert.AreEqual(2, editor.Document.GetLineByOffset(offset).LineNumber);
	}

	[TestMethod]
	public void TryGetOffsetFromPoint_OutsideVisualLines_ReturnsFalse()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		Point textAreaPoint = TestHost.TranslatePointTo(editor.TextArea.TextView, new Point(2.0, 100000.0), editor.TextArea);

		bool resolved = editor.TextArea.TryGetOffsetFromPoint(textAreaPoint, out int offset);

		Assert.IsFalse(resolved);
		Assert.AreEqual(0, offset);
	}

	[TestMethod]
	public void TryGetOffsetFromPoint_PastLineEnd_ClampsToLineEnd()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		TextView textView = editor.TextArea.TextView;

		Assert.IsNotEmpty(textView.VisualLines);

		VisualLine line = textView.VisualLines[0];
		double lineTop = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.TextTop);

		// The point is beyond the end of the text but still inside the rendered viewport, so the offset clamps to the line end.
		Point textAreaPoint = TestHost.TranslatePointTo(textView, new Point(500.0, lineTop + 2.0), editor.TextArea);

		bool resolved = editor.TextArea.TryGetOffsetFromPoint(textAreaPoint, out int offset);

		Assert.IsTrue(resolved);
		Assert.AreEqual(editor.Document.TextLength, offset);
	}

	[TestMethod]
	public void TryGetTextOffsetFromPoint_OverTheText_MapsToTheOffsetUnderThePoint()
	{
		TextEditor editor = TestHost.CreateEditor("first line\r\nsecond");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		// A pixel past the start of the second character, which the mapping rounds to the boundary before it on either engine.
		VisualLine firstLine = editor.TextArea.TextView.VisualLines[0];
		double x = firstLine.GetVisualPosition(1, VisualYPosition.TextTop).X + 1.0;

		Assert.IsTrue(editor.TextArea.TryGetTextOffsetFromPoint(PointOnFirstLine(editor, x), out int offset));
		Assert.AreEqual(1, offset);
	}

	[TestMethod]
	public void TryGetTextOffsetFromPoint_PastTheEndOfALine_ReturnsFalse()
	{
		TextEditor editor = TestHost.CreateEditor("first line\r\nsecond");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		// The clamped mapping reports the end of the line there, which would pick its last word from the blank space after it.
		Assert.IsFalse(editor.TextArea.TryGetTextOffsetFromPoint(PointOnFirstLine(editor, x: 400.0), out int offset));
		Assert.AreEqual(0, offset);
	}

	[TestMethod]
	public void TryGetTextOffsetFromPoint_OverALeftMargin_ReturnsFalse()
	{
		TextEditor editor = TestHost.CreateEditor("first line\r\nsecond");
		editor.ShowLineNumbers = true;
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		// The point lies over the line numbers, left of the text view, where the clamped mapping reports the start of the line and its first word.
		Assert.IsFalse(editor.TextArea.TryGetTextOffsetFromPoint(PointOnFirstLine(editor, x: -4.0), out int offset));
		Assert.AreEqual(0, offset);
	}

	[TestMethod]
	public void TryGetOffsetFromPoint_TextAreaWithoutDocument_ReturnsFalse()
	{
		TextEditor editor = TestHost.CreateEditor("abc");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		editor.TextArea.Document = null!;

		bool resolved = editor.TextArea.TryGetOffsetFromPoint(new Point(2.0, 2.0), out int offset);

		Assert.IsFalse(resolved);
		Assert.AreEqual(0, offset);
	}

	[TestMethod]
	public void TryGetOffsetFromPoint_UnhostedTextArea_ReturnsFalse()
	{
		// A text area that was never loaded has no rendered view to map against; the Try contract reports a failed mapping.
		TextEditor editor = TestHost.CreateEditor("abc");

		bool resolved = editor.TextArea.TryGetOffsetFromPoint(new Point(0.0, 0.0), out int offset);

		Assert.IsFalse(resolved);
		Assert.AreEqual(0, offset);
	}

	[TestMethod]
	public void TryGetOffsetFromPoint_NullTextArea_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => TextAreaExtensions.TryGetOffsetFromPoint(null!, new Point(0.0, 0.0), out _));

	[TestMethod]
	public void CreateCaretLocation_LineAndColumnBeyondDocument_ClampsToLastLineEnd()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");

		NavigationLocation location = TextAreaExtensions.CreateCaretLocation(editor.TextArea, "path.txt", new TextLocation(99, 99));

		Assert.AreEqual(15, location.CaretOffset);
		Assert.AreEqual(new TextRange(15, 0), location.Selection);
		Assert.AreEqual(99, location.PreferredDocumentLine);
	}

	[TestMethod]
	public void CreateCaretLocation_ZeroBasedLineAndColumn_ClampsToFirstLineStart()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");

		NavigationLocation location = TextAreaExtensions.CreateCaretLocation(editor.TextArea, "path.txt", new TextLocation(0, 0));

		Assert.AreEqual(0, location.CaretOffset);
		Assert.AreEqual(new TextRange(0, 0), location.Selection);
		Assert.AreEqual(0, location.PreferredDocumentLine);
	}

	[TestMethod]
	public void CreateRangeLocation_ClampsEndpointsAndSpansLines()
	{
		TextEditor editor = TestHost.CreateEditor("aaaa\r\nbbbb\r\ncccc");

		NavigationLocation location = TextAreaExtensions.CreateRangeLocation(
			textArea: editor.TextArea,
			documentId: "path.txt",
			start: new TextLocation(2, 2),
			end: new TextLocation(3, 3)
		);

		Assert.AreEqual(7, location.CaretOffset);
		Assert.AreEqual(new TextRange(7, 7), location.Selection);
		Assert.AreEqual(2, location.PreferredDocumentLine);
	}

	[TestMethod]
	public void CreateRangeLocation_ReversedEndpoints_ProducesEmptySelection()
	{
		TextEditor editor = TestHost.CreateEditor("aaaa\r\nbbbb\r\ncccc");

		NavigationLocation location = TextAreaExtensions.CreateRangeLocation(
			textArea: editor.TextArea,
			documentId: "path.txt",
			start: new TextLocation(3, 3),
			end: new TextLocation(2, 2)
		);

		Assert.AreEqual(14, location.CaretOffset);
		Assert.AreEqual(new TextRange(14, 0), location.Selection);
	}

	[TestMethod]
	public void ApplyLocation_ClampsOutOfBoundsOffsets()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var location = new NavigationLocation(DocumentId: "path.txt", CaretOffset: 99, Selection: new TextRange(90, 50), PreferredDocumentLine: 99);

		editor.TextArea.ApplyLocation(location);

		Assert.AreEqual(15, editor.CaretOffset);
		Assert.AreEqual(15, editor.SelectionStart);
		Assert.AreEqual(0, editor.SelectionLength);
	}

	[TestMethod]
	public void ApplyLocation_SelectionWithCaretAtSelectionStart_KeepsCaretAtSelectionStart()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var location = new NavigationLocation(DocumentId: "path.txt", CaretOffset: 5, Selection: new TextRange(5, 3), PreferredDocumentLine: 2);

		editor.TextArea.ApplyLocation(location);

		// Let the engine's deferred selection validation run; the caret is inside the
		// selection, so the selection must survive.
		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual("two", editor.SelectedText);
		Assert.AreEqual(5, editor.SelectionStart);
		Assert.AreEqual(3, editor.SelectionLength);
		Assert.AreEqual(5, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyLocation_SelectionWithCaretAtSelectionEnd_KeepsCaretAtSelectionEnd()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var location = new NavigationLocation(DocumentId: "path.txt", CaretOffset: 8, Selection: new TextRange(5, 3), PreferredDocumentLine: 2);

		editor.TextArea.ApplyLocation(location);

		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual("two", editor.SelectedText);
		Assert.AreEqual(5, editor.SelectionStart);
		Assert.AreEqual(3, editor.SelectionLength);
		Assert.AreEqual(8, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyLocation_CaretOutsideSelection_ClampsToNearestSelectionEdge()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var location = new NavigationLocation(DocumentId: "path.txt", CaretOffset: 99, Selection: new TextRange(5, 3), PreferredDocumentLine: 2);

		editor.TextArea.ApplyLocation(location);

		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		// The recorded caret is beyond the selection and clamps to its end.
		Assert.AreEqual("two", editor.SelectedText);
		Assert.AreEqual(8, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyLocation_CollapsedSelection_UsesRecordedCaretOffset()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var location = new NavigationLocation(DocumentId: "path.txt", CaretOffset: 7, Selection: new TextRange(0, 0), PreferredDocumentLine: 2);

		editor.TextArea.ApplyLocation(location);

		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(7, editor.CaretOffset);
		Assert.AreEqual(7, editor.SelectionStart);
		Assert.AreEqual(0, editor.SelectionLength);
	}

	[TestMethod]
	public void ApplyLocation_OffsetsInsideCrLfTerminators_SnapToThePrecedingLineEnd()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		// Offsets 4 and 9 sit between the '\r' and the '\n' of the two terminators, where a later
		// insertion would split the pair; each snaps to the end of its preceding line.
		editor.TextArea.ApplyLocation(
			new NavigationLocation(DocumentId: "path.txt", CaretOffset: 4, Selection: new TextRange(4, 0), PreferredDocumentLine: null)
		);

		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(3, editor.CaretOffset);
		Assert.AreEqual(3, editor.SelectionStart);
		Assert.AreEqual(0, editor.SelectionLength);

		editor.TextArea.ApplyLocation(
			new NavigationLocation(DocumentId: "path.txt", CaretOffset: 9, Selection: new TextRange(9, 0), PreferredDocumentLine: null)
		);

		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(8, editor.CaretOffset);
		Assert.AreEqual(8, editor.SelectionStart);
		Assert.AreEqual(0, editor.SelectionLength);
	}

	[TestMethod]
	public void ApplyLocation_RoundTripsCapturedReversedSelection()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		// The location records a reversed selection (the caret sits at the selection start); disturb the
		// editor and restore that location.
		var captured = new NavigationLocation(DocumentId: "path.txt", CaretOffset: 5, Selection: new TextRange(5, 3), PreferredDocumentLine: 2);

		editor.Select(0, 0);
		editor.CaretOffset = 0;

		editor.TextArea.ApplyLocation(captured);

		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual("two", editor.SelectedText);
		Assert.AreEqual(5, editor.SelectionStart);
		Assert.AreEqual(3, editor.SelectionLength);
		Assert.AreEqual(5, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyLocation_SelectionWithCaretInsideSelection_KeepsRecordedCaret()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var location = new NavigationLocation(DocumentId: "path.txt", CaretOffset: 6, Selection: new TextRange(5, 3), PreferredDocumentLine: 2);

		editor.TextArea.ApplyLocation(location);

		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		// A caret within the selection bounds is kept where it was recorded.
		Assert.AreEqual("two", editor.SelectedText);
		Assert.AreEqual(6, editor.CaretOffset);
	}

	[TestMethod]
	public void GetWordFromOffset_NegativeOffset_ClampsToDocumentStart()
	{
		TextEditor editor = TestHost.CreateEditor("alpha beta");

		Assert.AreEqual("alpha", editor.TextArea.GetWordFromOffset(2));
		Assert.AreEqual("alpha", editor.TextArea.GetWordFromOffset(0));
		Assert.AreEqual("alpha", editor.TextArea.GetWordFromOffset(-10));
	}

	[TestMethod]
	public void GetWordFromOffset_OffsetBeyondDocument_ClampsToDocumentEnd()
	{
		TextEditor editor = TestHost.CreateEditor("alpha beta");

		Assert.AreEqual("beta", editor.TextArea.GetWordFromOffset(8));
		Assert.AreEqual("beta", editor.TextArea.GetWordFromOffset(editor.Document.TextLength));
		Assert.AreEqual("beta", editor.TextArea.GetWordFromOffset(1000));
	}

	[TestMethod]
	public void GetWordFromOffset_OffsetOnSeparator_ReturnsNull()
	{
		TextEditor editor = TestHost.CreateEditor("foo(bar, baz);");

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
		TextEditor editor = TestHost.CreateEditor("  alpha");

		Assert.IsNull(editor.TextArea.GetWordFromOffset(0));
		Assert.IsNull(editor.TextArea.GetWordFromOffset(1));
		Assert.AreEqual("alpha", editor.TextArea.GetWordFromOffset(2));
	}

	[TestMethod]
	public void GetWordFromOffset_WordWithDigitsAndUnderscores_ReturnsWholeWord()
	{
		TextEditor editor = TestHost.CreateEditor("ident_1 v2");

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
		TextEditor editor = TestHost.CreateEditor("123abc");

		// The walk consults only the part-character rule, so a run that could not start an identifier
		// under most language rules is still returned as one word.
		Assert.AreEqual("123abc", editor.TextArea.GetWordFromOffset(0));
	}

	[TestMethod]
	public void GetWordFromOffset_OffsetAtDocumentEndAfterTerminator_ReturnsNull()
	{
		TextEditor editor = TestHost.CreateEditor("abc\n");

		// The probe lands on the trailing terminator, which cannot be part of a word.
		Assert.IsNull(editor.TextArea.GetWordFromOffset(editor.Document.TextLength));
	}

	[TestMethod]
	public void ApplyLocation_PreferredDocumentLineBeyondDocument_ClampsToDocumentEnd()
	{
		TextEditor editor = TestHost.CreateEditor(string.Join("\r\n", Enumerable.Repeat("line", 400)));
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		editor.ScrollToLine(1);
		editor.UpdateLayout();

		double initialVerticalOffset = editor.TextArea.TextView.VerticalOffset;

		// Premise: the clamped scroll target must start out of view for the assertion to mean anything.
		Assert.IsFalse(IsLineVisible(editor, 400), "Expected the last line to be outside the visible range.");

		// A preferred line beyond the document is clamped to the last line instead of throwing.
		editor.TextArea.ApplyLocation(
			new NavigationLocation(DocumentId: "path.txt", CaretOffset: 0, Selection: new TextRange(0, 0), PreferredDocumentLine: 9999),
			focus: EditorFocusMode.LeaveUnchanged
		);

		editor.UpdateLayout();

		Assert.IsGreaterThan(initialVerticalOffset, editor.TextArea.TextView.VerticalOffset);
		Assert.IsTrue(IsLineVisible(editor, 400), "Expected the clamped last line to be scrolled into view.");
	}

	[TestMethod]
	public void ApplyLocation_WithoutPreferredDocumentLine_ScrollsToTheSelectionStartLine()
	{
		TextEditor editor = TestHost.CreateEditor(string.Join("\r\n", Enumerable.Repeat("line", 400)));
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		editor.ScrollToLine(1);
		editor.UpdateLayout();

		double initialVerticalOffset = editor.TextArea.TextView.VerticalOffset;

		Assert.IsFalse(IsLineVisible(editor, 200), "Expected line 200 to be outside the visible range.");

		// Without a preferred line, the line of the selection start is the scroll target.
		int selectionStart = editor.Document.GetLineByNumber(200).Offset;

		editor.TextArea.ApplyLocation(
			new NavigationLocation(
				DocumentId: "path.txt",
				CaretOffset: selectionStart,
				Selection: new TextRange(selectionStart, 0),
				PreferredDocumentLine: null
			),

			focus: EditorFocusMode.LeaveUnchanged
		);

		editor.UpdateLayout();

		Assert.IsGreaterThan(initialVerticalOffset, editor.TextArea.TextView.VerticalOffset);
		Assert.IsTrue(IsLineVisible(editor, 200), "Expected the selection start line to be scrolled into view.");
	}

	[TestMethod]
	public void ApplyLocation_WithoutFocus_StillScrollsToPreferredDocumentLine()
	{
		TextEditor editor = TestHost.CreateEditor(string.Join("\r\n", Enumerable.Repeat("line", 400)));
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		editor.ScrollToLine(1);
		editor.UpdateLayout();

		double initialVerticalOffset = editor.TextArea.TextView.VerticalOffset;

		Assert.IsFalse(IsLineVisible(editor, 200), "Expected line 200 to be outside the visible range.");

		editor.TextArea.ApplyLocation(
			new NavigationLocation(DocumentId: "path.txt", CaretOffset: 0, Selection: new TextRange(0, 0), PreferredDocumentLine: 200),
			focus: EditorFocusMode.LeaveUnchanged
		);

		editor.UpdateLayout();

		Assert.IsGreaterThan(initialVerticalOffset, editor.TextArea.TextView.VerticalOffset);
		Assert.AreEqual(0, editor.CaretOffset);
		Assert.IsTrue(IsLineVisible(editor, 200), "Expected the preferred line to be scrolled into view even without focus.");
	}

	[TestMethod]
	public void ApplyLocation_BareTextAreaHost_ScrollsThePreferredDocumentLineIntoView()
	{
		var textArea = new TextArea { Document = new TextDocument(string.Join("\r\n", Enumerable.Repeat(new string('x', 2000), 400))) };
#if AVALONIAEDIT
		// Avalonia's ScrollViewer has no CanContentScroll flag; it delegates to a logical-scrollable child,
		// so a bare text area composes with it.
#endif
		var scrollViewer = new ScrollViewer
		{
			Content = textArea,
#if AVALONIAEDIT
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
#else
			CanContentScroll = true,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
#endif
		};

		using HostWindow hostWindow = TestHost.ShowInHostWindow(scrollViewer);

#if AVALONIAEDIT
		scrollViewer.Offset = scrollViewer.Offset.WithX(500);
#else
		scrollViewer.ScrollToHorizontalOffset(500);
#endif
		textArea.UpdateLayout();

		double initialVerticalOffset = textArea.TextView.VerticalOffset;
		double initialHorizontalOffset = textArea.TextView.HorizontalOffset;

		// Premise: the scroll target must start out of view and the view must be panned right for
		// the assertions to mean anything.
		Assert.IsFalse(IsLineVisible(textArea, 400), "Expected the last line to be outside the visible range.");
		Assert.IsGreaterThan(0.0, initialHorizontalOffset);

		// A text area without a TextEditor wrapper is a supported composition point when the host
		// supplies the scrolling - here a viewer; the location is applied and the preferred line is
		// scrolled into view without panning horizontally.
		textArea.ApplyLocation(
			new NavigationLocation(DocumentId: "path.txt", CaretOffset: 0, Selection: new TextRange(0, 0), PreferredDocumentLine: 400),
			focus: EditorFocusMode.LeaveUnchanged
		);

		textArea.UpdateLayout();

		Assert.IsGreaterThan(initialVerticalOffset, textArea.TextView.VerticalOffset);
		Assert.IsTrue(IsLineVisible(textArea, 400), "Expected the preferred line to be scrolled into view.");
		Assert.AreEqual(initialHorizontalOffset, textArea.TextView.HorizontalOffset, 0.001);
	}

	[TestMethod]
	public void ApplyLocation_SelectionStartInsideCrLfInterior_SnapsStartOutOfThePair()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		// The selection start (offset 4) sits between the '\r' and the '\n'; it snaps back to the end
		// of the preceding line, so the selection begins before the terminator instead of inside it.
		editor.TextArea.ApplyLocation(
			new NavigationLocation(DocumentId: "path.txt", CaretOffset: 3, Selection: new TextRange(4, 3), PreferredDocumentLine: null)
		);

		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual("\r\nt", editor.SelectedText);
		Assert.AreEqual(3, editor.SelectionStart);
		Assert.AreEqual(3, editor.SelectionLength);
		Assert.AreEqual(3, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyLocation_SelectionEndInsideCrLfInterior_SnapsEndOutOfThePair()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		// The selection ends between the '\r' and the '\n'; the snapped end shortens the selection
		// to the preceding line's content.
		editor.TextArea.ApplyLocation(
			new NavigationLocation(DocumentId: "path.txt", CaretOffset: 0, Selection: new TextRange(0, 4), PreferredDocumentLine: null)
		);

		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual("one", editor.SelectedText);
		Assert.AreEqual(0, editor.SelectionStart);
		Assert.AreEqual(3, editor.SelectionLength);
		Assert.AreEqual(0, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyLocation_SelectionCollapsesWhenTheSnappedEndFallsBeforeTheStart()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		// The selection is the single '\r'; its end snaps out of the pair interior and lands on the
		// snapped start, so the selection collapses at the recorded caret.
		editor.TextArea.ApplyLocation(
			new NavigationLocation(DocumentId: "path.txt", CaretOffset: 3, Selection: new TextRange(3, 1), PreferredDocumentLine: null)
		);

		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

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
		Assert.ThrowsExactly<InvalidOperationException>(() => TextAreaExtensions.CreateCaretLocation(textArea, "path.txt", new TextLocation(1, 1)));
	}

	[TestMethod]
	public void CreateRangeLocation_TextAreaWithoutDocument_Throws()
	{
		var textArea = new TextArea();

		Assert.ThrowsExactly<InvalidOperationException>(() =>
			TextAreaExtensions.CreateRangeLocation(textArea, documentId: "path.txt", start: new TextLocation(1, 1), end: new TextLocation(1, 2))
		);
	}

	[TestMethod]
	public void ApplyLocation_TextAreaWithoutDocument_Throws()
	{
		var textArea = new TextArea();

		Assert.ThrowsExactly<InvalidOperationException>(() =>
			textArea.ApplyLocation(
				new NavigationLocation(DocumentId: "path.txt", CaretOffset: 0, Selection: new TextRange(0, 0), PreferredDocumentLine: 1)
			)
		);
	}

	[TestMethod]
	public void ApplyLocation_NegativeCaretOffset_ClampsToTheDocumentStart()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		editor.TextArea.ApplyLocation(
			new NavigationLocation(DocumentId: "path.txt", CaretOffset: -5, Selection: new TextRange(0, 0), PreferredDocumentLine: null)
		);

		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(0, editor.CaretOffset);
		Assert.AreEqual(0, editor.SelectionStart);
		Assert.AreEqual(0, editor.SelectionLength);
	}

	// A point inside the first row of the first line, at a distance from the left edge of the text, in the text area's coordinates.
	private static Point PointOnFirstLine(TextEditor editor, double x)
	{
		TextView textView = editor.TextArea.TextView;
		VisualLine firstLine = textView.VisualLines[0];
		double top = firstLine.GetTextLineVisualYPosition(firstLine.TextLines[0], VisualYPosition.TextTop);

		return TestHost.TranslatePointTo(textView, new Point(x, top + 2.0), editor.TextArea);
	}
}
