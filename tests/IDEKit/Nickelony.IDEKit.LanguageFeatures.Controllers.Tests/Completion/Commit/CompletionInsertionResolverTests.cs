using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Commit;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion.Snippets;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Verifies which text a completion item inserts and where the caret lands, without an editor.</summary>
[TestClass]
public sealed class CompletionInsertionResolverTests
{
	[TestMethod]
	public void Resolve_NullItem_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => CompletionInsertionResolver.Resolve(null!));

	[TestMethod]
	public void Resolve_PlainItem_InsertsTheInsertTextWithoutACaret()
	{
		CompletionInsertion insertion = CompletionInsertionResolver.Resolve(new CompletionItem("print") { InsertText = "print()" });

		Assert.AreEqual("print()", insertion.Text);
		Assert.IsNull(insertion.CaretOffset);
	}

	[TestMethod]
	public void Resolve_ItemWithoutInsertText_InsertsTheLabel()
	{
		CompletionInsertion insertion = CompletionInsertionResolver.Resolve(new CompletionItem("print"));
		Assert.AreEqual("print", insertion.Text);
	}

	[TestMethod]
	public void Resolve_ItemWithATextEdit_PrefersTheEditText()
	{
		var item = new CompletionItem("print") { InsertText = "ignored", TextEdit = new CompletionTextEdit(new TextRange(0, 2), newText: "printf") };
		Assert.AreEqual("printf", CompletionInsertionResolver.Resolve(item).Text);
	}

	[TestMethod]
	public void Resolve_SnippetWithAFinalTabstop_ExpandsAndPlacesTheCaretThere()
	{
		var item = new CompletionItem("call") { InsertText = "call($0)", InsertTextFormat = CompletionInsertTextFormat.Snippet };

		CompletionInsertion insertion = CompletionInsertionResolver.Resolve(item);

		Assert.AreEqual("call()", insertion.Text);
		Assert.AreEqual(5, insertion.CaretOffset);
	}

	[TestMethod]
	public void Resolve_SnippetWithoutAFinalTabstop_ExpandsAndLeavesTheCaretAfterTheText()
	{
		var item = new CompletionItem("call") { InsertText = "call(${1:arg})", InsertTextFormat = CompletionInsertTextFormat.Snippet };

		CompletionInsertion insertion = CompletionInsertionResolver.Resolve(item);

		Assert.AreEqual("call(arg)", insertion.Text);
		Assert.IsNull(insertion.CaretOffset);
	}

	[TestMethod]
	public void Resolve_SnippetTextEdit_ExpandsTheEditText()
	{
		var item = new CompletionItem("call")
		{
			TextEdit = new CompletionTextEdit(new TextRange(0, 1), newText: "go($0)"),
			InsertTextFormat = CompletionInsertTextFormat.Snippet,
		};

		CompletionInsertion insertion = CompletionInsertionResolver.Resolve(item);

		Assert.AreEqual("go()", insertion.Text);
		Assert.AreEqual(3, insertion.CaretOffset);
	}

	[TestMethod]
	public void Resolve_Expansion_PlacesTheCaretAfterTheFinalTabstopText()
	{
		CompletionInsertion insertion = CompletionInsertionResolver.Resolve(SnippetExpander.Expand("call(${1:a})${0:tail}"));

		Assert.AreEqual("call(a)tail", insertion.Text);
		Assert.AreEqual("call(a)tail".Length, insertion.CaretOffset);
	}

	[TestMethod]
	public void Resolve_ExpansionWithoutAFinalTabstop_LeavesTheCaretAfterTheText()
	{
		CompletionInsertion insertion = CompletionInsertionResolver.Resolve(SnippetExpander.Expand("call(${1:a})"));

		Assert.AreEqual("call(a)", insertion.Text);
		Assert.IsNull(insertion.CaretOffset);
	}

	[TestMethod]
	public void Resolve_DefaultExpansion_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => CompletionInsertionResolver.Resolve(default(SnippetExpansion)));

	[TestMethod]
	public void ResolveSnippet_NullItem_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => CompletionInsertionResolver.ResolveSnippet(null!));

	[TestMethod]
	public void ResolveSnippet_PlainTextItemWithTabstopSyntax_ReturnsNull()
	{
		var item = new CompletionItem("call") { InsertText = "call(${1:arg})" };
		Assert.IsNull(CompletionInsertionResolver.ResolveSnippet(item));
	}

	[TestMethod]
	public void ResolveSnippet_SnippetItem_ReturnsTheExpansionOfTheResolvedInsertionText()
	{
		var item = new CompletionItem("call") { InsertText = "call(${1:a}, ${2:b})$0", InsertTextFormat = CompletionInsertTextFormat.Snippet };

		SnippetExpansion expansion =
			CompletionInsertionResolver.ResolveSnippet(item) ?? throw new AssertFailedException("The item inserts a snippet.");

		Assert.AreEqual(CompletionInsertionResolver.Resolve(item).Text, expansion.Text);
		Assert.AreEqual("call(a, b)", expansion.Text);
		Assert.AreSequenceEqual([1, 2, 0], [.. expansion.Placeholders.Select(placeholder => placeholder.Index)]);
	}

	[TestMethod]
	public void ResolveSnippet_SnippetTextEdit_ExpandsTheEditText()
	{
		var item = new CompletionItem("call")
		{
			InsertText = "ignored(${1:x})",
			TextEdit = new CompletionTextEdit(new TextRange(0, 1), newText: "go(${1:arg})"),
			InsertTextFormat = CompletionInsertTextFormat.Snippet,
		};

		Assert.AreEqual("go(arg)", CompletionInsertionResolver.ResolveSnippet(item)?.Text);
	}
}
