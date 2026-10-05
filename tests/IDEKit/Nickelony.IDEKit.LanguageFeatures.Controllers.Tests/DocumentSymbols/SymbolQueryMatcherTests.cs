using Nickelony.IDEKit.LanguageFeatures.Controllers.DocumentSymbols;
using Nickelony.Matching;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
public sealed class SymbolQueryMatcherTests
{
	private static bool Matches(string query, string name, string? container) =>
		new SymbolQueryMatcher(query).TryMatch(name, container, score: out _, nameRanges: out _, containerRanges: out _);

	[TestMethod]
	public void MatchesEverything_ForAQueryWithoutAWordToMatch()
	{
		Assert.IsTrue(new SymbolQueryMatcher("").MatchesEverything);
		Assert.IsTrue(new SymbolQueryMatcher("  ").MatchesEverything);
		Assert.IsTrue(new SymbolQueryMatcher("*").MatchesEverything);
		Assert.IsFalse(new SymbolQueryMatcher("a").MatchesEverything);
		Assert.IsTrue(Matches("", "Anything", null));
	}

	[TestMethod]
	public void TryMatch_TheFirstWordMatchesTheNameAndNotTheContainer()
	{
		Assert.IsTrue(Matches("one", "One", "Alpha"));
		Assert.IsFalse(Matches("alp", "One", "Alpha"));
	}

	[TestMethod]
	public void TryMatch_TheWordsAfterTheFirstMatchTheContainer()
	{
		Assert.IsTrue(Matches("one alp", "One", "Alpha"));
		Assert.IsFalse(Matches("one zzz", "One", "Alpha"));
		Assert.IsFalse(Matches("one alp", "One", null));
	}

	[TestMethod]
	public void TryMatch_ANameThatHoldsTheWholeQueryNeedsNoContainer()
	{
		Assert.IsTrue(Matches("alpha beta", "Alpha Beta", null));
		Assert.IsFalse(Matches("alpha beta", "Alpha", null));
	}

	[TestMethod]
	public void TryMatch_ReportsTheScoreAndTheMatchedCharacters()
	{
		var matcher = new SymbolQueryMatcher("on alp");

		Assert.IsTrue(
			matcher.TryMatch(
				name: "One",
				container: "Alpha",
				out int score,
				out IReadOnlyList<MatchRange> nameRanges,
				out IReadOnlyList<MatchRange> containerRanges
			)
		);

		Assert.IsGreaterThan(0, score);
		Assert.AreEqual(0, nameRanges[0].Offset);
		Assert.AreEqual(0, containerRanges[0].Offset);
	}

	[TestMethod]
	public void TryMatch_ANameMatchScoresAboveTheSameNameWithoutAPrefix()
	{
		var matcher = new SymbolQueryMatcher("a");

		matcher.TryMatch(name: "ab", container: null, score: out int prefix, nameRanges: out _, containerRanges: out _);
		matcher.TryMatch(name: "xxa", container: null, score: out int inner, nameRanges: out _, containerRanges: out _);

		Assert.IsGreaterThan(inner, prefix);
	}

	[TestMethod]
	public void MatchesContainerWords_AQueryOfOneWordNeedsNothingBeyondTheServer()
	{
		var matcher = new SymbolQueryMatcher("util");
		Assert.IsTrue(matcher.MatchesContainerWords("Util", "anything"));
	}

	[TestMethod]
	public void MatchesContainerWords_TheWordsAfterTheFirstMustMatchTheDescription()
	{
		var matcher = new SymbolQueryMatcher("util core");

		Assert.IsTrue(matcher.MatchesContainerWords("Util", "Core • src/util.lua"));
		Assert.IsFalse(matcher.MatchesContainerWords("Util", "Other • lib/foo.lua"));
	}

	[TestMethod]
	public void MatchesContainerWords_ANameThatHoldsTheWholeQueryNeedsNoDescription()
	{
		var matcher = new SymbolQueryMatcher("util ity");
		Assert.IsTrue(matcher.MatchesContainerWords("Utility", "unrelated"));
	}
}
