namespace Nickelony.Matching.Tests;

/// <summary>Pins the three forms of the match of the command palette: a prefix, the starts of words, and a contiguous substring.</summary>
[TestClass]
public sealed class WordMatcherTests
{
	[TestMethod]
	public void TryMatch_NullQueryOrText_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => WordMatcher.TryMatch(null!, "text", out _));
		Assert.ThrowsExactly<ArgumentNullException>(() => WordMatcher.TryMatch("query", null!, out _));
	}

	[TestMethod]
	public void TryMatch_PrefixOfTheText_MatchesTheWholePrefixIgnoringCase()
	{
		Assert.IsTrue(WordMatcher.TryMatch("FORM", "Format Document", out IReadOnlyList<MatchRange> ranges));
		Assert.AreSequenceEqual([new MatchRange(0, 4)], [.. ranges]);
	}

	[TestMethod]
	public void TryMatch_StartsOfWords_MatchesTheFirstCharacterOfEachWord()
	{
		Assert.IsTrue(WordMatcher.TryMatch("tlc", "Toggle Line Comment", out IReadOnlyList<MatchRange> ranges));
		Assert.AreSequenceEqual([new MatchRange(0, 1), new MatchRange(7, 1), new MatchRange(12, 1)], [.. ranges]);
	}

	[TestMethod]
	public void TryMatch_StartsOfWordsWithRunsInsideAWord_MergesTheRuns()
	{
		Assert.IsTrue(WordMatcher.TryMatch("gtdef", "Go To Definition", out IReadOnlyList<MatchRange> ranges));
		Assert.AreSequenceEqual([new MatchRange(0, 1), new MatchRange(3, 1), new MatchRange(6, 3)], [.. ranges]);
	}

	[TestMethod]
	public void TryMatch_AWordStartThatNeedsBacktracking_StillMatches()
	{
		// The first word offers "a" twice, and only the second branch lets the next word supply the "b".
		Assert.IsTrue(WordMatcher.TryMatch("aab", "aac ab", out IReadOnlyList<MatchRange> ranges));
		Assert.IsNotEmpty(ranges);
	}

	[TestMethod]
	public void TryMatch_ContiguousSubstring_MatchesWhereverItIs()
	{
		Assert.IsTrue(WordMatcher.TryMatch("rmat", "Format Document", out IReadOnlyList<MatchRange> ranges));
		Assert.AreSequenceEqual([new MatchRange(2, 4)], [.. ranges]);
	}

	[TestMethod]
	public void TryMatch_CharactersInsideWordsThatAreNotContiguous_DoNotMatch()
	{
		Assert.IsFalse(WordMatcher.TryMatch("fmt", "Format Document", out IReadOnlyList<MatchRange> ranges));
		Assert.IsEmpty(ranges);
	}

	[TestMethod]
	public void TryMatch_AQueryLongerThanTheText_DoesNotMatch() => Assert.IsFalse(WordMatcher.TryMatch("format document", "Format", out _));

	[TestMethod]
	public void TryMatch_EmptyQueryOrText_DoesNotMatch()
	{
		Assert.IsFalse(WordMatcher.TryMatch(string.Empty, "Format", out _));
		Assert.IsFalse(WordMatcher.TryMatch("f", string.Empty, out _));
	}

	[TestMethod]
	public void TryMatch_ATextThatExhaustsTheBacktrackingBudget_DoesNotMatchAndReturns()
	{
		string text = string.Concat(Enumerable.Repeat("a ", 200));
		string query = new string('a', 100) + "b";

		Assert.IsFalse(WordMatcher.TryMatch(query, text, out IReadOnlyList<MatchRange> ranges));
		Assert.IsEmpty(ranges);
	}
}
