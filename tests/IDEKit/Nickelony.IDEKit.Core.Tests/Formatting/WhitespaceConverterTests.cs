namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class WhitespaceConverterTests
{
	[TestMethod]
	[DataRow(0)]
	[DataRow(-4)]
	public void ConvertIndentationToTabs_NonPositiveTabSize_Throws(int tabSize)
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WhitespaceConverter.ConvertIndentationToTabs("code", tabSize));
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(-4)]
	public void ExpandTabs_NonPositiveTabSize_Throws(int tabSize)
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WhitespaceConverter.ExpandTabs("code", tabSize));
	}

	[TestMethod]
	public void ConvertIndentationToTabs_EmptyInput_ReturnsEmpty()
	{
		Assert.AreEqual(string.Empty, WhitespaceConverter.ConvertIndentationToTabs(string.Empty, 4));
	}

	[TestMethod]
	public void ConvertIndentationToTabs_NoIndentation_ReturnsOriginal()
	{
		const string input = "Legend= 42";

		Assert.AreEqual(input, WhitespaceConverter.ConvertIndentationToTabs(input, 4));
	}

	[TestMethod]
	public void ConvertIndentationToTabs_FourSpaces_BecomesOneTab()
	{
		Assert.AreEqual("\tLegend= 42", WhitespaceConverter.ConvertIndentationToTabs("    Legend= 42", 4));
	}

	[TestMethod]
	public void ConvertIndentationToTabs_EightSpaces_BecomesTwoTabs()
	{
		Assert.AreEqual("\t\tLegend= 42", WhitespaceConverter.ConvertIndentationToTabs("        Legend= 42", 4));
	}

	[TestMethod]
	public void ConvertIndentationToTabs_PartialLeadingSpaces_StayAsSpaces()
	{
		Assert.AreEqual("   Legend= 42", WhitespaceConverter.ConvertIndentationToTabs("   Legend= 42", 4));
	}

	[TestMethod]
	public void ConvertIndentationToTabs_SpacesNotAlignedToTabStop_KeepTrailingPartialGroup()
	{
		// The first four spaces form a tab; the remaining two stay as spaces.
		Assert.AreEqual("\t  Legend= 42", WhitespaceConverter.ConvertIndentationToTabs("      Legend= 42", 4));
	}

	[TestMethod]
	public void ConvertIndentationToTabsThenExpandTabs_RestoresSpaceIndentation()
	{
		// Space-only indentation survives the round trip: each aligned group becomes one tab and
		// expands back to the same number of spaces, so the visual columns are preserved.
		const string text = "    a\n        b\n    c";

		string tabs = WhitespaceConverter.ConvertIndentationToTabs(text, 4);
		string spaces = WhitespaceConverter.ExpandTabs(tabs, 4);

		Assert.AreEqual("\ta\n\t\tb\n\tc", tabs);
		Assert.AreEqual(text, spaces);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_ExistingTab_IsPreservedAndAdvancesColumn()
	{
		// The tab advances from column 2 to the next tab stop (4), so the two following spaces do
		// not reach the next stop and stay as spaces.
		Assert.AreEqual("  \t  Legend= 42", WhitespaceConverter.ConvertIndentationToTabs("  \t  Legend= 42", 4));
		Assert.AreEqual("\t   Legend= 42", WhitespaceConverter.ConvertIndentationToTabs("\t   Legend= 42", 4));
	}

	[TestMethod]
	public void ConvertIndentationToTabs_ConsecutiveTabs_ArePreservedAndAdvanceColumns()
	{
		// Two tabs advance to column 8 at tab size 4, so the following two-space run cannot reach a
		// tab stop and stays as spaces.
		Assert.AreEqual("\t\t  x", WhitespaceConverter.ConvertIndentationToTabs("\t\t  x", 4));
	}

	[TestMethod]
	public void ConvertIndentationToTabs_TabSizeOne_ConvertsEveryIndentationSpace()
	{
		Assert.AreEqual("\t\tx", WhitespaceConverter.ConvertIndentationToTabs("  x", 1));
	}

	[TestMethod]
	public void ConvertIndentationToTabs_NonSpaceWhitespace_EndsTheIndentationScan()
	{
		// A non-breaking space or form feed is content, not indentation whitespace, so it ends the
		// scan; the spaces before it still convert.
		Assert.AreEqual("\t\u00A0x", WhitespaceConverter.ConvertIndentationToTabs("    \u00A0x", 4));
		Assert.AreEqual("\t\fx", WhitespaceConverter.ConvertIndentationToTabs("    \fx", 4));
	}

	[TestMethod]
	public void ConvertIndentationToTabs_EmptyLines_StayEmpty()
	{
		Assert.AreEqual("\n\n", WhitespaceConverter.ConvertIndentationToTabs("\n\n", 4));
	}

	[TestMethod]
	public void ConvertIndentationToTabs_PreservesNonIndentationContent()
	{
		const string input = "    Legend= \"  hello;world  \" ; 42 >\n  , 43";

		string result = WhitespaceConverter.ConvertIndentationToTabs(input, 4);

		// Content after indentation is unchanged.
		Assert.AreEqual("\tLegend= \"  hello;world  \" ; 42 >\n  , 43", result);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_LargeTabSize_KeepsSpaces()
	{
		// Four spaces do not reach a tab stop of 1024.
		Assert.AreEqual("    Legend= 42", WhitespaceConverter.ConvertIndentationToTabs("    Legend= 42", 1024));
	}

	[TestMethod]
	[DataRow("    A\n    B\n", "\tA\n\tB\n", DisplayName = "LF")]
	[DataRow("    A\r\n    B\r\n", "\tA\r\n\tB\r\n", DisplayName = "CRLF")]
	[DataRow("    A\r\n    B\n    C", "\tA\r\n\tB\n\tC", DisplayName = "Mixed")]
	[DataRow("    A\r    B\r", "\tA\r\tB\r", DisplayName = "StandaloneCR")]
	public void ConvertIndentationToTabs_LineEndings_Preserved(string input, string expected)
	{
		Assert.AreEqual(expected, WhitespaceConverter.ConvertIndentationToTabs(input, 4));
	}

	[TestMethod]
	public void ExpandTabs_EmptyInput_ReturnsEmpty()
	{
		Assert.AreEqual(string.Empty, WhitespaceConverter.ExpandTabs(string.Empty, 4));
	}

	[TestMethod]
	public void ExpandTabs_NoTabs_ReturnsOriginal()
	{
		const string input = "Legend= 42";

		Assert.AreEqual(input, WhitespaceConverter.ExpandTabs(input, 4));
	}

	[TestMethod]
	public void ExpandTabs_LeadingTab_BecomesTabSizeSpaces()
	{
		Assert.AreEqual("    Legend= 42", WhitespaceConverter.ExpandTabs("\tLegend= 42", 4));
	}

	[TestMethod]
	public void ExpandTabs_TabInsideContent_ReachesNextTabStop()
	{
		// Column before the tab is 1 ('a'), so the tab expands to 3 spaces.
		Assert.AreEqual("a   b", WhitespaceConverter.ExpandTabs("a\tb", 4));
	}

	[TestMethod]
	public void ExpandTabs_TabAfterPartialIndent_ReachesNextTabStop()
	{
		// Three spaces then a tab: the tab expands to one space (next stop at column 4).
		Assert.AreEqual("    b", WhitespaceConverter.ExpandTabs("   \tb", 4));
	}

	[TestMethod]
	public void ExpandTabs_TabAfterExactTabStop_ExpandsFully()
	{
		// Four spaces then a tab: the tab expands to four spaces.
		Assert.AreEqual("        b", WhitespaceConverter.ExpandTabs("    \tb", 4));
	}

	[TestMethod]
	public void ExpandTabs_EmptyLines_StayEmpty()
	{
		Assert.AreEqual("\n\n", WhitespaceConverter.ExpandTabs("\n\n", 4));
	}

	[TestMethod]
	[DataRow("\tA\n\tB\n", "    A\n    B\n", DisplayName = "LF")]
	[DataRow("\tA\r\n\tB\r\n", "    A\r\n    B\r\n", DisplayName = "CRLF")]
	[DataRow("\tA\r\n\tB\n\tC", "    A\r\n    B\n    C", DisplayName = "Mixed")]
	[DataRow("\tA\r\tB\r", "    A\r    B\r", DisplayName = "StandaloneCR")]
	public void ExpandTabs_LineEndings_Preserved(string input, string expected)
	{
		Assert.AreEqual(expected, WhitespaceConverter.ExpandTabs(input, 4));
	}

	[TestMethod]
	public void ConvertIndentationToTabs_NoConvertibleIndentation_ReturnsSameInstance()
	{
		// No leading space reaches a tab stop, so callers can detect the no-op by reference, matching
		// TrimTrailingWhitespaceFormatter.
		const string input = "code\n\tcode";

		Assert.AreSame(input, WhitespaceConverter.ConvertIndentationToTabs(input, 4));
	}

	[TestMethod]
	public void ExpandTabs_NoTabs_ReturnsSameInstance()
	{
		const string input = "code\n  code";

		Assert.AreSame(input, WhitespaceConverter.ExpandTabs(input, 4));
	}
}
