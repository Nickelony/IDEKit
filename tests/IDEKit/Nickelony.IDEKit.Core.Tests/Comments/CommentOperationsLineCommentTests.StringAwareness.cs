using static Nickelony.IDEKit.Core.Tests.CommentTestSupport;

namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class CommentOperationsLineCommentTests
{
	[TestMethod]
	[DataRow("\"a\\\\\" // c", StringLiteralStyle.DoubleQuoted | StringLiteralStyle.TripleDoubleQuoted, DisplayName = "DoubleQuoted")]
	[DataRow("`a\\\\` // c", StringLiteralStyle.BacktickQuoted, DisplayName = "BacktickQuoted")]
	public void FindCommentStart_EscapedBackslashBeforeClosingQuote_FindsTheComment(string text, StringLiteralStyle stringStyle)
	{
		// '\\' is an escaped backslash, so the closing quote still ends the string and the delimiter
		// after it starts a real comment.
		var syntax = new CommentSyntax("//", null, stringStyle);

		Assert.AreEqual(5, FindCommentStart(text, syntax));
	}

	[TestMethod]
	public void FindCommentStart_SlashSlashInsideQuotedString_ReturnsNegative()
	{
		int result = FindCommentStart(UrlLine, s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_SlashSlashInsideQuotesThenRealComment_FindsRealComment()
	{
		const string input = UrlLine + " // note";
		int result = FindCommentStart(input, s_cStyleSyntax);

		// The comment span starts at the space before the real delimiter.
		Assert.AreEqual(UrlLine.Length, result);
	}

	[TestMethod]
	public void FindCommentStart_WithoutStringAwareness_FindsHashInsideQuotes()
	{
		// Without string awareness, a `#` inside quotes starts a comment.
		int result = FindCommentStart("url = \"x#y\"", new CommentSyntax("#", null, StringLiteralStyle.None));

		Assert.AreEqual(8, result);
	}

	[TestMethod]
	[DataRow("url = \"http://x/#y\"", DisplayName = "DoubleQuoted")]
	[DataRow("url = 'http://x/#y'", DisplayName = "SingleQuoted")]
	public void FindCommentStart_PythonHashInsideQuotes_ReturnsNegative(string text)
	{
		// Both quote styles hide the hash, so the URL stays string content.
		Assert.AreEqual(-1, FindCommentStart(text, CommentSyntaxFixtures.HashLine));
	}

	[TestMethod]
	public void FindCommentStart_PythonHashAfterString_FindsCommentStart()
	{
		int result = FindCommentStart("url = 'x' # note", CommentSyntaxFixtures.HashLine);

		// The comment (including leading whitespace) starts at index 9 (the space before '#').
		Assert.AreEqual(9, result);
	}

	[TestMethod]
	public void FindCommentStart_SingleQuotesWithDoubleQuotedStyle_TreatsDelimiterAsComment()
	{
		// DoubleQuoted tracks only double quotes, so // inside single quotes is treated as a comment.
		int result = FindCommentStart("s = 'a//b'", new CommentSyntax("//", null, StringLiteralStyle.DoubleQuoted));

		Assert.AreEqual(6, result);
	}

	[TestMethod]
	public void FindCommentStart_SingleQuotesWithCombinedStyle_ReturnsNegative()
	{
		int result = FindCommentStart("s = 'a//b'", CommentSyntaxFixtures.CStyleLineDoubleSingle);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_InterleavedQuotes_TracksOuterString()
	{
		// A single quote inside a double-quoted string must not open a new string.
		int result = FindCommentStart("msg = \"it's a // test\" // real", CommentSyntaxFixtures.CStyleLineDoubleSingle);

		// The comment (including leading whitespace) starts at index 22 (the space before the real '//').
		Assert.AreEqual(22, result);
	}

	[TestMethod]
	public void FindCommentStart_BacktickStringWithFlag_ReturnsNegative()
	{
		int result = FindCommentStart("s = `a//b`", CommentSyntaxFixtures.CStyleLineBacktick);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_BacktickStringWithoutFlag_FindsDelimiter()
	{
		// Without the BacktickQuoted flag, // inside backticks is treated as a comment.
		int result = FindCommentStart("s = `a//b`", CommentSyntaxFixtures.CStyleLineDoubleSingle);

		Assert.AreEqual(6, result);
	}

	[TestMethod]
	public void FindCommentStart_QuoteStateResetsAtCarriageReturn()
	{
		// The unterminated double-quoted string ends at the lone CR, so the semicolon is code.
		var doubleQuotedSyntax = new CommentSyntax(";", null, StringLiteralStyle.DoubleQuoted);

		int result = FindCommentStart("\"a\r; c", doubleQuotedSyntax);

		// The comment span starts at the CR that precedes the delimiter.
		Assert.AreEqual(2, result);
	}
}
