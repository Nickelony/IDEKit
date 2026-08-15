namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class CommentOperationsCodeRangeTests
{
	private static readonly CommentSyntax s_semicolonSyntax = CommentSyntaxFixtures.SemicolonLine;
	private static readonly CommentSyntax s_blockAwareSyntax = CommentSyntaxFixtures.CStyle;

	[TestMethod]
	public void GetCodeRange_LineWithComment_ReturnsCodeBeforeWhitespace()
	{
		TextRange range = CommentOperations.GetCodeRange("Legend= 42 ; comment", s_semicolonSyntax);

		Assert.AreEqual(0, range.Offset);
		// Whitespace before the delimiter belongs to the span, so code ends before the space at index 10.
		Assert.AreEqual(10, range.Length);
		Assert.AreEqual("Legend= 42", range.GetText("Legend= 42 ; comment"));
	}

	[TestMethod]
	public void GetCodeRange_LineWithBlockComment_ReturnsCodeBeforeOpener()
	{
		TextRange range = CommentOperations.GetCodeRange("code /* comment */", s_blockAwareSyntax);

		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(5, range.Length);
		Assert.AreEqual("code ", range.GetText("code /* comment */"));
	}

	[TestMethod]
	public void GetCodeRange_CommentOnlyLine_ReturnsEmptyRange()
	{
		TextRange range = CommentOperations.GetCodeRange("; just a comment", s_semicolonSyntax);

		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(0, range.Length);
		Assert.IsTrue(range.IsEmpty);
	}

	[TestMethod]
	public void GetCodeRange_CommentOnlyLineAfterCode_ExcludesAbsorbedLineEnding()
	{
		TextRange range = CommentOperations.GetCodeRange("code\r\n; comment", s_semicolonSyntax);

		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(4, range.Length);
		Assert.AreEqual("code", range.GetText("code\r\n; comment"));
	}

	[TestMethod]
	public void GetCodeRange_CommentOnlyBlock_ReturnsEmptyRange()
	{
		TextRange range = CommentOperations.GetCodeRange("/* comment */", s_blockAwareSyntax);

		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(0, range.Length);
	}

	[TestMethod]
	public void GetCodeRange_NoComment_ReturnsFullText()
	{
		TextRange range = CommentOperations.GetCodeRange("Legend= 42", s_semicolonSyntax);

		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(10, range.Length);
		Assert.AreEqual("Legend= 42", range.GetText("Legend= 42"));
	}

	[TestMethod]
	public void GetCodeRange_EmptyString_ReturnsEmptyRange()
	{
		TextRange range = CommentOperations.GetCodeRange(string.Empty, s_semicolonSyntax);

		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(0, range.Length);
	}

	[TestMethod]
	public void GetCodeRange_PythonHashInSingleQuotes_ReturnsFullText()
	{
		var pythonSyntax = new CommentSyntax("#", null, StringLiteralStyle.DoubleQuoted | StringLiteralStyle.SingleQuoted);

		TextRange range = CommentOperations.GetCodeRange("url = 'http://x/#y'", pythonSyntax);

		Assert.AreEqual(0, range.Offset);
		Assert.AreEqual(19, range.Length);
	}
}
