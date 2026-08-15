#if AVALONIAEDIT
using Nickelony.IDEKit.AvaloniaEdit.Editing;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

[STATestClass]
public sealed class TextEditorEditOperationsTests
{
	[TestMethod]
	public void InsertText_InsertsAtOffsetAndPlacesCaretAfterText()
	{
		var editor = TestHost.CreateEditor("ab");

		editor.InsertText(1, "XY");

		Assert.AreEqual("aXYb", editor.Text);
		Assert.AreEqual(3, editor.CaretOffset);
	}

	[TestMethod]
	public void InsertText_AndApplyEdit_AreEachOneUndoStep()
	{
		var editor = TestHost.CreateEditor("ab");
		editor.Document.UndoStack.ClearAll();

		editor.InsertText(1, "XY");

		editor.Document.UndoStack.Undo();

		Assert.AreEqual("ab", editor.Text);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);

		editor.ApplyEdit(new TextEditRequest(0, 2, "Z"));

		editor.Document.UndoStack.Undo();

		Assert.AreEqual("ab", editor.Text);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
	}

	[TestMethod]
	public void InsertText_EmptyDocument_InsertsAtStartAndPlacesCaretAfterText()
	{
		var editor = TestHost.CreateEditor(string.Empty);

		editor.InsertText(0, "X");

		Assert.AreEqual("X", editor.Text);
		Assert.AreEqual(1, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyEdit_WithCaretOffsetAfterEdit_PlacesCaretAtRequestedOffset()
	{
		var editor = TestHost.CreateEditor("ab");

		editor.ApplyEdit(new TextEditRequest(1, 0, "X") { CaretOffsetAfterEdit = 1 });

		Assert.AreEqual("aXb", editor.Text);
		Assert.AreEqual(1, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyEdit_ReplacesRequestedRange()
	{
		var editor = TestHost.CreateEditor("abcdef");

		editor.ApplyEdit(new TextEditRequest(1, 3, "X"));

		Assert.AreEqual("aXef", editor.Text);
		Assert.AreEqual(2, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyEdit_EmptyDocument_EmptyRange_InsertsText()
	{
		var editor = TestHost.CreateEditor(string.Empty);

		editor.ApplyEdit(new TextEditRequest(0, 0, "XY"));

		Assert.AreEqual("XY", editor.Text);
		Assert.AreEqual(2, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyEdit_RangeEndOverflow_ThrowsArgumentOutOfRangeExceptionForStartOffset()
	{
		var editor = TestHost.CreateEditor("one");

		var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			editor.ApplyEdit(new TextEditRequest(int.MaxValue, 1, "x")));

		// The range guard rejects the request before it can reach a target as a negative range.
		Assert.AreEqual("startOffset", exception.ParamName);
		Assert.AreEqual("one", editor.Text);
	}

	[TestMethod]
	public void InsertText_WithContractEditTarget_AppliesToTargetAndPublishesEditorDocument()
	{
		var editor = TestHost.CreateEditor("ab");
		var target = new ContractEditTarget(editor);

		editor.InsertText(1, "X", editTarget: target);

		Assert.HasCount(1, target.Operations);

		TextEditOperation operation = target.Operations[0];

		Assert.AreEqual(1, operation.StartOffset);
		Assert.AreEqual(1, operation.EndOffset);
		Assert.AreEqual("X", operation.NewText);

		// The contract-honoring target publishes the edit to the editor document before returning,
		// so the caret is derived from the updated document.
		Assert.AreEqual("aXb", target.Text);
		Assert.AreEqual("aXb", editor.Text);
		Assert.AreEqual(2, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyEdit_WithNonUpdatingTarget_ClampsCaretAgainstTheStaleDocument()
	{
		var editor = TestHost.CreateEditor("ab");
		var target = new RecordingEditTarget("ab");

		editor.ApplyEdit(new TextEditRequest(1, 0, "XYZ") { CaretOffsetAfterEdit = 100 }, target);

		// The double deliberately does not update the editor document, so the requested caret offset
		// is clamped against the stale editor length instead of the target's post-edit content.
		Assert.AreEqual("ab", editor.Text);
		Assert.AreEqual(2, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyEdit_CaretOffsetAfterEditBeyondDocument_ClampsToTextLength()
	{
		var editor = TestHost.CreateEditor("ab");

		editor.ApplyEdit(new TextEditRequest(1, 0, "X") { CaretOffsetAfterEdit = 100 });

		Assert.AreEqual(3, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyEdit_NegativeCaretOffsetAfterEdit_ClampsToZero()
	{
		var editor = TestHost.CreateEditor("ab");

		editor.ApplyEdit(new TextEditRequest(1, 0, "X") { CaretOffsetAfterEdit = -5 });

		Assert.AreEqual("aXb", editor.Text);
		Assert.AreEqual(0, editor.CaretOffset);
	}

	[TestMethod]
	public void InsertText_OffsetBeyondDocument_ThrowsArgumentOutOfRangeException()
	{
		var editor = TestHost.CreateEditor("ab");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => editor.InsertText(10, "X"));

		Assert.AreEqual("ab", editor.Text);
		Assert.AreEqual(0, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyEdit_LengthBeyondDocument_ThrowsArgumentOutOfRangeException()
	{
		var editor = TestHost.CreateEditor("ab");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => editor.ApplyEdit(new TextEditRequest(1, 10, "X")));

		Assert.AreEqual("ab", editor.Text);
	}

	[TestMethod]
	public void InsertText_NegativeInsertOffset_ThrowsArgumentOutOfRangeException()
	{
		var editor = TestHost.CreateEditor("ab");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => editor.InsertText(-1, "X"));

		Assert.AreEqual("ab", editor.Text);
		Assert.AreEqual(0, editor.CaretOffset);
	}

	[TestMethod]
	public void ApplyEdit_NegativeStartOffsetOrLength_ThrowsArgumentOutOfRangeException()
	{
		var editor = TestHost.CreateEditor("ab");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => editor.ApplyEdit(new TextEditRequest(-1, 0, "X")));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => editor.ApplyEdit(new TextEditRequest(0, -1, "X")));

		Assert.AreEqual("ab", editor.Text);
	}
}
