using Nickelony.IDEKit.IntelliSense.Hover;

namespace Nickelony.LanguageServer.Lua.Tests;

public sealed partial class LuaLanguageServerIntelliSenseProviderTests
{
	[TestMethod]
	public async Task HealthyFastPath_DoesNotPublishAdditionalCapabilitiesTransition()
	{
		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([TestPaths.Root], client);
		int capabilitiesChangedCount = 0;

		provider.CapabilitiesChanged += (_, _) => capabilitiesChangedCount++;

		await provider.GetHoverAsync(TestPaths.Script("test.lua"), "return 1", new TextPosition(0, 0));

		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
		Assert.AreEqual(1, capabilitiesChangedCount);

		await provider.GetHoverAsync(TestPaths.Script("test.lua"), "return 2", new TextPosition(0, 0));

		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
		Assert.IsTrue(provider.IsAvailable);
		Assert.AreEqual(1, capabilitiesChangedCount);
		Assert.AreEqual(1, client.StartCallCount);
	}

	[TestMethod]
	public async Task DisposeDuringStartupLeavesDisposedAsTerminalState()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		client.BlockNextStartAsync();

		using var provider = new LuaLanguageServerIntelliSenseProvider([TestPaths.Root], client);
		Task<TextHoverInfo?> request = provider.GetHoverAsync(TestPaths.Script("test.lua"), "return 1", new TextPosition(0, 0));

		await TestPolling.UntilAsync(
			() => client.StartCallCount > 0,
			TestPolling.DefaultTimeout,
			"The request must reach the blocked startup before disposal.").ConfigureAwait(false);

		Assert.AreEqual(1, client.StartCallCount);

		provider.Dispose();
		client.ReleaseStartAsync();

		Assert.IsNull(await request.ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Disposed, provider.State);
	}

	[TestMethod]
	public async Task ActiveTransportLossPublishesUnavailableCapabilitiesOnceAndRestartRecovers()
	{
		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([TestPaths.Root], client);
		int capabilitiesChangedCount = 0;

		provider.CapabilitiesChanged += (_, _) => capabilitiesChangedCount++;

		await provider.GetHoverAsync(TestPaths.Script("test.lua"), "return 1", new TextPosition(0, 0));

		long activeGeneration = client.TransportGeneration;
		client.PublishTransportUnavailable(activeGeneration);

		// The unhealthy reset keeps the generation number, mirroring the real client contract.
		Assert.AreEqual(activeGeneration, client.TransportGeneration);
		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);
		Assert.IsFalse(provider.IsAvailable);
		Assert.IsFalse(provider.SupportsReferences);
		Assert.AreEqual(2, capabilitiesChangedCount);

		await provider.GetHoverAsync(TestPaths.Script("test.lua"), "return 2", new TextPosition(0, 0));

		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
		Assert.IsTrue(client.TransportGeneration > activeGeneration);
		Assert.IsTrue(provider.SupportsReferences);
		Assert.AreEqual(3, capabilitiesChangedCount);
	}

	[TestMethod]
	public async Task CapabilitySurface_MirrorsTheNegotiatedSnapshotAcrossTransportLossAndRestart()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "return 1";

		using var client = new FakeLanguageServerClient
		{
			SupportsReferences = true,
			SupportsRename = false,
			SupportsFormatting = false,
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		// Like the real client, the fake reports no optional capabilities before a successful startup.
		Assert.IsFalse(client.SupportsReferences);
		Assert.IsFalse(client.SupportsRename);
		Assert.IsFalse(client.SupportsFormatting);
		Assert.IsFalse(client.SupportsSemanticTokensFull);
		Assert.IsFalse(client.SupportsCompletionResolve);
		Assert.AreEqual(TextDocumentSyncKind.None, client.TextDocumentSyncKind);

		await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

		// The post-negotiation surface mirrors the snapshot member by member.
		Assert.IsTrue(provider.SupportsReferences);
		Assert.IsFalse(provider.SupportsRename);
		Assert.IsFalse(provider.SupportsFormatting);
		Assert.IsTrue(client.SupportsReferences);
		Assert.IsFalse(client.SupportsRename);
		Assert.IsTrue(client.SupportsSemanticTokensFull);
		Assert.AreEqual(TextDocumentSyncKind.Incremental, client.TextDocumentSyncKind);

		// A capability the snapshot withholds is never exercised.
		TextWorkspaceEdit? rename = await provider.RenameSymbolAsync(new LanguageServerRenameRequest(filePath, content, new TextPosition(0, 7), "renamed"));

		Assert.IsNull(rename);
		CollectionAssert.DoesNotContain(client.GetSentMethodNames(), "textDocument/rename");

		// Transport loss clears the snapshot to the not-initialized values and the restart re-negotiates it.
		client.PublishTransportUnavailable(client.TransportGeneration);

		Assert.IsFalse(client.SupportsReferences);
		Assert.IsFalse(client.SupportsSemanticTokensFull);
		Assert.AreEqual(TextDocumentSyncKind.None, client.TextDocumentSyncKind);
		Assert.IsFalse(provider.SupportsReferences);

		await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

		Assert.IsTrue(provider.SupportsReferences);
		Assert.IsTrue(client.SupportsSemanticTokensFull);
		Assert.AreEqual(TextDocumentSyncKind.Incremental, client.TextDocumentSyncKind);
	}

	[TestMethod]
	public async Task TransportLossBeforeReadyPublicationCannotBeOverwrittenByStartupCompletion()
	{
		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([TestPaths.Root], client);
		int capabilitiesChangedCount = 0;
		long startedGeneration = 0;

		provider.CapabilitiesChanged += (_, _) => capabilitiesChangedCount++;
		client.BeforeReturningStartResult = () =>
		{
			startedGeneration = client.TransportGeneration;
			client.PublishTransportUnavailable();
		};

		TextHoverInfo? hover = await provider.GetHoverAsync(
			TestPaths.Script("test.lua"), "return 1", new TextPosition(0, 0));

		Assert.IsNull(hover);

		// The unhealthy reset keeps the issued generation, mirroring the real client contract.
		Assert.AreEqual(startedGeneration, client.TransportGeneration);
		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);
		Assert.IsFalse(provider.IsAvailable);
		Assert.AreEqual(1, capabilitiesChangedCount);
	}

	[TestMethod]
	public async Task StaleTransportLossCannotChangeCurrentReadySession()
	{
		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([TestPaths.Root], client);

		await provider.GetHoverAsync(TestPaths.Script("test.lua"), "return 1", new TextPosition(0, 0));

		long staleGeneration = client.TransportGeneration;
		client.IsReady = false;

		await provider.GetHoverAsync(TestPaths.Script("test.lua"), "return 2", new TextPosition(0, 0));

		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);

		client.PublishTransportUnavailable(staleGeneration);

		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
		Assert.IsTrue(provider.IsAvailable);
	}

	[TestMethod]
	public async Task TransportLossRacingDispose_DoesNotPublishAfterDisposeOrOverwriteDisposedState()
	{
		using var client = new FakeLanguageServerClient();
		var provider = new LuaLanguageServerIntelliSenseProvider([TestPaths.Root], client);
		using var transportCallbackCaptured = new ManualResetEventSlim();
		using var releaseTransportCallback = new ManualResetEventSlim();
		int capabilitiesChangedCount = 0;

		provider.CapabilitiesChanged += (_, _) => capabilitiesChangedCount++;

		await provider.GetHoverAsync(TestPaths.Script("test.lua"), "return 1", new TextPosition(0, 0));

		long activeGeneration = client.TransportGeneration;

		Assert.AreEqual(1, capabilitiesChangedCount);

		client.BeforePublishingTransportUnavailable = () =>
		{
			transportCallbackCaptured.Set();
			releaseTransportCallback.Wait();
		};

		Task transportLoss = Task.Run(() => client.PublishTransportUnavailable(activeGeneration));

		try
		{
			Assert.IsTrue(transportCallbackCaptured.Wait(TestPolling.DefaultTimeout));
		}
		finally
		{
			provider.Dispose();
			releaseTransportCallback.Set();
		}

		await transportLoss;

		Assert.AreEqual(LanguageServerProviderState.Disposed, provider.State);
		Assert.IsFalse(provider.IsAvailable);
		Assert.AreEqual(1, capabilitiesChangedCount);
	}
}
