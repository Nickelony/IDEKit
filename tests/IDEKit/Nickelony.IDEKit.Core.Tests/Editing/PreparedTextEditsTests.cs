using static Nickelony.IDEKit.Core.Tests.TextEditInputs;

namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class PreparedTextEditsTests
{
	[TestMethod]
	public void Constructor_UnorderedOperations_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() => new PreparedTextEdits(
			[new TextEditOperation(0, 1, "x", 0), new TextEditOperation(2, 3, "y", 1)]));
	}

	[TestMethod]
	public void Constructor_NullOperation_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() => new PreparedTextEdits([null!]));
	}

	[TestMethod]
	public void Constructor_OverlappingOperations_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() => new PreparedTextEdits(
			[new TextEditOperation(7, 9, "y", 1), new TextEditOperation(5, 10, "x", 0)]));
	}

	[TestMethod]
	public void Constructor_InsertionBeforeReplacementAtSameStartOffset_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() => new PreparedTextEdits(
			[new TextEditOperation(3, 3, "X", 0), new TextEditOperation(3, 5, "Y", 1)]));
	}

	[TestMethod]
	public void Constructor_DuplicateInsertions_AreAcceptedInListOrder()
	{
		// Same-offset insertions are valid; the list order defines the order their texts appear in
		// the resulting text (the first list entry applies last and lands leftmost).
		var edits = new PreparedTextEdits(
			[new TextEditOperation(3, 3, "Y", 1), new TextEditOperation(3, 3, "X", 0)]);

		Assert.AreEqual(2, edits.Operations.Count);
		Assert.AreEqual("Y", edits.Operations[0].NewText);
		Assert.AreEqual("X", edits.Operations[1].NewText);
	}

	[TestMethod]
	public void Constructor_NoOpOperation_IsAcceptedAndSkippedByValidation()
	{
		// A no-op operation cannot conflict with another operation or with the required order, so it
		// is accepted and preserved in the batch.
		var noOp = new TextEditOperation(2, 2, string.Empty, 0);

		var edits = new PreparedTextEdits([noOp]);

		Assert.AreEqual(1, edits.Operations.Count);
		Assert.AreEqual(noOp, edits.Operations[0]);
	}

	[TestMethod]
	public void Constructor_ReplacementBeforeInsertionAtSameStartOffset_IsAccepted()
	{
		// List order applies the replacement before the insertion, matching the kernel's ordering rule;
		// the insertion point maps before the inserted text.
		var batch = new PreparedTextEdits(
			[new TextEditOperation(3, 5, "Y", 1), new TextEditOperation(3, 3, "X", 0)]);

		Assert.AreEqual(2, batch.Operations.Count);
		Assert.AreEqual(3, batch.MapOffset(3));
		Assert.AreEqual(5, batch.MapOffset(4));
	}

	[TestMethod]
	public void MapOffset_AccountsForReplacementAndInsertionDeltas()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(1, 2, "X"), CreateEdit(5, 0, "YZ")]);

		Assert.IsTrue(result.IsValid);
		Assert.AreEqual(0, result.Edits.MapOffset(0));
		Assert.AreEqual(1, result.Edits.MapOffset(1));
		Assert.AreEqual(2, result.Edits.MapOffset(3));
		Assert.AreEqual(7, result.Edits.MapOffset(6));
	}

	[TestMethod]
	public void MapOffset_EndOfReplacedRange_MapsAfterLongerReplacement()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(1, 2, "XYZ")]);

		Assert.IsTrue(result.IsValid);

		// The end of the replaced range maps after the replacement, not inside it.
		Assert.AreEqual(4, result.Edits.MapOffset(3));
	}

	[TestMethod]
	public void MapOffset_InsideReplacedRange_MapsRelativePosition()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(1, 2, "XYZ")]);

		Assert.AreEqual(2, result.Edits.MapOffset(2));
	}

	[TestMethod]
	public void MapOffset_InsideShorterReplacement_ClampsToReplacementEnd()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(1, 4, "x")]);

		Assert.IsTrue(result.IsValid);

		// The source offset is inside the replaced range, so it maps to the same relative position
		// clamped to the shorter replacement text.
		Assert.AreEqual(2, result.Edits.MapOffset(3));
	}

	[TestMethod]
	public void MapOffset_InsideDeletedRange_MapsToDeletionPoint()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(1, 4, string.Empty)]);

		Assert.IsTrue(result.IsValid);
		Assert.AreEqual(1, result.Edits.MapOffset(3));

		// The end of the deleted range maps after the deletion.
		Assert.AreEqual(1, result.Edits.MapOffset(5));
	}

	[TestMethod]
	public void MapOffset_AtInsertionPoint_MapsBeforeInsertedText()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(3, 0, "ZZ")]);

		Assert.IsTrue(result.IsValid);

		// The start of the zero-length range wins over its end, so the insertion point itself maps
		// before the inserted text while later offsets shift past it.
		Assert.AreEqual(3, result.Edits.MapOffset(3));
		Assert.AreEqual(2, result.Edits.MapOffset(2));
		Assert.AreEqual(6, result.Edits.MapOffset(4));
	}

	[TestMethod]
	public void MapOffset_MixedInsertionsAtBothReplacementBoundaries_ShiftsLaterOffsets()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abcdef"),
			[CreateEdit(1, 2, "X"), CreateEdit(1, 0, "Y"), CreateEdit(3, 0, "Z")]);

		Assert.IsTrue(result.IsValid);

		// The batch produces "aYXZdef": the insertion at the replacement start lands before "X" and
		// the insertion at its end before "Z". Offsets at both insertion points map before the
		// inserted text, offsets inside the replaced range map into "X" clamped to its length, and
		// only offsets past both operations shift by the combined delta.
		Assert.AreEqual(0, result.Edits.MapOffset(0));
		Assert.AreEqual(1, result.Edits.MapOffset(1));
		Assert.AreEqual(3, result.Edits.MapOffset(2));
		Assert.AreEqual(3, result.Edits.MapOffset(3));
		Assert.AreEqual(5, result.Edits.MapOffset(4));
		Assert.AreEqual(6, result.Edits.MapOffset(5));
		Assert.AreEqual(7, result.Edits.MapOffset(6));
	}

	[TestMethod]
	public void MapOffset_InsertionAtDocumentEnd_MapsBeforeInsertedText()
	{
		TextEditPreparationResult result = TextEditKernel.Prepare(
			new StringTextSnapshot("abc"),
			[CreateEdit(3, 0, "XY")]);

		Assert.IsTrue(result.IsValid);

		// The start of the zero-length range wins at the end of the text as well, so the document-end
		// offset maps before the inserted text even though the edited document is longer.
		Assert.AreEqual(3, result.Edits.MapOffset(3));
		Assert.AreEqual(2, result.Edits.MapOffset(2));
	}

	[TestMethod]
	public void MapOffset_WithoutOperations_ReturnsOffset()
	{
		Assert.AreEqual(5, new PreparedTextEdits([]).MapOffset(5));
	}

	[TestMethod]
	public void MapOffset_NegativeOffset_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PreparedTextEdits([]).MapOffset(-1));
	}
}
