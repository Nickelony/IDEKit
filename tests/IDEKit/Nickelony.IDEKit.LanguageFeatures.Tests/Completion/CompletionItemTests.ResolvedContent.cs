using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Markup;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

public sealed partial class CompletionItemTests
{
	[TestMethod]
	public void WithResolvedContent_OverridesDetailDocumentationAndKind()
	{
		var item = new CompletionItem("label")
		{
			Documentation = "base",
			Detail = "base detail",
			Kind = CompletionItemKind.Field,
		};

		var resolvedContent = new CompletionItem("label")
		{
			Documentation = new MarkupContent("**resolved**", MarkupKind.Markdown),
			Detail = "resolved detail",
			Kind = CompletionItemKind.Method,
		};

		CompletionItem merged = item.WithResolvedContent(resolvedContent);

		Assert.AreEqual("resolved detail", merged.Detail);
		Assert.AreEqual("**resolved**", merged.Documentation?.Value);
		Assert.AreEqual(MarkupKind.Markdown, merged.Documentation?.Kind);
		Assert.AreSame(CompletionItemKind.Method, merged.Kind);
	}

	[TestMethod]
	public void WithResolvedContent_GenericKind_IsFilledFromResolvedItem()
	{
		// A resolved kind that was set fills in when the current item never set one, so a resolved
		// item upgrades an unclassified item.
		var item = new CompletionItem("label");
		var resolvedContent = new CompletionItem("label") { Kind = CompletionItemKind.Method };

		CompletionItem merged = item.WithResolvedContent(resolvedContent);

		Assert.AreSame(CompletionItemKind.Method, merged.Kind);
	}

	[TestMethod]
	public void WithResolvedContent_ExplicitGenericKind_ResetsCurrentKind()
	{
		// An explicitly assigned category wins, including a reset to the Generic fallback.
		var item = new CompletionItem("label") { Kind = CompletionItemKind.Field };
		var resolvedContent = new CompletionItem("label") { Kind = CompletionItemKind.Generic };

		CompletionItem merged = item.WithResolvedContent(resolvedContent);

		Assert.AreSame(CompletionItemKind.Generic, merged.Kind);
	}

	[TestMethod]
	public void WithResolvedContent_ResolvedPriorityAndPreselect_KeepCurrentValues()
	{
		// Priority and preselect state are identity fields: a resolved item never overrides them.
		var item = new CompletionItem("label") { Priority = 5.0, IsPreselected = true };

		var resolvedContent = new CompletionItem("label")
		{
			Detail = "resolved detail",
			Priority = 9.0,
			IsPreselected = false,
		};

		CompletionItem merged = item.WithResolvedContent(resolvedContent);

		Assert.AreEqual("resolved detail", merged.Detail);
		Assert.AreEqual(5.0, merged.Priority);
		Assert.IsTrue(merged.IsPreselected);
	}

	[TestMethod]
	public void WithResolvedContent_BlankResolvedFields_KeepCurrentValues()
	{
		var item = new CompletionItem("label")
		{
			Documentation = "base",
			Detail = "base detail",
			Kind = CompletionItemKind.Field,
		};

		var resolvedContent = new CompletionItem("label") { Documentation = "   ", Detail = "  " };

		CompletionItem merged = item.WithResolvedContent(resolvedContent);

		Assert.AreEqual("base detail", merged.Detail);
		Assert.AreEqual("base", merged.Documentation?.Value);
		Assert.AreSame(CompletionItemKind.Field, merged.Kind);
	}

	[TestMethod]
	public void WithResolvedContent_AdoptsSortTextOnlyWhenMissing()
	{
		var resolvedContent = new CompletionItem("label") { SortText = "0001" };
		var withoutSortText = new CompletionItem("label");
		var withSortText = new CompletionItem("label") { SortText = "0002", IsPreselected = true };

		CompletionItem adopted = withoutSortText.WithResolvedContent(resolvedContent);
		CompletionItem kept = withSortText.WithResolvedContent(resolvedContent);

		Assert.AreEqual("0001", adopted.SortText);
		Assert.AreEqual("0002", kept.SortText);

		// Preselect state always comes from the original item; the resolved item cannot change it.
		Assert.IsTrue(kept.IsPreselected);
	}

	[TestMethod]
	public void WithResolvedContent_NoContribution_ReturnsTheSameInstance()
	{
		var item = new CompletionItem("label") { Detail = "detail", Documentation = "docs" };

		CompletionItem merged = item.WithResolvedContent(new CompletionItem("other"));

		Assert.AreSame(item, merged);
	}

	[TestMethod]
	public void WithResolvedContent_BlankPresentationFields_AdoptResolvedValuesAndIgnoreResolvedStamps()
	{
		// Fill-in direction: the current item has no detail or documentation yet, so the resolved values
		// apply; resolved request stamps never override the originating stamps.
		var item = new CompletionItem("label") { RequestDocumentVersion = 4, RequestGeneration = 9 };

		var resolved = new CompletionItem("label")
		{
			Detail = "resolved detail",
			Documentation = "resolved description",
			RequestDocumentVersion = 77,
			RequestGeneration = 88,
		};

		CompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("resolved detail", merged.Detail);
		Assert.AreEqual("resolved description", merged.Documentation?.Value);
		Assert.AreEqual(4, merged.RequestDocumentVersion);
		Assert.AreEqual(9, merged.RequestGeneration);
	}

	[TestMethod]
	public void WithResolvedContent_ResolvedPlainDocumentation_ClearsMarkdownMode()
	{
		// The documentation and its kind are adopted together from a non-blank resolved value.
		var item = new CompletionItem("label") { Documentation = new MarkupContent("**marked**", MarkupKind.Markdown) };
		var resolved = new CompletionItem("label") { Documentation = "  plain text  " };

		CompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("plain text", merged.Documentation?.Value);
		Assert.AreEqual(MarkupKind.PlainText, merged.Documentation?.Kind);
	}

	[TestMethod]
	public void WithResolvedContent_ExplicitResolvedTextEqualToOneOwnLabel_IsAdopted()
	{
		// An explicitly assigned value is intentional even when it equals its own label; only unset
		// (label-fallback) text is filled in from the resolved item.
		var item = new CompletionItem("label");
		var resolved = new CompletionItem("spawn") { InsertText = "spawn", FilterText = "spawn" };

		CompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("spawn", merged.InsertText);
		Assert.AreEqual("spawn", merged.FilterText);
	}

	[TestMethod]
	public void WithResolvedContent_MergesTagsAsUnionWithoutDuplicates()
	{
		// The resolved tags fill in when the current item has none, and a tag both sides carry is
		// merged once, so the union never repeats an entry.
		var withoutTags = new CompletionItem("label");
		var withTags = new CompletionItem("label") { Tags = [CompletionItemTag.Deprecated] };
		var resolved = new CompletionItem("label") { Tags = [CompletionItemTag.Deprecated] };

		CompletionItem adopted = withoutTags.WithResolvedContent(resolved);
		CompletionItem merged = withTags.WithResolvedContent(resolved);

		Assert.AreSequenceEqual([CompletionItemTag.Deprecated], [.. adopted.Tags]);
		Assert.HasCount(1, merged.Tags);
	}

	[TestMethod]
	public void WithResolvedContent_AdoptsCommitCharactersOnlyWhenMissing()
	{
		var without = new CompletionItem("label");
		var with = new CompletionItem("label") { CommitCharacters = ["("] };
		var resolved = new CompletionItem("label") { CommitCharacters = [".", ":"] };

		CompletionItem adopted = without.WithResolvedContent(resolved);
		CompletionItem kept = with.WithResolvedContent(resolved);

		Assert.AreSequenceEqual([".", ":"], [.. adopted.CommitCharacters]);
		Assert.AreSequenceEqual(["("], [.. kept.CommitCharacters]);
	}

	[TestMethod]
	public void WithResolvedContent_AdoptsAdditionalTextEditsOnlyWhenMissing()
	{
		var keptEdit = new CompletionTextEdit(new TextRange(1, 1), newText: "kept");
		var resolvedEdit = new CompletionTextEdit(new TextRange(2, 2), newText: "resolved");

		var without = new CompletionItem("label");
		var with = new CompletionItem("label") { AdditionalTextEdits = [keptEdit] };

		CompletionItem adopted = without.WithResolvedContent(new CompletionItem("label") { AdditionalTextEdits = [resolvedEdit] });
		CompletionItem kept = with.WithResolvedContent(new CompletionItem("label") { AdditionalTextEdits = [resolvedEdit] });

		Assert.HasCount(1, adopted.AdditionalTextEdits);
		Assert.AreEqual("resolved", adopted.AdditionalTextEdits[0].NewText);
		Assert.HasCount(1, kept.AdditionalTextEdits);
		Assert.AreEqual("kept", kept.AdditionalTextEdits[0].NewText);
	}
}
