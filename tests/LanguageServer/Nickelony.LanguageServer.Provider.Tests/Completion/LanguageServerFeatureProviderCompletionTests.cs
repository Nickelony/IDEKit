using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Markup;
using Nickelony.IDEKit.LanguageFeatures.Rename;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers the completion requests of the framework pipeline: the request context, the mapped items, the connection retry, and item resolution.
/// </summary>
[TestClass]
public sealed class LanguageServerFeatureProviderCompletionTests
{
	[TestMethod]
	public async Task GetCompletionItemsAsync_ResolvesCompletionItemDetailsWhenServerSupportsResolve()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			SupportsCompletionResolve = true,

			CompletionResponse = JsonSerializer.SerializeToElement(
				new
				{
					items = new object[]
					{
						new
						{
							label = "spawn",
							kind = 3,
							insertText = "spawn",
							data = new { completionId = 7, source = "server" },
						},
					},
				}
			),

			CompletionResolveResponse = JsonSerializer.SerializeToElement(
				new
				{
					label = "spawn",
					kind = 3,
					insertText = "spawn",
					detail = "function",
					documentation = "Spawn docs.",
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<CompletionItem> items = await provider.GetCompletionItemsAsync(
			new CompletionRequest(filePath, "spa", 3),
			TestContext.CancellationToken
		);

		CompletionItem resolvedItem = await items[0].ResolveAsync(TestContext.CancellationToken);

		Assert.HasCount(1, items);
		Assert.IsTrue(items[0].CanResolve);
		Assert.AreEqual("function", resolvedItem.Detail);
		Assert.AreEqual("Spawn docs.", resolvedItem.Documentation?.Value);
		Assert.AreEqual(7, client.GetLastRequestParameters("completionItem/resolve").GetProperty("data").GetProperty("completionId").GetInt32());

		Assert.AreSequenceEqual(["textDocument/didOpen", "textDocument/completion", "completionItem/resolve"], client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_ResolveRequest_WritesTheItemDocumentationAsMarkupContent()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			SupportsCompletionResolve = true,

			CompletionResponse = JsonSerializer.SerializeToElement(
				new
				{
					items = new object[]
					{
						new
						{
							label = "spawn",
							kind = 3,
							detail = "function",
							documentation = "Spawn docs.",
							data = new { completionId = 7 },
						},
					},
				}
			),

			CompletionResolveResponse = JsonSerializer.SerializeToElement(
				new
				{
					label = "spawn",
					kind = 3,
					detail = "function",
					documentation = new { kind = "markdown", value = "Resolved docs." },
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<CompletionItem> items = await provider.GetCompletionItemsAsync(
			new CompletionRequest(filePath, "spa", 3),
			TestContext.CancellationToken
		);

		Assert.HasCount(1, items);
		Assert.IsTrue(items[0].CanResolve);

		CompletionItem resolvedItem = await items[0].ResolveAsync(TestContext.CancellationToken);

		// A bare string from the server is plain text, and it goes back in the resolve request as MarkupContent.
		JsonElement sentDocumentation = client.GetLastRequestParameters("completionItem/resolve").GetProperty("documentation");

		Assert.AreEqual("plaintext", sentDocumentation.GetProperty("kind").GetString());
		Assert.AreEqual("Spawn docs.", sentDocumentation.GetProperty("value").GetString());
		Assert.AreEqual("Resolved docs.", resolvedItem.Documentation?.Value);
		Assert.AreEqual(MarkupKind.Markdown, resolvedItem.Documentation?.Kind);
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_ResolvePreservesOriginalInsertionMetadata()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			SupportsCompletionResolve = true,

			CompletionResponse = JsonSerializer.SerializeToElement(
				new
				{
					items = new object[]
					{
						new
						{
							label = "spawn",
							kind = 3,

							textEdit = new
							{
								newText = "spawn",
								range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 3 } },
							},
						},
					},
				}
			),

			CompletionResolveResponse = JsonSerializer.SerializeToElement(
				new
				{
					label = "spawn",
					kind = 3,
					insertText = "shouldNotReplaceOriginalInsertText",
					detail = "function",
					documentation = "Spawn docs.",
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<CompletionItem> items = await provider.GetCompletionItemsAsync(
			new CompletionRequest(filePath, "spa", 3),
			TestContext.CancellationToken
		);

		CompletionItem resolvedItem = await items[0].ResolveAsync(TestContext.CancellationToken);

		Assert.AreEqual("spawn", resolvedItem.InsertText);
		Assert.AreEqual(items[0].TextEdit, resolvedItem.TextEdit);
		Assert.AreEqual("function", resolvedItem.Detail);
		Assert.AreEqual("Spawn docs.", resolvedItem.Documentation?.Value);
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_ResolveAdoptsLateInsertionDataWhenOriginalReliesOnLabel()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			SupportsCompletionResolve = true,
			CompletionResponse = JsonSerializer.SerializeToElement(new { items = new object[] { new { label = "spawn", kind = 3 } } }),

			CompletionResolveResponse = JsonSerializer.SerializeToElement(
				new
				{
					label = "spawn",
					kind = 3,
					insertText = "spawn($0)",
					insertTextFormat = 2,
					detail = "function",
					documentation = "Spawn docs.",
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<CompletionItem> items = await provider.GetCompletionItemsAsync(
			new CompletionRequest(filePath, "spa", 3),
			TestContext.CancellationToken
		);

		CompletionItem resolvedItem = await items[0].ResolveAsync(TestContext.CancellationToken);

		Assert.AreEqual("spawn", items[0].InsertText);
		Assert.IsNull(items[0].TextEdit);
		Assert.AreEqual(CompletionInsertTextFormat.PlainText, items[0].InsertTextFormat);
		Assert.AreEqual("spawn($0)", resolvedItem.InsertText);
		Assert.AreEqual(CompletionInsertTextFormat.Snippet, resolvedItem.InsertTextFormat);
		Assert.AreEqual("function", resolvedItem.Detail);
		Assert.AreEqual("Spawn docs.", resolvedItem.Documentation?.Value);
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_OrdersPrioritiesByProtocolSortText()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			CompletionResponse = JsonSerializer.SerializeToElement(
				new
				{
					items = new object[]
					{
						new
						{
							label = "late",
							kind = 5,
							sortText = "0002",
						},

						new
						{
							label = "early",
							kind = 6,
							sortText = "0001",
						},
					},
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<CompletionItem> items = await provider.GetCompletionItemsAsync(
			new CompletionRequest(filePath, "loc", 3),
			TestContext.CancellationToken
		);

		CompletionItem lateItem = items.Single(item => item.Label == "late");
		CompletionItem earlyItem = items.Single(item => item.Label == "early");

		// The server's sortText is the protocol ordering key; the priority hint must agree with it
		// even when the response order differs.
		Assert.AreEqual("0001", earlyItem.SortText);
		Assert.AreEqual("0002", lateItem.SortText);
		Assert.IsGreaterThan(lateItem.Priority, earlyItem.Priority);
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_WithTriggerCharacter_PassesCompletionContext()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			CompletionResponse = JsonSerializer.SerializeToElement(new { items = Array.Empty<object>() }),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<CompletionItem> items = await provider.GetCompletionItemsAsync(
			new CompletionRequest(documentId: filePath, documentText: "spawn.", caretOffset: 6, context: CompletionContext.ForCharacter('.')),
			TestContext.CancellationToken
		);

		JsonElement parameters = client.GetLastRequestParameters("textDocument/completion");

		Assert.IsEmpty(items);
		Assert.AreEqual(new Uri(filePath).AbsoluteUri, parameters.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.AreEqual(0, parameters.GetProperty("position").GetProperty("line").GetInt32());
		Assert.AreEqual(6, parameters.GetProperty("position").GetProperty("character").GetInt32());
		Assert.AreEqual(2, parameters.GetProperty("context").GetProperty("triggerKind").GetInt32());
		Assert.AreEqual(".", parameters.GetProperty("context").GetProperty("triggerCharacter").GetString());
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_OffsetOnALaterLine_SendsThatLineAndCharacter()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			CompletionResponse = JsonSerializer.SerializeToElement(new { items = Array.Empty<object>() }),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		// Offset 21 is the fourth character of the third line: two seven-character lines and their CRLF terminators come first.
		await provider.GetCompletionItemsAsync(new CompletionRequest(filePath, "local a\r\nlocal b\r\nspawn", 21), TestContext.CancellationToken);

		JsonElement position = client.GetLastRequestParameters("textDocument/completion").GetProperty("position");

		Assert.AreEqual(2, position.GetProperty("line").GetInt32());
		Assert.AreEqual(3, position.GetProperty("character").GetInt32());
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_WithoutTriggerCharacter_PassesInvokedContext()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			CompletionResponse = JsonSerializer.SerializeToElement(new { items = Array.Empty<object>() }),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		await provider.GetCompletionItemsAsync(new CompletionRequest(filePath, "loc", 3), TestContext.CancellationToken);

		JsonElement context = client.GetLastRequestParameters("textDocument/completion").GetProperty("context");

		// An invoked request carries no trigger character.
		Assert.AreEqual(1, context.GetProperty("triggerKind").GetInt32());
		Assert.IsFalse(context.TryGetProperty("triggerCharacter", out _));
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_WithWhitespaceTriggerCharacter_PassesTheCharacterToTheServer()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			CompletionResponse = JsonSerializer.SerializeToElement(new { items = Array.Empty<object>() }),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		await provider.GetCompletionItemsAsync(
			new CompletionRequest(documentId: filePath, documentText: "loc ", caretOffset: 4, context: CompletionContext.ForCharacter(' ')),
			TestContext.CancellationToken
		);

		JsonElement context = client.GetLastRequestParameters("textDocument/completion").GetProperty("context");

		// A whitespace trigger character is a typed character like any other, so the server receives it as the trigger.
		Assert.AreEqual(2, context.GetProperty("triggerKind").GetInt32());
		Assert.AreEqual(" ", context.GetProperty("triggerCharacter").GetString());
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_InvalidFilePath_ReturnsEmptyWithoutSending()
	{
		string workspaceRoot = TestPaths.Root;

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<CompletionItem> items = await provider.GetCompletionItemsAsync(
			new CompletionRequest(TestPaths.InvalidFile, "local value = 1", 0),
			TestContext.CancellationToken
		);

		Assert.IsEmpty(items);
		Assert.IsEmpty(client.GetSentMethodNames());
		Assert.AreEqual(0, client.StartCallCount);
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_WhenResolveIsUnsupported_LeavesItemsUnresolvable()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			CompletionResponse = JsonSerializer.SerializeToElement(new { items = new object[] { new { label = "spawn", kind = 3 } } }),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<CompletionItem> items = await provider.GetCompletionItemsAsync(
			new CompletionRequest(filePath, "spa", 3),
			TestContext.CancellationToken
		);

		Assert.HasCount(1, items);
		Assert.IsFalse(items[0].CanResolve);
		Assert.AreSame(items[0], await items[0].ResolveAsync(TestContext.CancellationToken));
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_ItemWithDetailAndDocumentation_IsNotResolved()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			SupportsCompletionResolve = true,

			CompletionResponse = JsonSerializer.SerializeToElement(
				new
				{
					items = new object[]
					{
						new
						{
							label = "spawn",
							kind = 3,
							detail = "function spawn(room, objectName)",
							documentation = "Spawns an object.",
						},
					},
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<CompletionItem> items = await provider.GetCompletionItemsAsync(
			new CompletionRequest(filePath, "spa", 3),
			TestContext.CancellationToken
		);

		// The item already carries everything resolve could add, so no resolve callback is attached and
		// no round trip is scheduled.
		Assert.HasCount(1, items);
		Assert.IsFalse(items[0].CanResolve);
		Assert.DoesNotContain("completionItem/resolve", client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task ResolveAsync_AfterConnectionChangeFailure_RetriesOnceAndAdoptsTheResolvedContent()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			SupportsCompletionResolve = true,
			CompletionResponse = JsonSerializer.SerializeToElement(new { items = new object[] { new { label = "spawn", kind = 3 } } }),

			CompletionResolveResponse = JsonSerializer.SerializeToElement(
				new
				{
					label = "spawn",
					kind = 3,
					detail = "function spawn(room, objectName)",
					documentation = "Spawns an object.",
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<CompletionItem> items = await provider.GetCompletionItemsAsync(
			new CompletionRequest(filePath, "spa", 3),
			TestContext.CancellationToken
		);

		Assert.HasCount(1, items);
		Assert.IsTrue(items[0].CanResolve);

		// The resolve request crosses a connection boundary; the request runner restarts and retries once.
		client.ConnectionChangedRequestFailuresRemaining = 1;

		CompletionItem resolvedItem = await items[0].ResolveAsync(TestContext.CancellationToken);

		// The connection change is retried once and the resolved content is adopted.
		Assert.AreEqual("function spawn(room, objectName)", resolvedItem.Detail);
		Assert.AreEqual("Spawns an object.", resolvedItem.Documentation?.Value);
		Assert.AreEqual(2, client.GetSentMethodCount("completionItem/resolve"));
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_ResolveFailure_FallsBackToTheUnresolvedItem()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			SupportsCompletionResolve = true,
			ThrowInvalidOperationOnNextRequestMethod = "completionItem/resolve",
			CompletionResponse = JsonSerializer.SerializeToElement(new { items = new object[] { new { label = "spawn", kind = 3 } } }),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<CompletionItem> items = await provider.GetCompletionItemsAsync(
			new CompletionRequest(filePath, "spa", 3),
			TestContext.CancellationToken
		);

		Assert.HasCount(1, items);
		Assert.IsTrue(items[0].CanResolve);

		CompletionItem resolvedItem = await items[0].ResolveAsync(TestContext.CancellationToken);

		// The failure is contained: the caller receives an item equivalent to the unresolved one and
		// carrying no resolved content.
		Assert.AreEqual(items[0].Label, resolvedItem.Label);
		Assert.AreEqual("spawn", resolvedItem.InsertText);
		Assert.IsNull(resolvedItem.Detail);
		Assert.IsNull(resolvedItem.Documentation?.Value);
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_ItemWithDataAndNoAdditionalEdits_CanResolve()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			SupportsCompletionResolve = true,

			CompletionResponse = JsonSerializer.SerializeToElement(
				new
				{
					items = new object[]
					{
						new
						{
							label = "spawn",
							kind = 3,
							detail = "function spawn(room, objectName)",
							documentation = "Spawns an object.",
							data = new { completionId = 7 },
						},
					},
				}
			),

			CompletionResolveResponse = JsonSerializer.SerializeToElement(
				new
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
							newText = "local spawn = require('spawn')\n",
						},
					},
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<CompletionItem> items = await provider.GetCompletionItemsAsync(
			new CompletionRequest(filePath, "spa", 3),
			TestContext.CancellationToken
		);

		// Detail and documentation are present, but a server such as LuaLS signals resolve-only additions through the item's
		// opaque data member, so a resolve callback is attached and the additions arrive on demand.
		Assert.HasCount(1, items);
		Assert.IsTrue(items[0].CanResolve);

		CompletionItem resolvedItem = await items[0].ResolveAsync(TestContext.CancellationToken);

		Assert.HasCount(1, resolvedItem.AdditionalTextEdits);
	}

	[TestMethod]
	public async Task ResolveAsync_AfterTrackedContentChanged_DropsResolvedTextEdits()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			SupportsCompletionResolve = true,
			CompletionResponse = JsonSerializer.SerializeToElement(new { items = new object[] { new { label = "spawn", kind = 3 } } }),

			CompletionResolveResponse = JsonSerializer.SerializeToElement(
				new
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
							newText = "local spawn = require('spawn')\n",
						},
					},
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<CompletionItem> items = await provider.GetCompletionItemsAsync(
			new CompletionRequest(filePath, "spa", 3),
			TestContext.CancellationToken
		);

		Assert.IsTrue(items[0].CanResolve);

		// The document moves on before the resolve completes: the resolved payload's offsets do not describe
		// the current text, so the edit fields are dropped while the content fields are still adopted.
		provider.UpdateDocument(filePath, "spa.");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, TestPolling.DefaultTimeout));

		CompletionItem resolvedItem = await items[0].ResolveAsync(TestContext.CancellationToken);

		Assert.AreEqual("function spawn(room, objectName)", resolvedItem.Detail);
		Assert.AreEqual("Spawns an object.", resolvedItem.Documentation?.Value);
		Assert.IsEmpty(resolvedItem.AdditionalTextEdits);
	}

	public TestContext? TestContext { get; set; }
}
