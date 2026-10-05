namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class AdjacentBookmarkLineResolverTests
{
	private static readonly int[] s_lines = [2, 5, 9];

	[TestMethod]
	public void Resolve_NullLines_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => AdjacentBookmarkLineResolver.Resolve(null!, currentLineNumber: 1, searchForward: true));

	[TestMethod]
	public void Resolve_NoBookmarks_ReturnsNull()
	{
		Assert.IsNull(AdjacentBookmarkLineResolver.Resolve([], currentLineNumber: 3, searchForward: true));
		Assert.IsNull(AdjacentBookmarkLineResolver.Resolve([], currentLineNumber: 3, searchForward: false));
	}

	[TestMethod]
	[DataRow(1, 2)]
	[DataRow(2, 5)]
	[DataRow(3, 5)]
	[DataRow(5, 9)]
	public void Resolve_Forward_ReturnsTheNextBookmarkedLine(int current, int expected) =>
		Assert.AreEqual(expected, AdjacentBookmarkLineResolver.Resolve(s_lines, current, searchForward: true));

	[TestMethod]
	[DataRow(9, 2)]
	[DataRow(12, 2)]
	public void Resolve_ForwardPastTheLastBookmark_WrapsToTheFirst(int current, int expected) =>
		Assert.AreEqual(expected, AdjacentBookmarkLineResolver.Resolve(s_lines, current, searchForward: true));

	[TestMethod]
	[DataRow(10, 9)]
	[DataRow(9, 5)]
	[DataRow(6, 5)]
	[DataRow(5, 2)]
	public void Resolve_Backward_ReturnsThePreviousBookmarkedLine(int current, int expected) =>
		Assert.AreEqual(expected, AdjacentBookmarkLineResolver.Resolve(s_lines, current, searchForward: false));

	[TestMethod]
	[DataRow(2, 9)]
	[DataRow(1, 9)]
	public void Resolve_BackwardPastTheFirstBookmark_WrapsToTheLast(int current, int expected) =>
		Assert.AreEqual(expected, AdjacentBookmarkLineResolver.Resolve(s_lines, current, searchForward: false));

	[TestMethod]
	public void Resolve_SoleBookmarkOnTheCurrentLine_ReturnsThatLine()
	{
		Assert.AreEqual(4, AdjacentBookmarkLineResolver.Resolve([4], currentLineNumber: 4, searchForward: true));
		Assert.AreEqual(4, AdjacentBookmarkLineResolver.Resolve([4], currentLineNumber: 4, searchForward: false));
	}
}
