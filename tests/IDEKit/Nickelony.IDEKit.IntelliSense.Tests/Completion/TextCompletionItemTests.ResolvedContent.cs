using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

public sealed partial class TextCompletionItemTests
{
	[TestMethod]
	public void WithResolvedContent_OverridesDetailDocumentationAndKind()
	{
		var item = new TextCompletionItem("label")
		{
			Documentation = "base",
			Detail = "base detail",
			Kind = TextCompletionItemKind.Field
		};
		var resolvedContent = new TextCompletionItem("label")
		{
			Documentation = "**resolved**",
			Detail = "resolved detail",
			Kind = TextCompletionItemKind.Method,
			DocumentationKind = TextMarkupKind.Markdown
		};

		TextCompletionItem merged = item.WithResolvedContent(resolvedContent);

		Assert.AreEqual("resolved detail", merged.Detail);
		Assert.AreEqual("**resolved**", merged.Documentation);
		Assert.AreEqual(TextMarkupKind.Markdown, merged.DocumentationKind);
		Assert.AreSame(TextCompletionItemKind.Method, merged.Kind);
	}

	[TestMethod]
	public void WithResolvedContent_GenericKind_IsFilledFromResolvedItem()
	{
		// A resolved kind that was set fills in when the current item never set one, so a resolved
		// item upgrades an unclassified item.
		var item = new TextCompletionItem("label");
		var resolvedContent = new TextCompletionItem("label") { Kind = TextCompletionItemKind.Method };

		TextCompletionItem merged = item.WithResolvedContent(resolvedContent);

		Assert.AreSame(TextCompletionItemKind.Method, merged.Kind);
	}

	[TestMethod]
	public void WithResolvedContent_ExplicitGenericKind_ResetsCurrentKind()
	{
		// An explicitly assigned category wins, including a reset to the Generic fallback.
		var item = new TextCompletionItem("label") { Kind = TextCompletionItemKind.Field };
		var resolvedContent = new TextCompletionItem("label") { Kind = TextCompletionItemKind.Generic };

		TextCompletionItem merged = item.WithResolvedContent(resolvedContent);

		Assert.AreSame(TextCompletionItemKind.Generic, merged.Kind);
	}

	[TestMethod]
	public void WithResolvedContent_ResolvedPriorityAndPreselect_KeepCurrentValues()
	{
		// Priority and preselect state are identity fields: a resolved item never overrides them.
		var item = new TextCompletionItem("label") { Priority = 5.0, IsPreselected = true };
		var resolvedContent = new TextCompletionItem("label") { Detail = "resolved detail", Priority = 9.0, IsPreselected = false };

		TextCompletionItem merged = item.WithResolvedContent(resolvedContent);

		Assert.AreEqual("resolved detail", merged.Detail);
		Assert.AreEqual(5.0, merged.Priority);
		Assert.IsTrue(merged.IsPreselected);
	}

	[TestMethod]
	public void WithResolvedContent_BlankResolvedFields_KeepCurrentValues()
	{
		var item = new TextCompletionItem("label")
		{
			Documentation = "base",
			Detail = "base detail",
			Kind = TextCompletionItemKind.Field
		};
		var resolvedContent = new TextCompletionItem("label") { Documentation = "   ", Detail = "  " };

		TextCompletionItem merged = item.WithResolvedContent(resolvedContent);

		Assert.AreEqual("base detail", merged.Detail);
		Assert.AreEqual("base", merged.Documentation);
		Assert.AreSame(TextCompletionItemKind.Field, merged.Kind);
	}

	[TestMethod]
	public void WithResolvedContent_AdoptsSortTextOnlyWhenMissing()
	{
		var resolvedContent = new TextCompletionItem("label") { SortText = "0001" };
		var withoutSortText = new TextCompletionItem("label");
		var withSortText = new TextCompletionItem("label") { SortText = "0002", IsPreselected = true };

		TextCompletionItem adopted = withoutSortText.WithResolvedContent(resolvedContent);
		TextCompletionItem kept = withSortText.WithResolvedContent(resolvedContent);

		Assert.AreEqual("0001", adopted.SortText);
		Assert.AreEqual("0002", kept.SortText);

		// Preselect state always comes from the original item; the resolved item cannot change it.
		Assert.IsTrue(kept.IsPreselected);
	}

	[TestMethod]
	public void WithResolvedContent_NoContribution_ReturnsTheSameInstance()
	{
		var item = new TextCompletionItem("label") { Detail = "detail", Documentation = "docs" };

		TextCompletionItem merged = item.WithResolvedContent(new TextCompletionItem("other"));

		Assert.AreSame(item, merged);
	}

	[TestMethod]
	public void WithResolvedContent_BlankPresentationFields_AdoptResolvedValuesAndIgnoreResolvedStamps()
	{
		// Fill-in direction: the current item has no detail or documentation yet, so the resolved values
		// apply; resolved request stamps never override the originating stamps.
		var item = new TextCompletionItem("label") { RequestDocumentVersion = 4, RequestGeneration = 9 };
		var resolved = new TextCompletionItem("label")
		{
			Detail = "resolved detail",
			Documentation = "resolved description",
			RequestDocumentVersion = 77,
			RequestGeneration = 88
		};

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("resolved detail", merged.Detail);
		Assert.AreEqual("resolved description", merged.Documentation);
		Assert.AreEqual(4, merged.RequestDocumentVersion);
		Assert.AreEqual(9, merged.RequestGeneration);
	}

	[TestMethod]
	public void WithResolvedContent_ResolvedPlainDocumentation_ClearsMarkdownMode()
	{
		// The documentation and its kind are adopted together from a non-blank resolved value.
		var item = new TextCompletionItem("label")
		{
			Documentation = "**marked**",
			DocumentationKind = TextMarkupKind.Markdown
		};
		var resolved = new TextCompletionItem("label") { Documentation = "  plain text  " };

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("plain text", merged.Documentation);
		Assert.AreEqual(TextMarkupKind.PlainText, merged.DocumentationKind);
	}

	[TestMethod]
	public void WithResolvedContent_ExplicitResolvedTextEqualToOneOwnLabel_IsAdopted()
	{
		// An explicitly assigned value is intentional even when it equals its own label; only unset
		// (label-fallback) text is filled in from the resolved item.
		var item = new TextCompletionItem("label");
		var resolved = new TextCompletionItem("spawn") { InsertText = "spawn", FilterText = "spawn" };

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("spawn", merged.InsertText);
		Assert.AreEqual("spawn", merged.FilterText);
	}

	[TestMethod]
	public void WithResolvedContent_MergesTagsAsUnionWithoutDuplicates()
	{
		// The resolved tags fill in when the current item has none, and a tag both sides carry is
		// merged once, so the union never repeats an entry.
		var withoutTags = new TextCompletionItem("label");
		var withTags = new TextCompletionItem("label") { Tags = [TextCompletionTag.Deprecated] };
		var resolved = new TextCompletionItem("label") { Tags = [TextCompletionTag.Deprecated] };

		TextCompletionItem adopted = withoutTags.WithResolvedContent(resolved);
		TextCompletionItem merged = withTags.WithResolvedContent(resolved);

		CollectionAssert.AreEqual(new[] { TextCompletionTag.Deprecated }, adopted.Tags.ToArray());
		Assert.AreEqual(1, merged.Tags.Count);
	}

	[TestMethod]
	public void WithResolvedContent_AdoptsCommitCharactersOnlyWhenMissing()
	{
		var without = new TextCompletionItem("label");
		var with = new TextCompletionItem("label") { CommitCharacters = ["("] };
		var resolved = new TextCompletionItem("label") { CommitCharacters = [".", ":"] };

		TextCompletionItem adopted = without.WithResolvedContent(resolved);
		TextCompletionItem kept = with.WithResolvedContent(resolved);

		CollectionAssert.AreEqual(new[] { ".", ":" }, adopted.CommitCharacters.ToArray());
		CollectionAssert.AreEqual(new[] { "(" }, kept.CommitCharacters.ToArray());
	}

	[TestMethod]
	public void WithResolvedContent_AdoptsAdditionalTextEditsOnlyWhenMissing()
	{
		var keptEdit = new TextCompletionTextEdit(new TextRange(1, 1), newText: "kept");
		var resolvedEdit = new TextCompletionTextEdit(new TextRange(2, 2), newText: "resolved");

		var without = new TextCompletionItem("label");
		var with = new TextCompletionItem("label") { AdditionalTextEdits = [keptEdit] };

		TextCompletionItem adopted = without.WithResolvedContent(new TextCompletionItem("label") { AdditionalTextEdits = [resolvedEdit] });
		TextCompletionItem kept = with.WithResolvedContent(new TextCompletionItem("label") { AdditionalTextEdits = [resolvedEdit] });

		Assert.AreEqual(1, adopted.AdditionalTextEdits.Count);
		Assert.AreEqual("resolved", adopted.AdditionalTextEdits[0].NewText);
		Assert.AreEqual(1, kept.AdditionalTextEdits.Count);
		Assert.AreEqual("kept", kept.AdditionalTextEdits[0].NewText);
	}
}
