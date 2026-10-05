using static Nickelony.IDEKit.Core.Tests.TextIncrementalEditTestHelpers;

namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Verifies the lenient (skipping) preparation mode of the edit kernel, which keeps a usable batch instead of rejecting the whole batch when an entry
/// is malformed or conflicts.
/// </summary>
[TestClass]
public sealed class TextEditKernelSkipTests
{
	[TestMethod]
	public void PrepareSkippingInvalidEntries_AllValidEntries_ReturnsEveryOperationInDescendingOrder()
	{
		TextEditSkipResult result = TextEditKernel.PrepareSkippingInvalidEntries(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(1, 1, "B"), CreateEdit(4, 1, "E")]
		);

		Assert.IsFalse(result.HasSkippedEntries);
		Assert.IsEmpty(result.SkippedIssues);
		Assert.AreSequenceEqual([4, 1], [.. result.Edits.Operations.Select(operation => operation.Range.Offset)]);
	}

	[TestMethod]
	public void PrepareSkippingInvalidEntries_NullReplacementText_IsSkipped()
	{
		TextEditSkipResult result = TextEditKernel.PrepareSkippingInvalidEntries(
			new StringTextSnapshot("abcd"),
			[new TextIncrementalEdit(new TextRange(0, 1), null!), CreateEdit(2, 1, "C")]
		);

		Assert.HasCount(1, result.Edits.Operations);
		Assert.AreEqual(1, result.Edits.Operations[0].EditIndex);
		Assert.AreEqual(TextEditPreparationIssueKind.NullReplacementText, result.SkippedIssues[0].Kind);
		Assert.AreEqual(0, result.SkippedIssues[0].EditIndex);
	}

	[TestMethod]
	public void PrepareSkippingInvalidEntries_RangeOutsideTheDocument_IsSkipped()
	{
		TextEditSkipResult result = TextEditKernel.PrepareSkippingInvalidEntries(
			new StringTextSnapshot("abcd"),
			[CreateEdit(0, 1, "A"), CreateEdit(50, 1, "X")]
		);

		Assert.HasCount(1, result.Edits.Operations);
		Assert.AreEqual(TextEditPreparationIssueKind.RangeOutOfBounds, result.SkippedIssues[0].Kind);
		Assert.AreEqual(1, result.SkippedIssues[0].EditIndex);
	}

	[TestMethod]
	public void PrepareSkippingInvalidEntries_OutOfRangeNoOpEdit_IsStillSkipped()
	{
		// The range is validated before the no-op check, so an out-of-range no-op is reported instead of
		// being silently ignored.
		TextEditSkipResult result = TextEditKernel.PrepareSkippingInvalidEntries(new StringTextSnapshot("abc"), [CreateEdit(4, 0, string.Empty)]);

		Assert.IsEmpty(result.Edits.Operations);
		Assert.AreEqual(TextEditPreparationIssueKind.RangeOutOfBounds, result.SkippedIssues[0].Kind);
	}

	[TestMethod]
	public void PrepareSkippingInvalidEntries_InsertionInsideReplacement_IsSkipped()
	{
		TextEditSkipResult result = TextEditKernel.PrepareSkippingInvalidEntries(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(1, 3, "x"), CreateEdit(2, 0, "y")]
		);

		Assert.HasCount(1, result.Edits.Operations);
		Assert.AreEqual(0, result.Edits.Operations[0].EditIndex);
		Assert.HasCount(1, result.SkippedIssues);
		Assert.AreEqual(TextEditPreparationIssueKind.InsertionInsideReplacement, result.SkippedIssues[0].Kind);
		Assert.AreEqual(1, result.SkippedIssues[0].EditIndex);
		Assert.AreEqual(0, result.SkippedIssues[0].RelatedEditIndex);
	}

	[TestMethod]
	public void PrepareSkippingInvalidEntries_InsertionAtReplacementBoundary_IsAccepted()
	{
		TextEditSkipResult result = TextEditKernel.PrepareSkippingInvalidEntries(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(1, 2, "x"), CreateEdit(1, 0, "y"), CreateEdit(3, 0, "z")]
		);

		// An insertion that touches a replacement's boundary is accepted, so no entry is skipped.
		Assert.IsFalse(result.HasSkippedEntries);
		Assert.HasCount(3, result.Edits.Operations);
	}

	[TestMethod]
	public void PrepareSkippingInvalidEntries_OverlappingReplacements_KeepTheFirstEntryEvenWhenItStartsLater()
	{
		TextEditSkipResult result = TextEditKernel.PrepareSkippingInvalidEntries(
			new StringTextSnapshot("abcdefgh"),
			[CreateEdit(5, 2, "A"), CreateEdit(2, 4, "B")]
		);

		// Entries are evaluated in caller order, so the first entry wins a conflict even when a later
		// entry starts before it.
		Assert.HasCount(1, result.Edits.Operations);
		Assert.AreEqual(0, result.Edits.Operations[0].EditIndex);
		Assert.HasCount(1, result.SkippedIssues);
		Assert.AreEqual(TextEditPreparationIssueKind.ReplacementOverlap, result.SkippedIssues[0].Kind);
		Assert.AreEqual(1, result.SkippedIssues[0].EditIndex);
		Assert.AreEqual(0, result.SkippedIssues[0].RelatedEditIndex);
	}

	[TestMethod]
	public void PrepareSkippingInvalidEntries_SkippedEntryDoesNotShadowALaterCompatibleEntry()
	{
		TextEditSkipResult result = TextEditKernel.PrepareSkippingInvalidEntries(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(0, 2, "A"), CreateEdit(1, 2, "B"), CreateEdit(2, 2, "C")]
		);

		// The second entry is dropped for overlapping the first, so the third is compared with the first
		// only and accepted at its touching boundary. The application order is descending, so the third
		// entry (the highest offset) is applied first.
		Assert.HasCount(2, result.Edits.Operations);
		Assert.AreEqual(2, result.Edits.Operations[0].EditIndex);
		Assert.AreEqual(0, result.Edits.Operations[1].EditIndex);
		Assert.HasCount(1, result.SkippedIssues);
		Assert.AreEqual(1, result.SkippedIssues[0].EditIndex);
	}

	[TestMethod]
	public void PrepareSkippingInvalidEntries_NoOpEntry_ProducesNeitherOperationNorIssue()
	{
		TextEditSkipResult result = TextEditKernel.PrepareSkippingInvalidEntries(
			new StringTextSnapshot("abc"),
			[CreateEdit(1, 0, string.Empty), CreateEdit(1, 1, "B")]
		);

		Assert.IsFalse(result.HasSkippedEntries);
		Assert.HasCount(1, result.Edits.Operations);
		Assert.AreEqual("B", result.Edits.Operations[0].NewText);
	}

	[TestMethod]
	public void PrepareSkippingInvalidEntries_SameOffsetInsertions_KeepEditOrder()
	{
		TextEditSkipResult result = TextEditKernel.PrepareSkippingInvalidEntries(
			new StringTextSnapshot("abcd"),
			[CreateEdit(1, 0, "x"), CreateEdit(1, 0, "y")]
		);

		// The application list reverses the ascending candidate order, so the later edit applies first and
		// its text lands last in the resulting text.
		Assert.IsFalse(result.HasSkippedEntries);
		Assert.HasCount(2, result.Edits.Operations);
		Assert.AreEqual("y", result.Edits.Operations[0].NewText);
		Assert.AreEqual("x", result.Edits.Operations[1].NewText);
	}

	[TestMethod]
	public void PrepareSkippingInvalidEntries_EveryEntrySkipped_ReturnsAnEmptyBatch()
	{
		TextEditSkipResult result = TextEditKernel.PrepareSkippingInvalidEntries(new StringTextSnapshot("abc"), [default, CreateEdit(50, 1, "X")]);

		Assert.IsEmpty(result.Edits.Operations);
		Assert.IsTrue(result.HasSkippedEntries);
		Assert.HasCount(2, result.SkippedIssues);
	}

	[TestMethod]
	public void PrepareSkippingInvalidEntries_EmptyBatch_IsEmptyAndSkippedNothing()
	{
		TextEditSkipResult result = TextEditKernel.PrepareSkippingInvalidEntries(new StringTextSnapshot("abc"), []);

		Assert.IsEmpty(result.Edits.Operations);
		Assert.IsFalse(result.HasSkippedEntries);
	}

	[TestMethod]
	public void PrepareSkippingInvalidEntries_ConflictIssuesAgreeWithTheStrictKernel()
	{
		// The strict kernel rejects the batch and names the same conflicting entry; the lenient kernel
		// reports the same rule for the same pair and keeps the earlier entry.
		TextEditPreparationResult strict = TextEditKernel.Prepare(new StringTextSnapshot("abcdef"), [CreateEdit(1, 3, "x"), CreateEdit(2, 2, "y")]);

		TextEditSkipResult lenient = TextEditKernel.PrepareSkippingInvalidEntries(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(1, 3, "x"), CreateEdit(2, 2, "y")]
		);

		Assert.IsFalse(strict.Succeeded);
		Assert.HasCount(1, strict.Issues);
		Assert.HasCount(1, lenient.SkippedIssues);
		Assert.AreEqual(strict.Issues[0].Kind, lenient.SkippedIssues[0].Kind);
		Assert.AreEqual(strict.Issues[0].EditIndex, lenient.SkippedIssues[0].EditIndex);
		Assert.AreEqual(strict.Issues[0].RelatedEditIndex, lenient.SkippedIssues[0].RelatedEditIndex);
		Assert.AreEqual(strict.Issues[0].Message, lenient.SkippedIssues[0].Message);
		Assert.HasCount(1, lenient.Edits.Operations);
	}
}
