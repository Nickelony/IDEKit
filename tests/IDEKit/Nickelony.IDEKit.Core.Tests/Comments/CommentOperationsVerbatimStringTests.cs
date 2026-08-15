namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class CommentOperationsVerbatimStringTests
{
	private static readonly CommentSyntax s_cSharpSyntax = CommentSyntaxFixtures.CSharpVerbatim;

	[TestMethod]
	public void FindComment_VerbatimStringWithTrailingBackslash_RecognizesFollowingLineComment()
	{
		// The backslash before the closing quote is literal content in a verbatim string, so the
		// string closes and the following "//" starts a line comment.
		const string text = "var p = @\"C:\\\"; // note";

		CommentSpan? comment = CommentOperations.FindComment(text, s_cSharpSyntax);

		Assert.IsNotNull(comment);
		Assert.IsTrue(comment.Value.IsLineComment);
		Assert.AreEqual(16, comment.Value.DelimiterStart);
	}

	[TestMethod]
	public void FindComment_DoubledQuote_DoesNotCloseVerbatimString()
	{
		// A doubled quote is an escaped quote; the string closes at the single quote before the
		// semicolon, so the line comment is recognized.
		const string text = "var s = @\"say \"\"hi\"\" now\"; // note";

		CommentSpan? comment = CommentOperations.FindComment(text, s_cSharpSyntax);

		Assert.IsNotNull(comment);
		Assert.AreEqual(27, comment.Value.DelimiterStart);
	}

	[TestMethod]
	public void FindComment_VerbatimStringSpanningLines_RecognizesFollowingLineComment()
	{
		// Verbatim strings may contain line terminators, so the comment on the closing line counts.
		const string text = "var s = @\"a\nb\"; // note";

		CommentSpan? comment = CommentOperations.FindComment(text, s_cSharpSyntax);

		Assert.IsNotNull(comment);
		Assert.AreEqual(16, comment.Value.DelimiterStart);
	}

	[TestMethod]
	public void FindComment_VerbatimStringSpanningCrLfLines_RecognizesFollowingLineComment()
	{
		// CRLF line terminators are string content in a verbatim string, so the scan stays inside the
		// string until the closing quote and the comment on the closing line counts.
		const string text = "var s = @\"a\r\nb\"; // note";

		CommentSpan? comment = CommentOperations.FindComment(text, s_cSharpSyntax);

		Assert.IsNotNull(comment);
		Assert.AreEqual(17, comment.Value.DelimiterStart);
	}

	[TestMethod]
	public void FindComment_InterpolatedVerbatimStringOrder_RecognizesFollowingLineComment()
	{
		// The $@"..." order is recognized through its at sign; interpolation holes are not modeled.
		const string text = "var s = $@\"C:\\\"; // note";

		CommentSpan? comment = CommentOperations.FindComment(text, s_cSharpSyntax);

		Assert.IsNotNull(comment);
		Assert.AreEqual(17, comment.Value.DelimiterStart);
	}
	[TestMethod]
	public void FindComment_InterpolatedVerbatimStringReversedOrder_RecognizesFollowingLineComment()
	{
		// The @$"..." order is the second valid C# interpolation order; the backslash before the
		// closing quote is literal content, so the string closes and the following "//" starts a
		// line comment. Treating the quote as escaped (the regular double-quoted rule) would hide it.
		const string text = "var p = @$\"C:\\\"; // note";

		CommentSpan? comment = CommentOperations.FindComment(text, s_cSharpSyntax);

		Assert.IsNotNull(comment);
		Assert.IsTrue(comment.Value.IsLineComment);
		Assert.AreEqual(17, comment.Value.DelimiterStart);
	}

	[TestMethod]
	public void FindComment_ReversedOrderVerbatimStringDoubledQuote_DoesNotCloseVerbatimString()
	{
		// A doubled quote is an escaped quote in the @$"..." order too; the string closes at the
		// single quote before the semicolon.
		const string text = "var s = @$\"say \"\"hi\"\" now\"; // note";

		CommentSpan? comment = CommentOperations.FindComment(text, s_cSharpSyntax);

		Assert.IsNotNull(comment);
		Assert.AreEqual(28, comment.Value.DelimiterStart);
	}
	[TestMethod]
	public void FindComment_UnterminatedVerbatimString_ReturnsNull()
	{
		// An unterminated verbatim string keeps the scan inside the string to the end of the text.
		Assert.IsNull(CommentOperations.FindComment("var p = @\"C:\\", s_cSharpSyntax));
	}

	[TestMethod]
	public void RemoveComments_KeepsCodeBeforeAndAfterVerbatimString()
	{
		const string text = "var p = @\"C:\\\"; // note";

		// The span absorbs the whitespace before the delimiter, so removal leaves the code up to
		// the semicolon.
		Assert.AreEqual("var p = @\"C:\\\";", CommentOperations.RemoveComments(text, s_cSharpSyntax));
	}
}
