namespace Nickelony.Matching.Tests;

/// <summary>
/// Pins the ranking cases of the fuzzy matcher that a ranked list relies on: a subsequence match, a word start over an inner match, a contiguous run
/// over a scattered one, the file name over its folders in path mode, and the ranges to highlight.
/// </summary>
[TestClass]
public sealed class FuzzyMatcherTests
{
	[TestMethod]
	public void Score_AbbreviationOfSeveralWords_FindsTheCandidate()
	{
		var matcher = new FuzzyMatcher("fmtdoc");

		Assert.IsGreaterThan(0, matcher.Score("Format Document"));
		Assert.AreEqual(0, matcher.Score("Format Selection"));
	}

	[TestMethod]
	public void Score_QueryInADifferentCase_MatchesOrdinally()
	{
		Assert.IsGreaterThan(0, new FuzzyMatcher("FMTDOC").Score("format document"));
		Assert.IsGreaterThan(0, new FuzzyMatcher("fmtdoc").Score("FORMAT DOCUMENT"));
	}

	[TestMethod]
	public void Score_SameCaseAsTheQuery_OutranksADifferentCase()
	{
		var matcher = new FuzzyMatcher("Fmt");
		Assert.IsGreaterThan(matcher.Score("fmt"), matcher.Score("Fmt"));
	}

	[TestMethod]
	public void Score_WhitespaceInTheQuery_SeparatesPiecesThatAllMustMatch()
	{
		var matcher = new FuzzyMatcher("doc  fmt");

		// The pieces match in any order, and each piece scores on its own.
		Assert.IsGreaterThan(0, matcher.Score("Format Document"));
		Assert.AreEqual(0, matcher.Score("Document"));
		Assert.AreEqual(0, matcher.Score("Format"));

		Assert.AreEqual(
			new FuzzyMatcher("doc").Score("Format Document") + new FuzzyMatcher("fmt").Score("Format Document"),
			matcher.Score("Format Document")
		);
	}

	[TestMethod]
	public void Score_CharactersTheReferenceDrops_AreIgnored()
	{
		int expected = new FuzzyMatcher("fmtdoc").Score("Format Document");

		Assert.AreEqual(expected, new FuzzyMatcher("*fmt*doc").Score("Format Document"));
		Assert.AreEqual(expected, new FuzzyMatcher("fmt\u2026doc").Score("Format Document"));
		Assert.AreEqual(expected, new FuzzyMatcher("fmt\"doc").Score("Format Document"));
	}

	[TestMethod]
	public void Score_PieceInQuotes_MatchesAsOneContiguousRun()
	{
		var matcher = new FuzzyMatcher("\"doc\"");

		Assert.IsGreaterThan(0, matcher.Score("Format Document"));
		Assert.AreEqual(0, new FuzzyMatcher("\"dcm\"").Score("Format Document"));
		Assert.IsGreaterThan(0, new FuzzyMatcher("dcm").Score("Format Document"));
	}

	[TestMethod]
	public void Score_CharactersOutOfOrder_DoNotMatch() => Assert.AreEqual(0, new FuzzyMatcher("tf").Score("Format"));

	[TestMethod]
	public void Score_WordStart_OutranksAnInnerMatch()
	{
		var matcher = new FuzzyMatcher("doc");

		// Both candidates hold the contiguous run "doc"; only the first starts a word with it.
		Assert.IsGreaterThan(matcher.Score("redoc"), matcher.Score("open doc"));
	}

	[TestMethod]
	public void Score_CharactersAfterASeparator_OutrankCharactersInsideAWord()
	{
		var matcher = new FuzzyMatcher("fb");

		Assert.IsGreaterThan(matcher.Score("fxbx"), matcher.Score("foo-bar"));
		Assert.IsGreaterThan(matcher.Score("fxbx"), matcher.Score("foo_bar"));
		Assert.IsGreaterThan(matcher.Score("fxbx"), matcher.Score("foo.bar"));
	}

	[TestMethod]
	public void Score_UpperCaseLetterThatBeginsARun_CountsAsAWordStart()
	{
		var matcher = new FuzzyMatcher("np");
		Assert.IsGreaterThan(matcher.Score("nxpx"), matcher.Score("nullPointer"));
	}

	[TestMethod]
	public void Score_UpperCaseLetterAfterAnUpperCaseOne_CountsAsAWordStart()
	{
		// The H and the R of XMLHttpRequest follow a capital, and they still begin a run, so they count as word starts.
		Assert.IsGreaterThan(new FuzzyMatcher("xhr").Score("xmlhttprequest"), new FuzzyMatcher("xhr").Score("XMLHttpRequest"));
	}

	[TestMethod]
	public void Score_ContiguousRun_OutranksAScatteredMatch()
	{
		var matcher = new FuzzyMatcher("abc");
		Assert.IsGreaterThan(matcher.Score("xaxbxcx"), matcher.Score("xabcx"));
	}

	[TestMethod]
	public void Score_LongerRun_OutranksAShorterRun()
	{
		var matcher = new FuzzyMatcher("abcd");
		Assert.IsGreaterThan(matcher.Score("abxcd"), matcher.Score("xabcdx"));
	}

	[TestMethod]
	public void Score_LabelThatStartsWithTheQuery_OutranksAnyOtherLabelMatch()
	{
		var matcher = new FuzzyMatcher("for");

		int prefix = matcher.Score("Format");
		int inner = matcher.Score("Reformat Document For Real");

		Assert.IsGreaterThan(inner, prefix);
	}

	[TestMethod]
	public void Score_LongerPrefixShare_OutranksAShorterOne()
	{
		var matcher = new FuzzyMatcher("for");
		Assert.IsGreaterThan(matcher.Score("Formatting Options"), matcher.Score("Format"));
	}

	[TestMethod]
	public void Score_LabelMatch_OutranksADescriptionOnlyMatch()
	{
		var matcher = new FuzzyMatcher("file");

		int onLabel = matcher.Score("xx file xx", "other");
		int onDescription = matcher.Score("unrelated", "file");

		Assert.IsGreaterThan(0, onDescription);
		Assert.IsGreaterThan(onDescription, onLabel);
	}

	[TestMethod]
	public void Score_QueryThatMatchesNeither_IsZero() => Assert.AreEqual(0, new FuzzyMatcher("zzz").Score("Format Document", "Edit"));

	[TestMethod]
	public void Score_EmptyQuery_IsZeroAndTheMatcherIsEmpty()
	{
		var matcher = new FuzzyMatcher("  ");

		Assert.IsTrue(matcher.IsEmpty);
		Assert.AreEqual(0, matcher.Score("Format Document"));
		Assert.IsFalse(matcher.TryMatch("Format Document", null, out _));
	}

	[TestMethod]
	public void Score_PathModeWithoutASeparator_PrefersTheFileNameOverTheFolders()
	{
		var matcher = new FuzzyMatcher("main", FuzzyMatchMode.Path);

		int fileNameMatch = matcher.Score("Main.cs", "src/App");
		int folderMatch = matcher.Score("Program.cs", "src/main/App");

		Assert.IsGreaterThan(0, folderMatch);
		Assert.IsGreaterThan(folderMatch, fileNameMatch);
	}

	[TestMethod]
	public void Score_PathModeWithASeparator_MatchesAcrossTheFolderAndTheFileName()
	{
		var matcher = new FuzzyMatcher("app/main", FuzzyMatchMode.Path);

		Assert.IsGreaterThan(0, matcher.Score("Main.cs", "src/App"));
		Assert.AreEqual(0, matcher.Score("Main.cs", "src/Lib"));
	}

	[TestMethod]
	public void Score_ForwardAndBackwardSlash_AreEqual()
	{
		Assert.IsGreaterThan(0, new FuzzyMatcher("app/main", FuzzyMatchMode.Path).Score("Main.cs", "src\\App"));
		Assert.IsGreaterThan(0, new FuzzyMatcher("app\\main", FuzzyMatchMode.Path).Score("Main.cs", "src/App"));
		Assert.IsGreaterThan(0, new FuzzyMatcher("app\\main", FuzzyMatchMode.Path).Score("Main.cs", "src\\App"));
	}

	[TestMethod]
	public void Score_TextModeDescription_IsMatchedWhenTheLabelDoesNot()
	{
		var matcher = new FuzzyMatcher("editfmt");

		Assert.IsGreaterThan(0, matcher.Score("Format", "Edit"));
		Assert.AreEqual(0, new FuzzyMatcher("editfmt").Score("Format"));
	}

	[TestMethod]
	public void Score_LongCandidate_ScoresLikeAShortOneBeyondTheStackBuffers()
	{
		var matcher = new FuzzyMatcher("format");
		string padding = new('x', 3000);

		// The label does not start with the query, so the score is the match alone and the padding changes nothing.
		Assert.AreEqual(matcher.Score("xx Format"), matcher.Score("xx Format" + padding));
		Assert.IsGreaterThan(0, matcher.Score(padding + " Format"));
	}

	[TestMethod]
	public void Score_PairBeyondTheMatrixBound_DoesNotMatch()
	{
		var matcher = new FuzzyMatcher(new string('a', 1000));

		Assert.IsGreaterThan(0, matcher.Score(new string('a', 4000)));
		Assert.AreEqual(0, matcher.Score(new string('a', 5000)));
	}

	[TestMethod]
	public void Score_ForAnOrdinaryCandidate_AllocatesNothing()
	{
		var matcher = new FuzzyMatcher("fmtdoc");

		string label = "Format Document";
		string description = "Edit";

		// The first calls warm the type and the pool, so the measured calls show the steady state.
		for (int index = 0; index < 100; index++)
			_ = matcher.Score(label, description);

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1000; index++)
			_ = matcher.Score(label, description);

		Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before);
	}

	[TestMethod]
	public void TryMatch_AbbreviationOfSeveralWords_ReportsEachMatchedRun()
	{
		var matcher = new FuzzyMatcher("fmtdoc");

		Assert.IsTrue(matcher.TryMatch("Format Document", null, out FuzzyMatch match));

		// f, m, t of "Format", then the contiguous run "Doc" of "Document".
		Assert.AreSequenceEqual([new MatchRange(0, 1), new MatchRange(3, 1), new MatchRange(5, 1), new MatchRange(7, 3)], [.. match.LabelRanges]);
		Assert.IsEmpty(match.DescriptionRanges);
		Assert.AreEqual(matcher.Score("Format Document"), match.Score);
	}

	[TestMethod]
	public void TryMatch_LabelThatStartsWithTheQuery_ReportsThePrefix()
	{
		var matcher = new FuzzyMatcher("form");

		Assert.IsTrue(matcher.TryMatch("Format Document", null, out FuzzyMatch match));
		Assert.AreSequenceEqual([new MatchRange(0, 4)], [.. match.LabelRanges]);
	}

	[TestMethod]
	public void TryMatch_MatchOnTheDescription_ReportsTheRangesOfTheDescription()
	{
		var matcher = new FuzzyMatcher("cat");

		Assert.IsTrue(matcher.TryMatch("Open", "Category", out FuzzyMatch match));
		Assert.IsEmpty(match.LabelRanges);
		Assert.AreSequenceEqual([new MatchRange(0, 3)], [.. match.DescriptionRanges]);
	}

	[TestMethod]
	public void TryMatch_MatchAcrossTheDescriptionAndTheLabel_SplitsTheRanges()
	{
		var matcher = new FuzzyMatcher("src/main", FuzzyMatchMode.Path);

		Assert.IsTrue(matcher.TryMatch("Main.cs", "src/App", out FuzzyMatch match));

		// "src/" on the folder path, then the file name; the separator that joins the two belongs to neither part.
		Assert.AreSequenceEqual([new MatchRange(0, 4)], [.. match.DescriptionRanges]);
		Assert.AreSequenceEqual([new MatchRange(0, 4)], [.. match.LabelRanges]);
	}

	[TestMethod]
	public void TryMatch_NoMatch_ReturnsFalseWithADefaultMatch()
	{
		Assert.IsFalse(new FuzzyMatcher("zzz").TryMatch("Format", "Edit", out FuzzyMatch match));
		Assert.AreEqual(default, match);
	}

	[TestMethod]
	public void Constructor_NullQuery_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => new FuzzyMatcher(null!));

	[TestMethod]
	public void Constructor_UndefinedMode_Throws() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new FuzzyMatcher("a", (FuzzyMatchMode)99));

	[TestMethod]
	public void Score_NullLabel_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => new FuzzyMatcher("a").Score(null!));

	[TestMethod]
	public void TryMatch_NullLabel_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => new FuzzyMatcher("a").TryMatch(null!, null, out _));
}
