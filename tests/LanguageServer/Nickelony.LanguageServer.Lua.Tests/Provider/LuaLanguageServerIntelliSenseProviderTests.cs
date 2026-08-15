using Nickelony.IDEKit.IntelliSense.Completion;
using Nickelony.IDEKit.IntelliSense.Hover;
using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

[TestClass]
public sealed partial class LuaLanguageServerIntelliSenseProviderTests
{
	[TestMethod]
	public async Task Provider_IsUnavailableAndReportsNoCapabilitiesBeforeLazyStartup()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		int capabilitiesChangedCount = 0;

		provider.CapabilitiesChanged += (_, _) => capabilitiesChangedCount++;

		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);
		Assert.IsFalse(provider.IsAvailable);
		Assert.IsFalse(provider.SupportsReferences);
		Assert.IsFalse(provider.SupportsRename);
		Assert.IsFalse(provider.SupportsFormatting);

		await provider.GetHoverAsync(filePath, "local value = 1", new TextPosition(0, 0));

		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
		Assert.IsTrue(provider.IsAvailable);
		Assert.IsTrue(provider.SupportsReferences);
		Assert.IsTrue(provider.SupportsRename);
		Assert.IsTrue(provider.SupportsFormatting);
		Assert.AreEqual(1, capabilitiesChangedCount);
	}

	[TestMethod]
	public async Task MissingServerExecutable_RaisesOnePersistentStartupFailure()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1";

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], serverExecutablePath: null);
		var failures = new List<LanguageServerStartupFailure>();

		provider.StartupFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

		Assert.IsNull(await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0)).ConfigureAwait(false));
		Assert.IsNull(await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0)).ConfigureAwait(false));

		Assert.AreEqual(LanguageServerProviderState.Failed, provider.State);
		Assert.AreEqual(1, failures.Count);
		Assert.IsTrue(failures[0].IsPersistent);
		StringAssert.Contains(failures[0].Message, "executable is unavailable");
	}

	[TestMethod]
	public async Task OpenDocument_SendsDidOpenPayloadWithLuaLanguageIdAndInitialDocumentVersion()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		JsonElement parameters = client.GetLastNotificationParameters("textDocument/didOpen");
		JsonElement textDocument = parameters.GetProperty("textDocument");

		Assert.AreEqual(new Uri(filePath).AbsoluteUri, textDocument.GetProperty("uri").GetString());
		Assert.AreEqual("lua", textDocument.GetProperty("languageId").GetString());
		Assert.AreEqual(1, textDocument.GetProperty("version").GetInt32());
		Assert.AreEqual(content, textDocument.GetProperty("text").GetString());
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task GetHoverAsync_DisposeWhileRequestsShareBlockedStartup_BothReturnNullWithoutDisposedExceptions()
	{
		string workspaceRoot = TestPaths.Root;
		string firstFilePath = TestPaths.Script("test.lua");
		string secondFilePath = TestPaths.Script("other.lua");

		using var client = new FakeLanguageServerClient();
		client.BlockNextStartAsync();

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		int startupLockWaitCount = 0;
		var secondStartupLockWaitObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		LuaLanguageServerIntelliSenseProviderTestAccess.SetStartupLockWaitObserver(provider, () =>
		{
			if (Interlocked.Increment(ref startupLockWaitCount) >= 2)
				secondStartupLockWaitObserved.TrySetResult(true);
		});

		// The first request drives the startup and parks inside the blocked start call while it holds
		// the provider start lock; the second request then waits for that lock. Both must surface the
		// documented disposal fallback when the provider is disposed while the startup is pending.
		Task<TextHoverInfo?> firstHoverTask = provider.GetHoverAsync(firstFilePath, "local value = 1", new TextPosition(0, 0));

		await TestPolling.UntilAsync(
			() => client.StartCallCount > 0,
			TestPolling.DefaultTimeout,
			"The first request must reach the blocked startup.").ConfigureAwait(false);

		Assert.AreEqual(1, client.StartCallCount);

		Task<TextHoverInfo?> secondHoverTask = provider.GetHoverAsync(secondFilePath, "local value = 2", new TextPosition(0, 0));

		// The observer fires once per caller reaching the start lock, so the second request is provably parked
		// on the shared startup - whose holder is still inside the blocked start call - before the disposal.
		await secondStartupLockWaitObserved.Task.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		provider.Dispose();
		client.ReleaseStartAsync();

		Assert.IsNull(await firstHoverTask.ConfigureAwait(false));
		Assert.IsNull(await secondHoverTask.ConfigureAwait(false));
		Assert.AreEqual(1, client.DisposeCallCount);

		// No startup may complete once the provider is disposed. The second request can surface one
		// extra canceled start attempt when its start-lock acquisition races the disposal token
		// cancellation, so only the completed-start invariant is asserted here; the deterministic
		// shared-startup shape is pinned by the parallel-request robustness test.
		Assert.AreEqual(0L, client.TransportGeneration);
	}

	[TestMethod]
	public async Task GetHoverAsync_DisposeDuringInFlightRequest_ReturnsNullWithoutLateResult()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			HoverResponse = JsonSerializer.SerializeToElement(new
			{
				contents = new
				{
					kind = "markdown",
					value = "Hover docs."
				}
			})
		};

		client.BlockNextHoverRequest();

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		Task<TextHoverInfo?> hoverTask = provider.GetHoverAsync(filePath, "local value = 1", new TextPosition(0, 0));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/hover", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));

		provider.Dispose();
		client.ReleaseHoverRequest();

		Assert.IsNull(await hoverTask.ConfigureAwait(false));
	}

	[TestMethod]
	public async Task GetHoverAsync_DisposeDuringBlockedStart_ReturnsNullWithoutLeakingStartTask()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			IsReady = false,
			HoverResponse = JsonSerializer.SerializeToElement(new
			{
				contents = new
				{
					kind = "markdown",
					value = "Hover docs."
				}
			})
		};

		client.BlockNextStartAsync();

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		Task<TextHoverInfo?> hoverTask = provider.GetHoverAsync(filePath, "local value = 1", new TextPosition(0, 0));

		// The fake records the start call before parking on the gate, so the call count is the
		// observable signal that the hover task reached the blocked startup before disposal.
		await TestPolling.UntilAsync(
			() => client.StartCallCount > 0,
			TestPolling.DefaultTimeout,
			"The hover task must reach the blocked startup before disposal.").ConfigureAwait(false);

		Assert.AreEqual(1, client.StartCallCount);

		provider.Dispose();

		Assert.IsNull(await hoverTask.ConfigureAwait(false));
		Assert.AreEqual(1, client.StartCallCount);
		Assert.AreEqual(1, client.StartCancellationTokenCanBeCanceled.Count);
	}

	[TestMethod]
	public void Dispose_CancelsTheDisposeTokenAndLeavesItLinkable()
	{
		string workspaceRoot = TestPaths.Root;

		var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		CancellationTokenSource disposeCts = GetProviderDisposeCancellationTokenSource(provider);

		provider.Dispose();
		provider.Dispose();

		Assert.AreEqual(1, client.DisposeCallCount);
		Assert.IsTrue(disposeCts.IsCancellationRequested);

		// This is a deliberate direct seam: the invariant is not observable through the public
		// surface. Post-disposal request paths short-circuit on the disposed flag before they link
		// against the cached dispose token, and the failure mode it guards - disposing the source would
		// make concurrent in-flight linking throw ObjectDisposedException instead of returning the
		// documented disposal fallback - sits in an unschedulable race window. Pinning the token state
		// directly is the only deterministic guard against re-adding `_disposeCts.Dispose()` here.
		using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(disposeCts.Token);
		Assert.IsTrue(linkedSource.IsCancellationRequested);
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_ResolvesCompletionItemDetailsWhenServerSupportsResolve()
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
						insertText = "spawn",
						data = new
						{
							completionId = 7,
							source = "server"
						}
					}
				}
			}),
			CompletionResolveResponse = JsonSerializer.SerializeToElement(new
			{
				label = "spawn",
				kind = 3,
				insertText = "spawn",
				detail = "function",
				documentation = "Spawn docs."
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		IReadOnlyList<TextCompletionItem> items = await provider.GetCompletionItemsAsync(new LanguageServerCompletionRequest(filePath, "spa", new TextPosition(0, 3)));
		TextCompletionItem resolvedItem = await items[0].ResolveAsync();

		Assert.AreEqual(1, items.Count);
		Assert.IsTrue(items[0].CanResolve);
		Assert.AreEqual("function", resolvedItem.Detail);
		Assert.AreEqual("Spawn docs.", resolvedItem.Documentation);
		Assert.AreEqual(7, client.GetLastRequestParameters("completionItem/resolve").GetProperty("data").GetProperty("completionId").GetInt32());

		CollectionAssert.AreEqual(
			new[] { "textDocument/didOpen", "textDocument/completion", "completionItem/resolve" },
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_ResolvePreservesOriginalInsertionMetadata()
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
						textEdit = new
						{
							newText = "spawn",
							range = new
							{
								start = new { line = 0, character = 0 },
								end = new { line = 0, character = 3 }
							}
						}
					}
				}
			}),
			CompletionResolveResponse = JsonSerializer.SerializeToElement(new
			{
				label = "spawn",
				kind = 3,
				insertText = "shouldNotReplaceOriginalInsertText",
				detail = "function",
				documentation = "Spawn docs."
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		IReadOnlyList<TextCompletionItem> items = await provider.GetCompletionItemsAsync(new LanguageServerCompletionRequest(filePath, "spa", new TextPosition(0, 3)));
		TextCompletionItem resolvedItem = await items[0].ResolveAsync();

		Assert.AreEqual("spawn", resolvedItem.InsertText);
		Assert.AreEqual(items[0].TextEdit, resolvedItem.TextEdit);
		Assert.AreEqual("function", resolvedItem.Detail);
		Assert.AreEqual("Spawn docs.", resolvedItem.Documentation);
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_ResolveAdoptsLateInsertionDataWhenOriginalReliesOnLabel()
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
						kind = 3
					}
				}
			}),
			CompletionResolveResponse = JsonSerializer.SerializeToElement(new
			{
				label = "spawn",
				kind = 3,
				insertText = "spawn($0)",
				insertTextFormat = 2,
				detail = "function",
				documentation = "Spawn docs."
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		IReadOnlyList<TextCompletionItem> items = await provider.GetCompletionItemsAsync(new LanguageServerCompletionRequest(filePath, "spa", new TextPosition(0, 3)));
		TextCompletionItem resolvedItem = await items[0].ResolveAsync();

		Assert.AreEqual("spawn", items[0].InsertText);
		Assert.IsNull(items[0].TextEdit);
		Assert.AreEqual(TextCompletionInsertTextFormat.PlainText, items[0].InsertTextFormat);
		Assert.AreEqual("spawn($0)", resolvedItem.InsertText);
		Assert.AreEqual(TextCompletionInsertTextFormat.Snippet, resolvedItem.InsertTextFormat);
		Assert.AreEqual("function", resolvedItem.Detail);
		Assert.AreEqual("Spawn docs.", resolvedItem.Documentation);
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_OrdersPrioritiesByProtocolSortText()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			CompletionResponse = JsonSerializer.SerializeToElement(new
			{
				items = new object[]
				{
					new
					{
						label = "late",
						kind = 5,
						sortText = "0002"
					},
					new
					{
						label = "early",
						kind = 6,
						sortText = "0001"
					}
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextCompletionItem> items = await provider.GetCompletionItemsAsync(new LanguageServerCompletionRequest(filePath, "loc", new TextPosition(0, 3)));

		TextCompletionItem lateItem = items.Single(item => item.Label == "late");
		TextCompletionItem earlyItem = items.Single(item => item.Label == "early");

		// The server's sortText is the protocol ordering key; the priority hint must agree with it
		// even when the response order differs.
		Assert.AreEqual("0001", earlyItem.SortText);
		Assert.AreEqual("0002", lateItem.SortText);
		Assert.IsTrue(earlyItem.Priority > lateItem.Priority);
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_WithTriggerCharacter_PassesCompletionContext()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			CompletionResponse = JsonSerializer.SerializeToElement(new { items = Array.Empty<object>() })
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextCompletionItem> items = await provider.GetCompletionItemsAsync(new LanguageServerCompletionRequest(filePath, "spawn.", new TextPosition(0, 6), "."));
		JsonElement parameters = client.GetLastRequestParameters("textDocument/completion");

		Assert.AreEqual(0, items.Count);
		Assert.AreEqual(new Uri(filePath).AbsoluteUri, parameters.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.AreEqual(0, parameters.GetProperty("position").GetProperty("line").GetInt32());
		Assert.AreEqual(6, parameters.GetProperty("position").GetProperty("character").GetInt32());
		Assert.AreEqual(2, parameters.GetProperty("context").GetProperty("triggerKind").GetInt32());
		Assert.AreEqual(".", parameters.GetProperty("context").GetProperty("triggerCharacter").GetString());
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_WithoutTriggerCharacter_PassesInvokedContext()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			CompletionResponse = JsonSerializer.SerializeToElement(new { items = Array.Empty<object>() })
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		await provider.GetCompletionItemsAsync(new LanguageServerCompletionRequest(filePath, "loc", new TextPosition(0, 3)));
		JsonElement context = client.GetLastRequestParameters("textDocument/completion").GetProperty("context");

		// An invoked request carries no trigger character.
		Assert.AreEqual(1, context.GetProperty("triggerKind").GetInt32());
		Assert.IsFalse(context.TryGetProperty("triggerCharacter", out _));
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_WithPaddedTriggerCharacter_TrimsTheCharacter()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			CompletionResponse = JsonSerializer.SerializeToElement(new { items = Array.Empty<object>() })
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		await provider.GetCompletionItemsAsync(new LanguageServerCompletionRequest(filePath, "spawn.", new TextPosition(0, 6), "  .  "));
		JsonElement context = client.GetLastRequestParameters("textDocument/completion").GetProperty("context");

		// Surrounding whitespace is trimmed from a supplied trigger character.
		Assert.AreEqual(2, context.GetProperty("triggerKind").GetInt32());
		Assert.AreEqual(".", context.GetProperty("triggerCharacter").GetString());
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_WithBlankTriggerCharacter_PassesInvokedContext()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			CompletionResponse = JsonSerializer.SerializeToElement(new { items = Array.Empty<object>() })
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		await provider.GetCompletionItemsAsync(new LanguageServerCompletionRequest(filePath, "loc", new TextPosition(0, 3), "   "));
		JsonElement context = client.GetLastRequestParameters("textDocument/completion").GetProperty("context");

		// A blank trigger character is treated as absent, so the request is invoked by position.
		Assert.AreEqual(1, context.GetProperty("triggerKind").GetInt32());
		Assert.IsFalse(context.TryGetProperty("triggerCharacter", out _));
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_WhenRequestCrossesTransportBoundary_RetriesOnce()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			TransportChangedRequestFailuresRemaining = 1,
			CompletionResponse = JsonSerializer.SerializeToElement(new
			{
				items = new object[]
				{
					new
					{
						label = "spawn",
						kind = 3,
						insertText = "spawn"
					}
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		IReadOnlyList<TextCompletionItem> items = await provider.GetCompletionItemsAsync(new LanguageServerCompletionRequest(filePath, "spa", new TextPosition(0, 3))).ConfigureAwait(false);

		Assert.AreEqual(1, items.Count);
		Assert.AreEqual("spawn", items[0].Label);
		Assert.AreEqual(2, client.StartCallCount);
		Assert.AreEqual(2, CountSentMethods(client, "textDocument/completion"));
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_WhenTransportChangesOntoReadyTransport_RetriesWithoutRestart()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			TransportChangedRequestFailuresRemaining = 1,
			TransportChangedFailureDetachesTransport = false,
			CompletionResponse = JsonSerializer.SerializeToElement(new
			{
				items = new object[]
				{
					new
					{
						label = "spawn",
						kind = 3,
						insertText = "spawn"
					}
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		IReadOnlyList<TextCompletionItem> items = await provider.GetCompletionItemsAsync(new LanguageServerCompletionRequest(filePath, "spa", new TextPosition(0, 3))).ConfigureAwait(false);

		Assert.AreEqual(1, items.Count);
		Assert.AreEqual("spawn", items[0].Label);

		// The request crossed onto an already-ready newer transport, so the dispatcher retries once on the new
		// generation without starting another session.
		Assert.AreEqual(1, client.StartCallCount);
		Assert.AreEqual(2, CountSentMethods(client, "textDocument/completion"));
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_InvalidFilePath_ReturnsEmptyWithoutSending()
	{
		string workspaceRoot = TestPaths.Root;

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextCompletionItem> items = await provider.GetCompletionItemsAsync(
			new LanguageServerCompletionRequest("   ", "local value = 1", new TextPosition(0, 0)));

		Assert.AreEqual(0, items.Count);
		Assert.AreEqual(0, client.GetSentMethodNames().Length);
		Assert.AreEqual(0, client.StartCallCount);
	}

	[TestMethod]
	public async Task GetHoverAsync_WhenRequestTransportFails_RestartsAndRetriesOnce()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			ThrowIOExceptionOnNextRequestMethod = "textDocument/hover",
			HoverResponse = JsonSerializer.SerializeToElement(new
			{
				contents = new
				{
					kind = "markdown",
					value = "Hover docs."
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		TextHoverInfo? recoveredHover = await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0)).ConfigureAwait(false);

		Assert.IsNotNull(recoveredHover);
		Assert.AreEqual("Hover docs.", recoveredHover.Content);
		Assert.AreEqual(2, client.StartCallCount);
		Assert.AreEqual(2, CountSentMethods(client, "textDocument/hover"));
	}

	[TestMethod]
	public async Task RenameSymbolAsync_WhenRequestTransportFails_RestartsAndRetriesOnce()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("rename.lua");
		const string content = "local tracked_value = 1\r\nreturn tracked_value\r\n";

		using var client = new FakeLanguageServerClient
		{
			ThrowIOExceptionOnNextRequestMethod = "textDocument/rename",
			RenameResponse = JsonSerializer.SerializeToElement(new
			{
				changes = new Dictionary<string, object[]>
				{
					[new Uri(filePath).AbsoluteUri] =
					[
						new
						{
							newText = "renamed_value",
							range = new
							{
								start = new { line = 0, character = 6 },
								end = new { line = 0, character = 19 }
							}
						}
					]
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		TextWorkspaceEdit? recoveredRename = await provider
			.RenameSymbolAsync(new LanguageServerRenameRequest(filePath, content, new TextPosition(0, 8), "renamed_value"))
			.ConfigureAwait(false);

		Assert.IsNotNull(recoveredRename);
		Assert.IsTrue(recoveredRename.HasChanges);
		Assert.AreEqual(2, client.StartCallCount);
		Assert.AreEqual(2, CountSentMethods(client, "textDocument/rename"));
	}

	[TestMethod]
	public async Task GetHoverAsync_WhenRequestFailsWithoutTransportLoss_ThrowsWithoutRetrying()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			ThrowInvalidOperationOnNextRequestMethod = "textDocument/hover"
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0)).ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(1, client.StartCallCount);
		Assert.AreEqual(1, CountSentMethods(client, "textDocument/hover"));
	}

	[TestMethod]
	public async Task GetHoverAsync_IdleDocuments_DoNotAccumulateAcrossDistinctFiles()
	{
		string workspaceRoot = TestPaths.Root;
		using var client = new FakeLanguageServerClient
		{
			HoverResponse = JsonSerializer.SerializeToElement(new
			{
				contents = new
				{
					kind = "markdown",
					value = "Hover docs."
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		for (int i = 0; i < 20; i++)
		{
			string filePath = TestPaths.Script($"hover_{i}.lua");
			TextHoverInfo? hover = await provider.GetHoverAsync(filePath, "local value = 1", new TextPosition(0, 0));

			Assert.IsNotNull(hover);
		}

		// The idle-document tracking cap (LanguageServerProviderOptions.MaxTrackedIdleDocuments)
		// is deliberate provider policy; these assertions pin the
		// observable policy shape instead of the cap arithmetic: accumulation is bounded (fewer documents
		// stay tracked than were requested), the oldest idle documents are evicted, and the most recently
		// requested one is retained. Any cap that bounds accumulation below the request count
		// satisfies them.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TestPolling.DefaultTimeout));

		int trackedCount = CountSentMethods(client, "textDocument/didOpen") - CountSentMethods(client, "textDocument/didClose");

		Assert.IsTrue(trackedCount < 20, "Idle documents must not accumulate one per requested file.");

		int didOpenCountBeforeReopen = CountSentMethods(client, "textDocument/didOpen");
		int didCloseCountBeforeReopen = CountSentMethods(client, "textDocument/didClose");

		Assert.IsNotNull(await provider.GetHoverAsync(TestPaths.Script("hover_19.lua"), "local value = 1", new TextPosition(0, 0)));
		Assert.AreEqual(didOpenCountBeforeReopen, CountSentMethods(client, "textDocument/didOpen"), "The most recently used idle document must still be tracked.");
		Assert.AreEqual(didCloseCountBeforeReopen, CountSentMethods(client, "textDocument/didClose"));

		Assert.IsNotNull(await provider.GetHoverAsync(TestPaths.Script("hover_0.lua"), "local value = 1", new TextPosition(0, 0)));
		Assert.AreEqual(didOpenCountBeforeReopen + 1, CountSentMethods(client, "textDocument/didOpen"), "The oldest idle document must have been evicted.");
		Assert.IsTrue(CountSentMethods(client, "textDocument/didClose") > didCloseCountBeforeReopen, "Evicting the oldest idle document must close its server copy.");
	}

	[TestMethod]
	public async Task GetHoverAsync_IdleDocument_ReopensLazilyOnNextRequest()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("hover.lua");

		using var client = new FakeLanguageServerClient
		{
			HoverResponse = JsonSerializer.SerializeToElement(new
			{
				contents = new
				{
					kind = "markdown",
					value = "Hover docs."
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		Assert.IsNotNull(await provider.GetHoverAsync(filePath, "local value = 1", new TextPosition(0, 0)));

		for (int i = 0; i < 20; i++)
		{
			string otherFilePath = TestPaths.Script($"other_{i}.lua");
			Assert.IsNotNull(await provider.GetHoverAsync(otherFilePath, "local value = 1", new TextPosition(0, 0)));
		}

		int didOpenCountBeforeReopen = CountSentMethods(client, "textDocument/didOpen");
		int didCloseCountBeforeReopen = CountSentMethods(client, "textDocument/didClose");

		Assert.IsNotNull(await provider.GetHoverAsync(filePath, "local value = 1", new TextPosition(0, 0)));

		Assert.AreEqual(didOpenCountBeforeReopen + 1, CountSentMethods(client, "textDocument/didOpen"));
		Assert.AreEqual(didCloseCountBeforeReopen + 1, CountSentMethods(client, "textDocument/didClose"));
	}

	[TestMethod]
	public async Task FormatDocumentAsync_ReturnsFormattingEditsAndPassesEditorOptions()
	{
		string workspaceRoot = TestPaths.Root;
		string requestedFilePath = Path.Combine(TestPaths.Root, "Scripts", "..", "Scripts", "test.lua");
		const string content = "local value=1";

		string normalizedFilePath = LanguageServerPaths.NormalizeLocalPath(requestedFilePath);

		using var client = new FakeLanguageServerClient
		{
			SupportsFormatting = true,
			FormattingResponse = JsonSerializer.SerializeToElement(new object[]
			{
				new
				{
					range = new
					{
						start = new { line = 0, character = 0 },
						end = new { line = 0, character = 0 }
					},
					newText = "local value = 1\r\n"
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		TextWorkspaceEdit? workspaceEdit = await provider.FormatDocumentAsync(
			new LanguageServerFormattingRequest(requestedFilePath, content, new TextFormattingOptions(tabSize: 3, insertSpaces: false)));

		Assert.IsNotNull(workspaceEdit);
		Assert.AreEqual(1, workspaceEdit.DocumentEdits.Count);
		Assert.AreEqual(normalizedFilePath, workspaceEdit.DocumentEdits[0].FilePath);
		Assert.AreEqual(1, workspaceEdit.DocumentEdits[0].TextEdits.Count);
		Assert.AreEqual("local value = 1\r\n", workspaceEdit.DocumentEdits[0].TextEdits[0].NewText);

		JsonElement parameters = client.GetLastRequestParameters("textDocument/formatting");

		Assert.AreEqual(new Uri(normalizedFilePath).AbsoluteUri, parameters.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.AreEqual(3, parameters.GetProperty("options").GetProperty("tabSize").GetInt32());
		Assert.IsFalse(parameters.GetProperty("options").GetProperty("insertSpaces").GetBoolean());

		CollectionAssert.AreEqual(
			new[] { "textDocument/didOpen", "textDocument/formatting" },
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task FormatDocumentAsync_WithNonPositiveTabSize_SendsTheDefaultTabSize()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			SupportsFormatting = true,
			FormattingResponse = JsonSerializer.SerializeToElement(Array.Empty<object>())
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		await provider.FormatDocumentAsync(
			new LanguageServerFormattingRequest(filePath, "local value=1", new TextFormattingOptions(tabSize: 0, insertSpaces: true)));

		JsonElement parameters = client.GetLastRequestParameters("textDocument/formatting");

		// The options type clamps a non-positive tab size to its documented default before the request is built.
		Assert.AreEqual(TextFormattingOptions.DefaultTabSize, parameters.GetProperty("options").GetProperty("tabSize").GetInt32());
		Assert.IsTrue(parameters.GetProperty("options").GetProperty("insertSpaces").GetBoolean());
	}

	[TestMethod]
	public async Task FormatDocumentAsync_InvalidFilePath_ReturnsNullWithoutSending()
	{
		string workspaceRoot = TestPaths.Root;

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		TextWorkspaceEdit? workspaceEdit = await provider.FormatDocumentAsync(
			new LanguageServerFormattingRequest("   ", "local value = 1", new TextFormattingOptions(tabSize: 4, insertSpaces: true)));

		Assert.IsNull(workspaceEdit);
		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	[TestMethod]
	public async Task FormatDocumentAsync_EmptyEditList_ReturnsNullAfterTheRequest()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			SupportsFormatting = true,
			FormattingResponse = JsonSerializer.SerializeToElement(Array.Empty<object>())
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		TextWorkspaceEdit? workspaceEdit = await provider.FormatDocumentAsync(
			new LanguageServerFormattingRequest(filePath, content, new TextFormattingOptions(tabSize: 4, insertSpaces: true)));

		// An empty edit list means the document is already formatted; the provider reports null
		// instead of an empty workspace edit.
		Assert.IsNull(workspaceEdit);

		CollectionAssert.AreEqual(
			new[] { "textDocument/didOpen", "textDocument/formatting" },
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task RenameSymbolAsync_ReturnsWorkspaceEditFromTypedResponse()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = target\r\nprint(target)";
		string secondPath = TestPaths.Script("other.lua");

		using var client = new FakeLanguageServerClient
		{
			SupportsRename = true,
			RenameResponse = JsonSerializer.SerializeToElement(new
			{
				changes = new Dictionary<string, object[]>
				{
					[new Uri(filePath).AbsoluteUri] =
					[
						new
						{
							range = new
							{
								start = new { line = 0, character = 14 },
								end = new { line = 0, character = 20 }
							},
							newText = "renamed"
						}
					]
				},
				documentChanges = new object[]
				{
					new
					{
						textDocument = new { uri = new Uri(secondPath).AbsoluteUri },
						edits = new object[]
						{
							new
							{
								range = new
								{
									start = new { line = 1, character = 6 },
									end = new { line = 1, character = 12 }
								},
								newText = "renamed"
							}
						}
					}
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		TextWorkspaceEdit? workspaceEdit = await provider
			.RenameSymbolAsync(new LanguageServerRenameRequest(filePath, content, new TextPosition(0, 14), "renamed"));

		// The fake response populates both alternative edit representations; the structured
		// documentChanges list wins so a real server's response cannot be applied twice.
		Assert.IsNotNull(workspaceEdit);
		Assert.AreEqual(1, workspaceEdit.DocumentEdits.Count);
		Assert.AreEqual(secondPath, workspaceEdit.DocumentEdits[0].FilePath);
		Assert.AreEqual("renamed", workspaceEdit.DocumentEdits[0].TextEdits[0].NewText);

		JsonElement parameters = client.GetLastRequestParameters("textDocument/rename");

		Assert.AreEqual(new Uri(filePath).AbsoluteUri, parameters.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.AreEqual("renamed", parameters.GetProperty("newName").GetString());

		CollectionAssert.AreEqual(
			new[] { "textDocument/didOpen", "textDocument/rename" },
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task GetDiagnosticsAndSemanticTokens_AfterDisposeOrUnresolvablePath_ReturnEmpty()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var tokensPublished = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) => tokensPublished.TrySetResult(eventArgs.SemanticTokens);

		// Populate both caches before disposing so the post-disposal reads prove the guards, not an
		// empty store.
		provider.OpenDocument(filePath, "local value = 1");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 1, TestPolling.DefaultTimeout));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		Task tokensCompletedTask = await Task.WhenAny(tokensPublished.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(tokensPublished.Task, tokensCompletedTask);

		client.PublishDiagnostics(CreateDiagnostics(filePath, 1, 6, 11, "Current warning."));

		Assert.AreEqual(1, provider.GetDiagnostics(filePath).Count);
		Assert.AreEqual(1, provider.GetSemanticTokens(filePath).Count);

		// A path that cannot be normalized to a local file is rejected before any lookup.
		Assert.AreEqual(0, provider.GetDiagnostics("  ").Count);
		Assert.AreEqual(0, provider.GetSemanticTokens("  ").Count);

		provider.Dispose();

		Assert.AreEqual(0, provider.GetDiagnostics(filePath).Count);
		Assert.AreEqual(0, provider.GetSemanticTokens(filePath).Count);

		// A null path argument is an error on both reads.
		Assert.ThrowsExactly<ArgumentNullException>(() => provider.GetDiagnostics(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => provider.GetSemanticTokens(null!));
	}

	[TestMethod]
	public async Task FormatDocumentAsync_ReturnsNullWhenFormattingIsUnsupported()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			SupportsFormatting = false
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		TextWorkspaceEdit? workspaceEdit = await provider.FormatDocumentAsync(
			new LanguageServerFormattingRequest(filePath, "local value=1", new TextFormattingOptions(tabSize: 4, insertSpaces: true)));

		// The capability gate runs before document synchronization, so an unsupported request
		// produces no traffic at all.
		Assert.IsNull(workspaceEdit);
		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	[TestMethod]
	public void Constructor_WithNullWorkspaceRootList_ThrowsArgumentNullException()
	{
		using var client = new FakeLanguageServerClient();

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new LuaLanguageServerIntelliSenseProvider(null!, client));
	}

	[TestMethod]
	public void Constructor_WithEmptyWorkspaceRootList_ModelsAFolderlessProvider()
	{
		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([], client);

		// The provider layer no longer requires a workspace root; an empty list models a folderless provider
		// that matches the folderless client session. Ownership of the client transfers to the provider.
		Assert.IsFalse(provider.IsAvailable);
	}

	[TestMethod]
	public void Constructor_WithWhitespaceWorkspaceRootEntry_ThrowsArgumentException()
	{
		using var client = new FakeLanguageServerClient();

		Assert.ThrowsExactly<ArgumentException>(() =>
			new LuaLanguageServerIntelliSenseProvider(["   "], client));
	}

	[TestMethod]
	public void Constructor_WithDuplicateWorkspaceRoots_ThrowsArgumentException()
	{
		using var client = new FakeLanguageServerClient();

		Assert.ThrowsExactly<ArgumentException>(() =>
			new LuaLanguageServerIntelliSenseProvider([TestPaths.Root, TestPaths.Root], client));
	}
}
