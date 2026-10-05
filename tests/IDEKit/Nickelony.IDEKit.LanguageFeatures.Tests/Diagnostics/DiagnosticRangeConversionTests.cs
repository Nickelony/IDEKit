using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class DiagnosticRangeConversionTests
{
	// Lines: "alpha" at 0, "beta" at 6, "" at 11 and "delta" at 12; the text is 17 characters long.
	private static readonly TextLineMap s_lineMap = TextLineMap.Build("alpha\nbeta\n\ndelta");

	[TestMethod]
	public void FromLineAndCharacter_ColumnZeroWithoutLength_CoversTheWholeLine()
	{
		Assert.AreEqual(new TextRange(6, 4), DiagnosticRangeConversion.FromLineAndCharacter(s_lineMap, 1, 0));

		Assert.AreEqual(
			new TextRange(12, 5),
			DiagnosticRangeConversion.FromLineAndCharacter(lineMap: s_lineMap, lineIndex: 3, character: 0, length: null)
		);
	}

	[TestMethod]
	public void FromLineAndCharacter_ColumnWithoutLength_CoversTheRestOfTheLine() =>
		Assert.AreEqual(new TextRange(8, 2), DiagnosticRangeConversion.FromLineAndCharacter(s_lineMap, 1, 2));

	[TestMethod]
	public void FromLineAndCharacter_ColumnAndLength_CoversThatSpan()
	{
		Assert.AreEqual(
			new TextRange(1, 3),
			DiagnosticRangeConversion.FromLineAndCharacter(lineMap: s_lineMap, lineIndex: 0, character: 1, length: 3)
		);

		Assert.AreEqual(
			new TextRange(1, 0),
			DiagnosticRangeConversion.FromLineAndCharacter(lineMap: s_lineMap, lineIndex: 0, character: 1, length: 0)
		);
	}

	[TestMethod]
	public void FromLineAndCharacter_LengthPastTheLine_StopsAtTheLineEnd()
	{
		Assert.AreEqual(
			new TextRange(3, 2),
			DiagnosticRangeConversion.FromLineAndCharacter(lineMap: s_lineMap, lineIndex: 0, character: 3, length: 100)
		);

		Assert.AreEqual(
			new TextRange(0, 5),
			DiagnosticRangeConversion.FromLineAndCharacter(lineMap: s_lineMap, lineIndex: 0, character: 0, length: int.MaxValue)
		);
	}

	[TestMethod]
	public void FromLineAndCharacter_LinePastTheText_IsAnEmptyRangeAtTheEndOfTheText()
	{
		Assert.AreEqual(new TextRange(17, 0), DiagnosticRangeConversion.FromLineAndCharacter(s_lineMap, 4, 0));

		Assert.AreEqual(
			new TextRange(17, 0),
			DiagnosticRangeConversion.FromLineAndCharacter(lineMap: s_lineMap, lineIndex: 40, character: 3, length: 2)
		);

		Assert.AreEqual(new TextRange(0, 0), DiagnosticRangeConversion.FromLineAndCharacter(TextLineMap.Build(string.Empty), 1, 0));
	}

	[TestMethod]
	public void FromLineAndCharacter_ColumnPastTheLine_IsAnEmptyRangeAtTheEndOfTheLine()
	{
		Assert.AreEqual(new TextRange(10, 0), DiagnosticRangeConversion.FromLineAndCharacter(s_lineMap, 1, 4));

		Assert.AreEqual(
			new TextRange(10, 0),
			DiagnosticRangeConversion.FromLineAndCharacter(lineMap: s_lineMap, lineIndex: 1, character: 9, length: 3)
		);

		Assert.AreEqual(
			new TextRange(10, 0),
			DiagnosticRangeConversion.FromLineAndCharacter(lineMap: s_lineMap, lineIndex: 1, character: 99, length: null)
		);
	}

	[TestMethod]
	public void FromLineAndCharacter_EmptyLine_IsAnEmptyRangeAtTheLineStart()
	{
		Assert.AreEqual(new TextRange(11, 0), DiagnosticRangeConversion.FromLineAndCharacter(s_lineMap, 2, 0));

		Assert.AreEqual(
			new TextRange(11, 0),
			DiagnosticRangeConversion.FromLineAndCharacter(lineMap: s_lineMap, lineIndex: 2, character: 3, length: 2)
		);
	}

	[TestMethod]
	public void FromLineAndCharacter_TerminatorsOfEveryKind_AreExcludedFromTheRange()
	{
		var lineMap = TextLineMap.Build("ab\r\ncd\refg");

		Assert.AreEqual(new TextRange(0, 2), DiagnosticRangeConversion.FromLineAndCharacter(lineMap, 0, 0));
		Assert.AreEqual(new TextRange(4, 2), DiagnosticRangeConversion.FromLineAndCharacter(lineMap, 1, 0));
		Assert.AreEqual(new TextRange(7, 3), DiagnosticRangeConversion.FromLineAndCharacter(lineMap, 2, 0));
	}

	[TestMethod]
	public void FromLineAndCharacter_TextEndingInATerminator_HasAnEmptyLastLine()
	{
		var lineMap = TextLineMap.Build("ab\n");

		Assert.AreEqual(new TextRange(3, 0), DiagnosticRangeConversion.FromLineAndCharacter(lineMap, 1, 0));
		Assert.AreEqual(new TextRange(3, 0), DiagnosticRangeConversion.FromLineAndCharacter(lineMap, 2, 0));
	}

	[TestMethod]
	public void FromLineAndCharacter_NullLineMap_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => DiagnosticRangeConversion.FromLineAndCharacter(null!, 0, 0));

	[TestMethod]
	public void FromLineAndCharacter_NegativeCoordinates_ClampToZero()
	{
		// A host numbers a problem that has no line as -1, and one bad coordinate must not fail the diagnostics of a whole project.
		Assert.AreEqual(new TextRange(0, 5), DiagnosticRangeConversion.FromLineAndCharacter(s_lineMap, -2, 0));
		Assert.AreEqual(new TextRange(0, 5), DiagnosticRangeConversion.FromLineAndCharacter(s_lineMap, int.MinValue, -3));
		Assert.AreEqual(new TextRange(6, 4), DiagnosticRangeConversion.FromLineAndCharacter(s_lineMap, 1, -1));

		Assert.AreEqual(
			new TextRange(8, 0),
			DiagnosticRangeConversion.FromLineAndCharacter(lineMap: s_lineMap, lineIndex: 1, character: 2, length: -4)
		);
	}
}
