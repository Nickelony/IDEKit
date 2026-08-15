namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the clamp-and-collapse rule <see cref="TextRange.Normalize"/> applies to a raw offset range.
/// </summary>
[TestClass]
public sealed class TextRangeNormalizeTests
{
	[TestMethod]
	public void ZeroLengthRange_PromotesToSingleCharacterRange()
	{
		TextRange? range = TextRange.Normalize(6, 2, 2);

		Assert.AreEqual(new TextRange(2, 1), range);
	}

	[TestMethod]
	public void ReversedRange_AnchorsAtClampedStart()
	{
		TextRange? range = TextRange.Normalize(6, 4, 1);

		Assert.AreEqual(new TextRange(4, 1), range);
	}

	[TestMethod]
	public void StartBeyondText_ClampsToLastCharacter()
	{
		TextRange? range = TextRange.Normalize(6, 20, 30);

		Assert.AreEqual(new TextRange(5, 1), range);
	}

	[TestMethod]
	public void EmptyText_ReturnsNull()
	{
		// The renderer normalizes through this method before drawing; an empty text has no character
		// range to normalize into.
		Assert.IsNull(TextRange.Normalize(0, 0, 5));
	}

	[TestMethod]
	public void ZeroLengthAtTextEnd_PromotesToLastCharacter()
	{
		TextRange? range = TextRange.Normalize(6, 6, 6);

		Assert.AreEqual(new TextRange(5, 1), range);
	}

	[TestMethod]
	public void ReversedRangeEndingAtTextEnd_AnchorsAtClampedStart()
	{
		TextRange? range = TextRange.Normalize(6, 6, 4);

		Assert.AreEqual(new TextRange(5, 1), range);
	}

	[TestMethod]
	public void NegativeTextLength_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => TextRange.Normalize(-1, 0, 0));
	}
}
