namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextPositionTests
{
	[TestMethod]
	public void ClampNegativeToZero_NegativeCoordinates_ClampsToZero()
	{
		var position = new TextPosition(-3, -7);
		Assert.AreEqual(new TextPosition(0, 0), position.ClampNegativeToZero());
	}

	[TestMethod]
	public void ClampNegativeToZero_OneNegativeCoordinate_ClampsOnlyThatCoordinate()
	{
		Assert.AreEqual(new TextPosition(0, 4), new TextPosition(-1, 4).ClampNegativeToZero());
		Assert.AreEqual(new TextPosition(4, 0), new TextPosition(4, -1).ClampNegativeToZero());
	}

	[TestMethod]
	public void ClampNegativeToZero_NonNegativeCoordinates_ReturnsTheSameValues()
	{
		var position = new TextPosition(2, 9);
		Assert.AreEqual(position, position.ClampNegativeToZero());
	}

	[TestMethod]
	public void ClampNegativeToZero_DefaultInstance_ReturnsZeroPosition() => Assert.AreEqual(default, default(TextPosition).ClampNegativeToZero());

	[TestMethod]
	[DataRow(1, 5, 2, 0, -1)] // an earlier line wins whatever the characters are
	[DataRow(2, 0, 1, 5, 1)]
	[DataRow(3, 2, 3, 4, -1)] // the same line compares by character
	[DataRow(3, 4, 3, 2, 1)]
	[DataRow(3, 4, 3, 4, 0)]
	[DataRow(-1, 9, 0, 0, -1)] // a negative coordinate still orders, because no bounds check applies
	[DataRow(0, -1, 0, 0, -1)]
	public void CompareTo_OrdersByLineAndThenByCharacter(int line, int character, int otherLine, int otherCharacter, int expectedSign)
	{
		int comparison = new TextPosition(line, character).CompareTo(new TextPosition(otherLine, otherCharacter));
		Assert.AreEqual(expectedSign, Math.Sign(comparison));
	}

	[TestMethod]
	public void ComparisonOperators_AgreeWithTheOrder()
	{
		var before = new TextPosition(1, 9);
		var after = new TextPosition(2, 0);
		var sameAsAfter = new TextPosition(2, 0);

		Assert.IsTrue(before < after);
		Assert.IsTrue(before <= after);
		Assert.IsTrue(after > before);
		Assert.IsTrue(after >= before);

		Assert.IsFalse(after < before);
		Assert.IsFalse(after <= before);
		Assert.IsFalse(before > after);
		Assert.IsFalse(before >= after);

		// Equal positions are neither before nor after each other, but each is at or before and at or after the other.
		Assert.IsFalse(after < sameAsAfter);
		Assert.IsFalse(after > sameAsAfter);
		Assert.IsTrue(after <= sameAsAfter);
		Assert.IsTrue(after >= sameAsAfter);
	}

	[TestMethod]
	public void Sorting_PutsPositionsInDocumentOrder()
	{
		var positions = new List<TextPosition> { new(2, 1), new(0, 7), new(2, 0), new(1, 3) };

		positions.Sort();

		Assert.AreSequenceEqual([new TextPosition(0, 7), new TextPosition(1, 3), new TextPosition(2, 0), new TextPosition(2, 1)], positions);
	}
}
