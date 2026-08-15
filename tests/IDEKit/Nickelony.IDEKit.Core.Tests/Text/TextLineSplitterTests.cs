namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextLineSplitterTests
{
	[TestMethod]
	public void Split_RecognizesLfCrLfAndLoneCr()
	{
		CollectionAssert.AreEqual(
			new[] { "one", "two", "three", "four" },
			TextLineSplitter.Split("one\r\ntwo\nthree\rfour"));
	}

	[TestMethod]
	public void Split_TrailingLineEnding_YieldsFinalEmptyLine()
	{
		CollectionAssert.AreEqual(new[] { "one", string.Empty }, TextLineSplitter.Split("one\n"));
	}

	[TestMethod]
	public void Split_EmptyOrNullText_YieldsSingleEmptyLine()
	{
		CollectionAssert.AreEqual(new[] { string.Empty }, TextLineSplitter.Split(string.Empty));
		CollectionAssert.AreEqual(new[] { string.Empty }, TextLineSplitter.Split(null));
	}

	[TestMethod]
	public void Split_TerminatorOnlyTexts_YieldTwoEmptyLines()
	{
		CollectionAssert.AreEqual(new[] { string.Empty, string.Empty }, TextLineSplitter.Split("\n"));
		CollectionAssert.AreEqual(new[] { string.Empty, string.Empty }, TextLineSplitter.Split("\r\n"));
		CollectionAssert.AreEqual(new[] { string.Empty, string.Empty }, TextLineSplitter.Split("\r"));
	}

	[TestMethod]
	public void Split_SingleCharacterText_YieldsThatLine()
	{
		CollectionAssert.AreEqual(new[] { "a" }, TextLineSplitter.Split("a"));
	}

	[TestMethod]
	public void Split_MixedCrLfAndLoneCr_YieldsEveryLine()
	{
		// A CRLF pair followed by a lone CR terminates the empty line between them.
		CollectionAssert.AreEqual(new[] { "a", string.Empty, "b" }, TextLineSplitter.Split("a\r\n\rb"));
	}
}
