namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class CommentOperationsBlockCommentTests
{
	// Comment syntax used by the tests.
	private static readonly CommentSyntax s_cStyleSyntax = CommentSyntaxFixtures.CStyle;
	private static readonly CommentSyntax s_luaSyntax = CommentSyntaxFixtures.Lua;

	[TestMethod]
	public void EnumerateComments_FirstBlockComment_LineWithComment_ReturnsOpenDelimiterIndex()
	{
		int result = FindFirstBlockCommentDelimiterStart("code /* comment */", s_cStyleSyntax);

		// The comment opener starts at index 5.
		Assert.AreEqual(5, result);
	}

	[TestMethod]
	public void EnumerateComments_FirstBlockComment_NoComment_ReturnsNegative()
	{
		int result = FindFirstBlockCommentDelimiterStart("code", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void EnumerateComments_FirstBlockComment_CommentOnly_ReturnsZero()
	{
		int result = FindFirstBlockCommentDelimiterStart("/* comment */", s_cStyleSyntax);

		Assert.AreEqual(0, result);
	}

	[TestMethod]
	public void EnumerateComments_FirstBlockComment_UnclosedComment_ReturnsOpenDelimiterIndex()
	{
		int result = FindFirstBlockCommentDelimiterStart("code /* comment", s_cStyleSyntax);

		Assert.AreEqual(5, result);
	}

	[TestMethod]
	public void EnumerateComments_FirstBlockComment_EmptyString_ReturnsNegative()
	{
		int result = FindFirstBlockCommentDelimiterStart(string.Empty, s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void EnumerateComments_FirstBlockComment_InsideQuotedString_IsIgnored()
	{
		// The configured string rules keep the `/*` inside the quoted string from being treated as an opener.
		int result = FindFirstBlockCommentDelimiterStart("\"code /* x */\"", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void EnumerateComments_FirstBlockComment_CommentAfterString_FindsComment()
	{
		int result = FindFirstBlockCommentDelimiterStart("\"code\"/* x */", s_cStyleSyntax);

		// The opener follows the closing quote at index 6.
		Assert.AreEqual(6, result);
	}

	[TestMethod]
	public void EnumerateComments_FirstBlockComment_LuaBlockDelimiter_WithoutStringAwareness_FindsDelimiter()
	{
		// Without long-bracket string awareness, `--[[ ... ]]` is treated as a block comment.
		int result = FindFirstBlockCommentDelimiterStart("'x --[[ c ]]", s_luaSyntax);

		Assert.AreEqual(3, result);
	}

	[TestMethod]
	public void EnumerateComments_FirstBlockComment_LuaBlockDelimiter_InSingleQuotedString_ReturnsNegative()
	{
		int result = FindFirstBlockCommentDelimiterStart("'x --[[ c ]]", new CommentSyntax("--", new BlockCommentSyntax("--[[", "]]"), StringLiteralStyle.SingleQuoted));

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void EnumerateComments_FirstBlockComment_OpenDelimiterInsideLineComment_ReturnsNegative()
	{
		// The `/*` is inside a `//` line comment, so it is not a comment opener.
		int result = FindFirstBlockCommentDelimiterStart("// not /* a comment */", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void EnumerateComments_FirstBlockComment_BlockCommentAfterLineCommentOnNextLine_FindsComment()
	{
		// The line comment ends at the line terminator; the block comment on the next line is real.
		int result = FindFirstBlockCommentDelimiterStart("code // note\n/* real */", s_cStyleSyntax);

		Assert.AreEqual(13, result);
	}

	[TestMethod]
	public void EnumerateComments_FirstBlockComment_LineCommentPreventsBlockDetection()
	{
		// The `/*` is inside the configured `#` line comment, so it is not an opener.
		int result = FindFirstBlockCommentDelimiterStart("# note /* x */", new CommentSyntax("#", new BlockCommentSyntax("/*", "*/"), StringLiteralStyle.None));

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void EnumerateComments_FirstBlockComment_HashIsNotLineDelimiter()
	{
		// The configured `//` line delimiter does not recognize `#`, so the opener is found.
		int result = FindFirstBlockCommentDelimiterStart("# note /* x */", s_cStyleSyntax);

		Assert.AreEqual(7, result);
	}

	[TestMethod]
	public void EnumerateComments_FirstBlockComment_TripleQuotedString_IgnoresOpener()
	{
		// The /* inside the """ raw string is content, not a comment opener.
		int result = FindFirstBlockCommentDelimiterStart("\"\"\"a/* b */\"\"\"", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void EnumerateComments_FirstBlockComment_TripleQuotedStringMultiline_IgnoresOpener()
	{
		// The opener on the second line is inside the multi-line raw string.
		int result = FindFirstBlockCommentDelimiterStart("\"\"\"a\n/* b */\n\"\"\"", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void EnumerateComments_FirstBlockComment_FourQuoteRawString_OpenerInsideIsContent()
	{
		// The /* inside a four-quote raw string is content; only a four-quote run closes.
		int result = FindFirstBlockCommentDelimiterStart("\"\"\"\"a/* b */\"\"\"\"", s_cStyleSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindComment_CloserOverlapsOpenerCharacters_DoesNotCloseEarly()
	{
		// The '*' of the opener must not pair with the following '/' to form a closer.
		CommentSpan? comment = CommentOperations.FindComment("/*/*/", s_cStyleSyntax);

		Assert.IsTrue(comment.HasValue);
		Assert.AreEqual(0, comment.Value.StartOffset);
		Assert.AreEqual(5, comment.Value.EndOffset);
	}

	[TestMethod]
	public void RemoveBlockComments_RemovesCommentSpan()
	{
		string result = CommentOperations.RemoveBlockComments("a + /* c */ b", s_cStyleSyntax);

		// Whitespace around the comment is preserved.
		Assert.AreEqual("a +  b", result);
	}

	[TestMethod]
	public void RemoveBlockComments_NoComment_ReturnsOriginal()
	{
		string result = CommentOperations.RemoveBlockComments("a + b", s_cStyleSyntax);

		Assert.AreEqual("a + b", result);
	}

	[TestMethod]
	public void RemoveBlockComments_CommentOnly_ReturnsEmpty()
	{
		string result = CommentOperations.RemoveBlockComments("/* comment */", s_cStyleSyntax);

		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	public void RemoveBlockComments_EmptyString_ReturnsEmpty()
	{
		string result = CommentOperations.RemoveBlockComments(string.Empty, s_cStyleSyntax);

		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	public void RemoveBlockComments_UnclosedComment_RemovesToEnd()
	{
		string result = CommentOperations.RemoveBlockComments("a /* c", s_cStyleSyntax);

		Assert.AreEqual("a ", result);
	}

	[TestMethod]
	public void RemoveBlockComments_MultipleComments_RemovesAll()
	{
		string result = CommentOperations.RemoveBlockComments("/* a */ x /* b */", s_cStyleSyntax);

		Assert.AreEqual(" x ", result);
	}

	[TestMethod]
	public void RemoveBlockComments_AdjacentComments_RemovesBoth()
	{
		// Both adjacent block comments should be removed.
		string result = CommentOperations.RemoveBlockComments("a /* one *//* two */ b", s_cStyleSyntax);

		Assert.AreEqual("a  b", result);
	}

	[TestMethod]
	public void RemoveBlockComments_MultiLineComment_RemovesAcrossLines()
	{
		string result = CommentOperations.RemoveBlockComments("/* a\nb */ x", s_cStyleSyntax);

		Assert.AreEqual(" x", result);
	}

	[TestMethod]
	public void RemoveBlockComments_CommentInsideString_IsPreserved()
	{
		// The `/* ... */` inside the string is not a comment; the real one is removed.
		string result = CommentOperations.RemoveBlockComments("\"a /* x */\"/* real */", s_cStyleSyntax);

		Assert.AreEqual("\"a /* x */\"", result);
	}

	[TestMethod]
	public void RemoveBlockComments_OpenerInsideLineComment_IsPreserved()
	{
		// The `/* ... */` is inside a `//` line comment, so it is not removed.
		string result = CommentOperations.RemoveBlockComments("code // note /* x */", s_cStyleSyntax);

		Assert.AreEqual("code // note /* x */", result);
	}

	[TestMethod]
	public void RemoveBlockComments_BlockCommentAfterLineCommentOnNextLine_Removed()
	{
		string result = CommentOperations.RemoveBlockComments("code // note\n/* real */", s_cStyleSyntax);

		Assert.AreEqual("code // note\n", result);
	}

	[TestMethod]
	public void RemoveBlockComments_NestedNotAllowed_ClosesAtFirstCloser()
	{
		string result = CommentOperations.RemoveBlockComments("/* a /* b */ c", s_cStyleSyntax);

		Assert.AreEqual(" c", result);
	}

	[TestMethod]
	public void RemoveBlockComments_NestedAllowed_ClosesAtFinalCloser()
	{
		string result = CommentOperations.RemoveBlockComments("/* a /* b */ c */", CommentSyntaxFixtures.CStyleNested);

		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	public void RemoveBlockComments_NestedWithCrLf_ClosesAtFinalCloser()
	{
		string result = CommentOperations.RemoveBlockComments(
			"a /* one\r\n/* two */\r\nthree */ b",
			CommentSyntaxFixtures.CStyleNested);

		Assert.AreEqual("a  b", result);
	}

	[TestMethod]
	public void RemoveBlockComments_NestedAllowed_PreservesOuterComment()
	{
		string result = CommentOperations.RemoveBlockComments("x /* a /* b */ c */ y", CommentSyntaxFixtures.CStyleNested);

		Assert.AreEqual("x  y", result);
	}

	[TestMethod]
	public void RemoveBlockComments_NestedAllowed_AdjacentClosers_KeepOuterOpenOnShortRun()
	{
		// Two closers close both levels; the third ']' is content, so the outer comment stays open
		// through the end of the text.
		string result = CommentOperations.RemoveBlockComments(
			"a --[[ x --[[ y ]]] b",
			CommentSyntaxFixtures.LuaNested);

		Assert.AreEqual("a ", result);
	}

	[TestMethod]
	public void RemoveBlockComments_NestedAllowed_AdjacentClosers_CloseBothLevels()
	{
		// Four closers close the nested and the outer comment; the text after them stays.
		string result = CommentOperations.RemoveBlockComments(
			"a --[[ x --[[ y ]]]] b",
			CommentSyntaxFixtures.LuaNested);

		Assert.AreEqual("a  b", result);
	}

	[TestMethod]
	public void RemoveBlockComments_NestedAllowed_AdjacentCStyleClosers_CloseBothLevels()
	{
		string result = CommentOperations.RemoveBlockComments(
			"/* a /* b */*/ c",
			CommentSyntaxFixtures.CStyleNested);

		Assert.AreEqual(" c", result);
	}

	[TestMethod]
	public void RemoveBlockComments_TripleQuotedString_PreservesInsideComment()
	{
		string result = CommentOperations.RemoveBlockComments("\"\"\"a/* b */\"\"\"/* real */", s_cStyleSyntax);

		Assert.AreEqual("\"\"\"a/* b */\"\"\"", result);
	}

	[TestMethod]
	public void RemoveBlockComments_TripleQuotedString_CommentDirectlyAfterCloser()
	{
		// A comment immediately after the raw string is still removed.
		string result = CommentOperations.RemoveBlockComments("\"\"\"a\"\"\"/* real */", s_cStyleSyntax);

		Assert.AreEqual("\"\"\"a\"\"\"", result);
	}

	[TestMethod]
	public void RemoveBlockComments_TripleQuotedMultiline_PreservesInsideComment()
	{
		string result = CommentOperations.RemoveBlockComments("\"\"\"\n/* c */\n\"\"\"/* real */", s_cStyleSyntax);

		Assert.AreEqual("\"\"\"\n/* c */\n\"\"\"", result);
	}

	[TestMethod]
	public void RemoveBlockComments_CloserOverlapsOpenerCharacters_RemovesWholeComment()
	{
		// The '//' inside the still-open block comment must not start a line comment.
		string result = CommentOperations.RemoveBlockComments("x/*/ y // z */ w", s_cStyleSyntax);

		Assert.AreEqual("x w", result);
	}

	[TestMethod]
	public void RemoveBlockComments_PascalDelimiters_CloserOverlapKeepsCommentOpen()
	{
		// '(*)' is an unclosed comment in Pascal; the ')' after '*' must not close it.
		var pascalSyntax = new CommentSyntax("//", new BlockCommentSyntax("(*", "*)"), StringLiteralStyle.None);

		string result = CommentOperations.RemoveBlockComments("(*) x", pascalSyntax);

		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	public void RemoveBlockComments_NestedCloserOverlapsOpener_KeepsOuterCommentOpen()
	{
		// The nested opener's '*' must not pair with the following '/' to close the nested comment.
		string result = CommentOperations.RemoveBlockComments("/* a /*/ b */ x", CommentSyntaxFixtures.CStyleNested);

		Assert.AreEqual(string.Empty, result);
	}

	private static int FindFirstBlockCommentDelimiterStart(string text, CommentSyntax syntax)
	{
		foreach (CommentSpan span in CommentOperations.EnumerateComments(text, syntax))
		{
			if (span.IsBlockComment)
				return span.DelimiterStart;
		}

		return -1;
	}

	[TestMethod]
	public void MaskBlockComments_ReplacesCommentWithSpaces_PreservesLength()
	{
		string result = CommentOperations.MaskBlockComments("a /* c */ b", s_cStyleSyntax);

		// "/* c */" is 7 characters, replaced by spaces; surrounding whitespace is kept.
		Assert.AreEqual("a" + new string(' ', 9) + "b", result);
		Assert.AreEqual(11, result.Length);
	}

	[TestMethod]
	public void MaskBlockComments_NoComment_ReturnsOriginal()
	{
		string result = CommentOperations.MaskBlockComments("a + b", s_cStyleSyntax);

		Assert.AreEqual("a + b", result);
	}

	[TestMethod]
	public void MaskBlockComments_EmptyString_ReturnsEmpty()
	{
		string result = CommentOperations.MaskBlockComments(string.Empty, s_cStyleSyntax);

		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	public void MaskBlockComments_MultiLine_PreservesNewlinesAndLength()
	{
		string result = CommentOperations.MaskBlockComments("/* a\nb */", s_cStyleSyntax);

		// The line terminator inside the comment is preserved so line numbers stay stable.
		Assert.AreEqual("    \n    ", result);
		Assert.AreEqual(9, result.Length);
	}

	[TestMethod]
	public void MaskBlockComments_MultiLineWithCrLf_PreservesLineEndings()
	{
		string input = "/* a\r\nb */";

		string result = CommentOperations.MaskBlockComments(input, s_cStyleSyntax);

		Assert.AreEqual("    \r\n    ", result);
		Assert.AreEqual(input.Length, result.Length);
	}

	[TestMethod]
	public void MaskBlockComments_LoneCrLineEnding_PreservesLineBreak()
	{
		// A lone CR inside a block comment stays a CR; replacing it with a space would merge the lines.
		string input = "/* a\rb */";

		string result = CommentOperations.MaskBlockComments(input, s_cStyleSyntax);

		Assert.AreEqual("    \r    ", result);
		Assert.AreEqual(input.Length, result.Length);
	}

	[TestMethod]
	public void MaskBlockComments_UnclosedComment_MasksToEnd()
	{
		string result = CommentOperations.MaskBlockComments("a /* c", s_cStyleSyntax);

		Assert.AreEqual("a" + new string(' ', 5), result);
		Assert.AreEqual(6, result.Length);
	}

	[TestMethod]
	public void MaskBlockComments_CommentInsideString_IsPreserved()
	{
		string result = CommentOperations.MaskBlockComments("\"a /* x */\"", s_cStyleSyntax);

		Assert.AreEqual("\"a /* x */\"", result);
	}

	[TestMethod]
	public void MaskBlockComments_OpenerInsideLineComment_IsPreserved()
	{
		string result = CommentOperations.MaskBlockComments("code // note /* x */", s_cStyleSyntax);

		Assert.AreEqual("code // note /* x */", result);
	}

	[TestMethod]
	public void MaskBlockComments_TripleQuotedString_PreservesInsideComment()
	{
		string result = CommentOperations.MaskBlockComments("\"\"\"a/* b */\"\"\"/* real */", s_cStyleSyntax);

		// Only the real comment is masked; the one inside the raw string is kept.
		Assert.AreEqual("\"\"\"a/* b */\"\"\"" + new string(' ', 10), result);
	}

}
