using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.LanguageServer.Lua.Tests;

public sealed partial class ResponseParserTests
{
	[TestMethod]
	public void ParseCompletionItem_AppliesPreselectionAndResponseOrderBranches()
	{
		CompletionItemPayload plain = DeserializeCompletionItemPayload(new { label = "item", kind = 1 });
		CompletionItemPayload preselected = DeserializeCompletionItemPayload(new { label = "item", kind = 1, preselect = true });

		TextCompletionItem? plainItem = ResponseParser.ParseCompletionItem(plain, 0, "text");
		TextCompletionItem? preselectedItem = ResponseParser.ParseCompletionItem(preselected, 0, "text");
		TextCompletionItem? laterItem = ResponseParser.ParseCompletionItem(plain, 3, "text");

		Assert.IsNotNull(plainItem);
		Assert.IsNotNull(preselectedItem);
		Assert.IsNotNull(laterItem);

		// Ordering semantics only: a preselected variant outranks a plain one, and a better protocol
		// rank outranks a worse one; the exact weights stay internal.
		Assert.IsTrue(preselectedItem.Priority > plainItem.Priority);
		Assert.IsTrue(plainItem.Priority > laterItem.Priority);
	}

	[TestMethod]
	public void ParseCompletionItem_CarriesProtocolSortTextAndPreselection()
	{
		CompletionItemPayload itemElement = DeserializeCompletionItemPayload(new
		{
			label = "item",
			kind = 6,
			sortText = "0002",
			preselect = true
		});

		TextCompletionItem? item = ResponseParser.ParseCompletionItem(itemElement, 0, "text");

		Assert.IsNotNull(item);
		Assert.AreEqual("0002", item.SortText);
		Assert.IsTrue(item.IsPreselected);
	}

	[TestMethod]
	public void ParseCompletionItem_BlankSortText_IsNull()
	{
		CompletionItemPayload itemElement = DeserializeCompletionItemPayload(new { label = "item", kind = 6, sortText = "  " });

		TextCompletionItem? item = ResponseParser.ParseCompletionItem(itemElement, 0, "text");

		Assert.IsNotNull(item);
		Assert.IsNull(item.SortText);
		Assert.IsFalse(item.IsPreselected);
	}

	[TestMethod]
	public void ParseCompletionItems_RanksPrioritiesByProtocolSortText()
	{
		CompletionItemPayload late = DeserializeCompletionItemPayload(new { label = "late", kind = 6, sortText = "0002" });
		CompletionItemPayload early = DeserializeCompletionItemPayload(new { label = "early", kind = 6, sortText = "0001" });

		IReadOnlyList<TextCompletionItem> items = ResponseParser.ParseCompletionItems([late, early], "text");

		Assert.AreEqual(2, items.Count);
		Assert.AreEqual("late", items[0].Label);
		Assert.AreEqual("early", items[1].Label);

		// The priority hint agrees with the protocol ordering key even when the response order differs.
		Assert.IsTrue(items[1].Priority > items[0].Priority);
	}

	[TestMethod]
	public void ParseCompletionItems_FallsBackToLabelWhenSortTextIsMissing()
	{
		CompletionItemPayload beta = DeserializeCompletionItemPayload(new { label = "beta", kind = 6 });
		CompletionItemPayload alpha = DeserializeCompletionItemPayload(new { label = "alpha", kind = 6 });

		IReadOnlyList<TextCompletionItem> items = ResponseParser.ParseCompletionItems([beta, alpha], "text");

		Assert.AreEqual(2, items.Count);
		Assert.IsTrue(items[1].Priority > items[0].Priority);
		Assert.AreEqual("alpha", items[1].Label);
	}

	[TestMethod]
	public void ParseCompletionItems_KeepsResponseOrderForEqualSortKeys()
	{
		CompletionItemPayload first = DeserializeCompletionItemPayload(new { label = "alpha", kind = 6, sortText = "0001" });
		CompletionItemPayload second = DeserializeCompletionItemPayload(new { label = "beta", kind = 6, sortText = "0001" });

		IReadOnlyList<TextCompletionItem> items = ResponseParser.ParseCompletionItems([first, second], "text");

		Assert.AreEqual(2, items.Count);
		Assert.AreEqual("alpha", items[0].Label);
		Assert.AreEqual("beta", items[1].Label);
		Assert.IsTrue(items[0].Priority > items[1].Priority);
	}

	[TestMethod]
	public void ParseCompletionItems_SurfacesPreselectedItemAboveProtocolRank()
	{
		CompletionItemPayload first = DeserializeCompletionItemPayload(new { label = "first", kind = 6, sortText = "0001" });
		CompletionItemPayload preselected = DeserializeCompletionItemPayload(new { label = "preselected", kind = 6, sortText = "0002", preselect = true });

		IReadOnlyList<TextCompletionItem> items = ResponseParser.ParseCompletionItems([first, preselected], "text");

		Assert.AreEqual(2, items.Count);
		Assert.IsTrue(items[1].Priority > items[0].Priority);
	}

	[TestMethod]
	public void ParseCompletionItems_KeepsResponseOrderWhenSortTextTiesALabelFallback()
	{
		CompletionItemPayload explicitSortKey = DeserializeCompletionItemPayload(new { label = "alpha", kind = 6, sortText = "0002" });
		CompletionItemPayload labelFallback = DeserializeCompletionItemPayload(new { label = "0002", kind = 6 });

		IReadOnlyList<TextCompletionItem> items = ResponseParser.ParseCompletionItems([explicitSortKey, labelFallback], "text");

		// The ordering key mixes the explicit sortText with the label fallback; when both produce the same
		// key the server's response order still decides the rank.
		Assert.AreEqual(2, items.Count);
		Assert.AreEqual("alpha", items[0].Label);
		Assert.AreEqual("0002", items[1].Label);
		Assert.IsTrue(items[0].Priority > items[1].Priority);
	}
}
