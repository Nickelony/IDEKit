using System.Reflection;
#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;

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
		TextEditor editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		// The stamp is captured before the content is read and compared before publishing, as the
		// ITextEditVersionSource workflow describes; the first capture observes the document.
		long capturedStamp = target.CaptureVersion();

		target.Apply(PreparedBatchFactory.Create([new TextEditOperation(range: TextRange.FromBounds(1, 4), newText: "X", editIndex: 0)]));

		Assert.AreEqual("aXef", editor.Text);
		Assert.IsGreaterThan(capturedStamp, target.CaptureVersion());
	}

	[TestMethod]
	public void Version_DirectEditorEdit_AdvancesTheStamp()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		long capturedStamp = target.CaptureVersion();

		editor.Document.Insert(0, "X");

		// The capture follows every change to the current document, so the documented stale-batch workflow
		// detects direct edits as well.
		Assert.IsGreaterThan(capturedStamp, target.CaptureVersion());
	}

	[TestMethod]
	public void Version_ReadDoesNotObserve_UntilCaptureVersionIsCalled()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		long capturedStamp = target.CaptureVersion();

		// A direct edit is not observed by the side-effect-free Version read; only CaptureVersion observes it.
		editor.Document.Insert(0, "X");

		Assert.AreEqual(capturedStamp, target.Version);
		Assert.IsGreaterThan(capturedStamp, target.CaptureVersion());
	}

	[TestMethod]
	public void Apply_NoOperations_LeavesTheDocumentAndItsUndoStackUntouched()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		long capturedStamp = target.CaptureVersion();

		target.Apply(PreparedBatchFactory.Create([]));

		Assert.AreEqual("abcdef", editor.Text);
		Assert.AreEqual(capturedStamp, target.CaptureVersion());
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
	}

	[TestMethod]
	public void Apply_MultipleOperations_UndoAsSingleStep()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		target.Apply(
			PreparedBatchFactory.Create([
				new TextEditOperation(range: TextRange.FromBounds(4, 5), newText: "E", editIndex: 0),
				new TextEditOperation(range: TextRange.FromBounds(1, 2), newText: "B", editIndex: 1),
			])
		);

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

		// The stamp is captured first, so the later observation sees the document swap.
		long capturedStamp = target.CaptureVersion();

		target.Apply(
			PreparedBatchFactory.Create([
				new TextEditOperation(range: TextRange.FromBounds(4, 5), newText: "E", editIndex: 0),
				new TextEditOperation(range: TextRange.FromBounds(1, 2), newText: "B", editIndex: 1),
			])
		);

		Assert.AreEqual("aBcdEf", originalDocument.Text);
		Assert.AreEqual("original", replacementDocument.Text);
		Assert.AreEqual("original", editor.Text);

		// The stamp follows the document, so the captured stamp is stale after the swap.
		Assert.IsGreaterThan(capturedStamp, target.CaptureVersion());
	}

	[TestMethod]
	public void Apply_MultipleOperations_RaisesChangedOnce_AndAggregateEventsOnce()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");

		var target = new TextEditorEditTarget(editor);

		int changedCalls = 0;
		int textChangedCalls = 0;

		editor.Document.Changed += (_, _) => changedCalls++;
		editor.Document.TextChanged += (_, _) => textChangedCalls++;

		target.Apply(
			PreparedBatchFactory.Create([
				new TextEditOperation(range: TextRange.FromBounds(4, 5), newText: "E", editIndex: 0),
				new TextEditOperation(range: TextRange.FromBounds(1, 2), newText: "B", editIndex: 1),
			])
		);

		Assert.AreEqual("aBcdEf", editor.Text);

		// The batch is one replacement of the changed span, so one Changed event covers both operations.
		Assert.AreEqual(1, changedCalls);
		Assert.AreEqual(1, textChangedCalls);
	}

	[TestMethod]
	public void Apply_MultipleOperations_MapsAnchorsPerOperation()
	{
		TextEditor editor = TestHost.CreateEditor("0123456789");
		var target = new TextEditorEditTarget(editor);

		TextAnchor[] anchors = [.. new[] { 0, 3, 6, 9 }.Select(offset => editor.Document.CreateAnchor(offset))];

		// One operation deletes two characters and the other replaces one, so each anchor outside them moves by the operations before it.
		target.Apply(
			PreparedBatchFactory.Create([
				new TextEditOperation(range: TextRange.FromBounds(7, 8), newText: "<<<>>>", editIndex: 0),
				new TextEditOperation(range: TextRange.FromBounds(1, 3), newText: "", editIndex: 1),
			])
		);

		Assert.AreEqual("03456<<<>>>89", editor.Text);
		Assert.AreEqual(0, anchors[0].Offset);
		Assert.AreEqual(1, anchors[1].Offset);
		Assert.AreEqual(4, anchors[2].Offset);
		Assert.AreEqual(editor.Text.Length - 1, anchors[3].Offset);
	}

	[TestMethod]
	public void Apply_InsertionsAtOneOffset_AppearInEditIndexOrder()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		target.Apply(
			PreparedBatchFactory.Create([
				new TextEditOperation(range: TextRange.FromBounds(4, 4), newText: "2", editIndex: 0),
				new TextEditOperation(range: TextRange.FromBounds(4, 4), newText: "1", editIndex: 1),
				new TextEditOperation(range: TextRange.FromBounds(0, 1), newText: "A", editIndex: 2),
			])
		);

		// The kernel orders the batch so that its insertions at one offset read in edit-index order.
		Assert.AreEqual("Abcd21ef", editor.Text);
	}

	[TestMethod]
	public void Apply_OperationBeyondDocument_ThrowsWithoutApplying()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		long capturedStamp = target.CaptureVersion();

		// The batch is internally consistent, but the operation still fails against the document.
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			target.Apply(PreparedBatchFactory.Create([new TextEditOperation(range: TextRange.FromBounds(4, 99), newText: "X", editIndex: 0)]))
		);

		Assert.AreEqual("abcdef", editor.Text);
		Assert.AreEqual(capturedStamp, target.CaptureVersion());
	}

	[TestMethod]
	public void Apply_NoOpOperationsWithOutOfRangeOffsets_AreSkipped()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		// A no-op operation cannot change the document, so it is skipped instead of being replayed - and
		// failing - against the document.
		target.Apply(
			PreparedBatchFactory.Create([
				new TextEditOperation(range: TextRange.FromBounds(6, 6), newText: "Z", editIndex: 0),
				new TextEditOperation(range: TextRange.FromBounds(10, 10), newText: string.Empty, editIndex: 0),
			])
		);

		Assert.AreEqual("abcdefZ", editor.Text);
	}

	[TestMethod]
	public void Apply_OutOfOrderOperations_AreOrderedBeforeApplying()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		// The kernel orders a hand-built batch for application, so an out-of-order input is accepted.
		target.Apply(
			PreparedBatchFactory.Create([
				new TextEditOperation(range: TextRange.FromBounds(1, 2), newText: "B", editIndex: 0),
				new TextEditOperation(range: TextRange.FromBounds(4, 5), newText: "E", editIndex: 1),
			])
		);

		Assert.AreEqual("aBcdEf", editor.Text);
	}

	[TestMethod]
	public void Apply_RejectedBatch_LeavesTheDocumentUntouched()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		// A null entry is rejected by the kernel, so the batch is empty and nothing is applied.
		target.Apply(PreparedBatchFactory.Create([null!]));

		Assert.AreEqual("abcdef", editor.Text);
		Assert.AreEqual(0, target.Version);
	}

	[TestMethod]
	public void TryApply_MatchingStamp_AppliesTheBatchAndAdvancesTheStamp()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		long capturedStamp = target.CaptureVersion();

		bool applied = target.TryApply(
			PreparedBatchFactory.Create([new TextEditOperation(range: TextRange.FromBounds(1, 4), newText: "X", editIndex: 0)]),
			capturedStamp
		);

		Assert.IsTrue(applied);
		Assert.AreEqual("aXef", editor.Text);
		Assert.IsGreaterThan(capturedStamp, target.CaptureVersion());
	}

	[TestMethod]
	public void TryApply_StaleStamp_DoesNotApplyTheBatch()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		long capturedStamp = target.CaptureVersion();

		// A concurrent edit invalidates the captured stamp before the batch is published.
		editor.Document.Insert(0, "X");

		bool applied = target.TryApply(
			PreparedBatchFactory.Create([new TextEditOperation(range: TextRange.FromBounds(1, 4), newText: "Y", editIndex: 0)]),
			capturedStamp
		);

		Assert.IsFalse(applied);
		Assert.AreEqual("Xabcdef", editor.Text);
	}

	[TestMethod]
	public void TryApply_NoOperationsAndMatchingStamp_ReportsAppliedWithoutChangingTheDocument()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		long capturedStamp = target.CaptureVersion();

		bool applied = target.TryApply(PreparedBatchFactory.Create([]), capturedStamp);

		Assert.IsTrue(applied);
		Assert.AreEqual("abcdef", editor.Text);
		Assert.IsFalse(editor.Document.UndoStack.CanUndo);
	}

	[TestMethod]
	public void Apply_BeforeTheFirstVersionRead_AdvancesTheStamp()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		// No stamp was captured first: the apply must still record the edit so a later observation sees it.
		target.Apply(PreparedBatchFactory.Create([new TextEditOperation(range: TextRange.FromBounds(1, 4), newText: "X", editIndex: 0)]));

		Assert.AreEqual("aXef", editor.Text);
		Assert.IsGreaterThan(0, target.CaptureVersion());
	}

	[TestMethod]
	public void Version_DocumentSwap_AdvancesTheStamp()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		long capturedStamp = target.CaptureVersion();

		editor.Document = new TextDocument("other");

		// A swap invalidates any batch prepared against the previous document.
		Assert.IsGreaterThan(capturedStamp, target.CaptureVersion());
	}

	[TestMethod]
	public void Version_DocumentCleared_AdvancesTheStampAndTextReportsEmpty()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		long capturedStamp = target.CaptureVersion();

		editor.Document = null;

		Assert.IsGreaterThan(capturedStamp, target.CaptureVersion());
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
			target.Apply(PreparedBatchFactory.Create([new TextEditOperation(range: TextRange.FromBounds(0, 0), newText: "X", editIndex: 0)]))
		);
	}

	[TestMethod]
	public void Apply_NoOperationsAndNoDocument_IsSkippedWithoutThrowing()
	{
		var target = new TextEditorEditTarget(new TextEditor { Document = null });

		// A batch whose operations all change nothing needs no document.
		target.Apply(PreparedBatchFactory.Create([]));
	}

	[TestMethod]
	public void TryApply_EditorWithoutDocument_ReportsFalse()
	{
		var target = new TextEditorEditTarget(new TextEditor { Document = null });

		bool applied = target.TryApply(
			PreparedBatchFactory.Create([new TextEditOperation(range: TextRange.FromBounds(0, 0), newText: "X", editIndex: 0)]),
			0
		);

		Assert.IsFalse(applied);
	}

	[TestMethod]
	public void TryApply_SingleOperationBatch_PassesOverTheOperationsOnce()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		long capturedStamp = target.CaptureVersion();

		PreparedTextEdits batch = PreparedBatchFactory.Create([new TextEditOperation(range: TextRange.FromBounds(1, 4), newText: "X", editIndex: 0)]);
		CountingOperations counting = CountOperationReads(batch);

		Assert.IsTrue(target.TryApply(batch, capturedStamp));
		Assert.AreEqual("aXef", editor.Text);

		Assert.AreEqual(1, counting.Enumerations);
		Assert.AreEqual(0, counting.CountReads);
	}

	[TestMethod]
	public void TryApply_MultipleOperationBatch_PassesOverTheOperationsTwice()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		long capturedStamp = target.CaptureVersion();

		PreparedTextEdits batch = PreparedBatchFactory.Create([
			new TextEditOperation(range: TextRange.FromBounds(4, 5), newText: "X", editIndex: 0),
			new TextEditOperation(range: TextRange.FromBounds(2, 3), newText: "Y", editIndex: 1),
			new TextEditOperation(range: TextRange.FromBounds(0, 1), newText: "Z", editIndex: 2),
		]);

		CountingOperations counting = CountOperationReads(batch);

		Assert.IsTrue(target.TryApply(batch, capturedStamp));
		Assert.AreEqual("ZbYdXf", editor.Text);

		// One enumeration finds the operations that change text and builds the offset change map, and one indexed pass builds the replacement.
		Assert.AreEqual(1, counting.Enumerations);
		Assert.AreEqual(1, counting.CountReads);
	}

	[TestMethod]
	public void TryApply_StaleStamp_DoesNotReadTheOperations()
	{
		TextEditor editor = TestHost.CreateEditor("abcdef");
		var target = new TextEditorEditTarget(editor);

		long capturedStamp = target.CaptureVersion();

		editor.Document.Insert(0, "X");

		PreparedTextEdits batch = PreparedBatchFactory.Create([new TextEditOperation(range: TextRange.FromBounds(1, 4), newText: "Y", editIndex: 0)]);
		CountingOperations counting = CountOperationReads(batch);

		Assert.IsFalse(target.TryApply(batch, capturedStamp));

		Assert.AreEqual(0, counting.Enumerations);
		Assert.AreEqual(0, counting.CountReads);
	}

	// The batch copies the operations it is given, so the counting list takes the place of the stored list.
	private static CountingOperations CountOperationReads(PreparedTextEdits batch)
	{
		FieldInfo? field = typeof(PreparedTextEdits).GetField("<Operations>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);

		Assert.IsNotNull(field, "The batch must keep its operations in the backing field of Operations.");

		var counting = new CountingOperations(batch.Operations);

		field.SetValue(batch, counting);

		return counting;
	}

	private sealed class CountingOperations(IReadOnlyList<TextEditOperation> inner) : IReadOnlyList<TextEditOperation>
	{
		internal int Enumerations { get; private set; }

		internal int CountReads { get; private set; }

		public TextEditOperation this[int index] => inner[index];

		public int Count
		{
			get
			{
				CountReads++;
				return inner.Count;
			}
		}

		public IEnumerator<TextEditOperation> GetEnumerator()
		{
			Enumerations++;
			return inner.GetEnumerator();
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
	}
}
