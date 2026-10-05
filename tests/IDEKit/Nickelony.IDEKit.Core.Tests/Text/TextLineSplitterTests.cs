namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextLineSplitterTests
{
	[TestMethod]
	public void SplitIncludingTerminators_KeepsEachTerminatorOnItsLine_SoTheLinesConcatenateToTheText()
	{
		const string Text = "one\r\ntwo\nthree\rfour";

		string[] lines = TextLineSplitter.SplitIncludingTerminators(Text);

		Assert.AreSequenceEqual(["one\r\n", "two\n", "three\r", "four"], lines);
		Assert.AreEqual(Text, string.Concat(lines));
		Assert.AreSequenceEqual(["one\n", string.Empty], TextLineSplitter.SplitIncludingTerminators("one\n"));
		Assert.AreSequenceEqual([string.Empty], TextLineSplitter.SplitIncludingTerminators(null));
	}

	[TestMethod]
	public void Split_RecognizesLfCrLfAndLoneCr() =>
		Assert.AreSequenceEqual(["one", "two", "three", "four"], TextLineSplitter.Split("one\r\ntwo\nthree\rfour"));

	[TestMethod]
	public void Split_TrailingLineEnding_YieldsFinalEmptyLine() => Assert.AreSequenceEqual(["one", string.Empty], TextLineSplitter.Split("one\n"));

	[TestMethod]
	public void Split_EmptyOrNullText_YieldsSingleEmptyLine()
	{
		Assert.AreSequenceEqual([string.Empty], TextLineSplitter.Split(string.Empty));
		Assert.AreSequenceEqual([string.Empty], TextLineSplitter.Split(null));
	}

	[TestMethod]
	public void Split_TerminatorOnlyTexts_YieldTwoEmptyLines()
	{
		Assert.AreSequenceEqual([string.Empty, string.Empty], TextLineSplitter.Split("\n"));
		Assert.AreSequenceEqual([string.Empty, string.Empty], TextLineSplitter.Split("\r\n"));
		Assert.AreSequenceEqual([string.Empty, string.Empty], TextLineSplitter.Split("\r"));
	}

	[TestMethod]
	public void Split_SingleCharacterText_YieldsThatLine() => Assert.AreSequenceEqual(["a"], TextLineSplitter.Split("a"));

	[TestMethod]
	public void Split_MixedCrLfAndLoneCr_YieldsEveryLine()
	{
		// A CRLF pair followed by a lone CR terminates the empty line between them.
		Assert.AreSequenceEqual(["a", string.Empty, "b"], TextLineSplitter.Split("a\r\n\rb"));
	}
}
