using Nickelony.IDEKit.Core.Text;
#if AVALONIAEDIT
using Avalonia;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.LineStatus;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using System.Windows;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LineStatus;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

[STATestClass]
public sealed class TextEditorLineTerminatorExtensionsTests
{
	private const string Lines = "function f()\n    if x then\n        print(1)\n    end\nend\n";

	private static string With(string terminator) => Lines.Replace("\n", terminator, StringComparison.Ordinal);

	private static TextEditor CreateEditor(string text)
	{
		TextEditor editor = TestHost.CreateEditor(text);
		editor.Document.UndoStack.ClearAll();

		return editor;
	}

	private static string TerminatorOf(LineTerminatorStyle style)
	{
		return style switch
		{
			LineTerminatorStyle.Lf => "\n",
			LineTerminatorStyle.CrLf => "\r\n",
			_ => "\r",
		};
	}

	[TestMethod]
	[DataRow("\n", LineTerminatorStyle.CrLf)]
	[DataRow("\n", LineTerminatorStyle.Cr)]
	[DataRow("\r\n", LineTerminatorStyle.Lf)]
	[DataRow("\r\n", LineTerminatorStyle.Cr)]
	[DataRow("\r", LineTerminatorStyle.Lf)]
	[DataRow("\r", LineTerminatorStyle.CrLf)]
	public void ConvertLineTerminators_ReplacesEveryTerminatorAndNothingElse(string from, LineTerminatorStyle to)
	{
		TextEditor editor = CreateEditor(With(from));

		bool changed = editor.ConvertLineTerminators(to);

		Assert.IsTrue(changed);
		Assert.AreEqual(With(TerminatorOf(to)), editor.Text);
	}

	[TestMethod]
	public void ConvertLineTerminators_MixedText_BecomesOneStyle()
	{
		TextEditor editor = CreateEditor("a\nb\r\nc\rd\r\n\ne");

		editor.ConvertLineTerminators(LineTerminatorStyle.Lf);

		Assert.AreEqual("a\nb\nc\nd\n\ne", editor.Text);
		Assert.AreEqual(LineTerminatorStyle.Lf, LineTerminatorDetector.Detect(editor.Text));
	}

	[TestMethod]
	public void ConvertLineTerminators_IsOneUndoStep()
	{
		TextEditor editor = CreateEditor("a\r\nb\nc\rd\r\n");

		editor.ConvertLineTerminators(LineTerminatorStyle.Lf);

		editor.Document.UndoStack.Undo();

		Assert.AreEqual("a\r\nb\nc\rd\r\n", editor.Text);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);

		editor.Document.UndoStack.Redo();

		Assert.AreEqual("a\nb\nc\nd\n", editor.Text);
	}

	[TestMethod]
	public void ConvertLineTerminators_RaisesOneChange_AndAnOffsetChangeEntryPerReplacedTerminatorThatNamesOnlyThatTerminator()
	{
		TextEditor editor = CreateEditor("a\r\nb\nc\r\nd\r\ne");
		var entries = new List<(int Offset, int Removed, int Inserted)>();
		int changeCount = 0;

		editor.Document.Changed += (_, e) =>
		{
			changeCount++;
			entries.AddRange(e.OffsetChangeMap.Select(entry => (entry.Offset, entry.RemovalLength, entry.InsertionLength)));
		};

		editor.ConvertLineTerminators(LineTerminatorStyle.Lf);

		Assert.AreEqual("a\nb\nc\nd\ne", editor.Text);
		Assert.AreEqual(1, changeCount);

		// The entries run from the highest offset to the lowest, and the LF at offset 4 is not among them.
		Assert.AreSequenceEqual([(9, 2, 1), (6, 2, 1), (1, 2, 1)], entries);
	}

	[TestMethod]
	public void ConvertLineTerminators_AnchorsInTheContentFollowTheirText()
	{
		const string text = "alpha\r\nbeta\r\ngamma\r\ndelta";

		TextEditor editor = CreateEditor(text);
		string[] markers = ["alpha", "beta", "gamma", "delta"];

		TextAnchor[] anchors = [.. markers.Select(marker => editor.Document.CreateAnchor(text.IndexOf(marker, StringComparison.Ordinal)))];

		editor.ConvertLineTerminators(LineTerminatorStyle.Lf);

		for (int index = 0; index < markers.Length; index++)
			Assert.AreEqual(editor.Text.IndexOf(markers[index], StringComparison.Ordinal), anchors[index].Offset, markers[index]);
	}

	[TestMethod]
	[DataRow(0, DisplayName = "AtTheStartOfTheText")]
	[DataRow(3, DisplayName = "InsideALine")]
	[DataRow(5, DisplayName = "AtTheEndOfALine")]
	[DataRow(7, DisplayName = "AtTheStartOfALine")]
	[DataRow(11, DisplayName = "AtTheEndOfASecondLine")]
	[DataRow(13, DisplayName = "AtTheStartOfTheLastLine")]
	[DataRow(18, DisplayName = "AtTheEndOfTheText")]
	public void ConvertLineTerminators_KeepsTheCaretOnItsText(int caret)
	{
		const string text = "alpha\r\nbeta\r\ngamma";
		TextEditor editor = CreateEditor(text);

		editor.CaretOffset = caret;

		editor.ConvertLineTerminators(LineTerminatorStyle.Lf);

		// The text before the caret is the same text with the new terminators.
		Assert.AreEqual(text[..caret].Replace("\r\n", "\n", StringComparison.Ordinal), editor.Text[..editor.CaretOffset]);
		Assert.AreEqual(0, editor.SelectionLength);
	}

	[TestMethod]
	public void ConvertLineTerminators_KeepsASelectionOnItsText()
	{
		const string text = "alpha\r\nbeta\r\ngamma\r\ndelta";
		TextEditor editor = CreateEditor(text);

		// From the middle of "alpha" to the middle of "gamma".
		int start = text.IndexOf("pha", StringComparison.Ordinal);
		int end = text.IndexOf("mma", StringComparison.Ordinal);

		editor.Select(start, end - start);

		editor.ConvertLineTerminators(LineTerminatorStyle.Lf);

		Assert.AreEqual("pha\nbeta\nga", editor.SelectedText);
	}

	[TestMethod]
	public void ConvertLineTerminators_SelectedWholeLines_StayWholeLines()
	{
		const string text = "alpha\r\nbeta\r\ngamma\r\ndelta";
		TextEditor editor = CreateEditor(text);

		// The second and third line, with the terminator of the third.
		int start = text.IndexOf("beta", StringComparison.Ordinal);
		int end = text.IndexOf("delta", StringComparison.Ordinal);

		editor.Select(start, end - start);

		editor.ConvertLineTerminators(LineTerminatorStyle.Lf);

		Assert.AreEqual("beta\ngamma\n", editor.SelectedText);
	}

	[TestMethod]
	public void ConvertLineTerminators_SelectAll_KeepsEverythingSelected()
	{
		TextEditor editor = CreateEditor(With("\r\n"));

		editor.SelectAll();

		editor.ConvertLineTerminators(LineTerminatorStyle.Lf);

		Assert.AreEqual(editor.Text, editor.SelectedText);
	}

	[TestMethod]
	public void ConvertLineTerminators_RectangleSelection_StaysARectangle()
	{
		TextEditor editor = CreateEditor("alpha\r\nbeta\r\ngamma\r\n");
		TextArea textArea = editor.TextArea;

		textArea.Selection = new RectangleSelection(textArea, new TextViewPosition(1, 2), new TextViewPosition(3, 4));

		editor.ConvertLineTerminators(LineTerminatorStyle.Lf);

		Assert.AreEqual("alpha\nbeta\ngamma\n", editor.Text);
		Assert.IsInstanceOfType<RectangleSelection>(textArea.Selection);
		Assert.AreEqual(1, textArea.Selection.StartPosition.Line);
		Assert.AreEqual(2, textArea.Selection.StartPosition.Column);
		Assert.AreEqual(3, textArea.Selection.EndPosition.Line);
		Assert.AreEqual(4, textArea.Selection.EndPosition.Column);
	}

	[TestMethod]
	public void ConvertLineTerminators_NoChange_LeavesTheDocumentTheUndoStackAndTheSelectionUntouched()
	{
		TextEditor editor = CreateEditor("a\nb\nc\n");
		int changeCount = 0;

		editor.Select(1, 3);
		editor.Document.Changed += (_, _) => changeCount++;

		bool changed = editor.ConvertLineTerminators(LineTerminatorStyle.Lf);

		Assert.IsFalse(changed);
		Assert.AreEqual("a\nb\nc\n", editor.Text);
		Assert.AreEqual(0, changeCount);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
		Assert.AreEqual(1, editor.SelectionStart);
		Assert.AreEqual(3, editor.SelectionLength);
	}

	[TestMethod]
	[DataRow("", DisplayName = "Empty")]
	[DataRow("one line", DisplayName = "OneLineWithoutATerminator")]
	public void ConvertLineTerminators_TextWithoutATerminator_ChangesNothing(string text)
	{
		TextEditor editor = CreateEditor(text);

		bool changed = editor.ConvertLineTerminators(LineTerminatorStyle.CrLf);

		Assert.IsFalse(changed);
		Assert.AreEqual(text, editor.Text);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
	}

	[TestMethod]
	[DataRow(LineTerminatorStyle.Mixed)]
	[DataRow(LineTerminatorStyle.None)]
	public void ConvertLineTerminators_StyleThatNamesNoSingleTerminator_ThrowsAndChangesNothing(LineTerminatorStyle style)
	{
		TextEditor editor = CreateEditor("a\r\nb\n");

		Assert.ThrowsExactly<ArgumentException>(() => editor.ConvertLineTerminators(style));

		Assert.AreEqual("a\r\nb\n", editor.Text);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
	}

	[TestMethod]
	public void ConvertLineTerminators_NullEditor_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => TextEditorLineTerminatorExtensions.ConvertLineTerminators(null!, LineTerminatorStyle.Lf));

	[TestMethod]
	public void ConvertLineTerminators_EditorWithoutDocument_Throws()
	{
		var editor = new TextEditor { Document = null };
		Assert.ThrowsExactly<InvalidOperationException>(() => editor.ConvertLineTerminators(LineTerminatorStyle.Lf));
	}

	[TestMethod]
	public void ConvertLineTerminators_WithFakeEditTarget_AppliesOneBatchAndPublishesTheEditorDocument()
	{
		TextEditor editor = CreateEditor("a\r\nb\r\nc\r\n");
		var target = new FakeEditTarget(editor);

		editor.ConvertLineTerminators(LineTerminatorStyle.Lf, target);

		Assert.AreEqual("a\nb\nc\n", editor.Text);
		Assert.AreEqual(editor.Text, target.Text);
		Assert.HasCount(3, target.Operations);
	}

	[TestMethod]
	public void ConvertLineTerminators_TargetWithOtherContent_ThrowsAndLeavesTheDocument()
	{
		TextEditor editor = CreateEditor("a\r\nb\r\n");
		var target = new RecordingEditTarget("other");

		Assert.ThrowsExactly<InvalidOperationException>(() => editor.ConvertLineTerminators(LineTerminatorStyle.Lf, target));

		Assert.AreEqual("a\r\nb\r\n", editor.Text);
		Assert.AreEqual(0, target.ApplyCalls);
	}

	[TestMethod]
	public void ConvertLineTerminators_ScrolledEditor_KeepsTheScrollPosition()
	{
		TextEditor editor = TestHost.CreateEditor(string.Join("\r\n", Enumerable.Range(1, 200).Select(i => "line " + i)));

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		editor.Document.UndoStack.ClearAll();

		EditorViewTestHelpers.ScrollAwayFromOrigin(editor);

		Vector scrollBefore = editor.TextArea.TextView.ScrollOffset;

		// Premise: the vertical scroll offset must be genuinely nonzero, or the preservation assertion would be vacuous.
		Assert.IsGreaterThan(0.0, scrollBefore.Y);

		editor.ConvertLineTerminators(LineTerminatorStyle.Lf);

		Assert.AreEqual(scrollBefore.Y, editor.TextArea.TextView.ScrollOffset.Y, 1.0);
		Assert.StartsWith("line 1\nline 2\n", editor.Text);
	}

	[TestMethod]
	public void ConvertLineTerminators_OfThousandsOfLines_RaisesOneChangeAndKeepsAChangeMarkerTrackerFast()
	{
		const int lineCount = 4_000;

		string text = string.Join("\r\n", Enumerable.Range(1, lineCount).Select(i => "line " + i));
		TextEditor editor = CreateEditor(text);

		var tracker = new UnsavedChangesTracker(() => editor.Document);
		int changeCount = 0;

		tracker.SetBaseline(text);

		// The first read attaches the tracker to the document.
		Assert.IsEmpty(tracker.GetMarkedLineNumbers());

		editor.Document.Changed += (_, _) => changeCount++;

		var watch = System.Diagnostics.Stopwatch.StartNew();
		editor.ConvertLineTerminators(LineTerminatorStyle.Lf);
		IReadOnlyList<int> marked = tracker.GetMarkedLineNumbers();
		watch.Stop();

		Assert.AreEqual(1, changeCount);
		Assert.IsEmpty(marked, "The line terminators are normalized before the change markers compare the lines.");
		Assert.IsLessThan(2_000L, watch.ElapsedMilliseconds, "The conversion took " + watch.ElapsedMilliseconds + " ms.");
	}

	[TestMethod]
	[DataRow("\n", LineTerminatorStyle.CrLf)]
	[DataRow("\r\n", LineTerminatorStyle.Lf)]
	[DataRow("\r\n", LineTerminatorStyle.Cr)]
	public void EnterAfterAConversion_InsertsTheTerminatorOfTheNewStyle_InTheMiddleAndAtTheEnd(string from, LineTerminatorStyle to)
	{
		TextEditor editor = CreateEditor("one" + from + "two" + from + "three");
		string terminator = TerminatorOf(to);

		editor.ConvertLineTerminators(to);

		// In the middle of the text the engine takes the terminator of the caret's line, and on the last line, that of the line before it.
		editor.CaretOffset = 1;
		editor.TextArea.PerformTextInput("\n");
		editor.CaretOffset = editor.Text.Length;
		editor.TextArea.PerformTextInput("\n");

		Assert.AreEqual("o" + terminator + "ne" + terminator + "two" + terminator + "three" + terminator, editor.Text);
		Assert.AreEqual(to, LineTerminatorDetector.Detect(editor.Text));
	}

	[TestMethod]
	public void EnterInTextWithoutAnyTerminator_InsertsThePlatformTerminator_WhateverStyleWasChosen()
	{
		TextEditor editor = CreateEditor("one line");

		// The conversion has no terminator to replace, so the engine falls back to Environment.NewLine for the first new line.
		Assert.IsFalse(editor.ConvertLineTerminators(Environment.NewLine == "\n" ? LineTerminatorStyle.CrLf : LineTerminatorStyle.Lf));

		editor.CaretOffset = editor.Text.Length;
		editor.TextArea.PerformTextInput("\n");

		Assert.AreEqual("one line" + Environment.NewLine, editor.Text);
	}
}
