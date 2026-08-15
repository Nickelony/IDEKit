namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class CommentOperationsCodeEndTests
{
	private static readonly CommentSyntax s_lineOnlySyntax = CommentSyntaxFixtures.CStyleLine;
	private static readonly CommentSyntax s_blockAwareSyntax = CommentSyntaxFixtures.CStyleDoubleQuoted;

	[TestMethod]
	public void GetCodeEnd_IgnoresTrailingLineComment()
	{
		// The result is one past the last non-whitespace code character ('e' at index 3).
		Assert.AreEqual(4, CommentOperations.GetCodeEnd("code // note", s_lineOnlySyntax));
	}

	[TestMethod]
	public void GetCodeEnd_IgnoresTrailingBlockComment()
	{
		Assert.AreEqual(4, CommentOperations.GetCodeEnd("code /* note */", s_blockAwareSyntax));
	}

	[TestMethod]
	[DataRow("{", "}", "x { c }", 1, DisplayName = "BraceBlockComment")]
	[DataRow("[", "]", "x [c]", 1, DisplayName = "BracketBlockComment")]
	[DataRow("}{", "{}", "x }{ c {}", 1, DisplayName = "AdjacentSingleCharacterDelimiters")]
	public void GetCodeEnd_BlockCommentWithSingleCharacterCloser_DoesNotCountTheCloserAsCode(string opener, string closer, string text, int expected)
	{
		// A one-character closer is a delimiter character, so it is not code even though no
		// delimiter-remainder move follows the move that consumed it.
		var syntax = new CommentSyntax(null, new BlockCommentSyntax(opener, closer), StringLiteralStyle.None);

		Assert.AreEqual(expected, CommentOperations.GetCodeEnd(text, syntax));
	}

	[TestMethod]
	public void GetCodeEnd_IncludesMarkerAfterBlockComment()
	{
		// The trailing marker is code, so the result runs through its last character.
		Assert.AreEqual(17, CommentOperations.GetCodeEnd("value /* c */ ...", s_blockAwareSyntax));
	}

	[TestMethod]
	public void GetCodeEnd_StringCloseQuoteCountsAsCode()
	{
		// The closing quote is not inside the string after it is consumed, so it counts as code.
		Assert.AreEqual(7, CommentOperations.GetCodeEnd("x = \"a\"", s_lineOnlySyntax));
	}

	[TestMethod]
	public void GetCodeEnd_BacktickCloseQuoteCountsAsCode()
	{
		// A backtick string can span lines but is closed by a single character, which counts as code.
		var syntax = new CommentSyntax("//", null, StringLiteralStyle.BacktickQuoted);

		Assert.AreEqual(7, CommentOperations.GetCodeEnd("x = `a`", syntax));
	}

	[TestMethod]
	public void GetCodeEnd_RawStringIsSkippedWithItsDelimiters()
	{
		// The whole raw string, including its delimiters, is skipped, so the result stops at '='.
		Assert.AreEqual(3, CommentOperations.GetCodeEnd("x = \"\"\"a\"\"\"", s_lineOnlySyntax));
	}

	[TestMethod]
	public void GetCodeEnd_SingleQuotedRawStringIsSkippedWithItsDelimiters()
	{
		// The same rule applies to Python-style docstring delimiters.
		var syntax = new CommentSyntax("//", null, StringLiteralStyle.TripleSingleQuoted);

		Assert.AreEqual(3, CommentOperations.GetCodeEnd("x = '''a'''", syntax));
	}

	[TestMethod]
	public void GetCodeEnd_LongBracketStringIsSkippedWithItsDelimiters()
	{
		// The whole Lua-style long-bracket string, including both bracket delimiters, is skipped.
		var syntax = new CommentSyntax("//", null, StringLiteralStyle.LongBracketQuoted);

		Assert.AreEqual(3, CommentOperations.GetCodeEnd("x = [==[a]==]", syntax));
	}

	[TestMethod]
	public void GetCodeEnd_CloserOverlapsOpenerCharacters_ReportsNoCode()
	{
		Assert.AreEqual(0, CommentOperations.GetCodeEnd("/*/ x", s_blockAwareSyntax));
	}

	[TestMethod]
	public void GetCodeEnd_EmptyText_ReturnsZero()
	{
		Assert.AreEqual(0, CommentOperations.GetCodeEnd(string.Empty, s_lineOnlySyntax));
	}

	[TestMethod]
	[DataRow("code\r\n// note", 4, DisplayName = "CrLfAfterCode")]
	[DataRow("code\r// note", 4, DisplayName = "LoneCrAfterCode")]
	[DataRow("// note", 0, DisplayName = "CommentOnlyLine")]
	[DataRow("\r\n", 0, DisplayName = "TerminatorsOnly")]
	public void GetCodeEnd_WithLineTerminatorsAndCommentOnlyLine_IgnoresTerminators(string text, int expected)
	{
		// CRLF and lone CR after code are whitespace, and the comment-only line contributes no code.
		Assert.AreEqual(expected, CommentOperations.GetCodeEnd(text, s_lineOnlySyntax));
	}
}
