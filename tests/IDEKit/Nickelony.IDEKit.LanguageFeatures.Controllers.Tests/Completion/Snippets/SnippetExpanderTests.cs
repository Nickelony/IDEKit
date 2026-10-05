using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Snippets;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
public sealed class SnippetExpanderTests
{
	[TestMethod]
	public void Expand_PlainText_ReturnsTheSameTextWithoutPlaceholders()
	{
		SnippetExpansion result = SnippetExpander.Expand("print('hello')");

		Assert.AreEqual("print('hello')", result.Text);
		Assert.IsEmpty(result.Placeholders);
	}

	[TestMethod]
	public void Expand_EmptyOrWhitespaceInput_ReturnsItUnchanged()
	{
		SnippetExpansion emptyResult = SnippetExpander.Expand(string.Empty);

		Assert.AreEqual(string.Empty, emptyResult.Text);
		Assert.IsEmpty(emptyResult.Placeholders);

		Assert.AreEqual("   ", SnippetExpander.Expand("   ").Text);
		Assert.IsEmpty(SnippetExpander.Expand("   ").Placeholders);
	}

	[TestMethod]
	public void Expand_NullInput_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => SnippetExpander.Expand(null!));

	[TestMethod]
	public void Expand_BareTabstops_RemovesThemAndRecordsEmptyRanges()
	{
		SnippetExpansion result = SnippetExpander.Expand("local $1 = $2");

		Assert.AreEqual("local  = ", result.Text);

		Assert.AreSequenceEqual(
			[new SnippetPlaceholder(1, new TextRange(6, 0), null), new SnippetPlaceholder(2, new TextRange(9, 0), null)],
			[.. result.Placeholders]
		);
	}

	[TestMethod]
	public void Expand_BracedTabstop_RecordsAnEmptyRange()
	{
		SnippetExpansion result = SnippetExpander.Expand("local ${1}value");

		Assert.AreEqual("local value", result.Text);
		Assert.AreSequenceEqual([new SnippetPlaceholder(1, new TextRange(6, 0), null)], [.. result.Placeholders]);
	}

	[TestMethod]
	public void Expand_MultiDigitTabstop_ParsesTheWholeDigitRun()
	{
		SnippetExpansion result = SnippetExpander.Expand("$10");

		Assert.AreEqual(string.Empty, result.Text);
		Assert.AreSequenceEqual([new SnippetPlaceholder(10, new TextRange(0, 0), null)], [.. result.Placeholders]);
	}

	[TestMethod]
	public void Expand_DefaultText_EmbedsTheDefaultAndCoversItWithTheRange()
	{
		SnippetExpansion result = SnippetExpander.Expand("spawn(${1:name})");

		Assert.AreEqual("spawn(name)", result.Text);
		Assert.AreSequenceEqual([new SnippetPlaceholder(1, new TextRange(6, 4), null)], [.. result.Placeholders]);
	}

	[TestMethod]
	public void Expand_NestedPlaceholders_RecordsOuterAndInnerRanges()
	{
		SnippetExpansion result = SnippetExpander.Expand("${1:foo ${2:bar}}");

		Assert.AreEqual("foo bar", result.Text);

		Assert.AreSequenceEqual(
			[new SnippetPlaceholder(1, new TextRange(0, 7), null), new SnippetPlaceholder(2, new TextRange(4, 3), null)],
			[.. result.Placeholders]
		);
	}

	[TestMethod]
	public void Expand_ChoicePlaceholder_InsertsTheFirstChoiceAndReportsAllChoices()
	{
		SnippetExpansion result = SnippetExpander.Expand("${1|first,second|}");

		Assert.AreEqual("first", result.Text);

		SnippetPlaceholder placeholder = result.Placeholders.Single();

		Assert.AreEqual(1, placeholder.Index);
		Assert.AreEqual(new TextRange(0, 5), placeholder.Range);
		Assert.AreSequenceEqual(["first", "second"], [.. placeholder.Choices!]);
	}

	[TestMethod]
	public void Expand_ChoiceWithEscapedSeparator_KeepsTheEscapedCharacterInTheChoice()
	{
		SnippetExpansion result = SnippetExpander.Expand("${1|a\\,b,c|}");

		Assert.AreEqual("a,b", result.Text);

		SnippetPlaceholder placeholder = result.Placeholders.Single();

		Assert.AreEqual(new TextRange(0, 3), placeholder.Range);
		Assert.AreSequenceEqual(["a,b", "c"], [.. placeholder.Choices!]);
	}

	[TestMethod]
	public void Expand_ChoiceWithEscapedPipe_KeepsThePipeInsideTheChoice()
	{
		SnippetExpansion result = SnippetExpander.Expand("${1|x\\|y,z|}");

		Assert.AreEqual("x|y", result.Text);

		SnippetPlaceholder placeholder = result.Placeholders.Single();
		Assert.AreSequenceEqual(["x|y", "z"], [.. placeholder.Choices!]);
	}

	[TestMethod]
	public void Expand_ChoiceWithEmptyElement_StaysVerbatim()
	{
		// VS Code keeps a choice with an empty element as plain text; the whole construct stays literal.
		SnippetExpansion result = SnippetExpander.Expand("${1||}");

		Assert.AreEqual("${1||}", result.Text);
		Assert.IsEmpty(result.Placeholders);
	}

	[TestMethod]
	public void Expand_ChoiceWithTrailingEmptyElement_StaysVerbatim()
	{
		SnippetExpansion result = SnippetExpander.Expand("${1|a,b,|}");

		Assert.AreEqual("${1|a,b,|}", result.Text);
		Assert.IsEmpty(result.Placeholders);
	}

	[TestMethod]
	public void Expand_ZeroIndexChoice_StaysVerbatim()
	{
		// VS Code ignores "${0|...|}" choices.
		SnippetExpansion result = SnippetExpander.Expand("${0|foo,bar|}");

		Assert.AreEqual("${0|foo,bar|}", result.Text);
		Assert.IsEmpty(result.Placeholders);
	}

	[TestMethod]
	public void Expand_UnsupportedBodyWithNestedConstruct_ResolvesTheNestedConstruct()
	{
		// A closed but unsupported braced body keeps its literal text while constructs inside it
		// still resolve.
		SnippetExpansion result = SnippetExpander.Expand("${x${1}}");

		Assert.AreEqual("${x}", result.Text);
		Assert.AreSequenceEqual([new SnippetPlaceholder(1, new TextRange(3, 0), null)], [.. result.Placeholders]);
	}

	[TestMethod]
	public void Expand_UnsupportedBodyWithEscapedCloseBrace_ResolvesTheEscape()
	{
		SnippetExpansion result = SnippetExpander.Expand("${a\\}b}");

		Assert.AreEqual("${a}b}", result.Text);
		Assert.IsEmpty(result.Placeholders);
	}

	[TestMethod]
	public void Expand_ChoiceBodyEscapedCloseBrace_KeepsTheBraceInTheChoice()
	{
		SnippetExpansion result = SnippetExpander.Expand("${1|a\\}b,c|}");

		Assert.AreEqual("a}b", result.Text);
		Assert.AreSequenceEqual(["a}b", "c"], [.. result.Placeholders.Single().Choices!]);
	}

	[TestMethod]
	public void Expand_EscapedDollarSign_KeepsTheLiteralDollar()
	{
		SnippetExpansion result = SnippetExpander.Expand("cost: \\$5");

		Assert.AreEqual("cost: $5", result.Text);
		Assert.IsEmpty(result.Placeholders);
	}

	[TestMethod]
	public void Expand_TrailingLoneBackslash_StaysLiteral()
	{
		SnippetExpansion result = SnippetExpander.Expand("tail\\");

		Assert.AreEqual("tail\\", result.Text);
		Assert.IsEmpty(result.Placeholders);
	}

	[TestMethod]
	public void Expand_EscapedBackslashAtDefaultEnd_ResolvesInsideThePlaceholder()
	{
		SnippetExpansion result = SnippetExpander.Expand("${1:tail\\\\}");

		Assert.AreEqual("tail\\", result.Text);
		Assert.AreSequenceEqual([new SnippetPlaceholder(1, new TextRange(0, 5), null)], [.. result.Placeholders]);
	}

	[TestMethod]
	public void Expand_ChoiceWithEscapedDollarSign_KeepsTheDollarInsideTheChoice()
	{
		SnippetExpansion result = SnippetExpander.Expand("${1|a\\$b,c|}");

		Assert.AreEqual("a$b", result.Text);
		Assert.AreSequenceEqual(["a$b", "c"], [.. result.Placeholders.Single().Choices!]);
	}

	[TestMethod]
	public void Expand_EscapedClosingBraceAndBackslash_ResolvesTheEscapes()
	{
		Assert.AreEqual("}", SnippetExpander.Expand("\\}").Text);
		Assert.AreEqual("a\\b", SnippetExpander.Expand("a\\\\b").Text);
		Assert.AreEqual("it is \\$5", SnippetExpander.Expand("it is \\\\\\$5").Text);
	}

	[TestMethod]
	public void Expand_EscapedPlaceholderStart_StaysLiteral()
	{
		SnippetExpansion result = SnippetExpander.Expand("\\${1:foo}");

		Assert.AreEqual("${1:foo}", result.Text);
		Assert.IsEmpty(result.Placeholders);
	}

	[TestMethod]
	public void Expand_LiteralOpeningBraceInsideDefaultText_IsPreserved()
	{
		SnippetExpansion result = SnippetExpander.Expand("${1:use { here}");

		Assert.AreEqual("use { here", result.Text);
		Assert.AreSequenceEqual([new SnippetPlaceholder(1, new TextRange(0, 10), null)], [.. result.Placeholders]);
	}

	[TestMethod]
	public void Expand_UnterminatedPlaceholder_StaysLiteral()
	{
		SnippetExpansion result = SnippetExpander.Expand("a${1:b");

		Assert.AreEqual("a${1:b", result.Text);
		Assert.IsEmpty(result.Placeholders);
	}

	[TestMethod]
	public void Expand_UnterminatedNestedConstruct_StaysLiteral()
	{
		// The unterminated outer construct is literal while the scan later reaches the inner close
		// brace; the nested construct does not become a placeholder either because its body is not
		// supported.
		SnippetExpansion result = SnippetExpander.Expand("${a${b}");

		Assert.AreEqual("${a${b}", result.Text);
		Assert.IsEmpty(result.Placeholders);
	}

	[TestMethod]
	public void Expand_BraceFreeFallback_ResolvesEscapesAndTabstops()
	{
		// No close brace follows, so every braced construct stays literal while escapes and unbraced
		// tabstops in the remainder still resolve.
		SnippetExpansion escaped = SnippetExpander.Expand("${a\\$1");

		Assert.AreEqual("${a$1", escaped.Text);
		Assert.IsEmpty(escaped.Placeholders);

		SnippetExpansion tabstop = SnippetExpander.Expand("${a$1");

		Assert.AreEqual("${a", tabstop.Text);

		SnippetPlaceholder placeholder = tabstop.Placeholders.Single();

		Assert.AreEqual(1, placeholder.Index);
		Assert.AreEqual(new TextRange(3, 0), placeholder.Range);
	}

	[TestMethod]
	public void Expand_UnknownPlaceholderName_StaysVerbatim()
	{
		SnippetExpansion result = SnippetExpander.Expand("call(${name}, ${0:done})");

		Assert.AreEqual("call(${name}, done)", result.Text);
		Assert.AreSequenceEqual([new SnippetPlaceholder(0, new TextRange(14, 4), null)], [.. result.Placeholders]);
	}

	[TestMethod]
	public void Expand_UnsupportedPlaceholderBody_StaysVerbatim()
	{
		SnippetExpansion result = SnippetExpander.Expand("${1x}");

		Assert.AreEqual("${1x}", result.Text);
		Assert.IsEmpty(result.Placeholders);

		// Text following the still-literal body is preserved unchanged.
		SnippetExpansion withFollowingText = SnippetExpander.Expand("${1x} tail");

		Assert.AreEqual("${1x} tail", withFollowingText.Text);
		Assert.IsEmpty(withFollowingText.Placeholders);
	}

	[TestMethod]
	public void Expand_ChoiceWithoutTerminatingPipe_StaysVerbatim()
	{
		SnippetExpansion result = SnippetExpander.Expand("${1|a,b}");

		Assert.AreEqual("${1|a,b}", result.Text);
		Assert.IsEmpty(result.Placeholders);
	}

	[TestMethod]
	public void Expand_UnrepresentableTabstopIndex_StaysLiteral()
	{
		Assert.AreEqual("$9999999999", SnippetExpander.Expand("$9999999999").Text);
		Assert.AreEqual("${9999999999}", SnippetExpander.Expand("${9999999999}").Text);
	}

	[TestMethod]
	public void Expand_MultipleFinalCaretMarkers_RecordsEveryOccurrence()
	{
		SnippetExpansion result = SnippetExpander.Expand("a$0b$0c");

		Assert.AreEqual("abc", result.Text);

		Assert.AreSequenceEqual(
			[new SnippetPlaceholder(0, new TextRange(1, 0), null), new SnippetPlaceholder(0, new TextRange(2, 0), null)],
			[.. result.Placeholders]
		);
	}

	[TestMethod]
	public void Expand_FinalCaretInsideNestedDefaultText_RecordsTheNestedPosition()
	{
		SnippetExpansion result = SnippetExpander.Expand("x${1:y$0z}");

		Assert.AreEqual("xyz", result.Text);

		Assert.AreSequenceEqual(
			[new SnippetPlaceholder(1, new TextRange(1, 2), null), new SnippetPlaceholder(0, new TextRange(2, 0), null)],
			[.. result.Placeholders]
		);
	}

	[TestMethod]
	public void Expand_DollarWithoutDigitsOrBrace_StaysLiteral()
	{
		Assert.AreEqual("$ x", SnippetExpander.Expand("$ x").Text);
		Assert.AreEqual("100% $", SnippetExpander.Expand("100% $").Text);
	}

	[TestMethod]
	public void Expand_MirroredTabstopIndex_RecordsSeparateOrderedEntries()
	{
		SnippetExpansion result = SnippetExpander.Expand("${1:foo} and $1");

		Assert.AreEqual("foo and ", result.Text);

		Assert.AreSequenceEqual(
			[new SnippetPlaceholder(1, new TextRange(0, 3), null), new SnippetPlaceholder(1, new TextRange(8, 0), null)],
			[.. result.Placeholders]
		);
	}

	[TestMethod]
	public void Expand_RealWorldMultiLineSnippet_PreservesVisibleTextAndTabstops()
	{
		SnippetExpansion result = SnippetExpander.Expand("if ${1:condition} then\n\t$0\nend");

		Assert.AreEqual("if condition then\n\t\nend", result.Text);

		Assert.AreSequenceEqual(
			[new SnippetPlaceholder(1, new TextRange(3, 9), null), new SnippetPlaceholder(0, new TextRange(19, 0), null)],
			[.. result.Placeholders]
		);
	}

	[TestMethod]
	public void Expand_EmptyBracedConstruct_StaysVerbatim()
	{
		SnippetExpansion result = SnippetExpander.Expand("a${}b");

		Assert.AreEqual("a${}b", result.Text);
		Assert.IsEmpty(result.Placeholders);
	}

	[TestMethod]
	public void Expand_EmptyDefaultText_RecordsAnEmptyPlaceholderRange()
	{
		SnippetExpansion result = SnippetExpander.Expand("x${1:}y");

		Assert.AreEqual("xy", result.Text);
		Assert.AreSequenceEqual([new SnippetPlaceholder(1, new TextRange(1, 0), null)], [.. result.Placeholders]);
	}

	[TestMethod]
	public void Expand_ChoiceNestedInsideDefaultText_RecordsBothPlaceholders()
	{
		SnippetExpansion result = SnippetExpander.Expand("${1:${2|a,b|}}");

		Assert.AreEqual("a", result.Text);
		Assert.HasCount(2, result.Placeholders);

		SnippetPlaceholder outer = result.Placeholders[0];

		Assert.AreEqual(1, outer.Index);
		Assert.AreEqual(new TextRange(0, 1), outer.Range);
		Assert.IsNull(outer.Choices);

		SnippetPlaceholder nestedChoice = result.Placeholders[1];

		Assert.AreEqual(2, nestedChoice.Index);
		Assert.AreEqual(new TextRange(0, 1), nestedChoice.Range);
		Assert.AreSequenceEqual(["a", "b"], [.. nestedChoice.Choices!]);
	}

	[TestMethod]
	public void Expand_ChoiceTextContainingCloseBrace_ExpandsTheWholeChoice()
	{
		// Inside a choice, only "|" terminates it: a bare '}' is plain choice text.
		SnippetExpansion result = SnippetExpander.Expand("${1|a}b|}");

		Assert.AreEqual("a}b", result.Text);
		Assert.HasCount(1, result.Placeholders);
		Assert.AreEqual(1, result.Placeholders[0].Index);
		Assert.AreEqual(new TextRange(0, 3), result.Placeholders[0].Range);
		Assert.AreSequenceEqual(["a}b"], [.. result.Placeholders[0].Choices!]);
	}

	[TestMethod]
	public void Expand_MalformedChoiceFollowedByValidChoice_KeepsTheFirstLiteral()
	{
		// The first construct has no "|" terminator of its own, so it stays literal while scanning
		// resumes inside it and the later choice still expands.
		SnippetExpansion result = SnippetExpander.Expand("${1|a} ${2|b,c|}");

		Assert.AreEqual("${1|a} b", result.Text);
		Assert.HasCount(1, result.Placeholders);
		Assert.AreEqual(2, result.Placeholders[0].Index);
		Assert.AreEqual(new TextRange(7, 1), result.Placeholders[0].Range);
	}

	[TestMethod]
	public void Expand_NestedConstructInsideChoiceText_StaysLiteral()
	{
		SnippetExpansion result = SnippetExpander.Expand("${1|a${2:x},b|}");

		Assert.AreEqual("a${2:x}", result.Text);
		Assert.HasCount(1, result.Placeholders);
		Assert.AreEqual(1, result.Placeholders[0].Index);
		Assert.AreEqual(new TextRange(0, 7), result.Placeholders[0].Range);
		Assert.AreSequenceEqual(["a${2:x}", "b"], [.. result.Placeholders[0].Choices!]);
	}

	[TestMethod]
	public void Expand_NestingDeeperThanTheLimit_StopsExpandingWithoutThrowing()
	{
		string nested = string.Concat(Enumerable.Repeat("${1:", 64)) + "x" + new string('}', 64);

		SnippetExpansion result = SnippetExpander.Expand(nested);

		Assert.HasCount(32, result.Placeholders);
		Assert.IsTrue(result.Text.Contains('x'));
		Assert.IsTrue(result.Text.EndsWith('}'));
	}

	[TestMethod]
	public void Expand_LeadingZeroTabstopIndex_IsParsedAsDecimal()
	{
		SnippetExpansion result = SnippetExpander.Expand("$01");

		Assert.AreEqual(string.Empty, result.Text);
		Assert.AreSequenceEqual([new SnippetPlaceholder(1, new TextRange(0, 0), null)], [.. result.Placeholders]);
	}

	[TestMethod]
	public void Expand_UnknownEscape_KeepsTheBackslash()
	{
		SnippetExpansion result = SnippetExpander.Expand(@"a\nb");

		Assert.AreEqual(@"a\nb", result.Text);
		Assert.IsEmpty(result.Placeholders);
	}

	[TestMethod]
	public void Expand_NonAsciiDigits_AreNotTabstopDigits()
	{
		// The grammar defines [0-9]+; other Unicode decimal digits are plain text.
		SnippetExpansion result = SnippetExpander.Expand("$\u0661");

		Assert.AreEqual("$\u0661", result.Text);
		Assert.IsEmpty(result.Placeholders);
	}

	[TestMethod]
	public void Expand_UnterminatedNestedChoice_KeepsOneConsistentBoundary()
	{
		// The close-brace scan and the expansion path agree on malformed nested choices: an
		// unterminated choice is not a construct, so the enclosing placeholder closes at the first
		// brace that is not part of a choice terminator.
		SnippetExpansion result = SnippetExpander.Expand("${1:${2|a}b}c}");

		Assert.AreEqual("${2|ab}c}", result.Text);
		Assert.AreSequenceEqual([new SnippetPlaceholder(1, new TextRange(0, 5), null)], [.. result.Placeholders]);
	}

	[TestMethod]
	public void Expand_BraceFreeRemainder_MatchesTheTolerantFallback()
	{
		// Unterminated constructs with no close brace anywhere in the remainder take the single-pass
		// fallback; the visible text and tabstops stay identical to per-construct rescanning.
		SnippetExpansion literal = SnippetExpander.Expand("$a${b${c");

		Assert.AreEqual("$a${b${c", literal.Text);
		Assert.IsEmpty(literal.Placeholders);

		SnippetExpansion mixed = SnippetExpander.Expand("$1${a");

		Assert.AreEqual("${a", mixed.Text);
		Assert.AreSequenceEqual([new SnippetPlaceholder(1, new TextRange(0, 0), null)], [.. mixed.Placeholders]);
	}
}
