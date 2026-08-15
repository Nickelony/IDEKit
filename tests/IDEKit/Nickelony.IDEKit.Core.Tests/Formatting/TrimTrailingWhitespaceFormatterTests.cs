namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TrimTrailingWhitespaceFormatterTests
{
	[TestMethod]
	public void FormatDocument_ThroughTheInterface_UsesTheSameContract()
	{
		ITextDocumentFormatter formatter = TrimTrailingWhitespaceFormatter.Instance;

		Assert.AreEqual("a\nb", formatter.FormatDocument("a  \nb\t"));
	}

	[TestMethod]
	public void FormatDocument_TrimsTrailingWhitespacePerLineAndPreservesLf()
	{
		string result = TrimTrailingWhitespaceFormatter.Instance.FormatDocument("a  \nb\t\nc");

		Assert.AreEqual("a\nb\nc", result);
	}

	[TestMethod]
	public void FormatDocument_PreservesCrLfLineEndings()
	{
		string result = TrimTrailingWhitespaceFormatter.Instance.FormatDocument("a  \r\nb\t\r\n");

		Assert.AreEqual("a\r\nb\r\n", result);
	}

	[TestMethod]
	public void FormatDocument_PreservesLoneCrLineEndings()
	{
		string result = TrimTrailingWhitespaceFormatter.Instance.FormatDocument("a  \rb  ");

		Assert.AreEqual("a\rb", result);
	}

	[TestMethod]
	public void FormatDocument_MixedLineEndings_ArePreserved()
	{
		string result = TrimTrailingWhitespaceFormatter.Instance.FormatDocument("a \r\nb \nc  \rd");

		Assert.AreEqual("a\r\nb\nc\rd", result);
	}

	[TestMethod]
	public void FormatDocument_EmptyInput_ReturnsEmpty()
	{
		Assert.AreEqual(string.Empty, TrimTrailingWhitespaceFormatter.Instance.FormatDocument(string.Empty));
	}

	[TestMethod]
	public void FormatDocument_AlreadyTrimmedInput_ReturnsTheSameInstance()
	{
		// A no-op format reports "no changes" by returning the input unchanged, as the interface
		// documents; see ITextDocumentFormatter.
		const string content = "a\nb\r\nc";

		string result = TrimTrailingWhitespaceFormatter.Instance.FormatDocument(content);

		Assert.AreSame(content, result);
	}

	[TestMethod]
	public void FormatDocument_WhitespaceOnlyContent_TrimsToEmpty()
	{
		Assert.AreEqual(string.Empty, TrimTrailingWhitespaceFormatter.Instance.FormatDocument("   \t "));
	}

	[TestMethod]
	public void FormatDocument_TrailingNonAsciiWhitespace_IsPreserved()
	{
		// Only spaces and tabs are trimmed; a non-breaking space and an ideographic space at the end
		// of a line survive so content that relies on them is not altered silently.
		string result = TrimTrailingWhitespaceFormatter.Instance.FormatDocument("a\u00A0  \nb\u3000\t");

		Assert.AreEqual("a\u00A0\nb\u3000", result);
	}

	[TestMethod]
	public void FormatDocument_TrailingUnterminatedLine_IsTrimmed()
	{
		string result = TrimTrailingWhitespaceFormatter.Instance.FormatDocument("a\nb \t");

		Assert.AreEqual("a\nb", result);
	}
}
