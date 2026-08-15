namespace Nickelony.IDEKit.Core.Tests;

// These tests pin the ReadOnlySpan<char> overloads as an API surface and smoke-check their
// delegation: the span overloads share the string overloads' implementation, so the full behavior
// matrix lives in the sibling test files.
[TestClass]
public sealed class CommentOperationsSpanOverloadTests
{
	private static readonly CommentSyntax s_lineSyntax = CommentSyntaxFixtures.SemicolonLine;
	private static readonly CommentSyntax s_blockSyntax = CommentSyntaxFixtures.CStylePlain;

	[TestMethod]
	public void RemoveComments_SpanInput_RemovesLineComment()
	{
		ReadOnlySpan<char> text = "code ; note";

		string result = CommentOperations.RemoveComments(text, s_lineSyntax);

		Assert.AreEqual("code", result);
	}

	[TestMethod]
	public void MaskComments_SpanInput_MasksLineComment()
	{
		ReadOnlySpan<char> text = "code ; note";

		string result = CommentOperations.MaskComments(text, s_lineSyntax);

		Assert.AreEqual("code" + new string(' ', 7), result);
	}

	[TestMethod]
	public void RemoveBlockComments_SpanInput_RemovesBlockComment()
	{
		ReadOnlySpan<char> text = "a /* c */ b";

		string result = CommentOperations.RemoveBlockComments(text, s_blockSyntax);

		Assert.AreEqual("a  b", result);
	}

	[TestMethod]
	public void MaskBlockComments_SpanInput_MasksBlockComment()
	{
		ReadOnlySpan<char> text = "a /* c */ b";

		string result = CommentOperations.MaskBlockComments(text, s_blockSyntax);

		Assert.AreEqual("a " + new string(' ', 7) + " b", result);
	}
}
