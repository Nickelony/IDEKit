namespace Nickelony.IDEKit.Core.Tests;

/// <summary>Pins the clamp-and-collapse rule <see cref="TextRange.Normalize"/> applies to a range.</summary>
[TestClass]
public sealed class TextRangeNormalizeTests
{
	[TestMethod]
	public void ZeroLengthRange_PromotesToSingleCharacterRange()
	{
		TextRange? range = TextRange.Normalize(TextRange.FromBounds(2, 2), 6);
		Assert.AreEqual(new TextRange(2, 1), range);
	}

	[TestMethod]
	public void StartBeyondText_ClampsToLastCharacter()
	{
		TextRange? range = TextRange.Normalize(TextRange.FromBounds(20, 30), 6);
		Assert.AreEqual(new TextRange(5, 1), range);
	}

	[TestMethod]
	public void EmptyText_ReturnsNull()
	{
		// The renderer normalizes through this method before drawing; an empty text has no character
		// range to normalize into.
		Assert.IsNull(TextRange.Normalize(TextRange.FromBounds(0, 5), 0));
	}

	[TestMethod]
	public void ZeroLengthAtTextEnd_PromotesToLastCharacter()
	{
		TextRange? range = TextRange.Normalize(TextRange.FromBounds(6, 6), 6);
		Assert.AreEqual(new TextRange(5, 1), range);
	}

	[TestMethod]
	public void RangeInsideTheText_IsReturnedUnchanged()
	{
		TextRange? range = TextRange.Normalize(TextRange.FromBounds(1, 4), 6);
		Assert.AreEqual(new TextRange(1, 3), range);
	}

	[TestMethod]
	public void RangeEndingBeyondTheText_IsClampedToTheTextEnd()
	{
		TextRange? range = TextRange.Normalize(TextRange.FromBounds(4, 30), 6);
		Assert.AreEqual(new TextRange(4, 2), range);
	}

	[TestMethod]
	public void NegativeTextLength_Throws() => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TextRange.Normalize(default, -1));
}
