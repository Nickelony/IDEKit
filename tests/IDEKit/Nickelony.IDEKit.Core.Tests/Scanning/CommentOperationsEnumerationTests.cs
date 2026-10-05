namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class CommentOperationsEnumerationTests
{
	private static readonly CommentSyntax s_cStyleSyntax = CommentSyntaxTestHelpers.CStyle;

	[TestMethod]
	public void EnumerateComments_VisitsLineAndBlockCommentsInOrder()
	{
		const string text = "a // one\nb /* two */ c // three";
		var spans = new List<CommentSpan>();

		foreach (CommentSpan span in CommentOperations.EnumerateComments(text, s_cStyleSyntax))
			spans.Add(span);

		Assert.HasCount(3, spans);
		Assert.IsTrue(spans[0].IsLineComment);
		Assert.IsTrue(spans[1].IsBlockComment);
		Assert.IsTrue(spans[2].IsLineComment);
		Assert.IsLessThanOrEqualTo(spans[1].Range.Offset, spans[0].Range.EndOffset);
		Assert.IsLessThanOrEqualTo(spans[2].Range.Offset, spans[1].Range.EndOffset);
	}

	[TestMethod]
	public void EnumerateComments_CrLfLineComment_EndsBeforeTheCarriageReturn()
	{
		const string text = "code // note\r\nnext";
		var spans = new List<CommentSpan>();

		foreach (CommentSpan span in CommentOperations.EnumerateComments(text, s_cStyleSyntax))
			spans.Add(span);

		Assert.HasCount(1, spans);
		Assert.IsTrue(spans[0].IsLineComment);

		// The span covers the comment's own line and stops before the CRLF pair.
		Assert.AreEqual(4, spans[0].Range.Offset);
		Assert.AreEqual(12, spans[0].Range.EndOffset);
	}

	[TestMethod]
	public void EnumerateComments_NoComments_YieldsNothing()
	{
		var spans = new List<CommentSpan>();

		foreach (CommentSpan span in CommentOperations.EnumerateComments("no comments here", s_cStyleSyntax))
			spans.Add(span);

		Assert.IsEmpty(spans);
	}

	[TestMethod]
	public void EnumerateComments_UnclosedBlockComment_ExtendsToEndOfText()
	{
		const string text = "code /* unterminated";
		var spans = new List<CommentSpan>();

		foreach (CommentSpan span in CommentOperations.EnumerateComments(text, s_cStyleSyntax))
			spans.Add(span);

		Assert.HasCount(1, spans);
		Assert.IsTrue(spans[0].IsBlockComment);
		Assert.AreEqual(5, spans[0].Range.Offset);
		Assert.AreEqual(text.Length, spans[0].Range.EndOffset);
	}

	[TestMethod]
	public void EnumerateComments_BlockCommentBeforeLineComment_ReportsBothInOrder()
	{
		var spans = new List<CommentSpan>();

		foreach (CommentSpan span in CommentOperations.EnumerateComments("/* block */ code // line", CommentSyntaxTestHelpers.CStyleDoubleQuoted))
			spans.Add(span);

		Assert.HasCount(2, spans);
		Assert.IsTrue(spans[0].IsBlockComment);
		Assert.IsTrue(spans[1].IsLineComment);

		// The line span includes the whitespace before the delimiter, which itself starts at index 17.
		Assert.AreEqual(16, spans[1].Range.Offset);
		Assert.AreEqual(17, spans[1].DelimiterStart);
	}
}
