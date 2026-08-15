using Nickelony.IDEKit.IntelliSense.Completion;
using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

/// <summary>
/// Covers the completion resolve contract: when a callback is attached, transport retries, and how
/// failures and skipped resolutions fall back to the unresolved item.
/// </summary>
public sealed partial class LuaLanguageServerIntelliSenseProviderTests
{
	[TestMethod]
	public async Task GetCompletionItemsAsync_WhenResolveIsUnsupported_LeavesItemsUnresolvable()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			CompletionResponse = JsonSerializer.SerializeToElement(new
			{
				items = new object[]
				{
					new { label = "spawn", kind = 3 }
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextCompletionItem> items = await provider.GetCompletionItemsAsync(new LanguageServerCompletionRequest(filePath, "spa", new TextPosition(0, 3)));

		Assert.AreEqual(1, items.Count);
		Assert.IsFalse(items[0].CanResolve);
		Assert.AreSame(items[0], await items[0].ResolveAsync());
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_ItemWithDetailAndDocumentation_IsNotResolved()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			SupportsCompletionResolve = true,
			CompletionResponse = JsonSerializer.SerializeToElement(new
			{
				items = new object[]
				{
					new
					{
						label = "spawn",
						kind = 3,
						detail = "function spawn(room, objectName)",
						documentation = "Spawns an object."
					}
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextCompletionItem> items = await provider.GetCompletionItemsAsync(new LanguageServerCompletionRequest(filePath, "spa", new TextPosition(0, 3)));

		// The item already carries everything resolve could add, so no resolve callback is attached and
		// no round trip is scheduled.
		Assert.AreEqual(1, items.Count);
		Assert.IsFalse(items[0].CanResolve);
		CollectionAssert.DoesNotContain(client.GetSentMethodNames(), "completionItem/resolve");
	}

	[TestMethod]
	public async Task ResolveAsync_AfterTransportChangeFailure_RetriesOnceAndAdoptsTheResolvedContent()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			SupportsCompletionResolve = true,
			CompletionResponse = JsonSerializer.SerializeToElement(new
			{
				items = new object[]
				{
					new { label = "spawn", kind = 3 }
				}
			}),
			CompletionResolveResponse = JsonSerializer.SerializeToElement(new
			{
				label = "spawn",
				kind = 3,
				detail = "function spawn(room, objectName)",
				documentation = "Spawns an object."
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextCompletionItem> items = await provider.GetCompletionItemsAsync(new LanguageServerCompletionRequest(filePath, "spa", new TextPosition(0, 3)));

		Assert.AreEqual(1, items.Count);
		Assert.IsTrue(items[0].CanResolve);

		// The resolve request crosses a transport boundary; the dispatcher restarts and retries once.
		client.TransportChangedRequestFailuresRemaining = 1;

		TextCompletionItem resolvedItem = await items[0].ResolveAsync();

		// The transport change is retried once and the resolved content is adopted.
		Assert.AreEqual("function spawn(room, objectName)", resolvedItem.Detail);
		Assert.AreEqual("Spawns an object.", resolvedItem.Documentation);
		Assert.AreEqual(2, CountSentMethods(client, "completionItem/resolve"));
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_ResolveFailure_FallsBackToTheUnresolvedItem()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			SupportsCompletionResolve = true,
			ThrowInvalidOperationOnNextRequestMethod = "completionItem/resolve",
			CompletionResponse = JsonSerializer.SerializeToElement(new
			{
				items = new object[]
				{
					new { label = "spawn", kind = 3 }
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextCompletionItem> items = await provider.GetCompletionItemsAsync(new LanguageServerCompletionRequest(filePath, "spa", new TextPosition(0, 3)));

		Assert.AreEqual(1, items.Count);
		Assert.IsTrue(items[0].CanResolve);

		TextCompletionItem resolvedItem = await items[0].ResolveAsync();

		// The failure is contained: the caller receives an item equivalent to the unresolved one and
		// carrying no resolved content.
		Assert.AreEqual(items[0].Label, resolvedItem.Label);
		Assert.AreEqual("spawn", resolvedItem.InsertText);
		Assert.IsNull(resolvedItem.Detail);
		Assert.IsNull(resolvedItem.Documentation);
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_ItemWithDataAndNoAdditionalEdits_CanResolve()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			SupportsCompletionResolve = true,
			CompletionResponse = JsonSerializer.SerializeToElement(new
			{
				items = new object[]
				{
					new
					{
						label = "spawn",
						kind = 3,
						detail = "function spawn(room, objectName)",
						documentation = "Spawns an object.",
						data = new { completionId = 7 }
					}
				}
			}),
			CompletionResolveResponse = JsonSerializer.SerializeToElement(new
			{
				label = "spawn",
				kind = 3,
				detail = "function spawn(room, objectName)",
				documentation = "Spawns an object.",
				additionalTextEdits = new object[]
				{
					new
					{
						range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 0 } },
						newText = "local spawn = require('spawn')\n"
					}
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextCompletionItem> items = await provider.GetCompletionItemsAsync(new LanguageServerCompletionRequest(filePath, "spa", new TextPosition(0, 3)));

		// Detail and documentation are present, but LuaLS signals resolve-only additions through the item's
		// opaque data member, so a resolve callback is attached and the additions arrive on demand.
		Assert.AreEqual(1, items.Count);
		Assert.IsTrue(items[0].CanResolve);

		TextCompletionItem resolvedItem = await items[0].ResolveAsync();

		Assert.AreEqual(1, resolvedItem.AdditionalTextEdits.Count);
	}

	[TestMethod]
	public async Task ResolveAsync_AfterTrackedContentChanged_DropsResolvedTextEdits()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			SupportsCompletionResolve = true,
			CompletionResponse = JsonSerializer.SerializeToElement(new
			{
				items = new object[]
				{
					new { label = "spawn", kind = 3 }
				}
			}),
			CompletionResolveResponse = JsonSerializer.SerializeToElement(new
			{
				label = "spawn",
				kind = 3,
				detail = "function spawn(room, objectName)",
				documentation = "Spawns an object.",
				additionalTextEdits = new object[]
				{
					new
					{
						range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 0 } },
						newText = "local spawn = require('spawn')\n"
					}
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextCompletionItem> items = await provider.GetCompletionItemsAsync(new LanguageServerCompletionRequest(filePath, "spa", new TextPosition(0, 3)));
		Assert.IsTrue(items[0].CanResolve);

		// The document moves on before the resolve completes: the resolved payload's offsets no longer describe
		// the current text, so the edit fields are dropped while the content fields are still adopted.
		provider.UpdateDocument(filePath, "spa.");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, TestPolling.DefaultTimeout));

		TextCompletionItem resolvedItem = await items[0].ResolveAsync();

		Assert.AreEqual("function spawn(room, objectName)", resolvedItem.Detail);
		Assert.AreEqual("Spawns an object.", resolvedItem.Documentation);
		Assert.AreEqual(0, resolvedItem.AdditionalTextEdits.Count);
	}
}
