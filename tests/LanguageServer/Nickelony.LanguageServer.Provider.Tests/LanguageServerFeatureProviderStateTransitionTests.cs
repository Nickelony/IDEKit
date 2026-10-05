using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Rename;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the provider state and the published capability snapshot across startup, connection loss, restart and disposal.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderStateTransitionTests
{
	[TestMethod]
	public async Task HealthyFastPath_DoesNotPublishAdditionalCapabilitiesTransition()
	{
		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client);

		int capabilitiesChangedCount = 0;

		provider.CapabilitiesChanged += (_, _) => capabilitiesChangedCount++;

		await provider.GetHoverAsync(
			filePath: TestPaths.Script("test.test"),
			documentText: "return 1",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);
		Assert.AreEqual(1, capabilitiesChangedCount);

		await provider.GetHoverAsync(
			filePath: TestPaths.Script("test.test"),
			documentText: "return 2",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);
		Assert.IsTrue(provider.IsAvailable);
		Assert.AreEqual(1, capabilitiesChangedCount);
		Assert.AreEqual(1, client.StartCallCount);
	}

	[TestMethod]
	public async Task DisposeDuringStartupLeavesDisposedAsTerminalState()
	{
		using var client = new FakeStatefulLanguageServerClient { IsReady = false };
		client.BlockNextEnsureStartedAsync();

		using LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client);

		Task<HoverInformation?> request = provider.GetHoverAsync(
			filePath: TestPaths.Script("test.test"),
			documentText: "return 1",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		await TestPolling
			.UntilAsync(() => client.StartCallCount > 0, TestPolling.DefaultTimeout, "The request must reach the blocked startup before disposal.")
			.ConfigureAwait(false);

		Assert.AreEqual(1, client.StartCallCount);

		provider.Dispose();
		client.ReleaseEnsureStartedAsync();

		Assert.IsNull(await request.ConfigureAwait(false));
		Assert.AreEqual(LanguageServerFeatureProviderState.Disposed, provider.State);
	}

	[TestMethod]
	public async Task ActiveConnectionLossPublishesUnavailableCapabilitiesOnceAndRestartRecovers()
	{
		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client);

		int capabilitiesChangedCount = 0;

		provider.CapabilitiesChanged += (_, _) => capabilitiesChangedCount++;

		await provider.GetHoverAsync(
			filePath: TestPaths.Script("test.test"),
			documentText: "return 1",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		long activeGeneration = client.ConnectionGeneration;
		client.RaiseConnectionUnavailable(activeGeneration);

		// The unhealthy reset keeps the generation number, mirroring the real client contract.
		Assert.AreEqual(activeGeneration, client.ConnectionGeneration);
		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, provider.State);
		Assert.IsFalse(provider.IsAvailable);
		Assert.IsFalse(provider.Capabilities.SupportsReferences);
		Assert.AreEqual(2, capabilitiesChangedCount);

		await provider.GetHoverAsync(
			filePath: TestPaths.Script("test.test"),
			documentText: "return 2",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);
		Assert.IsGreaterThan(activeGeneration, client.ConnectionGeneration);
		Assert.IsTrue(provider.Capabilities.SupportsReferences);
		Assert.AreEqual(3, capabilitiesChangedCount);
	}

	[TestMethod]
	public async Task CapabilitySurface_MirrorsTheNegotiatedSnapshotAcrossConnectionLossAndRestart()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "return 1";

		using var client = new FakeStatefulLanguageServerClient
		{
			SupportsReferences = true,
			SupportsRename = false,
			SupportsFormatting = false,
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"],
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		// Like the real client, the fake reports no optional capabilities before a successful startup.
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsReferences);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsRename);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsFormatting);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsSemanticTokensFull);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsCompletionResolve);
		Assert.AreEqual(TextDocumentSyncKind.None, client.NegotiatedCapabilities.TextDocumentSyncKind);

		await provider.GetHoverAsync(
			filePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		// The post-negotiation surface mirrors the snapshot member by member.
		Assert.IsTrue(provider.Capabilities.SupportsReferences);
		Assert.IsFalse(provider.Capabilities.SupportsRename);
		Assert.IsTrue(client.NegotiatedCapabilities.SupportsReferences);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsRename);
		Assert.IsTrue(client.NegotiatedCapabilities.SupportsSemanticTokensFull);
		Assert.AreEqual(TextDocumentSyncKind.Incremental, client.NegotiatedCapabilities.TextDocumentSyncKind);

		// A capability the snapshot withholds is never exercised.
		WorkspaceEdit? rename = await provider.GetRenameEditsAsync(
			new RenameRequest(
				documentId: filePath,
				documentText: content,
				symbolOffset: content.OffsetAt(new TextPosition(0, 7)),
				newName: "renamed"
			),

			TestContext.CancellationToken
		);

		Assert.IsNull(rename);
		Assert.DoesNotContain("textDocument/rename", client.GetSentMethodNames());

		// Connection loss clears the snapshot to the not-initialized values and the restart re-negotiates it.
		client.RaiseConnectionUnavailable(client.ConnectionGeneration);

		Assert.IsFalse(client.NegotiatedCapabilities.SupportsReferences);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsSemanticTokensFull);
		Assert.AreEqual(TextDocumentSyncKind.None, client.NegotiatedCapabilities.TextDocumentSyncKind);
		Assert.IsFalse(provider.Capabilities.SupportsReferences);

		await provider.GetHoverAsync(
			filePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.IsTrue(provider.Capabilities.SupportsReferences);
		Assert.IsTrue(client.NegotiatedCapabilities.SupportsSemanticTokensFull);
		Assert.AreEqual(TextDocumentSyncKind.Incremental, client.NegotiatedCapabilities.TextDocumentSyncKind);
	}

	[TestMethod]
	public async Task ConnectionLossBeforeReadyPublicationCannotBeOverwrittenByStartupCompletion()
	{
		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client);

		int capabilitiesChangedCount = 0;
		long startedGeneration = 0;

		provider.CapabilitiesChanged += (_, _) => capabilitiesChangedCount++;

		client.BeforeReturningStartResult = () =>
		{
			startedGeneration = client.ConnectionGeneration;
			client.RaiseConnectionUnavailable();
		};

		HoverInformation? hover = await provider.GetHoverAsync(
			filePath: TestPaths.Script("test.test"),
			documentText: "return 1",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.IsNull(hover);

		// The unhealthy reset keeps the issued generation, mirroring the real client contract.
		Assert.AreEqual(startedGeneration, client.ConnectionGeneration);
		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, provider.State);
		Assert.IsFalse(provider.IsAvailable);
		Assert.AreEqual(1, capabilitiesChangedCount);
	}

	[TestMethod]
	public async Task StaleConnectionLossCannotChangeCurrentReadySession()
	{
		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client);

		await provider.GetHoverAsync(
			filePath: TestPaths.Script("test.test"),
			documentText: "return 1",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		long staleGeneration = client.ConnectionGeneration;
		client.IsReady = false;

		await provider.GetHoverAsync(
			filePath: TestPaths.Script("test.test"),
			documentText: "return 2",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);

		client.RaiseConnectionUnavailable(staleGeneration);

		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);
		Assert.IsTrue(provider.IsAvailable);
	}

	[TestMethod]
	public async Task ConnectionLossRacingDispose_DoesNotPublishAfterDisposeOrOverwriteDisposedState()
	{
		using var client = new FakeStatefulLanguageServerClient();

		LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client);

		using var connectionCallbackCaptured = new ManualResetEventSlim();
		using var releaseConnectionCallback = new ManualResetEventSlim();

		int capabilitiesChangedCount = 0;

		provider.CapabilitiesChanged += (_, _) => capabilitiesChangedCount++;

		await provider.GetHoverAsync(
			filePath: TestPaths.Script("test.test"),
			documentText: "return 1",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		long activeGeneration = client.ConnectionGeneration;

		Assert.AreEqual(1, capabilitiesChangedCount);

		client.BeforePublishingConnectionUnavailable = () =>
		{
			connectionCallbackCaptured.Set();
			releaseConnectionCallback.Wait(TestContext.CancellationToken);
		};

		var connectionLoss = Task.Run(() => client.RaiseConnectionUnavailable(activeGeneration), TestContext.CancellationToken);

		try
		{
			Assert.IsTrue(connectionCallbackCaptured.Wait(TestPolling.DefaultTimeout, TestContext.CancellationToken));
		}
		finally
		{
			provider.Dispose();
			releaseConnectionCallback.Set();
		}

		await connectionLoss;

		Assert.AreEqual(LanguageServerFeatureProviderState.Disposed, provider.State);
		Assert.IsFalse(provider.IsAvailable);
		Assert.AreEqual(1, capabilitiesChangedCount);
	}

	public TestContext? TestContext { get; set; }
}
