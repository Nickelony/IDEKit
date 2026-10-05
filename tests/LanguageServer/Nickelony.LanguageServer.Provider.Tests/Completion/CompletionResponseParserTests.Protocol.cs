using Nickelony.IDEKit.LanguageFeatures.Completion;
using static Nickelony.LanguageServer.Provider.Tests.PayloadTestHelpers;

namespace Nickelony.LanguageServer.Provider.Tests;

public sealed partial class CompletionResponseParserTests
{
	[TestMethod]
	public void ParseItem_MapsDeprecatedTagAndIgnoresUnknownValues()
	{
		Lsp.CompletionItemPayload deprecated = DeserializeCompletionItemPayload(new { label = "item", tags = new[] { 1 } });
		Lsp.CompletionItemPayload unknown = DeserializeCompletionItemPayload(new { label = "item", tags = new[] { 99 } });
		Lsp.CompletionItemPayload mixed = DeserializeCompletionItemPayload(new { label = "item", tags = new[] { 99, 1 } });

		CompletionItem? deprecatedItem = CompletionResponseParser.ParseItem(deprecated, 0, "text");
		CompletionItem? unknownItem = CompletionResponseParser.ParseItem(unknown, 0, "text");
		CompletionItem? mixedItem = CompletionResponseParser.ParseItem(mixed, 0, "text");

		Assert.IsNotNull(deprecatedItem);
		Assert.IsNotNull(unknownItem);
		Assert.IsNotNull(mixedItem);

		Assert.AreSequenceEqual([CompletionItemTag.Deprecated], [.. deprecatedItem.Tags]);
		Assert.IsEmpty(unknownItem.Tags);
		Assert.AreSequenceEqual([CompletionItemTag.Deprecated], [.. mixedItem.Tags]);
	}

	[TestMethod]
	public void ParseItem_MapsLegacyDeprecatedBoolean()
	{
		// LuaLS emits the deprecated boolean instead of the tags array; the parser must accept both.
		Lsp.CompletionItemPayload legacy = DeserializeCompletionItemPayload(new { label = "item", deprecated = true });
		Lsp.CompletionItemPayload notDeprecated = DeserializeCompletionItemPayload(new { label = "item", deprecated = false });

		Lsp.CompletionItemPayload both = DeserializeCompletionItemPayload(
			new
			{
				label = "item",
				deprecated = true,
				tags = new[] { 1 },
			}
		);

		CompletionItem? legacyItem = CompletionResponseParser.ParseItem(legacy, 0, "text");
		CompletionItem? notDeprecatedItem = CompletionResponseParser.ParseItem(notDeprecated, 0, "text");
		CompletionItem? bothItem = CompletionResponseParser.ParseItem(both, 0, "text");

		Assert.IsNotNull(legacyItem);
		Assert.IsNotNull(notDeprecatedItem);
		Assert.IsNotNull(bothItem);

		Assert.AreSequenceEqual([CompletionItemTag.Deprecated], [.. legacyItem.Tags]);
		Assert.IsEmpty(notDeprecatedItem.Tags);
		Assert.AreSequenceEqual([CompletionItemTag.Deprecated], [.. bothItem.Tags]);
	}

	[TestMethod]
	public void ParseItem_CarriesCommitCharactersVerbatim()
	{
		Lsp.CompletionItemPayload payload = DeserializeCompletionItemPayload(new { label = "item", commitCharacters = new[] { "(", "," } });

		CompletionItem? item = CompletionResponseParser.ParseItem(payload, 0, "text");

		Assert.IsNotNull(item);
		Assert.AreSequenceEqual(["(", ","], [.. item.CommitCharacters]);
	}

	[TestMethod]
	public void ParseItem_DropsNullCommitCharactersInsteadOfThrowing()
	{
		Lsp.CompletionItemPayload payload = DeserializeCompletionItemPayload(
			new Dictionary<string, object?>
			{
				["label"] = "item",

				// A non-conforming server can send a null entry; the shared model rejects one because it
				// can never match a typed character, so the parser filters it instead of failing the request.
				["commitCharacters"] = new object?[] { "(", null },
			}
		);

		CompletionItem? item = CompletionResponseParser.ParseItem(payload, 0, "text");

		Assert.IsNotNull(item);
		Assert.AreSequenceEqual(["("], [.. item.CommitCharacters]);
	}

	[TestMethod]
	public void ParseItem_ResolvesAdditionalTextEditRangesToOffsets()
	{
		Lsp.CompletionItemPayload payload = DeserializeCompletionItemPayload(
			new
			{
				label = "print",

				additionalTextEdits = new[]
				{
					new
					{
						range = new { start = new { line = 0, character = 6 }, end = new { line = 0, character = 6 } },
						newText = "local print = print\n",
					},
				},
			}
		);

		CompletionItem? item = CompletionResponseParser.ParseItem(payload, 0, "print()");

		Assert.IsNotNull(item);
		Assert.HasCount(1, item.AdditionalTextEdits);

		CompletionTextEdit edit = item.AdditionalTextEdits[0];

		Assert.AreEqual(6, edit.ReplacementRange.Offset);
		Assert.AreEqual(0, edit.ReplacementRange.Length);
		Assert.AreEqual("local print = print\n", edit.NewText);
	}

	[TestMethod]
	public void ParseItem_SkipsMalformedAdditionalTextEditsIndividually()
	{
		Lsp.CompletionItemPayload payload = DeserializeCompletionItemPayload(
			new
			{
				label = "print",

				additionalTextEdits = new object[]
				{
					new
					{
						range = new { start = new { line = 9, character = 0 }, end = new { line = 9, character = 1 } },
						newText = "out-of-range-line",
					},

					new { range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 0 } } },
					new { range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 0 } }, newText = "kept" },
				},
			}
		);

		CompletionItem? item = CompletionResponseParser.ParseItem(payload, 0, "print");

		Assert.IsNotNull(item);
		Assert.HasCount(1, item.AdditionalTextEdits);
		Assert.AreEqual("kept", item.AdditionalTextEdits[0].NewText);
	}

	[TestMethod]
	public void Parse_KeepsItemsThatDifferOnlyInAdditionalTextEdits()
	{
		Lsp.CompletionItemPayload plain = DeserializeCompletionItemPayload(new { label = "print", kind = 3 });

		Lsp.CompletionItemPayload withImport = DeserializeCompletionItemPayload(
			new
			{
				label = "print",
				kind = 3,

				additionalTextEdits = new[]
				{
					new
					{
						range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 0 } },
						newText = "local print = print\n",
					},
				},
			}
		);

		IReadOnlyList<CompletionItem> items = CompletionResponseParser.Parse([plain, withImport], "print");

		Assert.HasCount(2, items);
		Assert.IsEmpty(items[0].AdditionalTextEdits);
		Assert.HasCount(1, items[1].AdditionalTextEdits);
	}

	[TestMethod]
	public void Parse_KeepsItemsThatDifferOnlyInTags()
	{
		Lsp.CompletionItemPayload plain = DeserializeCompletionItemPayload(new { label = "print", kind = 3 });

		Lsp.CompletionItemPayload deprecated = DeserializeCompletionItemPayload(
			new
			{
				label = "print",
				kind = 3,
				tags = new[] { 1 },
			}
		);

		IReadOnlyList<CompletionItem> items = CompletionResponseParser.Parse([plain, deprecated], "print");

		Assert.HasCount(2, items);
	}

	[TestMethod]
	public void Parse_SkipsItemsWithoutAUsableLabel()
	{
		Lsp.CompletionItemPayload blank = DeserializeCompletionItemPayload(new { label = "   ", kind = 3 });
		Lsp.CompletionItemPayload missing = DeserializeCompletionItemPayload(new { kind = 3 });
		Lsp.CompletionItemPayload valid = DeserializeCompletionItemPayload(new { label = "print", kind = 3 });

		IReadOnlyList<CompletionItem> items = CompletionResponseParser.Parse([blank, missing, valid], "print");

		Assert.HasCount(1, items);
		Assert.AreEqual("print", items[0].Label);
	}

	[TestMethod]
	public void Parse_DetailWithoutDocumentation_StillRequestsResolve()
	{
		Lsp.CompletionItemPayload payload = DeserializeCompletionItemPayload(
			new
			{
				label = "spawn",
				kind = 3,
				detail = "function spawn()",
			}
		);

		int factoryCalls = 0;

		IReadOnlyList<CompletionItem> items = CompletionResponseParser.Parse(
			[payload],
			"spa",

			(item, _, _) =>
			{
				factoryCalls++;
				return _ => Task.FromResult(item);
			}
		);

		// A missing documentation field alone still benefits from a resolve round trip.
		Assert.HasCount(1, items);
		Assert.AreEqual(1, factoryCalls);
		Assert.IsTrue(items[0].CanResolve);
	}

	[TestMethod]
	public void Parse_KeepsItemsThatDifferOnlyInCommitCharacters()
	{
		Lsp.CompletionItemPayload plain = DeserializeCompletionItemPayload(new { label = "print", kind = 3 });

		Lsp.CompletionItemPayload withCommitCharacters = DeserializeCompletionItemPayload(
			new
			{
				label = "print",
				kind = 3,
				commitCharacters = new[] { "." },
			}
		);

		IReadOnlyList<CompletionItem> items = CompletionResponseParser.Parse([plain, withCommitCharacters], "print");

		Assert.HasCount(2, items);
		Assert.IsEmpty(items[0].CommitCharacters);
		Assert.HasCount(1, items[1].CommitCharacters);
	}

	[TestMethod]
	public void Parse_AttachesOneResolveCallbackPerItem()
	{
		Lsp.CompletionItemPayload first = DeserializeCompletionItemPayload(
			new
			{
				label = "spawn",
				kind = 3,
				detail = "function spawn()",
			}
		);

		Lsp.CompletionItemPayload second = DeserializeCompletionItemPayload(
			new
			{
				label = "spawn",
				kind = 3,
				detail = "function spawn()",
				preselect = true,
			}
		);

		int factoryCalls = 0;

		IReadOnlyList<CompletionItem> items = CompletionResponseParser.Parse(
			[first, second],
			"spa",

			(item, _, _) =>
			{
				factoryCalls++;
				return _ => Task.FromResult(item);
			}
		);

		// Every item carries its own resolve callback, so two server-identical entries produce two
		// independent round trips.
		Assert.HasCount(2, items);
		Assert.AreEqual(2, factoryCalls);
		Assert.IsTrue(items[0].CanResolve);
		Assert.IsTrue(items[1].CanResolve);
	}
}
