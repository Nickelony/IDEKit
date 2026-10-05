using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.LanguageServer.Client.Protocol;
using static Nickelony.LanguageServer.Provider.Tests.PayloadTestHelpers;

namespace Nickelony.LanguageServer.Provider.Tests;

public sealed partial class CompletionResponseParserTests
{
	[TestMethod]
	public void ParseItem_AppliesPreselectionAndResponseOrderBranches()
	{
		CompletionItemPayload plain = DeserializeCompletionItemPayload(new { label = "item", kind = 1 });

		CompletionItemPayload preselected = DeserializeCompletionItemPayload(
			new
			{
				label = "item",
				kind = 1,
				preselect = true,
			}
		);

		CompletionItem? plainItem = CompletionResponseParser.ParseItem(plain, 0, "text");
		CompletionItem? preselectedItem = CompletionResponseParser.ParseItem(preselected, 0, "text");
		CompletionItem? laterItem = CompletionResponseParser.ParseItem(plain, 3, "text");

		Assert.IsNotNull(plainItem);
		Assert.IsNotNull(preselectedItem);
		Assert.IsNotNull(laterItem);

		// Ordering semantics only: a preselected variant outranks a plain one, and a better protocol
		// rank outranks a worse one; the exact weights stay internal.
		Assert.IsGreaterThan(plainItem.Priority, preselectedItem.Priority);
		Assert.IsGreaterThan(laterItem.Priority, plainItem.Priority);
	}

	[TestMethod]
	public void ParseItem_CarriesProtocolSortTextAndPreselection()
	{
		CompletionItemPayload itemElement = DeserializeCompletionItemPayload(
			new
			{
				label = "item",
				kind = 6,
				sortText = "0002",
				preselect = true,
			}
		);

		CompletionItem? item = CompletionResponseParser.ParseItem(itemElement, 0, "text");

		Assert.IsNotNull(item);
		Assert.AreEqual("0002", item.SortText);
		Assert.IsTrue(item.IsPreselected);
	}

	[TestMethod]
	public void ParseItem_BlankSortText_IsNull()
	{
		CompletionItemPayload itemElement = DeserializeCompletionItemPayload(
			new
			{
				label = "item",
				kind = 6,
				sortText = "  ",
			}
		);

		CompletionItem? item = CompletionResponseParser.ParseItem(itemElement, 0, "text");

		Assert.IsNotNull(item);
		Assert.IsNull(item.SortText);
		Assert.IsFalse(item.IsPreselected);
	}

	[TestMethod]
	public void Parse_RanksPrioritiesByProtocolSortText()
	{
		CompletionItemPayload late = DeserializeCompletionItemPayload(
			new
			{
				label = "late",
				kind = 6,
				sortText = "0002",
			}
		);

		CompletionItemPayload early = DeserializeCompletionItemPayload(
			new
			{
				label = "early",
				kind = 6,
				sortText = "0001",
			}
		);

		IReadOnlyList<CompletionItem> items = CompletionResponseParser.Parse([late, early], "text");

		Assert.HasCount(2, items);
		Assert.AreEqual("late", items[0].Label);
		Assert.AreEqual("early", items[1].Label);

		// The priority hint agrees with the protocol ordering key even when the response order differs.
		Assert.IsGreaterThan(items[0].Priority, items[1].Priority);
	}

	[TestMethod]
	public void Parse_FallsBackToLabelWhenSortTextIsMissing()
	{
		CompletionItemPayload beta = DeserializeCompletionItemPayload(new { label = "beta", kind = 6 });
		CompletionItemPayload alpha = DeserializeCompletionItemPayload(new { label = "alpha", kind = 6 });

		IReadOnlyList<CompletionItem> items = CompletionResponseParser.Parse([beta, alpha], "text");

		Assert.HasCount(2, items);
		Assert.IsGreaterThan(items[0].Priority, items[1].Priority);
		Assert.AreEqual("alpha", items[1].Label);
	}

	[TestMethod]
	public void Parse_KeepsResponseOrderForEqualSortKeys()
	{
		CompletionItemPayload first = DeserializeCompletionItemPayload(
			new
			{
				label = "alpha",
				kind = 6,
				sortText = "0001",
			}
		);

		CompletionItemPayload second = DeserializeCompletionItemPayload(
			new
			{
				label = "beta",
				kind = 6,
				sortText = "0001",
			}
		);

		IReadOnlyList<CompletionItem> items = CompletionResponseParser.Parse([first, second], "text");

		Assert.HasCount(2, items);
		Assert.AreEqual("alpha", items[0].Label);
		Assert.AreEqual("beta", items[1].Label);
		Assert.IsGreaterThan(items[1].Priority, items[0].Priority);
	}

	[TestMethod]
	public void Parse_SurfacesPreselectedItemAboveProtocolRank()
	{
		CompletionItemPayload first = DeserializeCompletionItemPayload(
			new
			{
				label = "first",
				kind = 6,
				sortText = "0001",
			}
		);

		CompletionItemPayload preselected = DeserializeCompletionItemPayload(
			new
			{
				label = "preselected",
				kind = 6,
				sortText = "0002",
				preselect = true,
			}
		);

		IReadOnlyList<CompletionItem> items = CompletionResponseParser.Parse([first, preselected], "text");

		Assert.HasCount(2, items);
		Assert.IsGreaterThan(items[0].Priority, items[1].Priority);
	}

	[TestMethod]
	public void Parse_KeepsResponseOrderWhenSortTextTiesALabelFallback()
	{
		CompletionItemPayload explicitSortKey = DeserializeCompletionItemPayload(
			new
			{
				label = "alpha",
				kind = 6,
				sortText = "0002",
			}
		);

		CompletionItemPayload labelFallback = DeserializeCompletionItemPayload(new { label = "0002", kind = 6 });

		IReadOnlyList<CompletionItem> items = CompletionResponseParser.Parse([explicitSortKey, labelFallback], "text");

		// The ordering key mixes the explicit sortText with the label fallback; when both produce the same
		// key the server's response order still decides the rank.
		Assert.HasCount(2, items);
		Assert.AreEqual("alpha", items[0].Label);
		Assert.AreEqual("0002", items[1].Label);
		Assert.IsGreaterThan(items[1].Priority, items[0].Priority);
	}
}
