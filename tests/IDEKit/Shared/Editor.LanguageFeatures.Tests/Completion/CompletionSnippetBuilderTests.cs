#if AVALONIAEDIT
using AvaloniaEdit.Snippets;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Snippets;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit.Snippets;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Snippets;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>
/// Verifies which editor snippet elements an expanded completion snippet becomes: text, replaceable text, a bound mirror, and the caret.
/// </summary>
/// <remarks>
/// The description writes <c>T</c> for text, <c>R</c> for replaceable text, <c>B</c> for a mirror with the index of its target,
/// and <c>C</c> for the caret, which reads <c>C&gt;</c> when it keeps its place after text inserted at it.
/// </remarks>
[STATestClass]
public sealed class CompletionSnippetBuilderTests
{
	[TestMethod]
	public void Build_TwoParametersAndAFinalTabstop_BuildsTextReplaceablesAndTheCaret() =>
		Assert.AreEqual("T[rep(] R[s] T[, ] R[n] T[)] C", Describe("rep(${1:s}, ${2:n})$0"));

	[TestMethod]
	public void Build_WithoutAFinalTabstop_EndsWithTheCaret() => Assert.AreEqual("T[f(] R[x] T[)] C", Describe("f(${1:x})"));

	[TestMethod]
	public void Build_FinalTabstopBeforeMoreText_PlacesTheCaretThere() => Assert.AreEqual("T[f(] R[x] T[)] C T[;]", Describe("f(${1:x})$0;"));

	[TestMethod]
	public void Build_FinalTabstopWithDefaultText_KeepsTheTextBeforeTheCaret() =>
		Assert.AreEqual("T[f(] R[a] T[)] T[tail] C", Describe("f(${1:a})${0:tail}"));

	[TestMethod]
	public void Build_EmptyTabstops_BuildsEmptyReplaceables() => Assert.AreEqual("T[f(] R[] T[, ] R[] T[)] C", Describe("f($1, $2)"));

	[TestMethod]
	public void Build_TabstopsOutOfIndexOrder_FollowsTheTextOrder() => Assert.AreEqual("R[b] T[ ] R[a] C>", Describe("${2:b} ${1:a}"));

	[TestMethod]
	public void Build_ChoicePlaceholder_UsesTheFirstChoiceAsTheText() => Assert.AreEqual("R[a] C>", Describe("${1|a,b|}"));

	[TestMethod]
	public void Build_RepeatedTabstop_BindsTheLaterOccurrenceToTheFirst() => Assert.AreEqual("R[x] T[ = ] B[0] C>", Describe("${1:x} = $1"));

	[TestMethod]
	public void Build_TabstopRepeatedBeforeItsDefault_MirrorsTheFirstOccurrenceThatHasText() =>
		Assert.AreEqual("B[2] T[ = ] R[value] C>", Describe("$1 = ${1:value}"));

	[TestMethod]
	public void Build_CaretAfterTheEndingTabstop_KeepsItsPlaceAfterTextTypedThere() =>
		Assert.AreEqual("T[local ] R[name] C>", Describe("local ${1:name}"));

	[TestMethod]
	public void Build_FinalTabstopRightAfterATabstop_KeepsItsPlaceAfterTextTypedThere() =>
		Assert.AreEqual("T[f(] R[x] C> T[)]", Describe("f(${1:x}$0)"));

	[TestMethod]
	public void Build_FinalTabstopRightBeforeATabstop_StaysBeforeTextTypedThere() => Assert.AreEqual("T[f(] C R[x] T[)]", Describe("f($0${1:x})"));

	[TestMethod]
	public void Build_OnlyAFinalTabstop_ReturnsNull() => Assert.IsNull(CompletionSnippetBuilder.Build(SnippetExpander.Expand("f($0)")));

	[TestMethod]
	public void Build_NoPlaceholders_ReturnsNull() => Assert.IsNull(CompletionSnippetBuilder.Build(SnippetExpander.Expand("print")));

	[TestMethod]
	public void Build_NestedPlaceholders_ReturnsNull() => Assert.IsNull(CompletionSnippetBuilder.Build(SnippetExpander.Expand("${1:foo ${2:bar}}")));

	[TestMethod]
	[DataRow("${1:name}${2:args}")]
	[DataRow("f($1$2)")]
	[DataRow("${1:a}$2")]
	[DataRow("${1:a}$0${2:b}")]
	public void Build_TouchingTabstops_ReturnsNull(string snippetText) =>
		Assert.IsNull(CompletionSnippetBuilder.Build(SnippetExpander.Expand(snippetText)));

	[TestMethod]
	public void Build_TabstopsSeparatedByAMirror_BuildsTheSnippet() => Assert.AreEqual("R[a] B[0] R[] C>", Describe("${1:a}$1$2"));

	private static string Describe(string snippetText)
	{
		Snippet snippet =
			CompletionSnippetBuilder.Build(SnippetExpander.Expand(snippetText))
			?? throw new AssertFailedException("The snippet has navigable tabstops.");

		var parts = new List<string>();

		foreach (SnippetElement element in snippet.Elements)
		{
			parts.Add(
				element switch
				{
					SnippetBoundElement bound => $"B[{snippet.Elements.IndexOf(bound.TargetElement)}]",
					SnippetReplaceableTextElement replaceable => $"R[{replaceable.Text}]",
					SnippetTextElement text => $"T[{text.Text}]",
					CompletionSnippetBuilder.CaretElement caret => caret.MovesAfterInsertion ? "C>" : "C",
					_ => throw new AssertFailedException($"Unexpected element {element.GetType().Name}."),
				}
			);
		}

		return string.Join(" ", parts);
	}
}
