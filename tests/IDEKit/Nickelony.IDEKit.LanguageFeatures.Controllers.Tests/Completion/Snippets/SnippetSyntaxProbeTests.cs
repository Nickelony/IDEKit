using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Snippets;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
public sealed class SnippetSyntaxProbeTests
{
	[TestMethod]
	public void IsEscapeAt_BackslashWithACharacterAfterIt_ReturnsTrue()
	{
		const string snippet = "a\\$b";
		Assert.IsTrue(SnippetSyntaxProbe.IsEscapeAt(snippet, 1, snippet.Length));
	}

	[TestMethod]
	public void IsEscapeAt_TrailingBackslash_ReturnsFalse()
	{
		const string snippet = "ab\\";
		Assert.IsFalse(SnippetSyntaxProbe.IsEscapeAt(snippet, 2, snippet.Length));
	}

	[TestMethod]
	public void IsEscapeAt_BackslashWhoseNextCharacterIsOutsideTheRange_ReturnsFalse()
	{
		const string snippet = "a\\}b";
		Assert.IsFalse(SnippetSyntaxProbe.IsEscapeAt(snippet, 1, endIndex: 2));
	}

	[TestMethod]
	public void IsEscapeAt_ACharacterThatIsNotABackslash_ReturnsFalse()
	{
		const string snippet = "a$b";
		Assert.IsFalse(SnippetSyntaxProbe.IsEscapeAt(snippet, 1, snippet.Length));
	}

	[TestMethod]
	public void EscapesCloseBrace_AnEscapedCloseBrace_ReturnsTrue() => Assert.IsTrue(SnippetSyntaxProbe.EscapesCloseBrace("a\\}", 1));

	[TestMethod]
	public void EscapesCloseBrace_AnEscapedDollarSign_ReturnsFalse() => Assert.IsFalse(SnippetSyntaxProbe.EscapesCloseBrace("a\\$", 1));

	[TestMethod]
	public void EscapesCloseBrace_AnEscapedBackslashBeforeACloseBrace_ReturnsFalse() =>
		Assert.IsFalse(SnippetSyntaxProbe.EscapesCloseBrace("\\\\}", 0));

	[TestMethod]
	public void IsBraceOpenAt_ADollarSignBeforeAnOpenBrace_ReturnsTrue() => Assert.IsTrue(SnippetSyntaxProbe.IsBraceOpenAt("a${1}", 1, 5));

	[TestMethod]
	public void IsBraceOpenAt_ANestedConstruct_ReturnsTrueAtTheInnerDollarSign()
	{
		const string snippet = "${1:${2:x}}";

		Assert.IsTrue(SnippetSyntaxProbe.IsBraceOpenAt(snippet, 0, snippet.Length));
		Assert.IsTrue(SnippetSyntaxProbe.IsBraceOpenAt(snippet, 4, snippet.Length));
	}

	[TestMethod]
	public void IsBraceOpenAt_ATrailingDollarSign_ReturnsFalse() => Assert.IsFalse(SnippetSyntaxProbe.IsBraceOpenAt("cost$", 4, 5));

	[TestMethod]
	public void IsBraceOpenAt_AnOpenBraceOutsideTheRange_ReturnsFalse() => Assert.IsFalse(SnippetSyntaxProbe.IsBraceOpenAt("a${1}", 1, endIndex: 2));

	[TestMethod]
	public void IsBraceOpenAt_ADollarSignBeforeADigit_ReturnsFalse() => Assert.IsFalse(SnippetSyntaxProbe.IsBraceOpenAt("$1", 0, 2));

	[TestMethod]
	public void IsBraceOpenAt_AnOpenBraceWithoutADollarSign_ReturnsFalse() => Assert.IsFalse(SnippetSyntaxProbe.IsBraceOpenAt("{1}", 0, 3));

	[TestMethod]
	public void IsChoiceHeadAt_ADigitRunBeforeABar_ReturnsTrueWithTheEndOfTheRun()
	{
		Assert.IsTrue(SnippetSyntaxProbe.IsChoiceHeadAt(snippet: "${1|a,b|}", contentStartIndex: 2, endIndex: 9, out int digitEndIndex));
		Assert.AreEqual(3, digitEndIndex);
	}

	[TestMethod]
	public void IsChoiceHeadAt_AMultiDigitRun_ReturnsTrueWithTheEndOfTheWholeRun()
	{
		Assert.IsTrue(SnippetSyntaxProbe.IsChoiceHeadAt(snippet: "${12|a|}", contentStartIndex: 2, endIndex: 8, out int digitEndIndex));
		Assert.AreEqual(4, digitEndIndex);
	}

	[TestMethod]
	public void IsChoiceHeadAt_AnIndexThatDoesNotFitInAnInt_StillReturnsTrue() =>
		Assert.IsTrue(SnippetSyntaxProbe.IsChoiceHeadAt(snippet: "${99999999999|a|}", contentStartIndex: 2, endIndex: 17, digitEndIndex: out _));

	[TestMethod]
	public void IsChoiceHeadAt_NoDigits_ReturnsFalse() =>
		Assert.IsFalse(SnippetSyntaxProbe.IsChoiceHeadAt(snippet: "${|a|}", contentStartIndex: 2, endIndex: 6, digitEndIndex: out _));

	[TestMethod]
	public void IsChoiceHeadAt_ADigitRunBeforeAColon_ReturnsFalse() =>
		Assert.IsFalse(SnippetSyntaxProbe.IsChoiceHeadAt(snippet: "${1:a}", contentStartIndex: 2, endIndex: 6, digitEndIndex: out _));

	[TestMethod]
	public void IsChoiceHeadAt_ADigitRunThatReachesTheEndOfTheRange_ReturnsFalse() =>
		Assert.IsFalse(SnippetSyntaxProbe.IsChoiceHeadAt(snippet: "${1|a|}", contentStartIndex: 2, endIndex: 3, digitEndIndex: out _));

	[TestMethod]
	public void IsChoiceTerminatorAt_ABarBeforeACloseBrace_ReturnsTrue() => Assert.IsTrue(SnippetSyntaxProbe.IsChoiceTerminatorAt("a|}", 1, 3));

	[TestMethod]
	public void IsChoiceTerminatorAt_ABarBeforeAnotherCharacter_ReturnsFalse() =>
		Assert.IsFalse(SnippetSyntaxProbe.IsChoiceTerminatorAt("a|b}", 1, 4));

	[TestMethod]
	public void IsChoiceTerminatorAt_ATrailingBar_ReturnsFalse() => Assert.IsFalse(SnippetSyntaxProbe.IsChoiceTerminatorAt("a|", 1, 2));

	[TestMethod]
	public void IsChoiceTerminatorAt_ACloseBraceOutsideTheRange_ReturnsFalse() =>
		Assert.IsFalse(SnippetSyntaxProbe.IsChoiceTerminatorAt("a|}", 1, endIndex: 2));

	[TestMethod]
	public void IsChoiceTerminatorAt_ACloseBraceWithoutABar_ReturnsFalse() => Assert.IsFalse(SnippetSyntaxProbe.IsChoiceTerminatorAt("a}}", 1, 3));

	[TestMethod]
	public void TryReadIndex_ASingleDigit_ReturnsItsValue()
	{
		Assert.IsTrue(SnippetSyntaxProbe.TryReadIndex(snippet: "$1x", startIndex: 1, endIndex: 3, out int digitEndIndex, out int value));
		Assert.AreEqual(2, digitEndIndex);
		Assert.AreEqual(1, value);
	}

	[TestMethod]
	public void TryReadIndex_AMultiDigitRun_ReturnsTheWholeNumber()
	{
		Assert.IsTrue(SnippetSyntaxProbe.TryReadIndex(snippet: "$120x", startIndex: 1, endIndex: 5, out int digitEndIndex, out int value));
		Assert.AreEqual(4, digitEndIndex);
		Assert.AreEqual(120, value);
	}

	[TestMethod]
	public void TryReadIndex_LeadingZeros_AreIgnoredByTheValue()
	{
		Assert.IsTrue(SnippetSyntaxProbe.TryReadIndex(snippet: "$007", startIndex: 1, endIndex: 4, out int digitEndIndex, out int value));
		Assert.AreEqual(4, digitEndIndex);
		Assert.AreEqual(7, value);
	}

	[TestMethod]
	public void TryReadIndex_ZeroIsAnIndex()
	{
		Assert.IsTrue(SnippetSyntaxProbe.TryReadIndex(snippet: "$0", startIndex: 1, endIndex: 2, digitEndIndex: out _, out int value));
		Assert.AreEqual(0, value);
	}

	[TestMethod]
	public void TryReadIndex_NoDigits_ReturnsFalse()
	{
		Assert.IsFalse(SnippetSyntaxProbe.TryReadIndex(snippet: "$x", startIndex: 1, endIndex: 2, out int digitEndIndex, value: out _));
		Assert.AreEqual(1, digitEndIndex);
	}

	[TestMethod]
	public void TryReadIndex_StartAtTheEndOfTheRange_ReturnsFalse() =>
		Assert.IsFalse(SnippetSyntaxProbe.TryReadIndex(snippet: "a$", startIndex: 2, endIndex: 2, digitEndIndex: out _, value: out _));

	[TestMethod]
	public void TryReadIndex_TheLargestIntIndex_ReturnsTrue()
	{
		Assert.IsTrue(SnippetSyntaxProbe.TryReadIndex(snippet: "$2147483647", startIndex: 1, endIndex: 11, digitEndIndex: out _, out int value));
		Assert.AreEqual(int.MaxValue, value);
	}

	[TestMethod]
	public void TryReadIndex_OneAboveTheLargestIntIndex_ReturnsFalseAndStillReportsTheRun()
	{
		Assert.IsFalse(SnippetSyntaxProbe.TryReadIndex(snippet: "$2147483648", startIndex: 1, endIndex: 11, out int digitEndIndex, value: out _));
		Assert.AreEqual(11, digitEndIndex);
	}

	[TestMethod]
	public void TryReadIndex_ANonAsciiDigit_IsNotAnIndex() =>
		Assert.IsFalse(SnippetSyntaxProbe.TryReadIndex(snippet: "$٣", startIndex: 1, endIndex: 2, digitEndIndex: out _, value: out _));

	[TestMethod]
	public void TryReadIndex_ARunCutByTheEndOfTheRange_ReadsOnlyInsideTheRange()
	{
		Assert.IsTrue(SnippetSyntaxProbe.TryReadIndex(snippet: "$123", startIndex: 1, endIndex: 3, out int digitEndIndex, out int value));
		Assert.AreEqual(3, digitEndIndex);
		Assert.AreEqual(12, value);
	}

	[TestMethod]
	public void ScanDigits_StopsAtTheFirstNonDigit() => Assert.AreEqual(4, SnippetSyntaxProbe.ScanDigits("a123b", 1, 5));

	[TestMethod]
	public void ScanDigits_AcceptsTheBoundaryDigits() => Assert.AreEqual(2, SnippetSyntaxProbe.ScanDigits("09/", 0, 3));

	[TestMethod]
	public void ScanDigits_RejectsTheCharactersAroundTheDigitRange() => Assert.AreEqual(0, SnippetSyntaxProbe.ScanDigits("/:", 0, 2));

	[TestMethod]
	public void ScanDigits_RejectsAnotherScriptsDigits() => Assert.AreEqual(0, SnippetSyntaxProbe.ScanDigits("٣", 0, 1));

	[TestMethod]
	public void ScanDigits_StopsAtTheEndOfTheRange() => Assert.AreEqual(2, SnippetSyntaxProbe.ScanDigits("1234", 0, 2));

	[TestMethod]
	public void TryParseIndex_AboveTheLargestIntIndex_ReturnsFalse()
	{
		Assert.IsFalse(SnippetSyntaxProbe.TryParseIndex(snippet: "99999999999", startIndex: 0, endIndex: 11, out int value));
		Assert.AreEqual(0, value);
	}

	[TestMethod]
	public void TryParseIndex_AnEmptyRun_ParsesAsZero()
	{
		Assert.IsTrue(SnippetSyntaxProbe.TryParseIndex(snippet: "x", startIndex: 0, endIndex: 0, out int value));
		Assert.AreEqual(0, value);
	}
}
