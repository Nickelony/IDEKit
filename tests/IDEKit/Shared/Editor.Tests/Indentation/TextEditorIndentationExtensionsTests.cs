#if AVALONIAEDIT
using Avalonia;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Indentation;
using Nickelony.IDEKit.AvaloniaEdit.LineStatus;
using Nickelony.IDEKit.Core.Indentation;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using System.Windows;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Indentation;
using Nickelony.IDEKit.AvalonEdit.LineStatus;
using Nickelony.IDEKit.Core.Indentation;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

[STATestClass]
public sealed class TextEditorIndentationExtensionsTests
{
	private const string SpaceIndented = "function f()\n    if x then\n        print(\"a\tb\")\n    end\nend\n";

	private static string ToTabs(string text) => WhitespaceOperations.ConvertIndentationToTabs(text, 4);

	private static TextEditor CreateEditor(string text)
	{
		TextEditor editor = TestHost.CreateEditor(text);
		editor.Options.IndentationSize = 4;
		editor.Document.UndoStack.ClearAll();

		return editor;
	}

	private static string BigSpaceIndented(int lineCount) => string.Join("\n", Enumerable.Range(1, lineCount).Select(i => "    line " + i));

	[TestMethod]
	public void ConvertIndentationToTabs_ChangesTheLeadingWhitespaceOfEveryIndentedLine()
	{
		TextEditor editor = CreateEditor(SpaceIndented);

		bool changed = editor.ConvertIndentationToTabs();

		Assert.IsTrue(changed);
		Assert.AreEqual("function f()\n\tif x then\n\t\tprint(\"a\tb\")\n\tend\nend\n", editor.Text);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_UsesTheIndentationSizeOfTheEditor()
	{
		TextEditor editor = CreateEditor("a\n  b\n    c\n");
		editor.Options.IndentationSize = 2;

		editor.ConvertIndentationToTabs();

		Assert.AreEqual("a\n\tb\n\t\tc\n", editor.Text);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_MakesTheEditorInsertTabs_EvenWhenNoLineChanges()
	{
		TextEditor editor = CreateEditor("a\nb\n");
		editor.Options.ConvertTabsToSpaces = true;

		bool changed = editor.ConvertIndentationToTabs();

		Assert.IsFalse(changed);
		Assert.IsFalse(editor.Options.ConvertTabsToSpaces);
	}

	[TestMethod]
	public void ConvertIndentationToSpaces_MakesTheEditorInsertSpaces_EvenWhenNoLineChanges()
	{
		TextEditor editor = CreateEditor("a\n    b\n");
		editor.Options.ConvertTabsToSpaces = false;

		bool changed = editor.ConvertIndentationToSpaces();

		Assert.IsFalse(changed);
		Assert.IsTrue(editor.Options.ConvertTabsToSpaces);
	}

	[TestMethod]
	public void ConvertIndentationToSpaces_KeepsTheTabsAfterTheFirstCharacter()
	{
		TextEditor editor = CreateEditor("\tprint(\"a\tb\")\t-- note\n");

		editor.ConvertIndentationToSpaces();

		Assert.AreEqual("    print(\"a\tb\")\t-- note\n", editor.Text);
		Assert.IsTrue(editor.Options.ConvertTabsToSpaces);
	}

	[TestMethod]
	public void ResizeIndentation_GivesTheLevelsTheNewSize_AndSetsTheOptions()
	{
		TextEditor editor = CreateEditor("a\n  b\n    c\n   d\n");
		editor.Options.ConvertTabsToSpaces = false;

		bool changed = editor.ResizeIndentation(currentSize: 2, newSize: 3);

		Assert.IsTrue(changed);
		Assert.AreEqual("a\n   b\n      c\n    d\n", editor.Text);
		Assert.IsTrue(editor.Options.ConvertTabsToSpaces);
		Assert.AreEqual(3, editor.Options.IndentationSize);
	}

	[TestMethod]
	[DataRow(0, 4)]
	[DataRow(2, 0)]
	[DataRow(1001, 4)]
	[DataRow(2, 1001)]
	public void ResizeIndentation_SizeOutsideTheLimits_ThrowsAndChangesNothing(int currentSize, int newSize)
	{
		TextEditor editor = CreateEditor("a\n  b\n");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => editor.ResizeIndentation(currentSize, newSize));

		Assert.AreEqual("a\n  b\n", editor.Text);
		Assert.AreEqual(4, editor.Options.IndentationSize);
	}

	[TestMethod]
	[DataRow("\n")]
	[DataRow("\r\n")]
	[DataRow("\r")]
	public void ConvertIndentationToTabs_KeepsTheLineTerminators(string terminator)
	{
		string text = SpaceIndented.Replace("\n", terminator, StringComparison.Ordinal);
		TextEditor editor = CreateEditor(text);

		editor.ConvertIndentationToTabs();

		Assert.AreEqual(ToTabs(text), editor.Text);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_RaisesOneChange_AndAnOffsetChangeEntryPerChangedLineThatNamesOnlyItsIndentation()
	{
		TextEditor editor = CreateEditor("a\n    b\n        c\n    d\ne\n");
		var entries = new List<(int Offset, int Removed, int Inserted)>();
		int changeCount = 0;

		editor.Document.Changed += (_, e) =>
		{
			changeCount++;
			entries.AddRange(e.OffsetChangeMap.Select(entry => (entry.Offset, entry.RemovalLength, entry.InsertionLength)));
		};

		editor.ConvertIndentationToTabs();

		Assert.AreEqual("a\n\tb\n\t\tc\n\td\ne\n", editor.Text);
		Assert.AreEqual(1, changeCount);

		// The entries run from the highest offset to the lowest, and each one replaces the indentation of one line and nothing else.
		Assert.AreSequenceEqual([(18, 4, 1), (8, 8, 2), (2, 4, 1)], entries);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_IsOneUndoStep()
	{
		TextEditor editor = CreateEditor(SpaceIndented);

		editor.ConvertIndentationToTabs();

		editor.Document.UndoStack.Undo();

		Assert.AreEqual(SpaceIndented, editor.Text);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);

		editor.Document.UndoStack.Redo();

		Assert.AreEqual(ToTabs(SpaceIndented), editor.Text);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_UndoRestoresTheTextButNotTheOptions()
	{
		TextEditor editor = CreateEditor(SpaceIndented);

		editor.ConvertIndentationToTabs();
		editor.Document.UndoStack.Undo();

		Assert.AreEqual(SpaceIndented, editor.Text);
		Assert.IsFalse(editor.Options.ConvertTabsToSpaces);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_AnchorsInTheContentFollowTheirText()
	{
		TextEditor editor = CreateEditor(SpaceIndented);
		string[] markers = ["function", "if x", "print", "end\nend"];

		TextAnchor[] anchors = [.. markers.Select(marker => editor.Document.CreateAnchor(SpaceIndented.IndexOf(marker, StringComparison.Ordinal)))];

		editor.ConvertIndentationToTabs();

		for (int index = 0; index < markers.Length; index++)
			Assert.AreEqual(editor.Text.IndexOf(markers[index], StringComparison.Ordinal), anchors[index].Offset, markers[index]);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_KeepsTheCaretOnItsText()
	{
		TextEditor editor = CreateEditor(SpaceIndented);

		// The caret sits behind the "x" of "if x then".
		editor.CaretOffset = SpaceIndented.IndexOf("x then", StringComparison.Ordinal) + 1;

		editor.ConvertIndentationToTabs();

		Assert.AreEqual(editor.Text.IndexOf("x then", StringComparison.Ordinal) + 1, editor.CaretOffset);
	}

	[TestMethod]
	[DataRow(2, 2)]
	[DataRow(3, 4)]
	[DataRow(4, 4)]
	[DataRow(10, 4)]
	public void ConvertIndentationToTabs_CaretInsideAReplacedIndentation_MapsAsTheEngineMapsAnAnchor(int caret, int expected)
	{
		TextEditor editor = CreateEditor("a\n        b\n");

		// The second line starts at 2 and its eight spaces end at 10; they become two tabs, and the engine moves a caret inside
		// them behind the new indentation.
		editor.CaretOffset = caret;

		editor.ConvertIndentationToTabs();

		Assert.AreEqual("a\n\t\tb\n", editor.Text);
		Assert.AreEqual(expected, editor.CaretOffset);
		Assert.AreEqual(0, editor.SelectionLength);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_SelectionThatBeginsAndEndsInsideReplacedIndentations_KeepsTheTextBetweenThem()
	{
		TextEditor editor = CreateEditor("a\n        b\n    c\n");

		// From inside the indentation of the second line to inside the indentation of the third.
		editor.Select(3, 12);

		editor.ConvertIndentationToTabs();

		Assert.AreEqual("a\n\t\tb\n\tc\n", editor.Text);
		Assert.AreEqual("b\n", editor.SelectedText);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_KeepsASelectionInsideTheContent()
	{
		TextEditor editor = CreateEditor(SpaceIndented);

		int start = SpaceIndented.IndexOf("x then", StringComparison.Ordinal);
		int end = SpaceIndented.IndexOf("end\nend", StringComparison.Ordinal) + 3;

		editor.Select(start, end - start);

		editor.ConvertIndentationToTabs();

		Assert.AreEqual("x then\n\t\tprint(\"a\tb\")\n\tend", editor.SelectedText);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_SelectAll_KeepsEverythingSelected()
	{
		TextEditor editor = CreateEditor(SpaceIndented);

		editor.SelectAll();

		editor.ConvertIndentationToTabs();

		Assert.AreEqual(editor.Text, editor.SelectedText);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_SelectedLinesBeginningAtTheirStart_StaySelected()
	{
		TextEditor editor = CreateEditor(SpaceIndented);

		int start = SpaceIndented.IndexOf("    if", StringComparison.Ordinal);
		int end = SpaceIndented.IndexOf("    end", StringComparison.Ordinal);

		editor.Select(start, end - start);

		editor.ConvertIndentationToTabs();

		Assert.AreEqual("\tif x then\n\t\tprint(\"a\tb\")\n", editor.SelectedText);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_RectangleSelection_StaysARectangle()
	{
		TextEditor editor = CreateEditor("function f()\n    alpha\n    beta\n    gamma\nend\n");
		TextArea textArea = editor.TextArea;

		textArea.Selection = new RectangleSelection(textArea, new TextViewPosition(2, 6), new TextViewPosition(4, 8));

		editor.ConvertIndentationToTabs();

		Assert.AreEqual("function f()\n\talpha\n\tbeta\n\tgamma\nend\n", editor.Text);
		Assert.IsInstanceOfType<RectangleSelection>(textArea.Selection);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_NoChange_LeavesTheDocumentTheUndoStackAndTheSelectionUntouched()
	{
		TextEditor editor = CreateEditor("a\n\tb\n  c\n");
		int changeCount = 0;

		editor.Select(3, 4);
		editor.Document.Changed += (_, _) => changeCount++;

		bool changed = editor.ConvertIndentationToTabs();

		Assert.IsFalse(changed);
		Assert.AreEqual("a\n\tb\n  c\n", editor.Text);
		Assert.AreEqual(0, changeCount);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
		Assert.AreEqual(3, editor.SelectionStart);
		Assert.AreEqual(4, editor.SelectionLength);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_EmptyDocument_ChangesNothing()
	{
		TextEditor editor = CreateEditor(string.Empty);

		bool changed = editor.ConvertIndentationToTabs();

		Assert.IsFalse(changed);
		Assert.IsEmpty(editor.Text);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_WhitespaceOnlyLines_ConvertToo()
	{
		TextEditor editor = CreateEditor("a\n    \nb");

		editor.ConvertIndentationToTabs();

		Assert.AreEqual("a\n\t\nb", editor.Text);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_LastLineWithoutATerminator_Converts()
	{
		TextEditor editor = CreateEditor("a\n    b");

		editor.ConvertIndentationToTabs();

		Assert.AreEqual("a\n\tb", editor.Text);
	}

	[TestMethod]
	public void ConvertIndentation_NullEditor_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextEditorIndentationExtensions.ConvertIndentationToTabs(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => TextEditorIndentationExtensions.ConvertIndentationToSpaces(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => TextEditorIndentationExtensions.ResizeIndentation(null!, 2, 4));
		Assert.ThrowsExactly<ArgumentNullException>(() => TextEditorIndentationExtensions.DetectIndentation(null!, true, 4));
	}

	[TestMethod]
	public void ConvertIndentation_EditorWithoutDocument_Throws()
	{
		var editor = new TextEditor { Document = null };

		Assert.ThrowsExactly<InvalidOperationException>(() => editor.ConvertIndentationToTabs());
		Assert.ThrowsExactly<InvalidOperationException>(() => editor.DetectIndentation(true, 4));
	}

	[TestMethod]
	public void ConvertIndentationToTabs_WithFakeEditTarget_AppliesOneBatchAndPublishesTheEditorDocument()
	{
		TextEditor editor = CreateEditor(SpaceIndented);
		var target = new FakeEditTarget(editor);

		editor.ConvertIndentationToTabs(target);

		Assert.AreEqual(ToTabs(SpaceIndented), editor.Text);
		Assert.AreEqual(editor.Text, target.Text);
		Assert.HasCount(3, target.Operations);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_TargetWithOtherContent_ThrowsAndLeavesTheDocument()
	{
		TextEditor editor = CreateEditor(SpaceIndented);
		var target = new RecordingEditTarget("other");

		Assert.ThrowsExactly<InvalidOperationException>(() => editor.ConvertIndentationToTabs(target));

		Assert.AreEqual(SpaceIndented, editor.Text);
		Assert.AreEqual(0, target.ApplyCalls);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_ScrolledEditor_KeepsTheScrollPosition()
	{
		TextEditor editor = TestHost.CreateEditor(BigSpaceIndented(200));
		editor.Options.IndentationSize = 4;

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		editor.Document.UndoStack.ClearAll();

		EditorViewTestHelpers.ScrollAwayFromOrigin(editor);

		Vector scrollBefore = editor.TextArea.TextView.ScrollOffset;

		// Premise: the vertical scroll offset must be genuinely nonzero, or the preservation assertion would be vacuous.
		Assert.IsGreaterThan(0.0, scrollBefore.Y);

		editor.ConvertIndentationToTabs();

		Assert.AreEqual(scrollBefore.Y, editor.TextArea.TextView.ScrollOffset.Y, 1.0);
		Assert.StartsWith("\tline 1\n", editor.Text);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_OfThousandsOfLines_RaisesOneChangeAndKeepsAChangeMarkerTrackerFast()
	{
		const int lineCount = 4_000;

		string text = BigSpaceIndented(lineCount);
		TextEditor editor = CreateEditor(text);

		var tracker = new UnsavedChangesTracker(() => editor.Document);
		int changeCount = 0;

		tracker.SetBaseline(text);

		// The first read attaches the tracker to the document.
		Assert.IsEmpty(tracker.GetMarkedLineNumbers());

		editor.Document.Changed += (_, _) => changeCount++;

		var watch = System.Diagnostics.Stopwatch.StartNew();
		editor.ConvertIndentationToTabs();
		IReadOnlyList<int> marked = tracker.GetMarkedLineNumbers();
		watch.Stop();

		Assert.AreEqual(1, changeCount);
		Assert.HasCount(lineCount, marked);
		Assert.IsLessThan(2_000L, watch.ElapsedMilliseconds, "The conversion took " + watch.ElapsedMilliseconds + " ms.");
	}

	[TestMethod]
	public void DetectIndentation_SpaceText_SetsTheOptionsAndReturnsWhatTheTextShowed()
	{
		TextEditor editor = CreateEditor("a\n  b\n    c\n  d\ne\n");
		editor.Options.ConvertTabsToSpaces = false;

		IndentationDetection detection = editor.DetectIndentation(defaultInsertSpaces: false, defaultSize: 8);

		Assert.AreEqual(new IndentationDetection(InsertSpaces: true, Size: 2, InsertSpacesDetected: true, SizeDetected: true), detection);
		Assert.IsTrue(editor.Options.ConvertTabsToSpaces);
		Assert.AreEqual(2, editor.Options.IndentationSize);
	}

	[TestMethod]
	public void DetectIndentation_TabText_SetsTabsAndTheDefaultSize()
	{
		TextEditor editor = CreateEditor("a\n\tb\n\t\tc\n\td\ne\n");

		IndentationDetection detection = editor.DetectIndentation(defaultInsertSpaces: true, defaultSize: 3);

		Assert.AreEqual(new IndentationDetection(InsertSpaces: false, Size: 3, InsertSpacesDetected: true, SizeDetected: false), detection);
		Assert.IsFalse(editor.Options.ConvertTabsToSpaces);
		Assert.AreEqual(3, editor.Options.IndentationSize);
	}

	[TestMethod]
	public void DetectIndentation_TextThatShowsNothing_AppliesTheDefaults()
	{
		TextEditor editor = CreateEditor("print(1)\n");
		editor.Options.ConvertTabsToSpaces = false;

		IndentationDetection detection = editor.DetectIndentation(defaultInsertSpaces: true, defaultSize: 6);

		Assert.IsFalse(detection.InsertSpacesDetected);
		Assert.IsFalse(detection.SizeDetected);
		Assert.IsTrue(editor.Options.ConvertTabsToSpaces);
		Assert.AreEqual(6, editor.Options.IndentationSize);
	}

	[TestMethod]
	public void DetectIndentation_ReadsOnlyTheLinesTheDetectorCaps_AndLeavesTheText()
	{
		string unindented = string.Concat(Enumerable.Repeat("x\n", IndentationDetector.MaximumScannedLineCount));
		string indented = "a\n    b\n    c\n        d\n    e\n";

		TextEditor late = CreateEditor(unindented + indented);
		TextEditor early = CreateEditor(indented + unindented);

		IndentationDetection lateDetection = late.DetectIndentation(true, 2);
		IndentationDetection earlyDetection = early.DetectIndentation(true, 2);

		Assert.IsFalse(lateDetection.SizeDetected);
		Assert.AreEqual(2, late.Options.IndentationSize);
		Assert.IsTrue(earlyDetection.SizeDetected);
		Assert.AreEqual(4, early.Options.IndentationSize);
		Assert.AreEqual(unindented + indented, late.Text);
	}

	[TestMethod]
	public void DetectIndentation_OfAHugeDocument_CopiesOnlyTheLinesTheDetectorReads()
	{
		TextEditor editor = CreateEditor(BigSpaceIndented(100_000));

		_ = editor.DetectIndentation(true, 4);

		long before = GC.GetAllocatedBytesForCurrentThread();
		_ = editor.DetectIndentation(true, 4);
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		// The whole text is about 3.6 MB of UTF-16; the first 10,000 lines are about a tenth of it.
		Assert.IsLessThan(1_000_000L, allocated, "The detection allocated " + allocated + " bytes.");
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(1001)]
	public void DetectIndentation_DefaultSizeOutsideTheLimits_Throws(int defaultSize)
	{
		TextEditor editor = CreateEditor("a");
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => editor.DetectIndentation(true, defaultSize));
	}

	[TestMethod]
	public void DetectIndentation_ThenConvert_RoundTripsThroughTheOptions()
	{
		TextEditor editor = CreateEditor("a\n  b\n    c\n  d\ne\n");

		editor.DetectIndentation(true, 4);
		editor.ConvertIndentationToTabs();

		Assert.AreEqual("a\n\tb\n\t\tc\n\td\ne\n", editor.Text);
		Assert.IsFalse(editor.Options.ConvertTabsToSpaces);
	}
}
