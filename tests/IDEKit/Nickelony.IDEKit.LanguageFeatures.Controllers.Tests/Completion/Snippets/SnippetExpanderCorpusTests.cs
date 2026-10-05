using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Snippets;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// Pins the expander against realistic protocol snippets: common completion payloads that mix defaults, choices, escapes, and line endings,
/// plus the documented variable boundary where unsupported syntax stays literal.
/// </summary>
[TestClass]
public sealed class SnippetExpanderCorpusTests
{
	[TestMethod]
	public void Expand_Corpus_FunctionSnippet_ResolvesDefaultsAndFinalStop()
	{
		SnippetExpansion result = SnippetExpander.Expand("function ${1:name}(${2:args}) {\n\t$0\n}");

		Assert.AreEqual("function name(args) {\n\t\n}", result.Text);

		Assert.AreSequenceEqual(
			[
				new SnippetPlaceholder(1, new TextRange(9, 4), null),
				new SnippetPlaceholder(2, new TextRange(14, 4), null),
				new SnippetPlaceholder(0, new TextRange(23, 0), null),
			],

			[.. result.Placeholders]
		);
	}

	[TestMethod]
	public void Expand_Corpus_ChoiceSnippet_EmbedsTheFirstChoice()
	{
		SnippetExpansion result = SnippetExpander.Expand("${1|public,private,protected|} ${2:name};");

		Assert.AreEqual("public name;", result.Text);

		SnippetPlaceholder choice = result.Placeholders[0];

		Assert.AreEqual(1, choice.Index);
		Assert.AreEqual(new TextRange(0, 6), choice.Range);
		Assert.AreSequenceEqual(["public", "private", "protected"], [.. choice.Choices!]);

		SnippetPlaceholder name = result.Placeholders[1];

		Assert.AreEqual(2, name.Index);
		Assert.AreEqual(new TextRange(7, 4), name.Range);
		Assert.IsNull(name.Choices);
	}

	[TestMethod]
	public void Expand_Corpus_VariableSyntaxStaysLiteral()
	{
		// Variables and named placeholders are outside the supported subset; the documented boundary
		// is that the expander keeps them literal and a host resolves them around the expansion.
		const string snippet = "local file = '$TM_FILENAME' -- ${TM_FILENAME} ${VAR:default}";

		SnippetExpansion result = SnippetExpander.Expand(snippet);

		Assert.AreEqual(snippet, result.Text);
		Assert.IsEmpty(result.Placeholders);
	}

	[TestMethod]
	public void Expand_Corpus_CrlfSnippet_PreservesLineEndingsAndTabs()
	{
		SnippetExpansion result = SnippetExpander.Expand("if (\r\n\t${1:condition}\r\n) {\r\n\t$0\r\n}");

		Assert.AreEqual("if (\r\n\tcondition\r\n) {\r\n\t\r\n}", result.Text);

		Assert.AreSequenceEqual(
			[new SnippetPlaceholder(1, new TextRange(7, 9), null), new SnippetPlaceholder(0, new TextRange(24, 0), null)],
			[.. result.Placeholders]
		);
	}

	[TestMethod]
	public void Expand_Corpus_EscapedDollarBraceAndBackslash_ResolveInsideAndOutsideADefault()
	{
		SnippetExpansion result = SnippetExpander.Expand("cost: \\$${1:amount} \\\\ \\} ${2:a\\}b}");

		Assert.AreEqual("cost: $amount \\ } a}b", result.Text);

		Assert.AreSequenceEqual(
			[new SnippetPlaceholder(1, new TextRange(7, 6), null), new SnippetPlaceholder(2, new TextRange(18, 3), null)],
			[.. result.Placeholders]
		);
	}

	[TestMethod]
	public void Expand_Corpus_TrailingDollarAndTrailingBackslash_StayLiteral()
	{
		SnippetExpansion dollar = SnippetExpander.Expand("price 5$");
		SnippetExpansion backslash = SnippetExpander.Expand("dir\\");
		SnippetExpansion both = SnippetExpander.Expand("${1:a$}\\");

		Assert.AreEqual("price 5$", dollar.Text);
		Assert.IsEmpty(dollar.Placeholders);
		Assert.AreEqual("dir\\", backslash.Text);
		Assert.IsEmpty(backslash.Placeholders);
		Assert.AreEqual("a$\\", both.Text);
		Assert.AreSequenceEqual([new SnippetPlaceholder(1, new TextRange(0, 2), null)], [.. both.Placeholders]);
	}

	[TestMethod]
	public void Expand_Corpus_NestedDefaultsMultiDigitIndexesAndAChoice_OrderByIndexWithTheFinalStopLast()
	{
		SnippetExpansion result = SnippetExpander.Expand("${1:outer ${2:inner ${10:deep}}} $19 ${12|x,y|} $0");

		Assert.AreEqual("outer inner deep  x ", result.Text);

		Assert.AreSequenceEqual(
			[
				(1, new TextRange(0, 16)),
				(2, new TextRange(6, 10)),
				(10, new TextRange(12, 4)),
				(12, new TextRange(18, 1)),
				(19, new TextRange(17, 0)),
				(0, new TextRange(20, 0)),
			],

			[.. result.Placeholders.Select(placeholder => (placeholder.Index, placeholder.Range))]
		);

		Assert.AreSequenceEqual(["x", "y"], [.. result.Placeholders[3].Choices!]);
	}

	[TestMethod]
	public void Expand_Corpus_IndexesBeyondTheIntRange_StayLiteral()
	{
		const string snippet = "$99999999999 ${99999999999} ${99999999999|a|}";

		SnippetExpansion result = SnippetExpander.Expand(snippet);

		Assert.AreEqual(snippet, result.Text);
		Assert.IsEmpty(result.Placeholders);
	}

	[TestMethod]
	public void Expand_Corpus_ChoiceWithACloseBraceInsideADefault_DoesNotEndTheDefaultEarly()
	{
		SnippetExpansion result = SnippetExpander.Expand("${3:pre ${1|a},b|} post}");

		Assert.AreEqual("pre a} post", result.Text);

		Assert.AreSequenceEqual(
			[(1, new TextRange(4, 2)), (3, new TextRange(0, 11))],
			[.. result.Placeholders.Select(placeholder => (placeholder.Index, placeholder.Range))]
		);

		Assert.AreSequenceEqual(["a}", "b"], [.. result.Placeholders[0].Choices!]);
	}

	[TestMethod]
	public void Expand_Corpus_UnterminatedChoiceInsideADefault_StaysLiteralAndEndsAtTheFirstCloseBrace()
	{
		SnippetExpansion result = SnippetExpander.Expand("${1:a ${2|b,c} d}");

		Assert.AreEqual("a ${2|b,c d}", result.Text);
		Assert.AreSequenceEqual([new SnippetPlaceholder(1, new TextRange(0, 9), null)], [.. result.Placeholders]);
	}

	[TestMethod]
	public void Expand_Corpus_ChoiceLookalikeWithoutAnIndexInsideADefault_StaysLiteralAndKeepsTheDefaultOpen()
	{
		SnippetExpansion result = SnippetExpander.Expand("${1:${|a}}");

		Assert.AreEqual("${|a}", result.Text);
		Assert.AreSequenceEqual([new SnippetPlaceholder(1, new TextRange(0, 5), null)], [.. result.Placeholders]);
	}
}
