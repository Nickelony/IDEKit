using System.Globalization;
using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class FindReplaceOperationsTests
{
	[TestMethod]
	public void BuildPattern_LiteralTextIsEscaped() =>
		Assert.AreEqual(@"a\.b", FindReplaceOperations.BuildPattern("a.b", useRegex: false, matchWholeWord: false));

	[TestMethod]
	public void BuildPattern_RegexPassesThrough() =>
		Assert.AreEqual("a.b", FindReplaceOperations.BuildPattern("a.b", useRegex: true, matchWholeWord: false));

	[TestMethod]
	public void BuildPattern_WholeWordWrapsPatternInBoundaries() =>
		Assert.AreEqual(@"\bword\b", FindReplaceOperations.BuildPattern("word", useRegex: false, matchWholeWord: true));

	[TestMethod]
	public void BuildPattern_BlankFindTextReturnsEmpty() =>
		Assert.AreEqual(string.Empty, FindReplaceOperations.BuildPattern(string.Empty, useRegex: false, matchWholeWord: false));

	[TestMethod]
	public void BuildRegexOptions_IgnoresCaseUnlessRequested()
	{
		Assert.AreEqual(RegexOptions.None, FindReplaceOperations.BuildRegexOptions(caseSensitive: true));
		Assert.AreEqual(RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, FindReplaceOperations.BuildRegexOptions(caseSensitive: false));
	}

	[TestMethod]
	public void CountMatches_CountsCaseInsensitively() =>
		Assert.AreEqual(2, FindReplaceOperations.CountMatches("aaa bbb aaa", new SearchQuery("aaa", RegexOptions.IgnoreCase)));

	[TestMethod]
	public void CountMatches_EmptyPatternReturnsZero() =>
		Assert.AreEqual(0, FindReplaceOperations.CountMatches("aaa", new SearchQuery(string.Empty, RegexOptions.None)));

	[TestMethod]
	public void DefaultQuery_Throws()
	{
		// default(SearchQuery) carries a null pattern, so the helpers reject it instead of
		// searching for an empty pattern.
		SearchQuery query = default;

		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceOperations.CountMatches("aaa", query));
		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceOperations.FindAllMatches("aaa", query));
		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceOperations.FindNextMatch("aaa", 0, query));
		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceOperations.FindPreviousMatch("aaa", 3, query));
		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceOperations.ReplaceAll("aaa", query, "x"));
	}

	[TestMethod]
	public void NullArguments_Throw()
	{
		// The helpers validate the text and replacement arguments independently of the query.
		var query = new SearchQuery("aaa", RegexOptions.None);

		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceOperations.CountMatches(null!, query));
		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceOperations.FindAllMatches(null!, query));
		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceOperations.FindNextMatch(null!, 0, query));
		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceOperations.FindPreviousMatch(null!, 3, query));
		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceOperations.ReplaceAll(null!, query, "x"));
		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceOperations.ReplaceAll("aaa", query, null!));
	}

	[TestMethod]
	public void CountMatches_NegativeMatchTimeout_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			FindReplaceOperations.CountMatches("aaa", new SearchQuery("a", RegexOptions.None, TimeSpan.FromSeconds(-1)))
		);
	}

	[TestMethod]
	public void CountMatches_InfiniteMatchTimeout_IsAccepted() =>
		Assert.AreEqual(3, FindReplaceOperations.CountMatches("aaa", new SearchQuery("a", RegexOptions.None, Regex.InfiniteMatchTimeout)));

	[TestMethod]
	public void CountMatches_InvalidPattern_ThrowsRegexParseException() =>
		Assert.ThrowsExactly<RegexParseException>(() => FindReplaceOperations.CountMatches("aaa", new SearchQuery("(", RegexOptions.None)));

	[TestMethod]
	public void CountMatches_CatastrophicPattern_RespectsMatchTimeout()
	{
		// The nested quantifier backtracks catastrophically, so only the finite timeout can finish it.
		Assert.ThrowsExactly<RegexMatchTimeoutException>(() =>
			FindReplaceOperations.CountMatches(
				new string('a', 64) + "!",
				new SearchQuery("(a+)+b", RegexOptions.None, TimeSpan.FromMilliseconds(100))
			)
		);
	}

	[TestMethod]
	public void FindAllMatches_CatastrophicPattern_RespectsMatchTimeout()
	{
		// A finite timeout is propagated to the regex, so reading the matches fails instead of
		// letting the catastrophic backtracking run to completion.
		Assert.ThrowsExactly<RegexMatchTimeoutException>(() =>
			FindReplaceOperations
				.FindAllMatches(new string('a', 64) + "!", new SearchQuery("(a+)+b", RegexOptions.None, TimeSpan.FromMilliseconds(100)))
				.Count
		);
	}

	[TestMethod]
	public void FindAllMatches_ReturnsEveryMatch()
	{
		MatchCollection matches = FindReplaceOperations.FindAllMatches("aaa bbb aaa", new SearchQuery("aaa", RegexOptions.None));
		Assert.HasCount(2, matches);
	}

	[TestMethod]
	public void FindAllMatches_EmptyPatternReturnsNoMatches()
	{
		MatchCollection matches = FindReplaceOperations.FindAllMatches("aaa", new SearchQuery(string.Empty, RegexOptions.None));
		Assert.IsEmpty(matches);
	}

	[TestMethod]
	public void FindNextMatch_ReturnsFirstMatchAtOrAfterStartOffset()
	{
		var query = new SearchQuery("aaa", RegexOptions.None);

		const string text = "aaa bbb aaa ccc";

		Assert.AreEqual(0, FindReplaceOperations.FindNextMatch(text, 0, query)!.Index);
		Assert.AreEqual(8, FindReplaceOperations.FindNextMatch(text, 4, query)!.Index);
	}

	[TestMethod]
	public void FindNextMatch_NoMatch_ReturnsNull() =>
		Assert.IsNull(FindReplaceOperations.FindNextMatch("bbb", 0, new SearchQuery("aaa", RegexOptions.None)));

	[TestMethod]
	public void FindNextMatch_EmptyPattern_ReturnsNull() =>
		Assert.IsNull(FindReplaceOperations.FindNextMatch("aaa", 0, new SearchQuery(string.Empty, RegexOptions.None)));

	[TestMethod]
	public void FindNextMatch_AnchorsResolveAgainstTheDocument()
	{
		// '^' asserts the start of the document, so it cannot match at a later start offset.
		Assert.IsNull(FindReplaceOperations.FindNextMatch("xxx aaa", 4, new SearchQuery("^aaa", RegexOptions.None)));
		Assert.AreEqual(0, FindReplaceOperations.FindNextMatch("aaa xxx", 0, new SearchQuery("^aaa", RegexOptions.None))!.Index);
	}

	[TestMethod]
	public void FindNextMatch_WordBoundaryResolvesAgainstTheDocument()
	{
		// The 'a' at offset 2 is preceded by a word character, so '\b' does not match there.
		Assert.IsNull(FindReplaceOperations.FindNextMatch("xxaaa yyy", 2, new SearchQuery(@"\baaa\b", RegexOptions.None)));
	}

	[TestMethod]
	public void FindNextMatch_OffsetOutsideDocument_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			FindReplaceOperations.FindNextMatch("aaa", 4, new SearchQuery("aaa", RegexOptions.None))
		);
	}

	[TestMethod]
	public void FindPreviousMatch_ReturnsLastMatchEndingBeforeEndOffset()
	{
		var query = new SearchQuery("aaa", RegexOptions.None);

		const string text = "aaa bbb aaa ccc";

		Assert.AreEqual(0, FindReplaceOperations.FindPreviousMatch(text, 4, query)!.Index);
		Assert.AreEqual(8, FindReplaceOperations.FindPreviousMatch(text, 12, query)!.Index);
	}

	[TestMethod]
	public void FindPreviousMatch_MatchReachingBeyondEndOffset_IsExcluded()
	{
		var query = new SearchQuery("bbb", RegexOptions.None);

		Assert.IsNull(FindReplaceOperations.FindPreviousMatch("aaa bbb", 6, query));
		Assert.AreEqual(4, FindReplaceOperations.FindPreviousMatch("aaa bbb", 7, query)!.Index);
	}

	[TestMethod]
	public void FindPreviousMatch_NoMatch_ReturnsNull() =>
		Assert.IsNull(FindReplaceOperations.FindPreviousMatch("bbb", 3, new SearchQuery("aaa", RegexOptions.None)));

	[TestMethod]
	public void FindPreviousMatch_EndAnchorResolvesAgainstTheDocument()
	{
		// '$' asserts the end of the document, so it requires the match to reach the document end.
		Assert.IsNull(FindReplaceOperations.FindPreviousMatch("aaa xxx", 3, new SearchQuery("aaa$", RegexOptions.None)));
		Assert.AreEqual(0, FindReplaceOperations.FindPreviousMatch("aaa", 3, new SearchQuery("aaa$", RegexOptions.None))!.Index);
	}

	[TestMethod]
	public void FindPreviousMatch_EndOffsetOutsideDocument_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			FindReplaceOperations.FindPreviousMatch("aaa", 4, new SearchQuery("aaa", RegexOptions.None))
		);
	}

	[TestMethod]
	public void FindHelpers_NegativeOffsets_Throw()
	{
		var query = new SearchQuery("aaa", RegexOptions.None);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FindReplaceOperations.FindNextMatch("aaa", -1, query));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FindReplaceOperations.FindPreviousMatch("aaa", -1, query));
	}

	[TestMethod]
	public void FindNextMatch_StartOffsetAtDocumentEnd_ReturnsNull()
	{
		// The offset is valid (it equals the text length) but no match can start there.
		Assert.IsNull(FindReplaceOperations.FindNextMatch("aaa", 3, new SearchQuery("aaa", RegexOptions.None)));
	}

	[TestMethod]
	public void FindHelpers_InfiniteMatchTimeout_IsAccepted()
	{
		var query = new SearchQuery("aaa", RegexOptions.None, Regex.InfiniteMatchTimeout);

		Assert.AreEqual(0, FindReplaceOperations.FindNextMatch("aaa", 0, query)!.Index);
		Assert.AreEqual(0, FindReplaceOperations.FindPreviousMatch("aaa", 3, query)!.Index);
	}

	[TestMethod]
	public void FindHelpers_FiniteMatchTimeout_RespectsTimeout()
	{
		// A finite query timeout is enforced by the find helpers as well, so a catastrophic pattern
		// fails instead of running to completion.
		var query = new SearchQuery("(a+)+b", RegexOptions.None, TimeSpan.FromMilliseconds(100));

		string text = new string('a', 64) + "!";

		Assert.ThrowsExactly<RegexMatchTimeoutException>(() => FindReplaceOperations.FindNextMatch(text, 0, query));
		Assert.ThrowsExactly<RegexMatchTimeoutException>(() => FindReplaceOperations.FindPreviousMatch(text, text.Length, query));
	}

	[TestMethod]
	public void FiniteMatchTimeout_Throws()
	{
		var query = new SearchQuery("(a+)+b", RegexOptions.None, TimeSpan.FromMilliseconds(100));

		string text = new string('a', 64) + "!";

		Assert.ThrowsExactly<RegexMatchTimeoutException>(() => FindReplaceOperations.CountMatches(text, query));
		Assert.ThrowsExactly<RegexMatchTimeoutException>(() => FindReplaceOperations.FindAllMatches(text, query).Count);
		Assert.ThrowsExactly<RegexMatchTimeoutException>(() => FindReplaceOperations.ReplaceAll(text, query, "x"));
	}

	[TestMethod]
	[DoNotParallelize]
	public void FindHelpers_UnderTurkishCulture_MatchWithInvariantCaseFolding()
	{
		RegexOptions options = FindReplaceOperations.BuildRegexOptions(caseSensitive: false);

		CultureInfo originalCulture = CultureInfo.CurrentCulture;
		CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;

		try
		{
			// Turkish casing maps 'I' and 'i' to different letters, so culture-aware case folding
			// would refuse this match; the invariant options keep the result culture-independent.
			CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
			CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

			Assert.AreEqual(0, FindReplaceOperations.FindNextMatch("if (x)", 0, new SearchQuery("IF", options))!.Index);
			Assert.AreEqual(0, FindReplaceOperations.FindPreviousMatch("IF (x)", 6, new SearchQuery("if", options))!.Index);
		}
		finally
		{
			CultureInfo.CurrentCulture = originalCulture;
			CultureInfo.CurrentUICulture = originalUiCulture;
		}
	}

	[TestMethod]
	public void ReplaceAll_ReplacesEveryMatch() =>
		Assert.AreEqual("xx bbb xx", FindReplaceOperations.ReplaceAll("aaa bbb aaa", new SearchQuery("aaa", RegexOptions.None), "xx"));

	[TestMethod]
	public void ReplaceAll_CatastrophicPattern_RespectsMatchTimeout()
	{
		Assert.ThrowsExactly<RegexMatchTimeoutException>(() =>
			FindReplaceOperations.ReplaceAll(
				new string('a', 64) + "!",
				new SearchQuery("(a+)+b", RegexOptions.None, TimeSpan.FromMilliseconds(100)),
				"x"
			)
		);
	}

	[TestMethod]
	public void ReplaceAll_ReplacementIsSubstitutionPattern()
	{
		Assert.AreEqual("Smith John", FindReplaceOperations.ReplaceAll("John Smith", new SearchQuery(@"(\w+) (\w+)", RegexOptions.None), "$2 $1"));
		Assert.AreEqual("$1", FindReplaceOperations.ReplaceAll("a", new SearchQuery("a", RegexOptions.None), "$$1"));
	}

	[TestMethod]
	public void ReplaceAll_EmptyPatternReturnsOriginalText() =>
		Assert.AreEqual("aaa", FindReplaceOperations.ReplaceAll("aaa", new SearchQuery(string.Empty, RegexOptions.None), "x"));

	[TestMethod]
	public void BuildPattern_WholeWordRegexAlternation_KeepsBothBoundaries()
	{
		string pattern = FindReplaceOperations.BuildPattern("foo|bar", useRegex: true, matchWholeWord: true);

		Assert.AreEqual(@"\b(?:foo|bar)\b", pattern);
		Assert.IsFalse(Regex.IsMatch("foobar", pattern));
		Assert.IsTrue(Regex.IsMatch("foo bar", pattern));
	}

	[TestMethod]
	public void BuildPattern_NullFindText_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => FindReplaceOperations.BuildPattern(null!, useRegex: false, matchWholeWord: false));

	[TestMethod]
	public void Helpers_RejectRightToLeftOptions()
	{
		// The helpers define their own search direction, so a right-to-left regex would silently
		// violate their documented next/previous/document-order contracts.
		var query = new SearchQuery("a", RegexOptions.RightToLeft);

		Assert.ThrowsExactly<ArgumentException>(() => FindReplaceOperations.FindNextMatch("aaa", 0, query));
		Assert.ThrowsExactly<ArgumentException>(() => FindReplaceOperations.FindPreviousMatch("aaa", 3, query));
		Assert.ThrowsExactly<ArgumentException>(() => FindReplaceOperations.CountMatches("aaa", query));
		Assert.ThrowsExactly<ArgumentException>(() => FindReplaceOperations.FindAllMatches("aaa", query));
		Assert.ThrowsExactly<ArgumentException>(() => FindReplaceOperations.ReplaceAll("aaa", query, "b"));
	}

	[TestMethod]
	public void Helpers_EmptyPatternWithRightToLeft_IsStillNoSearch()
	{
		// An empty pattern means "no search text", so it resolves its empty result before the
		// direction validation applies.
		var query = new SearchQuery(string.Empty, RegexOptions.RightToLeft);

		Assert.IsNull(FindReplaceOperations.FindNextMatch("aaa", 0, query));
		Assert.IsNull(FindReplaceOperations.FindPreviousMatch("aaa", 3, query));
	}

	[TestMethod]
	public void FindPreviousMatch_EmptyPattern_ReturnsNull() =>
		Assert.IsNull(FindReplaceOperations.FindPreviousMatch("aaa", 3, new SearchQuery(string.Empty, RegexOptions.None)));
}
