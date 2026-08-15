using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

public sealed partial class TextCompletionItemTests
{
	[TestMethod]
	public async Task WithResolvedContent_KeepsCommitMetadataAndDropsResolveCallbackAsync()
	{
		var textEdit = new TextCompletionTextEdit(new TextRange(2, 4));
		var item = new TextCompletionItem("label")
		{
			Priority = 3.0,
			FilterText = "filtered",
			TextEdit = textEdit,
			RequestDocumentVersion = 4,
			RequestGeneration = 9,
			InsertTextFormat = TextCompletionInsertTextFormat.Snippet
		}.WithResolveCallback(static _ => Task.FromResult(new TextCompletionItem("resolved")));

		TextCompletionItem merged = item.WithResolvedContent(new TextCompletionItem("other") { Detail = "resolved" });
		TextCompletionItem resolvedItem = await merged.ResolveAsync().ConfigureAwait(false);

		Assert.AreEqual("label", merged.Label);
		Assert.AreEqual("label", merged.InsertText);
		Assert.AreEqual("filtered", merged.FilterText);
		Assert.AreEqual(3.0, merged.Priority);
		Assert.AreEqual(textEdit, merged.TextEdit);
		Assert.AreEqual(4, merged.RequestDocumentVersion);
		Assert.AreEqual(9, merged.RequestGeneration);
		Assert.AreEqual(TextCompletionInsertTextFormat.Snippet, merged.InsertTextFormat);
		Assert.IsFalse(merged.CanResolve);
		Assert.AreSame(merged, resolvedItem);
	}

	[TestMethod]
	public void WithResolvedContent_ResolvedCommitFields_FillInWhenCurrentItemLacksThem()
	{
		var resolvedEdit = new TextCompletionTextEdit(new TextRange(2, 4), new TextRange(2, 10));
		var item = new TextCompletionItem("label");

		TextCompletionItem merged = item.WithResolvedContent(
			new TextCompletionItem("label")
			{
				TextEdit = resolvedEdit,
				InsertTextFormat = TextCompletionInsertTextFormat.Snippet
			});

		Assert.AreEqual(resolvedEdit, merged.TextEdit);
		Assert.AreEqual(TextCompletionInsertTextFormat.Snippet, merged.InsertTextFormat);
	}

	[TestMethod]
	public void WithResolvedContent_BothSidesCarryCommitFields_KeepsCurrentTextEditButAdoptsTheResolvedFormat()
	{
		var currentEdit = new TextCompletionTextEdit(new TextRange(0, 3), new TextRange(0, 5), "current");
		var resolvedEdit = new TextCompletionTextEdit(new TextRange(1, 2), new TextRange(1, 4), "resolved");
		var item = new TextCompletionItem("label")
		{
			TextEdit = currentEdit,
			InsertTextFormat = TextCompletionInsertTextFormat.Snippet
		};
		var resolved = new TextCompletionItem("label")
		{
			TextEdit = resolvedEdit,
			InsertTextFormat = TextCompletionInsertTextFormat.PlainText
		};

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual(currentEdit, merged.TextEdit);

		// An explicitly assigned format wins, including a reset to plain text.
		Assert.AreEqual(TextCompletionInsertTextFormat.PlainText, merged.InsertTextFormat);
	}

	[TestMethod]
	public void WithResolvedContent_DistinctCurrentCommitText_StaysFromCurrentItem()
	{
		// Text that differs from the current label is treated as intentional and is never replaced.
		var item = new TextCompletionItem("label") { InsertText = "inserted", FilterText = "filtered" };
		var resolved = new TextCompletionItem("resolved-label") { InsertText = "resolved-insert", FilterText = "resolved-filter" };

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("label", merged.Label);
		Assert.AreEqual("inserted", merged.InsertText);
		Assert.AreEqual("filtered", merged.FilterText);
	}

	[TestMethod]
	public void WithResolvedContent_LabelFallbackCommitText_AdoptsResolvedCommitText()
	{
		// The list item only had the label fallback, so resolve-time commit data is adopted.
		var item = new TextCompletionItem("label");
		var resolved = new TextCompletionItem("label") { InsertText = "resolved-insert", FilterText = "resolved-filter" };

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("label", merged.Label);
		Assert.AreEqual("resolved-insert", merged.InsertText);
		Assert.AreEqual("resolved-filter", merged.FilterText);
	}

	[TestMethod]
	public void WithResolvedContent_LabelFallbackOnResolvedItem_KeepsCurrentCommitText()
	{
		// The resolved item only carries its own label fallback, so there is no commit data to adopt.
		var item = new TextCompletionItem("label");
		var resolved = new TextCompletionItem("resolved-label");

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("label", merged.Label);
		Assert.AreEqual("label", merged.InsertText);
		Assert.AreEqual("label", merged.FilterText);
	}

	[TestMethod]
	public void WithResolvedContent_NullInsertText_IsTheAbsentStateAndAdoptsResolvedText()
	{
		// A null assignment is the absent state, exactly like a never-assigned value, so a resolve
		// response can still supply the server's late insertion text.
		var item = new TextCompletionItem("label") { InsertText = null };
		var resolved = new TextCompletionItem("label") { InsertText = "resolved-insert" };

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("resolved-insert", merged.InsertText);
	}

	[TestMethod]
	public void WithResolvedContent_InsertTextEqualToTheLabel_PinsTheLabelFallback()
	{
		// Assigning the label itself is a non-null value, so the merge never replaces it; this is how
		// a host pins the commit text against a resolve response.
		var item = new TextCompletionItem("label") { InsertText = "label" };
		var resolved = new TextCompletionItem("label") { InsertText = "resolved-insert" };

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("label", merged.InsertText);
	}

	[TestMethod]
	public void WithResolvedContent_CurrentItemWithEditPayload_KeepsCommitText()
	{
		// An explicit edit payload makes the commit intentional even when the insertion text equals the label.
		var textEdit = new TextCompletionTextEdit(new TextRange(0, 3), new TextRange(0, 5));
		var item = new TextCompletionItem("spawn") { InsertText = "spawn", TextEdit = textEdit };
		var resolved = new TextCompletionItem("spawn") { InsertText = "resolved-spawn" };

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("spawn", merged.InsertText);
		Assert.AreEqual(textEdit, merged.TextEdit);
	}

	[TestMethod]
	public void WithResolvedContent_EditPayloadVetoesResolvedInsertText_WhenInsertTextWasNeverSet()
	{
		// The current item never set an insertion text but carries an edit payload; the resolved
		// insertion text must not be adopted, because the edit payload owns the commit text.
		var textEdit = new TextCompletionTextEdit(new TextRange(0, 3), new TextRange(0, 5));
		var item = new TextCompletionItem("spawn") { TextEdit = textEdit };
		var resolved = new TextCompletionItem("spawn") { InsertText = "resolved-spawn" };

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("spawn", merged.InsertText, "The label fallback must remain when an edit payload owns the commit text.");
		Assert.AreEqual(textEdit, merged.TextEdit);
	}

	[TestMethod]
	public void WithResolvedContent_ResolvedEditWithNewText_FillsInWhenCurrentItemLacksEdit()
	{
		// The edit payload is adopted as a whole, including its replacement text, so a resolve response
		// can supply the commit text for items that only had a plain insertion text.
		var resolvedEdit = new TextCompletionTextEdit(new TextRange(2, 4), newText: "\t");
		var item = new TextCompletionItem("label");

		TextCompletionItem merged = item.WithResolvedContent(new TextCompletionItem("label") { TextEdit = resolvedEdit });

		Assert.AreEqual(resolvedEdit, merged.TextEdit);
		Assert.AreEqual("\t", merged.TextEdit?.NewText);
	}

	[TestMethod]
	public void WithResolvedContent_CurrentItemWithEditPayload_StillAdoptsFilterTextFallback()
	{
		// Filter text only drives matching, so an edit payload on this item does not block a
		// resolve-time filter text; the committed text stays untouched.
		var textEdit = new TextCompletionTextEdit(new TextRange(0, 3), new TextRange(0, 5));
		var item = new TextCompletionItem("spawn") { TextEdit = textEdit };
		var resolved = new TextCompletionItem("spawn") { FilterText = "spawn_func" };

		TextCompletionItem merged = item.WithResolvedContent(resolved);

		Assert.AreEqual("spawn_func", merged.FilterText);
		Assert.AreEqual("spawn", merged.InsertText);
		Assert.AreEqual(textEdit, merged.TextEdit);
	}
}
