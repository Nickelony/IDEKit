#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
using Nickelony.IDEKit.Core.Text;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.Core.Text;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

[STATestClass]
public sealed class TextEditorLineOperationsTests
{
	[TestMethod]
	public void SelectLine_SelectsContentExcludingTerminator()
	{
		var editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		DocumentLine line = editor.Document.GetLineByNumber(2);

		editor.SelectLine(line);

		Assert.AreEqual(5, editor.SelectionStart);
		Assert.AreEqual(3, editor.SelectionLength);
	}

	[TestMethod]
	public void ReplaceLine_ReplacesContentAndPreservesTerminator()
	{
		var editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		DocumentLine line = editor.Document.GetLineByNumber(2);

		editor.ReplaceLine(line, "TWO");

		Assert.AreEqual("one\r\nTWO\r\nthree", editor.Text);
	}

	[TestMethod]
	public void ReplaceLine_SelectReplacementFalse_PlacesCaretAfterTheReplacement()
	{
		var editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		DocumentLine line = editor.Document.GetLineByNumber(2);

		editor.ReplaceLine(line, "TWO");

		Assert.AreEqual("one\r\nTWO\r\nthree", editor.Text);
		Assert.AreEqual(0, editor.SelectionLength);
		Assert.AreEqual(8, editor.CaretOffset);
	}

	[TestMethod]
	public void ReplaceLine_SelectReplacementTrue_KeepsReplacementSelected()
	{
		var editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		DocumentLine line = editor.Document.GetLineByNumber(2);

		editor.ReplaceLine(line, "TWO", selectReplacement: true);

		Assert.AreEqual("one\r\nTWO\r\nthree", editor.Text);
		Assert.AreEqual(5, editor.SelectionStart);
		Assert.AreEqual(3, editor.SelectionLength);
		Assert.AreEqual(8, editor.CaretOffset);
	}

	[TestMethod]
	public void ReplaceLine_MultiLineReplacement_PlacesCaretAfterReplacement()
	{
		var editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		DocumentLine line = editor.Document.GetLineByNumber(2);

		editor.ReplaceLine(line, "A\r\nB");

		Assert.AreEqual("one\r\nA\r\nB\r\nthree", editor.Text);
		Assert.AreEqual(0, editor.SelectionLength);
		Assert.AreEqual(9, editor.CaretOffset);
	}

	[TestMethod]
	public void ReplaceLine_MultiLineReplacement_SelectReplacementTrue_KeepsSelection()
	{
		var editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		DocumentLine line = editor.Document.GetLineByNumber(2);

		editor.ReplaceLine(line, "A\r\nB", selectReplacement: true);

		Assert.AreEqual("one\r\nA\r\nB\r\nthree", editor.Text);
		Assert.AreEqual(5, editor.SelectionStart);
		Assert.AreEqual(4, editor.SelectionLength);
	}

	[TestMethod]
	public void ReplaceContent_ReplacesWholeDocumentAndMovesCaretToEnd()
	{
		var editor = TestHost.CreateEditor("one\r\ntwo");

		editor.ReplaceContent("x");

		Assert.AreEqual("x", editor.Text);
		Assert.AreEqual(0, editor.SelectionLength);
		Assert.AreEqual(1, editor.CaretOffset);
	}

	[TestMethod]
	public void ReplaceLine_IdenticalText_LeavesUndoStackUntouched()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo");
		editor.Document.UndoStack.ClearAll();

		editor.ReplaceLine(editor.Document.GetLineByNumber(1), "one");

		Assert.AreEqual("one\r\ntwo", editor.Text);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
		Assert.AreEqual(3, editor.CaretOffset);
	}

	[TestMethod]
	public void ReplaceLine_IdenticalTextWithSelection_KeepsReplacementSelected()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo");
		editor.Document.UndoStack.ClearAll();

		editor.ReplaceLine(editor.Document.GetLineByNumber(1), "one", selectReplacement: true);

		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
		Assert.AreEqual(0, editor.SelectionStart);
		Assert.AreEqual(3, editor.SelectionLength);
	}

	[TestMethod]
	public void ReplaceContent_IdenticalContent_LeavesUndoStackUntouched()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo");

		editor.Document.UndoStack.ClearAll();
		editor.Select(0, 2);

		editor.ReplaceContent("one\r\ntwo");

		Assert.AreEqual("one\r\ntwo", editor.Text);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
		Assert.AreEqual(editor.Document.TextLength, editor.CaretOffset);
		Assert.AreEqual(0, editor.SelectionLength);
	}

	[TestMethod]
	public void ReplaceLine_WithContractEditTarget_AppliesOperationAndPublishesEditorDocument()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		DocumentLine line = editor.Document.GetLineByNumber(2);
		var target = new ContractEditTarget(editor);

		editor.ReplaceLine(line, "TWO", editTarget: target);

		Assert.HasCount(1, target.Operations);

		TextEditOperation operation = target.Operations[0];

		Assert.AreEqual(line.Offset, operation.StartOffset);
		Assert.AreEqual(line.EndOffset, operation.EndOffset);
		Assert.AreEqual("TWO", operation.NewText);

		// The contract-honoring target publishes the replacement to the editor document before
		// returning, so the post-edit caret is derived from the updated document.
		Assert.AreEqual("one\r\nTWO\r\nthree", target.Text);
		Assert.AreEqual("one\r\nTWO\r\nthree", editor.Text);
		Assert.AreEqual(line.Offset + "TWO".Length, editor.CaretOffset);
		Assert.AreEqual(0, editor.SelectionLength);
	}

	[TestMethod]
	public void ReplaceContent_WithContractEditTarget_AppliesOperationAndPublishesEditorDocument()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo");
		int originalLength = editor.Document.TextLength;
		var target = new ContractEditTarget(editor);

		editor.ReplaceContent("x", target);

		Assert.HasCount(1, target.Operations);

		TextEditOperation operation = target.Operations[0];

		Assert.AreEqual(0, operation.StartOffset);
		Assert.AreEqual(originalLength, operation.EndOffset);
		Assert.AreEqual("x", operation.NewText);

		Assert.AreEqual("x", target.Text);
		Assert.AreEqual("x", editor.Text);
		Assert.AreEqual(1, editor.CaretOffset);
	}

	[TestMethod]
	public void SelectLine_LineFromAnotherDocument_Throws()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo");
		var otherDocument = new TextDocument("alpha\r\nbeta");

		Assert.ThrowsExactly<ArgumentException>(() => editor.SelectLine(otherDocument.GetLineByNumber(1)));

		Assert.AreEqual(0, editor.SelectionStart);
		Assert.AreEqual(0, editor.SelectionLength);
	}

	[TestMethod]
	public void SelectLine_DeletedLine_Throws()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo");
		DocumentLine deletedLine = editor.Document.GetLineByNumber(2);

		editor.Document.Replace(0, editor.Document.TextLength, "single");

		Assert.IsTrue(deletedLine.IsDeleted);
		Assert.ThrowsExactly<ArgumentException>(() => editor.SelectLine(deletedLine));
	}

	[TestMethod]
	public void SelectLine_LineCapturedBeforeAnUnrelatedEdit_StillResolves()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo\r\nthree");
		DocumentLine line = editor.Document.GetLineByNumber(2);

		editor.Document.Insert(0, "X");

		// A line handle of this document stays valid across edits; its offsets follow the edit.
		editor.SelectLine(line);

		Assert.AreEqual("two", editor.SelectedText);
		Assert.AreEqual(6, editor.SelectionStart);
	}

	[TestMethod]
	public void ReplaceLine_LineFromAnotherDocument_ThrowsWithoutChangingTheDocument()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo");
		var otherDocument = new TextDocument("alpha\r\nbeta");

		Assert.ThrowsExactly<ArgumentException>(() =>
			editor.ReplaceLine(otherDocument.GetLineByNumber(1), "X"));

		Assert.AreEqual("one\r\ntwo", editor.Text);
	}

	[TestMethod]
	public void ReplaceLine_DeletedLine_ThrowsWithoutChangingTheDocument()
	{
		TextEditor editor = TestHost.CreateEditor("one\r\ntwo");
		DocumentLine deletedLine = editor.Document.GetLineByNumber(2);

		editor.Document.Replace(0, editor.Document.TextLength, "single");

		Assert.ThrowsExactly<ArgumentException>(() => editor.ReplaceLine(deletedLine, "X"));
		Assert.AreEqual("single", editor.Text);
	}
}
