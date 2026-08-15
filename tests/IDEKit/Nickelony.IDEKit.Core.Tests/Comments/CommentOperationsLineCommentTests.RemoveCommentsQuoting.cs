namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class CommentOperationsLineCommentTests
{
	[TestMethod]
	public void RemoveComments_SlashSlashInsideQuotedString_PreservesUrl()
	{
		string result = CommentOperations.RemoveComments(UrlLine, s_cStyleSyntax);

		Assert.AreEqual(UrlLine, result);
	}

	[TestMethod]
	public void RemoveComments_UrlThenRealComment_RemovesOnlyComment()
	{
		string result = CommentOperations.RemoveComments(UrlLine + " // note", s_cStyleSyntax);

		Assert.AreEqual(UrlLine, result);
	}

	[TestMethod]
	public void RemoveComments_EscapedQuote_PreservesSlashSlashInsideString()
	{
		// The \" is an escaped quote, so the // before the closing quote stays inside the string.
		string result = CommentOperations.RemoveComments("\"path\": \"a\\\"b//c\" // real", s_cStyleSyntax);

		Assert.AreEqual("\"path\": \"a\\\"b//c\"", result);
	}

	[TestMethod]
	public void RemoveComments_QuotedSlashSlashAcrossLines_ResetsQuoteStatePerLine()
	{
		string input = "\"path\": \"a//b\"\n\"title\": \"Caves\" // only this is a comment";

		string result = CommentOperations.RemoveComments(input, s_cStyleSyntax);

		Assert.AreEqual("\"path\": \"a//b\"\n\"title\": \"Caves\"", result);
	}

	[TestMethod]
	public void RemoveComments_PythonHashInString_PreservesStringAndRemovesComment()
	{
		string result = CommentOperations.RemoveComments("url = 'http://x/#y' # note", CommentSyntaxFixtures.HashLine);

		Assert.AreEqual("url = 'http://x/#y'", result);
	}

	[TestMethod]
	public void RemoveComments_EscapedSingleQuote_KeepsStringOpen()
	{
		// The \' is an escaped quote, so the # after it stays inside the string.
		string result = CommentOperations.RemoveComments("s = 'a\\'b#c' # real", CommentSyntaxFixtures.HashLine);

		Assert.AreEqual("s = 'a\\'b#c'", result);
	}

	[TestMethod]
	public void RemoveComments_BacktickTemplate_PreservesTemplateAndRemovesComment()
	{
		string result = CommentOperations.RemoveComments(
			"let s = `a//b`; // real",
			CommentSyntaxFixtures.CStyleLineBacktick);

		Assert.AreEqual("let s = `a//b`;", result);
	}

	[TestMethod]
	public void RemoveComments_EscapedBacktick_KeepsStringOpen()
	{
		// The \` is an escaped backtick, so the // after it stays inside the template.
		string result = CommentOperations.RemoveComments(
			"let s = `a\\`b//c` // real",
			CommentSyntaxFixtures.CStyleLineBacktick);

		Assert.AreEqual("let s = `a\\`b//c`", result);
	}

	[TestMethod]
	public void RemoveComments_FiveQuoteClosingRun_RemovesRealComment()
	{
		string result = CommentOperations.RemoveComments("\"\"\"a\"\"\"\"\" // real", s_cStyleSyntax);

		Assert.AreEqual("\"\"\"a\"\"\"\"\"", result);
	}

	[TestMethod]
	public void RemoveComments_TripleSingleQuotedFourQuoteOpener_TreatsSurplusQuoteAsContent()
	{
		// Python closes a ''' string at the first three quotes, so the fourth opening quote is
		// content and the trailing comment is a real comment.
		string result = CommentOperations.RemoveComments("''''a''' # note", new CommentSyntax("#", null, StringLiteralStyle.TripleSingleQuoted));

		Assert.AreEqual("''''a'''", result);
	}

	[TestMethod]
	public void RemoveComments_EscapedFirstQuote_DoesNotOpenRawString()
	{
		// A backslash-escaped quote cannot open a raw string, so the // behind the quotes is a real
		// comment. (The prefix is invalid code; the scanner must still recover.)
		string result = CommentOperations.RemoveComments("\\\"\"\" // x", s_cStyleSyntax);

		Assert.AreEqual("\\\"\"\"", result);
	}

	[TestMethod]
	public void RemoveComments_TripleQuotedMultiline_PreservesStringAndRemovesRealComment()
	{
		string result = CommentOperations.RemoveComments("\"\"\"\n// not a comment\n\"\"\" // real", s_cStyleSyntax);

		Assert.AreEqual("\"\"\"\n// not a comment\n\"\"\"", result);
	}

	[TestMethod]
	public void RemoveComments_BacktickTemplateMultiline_PreservesCommentInsideTemplate()
	{
		// The // on the second line is inside the multi-line backtick template.
		string result = CommentOperations.RemoveComments(
			"let s = `a\n// not a comment\nb`; // real",
			CommentSyntaxFixtures.CStyleLineBacktick);

		Assert.AreEqual("let s = `a\n// not a comment\nb`;", result);
	}
}
