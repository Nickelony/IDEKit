namespace Nickelony.Matching.Tests;

[TestClass]
public sealed class MatchRangeTests
{
	[TestMethod]
	public void Constructor_OffsetAndLength_ComputesTheEndOffset()
	{
		var range = new MatchRange(3, 4);

		Assert.AreEqual(3, range.Offset);
		Assert.AreEqual(4, range.Length);
		Assert.AreEqual(7, range.EndOffset);
	}

	[TestMethod]
	public void Constructor_EmptyRangeAtTheLargestOffset_IsValid()
	{
		var range = new MatchRange(int.MaxValue, 0);
		Assert.AreEqual(int.MaxValue, range.EndOffset);
	}

	[TestMethod]
	public void Constructor_NegativeOffsetOrLength_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MatchRange(-1, 1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MatchRange(0, -1));
	}

	[TestMethod]
	public void Constructor_EndPastTheLargestOffset_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MatchRange(int.MaxValue, 1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MatchRange(1, int.MaxValue));
	}

	[TestMethod]
	public void Equals_SameOffsetAndLength_AreEqual() => Assert.AreEqual(new MatchRange(2, 5), new MatchRange(2, 5));
}
