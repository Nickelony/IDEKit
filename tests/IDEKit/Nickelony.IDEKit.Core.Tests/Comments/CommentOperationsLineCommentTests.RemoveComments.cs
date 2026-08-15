namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class CommentOperationsLineCommentTests
{
	[TestMethod]
	public void FindComment_LoneCr_EndsCommentAtCarriageReturn()
	{
		CommentSpan? comment = CommentOperations.FindComment("a ; c\rb", s_semicolonSyntax);

		Assert.IsNotNull(comment);
		Assert.IsTrue(comment.Value.IsLineComment);
		Assert.AreEqual(2, comment.Value.DelimiterStart);
		Assert.AreEqual(5, comment.Value.EndOffset);
	}

	[TestMethod]
	public void FindComment_LineCommentWithDelimitersInItsContent_EndsAtTheTerminator()
	{
		// Nothing inside a line comment is recognized, so the block opener, the closer, and the
		// second delimiter all stay content and the span still ends at the line terminator.
		var syntax = new CommentSyntax("//", new BlockCommentSyntax("/*", "*/"), StringLiteralStyle.DoubleQuoted);
		CommentSpan? comment = CommentOperations.FindComment("code // /* x */ // y\nnext", syntax);

		Assert.IsNotNull(comment);
		Assert.IsTrue(comment.Value.IsLineComment);
		Assert.AreEqual(4, comment.Value.StartOffset);
		Assert.AreEqual(5, comment.Value.DelimiterStart);
		Assert.AreEqual(20, comment.Value.EndOffset);
	}

	[TestMethod]
	public void RemoveComments_RemovesCommentAndPrecedingWhitespace()
	{
		string result = CommentOperations.RemoveComments("Legend= 42 ; comment", s_semicolonSyntax);

		Assert.AreEqual("Legend= 42", result);
	}

	[TestMethod]
	public void RemoveComments_NoComment_ReturnsOriginal()
	{
		const string text = "Legend= 42";

		string result = CommentOperations.RemoveComments(text, s_semicolonSyntax);

		// The documented fast path returns the original instance when no comment matches.
		Assert.AreSame(text, result);
	}

	[TestMethod]
	public void RemoveComments_CommentOnlyLine_ReturnsEmpty()
	{
		string result = CommentOperations.RemoveComments("; just a comment", s_semicolonSyntax);

		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	public void RemoveComments_EmptyString_ReturnsEmpty()
	{
		string result = CommentOperations.RemoveComments(string.Empty, s_semicolonSyntax);

		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	[DataRow("Legend= 42 ; comment\n", "Legend= 42\n", DisplayName = "Lf")]
	[DataRow("Legend= 42 ; comment\r\n", "Legend= 42\r\n", DisplayName = "CrLf")]
	[DataRow("Legend= 42 ; comment\r", "Legend= 42\r", DisplayName = "Cr")]
	public void RemoveComments_LineTerminatorStaysOutsideTheCommentSpan(string text, string expected)
	{
		// The line terminator is left outside the comment span, whichever terminator the document uses.
		Assert.AreEqual(expected, CommentOperations.RemoveComments(text, s_semicolonSyntax));
	}

	[TestMethod]
	public void RemoveComments_MultipleLines_RemovesCommentsFromAllLines()
	{
		string input = "Line1 ; comment1\nLine2 ; comment2\nLine3";

		string result = CommentOperations.RemoveComments(input, s_semicolonSyntax);

		Assert.AreEqual("Line1\nLine2\nLine3", result);
	}

	[TestMethod]
	public void RemoveComments_AllCommentLines_RemovesInterveningNewlines()
	{
		string input = "; a\n; b\n; c";

		string result = CommentOperations.RemoveComments(input, s_semicolonSyntax);

		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	public void RemoveComments_TrailingCommentOnlyLine_RemovesPrecedingLineEnding()
	{
		// A comment-only line also removes its preceding line terminator.
		string input = "Line1 ; comment\n; comment";

		string result = CommentOperations.RemoveComments(input, s_semicolonSyntax);

		Assert.AreEqual("Line1", result);
	}

	[TestMethod]
	public void RemoveComments_CommentOnlyLine_RemovesPrecedingLineEnding()
	{
		string result = CommentOperations.RemoveComments("Line1\r\n; comment", s_semicolonSyntax);

		Assert.AreEqual("Line1", result);
	}

	[TestMethod]
	public void RemoveComments_CommentOnlyLineAfterBlankLine_PreservesBlankLine()
	{
		// The span absorbs only the line terminator directly before the comment line, so the blank line
		// above it stays.
		string result = CommentOperations.RemoveComments("a\n\n; c\nb", s_semicolonSyntax);

		Assert.AreEqual("a\n\nb", result);
	}

	[TestMethod]
	[DataRow("; a\nb", "\nb", DisplayName = "Lf")]
	[DataRow("; a\r\nb", "\r\nb", DisplayName = "CrLf")]
	public void RemoveComments_CommentOnlyFirstLine_LeavesEmptyFirstLine(string text, string expected)
	{
		// A comment-only first line has no preceding line terminator to absorb, so only its own text is
		// removed and the following line keeps its content.
		Assert.AreEqual(expected, CommentOperations.RemoveComments(text, s_semicolonSyntax));
	}

	[TestMethod]
	public void RemoveComments_LoneCrLineEnding_PreservesFollowingLine()
	{
		string result = CommentOperations.RemoveComments("a ; c\rb", s_semicolonSyntax);

		Assert.AreEqual("a\rb", result);
	}
}
