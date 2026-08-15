using static Nickelony.IDEKit.Core.Tests.CommentTestSupport;

namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class CommentOperationsLongBracketTests
{
	private static readonly CommentSyntax s_luaSyntax = CommentSyntaxFixtures.LuaLongBracketLine;

	[TestMethod]
	public void FindCommentStart_NoLongBrackets_DetectsLineComment()
	{
		int result = FindCommentStart("local x = 1 -- comment", s_luaSyntax);

		// The comment span includes the leading whitespace before '--'.
		Assert.AreEqual(11, result);
	}

	[TestMethod]
	public void FindCommentStart_CommentDelimiterInsideDoubleBracket_IsNotAComment()
	{
		// The -- inside [[ ]] is string content, not a comment.
		int result = FindCommentStart("local s = [[a -- b]] -- after", s_luaSyntax);

		// The real comment starts at the "-- after" suffix.
		Assert.AreEqual(20, result);
	}

	[TestMethod]
	public void FindCommentStart_CommentDelimiterInsideEqualsBracket_IsNotAComment()
	{
		int result = FindCommentStart("local s = [=[a -- b]=] -- after", s_luaSyntax);

		Assert.AreEqual(22, result);
	}

	[TestMethod]
	public void FindCommentStart_UnclosedLongBracket_ConsumesRestOfText()
	{
		// An unterminated long bracket runs to the end of the text, so no comment is found.
		int result = FindCommentStart("local s = [[unterminated -- not a comment", s_luaSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_LongBracketWithMismatchedCloser_ReturnsComment()
	{
		// The input contains a mismatched closer, then a matching closer and a real comment.
		int result = FindCommentStart("local s = [=[a ]] b]=] -- after", s_luaSyntax);

		// The matching closer ends at index 21, so the comment span starts at the space at index 22.
		Assert.AreEqual(22, result);
	}

	[TestMethod]
	public void FindCommentStart_MultiLineLongString_SpansLines()
	{
		int result = FindCommentStart("local s = [[line1\n-- not a comment\nline2]] -- after", s_luaSyntax);

		// The long bracket ends at index 41, so the comment span starts at the space at index 42.
		Assert.AreEqual(42, result);
		Assert.IsTrue(FindCommentStart("local s = [[line1\nline2]]", s_luaSyntax) == -1);
	}

	[TestMethod]
	public void FindCommentStart_CrLfInsideLongString_SpansLines()
	{
		// CRLF stays string content, so the closer after it still ends the long bracket and the
		// comment span starts at the whitespace before the delimiter.
		int result = FindCommentStart("local s = [[line1\r\nline2]] -- after", s_luaSyntax);

		Assert.AreEqual(26, result);
		Assert.IsTrue(FindCommentStart("local s = [[line1\r\nline2]]", s_luaSyntax) == -1);
	}

	[TestMethod]
	public void FindCommentStart_WithoutLongBracketFlag_TreatsBracketsAsCode()
	{
		// Without the LongBracketQuoted flag, the -- inside the brackets is a comment.
		var plainSyntax = new CommentSyntax("--", null, StringLiteralStyle.None);
		int result = FindCommentStart("local s = [[a -- b]]", plainSyntax);

		// The delimiter sits at index 14 and the span absorbs the preceding space at index 13.
		Assert.AreEqual(13, result);
	}

	[TestMethod]
	public void RemoveComments_CommentAfterLongString_KeepsString()
	{
		string result = CommentOperations.RemoveComments("local s = [[a -- b]] -- after", s_luaSyntax);

		Assert.AreEqual("local s = [[a -- b]]", result);
	}

	[TestMethod]
	public void RemoveComments_EqualsBracketString_KeepsString()
	{
		string result = CommentOperations.RemoveComments("local s = [=[a -- b]=] -- after", s_luaSyntax);

		Assert.AreEqual("local s = [=[a -- b]=]", result);
	}

	[TestMethod]
	public void MaskComments_CommentAfterLongString_MasksCommentNotString()
	{
		string result = CommentOperations.MaskComments("local s = [[a -- b]] -- after", s_luaSyntax);

		Assert.AreEqual("local s = [[a -- b]]         ", result);
	}
}
