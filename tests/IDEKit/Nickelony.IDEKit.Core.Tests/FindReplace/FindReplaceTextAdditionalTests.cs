using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the search-direction contract (RightToLeft rejection) and the empty-pattern behavior of the
/// find helpers.
/// </summary>
[TestClass]
public sealed class FindReplaceTextAdditionalTests
{
	[TestMethod]
	public void Helpers_RejectRightToLeftOptions()
	{
		// The helpers define their own search direction, so a right-to-left regex would silently
		// violate their documented next/previous/document-order contracts.
		var query = new TextSearchQuery("a", RegexOptions.RightToLeft);

		Assert.ThrowsExactly<ArgumentException>(() => FindReplaceText.FindNextMatch("aaa", 0, query));
		Assert.ThrowsExactly<ArgumentException>(() => FindReplaceText.FindPreviousMatch("aaa", 3, query));
		Assert.ThrowsExactly<ArgumentException>(() => FindReplaceText.CountMatches("aaa", query));
		Assert.ThrowsExactly<ArgumentException>(() => FindReplaceText.FindAllMatches("aaa", query));
		Assert.ThrowsExactly<ArgumentException>(() => FindReplaceText.ReplaceAll("aaa", query, "b"));
	}

	[TestMethod]
	public void Helpers_EmptyPatternWithRightToLeft_IsStillNoSearch()
	{
		// An empty pattern means "no search text", so it resolves its empty result before the
		// direction validation applies.
		var query = new TextSearchQuery(string.Empty, RegexOptions.RightToLeft);

		Assert.IsNull(FindReplaceText.FindNextMatch("aaa", 0, query));
		Assert.IsNull(FindReplaceText.FindPreviousMatch("aaa", 3, query));
	}

	[TestMethod]
	public void FindPreviousMatch_EmptyPattern_ReturnsNull()
	{
		Assert.IsNull(FindReplaceText.FindPreviousMatch("aaa", 3, new TextSearchQuery(string.Empty, RegexOptions.None)));
	}
}
