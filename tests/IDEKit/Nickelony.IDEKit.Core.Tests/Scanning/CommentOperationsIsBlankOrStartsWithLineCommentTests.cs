namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class CommentOperationsIsBlankOrStartsWithLineCommentTests
{
	private static readonly CommentSyntax s_semicolonSyntax = CommentSyntaxTestHelpers.SemicolonLine;
	private static readonly CommentSyntax s_cStyleSyntax = CommentSyntaxTestHelpers.CStyleLine;

	[TestMethod]
	public void IsBlankOrStartsWithLineComment_NullOrWhitespace_ReturnsTrue()
	{
		Assert.IsTrue(CommentOperations.IsBlankOrStartsWithLineComment(null, s_semicolonSyntax));
		Assert.IsTrue(CommentOperations.IsBlankOrStartsWithLineComment(string.Empty, s_semicolonSyntax));
		Assert.IsTrue(CommentOperations.IsBlankOrStartsWithLineComment("   \t ", s_semicolonSyntax));
	}

	[TestMethod]
	public void IsBlankOrStartsWithLineComment_CommentOnlyLine_ReturnsTrue()
	{
		Assert.IsTrue(CommentOperations.IsBlankOrStartsWithLineComment("; comment", s_semicolonSyntax));
		Assert.IsTrue(CommentOperations.IsBlankOrStartsWithLineComment("   ; comment", s_semicolonSyntax));
	}

	[TestMethod]
	public void IsBlankOrStartsWithLineComment_CodeLine_ReturnsFalse()
	{
		Assert.IsFalse(CommentOperations.IsBlankOrStartsWithLineComment("Legend= 42", s_semicolonSyntax));
		Assert.IsFalse(CommentOperations.IsBlankOrStartsWithLineComment("var x = 1", s_cStyleSyntax));
	}

	[TestMethod]
	public void IsBlankOrStartsWithLineComment_LineCommentText_ReturnsTrue() =>
		Assert.IsTrue(CommentOperations.IsBlankOrStartsWithLineComment("// not leading", s_cStyleSyntax));

	[TestMethod]
	public void IsBlankOrStartsWithLineComment_NoLineCommentDelimiter_OnlyChecksWhitespace()
	{
		// Block comments are not recognized, so a block-comment-only line is not reported as empty.
		var syntax = new CommentSyntax(null, new BlockCommentSyntax("/*", "*/"), StringLiteralStyles.None);

		Assert.IsTrue(CommentOperations.IsBlankOrStartsWithLineComment("   ", syntax));
		Assert.IsFalse(CommentOperations.IsBlankOrStartsWithLineComment("/* block */", syntax));
	}

	[TestMethod]
	public void IsBlankOrStartsWithLineComment_NonLayoutWhitespace_IsNeitherBlankNorComment()
	{
		var syntax = new CommentSyntax(";", null, StringLiteralStyles.None);

		// The unified alphabet is space/tab only, so a non-breaking space is content: the line is
		// neither blank nor a comment, matching the line-comment transforms.
		Assert.IsFalse(CommentOperations.IsBlankOrStartsWithLineComment(" ", syntax));
		Assert.IsFalse(CommentOperations.IsBlankOrStartsWithLineComment(" ; x", syntax));
		Assert.IsFalse(CommentOperations.IsBlankOrStartsWithLineComment(" x", syntax));
	}
}
