#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
using static Nickelony.IDEKit.AvaloniaEdit.Tests.DocumentTestHelpers;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Editing;
using static Nickelony.IDEKit.AvalonEdit.Tests.DocumentTestHelpers;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class TextEditorLineOperationsFirstMatchingTests
{
	[TestMethod]
	public void TryReplaceFirstMatchingLine_ReplacesFirstMatchingLine()
	{
		var editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");

		bool replaced = editor.TryReplaceFirstMatchingLine(
			lineText => lineText == "two" ? "2" : null,
			scrollToLine: false);

		Assert.IsTrue(replaced);
		Assert.AreEqual("one\r\n2\r\nthree", editor.Text);
		Assert.AreEqual(6, editor.CaretOffset);
	}

	[TestMethod]
	public void TryReplaceFirstMatchingLine_MultipleMatchingLines_ReplacesOnlyTheFirstMatch()
	{
		var editor = TestHost.CreateEditor("one\r\ntwo\r\ntwo");
		int selectorCalls = 0;

		bool replaced = editor.TryReplaceFirstMatchingLine(lineText =>
		{
			selectorCalls++;
			return lineText == "two" ? "2" : null;
		}, scrollToLine: false);

		Assert.IsTrue(replaced);
		Assert.AreEqual("one\r\n2\r\ntwo", editor.Text);

		// The scan stops at the first match, so the third line is never probed.
		Assert.AreEqual(2, selectorCalls);
	}

	[TestMethod]
	public void TryReplaceFirstMatchingLine_ScrollToLine_ScrollsTheViewportToTheReplacedLine()
	{
		string text = string.Join("\r\n", Enumerable.Range(1, 400).Select(number => $"line {number}"));
		var editor = new TextEditor { Document = new TextDocument(text), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		editor.UpdateLayout();

		Assert.AreEqual(0.0, editor.TextArea.TextView.VerticalOffset);

		// Premise: the replaced line must start out of view for the scroll assertion to mean anything.
		Assert.IsFalse(IsLineVisible(editor, 300), "Expected line 300 to be outside the visible range.");

		// Line 300 is beyond the first viewport, so scrolling must move the vertical offset.
		bool replaced = editor.TryReplaceFirstMatchingLine(
			lineText => lineText == "line 300" ? "line 300 edited" : null,
			scrollToLine: true);

		Assert.IsTrue(replaced);

		editor.UpdateLayout();

		Assert.IsGreaterThan(0.0, editor.TextArea.TextView.VerticalOffset);
		Assert.IsTrue(IsLineVisible(editor, 300), "Expected the replaced line to be scrolled into view.");
	}

	[TestMethod]
	public void TryReplaceFirstMatchingLine_NoMatch_ReturnsFalseAndLeavesDocument()
	{
		var editor = TestHost.CreateEditor("one\r\ntwo");

		bool replaced = TextEditorLineOperations.TryReplaceFirstMatchingLine(
			editor,
			_ => null,
			scrollToLine: false);

		Assert.IsFalse(replaced);
		Assert.AreEqual("one\r\ntwo", editor.Text);
	}

	[TestMethod]
	public void TryReplaceFirstMatchingLine_GrowingReplacementOnLastLine_PlacesCaretAtEndOfReplacement()
	{
		var editor = TestHost.CreateEditor("one\r\ntwo");

		bool replaced = editor.TryReplaceFirstMatchingLine(
			lineText => lineText == "two" ? "twotwo" : null,
			scrollToLine: false);

		Assert.IsTrue(replaced);
		Assert.AreEqual("one\r\ntwotwo", editor.Text);

		// The caret is placed at the end of the replacement text; the desired offset (5 + 6)
		// exceeds the pre-edit document length and must not be clamped against it.
		Assert.AreEqual(11, editor.CaretOffset);
	}

	[TestMethod]
	public void TryReplaceFirstMatchingLine_GrowingReplacementInSingleLineDocument_PlacesCaretAtEndOfReplacement()
	{
		var editor = TestHost.CreateEditor("ab");

		bool replaced = editor.TryReplaceFirstMatchingLine(
			_ => "abcd",
			scrollToLine: false);

		Assert.IsTrue(replaced);
		Assert.AreEqual("abcd", editor.Text);
		Assert.AreEqual(4, editor.CaretOffset);
	}

	[TestMethod]
	public void TryReplaceFirstMatchingLine_ReplacementEqualsLine_DoesNotEditDocumentButPlacesCaret()
	{
		var editor = TestHost.CreateEditor("one\r\ntwo");

		bool replaced = editor.TryReplaceFirstMatchingLine(
			lineText => lineText == "one" ? "one" : null,
			scrollToLine: false);

		Assert.IsTrue(replaced);
		Assert.AreEqual("one\r\ntwo", editor.Text);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
		Assert.AreEqual(3, editor.CaretOffset);
	}

	[TestMethod]
	public void TryReplaceFirstMatchingLine_WithEditTarget_DelegatesToTarget()
	{
		var editor = TestHost.CreateEditor("one\r\ntwo");
		var target = new RecordingEditTarget();

		bool replaced = editor.TryReplaceFirstMatchingLine(
			lineText => lineText == "one" ? "1" : null,
			scrollToLine: false,
			editTarget: target);

		Assert.IsTrue(replaced);
		Assert.AreEqual(1, target.ApplyCalls);
	}

	[TestMethod]
	public void TryReplaceFirstMatchingLine_WithLoadedEditor_AppliesEditAndPlacesCaret()
	{
		var editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		bool replaced = editor.TryReplaceFirstMatchingLine(
			lineText => lineText == "three" ? "3" : null);

		Assert.IsTrue(replaced);
		Assert.AreEqual("one\r\ntwo\r\n3", editor.Text);
		Assert.AreEqual(editor.Document.TextLength, editor.CaretOffset);
	}
}
