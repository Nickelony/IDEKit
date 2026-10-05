namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class IndentationDetectorTests
{
	// Six lines indented one level and two lines indented two levels, so a size gets six votes.
	private static string Program(string unit)
	{
		return string.Join(
			"\n",
			"function a()",
			unit + "if x then",
			unit + unit + "b()",
			unit + unit + "c()",
			unit + "end",
			"end",
			"function d()",
			unit + "e()",
			"end"
		);
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	public void Detect_TabIndentedText_ShowsTabsAndLeavesTheSizeToTheDefault(bool defaultInsertSpaces)
	{
		IndentationDetection detection = IndentationDetector.Detect(Program("\t"), defaultInsertSpaces, defaultSize: 3);
		Assert.AreEqual(new IndentationDetection(InsertSpaces: false, Size: 3, InsertSpacesDetected: true, SizeDetected: false), detection);
	}

	[TestMethod]
	[DataRow("  ", 2)]
	[DataRow("    ", 4)]
	[DataRow("      ", 6)]
	[DataRow("        ", 8)]
	[DataRow("   ", 3)]
	[DataRow("     ", 5)]
	[DataRow("       ", 7)]
	public void Detect_SpaceIndentedText_ShowsTheSizeOfOneLevel(string unit, int expectedSize)
	{
		foreach (bool defaultInsertSpaces in new[] { true, false })
		{
			IndentationDetection detection = IndentationDetector.Detect(Program(unit), defaultInsertSpaces, defaultSize: 1);

			Assert.AreEqual(
				new IndentationDetection(InsertSpaces: true, Size: expectedSize, InsertSpacesDetected: true, SizeDetected: true),
				detection
			);
		}
	}

	[TestMethod]
	public void Detect_OneSpacePerLevel_ShowsSpacesButNoSize()
	{
		// A size of one is not a candidate, so the two-space lines show the style and no size.
		IndentationDetection detection = IndentationDetector.Detect(Program(" "), defaultInsertSpaces: true, defaultSize: 4);
		Assert.AreEqual(new IndentationDetection(InsertSpaces: true, Size: 4, InsertSpacesDetected: true, SizeDetected: false), detection);
	}

	[TestMethod]
	public void Detect_MostlyTabsWithAStraySpaceLine_ShowsTabs()
	{
		string text = Program("\t") + "\n   stray";

		IndentationDetection detection = IndentationDetector.Detect(text, defaultInsertSpaces: true, defaultSize: 4);

		Assert.IsFalse(detection.InsertSpaces);
		Assert.IsTrue(detection.InsertSpacesDetected);
	}

	[TestMethod]
	public void Detect_MostlySpacesWithAStrayTabLine_ShowsSpacesAndTheirSize()
	{
		string text = Program("    ") + "\n\tstray";

		IndentationDetection detection = IndentationDetector.Detect(text, defaultInsertSpaces: false, defaultSize: 8);

		Assert.AreEqual(new IndentationDetection(InsertSpaces: true, Size: 4, InsertSpacesDetected: true, SizeDetected: true), detection);
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	public void Detect_EqualTabAndSpaceLines_ShowNoStyle(bool defaultInsertSpaces)
	{
		const string text = "a\n\tb\n\tc\n  d\n  e";

		IndentationDetection detection = IndentationDetector.Detect(text, defaultInsertSpaces, defaultSize: 4);

		Assert.AreEqual(defaultInsertSpaces, detection.InsertSpaces);
		Assert.IsFalse(detection.InsertSpacesDetected);
	}

	[TestMethod]
	public void Detect_TabLineWithAlignedSpaces_CountsAsATabLine()
	{
		// The tab is the common prefix, so the four spaces of the continuation line would vote for a size, but a tab text keeps the default.
		const string text = "a\n\tfoo(1,\n\t    2)\n\tbar()\nb\n\tbaz()";

		IndentationDetection detection = IndentationDetector.Detect(text, defaultInsertSpaces: true, defaultSize: 3);

		Assert.AreEqual(new IndentationDetection(InsertSpaces: false, Size: 3, InsertSpacesDetected: true, SizeDetected: false), detection);
	}

	[TestMethod]
	public void Detect_ArgumentsAlignedToAParenthesis_DoNotChangeTheSize()
	{
		string text = Program("    ") + "\nlocal x = foo(a,\n              b)\nlocal y = foo(c,\n              d)\n" + "function g()\n    h()\nend";

		IndentationDetection detection = IndentationDetector.Detect(text, defaultInsertSpaces: false, defaultSize: 2);

		Assert.AreEqual(new IndentationDetection(InsertSpaces: true, Size: 4, InsertSpacesDetected: true, SizeDetected: true), detection);
	}

	[TestMethod]
	public void Detect_ContinuationAfterACommaAlignedToAToken_DoesNotVoteForASize()
	{
		const string text = "var a = 1,\n    b = 2;\nvar c = 3,\n    d = 4;\nvar e = 5,\n    f = 6;";

		IndentationDetection detection = IndentationDetector.Detect(text, defaultInsertSpaces: true, defaultSize: 2);

		Assert.AreEqual(new IndentationDetection(InsertSpaces: true, Size: 2, InsertSpacesDetected: true, SizeDetected: false), detection);
	}

	[TestMethod]
	public void Detect_ContinuationAlignedToATokenThatMatchesTheDefault_VotesForTheDefault()
	{
		const string text = "var a = 1,\n    b = 2;\nvar c = 3,\n    d = 4;\nvar e = 5,\n    f = 6;";

		IndentationDetection detection = IndentationDetector.Detect(text, defaultInsertSpaces: true, defaultSize: 4);

		Assert.AreEqual(new IndentationDetection(InsertSpaces: true, Size: 4, InsertSpacesDetected: true, SizeDetected: true), detection);
	}

	[TestMethod]
	public void Detect_FewerIndentedLinesThanTheEvidenceNeeds_ReturnsTheDefaults()
	{
		IndentationDetection detection = IndentationDetector.Detect("function a()\n    b()\nend", defaultInsertSpaces: false, defaultSize: 3);
		Assert.AreEqual(new IndentationDetection(InsertSpaces: false, Size: 3, InsertSpacesDetected: false, SizeDetected: false), detection);
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	public void Detect_OneTabLineAndNoSpaceLine_ShowsTabs(bool defaultInsertSpaces)
	{
		// A lone tab line is unanimous evidence, because nothing else indents the text.
		IndentationDetection detection = IndentationDetector.Detect("a\n\tb\nc", defaultInsertSpaces, defaultSize: 4);
		Assert.AreEqual(new IndentationDetection(InsertSpaces: false, Size: 4, InsertSpacesDetected: true, SizeDetected: false), detection);
	}

	[TestMethod]
	public void Detect_OneTabLineAndAOneSpaceLine_ShowsTabs()
	{
		// A line of one space is not a space-indented line, so the lone tab line is still unanimous.
		IndentationDetection detection = IndentationDetector.Detect("a\n\tb\n c\n", defaultInsertSpaces: true, defaultSize: 4);

		Assert.IsFalse(detection.InsertSpaces);
		Assert.IsTrue(detection.InsertSpacesDetected);
	}

	[TestMethod]
	public void Detect_OneTabLineAndOneSpaceLine_ShowNoStyle()
	{
		IndentationDetection detection = IndentationDetector.Detect("a\n\tb\n  c\n", defaultInsertSpaces: true, defaultSize: 4);

		Assert.IsTrue(detection.InsertSpaces);
		Assert.IsFalse(detection.InsertSpacesDetected);
	}

	[TestMethod]
	public void Detect_OneSpaceLine_ShowsNoStyleBecauseItCanBeAnAlignment()
	{
		IndentationDetection detection = IndentationDetector.Detect("a\n  b\nc\n", defaultInsertSpaces: false, defaultSize: 4);

		Assert.IsFalse(detection.InsertSpaces);
		Assert.IsFalse(detection.InsertSpacesDetected);
	}

	[TestMethod]
	public void Detect_TwoTabLinesAgainstOneSpaceLine_ShowTabs()
	{
		IndentationDetection detection = IndentationDetector.Detect("a\n\tb\n\tc\n  d\n", defaultInsertSpaces: true, defaultSize: 4);

		Assert.IsFalse(detection.InsertSpaces);
		Assert.IsTrue(detection.InsertSpacesDetected);
	}

	[TestMethod]
	public void Detect_TwoSpaceLinesAgainstOneTabLine_ShowSpaces()
	{
		IndentationDetection detection = IndentationDetector.Detect("a\n\tb\n  c\n  d\n", defaultInsertSpaces: false, defaultSize: 4);

		Assert.IsTrue(detection.InsertSpaces);
		Assert.IsTrue(detection.InsertSpacesDetected);
	}

	[TestMethod]
	public void Detect_OneVoteForTwo_ReportsNoSize()
	{
		// The size half needs two votes, and this text casts one for two, so the default of four stays although two has the only vote.
		IndentationDetection detection = IndentationDetector.Detect("a\n  b\n  c\n", defaultInsertSpaces: true, defaultSize: 4);
		Assert.AreEqual(new IndentationDetection(InsertSpaces: true, Size: 4, InsertSpacesDetected: true, SizeDetected: false), detection);
	}

	[TestMethod]
	public void Detect_AVoteForTwoThatIsTooThinToDetect_DoesNotOverrideADefaultOfFour()
	{
		IndentationDetection detection = IndentationDetector.Detect("a\n  b\n", defaultInsertSpaces: true, defaultSize: 4);

		Assert.AreEqual(4, detection.Size);
		Assert.IsFalse(detection.SizeDetected);
	}

	[TestMethod]
	[DataRow(4, 3)]
	[DataRow(4, 5)]
	[DataRow(6, 5)]
	[DataRow(6, 7)]
	[DataRow(8, 3)]
	[DataRow(8, 7)]
	[DataRow(2, 3)]
	[DataRow(2, 5)]
	public void Detect_EqualVotesForAnEvenAndAnOddSize_ShowTheEvenSize(int even, int odd)
	{
		string text = VotesText((even, 4), (odd, 4));

		IndentationDetection detection = IndentationDetector.Detect(text, defaultInsertSpaces: true, defaultSize: 1);

		Assert.AreEqual(even, detection.Size);
	}

	[TestMethod]
	[DataRow(2, 4)]
	[DataRow(4, 6)]
	[DataRow(6, 8)]
	[DataRow(2, 8)]
	[DataRow(3, 5)]
	[DataRow(5, 7)]
	[DataRow(3, 7)]
	public void Detect_EqualVotesForTwoSizesOfOneParity_ShowTheSmallerOne(int smaller, int larger)
	{
		// The larger size is listed first in the text, so only the candidate order decides between the two.
		string text = VotesText((larger, 4), (smaller, 4));

		IndentationDetection detection = IndentationDetector.Detect(text, defaultInsertSpaces: true, defaultSize: 1);

		Assert.AreEqual(smaller, detection.Size);
	}

	[TestMethod]
	public void Detect_ContinuationAlignedToATokenWhenTheDefaultIsTabs_DoesNotVoteEvenForTheDefaultSize()
	{
		const string text = "var a = 1,\n    b = 2;\nvar c = 3,\n    d = 4;\nvar e = 5,\n    f = 6;";

		IndentationDetection detection = IndentationDetector.Detect(text, defaultInsertSpaces: false, defaultSize: 4);

		Assert.AreEqual(new IndentationDetection(InsertSpaces: true, Size: 4, InsertSpacesDetected: true, SizeDetected: false), detection);
	}

	[TestMethod]
	[DataRow(3, 2, 2)]
	[DataRow(3, 1, 4)]
	[DataRow(4, 3, 2)]
	[DataRow(4, 2, 4)]
	[DataRow(6, 4, 2)]
	[DataRow(6, 3, 4)]
	[DataRow(9, 6, 2)]
	[DataRow(9, 5, 4)]
	public void Detect_TwoAgainstFour_NeedsTwoThirdsOfTheVotesOfFour(int fourVotes, int twoVotes, int expectedSize)
	{
		string text = VotesText((4, fourVotes), (2, twoVotes));

		IndentationDetection detection = IndentationDetector.Detect(text, defaultInsertSpaces: true, defaultSize: 8);

		Assert.AreEqual(expectedSize, detection.Size);
		Assert.IsTrue(detection.SizeDetected);
	}

	[TestMethod]
	public void Detect_TwoWithMoreVotesThanFour_ShowsTwo()
	{
		IndentationDetection detection = IndentationDetector.Detect(VotesText((4, 2), (2, 5)), defaultInsertSpaces: true, defaultSize: 8);
		Assert.AreEqual(2, detection.Size);
	}

	[TestMethod]
	[DataRow(9_998, true)]
	[DataRow(9_999, false)]
	public void Detect_ReadsExactlyTheFirstTenThousandLines(int plainLineCount, bool expectedDetected)
	{
		// The two space lines end the text, so the second one is line 10,000 in the first case and line 10,001 in the second.
		string text = string.Concat(Enumerable.Repeat("x\n", plainLineCount)) + "  a\n  b\n";

		IndentationDetection detection = IndentationDetector.Detect(text, defaultInsertSpaces: false, defaultSize: 4);

		Assert.AreEqual(expectedDetected, detection.InsertSpacesDetected);
	}

	[TestMethod]
	public void Detect_DocCommentLinesOfOneExtraSpace_DoNotChangeTheSize()
	{
		const string text =
			"class A\n{\n    /**\n     * Summary.\n     * More.\n     */\n    void M()\n    {\n        N();\n    }\n\n    /**\n     * Other.\n     */\n    void O() { }\n}\n";

		IndentationDetection detection = IndentationDetector.Detect(text, defaultInsertSpaces: false, defaultSize: 2);

		Assert.AreEqual(new IndentationDetection(InsertSpaces: true, Size: 4, InsertSpacesDetected: true, SizeDetected: true), detection);
	}

	[TestMethod]
	[DataRow("    a", "      b", 2, false, DisplayName = "spaces against spaces")]
	[DataRow("\t  a", "\t    b", 2, false, DisplayName = "the common tab prefix is skipped")]
	[DataRow("\ta", "\t\tb", 0, false, DisplayName = "two tabs against one tab differ by zero spaces")]
	[DataRow("a", "\t    b", 0, false, DisplayName = "a line that mixes a tab and spaces is rejected")]
	[DataRow("\t  a", "b", 0, false, DisplayName = "the previous line that mixes a tab and spaces is rejected")]
	[DataRow("a", "    b", 4, false, DisplayName = "four spaces against none")]
	[DataRow("\ta", "        b", 8, false, DisplayName = "one tab against eight spaces is eight spaces apart")]
	[DataRow("\ta", "b", 0, false, DisplayName = "one tab against none differs by zero spaces")]
	[DataRow("  a", "\t\t\tb", 0, false, DisplayName = "tabs against spaces that do not divide")]
	[DataRow("        a", "\tb", 8, false, DisplayName = "eight spaces against one tab are eight apart")]
	[DataRow("      a", "\t\tb", 3, false, DisplayName = "six spaces against two tabs are three apart")]
	[DataRow("var a = 1,", "    b = 2;", 4, true, DisplayName = "alignment after a comma")]
	[DataRow("var a = 1;", "    b = 2;", 4, false, DisplayName = "no comma ends the previous line")]
	[DataRow("var,x = 1,", "    b = 2;", 4, false, DisplayName = "the previous line has no space at the aligned column")]
	[DataRow("  x  = 1,", "      y = 2;", 4, false, DisplayName = "the current line has a space at the checked column")]
	[DataRow("a,", "    b", 4, false, DisplayName = "the previous line is shorter than the aligned column")]
	[DataRow("var a = 1,", "    ", 4, false, DisplayName = "the current line ends at the aligned column")]
	[DataRow("var a = 1,", "b", 0, false, DisplayName = "no difference is no alignment")]
	public void GetSizeDifference_ComparesTheIndentationOfTwoLines(string previousLine, string line, int expectedDifference, bool expectedAlignment)
	{
		int difference = IndentationDetector.GetSizeDifference(
			previousLine,
			previousIndentLength: LeadingIndentation.Measure(previousLine),
			line,
			indentLength: LeadingIndentation.Measure(line),
			out bool looksLikeAlignment
		);

		Assert.AreEqual(expectedDifference, difference);
		Assert.AreEqual(expectedAlignment, looksLikeAlignment);
	}

	// Builds a text whose line pairs cast the given number of votes for each size: one line per vote, stepping the indentation up or down by the size.
	private static string VotesText(params (int Size, int Votes)[] sizes)
	{
		var lines = new List<string> { "x" };
		int level = 0;

		foreach ((int size, int votes) in sizes)
		{
			for (int vote = 0; vote < votes; vote++)
			{
				level = level >= size ? level - size : level + size;
				lines.Add(new string(' ', level) + "x");
			}
		}

		return string.Join("\n", lines);
	}

	[TestMethod]
	public void Detect_SpaceTextWhenTabsAreTheDefault_ShowsStyleAndSize()
	{
		IndentationDetection detection = IndentationDetector.Detect(Program("  "), defaultInsertSpaces: false, defaultSize: 4);
		Assert.AreEqual(new IndentationDetection(InsertSpaces: true, Size: 2, InsertSpacesDetected: true, SizeDetected: true), detection);
	}

	[TestMethod]
	public void Detect_ThinSpaceEvidenceWhenTabsAreTheDefault_GuessesNoSize()
	{
		// The style stays tabs, and a tab text has no size to guess.
		IndentationDetection detection = IndentationDetector.Detect("a\n  b", defaultInsertSpaces: false, defaultSize: 6);
		Assert.AreEqual(new IndentationDetection(InsertSpaces: false, Size: 6, InsertSpacesDetected: false, SizeDetected: false), detection);
	}

	[TestMethod]
	public void Detect_EmptyText_ReturnsTheDefaults()
	{
		IndentationDetection detection = IndentationDetector.Detect(string.Empty, defaultInsertSpaces: true, defaultSize: 4);
		Assert.AreEqual(new IndentationDetection(InsertSpaces: true, Size: 4, InsertSpacesDetected: false, SizeDetected: false), detection);
	}

	[TestMethod]
	public void Detect_BlankAndWhitespaceOnlyLines_AreIgnored()
	{
		const string text = "\n   \n\t\n    \n\t \n";

		IndentationDetection detection = IndentationDetector.Detect(text, defaultInsertSpaces: true, defaultSize: 4);

		Assert.AreEqual(new IndentationDetection(InsertSpaces: true, Size: 4, InsertSpacesDetected: false, SizeDetected: false), detection);
	}

	[TestMethod]
	public void Detect_BlankLinesBetweenIndentedLines_DoNotChangeTheSize()
	{
		string text = Program("  ").Replace("\n", "\n\n   \n", StringComparison.Ordinal);

		IndentationDetection detection = IndentationDetector.Detect(text, defaultInsertSpaces: false, defaultSize: 4);

		Assert.AreEqual(new IndentationDetection(InsertSpaces: true, Size: 2, InsertSpacesDetected: true, SizeDetected: true), detection);
	}

	[TestMethod]
	public void Detect_ASizeOfTwoWithAtLeastHalfTheVotesOfFour_ShowsTwo()
	{
		// Four leads with six votes against four, but two has more than half of the votes of four.
		const string text = "a\n    b\nc\n    d\ne\n  f\ng\n  h\ni\n    j\nk";

		IndentationDetection detection = IndentationDetector.Detect(text, defaultInsertSpaces: true, defaultSize: 4);

		Assert.AreEqual(2, detection.Size);
		Assert.IsTrue(detection.SizeDetected);
	}

	[TestMethod]
	public void Detect_ASizeOfTwoWithLessThanHalfTheVotesOfFour_ShowsFour()
	{
		const string text = "a\n    b\nc\n    d\ne\n    f\ng\n    h\ni\n    j\nk\n  l\nm";

		IndentationDetection detection = IndentationDetector.Detect(text, defaultInsertSpaces: true, defaultSize: 8);

		Assert.AreEqual(4, detection.Size);
		Assert.IsTrue(detection.SizeDetected);
	}

	[TestMethod]
	[DataRow("\n")]
	[DataRow("\r\n")]
	[DataRow("\r")]
	public void Detect_AnyLineTerminator_GivesTheSameResult(string terminator)
	{
		string text = Program("  ").Replace("\n", terminator, StringComparison.Ordinal);

		IndentationDetection detection = IndentationDetector.Detect(text, defaultInsertSpaces: false, defaultSize: 4);

		Assert.AreEqual(new IndentationDetection(InsertSpaces: true, Size: 2, InsertSpacesDetected: true, SizeDetected: true), detection);
	}

	[TestMethod]
	public void Detect_IndentationAfterTheFirstTenThousandLines_IsNotRead()
	{
		string unindented = string.Concat(Enumerable.Repeat("x\n", 10_000));

		IndentationDetection late = IndentationDetector.Detect(unindented + Program("  "), defaultInsertSpaces: false, defaultSize: 4);
		IndentationDetection early = IndentationDetector.Detect(Program("  ") + "\n" + unindented, defaultInsertSpaces: false, defaultSize: 4);

		Assert.IsFalse(late.InsertSpacesDetected);
		Assert.IsFalse(late.SizeDetected);
		Assert.IsTrue(early.InsertSpacesDetected);
		Assert.IsTrue(early.SizeDetected);
	}

	[TestMethod]
	public void Detect_LargeText_AllocatesNothing()
	{
		string text = string.Concat(Enumerable.Repeat(Program("    ") + "\n", 1_000));

		_ = IndentationDetector.Detect(text, defaultInsertSpaces: true, defaultSize: 4);

		long before = GC.GetAllocatedBytesForCurrentThread();
		IndentationDetection detection = IndentationDetector.Detect(text, defaultInsertSpaces: true, defaultSize: 4);
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.AreEqual(4, detection.Size);
		Assert.AreEqual(0L, allocated);
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(-4)]
	[DataRow(1001)]
	public void Detect_DefaultSizeOutsideTheLimits_Throws(int defaultSize) =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => IndentationDetector.Detect("a", defaultInsertSpaces: true, defaultSize));
}
