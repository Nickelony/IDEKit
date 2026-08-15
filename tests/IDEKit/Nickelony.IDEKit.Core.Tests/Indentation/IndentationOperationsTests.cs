namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class IndentationOperationsTests
{
	[TestMethod]
	[DataRow(false, 4, "\t", DisplayName = "TabMode")]
	[DataRow(true, 4, "    ", DisplayName = "Spaces")]
	[DataRow(true, 0, "    ", DisplayName = "FallsBackToFour")]
	public void CreateIndentationUnit_ReturnsExpectedUnit(bool convertTabsToSpaces, int indentationSize, string expected)
	{
		Assert.AreEqual(expected, IndentationOperations.CreateIndentationUnit(convertTabsToSpaces, indentationSize));
	}

	[TestMethod]
	[DataRow("", "", DisplayName = "Empty")]
	[DataRow("x", "", DisplayName = "NoWhitespace")]
	[DataRow("   x", "   ", DisplayName = "Spaces")]
	[DataRow("\t x", "\t ", DisplayName = "TabAndSpace")]
	[DataRow("x\n", "", DisplayName = "TerminatorAfterContent")]
	public void GetLeadingWhitespace_ReturnsWhitespacePrefix(string text, string expected)
	{
		Assert.AreEqual(expected, IndentationOperations.GetLeadingWhitespace(text));
	}

	[TestMethod]
	[DataRow("", 0, DisplayName = "Empty")]
	[DataRow("x", 0, DisplayName = "NoWhitespace")]
	[DataRow("   x", 3, DisplayName = "Spaces")]
	[DataRow("\tx", 1, DisplayName = "Tab")]
	public void GetLeadingWhitespaceLength_CountsWhitespaceBeforeContent(string text, int expected)
	{
		Assert.AreEqual(expected, IndentationOperations.GetLeadingWhitespaceLength(text));
	}

	[TestMethod]
	public void GetLeadingWhitespace_LineWithCarriageReturn_ExcludesTerminators()
	{
		Assert.AreEqual("\t ", IndentationOperations.GetLeadingWhitespace("\t \r x"));
	}

	[TestMethod]
	[DataRow("  \u00A0x", 2, DisplayName = "NonBreakingSpace")]
	[DataRow("\u000Cx", 0, DisplayName = "FormFeed")]
	[DataRow("   x", 3, DisplayName = "Spaces")]
	public void GetLeadingWhitespaceLength_NonLayoutWhitespaceEndsTheScan(string text, int expected)
	{
		// Only spaces and tabs count as indentation; a non-breaking space or a form feed is content.
		Assert.AreEqual(expected, IndentationOperations.GetLeadingWhitespaceLength(text));
	}

	[TestMethod]
	[DataRow("", "    ", "", DisplayName = "EmptyIndentation")]
	[DataRow("  ", "    ", "", DisplayName = "ShorterThanUnit")]
	[DataRow("\t\t", "\t", "\t", DisplayName = "TwoTabs")]
	[DataRow("        ", "    ", "    ", DisplayName = "TwoUnits")]
	public void TruncateIndentationByUnitLength_RemovesOneUnit(string indentation, string unit, string expected)
	{
		Assert.AreEqual(expected, IndentationOperations.TruncateIndentationByUnitLength(indentation, unit));
	}

	[TestMethod]
	public void TruncateIndentationByUnitLength_MixedIndentation_TruncatesByUnitLength()
	{
		// The unit is never matched, so a tab unit removes one character from space indentation.
		Assert.AreEqual("     ", IndentationOperations.TruncateIndentationByUnitLength("      ", "\t"));
		Assert.AreEqual(" ", IndentationOperations.TruncateIndentationByUnitLength("  ", "\t"));
	}

	[TestMethod]
	public void TruncateIndentationByUnitLength_EmptyUnit_RemovesOneCharacter()
	{
		Assert.AreEqual("ab", IndentationOperations.TruncateIndentationByUnitLength("abc", string.Empty));
	}

	[TestMethod]
	public void TruncateIndentationByUnitLength_UnitLongerThanIndentation_RemovesWholeIndentation()
	{
		Assert.AreEqual(string.Empty, IndentationOperations.TruncateIndentationByUnitLength("  ", "    "));
	}

	[TestMethod]
	[DataRow("  ", "    ", 0, "  ", DisplayName = "ZeroLevels")]
	[DataRow("  ", "    ", 2, "          ", DisplayName = "TwoLevels")]
	public void BuildIndentation_ComposesBaseAndLevels(string baseIndentation, string unit, int level, string expected)
	{
		Assert.AreEqual(expected, IndentationOperations.BuildIndentation(baseIndentation, unit, level));
	}

	[TestMethod]
	public void BuildIndentation_NegativeLevel_ReturnsBaseIndentation()
	{
		Assert.AreEqual("  ", IndentationOperations.BuildIndentation("  ", "    ", -3));
	}

	[TestMethod]
	public void BuildIndentation_ManyLevels_AppendsEveryUnit()
	{
		string result = IndentationOperations.BuildIndentation(string.Empty, "\t", 10_000);

		Assert.AreEqual(10_000, result.Length);
		Assert.IsTrue(result.All(character => character == '\t'));
	}

	[TestMethod]
	[DataRow("", false, DisplayName = "Empty")]
	[DataRow("abc", false, DisplayName = "NoBreak")]
	[DataRow("a\rb", true, DisplayName = "LoneCr")]
	[DataRow("a\nb", true, DisplayName = "Lf")]
	[DataRow("a\r\nb", true, DisplayName = "CrLf")]
	public void ContainsLineBreak_DetectsCrLfAndLoneBreaks(string text, bool expected)
	{
		Assert.AreEqual(expected, IndentationOperations.ContainsLineBreak(text));
	}

	[TestMethod]
	public void SplitLines_PreservesDelimitersAndOffsets()
	{
		IReadOnlyList<IndentationTextLine> lines = IndentationOperations.SplitLines("a\r\nb\nc");

		Assert.AreEqual(3, lines.Count);
		Assert.AreEqual(new IndentationTextLine("a", "\r\n", 0), lines[0]);
		Assert.AreEqual(new IndentationTextLine("b", "\n", 3), lines[1]);
		Assert.AreEqual(new IndentationTextLine("c", string.Empty, 5), lines[2]);
	}

	[TestMethod]
	public void SplitLines_TrailingLineBreak_IncludesFinalEmptyLine()
	{
		IReadOnlyList<IndentationTextLine> lines = IndentationOperations.SplitLines("a\n");

		Assert.AreEqual(2, lines.Count);
		Assert.AreEqual(new IndentationTextLine("a", "\n", 0), lines[0]);
		Assert.AreEqual(new IndentationTextLine(string.Empty, string.Empty, 2), lines[1]);
	}

	[TestMethod]
	public void SplitLines_ReturnedView_RejectsMutation()
	{
		IReadOnlyList<IndentationTextLine> lines = IndentationOperations.SplitLines("a");

		// The internal list must not be reachable through the returned view: mutation through the
		// list interface throws.
		Assert.ThrowsExactly<NotSupportedException>(() =>
			((IList<IndentationTextLine>)lines).Add(new IndentationTextLine("b", string.Empty, 1)));
	}
}
