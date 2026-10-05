namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class WhitespaceOperationsTests
{
	[TestMethod]
	[DataRow(0)]
	[DataRow(-4)]
	public void ConvertIndentationToTabs_NonPositiveTabSize_Throws(int tabSize) =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WhitespaceOperations.ConvertIndentationToTabs("code", tabSize));

	[TestMethod]
	[DataRow(0)]
	[DataRow(-4)]
	public void ExpandTabs_NonPositiveTabSize_Throws(int tabSize) =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WhitespaceOperations.ExpandTabs("code", tabSize));

	[TestMethod]
	public void ConvertIndentationToTabs_EmptyInput_ReturnsEmpty() =>
		Assert.AreEqual(string.Empty, WhitespaceOperations.ConvertIndentationToTabs(string.Empty, 4));

	[TestMethod]
	public void ConvertIndentationToTabs_NoIndentation_ReturnsOriginal()
	{
		const string input = "Legend= 42";
		Assert.AreEqual(input, WhitespaceOperations.ConvertIndentationToTabs(input, 4));
	}

	[TestMethod]
	public void ConvertIndentationToTabs_FourSpaces_BecomesOneTab() =>
		Assert.AreEqual("\tLegend= 42", WhitespaceOperations.ConvertIndentationToTabs("    Legend= 42", 4));

	[TestMethod]
	public void ConvertIndentationToTabs_EightSpaces_BecomesTwoTabs() =>
		Assert.AreEqual("\t\tLegend= 42", WhitespaceOperations.ConvertIndentationToTabs("        Legend= 42", 4));

	[TestMethod]
	public void ConvertIndentationToTabs_PartialLeadingSpaces_StayAsSpaces() =>
		Assert.AreEqual("   Legend= 42", WhitespaceOperations.ConvertIndentationToTabs("   Legend= 42", 4));

	[TestMethod]
	public void ConvertIndentationToTabs_SpacesNotAlignedToTabStop_KeepTrailingPartialGroup()
	{
		// The first four spaces form a tab; the remaining two stay as spaces.
		Assert.AreEqual("\t  Legend= 42", WhitespaceOperations.ConvertIndentationToTabs("      Legend= 42", 4));
	}

	// The slow reference: walk the columns, turn each group of spaces that reaches the next tab stop into a tab, and keep the rest.
	private static string ColumnWalk(string indentation, int tabSize)
	{
		var result = new System.Text.StringBuilder();

		int column = 0;
		int index = 0;

		while (index < indentation.Length)
		{
			if (indentation[index] == '\t')
			{
				result.Append('\t');
				column = ((column / tabSize) + 1) * tabSize;
				index++;

				continue;
			}

			int spaces = 0;

			while (index < indentation.Length && indentation[index] == ' ')
			{
				spaces++;
				index++;
			}

			while (spaces > 0)
			{
				int toStop = tabSize - (column % tabSize);

				if (spaces < toStop)
					break;

				result.Append('\t');
				column += toStop;
				spaces -= toStop;
			}

			result.Append(' ', spaces);
			column += spaces;
		}

		return result.ToString();
	}

	[TestMethod]
	public void ConvertIndentationToTabs_AgreesWithAColumnWalkOnEveryShortIndentation()
	{
		for (int tabSize = 1; tabSize <= 5; tabSize++)
		{
			for (int length = 0; length <= 9; length++)
			{
				for (int mask = 0; mask < (1 << length); mask++)
				{
					string indentation = string.Concat(Enumerable.Range(0, length).Select(bit => ((mask >> bit) & 1) == 1 ? "\t" : " "));

					Assert.AreEqual(
						ColumnWalk(indentation, tabSize) + "x",
						WhitespaceOperations.ConvertIndentationToTabs(indentation + "x", tabSize),
						$"tab size {tabSize}, indentation {indentation.Replace("\t", "T", StringComparison.Ordinal)}"
					);
				}
			}
		}
	}

	[TestMethod]
	public void ConvertIndentationToTabsThenExpandTabs_RestoresSpaceIndentation()
	{
		// Space-only indentation survives the round trip: each aligned group becomes one tab and
		// expands back to the same number of spaces, so the visual columns are preserved.
		const string text = "    a\n        b\n    c";

		string tabs = WhitespaceOperations.ConvertIndentationToTabs(text, 4);
		string spaces = WhitespaceOperations.ExpandTabs(tabs, 4);

		Assert.AreEqual("\ta\n\t\tb\n\tc", tabs);
		Assert.AreEqual(text, spaces);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_ExistingTab_IsPreservedAndAdvancesColumn()
	{
		// The tab advances from column 2 to the next tab stop (4), so the two following spaces do
		// not reach the next stop and stay as spaces.
		Assert.AreEqual("  \t  Legend= 42", WhitespaceOperations.ConvertIndentationToTabs("  \t  Legend= 42", 4));
		Assert.AreEqual("\t   Legend= 42", WhitespaceOperations.ConvertIndentationToTabs("\t   Legend= 42", 4));
	}

	[TestMethod]
	public void ConvertIndentationToTabs_ConsecutiveTabs_ArePreservedAndAdvanceColumns()
	{
		// Two tabs advance to column 8 at tab size 4, so the following two-space run cannot reach a
		// tab stop and stays as spaces.
		Assert.AreEqual("\t\t  x", WhitespaceOperations.ConvertIndentationToTabs("\t\t  x", 4));
	}

	[TestMethod]
	public void ConvertIndentationToTabs_TabSizeOne_ConvertsEveryIndentationSpace() =>
		Assert.AreEqual("\t\tx", WhitespaceOperations.ConvertIndentationToTabs("  x", 1));

	[TestMethod]
	public void ConvertIndentationToTabs_NonSpaceWhitespace_EndsTheIndentationScan()
	{
		// A non-breaking space or form feed is content, not indentation whitespace, so it ends the
		// scan; the spaces before it still convert.
		Assert.AreEqual("\t\u00A0x", WhitespaceOperations.ConvertIndentationToTabs("    \u00A0x", 4));
		Assert.AreEqual("\t\fx", WhitespaceOperations.ConvertIndentationToTabs("    \fx", 4));
	}

	[TestMethod]
	public void ConvertIndentationToTabs_EmptyLines_StayEmpty() => Assert.AreEqual("\n\n", WhitespaceOperations.ConvertIndentationToTabs("\n\n", 4));

	[TestMethod]
	public void ConvertIndentationToTabs_PreservesNonIndentationContent()
	{
		const string input = "    Legend= \"  hello;world  \" ; 42 >\n  , 43";

		string result = WhitespaceOperations.ConvertIndentationToTabs(input, 4);

		// Content after indentation is unchanged.
		Assert.AreEqual("\tLegend= \"  hello;world  \" ; 42 >\n  , 43", result);
	}

	[TestMethod]
	public void ConvertIndentationToTabs_LargeTabSize_KeepsSpaces()
	{
		// Four spaces do not reach the largest tab stop.
		Assert.AreEqual("    Legend= 42", WhitespaceOperations.ConvertIndentationToTabs("    Legend= 42", IndentationOperations.MaximumSize));
	}

	[TestMethod]
	[DataRow("    A\n    B\n", "\tA\n\tB\n", DisplayName = "LF")]
	[DataRow("    A\r\n    B\r\n", "\tA\r\n\tB\r\n", DisplayName = "CRLF")]
	[DataRow("    A\r\n    B\n    C", "\tA\r\n\tB\n\tC", DisplayName = "Mixed")]
	[DataRow("    A\r    B\r", "\tA\r\tB\r", DisplayName = "StandaloneCR")]
	public void ConvertIndentationToTabs_LineEndings_Preserved(string input, string expected) =>
		Assert.AreEqual(expected, WhitespaceOperations.ConvertIndentationToTabs(input, 4));

	[TestMethod]
	public void ExpandTabs_EmptyInput_ReturnsEmpty() => Assert.AreEqual(string.Empty, WhitespaceOperations.ExpandTabs(string.Empty, 4));

	[TestMethod]
	public void ExpandTabs_NoTabs_ReturnsOriginal()
	{
		const string input = "Legend= 42";
		Assert.AreEqual(input, WhitespaceOperations.ExpandTabs(input, 4));
	}

	[TestMethod]
	public void ExpandTabs_LeadingTab_BecomesTabSizeSpaces() => Assert.AreEqual("    Legend= 42", WhitespaceOperations.ExpandTabs("\tLegend= 42", 4));

	[TestMethod]
	public void ExpandTabs_TabInsideContent_ReachesNextTabStop()
	{
		// Column before the tab is 1 ('a'), so the tab expands to 3 spaces.
		Assert.AreEqual("a   b", WhitespaceOperations.ExpandTabs("a\tb", 4));
	}

	[TestMethod]
	public void ExpandTabs_TabAfterPartialIndent_ReachesNextTabStop()
	{
		// Three spaces then a tab: the tab expands to one space (next stop at column 4).
		Assert.AreEqual("    b", WhitespaceOperations.ExpandTabs("   \tb", 4));
	}

	[TestMethod]
	public void ExpandTabs_TabAfterExactTabStop_ExpandsFully()
	{
		// Four spaces then a tab: the tab expands to four spaces.
		Assert.AreEqual("        b", WhitespaceOperations.ExpandTabs("    \tb", 4));
	}

	[TestMethod]
	public void ExpandTabs_EmptyLines_StayEmpty() => Assert.AreEqual("\n\n", WhitespaceOperations.ExpandTabs("\n\n", 4));

	[TestMethod]
	[DataRow("\tA\n\tB\n", "    A\n    B\n", DisplayName = "LF")]
	[DataRow("\tA\r\n\tB\r\n", "    A\r\n    B\r\n", DisplayName = "CRLF")]
	[DataRow("\tA\r\n\tB\n\tC", "    A\r\n    B\n    C", DisplayName = "Mixed")]
	[DataRow("\tA\r\tB\r", "    A\r    B\r", DisplayName = "StandaloneCR")]
	public void ExpandTabs_LineEndings_Preserved(string input, string expected) =>
		Assert.AreEqual(expected, WhitespaceOperations.ExpandTabs(input, 4));

	[TestMethod]
	public void ConvertIndentationToTabs_NoConvertibleIndentation_ReturnsSameInstance()
	{
		// No leading space reaches a tab stop, so callers can detect the no-op by reference, matching
		// TrimTrailingWhitespaceFormatter.
		const string input = "code\n\tcode";
		Assert.AreSame(input, WhitespaceOperations.ConvertIndentationToTabs(input, 4));
	}

	[TestMethod]
	public void ExpandTabs_NoTabs_ReturnsSameInstance()
	{
		const string input = "code\n  code";
		Assert.AreSame(input, WhitespaceOperations.ExpandTabs(input, 4));
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(-4)]
	public void ConvertIndentationToSpaces_NonPositiveTabSize_Throws(int tabSize) =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WhitespaceOperations.ConvertIndentationToSpaces("code", tabSize));

	[TestMethod]
	public void ConvertIndentationToSpaces_NullText_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => WhitespaceOperations.ConvertIndentationToSpaces(null!, 4));

	[TestMethod]
	public void ConvertIndentationToSpaces_EmptyInput_ReturnsSameInstance() =>
		Assert.AreSame(string.Empty, WhitespaceOperations.ConvertIndentationToSpaces(string.Empty, 4));

	[TestMethod]
	public void ConvertIndentationToSpaces_LeadingTabs_BecomeTabSizeSpacesEach() =>
		Assert.AreEqual("    a\n        b", WhitespaceOperations.ConvertIndentationToSpaces("\ta\n\t\tb", 4));

	[TestMethod]
	public void ConvertIndentationToSpaces_TabAfterPartialIndent_ReachesNextTabStop() =>
		Assert.AreEqual("    b", WhitespaceOperations.ConvertIndentationToSpaces("   \tb", 4));

	[TestMethod]
	public void ConvertIndentationToSpaces_TabsAndSpaces_KeepTheWidthOfTheIndentation() =>
		Assert.AreEqual("      b", WhitespaceOperations.ConvertIndentationToSpaces("\t  b", 4));

	[TestMethod]
	public void ConvertIndentationToSpaces_TabAfterTheFirstCharacter_Stays()
	{
		// Only the leading whitespace converts, so a tab after code or inside a string literal keeps its meaning.
		const string input = "\tprint(\"a\tb\")\t-- note";
		Assert.AreEqual("    print(\"a\tb\")\t-- note", WhitespaceOperations.ConvertIndentationToSpaces(input, 4));
	}

	[TestMethod]
	public void ConvertIndentationToSpaces_TabAfterNonSpaceWhitespace_Stays()
	{
		// A form feed is content, so the tab behind it is not indentation.
		Assert.AreEqual("    \f\tx", WhitespaceOperations.ConvertIndentationToSpaces("\t\f\tx", 4));
	}

	[TestMethod]
	public void ConvertIndentationToSpaces_WhitespaceOnlyLine_ConvertsItsTabs() =>
		Assert.AreEqual("    \n", WhitespaceOperations.ConvertIndentationToSpaces("\t\n", 4));

	[TestMethod]
	public void ConvertIndentationToSpaces_NoLeadingTab_ReturnsSameInstance()
	{
		const string input = "code\n  code\n    x\ty";
		Assert.AreSame(input, WhitespaceOperations.ConvertIndentationToSpaces(input, 4));
	}

	[TestMethod]
	[DataRow("\tA\n\tB\n", "    A\n    B\n", DisplayName = "LF")]
	[DataRow("\tA\r\n\tB\r\n", "    A\r\n    B\r\n", DisplayName = "CRLF")]
	[DataRow("\tA\r\n\tB\n\tC", "    A\r\n    B\n    C", DisplayName = "Mixed")]
	[DataRow("\tA\r\tB\r", "    A\r    B\r", DisplayName = "StandaloneCR")]
	public void ConvertIndentationToSpaces_LineEndings_Preserved(string input, string expected) =>
		Assert.AreEqual(expected, WhitespaceOperations.ConvertIndentationToSpaces(input, 4));

	[TestMethod]
	public void ConvertIndentationToSpacesThenToTabs_RestoresTabIndentation()
	{
		const string text = "\ta\n\t\tb\n\tc\n";

		string spaces = WhitespaceOperations.ConvertIndentationToSpaces(text, 4);

		Assert.AreEqual(text, WhitespaceOperations.ConvertIndentationToTabs(spaces, 4));
	}

	[TestMethod]
	public void ExpandTabs_TabAfterTheFirstCharacter_ExpandsWhereConvertIndentationToSpacesDoesNot()
	{
		const string input = "a\tb";

		Assert.AreEqual("a   b", WhitespaceOperations.ExpandTabs(input, 4));
		Assert.AreSame(input, WhitespaceOperations.ConvertIndentationToSpaces(input, 4));
	}

	[TestMethod]
	[DataRow(0, 4)]
	[DataRow(-2, 4)]
	[DataRow(2, 0)]
	[DataRow(2, -4)]
	[DataRow(1001, 4)]
	[DataRow(2, 1001)]
	public void ResizeIndentation_SizeOutsideTheLimits_Throws(int currentSize, int newSize) =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WhitespaceOperations.ResizeIndentation("code", currentSize, newSize));

	[TestMethod]
	public void MaximumSize_IsTheLimitOfTheEditorEnginesIndentationSize() => Assert.AreEqual(1000, IndentationOperations.MaximumSize);

	[TestMethod]
	public void EveryOperationWithATabSize_AcceptsTheMaximumAndRejectsOneMore()
	{
		int limit = IndentationOperations.MaximumSize;

		Assert.AreEqual("a", WhitespaceOperations.ConvertIndentationToTabs("a", limit));
		Assert.AreEqual("a", WhitespaceOperations.ConvertIndentationToSpaces("a", limit));
		Assert.AreEqual("a", WhitespaceOperations.ExpandTabs("a", limit));
		Assert.AreEqual("a", WhitespaceOperations.ResizeIndentation("a", limit, limit));

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WhitespaceOperations.ConvertIndentationToTabs("a", limit + 1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WhitespaceOperations.ConvertIndentationToSpaces("a", limit + 1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WhitespaceOperations.ExpandTabs("a", limit + 1));
	}

	[TestMethod]
	public void ResizeIndentation_NullText_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => WhitespaceOperations.ResizeIndentation(null!, 2, 4));

	[TestMethod]
	public void ResizeIndentation_TwoToFour_DoublesEveryLevel() =>
		Assert.AreEqual("a\n    b\n        c\n    d", WhitespaceOperations.ResizeIndentation("a\n  b\n    c\n  d", 2, 4));

	[TestMethod]
	public void ResizeIndentation_FourToTwo_HalvesEveryLevel() =>
		Assert.AreEqual("a\n  b\n    c", WhitespaceOperations.ResizeIndentation("a\n    b\n        c", 4, 2));

	[TestMethod]
	public void ResizeIndentation_RemainderThatIsNotAWholeLevel_IsKept()
	{
		// Five spaces at size 2 are two levels and one extra column: two levels of four, and the column stays.
		Assert.AreEqual("         x", WhitespaceOperations.ResizeIndentation("     x", 2, 4));
		Assert.AreEqual("     x", WhitespaceOperations.ResizeIndentation("   x", 2, 4));
	}

	[TestMethod]
	public void ResizeIndentation_LineWithATabInItsIndentation_IsLeftAsItIs()
	{
		const string input = "a\n\tb\n  \tc\n\t  d\n  e";
		Assert.AreEqual("a\n\tb\n  \tc\n\t  d\n    e", WhitespaceOperations.ResizeIndentation(input, 2, 4));
	}

	[TestMethod]
	public void ResizeIndentation_NonSpaceWhitespace_EndsTheIndentationScan()
	{
		// A non-breaking space is content, so the two spaces before it are the indentation and the space after it stays.
		Assert.AreEqual("    \u00A0  x", WhitespaceOperations.ResizeIndentation("  \u00A0  x", 2, 4));
	}

	[TestMethod]
	public void ResizeIndentation_SpacesAfterTheFirstCharacter_Stay() =>
		Assert.AreEqual("    a  b   c", WhitespaceOperations.ResizeIndentation("  a  b   c", 2, 4));

	[TestMethod]
	public void ResizeIndentation_SameSize_ReturnsSameInstance()
	{
		const string input = "a\n  b\n    c";
		Assert.AreSame(input, WhitespaceOperations.ResizeIndentation(input, 2, 2));
	}

	[TestMethod]
	public void ResizeIndentation_NoIndentedLine_ReturnsSameInstance()
	{
		const string input = "a\n\tb\nc";
		Assert.AreSame(input, WhitespaceOperations.ResizeIndentation(input, 2, 4));
	}

	[TestMethod]
	public void ResizeIndentation_OnlyRemainders_ReturnsSameInstance()
	{
		// One space is less than a level of two, so it is a remainder at any new size.
		const string input = "a\n b";
		Assert.AreSame(input, WhitespaceOperations.ResizeIndentation(input, 2, 8));
	}

	[TestMethod]
	[DataRow("a\n  b\n", "a\n    b\n", DisplayName = "LF")]
	[DataRow("a\r\n  b\r\n", "a\r\n    b\r\n", DisplayName = "CRLF")]
	[DataRow("a\r\n  b\n  c", "a\r\n    b\n    c", DisplayName = "Mixed")]
	[DataRow("a\r  b\r", "a\r    b\r", DisplayName = "StandaloneCR")]
	public void ResizeIndentation_LineEndings_Preserved(string input, string expected) =>
		Assert.AreEqual(expected, WhitespaceOperations.ResizeIndentation(input, 2, 4));
}
