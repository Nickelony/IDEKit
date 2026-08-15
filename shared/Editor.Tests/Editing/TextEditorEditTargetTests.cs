#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

[STATestClass]
public sealed class TextEditorEditTargetTests
{
	[TestMethod]
	public void Text_ReturnsDocumentText()
	{
		var target = new TextEditorEditTarget(TestHost.CreateEditor("abcdef"));
		Assert.AreEqual("abcdef", target.Text);
	}

	[TestMethod]
	public void Apply_AppliesOperationsAndAdvancesTheStamp()
	{
		var editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		// The stamp is captured before the content is read and compared before publishing, as the
		// ITextEditTargetVersion workflow describes; the first read attaches the change subscription.
		long capturedStamp = target.Version;

		target.Apply(PreparedBatch.From([new TextEditOperation(1, 4, "X", 0)]));

		Assert.AreEqual("aXef", editor.Text);
		Assert.IsGreaterThan(capturedStamp, target.Version);
	}

	[TestMethod]
	public void Version_DirectEditorEdit_AdvancesTheStamp()
	{
		var editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		long capturedStamp = target.Version;

		editor.Document.Insert(0, "X");

		// The stamp follows every change to the current document, so the documented stale-batch workflow
		// detects direct edits as well.
		Assert.IsGreaterThan(capturedStamp, target.Version);
	}

	[TestMethod]
	public void Apply_NoOperations_LeavesTheDocumentAndItsUndoStackUntouched()
	{
		var editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		long capturedStamp = target.Version;

		target.Apply(PreparedBatch.From([]));

		Assert.AreEqual("abcdef", editor.Text);
		Assert.AreEqual(capturedStamp, target.Version);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
	}

	[TestMethod]
	public void Apply_MultipleOperations_UndoAsSingleStep()
	{
		var editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		target.Apply(PreparedBatch.From([
			new TextEditOperation(4, 5, "E", 0),
			new TextEditOperation(1, 2, "B", 1)]));

		Assert.AreEqual("aBcdEf", editor.Text);

		editor.Undo();

		Assert.AreEqual("abcdef", editor.Text);
	}

	[TestMethod]
	public void Apply_ChangedHandlerSwapsTheEditorDocument_AppliesTheBatchToTheResolvedDocument()
	{
		var originalDocument = new TextDocument("abcdef");
		var replacementDocument = new TextDocument("original");
		var editor = new TextEditor { Document = originalDocument };
		var target = new TextEditorEditTarget(editor);

		// A Changed handler swaps the editor's document after the first operation. The target resolves
		// its document once per call, so the batch still applies to the document it started with, and
		// the new document is left untouched.
		int changedCalls = 0;
		originalDocument.Changed += (_, _) =>
		{
			if (changedCalls++ == 0)
				editor.Document = replacementDocument;
		};

		// The stamp is captured first, which attaches the change subscription to the original document.
		long capturedStamp = target.Version;

		target.Apply(PreparedBatch.From([
			new TextEditOperation(4, 5, "E", 0),
			new TextEditOperation(1, 2, "B", 1)]));

		Assert.AreEqual("aBcdEf", originalDocument.Text);
		Assert.AreEqual("original", replacementDocument.Text);
		Assert.AreEqual("original", editor.Text);

		// The stamp follows the document, and the read after the swap attaches to the new document, so it
		// has advanced in both cases.
		Assert.IsGreaterThan(capturedStamp, target.Version);
	}

	[TestMethod]
	public void Apply_MultipleOperations_RaisesChangedPerOperation_AndAggregateEventsOnce()
	{
		var editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		int changedCalls = 0;
		int textChangedCalls = 0;

		editor.Document.Changed += (_, _) => changedCalls++;
		editor.Document.TextChanged += (_, _) => textChangedCalls++;

		target.Apply(PreparedBatch.From([
			new TextEditOperation(4, 5, "E", 0),
			new TextEditOperation(1, 2, "B", 1)]));

		Assert.AreEqual("aBcdEf", editor.Text);

		// The document update groups the aggregate events but raises Changed per operation.
		Assert.AreEqual(2, changedCalls);
		Assert.AreEqual(1, textChangedCalls);
	}

	[TestMethod]
	public void Apply_OperationBeyondDocument_ThrowsWithoutApplying()
	{
		var editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		long capturedStamp = target.Version;

		// The batch is internally consistent, but the operation still fails against the document.
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			target.Apply(PreparedBatch.From([new TextEditOperation(4, 99, "X", 0)])));

		Assert.AreEqual("abcdef", editor.Text);
		Assert.AreEqual(capturedStamp, target.Version);
	}

	[TestMethod]
	public void Apply_NoOpOperationsWithOutOfRangeOffsets_AreSkipped()
	{
		var editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		// A no-op operation cannot change the document, so it is skipped instead of being replayed - and
		// failing - against the document.
		target.Apply(PreparedBatch.From([
			new TextEditOperation(6, 6, "Z", 0),
			new TextEditOperation(10, 10, string.Empty, 0)]));

		Assert.AreEqual("abcdefZ", editor.Text);
	}

	[TestMethod]
	public void Apply_OutOfOrderOperations_AreOrderedBeforeApplying()
	{
		var editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		// The kernel orders a hand-built batch for application, so an out-of-order input is accepted.
		target.Apply(PreparedBatch.From([
			new TextEditOperation(1, 2, "B", 0),
			new TextEditOperation(4, 5, "E", 1)]));

		Assert.AreEqual("aBcdEf", editor.Text);
	}

	[TestMethod]
	public void Apply_RejectedBatch_LeavesTheDocumentUntouched()
	{
		var editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		// A null entry is rejected by the kernel, so the batch is empty and nothing is applied.
		target.Apply(PreparedBatch.From([null!]));

		Assert.AreEqual("abcdef", editor.Text);
		Assert.AreEqual(0, target.Version);
	}

	[TestMethod]
	public void TryApply_MatchingStamp_AppliesTheBatchAndAdvancesTheStamp()
	{
		var editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		long capturedStamp = target.Version;

		bool applied = target.TryApply(PreparedBatch.From([new TextEditOperation(1, 4, "X", 0)]), capturedStamp);

		Assert.IsTrue(applied);
		Assert.AreEqual("aXef", editor.Text);
		Assert.IsGreaterThan(capturedStamp, target.Version);
	}

	[TestMethod]
	public void TryApply_StaleStamp_DoesNotApplyTheBatch()
	{
		var editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		long capturedStamp = target.Version;

		// A concurrent edit invalidates the captured stamp before the batch is published.
		editor.Document.Insert(0, "X");

		bool applied = target.TryApply(PreparedBatch.From([new TextEditOperation(1, 4, "Y", 0)]), capturedStamp);

		Assert.IsFalse(applied);
		Assert.AreEqual("Xabcdef", editor.Text);
	}

	[TestMethod]
	public void TryApply_NoOperationsAndMatchingStamp_ReportsAppliedWithoutChangingTheDocument()
	{
		var editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		long capturedStamp = target.Version;

		bool applied = target.TryApply(PreparedBatch.From([]), capturedStamp);

		Assert.IsTrue(applied);
		Assert.AreEqual("abcdef", editor.Text);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
	}

	[TestMethod]
	public void Apply_BeforeTheFirstVersionRead_AdvancesTheStamp()
	{
		var editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		// No stamp was captured first: the apply must still record the edit so a later read observes it.
		target.Apply(PreparedBatch.From([new TextEditOperation(1, 4, "X", 0)]));

		Assert.AreEqual("aXef", editor.Text);
		Assert.IsGreaterThan(0, target.Version);
	}

	[TestMethod]
	public void Version_DocumentSwap_AdvancesTheStamp()
	{
		var editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		long capturedStamp = target.Version;

		editor.Document = new TextDocument("other");

		// A swap invalidates any batch prepared against the previous document.
		Assert.IsGreaterThan(capturedStamp, target.Version);
	}

	[TestMethod]
	public void Version_DocumentCleared_AdvancesTheStampAndTextReportsEmpty()
	{
		var editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		long capturedStamp = target.Version;

		editor.Document = null;

		Assert.IsGreaterThan(capturedStamp, target.Version);
		Assert.AreEqual(string.Empty, target.Text);
	}

	[TestMethod]
	public void Version_EditorWithoutDocument_NeverThrowsAndReportsTheLastStamp()
	{
		var target = new TextEditorEditTarget(new TextEditor { Document = null });

		Assert.AreEqual(0, target.Version);
		Assert.AreEqual(string.Empty, target.Text);
	}

	[TestMethod]
	public void Apply_EditorWithoutDocument_ThrowsInvalidOperationException()
	{
		var target = new TextEditorEditTarget(new TextEditor { Document = null });

		Assert.ThrowsExactly<InvalidOperationException>(() =>
			target.Apply(PreparedBatch.From([new TextEditOperation(0, 0, "X", 0)])));
	}

	[TestMethod]
	public void Apply_NoOperationsAndNoDocument_IsSkippedWithoutThrowing()
	{
		var target = new TextEditorEditTarget(new TextEditor { Document = null });

		// A batch whose operations all change nothing needs no document.
		target.Apply(PreparedBatch.From([]));
	}

	[TestMethod]
	public void TryApply_EditorWithoutDocument_ReportsFalse()
	{
		var target = new TextEditorEditTarget(new TextEditor { Document = null });

		bool applied = target.TryApply(PreparedBatch.From([new TextEditOperation(0, 0, "X", 0)]), 0);

		Assert.IsFalse(applied);
	}
}
