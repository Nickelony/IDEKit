using static Nickelony.IDEKit.Core.Tests.CommentTestSupport;

namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed partial class CommentOperationsLineCommentTests
{
	[TestMethod]
	public void FindCommentStart_LineWithComment_ReturnsWhitespaceBeforeSemicolon()
	{
		int result = FindCommentStart("Legend= 42 ; comment", s_semicolonSyntax);

		// The comment (including leading whitespace) starts at index 10 (the space before ';').
		Assert.AreEqual(10, result);
	}

	[TestMethod]
	public void FindCommentStart_NoComment_ReturnsNegative()
	{
		int result = FindCommentStart("Legend= 42", s_semicolonSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_CommentOnlyLine_ReturnsZero()
	{
		int result = FindCommentStart("; just a comment", s_semicolonSyntax);

		Assert.AreEqual(0, result);
	}

	[TestMethod]
	public void FindCommentStart_WithoutStringAwareness_TreatsSemicolonAsComment()
	{
		// String awareness is disabled, so a semicolon inside quotes is treated as a delimiter.
		int result = FindCommentStart("Legend= \"hello;world\"", s_semicolonSyntax);

		// The delimiter sits at index 14, so the span starts there.
		Assert.AreEqual(14, result);
	}

	[TestMethod]
	public void FindCommentStart_NonBreakingSpaceBeforeComment_IncludesWhitespace()
	{
		int result = FindCommentStart("Legend= 42\u00A0; comment", s_semicolonSyntax);

		Assert.AreEqual(10, result);
	}

	[TestMethod]
	public void FindCommentStart_EmptyString_ReturnsNegative()
	{
		int result = FindCommentStart(string.Empty, s_semicolonSyntax);

		Assert.AreEqual(-1, result);
	}

	[TestMethod]
	public void FindCommentStart_MultiCharacterDelimiter_FindsDelimiter()
	{
		int result = FindCommentStart("code // comment", s_cStyleSyntax);

		// The comment (including leading whitespace) starts at index 4 (the space before '//').
		Assert.AreEqual(4, result);
	}

	[TestMethod]
	public void FindCommentStart_DelimiterAtEnd_ReturnsCorrectPosition()
	{
		int result = FindCommentStart("code ;", s_semicolonSyntax);

		// The comment (including leading whitespace) starts at index 4 (the space before ';').
		Assert.AreEqual(4, result);
	}

	[TestMethod]
	[DataRow(StringLiteralStyle.None, DisplayName = "WithoutStringAwareness")]
	[DataRow(StringLiteralStyle.DoubleQuoted, DisplayName = "WithStringAwareness")]
	public void FindCommentStart_EmptyDelimiter_ReturnsNegative(StringLiteralStyle stringStyle)
	{
		// An empty delimiter disables line-comment awareness regardless of the string style.
		var syntax = new CommentSyntax(string.Empty, null, stringStyle);

		Assert.AreEqual(-1, FindCommentStart("code ; comment", syntax));
	}
}
