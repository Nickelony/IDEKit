namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the small behaviors of <see cref="TextRange"/> that the core suite does not exercise.
/// </summary>
[TestClass]
public sealed class TextRangeAdditionalTests
{
	[TestMethod]
	public void IsEmpty_ReflectsZeroLength()
	{
		Assert.IsTrue(new TextRange(4, 0).IsEmpty);
		Assert.IsFalse(new TextRange(4, 1).IsEmpty);
	}

	[TestMethod]
	public void ToString_UsesHalfOpenIntervalNotation()
	{
		Assert.AreEqual("[4..10)", new TextRange(4, 6).ToString());
	}
}
