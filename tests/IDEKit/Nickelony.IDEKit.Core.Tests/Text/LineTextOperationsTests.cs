namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class LineTextOperationsTests
{
	[TestMethod]
	[DataRow("", "", DisplayName = "Empty")]
	[DataRow("x", "", DisplayName = "NoWhitespace")]
	[DataRow("   x", "   ", DisplayName = "Spaces")]
	[DataRow("\t x", "\t ", DisplayName = "TabAndSpace")]
	[DataRow("x\n", "", DisplayName = "TerminatorAfterContent")]
	public void GetLeadingWhitespace_ReturnsWhitespacePrefix(string text, string expected) =>
		Assert.AreEqual(expected, LineTextOperations.GetLeadingWhitespace(text));

	[TestMethod]
	[DataRow("", 0, DisplayName = "Empty")]
	[DataRow("x", 0, DisplayName = "NoWhitespace")]
	[DataRow("   x", 3, DisplayName = "Spaces")]
	[DataRow("\tx", 1, DisplayName = "Tab")]
	public void GetLeadingWhitespaceLength_CountsWhitespaceBeforeContent(string text, int expected) =>
		Assert.AreEqual(expected, LineTextOperations.GetLeadingWhitespaceLength(text));

	[TestMethod]
	public void GetLeadingWhitespace_LineWithCarriageReturn_ExcludesTerminators() =>
		Assert.AreEqual("\t ", LineTextOperations.GetLeadingWhitespace("\t \r x"));

	[TestMethod]
	[DataRow("  \u00A0x", 2, DisplayName = "NonBreakingSpace")]
	[DataRow("\u000Cx", 0, DisplayName = "FormFeed")]
	[DataRow("   x", 3, DisplayName = "Spaces")]
	public void GetLeadingWhitespaceLength_NonLayoutWhitespaceEndsTheScan(string text, int expected)
	{
		// Only spaces and tabs count as indentation; a non-breaking space or a form feed is content.
		Assert.AreEqual(expected, LineTextOperations.GetLeadingWhitespaceLength(text));
	}
}
