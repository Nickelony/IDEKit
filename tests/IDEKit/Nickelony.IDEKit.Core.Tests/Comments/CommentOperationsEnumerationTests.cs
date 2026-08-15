namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class CommentOperationsEnumerationTests
{
	private static readonly CommentSyntax s_cStyleSyntax = CommentSyntaxFixtures.CStyle;

	[TestMethod]
	public void EnumerateComments_VisitsLineAndBlockCommentsInOrder()
	{
		const string text = "a // one\nb /* two */ c // three";
		var spans = new List<CommentSpan>();

		foreach (CommentSpan span in CommentOperations.EnumerateComments(text, s_cStyleSyntax))
			spans.Add(span);

		Assert.AreEqual(3, spans.Count);
		Assert.IsTrue(spans[0].IsLineComment);
		Assert.IsTrue(spans[1].IsBlockComment);
		Assert.IsTrue(spans[2].IsLineComment);
		Assert.IsTrue(spans[0].EndOffset <= spans[1].StartOffset);
		Assert.IsTrue(spans[1].EndOffset <= spans[2].StartOffset);
	}

	[TestMethod]
	public void EnumerateComments_CrLfLineComment_EndsBeforeTheCarriageReturn()
	{
		const string text = "code // note\r\nnext";
		var spans = new List<CommentSpan>();

		foreach (CommentSpan span in CommentOperations.EnumerateComments(text, s_cStyleSyntax))
			spans.Add(span);

		Assert.AreEqual(1, spans.Count);
		Assert.IsTrue(spans[0].IsLineComment);

		// The span covers the comment's own line and stops before the CRLF pair.
		Assert.AreEqual(4, spans[0].StartOffset);
		Assert.AreEqual(12, spans[0].EndOffset);
	}

	[TestMethod]
	public void EnumerateComments_NoComments_YieldsNothing()
	{
		var spans = new List<CommentSpan>();

		foreach (CommentSpan span in CommentOperations.EnumerateComments("no comments here", s_cStyleSyntax))
			spans.Add(span);

		Assert.AreEqual(0, spans.Count);
	}

	[TestMethod]
	public void EnumerateComments_UnclosedBlockComment_ExtendsToEndOfText()
	{
		const string text = "code /* unterminated";
		var spans = new List<CommentSpan>();

		foreach (CommentSpan span in CommentOperations.EnumerateComments(text, s_cStyleSyntax))
			spans.Add(span);

		Assert.AreEqual(1, spans.Count);
		Assert.IsTrue(spans[0].IsBlockComment);
		Assert.AreEqual(5, spans[0].StartOffset);
		Assert.AreEqual(text.Length, spans[0].EndOffset);
	}

	[TestMethod]
	public void EnumerateComments_BlockCommentBeforeLineComment_ReportsBothInOrder()
	{
		var spans = new List<CommentSpan>();

		foreach (CommentSpan span in CommentOperations.EnumerateComments("/* block */ code // line", CommentSyntaxFixtures.CStyleDoubleQuoted))
			spans.Add(span);

		Assert.AreEqual(2, spans.Count);
		Assert.IsTrue(spans[0].IsBlockComment);
		Assert.IsTrue(spans[1].IsLineComment);
		// The line span includes the whitespace before the delimiter, which itself starts at index 17.
		Assert.AreEqual(16, spans[1].StartOffset);
		Assert.AreEqual(17, spans[1].DelimiterStart);
	}
}
