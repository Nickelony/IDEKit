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
	public void ClampNegativeToZero_DefaultInstance_ReturnsZeroPosition()
	{
		Assert.AreEqual(default(TextPosition), default(TextPosition).ClampNegativeToZero());
	}
}
