namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextPositionRangeTests
{
	[TestMethod]
	public void Equality_SamePositions_AreEqual()
	{
		var range = new TextPositionRange(new TextPosition(1, 2), new TextPosition(3, 4));
		var same = new TextPositionRange(new TextPosition(1, 2), new TextPosition(3, 4));

		Assert.AreEqual(range, same);
		Assert.AreEqual(range.GetHashCode(), same.GetHashCode());
	}

	[TestMethod]
	public void StartAndEnd_ExposeTheSuppliedPositions()
	{
		var range = new TextPositionRange(new TextPosition(2, 5), new TextPosition(2, 9));

		Assert.AreEqual(new TextPosition(2, 5), range.Start);
		Assert.AreEqual(new TextPosition(2, 9), range.End);
	}

	[TestMethod]
	public void Default_ReportsOriginPositions()
	{
		var range = default(TextPositionRange);

		Assert.AreEqual(new TextPosition(0, 0), range.Start);
		Assert.AreEqual(new TextPosition(0, 0), range.End);
		Assert.IsTrue(range.IsEmpty);
	}

	[TestMethod]
	public void ReversedRange_IsStoredAsSupplied()
	{
		// No ordering is enforced, so consumers that need validation must check it themselves.
		var range = new TextPositionRange(new TextPosition(3, 0), new TextPosition(1, 0));

		Assert.AreEqual(new TextPosition(3, 0), range.Start);
		Assert.AreEqual(new TextPosition(1, 0), range.End);
	}

	[TestMethod]
	public void IsEmpty_EqualEndpoints_IsTrue()
	{
		Assert.IsTrue(new TextPositionRange(new TextPosition(1, 2), new TextPosition(1, 2)).IsEmpty);
		Assert.IsFalse(new TextPositionRange(new TextPosition(1, 2), new TextPosition(1, 3)).IsEmpty);
	}

	[TestMethod]
	public void IsEmpty_ReversedRange_IsFalse()
	{
		// A reversed range contains no positions but still spans two endpoints, so it is not empty.
		Assert.IsFalse(new TextPositionRange(new TextPosition(3, 0), new TextPosition(1, 0)).IsEmpty);
	}

	[TestMethod]
	public void Contains_IsHalfOpenAndLexicographic()
	{
		var range = new TextPositionRange(new TextPosition(1, 2), new TextPosition(3, 4));

		Assert.IsTrue(range.Contains(new TextPosition(1, 2)));    // the start is inclusive
		Assert.IsTrue(range.Contains(new TextPosition(2, 0)));    // inside, on a middle line
		Assert.IsFalse(range.Contains(new TextPosition(3, 4)));   // the end is exclusive
		Assert.IsFalse(range.Contains(new TextPosition(1, 1)));   // before the start on the start line
		Assert.IsFalse(range.Contains(new TextPosition(3, 5)));   // after the end on the end line
		Assert.IsFalse(range.Contains(new TextPosition(0, 0)));   // before the start line
	}

	[TestMethod]
	public void Contains_EmptyOrReversedRange_ContainsNoPosition()
	{
		var empty = new TextPositionRange(new TextPosition(2, 2), new TextPosition(2, 2));
		var reversed = new TextPositionRange(new TextPosition(3, 0), new TextPosition(1, 0));

		Assert.IsFalse(empty.Contains(new TextPosition(2, 2)));
		Assert.IsFalse(reversed.Contains(new TextPosition(2, 0)));
	}
}
