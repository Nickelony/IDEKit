using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins how the find helpers treat a pattern that can match the empty string: the matches carry a
/// zero length, and the caller's start and end offsets still decide direction and inclusion.
/// </summary>
[TestClass]
public sealed class FindReplaceTextZeroWidthTests
{
	[TestMethod]
	public void FindAllMatches_ZeroWidthPattern_ReturnsTheEmptyMatchesInDocumentOrder()
	{
		// The word boundary matches the empty string at both ends of the document, so the collection
		// holds two zero-length matches rather than being treated as "no match".
		MatchCollection matches = FindReplaceText.FindAllMatches("ab", new TextSearchQuery(@"\b", RegexOptions.None));

		Assert.AreEqual(2, matches.Count);
		Assert.AreEqual(0, matches[0].Index);
		Assert.AreEqual(0, matches[0].Length);
		Assert.AreEqual(2, matches[1].Index);
		Assert.AreEqual(0, matches[1].Length);
	}

	[TestMethod]
	public void CountMatches_ZeroWidthPattern_CountsTheEmptyMatches()
	{
		Assert.AreEqual(2, FindReplaceText.CountMatches("ab", new TextSearchQuery(@"\b", RegexOptions.None)));
	}

	[TestMethod]
	public void FindNextMatch_ZeroWidthMatchAtTheDocumentEnd_IsFound()
	{
		// The scan starts at the end offset, where "x*" matches the empty string, so the helper
		// reports a found match with a zero length instead of "no match".
		Match? match = FindReplaceText.FindNextMatch("ab", 2, new TextSearchQuery("x*", RegexOptions.None));

		Assert.IsNotNull(match);
		Assert.AreEqual(2, match!.Index);
		Assert.AreEqual(0, match.Length);
	}

	[TestMethod]
	public void FindNextMatch_ZeroWidthMatchBeforeTheStartOffset_IsSkipped()
	{
		// The start offset is the scan origin, so the empty match at index 1 wins over the one at 0.
		Match? match = FindReplaceText.FindNextMatch("ab", 1, new TextSearchQuery("x*", RegexOptions.None));

		Assert.IsNotNull(match);
		Assert.AreEqual(1, match!.Index);
	}

	[TestMethod]
	public void FindPreviousMatch_ZeroWidthMatchEndingExactlyAtTheEndOffset_IsIncluded()
	{
		// The end offset is compared with ">", so an empty match that ends exactly at it is the answer
		// rather than being excluded as reaching beyond it.
		Match? match = FindReplaceText.FindPreviousMatch("ab", 2, new TextSearchQuery("x*", RegexOptions.None));

		Assert.IsNotNull(match);
		Assert.AreEqual(2, match!.Index);
		Assert.AreEqual(0, match.Length);
	}

	[TestMethod]
	public void ReplaceAll_ZeroWidthPattern_InsertsAtEveryPosition()
	{
		// The replacement delegates to the regex, so an empty match also inserts between and around the
		// characters instead of being skipped.
		Assert.AreEqual(
			"-a-b-",
			FindReplaceText.ReplaceAll("ab", new TextSearchQuery("x*", RegexOptions.None), "-"));
	}
}
