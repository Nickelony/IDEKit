namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class LineTerminatorNormalizerTests
{
	[TestMethod]
	[DataRow("a\r\nb\rc", "a\nb\nc", DisplayName = "CrLfAndLoneCr")]
	[DataRow("a\r\n\r\nb", "a\n\nb", DisplayName = "ConsecutiveCrLf")]
	[DataRow("\r", "\n", DisplayName = "LoneCrOnly")]
	public void NormalizeToLineFeeds_MixedTerminators_ReturnsLineFeeds(string text, string expected)
	{
		Assert.AreEqual(expected, LineTerminatorNormalizer.NormalizeToLineFeeds(text));
	}

	[TestMethod]
	public void NormalizeToLineFeeds_LineFeedsOnly_ReturnsTheSameInstance()
	{
		const string text = "a\nb\nc";

		Assert.AreSame(text, LineTerminatorNormalizer.NormalizeToLineFeeds(text));
	}

	[TestMethod]
	public void NormalizeToLineFeeds_EmptyString_ReturnsTheSameInstance()
	{
		Assert.AreSame(string.Empty, LineTerminatorNormalizer.NormalizeToLineFeeds(string.Empty));
	}

	[TestMethod]
	public void NormalizeToLineFeeds_NullText_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => LineTerminatorNormalizer.NormalizeToLineFeeds(null!));
	}
}
