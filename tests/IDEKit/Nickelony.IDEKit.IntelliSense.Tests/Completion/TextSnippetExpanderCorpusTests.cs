using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

/// <summary>
/// Pins the expander against realistic protocol snippets: common completion payloads that mix
/// defaults, choices, escapes, and line endings, plus the documented variable boundary where
/// unsupported syntax stays literal.
/// </summary>
[TestClass]
public sealed class TextSnippetExpanderCorpusTests
{
	[TestMethod]
	public void Expand_Corpus_FunctionSnippet_ResolvesDefaultsAndFinalStop()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("function ${1:name}(${2:args}) {\n\t$0\n}");

		Assert.AreEqual("function name(args) {\n\t\n}", result.Text);
		CollectionAssert.AreEqual(
			new[]
			{
				new TextSnippetPlaceholder(1, new TextRange(9, 4), null),
				new TextSnippetPlaceholder(2, new TextRange(14, 4), null),
				new TextSnippetPlaceholder(0, new TextRange(23, 0), null)
			},
			result.Placeholders.ToArray());
	}

	[TestMethod]
	public void Expand_Corpus_ChoiceSnippet_EmbedsTheFirstChoice()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("${1|public,private,protected|} ${2:name};");

		Assert.AreEqual("public name;", result.Text);

		TextSnippetPlaceholder choice = result.Placeholders[0];
		Assert.AreEqual(1, choice.Index);
		Assert.AreEqual(new TextRange(0, 6), choice.Range);
		CollectionAssert.AreEqual(new[] { "public", "private", "protected" }, choice.Choices!.ToArray());

		TextSnippetPlaceholder name = result.Placeholders[1];
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

		TextSnippetExpansion result = TextSnippetExpander.Expand(snippet);

		Assert.AreEqual(snippet, result.Text);
		Assert.AreEqual(0, result.Placeholders.Count);
	}

	[TestMethod]
	public void Expand_Corpus_CrlfSnippet_PreservesLineEndingsAndTabs()
	{
		TextSnippetExpansion result = TextSnippetExpander.Expand("if (\r\n\t${1:condition}\r\n) {\r\n\t$0\r\n}");

		Assert.AreEqual("if (\r\n\tcondition\r\n) {\r\n\t\r\n}", result.Text);
		CollectionAssert.AreEqual(
			new[]
			{
				new TextSnippetPlaceholder(1, new TextRange(7, 9), null),
				new TextSnippetPlaceholder(0, new TextRange(24, 0), null)
			},
			result.Placeholders.ToArray());
	}
}
