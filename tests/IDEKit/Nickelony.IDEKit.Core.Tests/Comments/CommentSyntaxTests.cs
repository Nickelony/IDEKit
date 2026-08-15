namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class CommentSyntaxTests
{
	[TestMethod]
	public void Constructor_ExposesTheSuppliedDelimitersAndStyles()
	{
		var blockComments = new BlockCommentSyntax("/*", "*/", allowNesting: true);

		var syntax = new CommentSyntax(
			"//",
			blockComments,
			StringLiteralStyle.DoubleQuoted | StringLiteralStyle.BacktickQuoted);

		Assert.AreEqual("//", syntax.LineCommentDelimiter);
		Assert.AreEqual(blockComments, syntax.BlockComments);
		Assert.AreEqual(StringLiteralStyle.DoubleQuoted | StringLiteralStyle.BacktickQuoted, syntax.StringStyle);
	}

	[TestMethod]
	public void Constructor_NullOrEmptyLineDelimiter_DisablesLineComments()
	{
		var nullDelimiter = new CommentSyntax(null, null, StringLiteralStyle.None);
		var emptyDelimiter = new CommentSyntax(string.Empty, null, StringLiteralStyle.None);

		Assert.IsNull(nullDelimiter.LineCommentDelimiter);
		Assert.IsNull(nullDelimiter.BlockComments);
		Assert.IsNull(emptyDelimiter.LineCommentDelimiter);
		Assert.IsNull(emptyDelimiter.BlockComments);
	}

	[TestMethod]
	public void Constructor_DefaultBlockPair_Throws()
	{
		// An uninitialized pair carries null delimiters; passing it would silently disable block
		// comments, so it is rejected instead of normalized away.
		Assert.ThrowsExactly<ArgumentException>(() =>
			new CommentSyntax("//", default(BlockCommentSyntax), StringLiteralStyle.None));
	}

	[TestMethod]
	public void Constructor_WhitespaceOnlyLineDelimiter_DisablesLineComments()
	{
		var spaceDelimiter = new CommentSyntax(" ", null, StringLiteralStyle.None);
		var tabDelimiter = new CommentSyntax("\t", null, StringLiteralStyle.None);

		Assert.IsNull(spaceDelimiter.LineCommentDelimiter);
		Assert.IsNull(tabDelimiter.LineCommentDelimiter);

		// With line-comment awareness disabled, a space cannot start a comment.
		Assert.IsNull(CommentOperations.FindComment("a b c", spaceDelimiter));
		Assert.AreEqual("a b c", CommentOperations.RemoveComments("a b c", spaceDelimiter));
	}
}
