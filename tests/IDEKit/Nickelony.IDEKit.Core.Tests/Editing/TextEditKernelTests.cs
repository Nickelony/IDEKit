using static Nickelony.IDEKit.Core.Tests.TextEditInputs;

namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextEditKernelTests
{
	[TestMethod]
	public void Prepare_NonOverlappingEdits_ReturnsDescendingOperations()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[
				CreateEdit(1, 1, "B"),
				CreateEdit(4, 1, "E")
			]);

		Assert.IsTrue(result.IsValid);
		CollectionAssert.AreEqual(new[] { 4, 1 }, result.Edits.Operations.Select(operation => operation.StartOffset).ToArray());
		Assert.AreEqual("B", result.Edits.Operations[1].NewText);
		Assert.AreEqual(1, result.Edits.Operations[1].Length);
	}

	[TestMethod]
	public void Prepare_AdjacentInsertions_AreValid()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcd"),
			[
				CreateEdit(1, 0, "x"),
				CreateEdit(2, 0, "y")
			]);

		Assert.IsTrue(result.IsValid);
		Assert.AreEqual(2, result.Edits.Operations.Count);
		Assert.AreEqual(2, result.Edits.Operations[0].StartOffset);
		Assert.AreEqual(1, result.Edits.Operations[1].StartOffset);
	}

	[TestMethod]
	public void Prepare_SameOffsetInsertions_AreAcceptedInEditOrder()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcd"),
			[CreateEdit(1, 0, "x"), CreateEdit(1, 0, "y")]);

		Assert.IsTrue(result.IsValid);
		Assert.AreEqual(2, result.Edits.Operations.Count);

		// Operations apply in list order (highest source offset first, and at one offset the highest
		// edit index first), so applying the list inserts the texts in edit order: "x" then "y".
		Assert.AreEqual(1, result.Edits.Operations[0].StartOffset);
		Assert.AreEqual(1, result.Edits.Operations[0].EditIndex);
		Assert.AreEqual("y", result.Edits.Operations[0].NewText);
		Assert.AreEqual(0, result.Edits.Operations[1].EditIndex);
		Assert.AreEqual("x", result.Edits.Operations[1].NewText);
	}

	[TestMethod]
	public void Prepare_InsertionInsideReplacement_IsRejected()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(1, 3, "x"), CreateEdit(2, 0, "y")]);

		Assert.IsFalse(result.IsValid);
		Assert.AreEqual(1, result.Issues.Count);
		Assert.AreEqual(TextEditPreparationIssueKind.InsertionInsideReplacement, result.Issues[0].Kind);
		Assert.AreEqual(1, result.Issues[0].EditIndex);
		Assert.AreEqual(0, result.Issues[0].RelatedEditIndex);
	}

	[TestMethod]
	public void Prepare_OverlappingReplacements_AreRejected()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(1, 3, "x"), CreateEdit(3, 2, "y")]);

		Assert.IsFalse(result.IsValid);
		Assert.AreEqual(1, result.Issues.Count);
		Assert.AreEqual(TextEditPreparationIssueKind.ReplacementOverlap, result.Issues[0].Kind);
		Assert.AreEqual(1, result.Issues[0].EditIndex);
		Assert.AreEqual(0, result.Issues[0].RelatedEditIndex);
	}

	[TestMethod]
	public void Prepare_InvalidRange_ReturnsIssueWithoutOperations()
	{
		var snapshot = new StringTextSnapshot("text");
		TextEditPreparationResult result = TextEditKernel.Prepare(
			snapshot,
			[CreateEdit(4, 1, "invalid")]);

		Assert.IsFalse(result.IsValid);
		Assert.AreEqual(1, result.Issues.Count);
		Assert.AreEqual(TextEditPreparationIssueKind.RangeOutOfBounds, result.Issues[0].Kind);
		Assert.AreEqual(0, result.Issues[0].EditIndex);
		Assert.AreEqual(0, result.Edits.Operations.Count);
		Assert.AreEqual("text", snapshot.GetText(0, snapshot.TextLength));
	}

	[TestMethod]
	public void Prepare_NullEdit_IsReportedAsNullEdit()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcd"),
			[null, CreateEdit(0, 1, "A")]);

		Assert.IsFalse(result.IsValid);
		Assert.AreEqual(1, result.Issues.Count);
		Assert.AreEqual(TextEditPreparationIssueKind.NullEdit, result.Issues[0].Kind);
		Assert.AreEqual(0, result.Issues[0].EditIndex);
		Assert.IsNull(result.Issues[0].RelatedEditIndex);
		Assert.AreEqual(0, result.Edits.Operations.Count);
	}

	[TestMethod]
	public void Prepare_NullReplacementText_IsReportedAsInvalidReplacementText()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcd"),
			[new TextEditInput(new TextRange(0, 1), null!)]);

		Assert.IsFalse(result.IsValid);
		Assert.AreEqual(1, result.Issues.Count);
		Assert.AreEqual(TextEditPreparationIssueKind.NullReplacementText, result.Issues[0].Kind);
		Assert.AreEqual(0, result.Issues[0].EditIndex);
		Assert.AreEqual(0, result.Edits.Operations.Count);
	}

	[TestMethod]
	public void Prepare_UsesUtf16OffsetsForSupplementaryCharacters()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("A\U0001F600B"),
			[CreateEdit(3, 1, "C")]);

		Assert.IsTrue(result.IsValid);
		Assert.AreEqual(3, result.Edits.Operations[0].StartOffset);
		Assert.AreEqual(4, result.Edits.Operations[0].EndOffset);
	}

	[TestMethod]
	public void Prepare_EmptyBatch_IsValidWithoutOperations()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(new StringTextSnapshot("abc"), []);

		Assert.IsTrue(result.IsValid);
		Assert.AreEqual(0, result.Edits.Operations.Count);
		Assert.AreEqual(0, result.Issues.Count);
	}

	[TestMethod]
	public void Prepare_ThreeSameOffsetInsertions_KeepEditOrder()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcd"),
			[CreateEdit(1, 0, "x"), CreateEdit(1, 0, "y"), CreateEdit(1, 0, "z")]);

		Assert.IsTrue(result.IsValid);
		Assert.AreEqual(3, result.Edits.Operations.Count);

		// The application list reverses the ascending candidate order, so the last edit applies first
		// and its text lands last in the resulting text.
		Assert.AreEqual("z", result.Edits.Operations[0].NewText);
		Assert.AreEqual("y", result.Edits.Operations[1].NewText);
		Assert.AreEqual("x", result.Edits.Operations[2].NewText);
	}

	[TestMethod]
	public void Prepare_ManySameOffsetInsertions_StayLinearAndValid()
	{
		const int insertionCount = 5000;
		var edits = new TextEditInput[insertionCount];

		for (int index = 0; index < insertionCount; index++)
			edits[index] = CreateEdit(2, 0, index.ToString());

		TextEditPreparationResult result = TextEditKernel.Prepare(new StringTextSnapshot("abcd"), edits);

		// The conflict pass skips insertion-to-insertion pairs, so a batch of same-offset insertions
		// stays linear and valid; the application order places the lowest edit index first.
		Assert.IsTrue(result.IsValid);
		Assert.AreEqual(insertionCount, result.Edits.Operations.Count);
		Assert.AreEqual(insertionCount - 1, result.Edits.Operations[0].EditIndex);
		Assert.AreEqual(0, result.Edits.Operations[insertionCount - 1].EditIndex);
	}

	[TestMethod]
	public void Prepare_PathologicalOverlap_ReportsCappedConflictIssues()
	{
		const int replacementCount = 600;
		var edits = new TextEditInput[replacementCount];

		for (int index = 0; index < replacementCount; index++)
			edits[index] = CreateEdit(index, 2, "x");

		TextEditPreparationResult result = TextEditKernel.Prepare(new StringTextSnapshot(new string('a', 1000)), edits);

		// Every replacement overlaps its later neighbor, so the full pairwise report would grow
		// quadratically; the kernel caps the reported issues while the batch stays rejected
		// either way.
		Assert.IsFalse(result.IsValid);
		Assert.AreEqual(512, result.Issues.Count);
		Assert.AreEqual(TextEditPreparationIssueKind.ReplacementOverlap, result.Issues[0].Kind);
	}

	[TestMethod]
	public void Prepare_InsertionAtReplacementEnd_IsAccepted()
	{
		// The insertion touches the replacement end rather than its interior, so the batch stays valid.
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(1, 2, "x"), CreateEdit(3, 0, "y")]);

		Assert.IsTrue(result.IsValid);
		Assert.AreEqual(2, result.Edits.Operations.Count);
		Assert.AreEqual(3, result.Edits.Operations[0].StartOffset);
		Assert.AreEqual(1, result.Edits.Operations[1].StartOffset);
	}

	[TestMethod]
	public void Prepare_InsertionAtReplacementBoundary_IsAccepted()
	{
		// The insertion touches the replacement start rather than its interior, so the batch stays
		// valid; the replacement applies before the insertion at the shared offset.
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(1, 2, "x"), CreateEdit(1, 0, "y")]);

		Assert.IsTrue(result.IsValid);
		Assert.AreEqual(2, result.Edits.Operations.Count);
		Assert.AreEqual(3, result.Edits.Operations[0].EndOffset);
		Assert.AreEqual(1, result.Edits.Operations[1].StartOffset);
		Assert.AreEqual(1, result.Edits.MapOffset(1));
	}

	[TestMethod]
	public void Prepare_PropagatesEditIndexIntoOperations()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(1, 1, "B"), CreateEdit(4, 1, "E")]);

		Assert.IsTrue(result.IsValid);

		// The descending order reverses the candidate list, so each operation must keep the source
		// index of the edit it was created from instead of the reversed position.
		Assert.AreEqual(1, result.Edits.Operations[0].EditIndex);
		Assert.AreEqual("E", result.Edits.Operations[0].NewText);
		Assert.AreEqual(0, result.Edits.Operations[1].EditIndex);
		Assert.AreEqual("B", result.Edits.Operations[1].NewText);
	}

	[TestMethod]
	public void Prepare_ReportsDocumentedIssueMessages()
	{
		TextEditPreparationResult nullEdit = TextEditKernel.Prepare(new StringTextSnapshot("abcd"), [null]);
		TextEditPreparationResult nullText = TextEditKernel.Prepare(
			new StringTextSnapshot("abcd"),
			[new TextEditInput(new TextRange(0, 1), null!)]);
		TextEditPreparationResult invalidRange = TextEditKernel.Prepare(
			new StringTextSnapshot("abcd"),
			[CreateEdit(4, 1, "x")]);
		TextEditPreparationResult insertionInsideReplacement = TextEditKernel.Prepare(
			new StringTextSnapshot("abcd"),
			[CreateEdit(1, 2, "x"), CreateEdit(2, 0, "y")]);
		TextEditPreparationResult overlappingReplacements = TextEditKernel.Prepare(
			new StringTextSnapshot("abcd"),
			[CreateEdit(1, 2, "x"), CreateEdit(2, 2, "y")]);

		// The issues carry human-readable messages so hosts can surface them without wording
		// their own text; the category is the branch-on value.
		Assert.AreEqual("The edit is null.", nullEdit.Issues[0].Message);
		Assert.AreEqual(TextEditPreparationIssueKind.NullEdit, nullEdit.Issues[0].Kind);
		Assert.AreEqual("The edit replacement text is null.", nullText.Issues[0].Message);
		Assert.AreEqual(TextEditPreparationIssueKind.NullReplacementText, nullText.Issues[0].Kind);
		Assert.AreEqual("The edit range is outside the document.", invalidRange.Issues[0].Message);
		Assert.AreEqual(TextEditPreparationIssueKind.RangeOutOfBounds, invalidRange.Issues[0].Kind);
		Assert.AreEqual("An insertion starts inside a replacement range.", insertionInsideReplacement.Issues[0].Message);
		Assert.AreEqual(
			TextEditPreparationIssueKind.InsertionInsideReplacement,
			insertionInsideReplacement.Issues[0].Kind);
		Assert.AreEqual("The edit ranges overlap.", overlappingReplacements.Issues[0].Message);
		Assert.AreEqual(TextEditPreparationIssueKind.ReplacementOverlap, overlappingReplacements.Issues[0].Kind);
	}

	[TestMethod]
	public void Prepare_SameOffsetInsertionsAtReplacementBoundary_ReportOnlyTheOverlap()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[
				CreateEdit(1, 3, "X"),
				CreateEdit(1, 0, "a"),
				CreateEdit(1, 0, "b"),
				CreateEdit(3, 2, "Y")
			]);

		Assert.IsFalse(result.IsValid);
		Assert.AreEqual(0, result.Edits.Operations.Count);

		// The same-offset insertions are valid; the overlapping replacement is reported against the
		// replacement that precedes it.
		Assert.AreEqual(1, result.Issues.Count);
		Assert.AreEqual(3, result.Issues[0].EditIndex);
		Assert.AreEqual(0, result.Issues[0].RelatedEditIndex);
		Assert.AreEqual(TextEditPreparationIssueKind.ReplacementOverlap, result.Issues[0].Kind);
	}

	[TestMethod]
	public void Prepare_InsertionInsideReplacement_NamesTheTriggeringEditAndTheReplacement()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcd"),
			[CreateEdit(1, 2, "x"), CreateEdit(2, 0, "y")]);

		Assert.IsFalse(result.IsValid);
		Assert.AreEqual(1, result.Issues.Count);

		// The issue names the triggering edit and its counterpart: the insertion inside the
		// replacement range names the replacement as related.
		Assert.AreEqual(1, result.Issues[0].EditIndex);
		Assert.AreEqual(0, result.Issues[0].RelatedEditIndex);
	}

	[TestMethod]
	public void Prepare_MixedValidAndInvalidEdits_ReturnNoOperations()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(1, 1, "B"), CreateEdit(99, 1, "bad")]);

		Assert.IsFalse(result.IsValid);
		Assert.AreEqual(1, result.Issues.Count);
		Assert.AreEqual(1, result.Issues[0].EditIndex);
		Assert.AreEqual(0, result.Edits.Operations.Count);
	}

	[TestMethod]
	public void Prepare_MixedInvalidEditAndConflict_ReportsInvalidFirstAndKeepsBothCategories()
	{
		// Invalid edits are diagnosed in the input loop; conflicts are appended afterwards. The
		// ordering contract covers mixed batches, so hosts can present invalid edits first.
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[
				CreateEdit(99, 1, "bad"),
				CreateEdit(1, 2, "x"),
				CreateEdit(2, 2, "y")
			]);

		Assert.IsFalse(result.IsValid);
		Assert.AreEqual(2, result.Issues.Count);
		Assert.AreEqual(TextEditPreparationIssueKind.RangeOutOfBounds, result.Issues[0].Kind);
		Assert.AreEqual(0, result.Issues[0].EditIndex);
		Assert.AreEqual("The edit range is outside the document.", result.Issues[0].Message);
		Assert.AreEqual(TextEditPreparationIssueKind.ReplacementOverlap, result.Issues[1].Kind);
		Assert.AreEqual(2, result.Issues[1].EditIndex);
		Assert.AreEqual("The edit ranges overlap.", result.Issues[1].Message);
		Assert.AreEqual(0, result.Edits.Operations.Count);
	}

	[TestMethod]
	public void Prepare_NoOpEdits_AreIgnoredAndBatchStaysValid()
	{
		// An empty range with an empty replacement text changes nothing, so it is not classified as
		// an insertion.
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abc"),
			[CreateEdit(1, 0, string.Empty), CreateEdit(1, 0, string.Empty)]);

		Assert.IsTrue(result.IsValid);
		Assert.AreEqual(0, result.Edits.Operations.Count);
	}

	[TestMethod]
	public void Prepare_NoOpEditOutsideTheDocument_IsStillRejected()
	{
		// Ranges are validated before the no-op check, so an out-of-range no-op reports the range
		// issue instead of being silently ignored.
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abc"),
			[CreateEdit(4, 0, string.Empty)]);

		Assert.IsFalse(result.IsValid);
		Assert.AreEqual(1, result.Issues.Count);
		Assert.AreEqual("The edit range is outside the document.", result.Issues[0].Message);
	}

	[TestMethod]
	public void Prepare_NoOpEditInsideDeletion_DoesNotConflict()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(1, 3, string.Empty), CreateEdit(2, 0, string.Empty)]);

		Assert.IsTrue(result.IsValid);
		Assert.AreEqual(1, result.Edits.Operations.Count);
		Assert.AreEqual(1, result.Edits.Operations[0].StartOffset);
	}

	[TestMethod]
	public void Prepare_ConflictIssues_AgreeWithPreparedTextEditsValidation()
	{
		// The kernel and the batch constructor share one conflict-rule set, so a batch the kernel
		// accepts always constructs and a rejected batch fails construction with the same rules;
		// same-offset insertions are accepted by both.
		TextEditPreparationResult duplicateResult = TextEditKernel.Prepare(
			new StringTextSnapshot("abc"),
			[CreateEdit(1, 0, "x"), CreateEdit(1, 0, "y")]);

		Assert.IsTrue(duplicateResult.IsValid);
		Assert.AreEqual(2, new PreparedTextEdits(duplicateResult.Edits.Operations).Operations.Count);

		TextEditPreparationResult intersectionResult = TextEditKernel.Prepare(
			new StringTextSnapshot("abc"),
			[CreateEdit(0, 2, "x"), CreateEdit(1, 0, "y")]);

		Assert.IsFalse(intersectionResult.IsValid);
		Assert.ThrowsExactly<ArgumentException>(() => new PreparedTextEdits(
			[new TextEditOperation(0, 2, "x", 0), new TextEditOperation(1, 1, "y", 1)]));

		TextEditPreparationResult acceptedResult = TextEditKernel.Prepare(
			new StringTextSnapshot("abc"),
			[CreateEdit(1, 2, "x"), CreateEdit(1, 0, "y"), CreateEdit(3, 0, "z")]);

		Assert.IsTrue(acceptedResult.IsValid);
		Assert.AreEqual(3, new PreparedTextEdits(acceptedResult.Edits.Operations).Operations.Count);
	}
}
