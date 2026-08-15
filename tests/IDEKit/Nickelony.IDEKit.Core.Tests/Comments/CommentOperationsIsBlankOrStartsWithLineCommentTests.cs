namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class CommentOperationsIsBlankOrStartsWithLineCommentTests
{
	private static readonly CommentSyntax s_semicolonSyntax = CommentSyntaxFixtures.SemicolonLine;
	private static readonly CommentSyntax s_cStyleSyntax = CommentSyntaxFixtures.CStyleLine;

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
	public void IsBlankOrStartsWithLineComment_LineCommentText_ReturnsTrue()
	{
		Assert.IsTrue(CommentOperations.IsBlankOrStartsWithLineComment("// not leading", s_cStyleSyntax));
	}

	[TestMethod]
	public void IsBlankOrStartsWithLineComment_NoLineCommentDelimiter_OnlyChecksWhitespace()
	{
		// Block comments are not recognized, so a block-comment-only line is not reported as empty.
		var syntax = new CommentSyntax(null, new BlockCommentSyntax("/*", "*/"), StringLiteralStyle.None);

		Assert.IsTrue(CommentOperations.IsBlankOrStartsWithLineComment("   ", syntax));
		Assert.IsFalse(CommentOperations.IsBlankOrStartsWithLineComment("/* block */", syntax));
	}
}
