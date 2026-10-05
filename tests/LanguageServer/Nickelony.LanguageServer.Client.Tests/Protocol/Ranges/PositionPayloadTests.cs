namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class PositionPayloadTests
{
	[TestMethod]
	[DataRow(1, 5, 2, 0, -1)] // an earlier line wins whatever the characters are
	[DataRow(2, 0, 1, 5, 1)]
	[DataRow(3, 2, 3, 4, -1)] // the same line compares by character
	[DataRow(3, 4, 3, 2, 1)]
	[DataRow(3, 4, 3, 4, 0)]
	[DataRow(-1, 9, 0, 0, -1)] // a negative coordinate still has a place in the order
	public void CompareTo_OrdersByLineAndThenByCharacter(int line, int character, int otherLine, int otherCharacter, int expectedSign)
	{
		int comparison = new PositionPayload(line, character).CompareTo(new PositionPayload(otherLine, otherCharacter));
		Assert.AreEqual(expectedSign, Math.Sign(comparison));
	}

	[TestMethod]
	public void ComparisonOperators_AgreeWithTheOrder()
	{
		var before = new PositionPayload(1, 9);
		var after = new PositionPayload(2, 0);
		var sameAsAfter = new PositionPayload(2, 0);

		Assert.IsTrue(before < after);
		Assert.IsTrue(before <= after);
		Assert.IsTrue(after > before);
		Assert.IsTrue(after >= before);

		Assert.IsFalse(after < sameAsAfter);
		Assert.IsFalse(after > sameAsAfter);
		Assert.IsTrue(after <= sameAsAfter);
		Assert.IsTrue(after >= sameAsAfter);
	}

	[TestMethod]
	public void RangeIsReversed_OnlyWhenTheEndIsBeforeTheStart()
	{
		Assert.IsTrue(new RangePayload(new PositionPayload(3, 0), new PositionPayload(1, 0)).IsReversed);
		Assert.IsTrue(new RangePayload(new PositionPayload(2, 5), new PositionPayload(2, 4)).IsReversed);

		Assert.IsFalse(new RangePayload(new PositionPayload(1, 9), new PositionPayload(2, 0)).IsReversed);
		Assert.IsFalse(new RangePayload(new PositionPayload(2, 4), new PositionPayload(2, 4)).IsReversed);
		Assert.IsFalse(default(RangePayload).IsReversed);
	}
}
