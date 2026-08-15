using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

[TestClass]
public sealed class TextSnippetExpanderTests
{
	[TestMethod]
	public void Expand_PlainText_ReturnsTheSameTextWithoutPlaceholders()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("print('hello')");

		Assert.AreEqual("print('hello')", result.Text);
		Assert.AreEqual(0, result.Placeholders.Count);
	}

	[TestMethod]
	public void Expand_EmptyOrWhitespaceInput_ReturnsItUnchanged()
	{
		TextSnippetExpansion emptyResult = TextSnippetExpander.Expand(string.Empty);

		Assert.AreEqual(string.Empty, emptyResult.Text);
		Assert.AreEqual(0, emptyResult.Placeholders.Count);

		Assert.AreEqual("   ", TextSnippetExpander.Expand("   ").Text);
		Assert.AreEqual(0, TextSnippetExpander.Expand("   ").Placeholders.Count);
	}

	[TestMethod]
	public void Expand_NullInput_Throws()
		=> Assert.ThrowsExactly<ArgumentNullException>(() => TextSnippetExpander.Expand(null!));

	[TestMethod]
	public void Expand_BareTabstops_RemovesThemAndRecordsEmptyRanges()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("local $1 = $2");

		Assert.AreEqual("local  = ", result.Text);
		CollectionAssert.AreEqual(
			new[]
			{
				new TextSnippetPlaceholder(1, new TextRange(6, 0), null),
				new TextSnippetPlaceholder(2, new TextRange(9, 0), null)
			},
			result.Placeholders.ToArray());
	}

	[TestMethod]
	public void Expand_BracedTabstop_RecordsAnEmptyRange()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("local ${1}value");

		Assert.AreEqual("local value", result.Text);
		CollectionAssert.AreEqual(
			new[] { new TextSnippetPlaceholder(1, new TextRange(6, 0), null) },
			result.Placeholders.ToArray());
	}

	[TestMethod]
	public void Expand_MultiDigitTabstop_ParsesTheWholeDigitRun()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("$10");

		Assert.AreEqual(string.Empty, result.Text);
		CollectionAssert.AreEqual(
			new[] { new TextSnippetPlaceholder(10, new TextRange(0, 0), null) },
			result.Placeholders.ToArray());
	}

	[TestMethod]
	public void Expand_DefaultText_EmbedsTheDefaultAndCoversItWithTheRange()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("spawn(${1:name})");

		Assert.AreEqual("spawn(name)", result.Text);
		CollectionAssert.AreEqual(
			new[] { new TextSnippetPlaceholder(1, new TextRange(6, 4), null) },
			result.Placeholders.ToArray());
	}

	[TestMethod]
	public void Expand_NestedPlaceholders_RecordsOuterAndInnerRanges()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("${1:foo ${2:bar}}");

		Assert.AreEqual("foo bar", result.Text);
		CollectionAssert.AreEqual(
			new[]
			{
				new TextSnippetPlaceholder(1, new TextRange(0, 7), null),
				new TextSnippetPlaceholder(2, new TextRange(4, 3), null)
			},
			result.Placeholders.ToArray());
	}

	[TestMethod]
	public void Expand_ChoicePlaceholder_InsertsTheFirstChoiceAndReportsAllChoices()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("${1|first,second|}");

		Assert.AreEqual("first", result.Text);

		TextSnippetPlaceholder placeholder = result.Placeholders.Single();
		Assert.AreEqual(1, placeholder.Index);
		Assert.AreEqual(new TextRange(0, 5), placeholder.Range);
		CollectionAssert.AreEqual(new[] { "first", "second" }, placeholder.Choices!.ToArray());
	}

	[TestMethod]
	public void Expand_ChoiceWithEscapedSeparator_KeepsTheEscapedCharacterInTheChoice()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("${1|a\\,b,c|}");

		Assert.AreEqual("a,b", result.Text);

		TextSnippetPlaceholder placeholder = result.Placeholders.Single();
		Assert.AreEqual(new TextRange(0, 3), placeholder.Range);
		CollectionAssert.AreEqual(new[] { "a,b", "c" }, placeholder.Choices!.ToArray());
	}

	[TestMethod]
	public void Expand_ChoiceWithEscapedPipe_KeepsThePipeInsideTheChoice()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("${1|x\\|y,z|}");

		Assert.AreEqual("x|y", result.Text);

		TextSnippetPlaceholder placeholder = result.Placeholders.Single();
		CollectionAssert.AreEqual(new[] { "x|y", "z" }, placeholder.Choices!.ToArray());
	}

	[TestMethod]
	public void Expand_ChoiceWithEmptyElement_StaysVerbatim()
	{
		// VS Code keeps a choice with an empty element as plain text; the whole construct stays literal.
		TextSnippetExpansion result = TextSnippetExpander.Expand("${1||}");

		Assert.AreEqual("${1||}", result.Text);
		Assert.AreEqual(0, result.Placeholders.Count);
	}

	[TestMethod]
	public void Expand_ChoiceWithTrailingEmptyElement_StaysVerbatim()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("${1|a,b,|}");

		Assert.AreEqual("${1|a,b,|}", result.Text);
		Assert.AreEqual(0, result.Placeholders.Count);
	}

	[TestMethod]
	public void Expand_ZeroIndexChoice_StaysVerbatim()
	{
		// VS Code ignores "${0|...|}" choices (issue #31599).
		TextSnippetExpansion result = TextSnippetExpander.Expand("${0|foo,bar|}");

		Assert.AreEqual("${0|foo,bar|}", result.Text);
		Assert.AreEqual(0, result.Placeholders.Count);
	}

	[TestMethod]
	public void Expand_UnsupportedBodyWithNestedConstruct_ResolvesTheNestedConstruct()
	{
		// A closed but unsupported braced body keeps its literal text while constructs inside it
		// still resolve.
		TextSnippetExpansion result = TextSnippetExpander.Expand("${x${1}}");

		Assert.AreEqual("${x}", result.Text);
		CollectionAssert.AreEqual(
			new[] { new TextSnippetPlaceholder(1, new TextRange(3, 0), null) },
			result.Placeholders.ToArray());
	}

	[TestMethod]
	public void Expand_UnsupportedBodyWithEscapedCloseBrace_ResolvesTheEscape()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("${a\\}b}");

		Assert.AreEqual("${a}b}", result.Text);
		Assert.AreEqual(0, result.Placeholders.Count);
	}

	[TestMethod]
	public void Expand_ChoiceBodyEscapedCloseBrace_KeepsTheBraceInTheChoice()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("${1|a\\}b,c|}");

		Assert.AreEqual("a}b", result.Text);
		CollectionAssert.AreEqual(new[] { "a}b", "c" }, result.Placeholders.Single().Choices!.ToArray());
	}

	[TestMethod]
	public void Expand_EscapedDollarSign_KeepsTheLiteralDollar()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("cost: \\$5");

		Assert.AreEqual("cost: $5", result.Text);
		Assert.AreEqual(0, result.Placeholders.Count);
	}

	[TestMethod]
	public void Expand_TrailingLoneBackslash_StaysLiteral()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("tail\\");

		Assert.AreEqual("tail\\", result.Text);
		Assert.AreEqual(0, result.Placeholders.Count);
	}

	[TestMethod]
	public void Expand_EscapedBackslashAtDefaultEnd_ResolvesInsideThePlaceholder()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("${1:tail\\\\}");

		Assert.AreEqual("tail\\", result.Text);
		CollectionAssert.AreEqual(
			new[] { new TextSnippetPlaceholder(1, new TextRange(0, 5), null) },
			result.Placeholders.ToArray());
	}

	[TestMethod]
	public void Expand_ChoiceWithEscapedDollarSign_KeepsTheDollarInsideTheChoice()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("${1|a\\$b,c|}");

		Assert.AreEqual("a$b", result.Text);
		CollectionAssert.AreEqual(new[] { "a$b", "c" }, result.Placeholders.Single().Choices!.ToArray());
	}

	[TestMethod]
	public void Expand_EscapedClosingBraceAndBackslash_ResolvesTheEscapes()
	{
		Assert.AreEqual("}", TextSnippetExpander.Expand("\\}").Text);
		Assert.AreEqual("a\\b", TextSnippetExpander.Expand("a\\\\b").Text);
		Assert.AreEqual("it is \\$5", TextSnippetExpander.Expand("it is \\\\\\$5").Text);
	}

	[TestMethod]
	public void Expand_EscapedPlaceholderStart_StaysLiteral()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("\\${1:foo}");

		Assert.AreEqual("${1:foo}", result.Text);
		Assert.AreEqual(0, result.Placeholders.Count);
	}

	[TestMethod]
	public void Expand_LiteralOpeningBraceInsideDefaultText_IsPreserved()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("${1:use { here}");

		Assert.AreEqual("use { here", result.Text);
		CollectionAssert.AreEqual(
			new[] { new TextSnippetPlaceholder(1, new TextRange(0, 10), null) },
			result.Placeholders.ToArray());
	}

	[TestMethod]
	public void Expand_UnterminatedPlaceholder_StaysLiteral()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("a${1:b");

		Assert.AreEqual("a${1:b", result.Text);
		Assert.AreEqual(0, result.Placeholders.Count);
	}

	[TestMethod]
	public void Expand_UnterminatedNestedConstruct_StaysLiteral()
	{
		// The unterminated outer construct is literal while the scan later reaches the inner close
		// brace; the nested construct does not become a placeholder either because its body is not
		// supported.
		TextSnippetExpansion result = TextSnippetExpander.Expand("${a${b}");

		Assert.AreEqual("${a${b}", result.Text);
		Assert.AreEqual(0, result.Placeholders.Count);
	}

	[TestMethod]
	public void Expand_BraceFreeFallback_ResolvesEscapesAndTabstops()
	{
		// No close brace follows, so every braced construct stays literal while escapes and unbraced
		// tabstops in the remainder still resolve.
		TextSnippetExpansion escaped = TextSnippetExpander.Expand("${a\\$1");

		Assert.AreEqual("${a$1", escaped.Text);
		Assert.AreEqual(0, escaped.Placeholders.Count);

		TextSnippetExpansion tabstop = TextSnippetExpander.Expand("${a$1");

		Assert.AreEqual("${a", tabstop.Text);
		TextSnippetPlaceholder placeholder = tabstop.Placeholders.Single();
		Assert.AreEqual(1, placeholder.Index);
		Assert.AreEqual(new TextRange(3, 0), placeholder.Range);
	}

	[TestMethod]
	public void Expand_UnknownPlaceholderName_StaysVerbatim()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("call(${name}, ${0:done})");

		Assert.AreEqual("call(${name}, done)", result.Text);
		CollectionAssert.AreEqual(
			new[] { new TextSnippetPlaceholder(0, new TextRange(14, 4), null) },
			result.Placeholders.ToArray());
	}

	[TestMethod]
	public void Expand_UnsupportedPlaceholderBody_StaysVerbatim()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("${1x}");

		Assert.AreEqual("${1x}", result.Text);
		Assert.AreEqual(0, result.Placeholders.Count);

		// Text following the still-literal body is preserved unchanged.
		TextSnippetExpansion withFollowingText = TextSnippetExpander.Expand("${1x} tail");

		Assert.AreEqual("${1x} tail", withFollowingText.Text);
		Assert.AreEqual(0, withFollowingText.Placeholders.Count);
	}

	[TestMethod]
	public void Expand_ChoiceWithoutTerminatingPipe_StaysVerbatim()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("${1|a,b}");

		Assert.AreEqual("${1|a,b}", result.Text);
		Assert.AreEqual(0, result.Placeholders.Count);
	}

	[TestMethod]
	public void Expand_UnrepresentableTabstopIndex_StaysLiteral()
	{
		Assert.AreEqual("$9999999999", TextSnippetExpander.Expand("$9999999999").Text);
		Assert.AreEqual("${9999999999}", TextSnippetExpander.Expand("${9999999999}").Text);
	}

	[TestMethod]
	public void Expand_MultipleFinalCaretMarkers_RecordsEveryOccurrence()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("a$0b$0c");

		Assert.AreEqual("abc", result.Text);
		CollectionAssert.AreEqual(
			new[]
			{
				new TextSnippetPlaceholder(0, new TextRange(1, 0), null),
				new TextSnippetPlaceholder(0, new TextRange(2, 0), null)
			},
			result.Placeholders.ToArray());
	}

	[TestMethod]
	public void Expand_FinalCaretInsideNestedDefaultText_RecordsTheNestedPosition()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("x${1:y$0z}");

		Assert.AreEqual("xyz", result.Text);
		CollectionAssert.AreEqual(
			new[]
			{
				new TextSnippetPlaceholder(1, new TextRange(1, 2), null),
				new TextSnippetPlaceholder(0, new TextRange(2, 0), null)
			},
			result.Placeholders.ToArray());
	}

	[TestMethod]
	public void Expand_DollarWithoutDigitsOrBrace_StaysLiteral()
	{
		Assert.AreEqual("$ x", TextSnippetExpander.Expand("$ x").Text);
		Assert.AreEqual("100% $", TextSnippetExpander.Expand("100% $").Text);
	}

	[TestMethod]
	public void Expand_MirroredTabstopIndex_RecordsSeparateOrderedEntries()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("${1:foo} and $1");

		Assert.AreEqual("foo and ", result.Text);
		CollectionAssert.AreEqual(
			new[]
			{
				new TextSnippetPlaceholder(1, new TextRange(0, 3), null),
				new TextSnippetPlaceholder(1, new TextRange(8, 0), null)
			},
			result.Placeholders.ToArray());
	}

	[TestMethod]
	public void Expand_RealWorldMultiLineSnippet_PreservesVisibleTextAndTabstops()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("if ${1:condition} then\n\t$0\nend");

		Assert.AreEqual("if condition then\n\t\nend", result.Text);
		CollectionAssert.AreEqual(
			new[]
			{
				new TextSnippetPlaceholder(1, new TextRange(3, 9), null),
				new TextSnippetPlaceholder(0, new TextRange(19, 0), null)
			},
			result.Placeholders.ToArray());
	}

	[TestMethod]
	public void Expand_EmptyBracedConstruct_StaysVerbatim()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("a${}b");

		Assert.AreEqual("a${}b", result.Text);
		Assert.AreEqual(0, result.Placeholders.Count);
	}

	[TestMethod]
	public void Expand_EmptyDefaultText_RecordsAnEmptyPlaceholderRange()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("x${1:}y");

		Assert.AreEqual("xy", result.Text);
		CollectionAssert.AreEqual(
			new[] { new TextSnippetPlaceholder(1, new TextRange(1, 0), null) },
			result.Placeholders.ToArray());
	}

	[TestMethod]
	public void Expand_ChoiceNestedInsideDefaultText_RecordsBothPlaceholders()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("${1:${2|a,b|}}");

		Assert.AreEqual("a", result.Text);
		Assert.AreEqual(2, result.Placeholders.Count);

		TextSnippetPlaceholder outer = result.Placeholders[0];
		Assert.AreEqual(1, outer.Index);
		Assert.AreEqual(new TextRange(0, 1), outer.Range);
		Assert.IsNull(outer.Choices);

		TextSnippetPlaceholder nestedChoice = result.Placeholders[1];
		Assert.AreEqual(2, nestedChoice.Index);
		Assert.AreEqual(new TextRange(0, 1), nestedChoice.Range);
		CollectionAssert.AreEqual(new[] { "a", "b" }, nestedChoice.Choices!.ToArray());
	}

	[TestMethod]
	public void Expand_ChoiceTextContainingCloseBrace_ExpandsTheWholeChoice()
	{
		// Inside a choice, only "|" terminates it: a bare '}' is plain choice text.
		TextSnippetExpansion result = TextSnippetExpander.Expand("${1|a}b|}");

		Assert.AreEqual("a}b", result.Text);
		Assert.AreEqual(1, result.Placeholders.Count);
		Assert.AreEqual(1, result.Placeholders[0].Index);
		Assert.AreEqual(new TextRange(0, 3), result.Placeholders[0].Range);
		CollectionAssert.AreEqual(new[] { "a}b" }, result.Placeholders[0].Choices!.ToArray());
	}

	[TestMethod]
	public void Expand_MalformedChoiceFollowedByValidChoice_KeepsTheFirstLiteral()
	{
		// The first construct has no "|" terminator of its own, so it stays literal while scanning
		// resumes inside it and the later choice still expands.
		TextSnippetExpansion result = TextSnippetExpander.Expand("${1|a} ${2|b,c|}");

		Assert.AreEqual("${1|a} b", result.Text);
		Assert.AreEqual(1, result.Placeholders.Count);
		Assert.AreEqual(2, result.Placeholders[0].Index);
		Assert.AreEqual(new TextRange(7, 1), result.Placeholders[0].Range);
	}

	[TestMethod]
	public void Expand_NestedConstructInsideChoiceText_StaysLiteral()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("${1|a${2:x},b|}");

		Assert.AreEqual("a${2:x}", result.Text);
		Assert.AreEqual(1, result.Placeholders.Count);
		Assert.AreEqual(1, result.Placeholders[0].Index);
		Assert.AreEqual(new TextRange(0, 7), result.Placeholders[0].Range);
		CollectionAssert.AreEqual(new[] { "a${2:x}", "b" }, result.Placeholders[0].Choices!.ToArray());
	}

	[TestMethod]
	public void Expand_NestingDeeperThanTheLimit_StopsExpandingWithoutThrowing()
	{
		string nested = string.Concat(Enumerable.Repeat("${1:", 64)) + "x" + new string('}', 64);

		TextSnippetExpansion result = TextSnippetExpander.Expand(nested);

		Assert.AreEqual(32, result.Placeholders.Count);
		Assert.IsTrue(result.Text.Contains('x'));
		Assert.IsTrue(result.Text.EndsWith('}'));
	}

	[TestMethod]
	public void Expand_LeadingZeroTabstopIndex_IsParsedAsDecimal()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("$01");

		Assert.AreEqual(string.Empty, result.Text);
		CollectionAssert.AreEqual(
			new[] { new TextSnippetPlaceholder(1, new TextRange(0, 0), null) },
			result.Placeholders.ToArray());
	}

	[TestMethod]
	public void Expand_UnknownEscape_KeepsTheBackslash()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand(@"a\nb");

		Assert.AreEqual(@"a\nb", result.Text);
		Assert.AreEqual(0, result.Placeholders.Count);
	}

	[TestMethod]
	public void Expand_NonAsciiDigits_AreNotTabstopDigits()
	{
		// The grammar defines [0-9]+; other Unicode decimal digits are plain text.
		TextSnippetExpansion result = TextSnippetExpander.Expand("$\u0661");

		Assert.AreEqual("$\u0661", result.Text);
		Assert.AreEqual(0, result.Placeholders.Count);
	}

	[TestMethod]
	public void Expand_UnterminatedNestedChoice_KeepsOneConsistentBoundary()
	{
		// The close-brace scan and the expansion path agree on malformed nested choices: an
		// unterminated choice is not a construct, so the enclosing placeholder closes at the first
		// brace that is not part of a choice terminator.
		TextSnippetExpansion result = TextSnippetExpander.Expand("${1:${2|a}b}c}");

		Assert.AreEqual("${2|ab}c}", result.Text);
		CollectionAssert.AreEqual(
			new[] { new TextSnippetPlaceholder(1, new TextRange(0, 5), null) },
			result.Placeholders.ToArray());
	}

	[TestMethod]
	public void Expand_BraceFreeRemainder_MatchesTheTolerantFallback()
	{
		// Unterminated constructs with no close brace anywhere in the remainder take the single-pass
		// fallback; the visible text and tabstops stay identical to per-construct rescanning.
		TextSnippetExpansion literal = TextSnippetExpander.Expand("$a${b${c");

		Assert.AreEqual("$a${b${c", literal.Text);
		Assert.AreEqual(0, literal.Placeholders.Count);

		TextSnippetExpansion mixed = TextSnippetExpander.Expand("$1${a");

		Assert.AreEqual("${a", mixed.Text);
		CollectionAssert.AreEqual(
			new[] { new TextSnippetPlaceholder(1, new TextRange(0, 0), null) },
			mixed.Placeholders.ToArray());
	}
}
