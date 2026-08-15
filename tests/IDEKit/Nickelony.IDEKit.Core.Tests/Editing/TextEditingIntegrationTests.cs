namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the edit-pipeline behaviors added for batch offset mapping and the surrogate-safe
/// incremental calculation.
/// </summary>
[TestClass]
public sealed class TextEditingIntegrationTests
{
	[TestMethod]
	public void MapOffsets_MatchesMapOffsetPerElement()
	{
		var batch = new PreparedTextEdits(
			[new TextEditOperation(5, 7, "XY", 1), new TextEditOperation(2, 2, "i", 0)]);

		int[] offsets = [9, 0, 2, 3, 6, 7, 12];
		int[] mapped = batch.MapOffsets(offsets);

		Assert.AreEqual(offsets.Length, mapped.Length);

		for (int index = 0; index < offsets.Length; index++)
			Assert.AreEqual(batch.MapOffset(offsets[index]), mapped[index], $"Offset {offsets[index]} must map alike.");
	}

	[TestMethod]
	public void MapOffsets_NegativeOffset_Throws()
	{
		var batch = new PreparedTextEdits([new TextEditOperation(2, 2, "i", 0)]);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => batch.MapOffsets([1, -1]));
	}

	[TestMethod]
	public void MapOffsets_EmptyInput_ReturnsEmptyArray()
	{
		var batch = new PreparedTextEdits([new TextEditOperation(2, 2, "i", 0)]);

		Assert.AreEqual(0, batch.MapOffsets([]).Length);
	}

	[TestMethod]
	public void Compute_SurrogatePair_StaysOnUnitBoundariesAndReconstructsTheNewText()
	{
		const string oldText = "\U0001F600A";
		const string newText = "\U0001F600B";

		TextIncrementalEdit change = TextIncrementalEditCalculator.Compute(oldText, newText);

		string applied = string.Concat(
			oldText[..change.Range.Offset],
			change.NewText,
			oldText[change.Range.EndOffset..]);

		Assert.AreEqual(newText, applied);

		// Neither boundary may fall between the halves of a surrogate pair.
		Assert.IsFalse(
			change.Range.Offset > 0 && change.Range.Offset < oldText.Length
			&& char.IsHighSurrogate(oldText[change.Range.Offset - 1])
			&& char.IsLowSurrogate(oldText[change.Range.Offset]));
		Assert.IsFalse(
			change.Range.EndOffset > 0 && change.Range.EndOffset < oldText.Length
			&& char.IsHighSurrogate(oldText[change.Range.EndOffset - 1])
			&& char.IsLowSurrogate(oldText[change.Range.EndOffset]));
	}
}
