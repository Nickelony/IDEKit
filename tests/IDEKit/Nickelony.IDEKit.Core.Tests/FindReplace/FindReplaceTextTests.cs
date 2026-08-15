using System.Globalization;
using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class FindReplaceTextTests
{
	[TestMethod]
	public void BuildPattern_LiteralTextIsEscaped()
	{
		Assert.AreEqual(@"a\.b", FindReplaceText.BuildPattern("a.b", useRegex: false, matchWholeWord: false));
	}

	[TestMethod]
	public void BuildPattern_RegexPassesThrough()
	{
		Assert.AreEqual("a.b", FindReplaceText.BuildPattern("a.b", useRegex: true, matchWholeWord: false));
	}

	[TestMethod]
	public void BuildPattern_WholeWordWrapsPatternInBoundaries()
	{
		Assert.AreEqual(@"\bword\b", FindReplaceText.BuildPattern("word", useRegex: false, matchWholeWord: true));
	}

	[TestMethod]
	public void BuildPattern_BlankFindTextReturnsEmpty()
	{
		Assert.AreEqual(string.Empty, FindReplaceText.BuildPattern(string.Empty, useRegex: false, matchWholeWord: false));
	}

	[TestMethod]
	public void BuildRegexOptions_IgnoresCaseUnlessRequested()
	{
		Assert.AreEqual(RegexOptions.None, FindReplaceText.BuildRegexOptions(caseSensitive: true));
		Assert.AreEqual(
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
			FindReplaceText.BuildRegexOptions(caseSensitive: false));
	}

	[TestMethod]
	public void CountMatches_CountsCaseInsensitively()
	{
		Assert.AreEqual(2, FindReplaceText.CountMatches("aaa bbb aaa", new TextSearchQuery("aaa", RegexOptions.IgnoreCase)));
	}

	[TestMethod]
	public void CountMatches_EmptyPatternReturnsZero()
	{
		Assert.AreEqual(0, FindReplaceText.CountMatches("aaa", new TextSearchQuery(string.Empty, RegexOptions.None)));
	}

	[TestMethod]
	public void DefaultQuery_Throws()
	{
		// default(TextSearchQuery) carries a null pattern, so the helpers reject it instead of
		// searching for an empty pattern.
		TextSearchQuery query = default;

		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceText.CountMatches("aaa", query));
		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceText.FindAllMatches("aaa", query));
		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceText.FindNextMatch("aaa", 0, query));
		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceText.FindPreviousMatch("aaa", 3, query));
		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceText.ReplaceAll("aaa", query, "x"));
	}

	[TestMethod]
	public void NullArguments_Throw()
	{
		// The helpers validate the text and replacement arguments independently of the query.
		var query = new TextSearchQuery("aaa", RegexOptions.None);

		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceText.CountMatches(null!, query));
		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceText.FindAllMatches(null!, query));
		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceText.FindNextMatch(null!, 0, query));
		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceText.FindPreviousMatch(null!, 3, query));
		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceText.ReplaceAll(null!, query, "x"));
		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceText.ReplaceAll("aaa", query, null!));
	}

	[TestMethod]
	public void CountMatches_NegativeMatchTimeout_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			FindReplaceText.CountMatches("aaa", new TextSearchQuery("a", RegexOptions.None, TimeSpan.FromSeconds(-1))));
	}

	[TestMethod]
	public void CountMatches_InfiniteMatchTimeout_IsAccepted()
	{
		Assert.AreEqual(3, FindReplaceText.CountMatches("aaa", new TextSearchQuery("a", RegexOptions.None, Regex.InfiniteMatchTimeout)));
	}

	[TestMethod]
	public void CountMatches_InvalidPattern_ThrowsRegexParseException()
	{
		Assert.ThrowsExactly<RegexParseException>(() =>
			FindReplaceText.CountMatches("aaa", new TextSearchQuery("(", RegexOptions.None)));
	}

	[TestMethod]
	public void CountMatches_CatastrophicPattern_RespectsMatchTimeout()
	{
		// The nested quantifier backtracks catastrophically, so only the finite timeout can finish it.
		Assert.ThrowsExactly<RegexMatchTimeoutException>(() =>
			FindReplaceText.CountMatches(
				new string('a', 64) + "!",
				new TextSearchQuery("(a+)+b", RegexOptions.None, TimeSpan.FromMilliseconds(100))));
	}

	[TestMethod]
	public void FindAllMatches_CatastrophicPattern_RespectsMatchTimeout()
	{
		// A finite timeout is propagated to the regex, so reading the matches fails instead of
		// letting the catastrophic backtracking run to completion.
		Assert.ThrowsExactly<RegexMatchTimeoutException>(() =>
			FindReplaceText.FindAllMatches(
				new string('a', 64) + "!",
				new TextSearchQuery("(a+)+b", RegexOptions.None, TimeSpan.FromMilliseconds(100))).Count);
	}

	[TestMethod]
	public void FindAllMatches_ReturnsEveryMatch()
	{
		MatchCollection matches = FindReplaceText.FindAllMatches("aaa bbb aaa", new TextSearchQuery("aaa", RegexOptions.None));

		Assert.AreEqual(2, matches.Count);
	}

	[TestMethod]
	public void FindAllMatches_EmptyPatternReturnsNoMatches()
	{
		MatchCollection matches = FindReplaceText.FindAllMatches("aaa", new TextSearchQuery(string.Empty, RegexOptions.None));

		Assert.IsEmpty(matches);
	}

	[TestMethod]
	public void FindNextMatch_ReturnsFirstMatchAtOrAfterStartOffset()
	{
		var query = new TextSearchQuery("aaa", RegexOptions.None);
		const string text = "aaa bbb aaa ccc";

		Assert.AreEqual(0, FindReplaceText.FindNextMatch(text, 0, query)!.Index);
		Assert.AreEqual(8, FindReplaceText.FindNextMatch(text, 4, query)!.Index);
	}

	[TestMethod]
	public void FindNextMatch_NoMatch_ReturnsNull()
	{
		Assert.IsNull(FindReplaceText.FindNextMatch("bbb", 0, new TextSearchQuery("aaa", RegexOptions.None)));
	}

	[TestMethod]
	public void FindNextMatch_EmptyPattern_ReturnsNull()
	{
		Assert.IsNull(FindReplaceText.FindNextMatch("aaa", 0, new TextSearchQuery(string.Empty, RegexOptions.None)));
	}

	[TestMethod]
	public void FindNextMatch_AnchorsResolveAgainstTheDocument()
	{
		// '^' asserts the start of the document, so it cannot match at a later start offset.
		Assert.IsNull(FindReplaceText.FindNextMatch("xxx aaa", 4, new TextSearchQuery("^aaa", RegexOptions.None)));
		Assert.AreEqual(0, FindReplaceText.FindNextMatch("aaa xxx", 0, new TextSearchQuery("^aaa", RegexOptions.None))!.Index);
	}

	[TestMethod]
	public void FindNextMatch_WordBoundaryResolvesAgainstTheDocument()
	{
		// The 'a' at offset 2 is preceded by a word character, so '\b' does not match there.
		Assert.IsNull(FindReplaceText.FindNextMatch("xxaaa yyy", 2, new TextSearchQuery(@"\baaa\b", RegexOptions.None)));
	}

	[TestMethod]
	public void FindNextMatch_OffsetOutsideDocument_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			FindReplaceText.FindNextMatch("aaa", 4, new TextSearchQuery("aaa", RegexOptions.None)));
	}

	[TestMethod]
	public void FindPreviousMatch_ReturnsLastMatchEndingBeforeEndOffset()
	{
		var query = new TextSearchQuery("aaa", RegexOptions.None);
		const string text = "aaa bbb aaa ccc";

		Assert.AreEqual(0, FindReplaceText.FindPreviousMatch(text, 4, query)!.Index);
		Assert.AreEqual(8, FindReplaceText.FindPreviousMatch(text, 12, query)!.Index);
	}

	[TestMethod]
	public void FindPreviousMatch_MatchReachingBeyondEndOffset_IsExcluded()
	{
		var query = new TextSearchQuery("bbb", RegexOptions.None);

		Assert.IsNull(FindReplaceText.FindPreviousMatch("aaa bbb", 6, query));
		Assert.AreEqual(4, FindReplaceText.FindPreviousMatch("aaa bbb", 7, query)!.Index);
	}

	[TestMethod]
	public void FindPreviousMatch_NoMatch_ReturnsNull()
	{
		Assert.IsNull(FindReplaceText.FindPreviousMatch("bbb", 3, new TextSearchQuery("aaa", RegexOptions.None)));
	}

	[TestMethod]
	public void FindPreviousMatch_EndAnchorResolvesAgainstTheDocument()
	{
		// '$' asserts the end of the document, so it requires the match to reach the document end.
		Assert.IsNull(FindReplaceText.FindPreviousMatch("aaa xxx", 3, new TextSearchQuery("aaa$", RegexOptions.None)));
		Assert.AreEqual(0, FindReplaceText.FindPreviousMatch("aaa", 3, new TextSearchQuery("aaa$", RegexOptions.None))!.Index);
	}

	[TestMethod]
	public void FindPreviousMatch_EndOffsetOutsideDocument_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			FindReplaceText.FindPreviousMatch("aaa", 4, new TextSearchQuery("aaa", RegexOptions.None)));
	}

	[TestMethod]
	public void FindHelpers_NegativeOffsets_Throw()
	{
		var query = new TextSearchQuery("aaa", RegexOptions.None);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FindReplaceText.FindNextMatch("aaa", -1, query));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FindReplaceText.FindPreviousMatch("aaa", -1, query));
	}

	[TestMethod]
	public void FindNextMatch_StartOffsetAtDocumentEnd_ReturnsNull()
	{
		// The offset is valid (it equals the text length) but no match can start there.
		Assert.IsNull(FindReplaceText.FindNextMatch("aaa", 3, new TextSearchQuery("aaa", RegexOptions.None)));
	}

	[TestMethod]
	public void FindHelpers_InfiniteMatchTimeout_IsAccepted()
	{
		var query = new TextSearchQuery("aaa", RegexOptions.None, Regex.InfiniteMatchTimeout);

		Assert.AreEqual(0, FindReplaceText.FindNextMatch("aaa", 0, query)!.Index);
		Assert.AreEqual(0, FindReplaceText.FindPreviousMatch("aaa", 3, query)!.Index);
	}

	[TestMethod]
	public void FindHelpers_FiniteMatchTimeout_RespectsTimeout()
	{
		// A finite query timeout is enforced by the find helpers as well, so a catastrophic pattern
		// fails instead of running to completion.
		var query = new TextSearchQuery("(a+)+b", RegexOptions.None, TimeSpan.FromMilliseconds(100));
		string text = new string('a', 64) + "!";

		Assert.ThrowsExactly<RegexMatchTimeoutException>(() => FindReplaceText.FindNextMatch(text, 0, query));
		Assert.ThrowsExactly<RegexMatchTimeoutException>(() => FindReplaceText.FindPreviousMatch(text, text.Length, query));
	}

	[TestMethod]
	public void FiniteMatchTimeout_Throws()
	{
		var query = new TextSearchQuery("(a+)+b", RegexOptions.None, TimeSpan.FromMilliseconds(100));
		string text = new string('a', 64) + "!";

		Assert.ThrowsExactly<RegexMatchTimeoutException>(() => FindReplaceText.CountMatches(text, query));
		Assert.ThrowsExactly<RegexMatchTimeoutException>(() => FindReplaceText.FindAllMatches(text, query).Count);
		Assert.ThrowsExactly<RegexMatchTimeoutException>(() => FindReplaceText.ReplaceAll(text, query, "x"));
	}

	[TestMethod]
	[DoNotParallelize]
	public void FindHelpers_UnderTurkishCulture_MatchWithInvariantCaseFolding()
	{
		RegexOptions options = FindReplaceText.BuildRegexOptions(caseSensitive: false);
		CultureInfo originalCulture = CultureInfo.CurrentCulture;
		CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;

		try
		{
			// Turkish casing maps 'I' and 'i' to different letters, so culture-aware case folding
			// would refuse this match; the invariant options keep the result culture-independent.
			CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
			CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

			Assert.AreEqual(0, FindReplaceText.FindNextMatch("if (x)", 0, new TextSearchQuery("IF", options))!.Index);
			Assert.AreEqual(0, FindReplaceText.FindPreviousMatch("IF (x)", 6, new TextSearchQuery("if", options))!.Index);
		}
		finally
		{
			CultureInfo.CurrentCulture = originalCulture;
			CultureInfo.CurrentUICulture = originalUiCulture;
		}
	}

	[TestMethod]
	public void ReplaceAll_ReplacesEveryMatch()
	{
		Assert.AreEqual(
			"xx bbb xx",
			FindReplaceText.ReplaceAll("aaa bbb aaa", new TextSearchQuery("aaa", RegexOptions.None), "xx"));
	}

	[TestMethod]
	public void ReplaceAll_CatastrophicPattern_RespectsMatchTimeout()
	{
		Assert.ThrowsExactly<RegexMatchTimeoutException>(() =>
			FindReplaceText.ReplaceAll(
				new string('a', 64) + "!",
				new TextSearchQuery("(a+)+b", RegexOptions.None, TimeSpan.FromMilliseconds(100)),
				"x"));
	}

	[TestMethod]
	public void ReplaceAll_ReplacementIsSubstitutionPattern()
	{
		Assert.AreEqual(
			"Smith John",
			FindReplaceText.ReplaceAll("John Smith", new TextSearchQuery(@"(\w+) (\w+)", RegexOptions.None), "$2 $1"));

		Assert.AreEqual(
			"$1",
			FindReplaceText.ReplaceAll("a", new TextSearchQuery("a", RegexOptions.None), "$$1"));
	}

	[TestMethod]
	public void ReplaceAll_EmptyPatternReturnsOriginalText()
	{
		Assert.AreEqual("aaa", FindReplaceText.ReplaceAll("aaa", new TextSearchQuery(string.Empty, RegexOptions.None), "x"));
	}

	[TestMethod]
	public void BuildPattern_WholeWordRegexAlternation_KeepsBothBoundaries()
	{
		string pattern = FindReplaceText.BuildPattern("foo|bar", useRegex: true, matchWholeWord: true);

		Assert.AreEqual(@"\b(?:foo|bar)\b", pattern);
		Assert.IsFalse(Regex.IsMatch("foobar", pattern));
		Assert.IsTrue(Regex.IsMatch("foo bar", pattern));
	}

	[TestMethod]
	public void BuildPattern_NullFindText_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceText.BuildPattern(
			null!, useRegex: false, matchWholeWord: false));
	}
}
