namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class CommentOperationsLineCommentTests
{
	[TestMethod]
	public void MaskComments_ReplacesCommentWithSpaces_PreservesLength()
	{
		string result = CommentOperations.MaskComments("Legend= 42 ; comment", s_semicolonSyntax);

		Assert.AreEqual("Legend= 42          ", result);
		Assert.AreEqual(20, result.Length);
	}

	[TestMethod]
	public void MaskComments_NoComment_ReturnsOriginal()
	{
		string result = CommentOperations.MaskComments("Legend= 42", s_semicolonSyntax);

		Assert.AreEqual("Legend= 42", result);
	}

	[TestMethod]
	public void MaskComments_EmptyString_ReturnsEmpty()
	{
		string result = CommentOperations.MaskComments(string.Empty, s_semicolonSyntax);

		Assert.AreEqual(string.Empty, result);
	}

	[TestMethod]
	public void MaskComments_WithTrailingNewline_PreservesNewlineAndLength()
	{
		string input = "Legend= 42 ; comment\n";
		string result = CommentOperations.MaskComments(input, s_semicolonSyntax);

		Assert.AreEqual("Legend= 42          \n", result);
		Assert.AreEqual(input.Length, result.Length);
	}

	[TestMethod]
	public void MaskComments_CRLF_PreservesLineEnding()
	{
		string input = "A ; c\r\nB ; d\r\n";
		string result = CommentOperations.MaskComments(input, s_semicolonSyntax);

		Assert.AreEqual("A    \r\nB    \r\n", result);
		Assert.AreEqual(input.Length, result.Length);
		Assert.IsFalse(result.Contains(";"));
	}

	[TestMethod]
	public void MaskComments_CommentOnlyLine_PreservesLineEndingAndLength()
	{
		// The comment-only line includes the preceding CRLF in its span; the line terminator is kept so
		// the line structure stays intact while the delimiter and the comment text become spaces.
		string input = "Line1\r\n; comment";
		string result = CommentOperations.MaskComments(input, s_semicolonSyntax);

		Assert.AreEqual("Line1\r\n" + new string(' ', 9), result);
		Assert.AreEqual(input.Length, result.Length);
	}

	[TestMethod]
	public void MaskComments_CommentOnlyLineWithLoneCr_PreservesLineEnding()
	{
		// A lone CR ending the preceding line is kept as well, so the line count does not change.
		string input = "Line1\r; comment";
		string result = CommentOperations.MaskComments(input, s_semicolonSyntax);

		Assert.AreEqual("Line1\r" + new string(' ', 9), result);
		Assert.AreEqual(input.Length, result.Length);
	}

	[TestMethod]
	public void MaskComments_CommentOnlyLineAfterBlankLine_PreservesLineStructure()
	{
		string input = "a\n\n; c\nb";

		string result = CommentOperations.MaskComments(input, s_semicolonSyntax);

		// Only the line terminator directly before the comment is absorbed, so the blank line stays.
		Assert.AreEqual("a\n\n   \nb", result);
		Assert.AreEqual(input.Length, result.Length);
	}

	[TestMethod]
	public void MaskComments_SpacesMatchOriginalCommentLength()
	{
		// Every masked character becomes a space, including the whitespace and the delimiter.
		Assert.AreEqual("         ", CommentOperations.MaskComments("; comment", s_semicolonSyntax));
		Assert.AreEqual("Line1      \nLine2        ", CommentOperations.MaskComments("Line1 ; abc\nLine2 ; defgh", s_semicolonSyntax));
	}

	[TestMethod]
	public void MaskComments_CommentOnlyFirstLine_MasksTextAndKeepsLineStructure()
	{
		// The first line has no preceding line terminator to absorb, so the delimiter and text become
		// spaces while the line structure stays intact.
		string input = "; a\nb";
		string result = CommentOperations.MaskComments(input, s_semicolonSyntax);

		Assert.AreEqual("   \nb", result);
		Assert.AreEqual(input.Length, result.Length);

		string crlfResult = CommentOperations.MaskComments("; a\r\nb", s_semicolonSyntax);

		Assert.AreEqual("   \r\nb", crlfResult);
	}

	[TestMethod]
	public void MaskComments_PythonHashInString_PreservesLength()
	{
		string input = "x = 'a#b' # comment";
		string result = CommentOperations.MaskComments(input, CommentSyntaxFixtures.HashLine);

		// Only the comment and its preceding whitespace are masked; the # inside the string is kept.
		Assert.AreEqual("x = 'a#b'" + new string(' ', 10), result);
		Assert.AreEqual(input.Length, result.Length);
	}

	[TestMethod]
	public void MaskComments_LoneCrLineEnding_PreservesLineBreakAndLength()
	{
		string input = "a ; c\rb";

		string result = CommentOperations.MaskComments(input, s_semicolonSyntax);

		Assert.AreEqual("a    \rb", result);
		Assert.AreEqual(input.Length, result.Length);
	}
}
