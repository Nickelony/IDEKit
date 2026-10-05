#if AVALONIAEDIT
using Avalonia;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Formatting;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Formatting;
using static Nickelony.IDEKit.AvaloniaEdit.Tests.EditingTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using System.Windows;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Formatting;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Formatting;
using static Nickelony.IDEKit.AvalonEdit.Tests.EditingTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class DocumentFormatterServiceTests
{
	[TestMethod]
	public void FormatDocument_IsOneUndoStep_PreservesCaretLineAndScroll()
	{
		string original = string.Join("\r\n", Enumerable.Range(1, 200).Select(i => "Line " + i + "   "));

		var editor = new TextEditor { Text = original };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		editor.Document.UndoStack.ClearAll();
		editor.CaretOffset = editor.Document.GetOffset(2, 3);

		EditorViewTestHelpers.ScrollAwayFromOrigin(editor);

		Vector scrollBefore = editor.TextArea.TextView.ScrollOffset;

		// Premise: the vertical scroll offset must be genuinely nonzero, or the preservation assertion
		// below would be vacuous.
		Assert.IsGreaterThan(0.0, scrollBefore.Y);

		var service = new DocumentFormatterService();
		service.FormatDocument(editor, new CrLfTrimFormatter());

		Assert.AreEqual(TrimTrailingWhitespace(original), editor.Text);

		// The caret remains on line 2 after the full-document replacement.
		Assert.AreEqual(2, editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber);

		// Both scroll offsets are preserved.
		Assert.AreEqual(scrollBefore.X, editor.TextArea.TextView.ScrollOffset.X, 1.0);
		Assert.AreEqual(scrollBefore.Y, editor.TextArea.TextView.ScrollOffset.Y, 1.0);

		// Undo restores the original text, and redo restores the formatted text.
		editor.Undo();
		Assert.AreEqual(original, editor.Text);

		editor.Redo();
		Assert.AreEqual(TrimTrailingWhitespace(original), editor.Text);
	}

	[TestMethod]
	public void FormatDocument_RectangleSelectionOutsideTheChange_StaysARectangleOverTheSameLines()
	{
		TextEditor editor = TestHost.CreateEditor("ab\r\ncd\r\nef   ");
		TextArea textArea = editor.TextArea;

		textArea.Selection = new RectangleSelection(textArea, new TextViewPosition(1, 1), new TextViewPosition(2, 3));

		new DocumentFormatterService().FormatDocument(editor, TrimTrailingWhitespaceFormatter.Instance);

		Assert.AreEqual("ab\r\ncd\r\nef", editor.Text);
		Assert.IsInstanceOfType<RectangleSelection>(textArea.Selection);
		Assert.AreEqual(1, textArea.Selection.StartPosition.Line);
		Assert.AreEqual(2, textArea.Selection.EndPosition.Line);
	}

	[TestMethod]
	public void FormatDocument_NoChanges_LeavesEditorAndUndoStackUntouched()
	{
		var editor = new TextEditor { Text = "No changes here" };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		editor.Document.UndoStack.ClearAll();
		editor.CaretOffset = 3;

		var service = new DocumentFormatterService();
		service.FormatDocument(editor, new IdentityFormatter());

		Assert.AreEqual("No changes here", editor.Text);
		Assert.AreEqual(3, editor.CaretOffset);

		// No edit was applied, so there is nothing to undo.
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
	}

	[TestMethod]
	public void FormatDocument_EmptyDocumentUnchanged_LeavesEditorUntouched()
	{
		var editor = new TextEditor { Text = string.Empty };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		editor.Document.UndoStack.ClearAll();

		var service = new DocumentFormatterService();
		service.FormatDocument(editor, new IdentityFormatter());

		Assert.IsEmpty(editor.Text);
		Assert.AreEqual(0, editor.CaretOffset);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
	}

	[TestMethod]
	public void FormatDocument_AppliesSuppliedFormatter()
	{
		var editor = new TextEditor { Text = "Customize= CUST_BAR,foo   \r\nLegend =1\t" };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var service = new DocumentFormatterService();
		service.FormatDocument(editor, TrimTrailingWhitespaceFormatter.Instance);

		// The host selects the trim formatter explicitly, so no other formatting policy is applied.
		Assert.AreEqual("Customize= CUST_BAR,foo" + "\r\n" + "Legend =1", editor.Text);
	}

	[TestMethod]
	public void FormatDocument_CaretAfterChangedRange_ShiftsByTheReplacementLengthDifference()
	{
		var editor = new TextEditor { Text = "keep\r\nchange me   \r\nkeep" };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		editor.CaretOffset = editor.Document.TextLength;

		var service = new DocumentFormatterService();
		service.FormatDocument(editor, TrimTrailingWhitespaceFormatter.Instance);

		Assert.AreEqual("keep\r\nchange me\r\nkeep", editor.Text);

		// The caret sat after the changed range, so its offset shifts by the length difference of the
		// replacement and still marks the end of the document.
		Assert.AreEqual(editor.Document.TextLength, editor.CaretOffset);
		Assert.AreEqual("keep", editor.Document.GetText(editor.Document.GetLineByNumber(3)));
	}

	[TestMethod]
	public void FormatDocument_CaretAtChangedRangeEnd_MapsAfterTheReplacement()
	{
		string original = "keep\r\nchange me   \r\nkeep";
		string formatted = "keep\r\nchange me\r\nkeep";

		TextIncrementalEdit change = TextIncrementalEditCalculator.Compute(original, formatted);

		var editor = new TextEditor { Text = original };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		// The caret sits exactly on the changed range's inclusive end, which the contract counts as
		// surviving; the mapped offset lands after the replacement.
		editor.CaretOffset = change.Range.EndOffset;

		var service = new DocumentFormatterService();
		service.FormatDocument(editor, TrimTrailingWhitespaceFormatter.Instance);

		Assert.AreEqual(formatted, editor.Text);
		Assert.AreEqual(change.Range.Offset + change.NewText.Length, editor.CaretOffset);
	}

	[TestMethod]
	public void FormatDocument_PureDeletionAtCaret_MapsCaretToTheDeletedRangeStart()
	{
		var editor = new TextEditor { Text = "abX" };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		// The caret sits exactly on the changed range's inclusive start of a pure deletion.
		editor.CaretOffset = 2;

		var service = new DocumentFormatterService();
		service.FormatDocument(editor, new RemoveTrailingMarkerFormatter());

		Assert.AreEqual("ab", editor.Text);
		Assert.AreEqual(2, editor.CaretOffset);
	}

	[TestMethod]
	public void FormatDocument_AnEditTargetThatScrollsTheViewToTheTop_PutsTheScrollPositionBack()
	{
		string original = string.Join("\r\n", Enumerable.Range(1, 200).Select(i => "Line " + i + "   "));

		var editor = new TextEditor { Text = original };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		EditorViewTestHelpers.ScrollAwayFromOrigin(editor);

		double scrollBefore = editor.TextArea.TextView.ScrollOffset.Y;

		Assert.IsGreaterThan(0.0, scrollBefore);

		var target = new TopScrollingEditTarget(editor, hostWindow.Dispatcher);

		new DocumentFormatterService().FormatDocument(editor, new CrLfTrimFormatter(), target);

		// The WPF scroll viewer applies a scroll command on the next dispatcher turn.
		TestHost.PumpDispatcher(hostWindow.Dispatcher, DispatcherPriority.Background);

		// Premise: the target moved the view, so the restored offset below is the applier's doing.
		Assert.IsLessThan(scrollBefore, target.OffsetAfterApply);
		Assert.AreEqual(TrimTrailingWhitespace(original), editor.Text);
		Assert.AreEqual(scrollBefore, editor.TextArea.TextView.ScrollOffset.Y, 1.0);
	}

	private static string TrimTrailingWhitespace(string content) => string.Join("\r\n", content.Split("\r\n").Select(line => line.TrimEnd()));

	private sealed class CrLfTrimFormatter : IDocumentFormatter
	{
		public string FormatDocument(string content) => TrimTrailingWhitespace(content);
	}

	/// <summary>
	/// An edit target over the editor whose apply scrolls the view to the top, as a host that reveals the start of an edited document does.
	/// </summary>
	private sealed class TopScrollingEditTarget : ITextEditTarget
	{
		private readonly TextEditor _editor;
		private readonly Dispatcher _dispatcher;
		private readonly TextEditorEditTarget _inner;

		public TopScrollingEditTarget(TextEditor editor, Dispatcher dispatcher)
		{
			_editor = editor;
			_dispatcher = dispatcher;
			_inner = new TextEditorEditTarget(editor);
		}

		public double OffsetAfterApply { get; private set; } = double.NaN;

		public string Text => _inner.Text;

		public void Apply(PreparedTextEdits edits)
		{
			_inner.Apply(edits);

			_editor.ScrollToLine(1);
			TestHost.PumpDispatcher(_dispatcher, DispatcherPriority.Background);

			OffsetAfterApply = _editor.TextArea.TextView.ScrollOffset.Y;
		}
	}

	[TestMethod]
	public void FormatDocument_CaretLineMissingInResult_PlacesCaretAtDocumentEnd()
	{
		var editor = new TextEditor { Text = "line 1\r\nline 2\r\nline 3" };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		editor.Document.UndoStack.ClearAll();
		editor.CaretOffset = editor.Document.GetLineByNumber(3).Offset;

		var service = new DocumentFormatterService();
		service.FormatDocument(editor, new CollapseFormatter());

		// The original caret line (3) is gone after the collapse; the caret lands at the document end.
		Assert.AreEqual("collapsed", editor.Text);
		Assert.AreEqual(editor.Document.TextLength, editor.CaretOffset);
	}

	[TestMethod]
	public void FormatDocument_FormatterReturnsEmpty_ReplacesDocumentAndSelectsStart()
	{
		var editor = new TextEditor { Text = "content" };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var service = new DocumentFormatterService();
		service.FormatDocument(editor, new EmptyFormatter());

		// The caret line (1) survives as the only line of the empty result.
		Assert.IsEmpty(editor.Text);
		Assert.AreEqual(0, editor.CaretOffset);
	}

	[TestMethod]
	public void FormatDocument_EmptyDocumentWithContent_KeepsCaretAtInsertionPoint()
	{
		var editor = new TextEditor { Text = string.Empty };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var service = new DocumentFormatterService();
		service.FormatDocument(editor, new AppendingFormatter());

		Assert.AreEqual("!", editor.Text);

		// Minimal-edit mapping keeps an offset at the start of an insertion before the inserted text.
		Assert.AreEqual(0, editor.CaretOffset);
	}

	[TestMethod]
	public void FormatDocument_ChangeOutsideCaretLine_PreservesCaretColumn()
	{
		var editor = new TextEditor { Text = "keep\r\nchange me   \r\nkeep" };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		editor.CaretOffset = editor.Document.GetOffset(1, 3);

		var service = new DocumentFormatterService();
		service.FormatDocument(editor, TrimTrailingWhitespaceFormatter.Instance);

		// The changed range is on line 2; the caret on line 1 keeps its exact offset.
		Assert.AreEqual(editor.Document.GetOffset(1, 3), editor.CaretOffset);
		Assert.AreEqual("keep\r\nchange me\r\nkeep", editor.Text);
	}

	[TestMethod]
	public void FormatDocument_ChangeOutsideSelection_PreservesSelection()
	{
		var editor = new TextEditor { Text = "keep\r\nchange me   \r\nkeep" };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		editor.Select(0, 4);

		var service = new DocumentFormatterService();
		service.FormatDocument(editor, TrimTrailingWhitespaceFormatter.Instance);

		Assert.AreEqual(0, editor.SelectionStart);
		Assert.AreEqual(4, editor.SelectionLength);
	}

	[TestMethod]
	public void FormatDocument_ChangeOutsideSelection_PreservesTheCaretInsideIt()
	{
		var editor = new TextEditor { Text = "keep\r\nchange me   \r\nkeep" };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		// The caret sits inside the selection, not at its end, and the changed range is on another line.
		editor.Select(0, 4);
		editor.CaretOffset = 1;

		var service = new DocumentFormatterService();
		service.FormatDocument(editor, TrimTrailingWhitespaceFormatter.Instance);

		Assert.AreEqual(0, editor.SelectionStart);
		Assert.AreEqual(4, editor.SelectionLength);
		Assert.AreEqual(1, editor.CaretOffset);
	}

	[TestMethod]
	public void FormatDocument_ChangeCoveringSelection_CollapsesSelectionToOriginalCaretLine()
	{
		var editor = new TextEditor { Text = "line 1   \r\nline 2   \r\nline 3   " };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		// The trim formatter changes trailing whitespace on every line, so the changed range
		// covers the caret and the selection; the selection collapses to the original caret line.
		editor.Select(2, 5);
		editor.CaretOffset = editor.Document.GetLineByNumber(2).Offset;

		var service = new DocumentFormatterService();
		service.FormatDocument(editor, TrimTrailingWhitespaceFormatter.Instance);

		Assert.AreEqual(0, editor.SelectionLength);
		Assert.AreEqual(2, editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber);
	}

	[TestMethod]
	public void FormatDocument_FormatterReturnsNull_AppliesNothing()
	{
		var editor = new TextEditor { Text = "content" };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		editor.Document.UndoStack.ClearAll();

		var service = new DocumentFormatterService();
		service.FormatDocument(editor, new NullFormatter());

		// A formatter that declines (null) means "no changes": the document and the undo stack stay
		// untouched.
		Assert.AreEqual("content", editor.Text);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
	}

	[TestMethod]
	public void FormatDocument_FormatterThrows_LeavesDocumentAndUndoStackUntouched()
	{
		var editor = new TextEditor { Text = "content" };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		editor.Document.UndoStack.ClearAll();

		var service = new DocumentFormatterService();

		// The failure happens before any edit is applied, so the document and the undo stack stay
		// untouched and the exception reaches the caller.
		Assert.ThrowsExactly<InvalidOperationException>(() => service.FormatDocument(editor, new ThrowingFormatter()));

		Assert.AreEqual("content", editor.Text);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
	}

	[TestMethod]
	public void FormatDocument_ChangeCoversSelectionWithCaretOutside_CollapsesSelectionToCaretLine()
	{
		var editor = new TextEditor { Text = "keep\r\nchange me   \r\nkeep" };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		// The trailing-whitespace change covers the selection but not the caret, so the selection
		// cannot be mapped and collapses to the end of the line the caret was on.
		editor.Select(16, 4);
		editor.CaretOffset = 3;

		var service = new DocumentFormatterService();
		service.FormatDocument(editor, TrimTrailingWhitespaceFormatter.Instance);

		Assert.AreEqual("keep\r\nchange me\r\nkeep", editor.Text);
		Assert.AreEqual(0, editor.SelectionLength);

		DocumentLine caretLine = editor.Document.GetLineByOffset(editor.CaretOffset);

		Assert.AreEqual(1, caretLine.LineNumber);
		Assert.AreEqual(caretLine.EndOffset, editor.CaretOffset);
	}

	[TestMethod]
	public void FormatDocument_WithFakeEditTarget_AppliesAndPublishesEditorDocument()
	{
		var editor = new TextEditor { Text = "a   \r\nb   " };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var target = new FakeEditTarget(editor);
		var service = new DocumentFormatterService();

		service.FormatDocument(editor, TrimTrailingWhitespaceFormatter.Instance, target);

		string expected = "a" + "\r\n" + "b";

		// The contract-honoring target publishes the formatted content to the editor document
		// before returning.
		Assert.AreEqual(expected, target.Text);
		Assert.AreEqual(expected, editor.Text);
	}

	[TestMethod]
	public void FormatDocument_WithEditTarget_AppliesSingleReplaceThroughTarget()
	{
		string original = "a   \r\nb   ";

		var editor = new TextEditor { Text = original };

		var target = new RecordingEditTarget(original);

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var service = new DocumentFormatterService();
		service.FormatDocument(editor, TrimTrailingWhitespaceFormatter.Instance, target);

		Assert.HasCount(1, target.Operations);

		TextEditOperation operation = target.Operations[0];

		// The edit is minimal: only the range between the first and the last difference is replaced.
		Assert.AreEqual(1, operation.Range.Offset);
		Assert.AreEqual(original.Length, operation.Range.EndOffset);
		Assert.AreEqual("\r\n" + "b", operation.NewText);

		// The batch is prepared from the editor's document, and a target that does not publish leaves the
		// document untouched.
		Assert.AreEqual(original, editor.Text);
	}

	[TestMethod]
	public void FormatDocument_WithEditTargetHoldingOtherContent_ThrowsBeforeFormatting()
	{
		var editor = new TextEditor { Text = "editor content" };

		var target = new RecordingEditTarget("target content");
		var formatter = new AppendingFormatter();

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var service = new DocumentFormatterService();

		// The batch and the caret, selection, and scroll all belong to the editor's document, so a target
		// that reports other content would receive a batch prepared for a text those offsets do not belong
		// to. The edit-target contract requires the same content, and the violation fails fast before the
		// formatter runs instead of applying to one text and moving the caret through another.
		Assert.ThrowsExactly<InvalidOperationException>(() => service.FormatDocument(editor, formatter, target));

		Assert.IsNull(formatter.LastInput);
		Assert.AreEqual(0, target.ApplyCalls);
		Assert.AreEqual("editor content", editor.Text);
	}

	[TestMethod]
	public void FormatDocument_WithEditTarget_UnchangedOutput_DoesNothing()
	{
		var editor = new TextEditor { Text = "same" };

		var target = new RecordingEditTarget("same");

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		var service = new DocumentFormatterService();
		service.FormatDocument(editor, new IdentityFormatter(), target);

		Assert.IsEmpty(target.Operations);
		Assert.AreEqual(0, target.ApplyCalls);
	}

	private sealed class EmptyFormatter : IDocumentFormatter
	{
		public string FormatDocument(string content) => string.Empty;
	}

	private sealed class NullFormatter : IDocumentFormatter
	{
		public string? FormatDocument(string content) => null;
	}

	private sealed class ThrowingFormatter : IDocumentFormatter
	{
		public string FormatDocument(string content) => throw new InvalidOperationException("The formatter failed.");
	}

	private sealed class CollapseFormatter : IDocumentFormatter
	{
		public string FormatDocument(string content) => "collapsed";
	}

	private sealed class RemoveTrailingMarkerFormatter : IDocumentFormatter
	{
		public string FormatDocument(string content) => content.TrimEnd('X');
	}

	private sealed class AppendingFormatter : IDocumentFormatter
	{
		public string? LastInput { get; private set; }

		public string FormatDocument(string content)
		{
			LastInput = content;
			return content + "!";
		}
	}
}
