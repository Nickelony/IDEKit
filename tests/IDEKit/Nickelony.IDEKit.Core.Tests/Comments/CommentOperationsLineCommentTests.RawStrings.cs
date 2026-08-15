using static Nickelony.IDEKit.Core.Tests.CommentTestSupport;

namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class CommentOperationsLineCommentTests
{
	[TestMethod]
	public void FindCommentStart_TripleQuotedString_IgnoresDelimiter()
	{
		// The // inside the """ raw string is content, not a comment.
		int result = FindCommentStart("s = \"\"\"a//b\"\"\"", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_TripleQuotedStringThenRealComment_FindsComment()
	{
		int result = FindCommentStart("\"\"\"a\"\"\" // real", s_cStyleSyntax);

		// The comment (including leading whitespace) starts at index 7 (the space before '//').
		Assert.AreEqual(7, result);
	}

	[TestMethod]
	public void FindCommentStart_TripleQuotedStringMultiline_IgnoresDelimiter()
	{
		// The // on the second line is inside the multi-line raw string.
		int result = FindCommentStart("\"\"\"a\n// b\n\"\"\"", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_TripleQuotedStringMultilineWithCrLf_IgnoresDelimiter()
	{
		// CRLF stays string content in a raw string, so the // on the second line is still inside it.
		int result = FindCommentStart("\"\"\"a\r\n// b\r\n\"\"\"", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_FourQuoteRawString_TripleSequenceInsideIsContent()
	{
		// The // inside a four-quote raw string is content; only a four-quote run closes.
		int result = FindCommentStart("\"\"\"\"a\"\"\"b//c\"\"\"\"", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_FourQuoteRawString_CommentAfterClose_FindsComment()
	{
		int result = FindCommentStart("\"\"\"\"a\"\"\"b\"\"\"\" // real", s_cStyleSyntax);

		// The comment (including leading whitespace) starts at index 13 (the space before '//').
		Assert.AreEqual(13, result);
	}

	[TestMethod]
	public void FindCommentStart_SixQuoteClosingRun_RescansSurplusQuotesAsCode()
	{
		// The closer is the opener's three-quote length; the three surplus quotes are re-scanned as
		// code, where they open a new raw string that hides the //. A model that treated the surplus
		// quotes as string content would report a comment here.
		int result = FindCommentStart("\"\"\"a\"\"\"\"\"\"//b\"\"\"", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_FourQuoteClosingRun_OddSurplusHidesComment()
	{
		// The closer is the opener's three-quote length; the single surplus quote is re-scanned as
		// code, where it opens a new double-quoted string that hides the //. This is the documented
		// recovery for the compiler-invalid over-long closer.
		int result = FindCommentStart("\"\"\"a\"\"\"\" // note", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_FiveQuoteClosingRun_EvenSurplusRealignsAndFindsComment()
	{
		// The two surplus quotes are re-scanned as code and pair up as an empty string, so the //
		// after them is a real comment. The comment (including leading whitespace) starts at
		// index 9 (the space before '//').
		int result = FindCommentStart("\"\"\"a\"\"\"\"\" // real", s_cStyleSyntax);

		Assert.AreEqual(9, result);
	}

	[TestMethod]
	public void FindCommentStart_TripleSingleQuotedString_IgnoresHash()
	{
		// Python docstring: the # inside ''' is content, not a comment.
		int result = FindCommentStart("'''a#b'''", new CommentSyntax("#", null, StringLiteralStyle.TripleSingleQuoted));

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_TripleSingleQuotedStringThenRealComment_FindsComment()
	{
		int result = FindCommentStart("'''a''' # note", new CommentSyntax("#", null, StringLiteralStyle.TripleSingleQuoted));

		// The comment (including leading whitespace) starts at index 7 (the space before '#').
		Assert.AreEqual(7, result);
	}

	[TestMethod]
	public void FindCommentStart_TripleSingleQuotedPrecededByBackslash_IsNotAnOpener()
	{
		// A quote run directly preceded by a backslash is not recognized as an opener (see
		// StringLiteralStyle), so the hash inside the text is a line comment after all.
		int result = FindCommentStart("\\'''a # b'''", new CommentSyntax("#", null, StringLiteralStyle.TripleSingleQuoted));

		// The comment (including leading whitespace) starts at index 5 (the space before '#').
		Assert.AreEqual(5, result);
	}

	[TestMethod]
	public void FindCommentStart_TripleDoubleQuotedPrecededByBackslash_IsNotAnOpener()
	{
		int result = FindCommentStart("\\\"\"\"a // b\"\"\"", s_cStyleSyntax);

		// The comment (including leading whitespace) starts at index 5 (the space before '//').
		Assert.AreEqual(5, result);
	}

	[TestMethod]
	public void FindCommentStart_TripleSingleQuotedSixQuotes_ClosesAtTheFirstThree()
	{
		// '''''' is an empty Python string (opener + closer), so the # after it starts a real comment.
		int result = FindCommentStart("'''''' # x", new CommentSyntax("#", null, StringLiteralStyle.TripleSingleQuoted));

		// The comment (including leading whitespace) starts at index 6 (the space before '#').
		Assert.AreEqual(6, result);
	}

	[TestMethod]
	public void FindCommentStart_FourQuoteRawString_IgnoresDelimiterInsideString()
	{
		// C# raw strings with a four-quote delimiter: the // inside the string is content.
		int result = FindCommentStart("\"\"\"\"a//b\"\"\"\" // real", s_cStyleSyntax);

		// The comment (including leading whitespace) starts at index 12 (the space before '//').
		Assert.AreEqual(12, result);
	}
}
