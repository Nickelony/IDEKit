using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Rename;
using Nickelony.LanguageServer.Client.Protocol;
using static Nickelony.LanguageServer.Provider.Tests.DiagnosticsTestHelpers;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers how disposal ends the requests that are in flight or waiting on a startup, the disposal token, and the results after disposal.
/// </summary>
[TestClass]
public sealed class LanguageServerFeatureProviderDisposalTests
{
	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task GetHoverAsync_DisposeWhileRequestsShareBlockedStartup_BothReturnNullWithoutDisposedExceptions()
	{
		string workspaceRoot = TestPaths.Root;
		string firstFilePath = TestPaths.Script("test.test");
		string secondFilePath = TestPaths.Script("other.test");

		using var client = new FakeStatefulLanguageServerClient();
		client.BlockNextEnsureStartedAsync();

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		int startupLockWaitCount = 0;
		var secondStartupLockWaitObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.TestHooks.StartupLockWaitObserver = () =>
		{
			if (Interlocked.Increment(ref startupLockWaitCount) >= 2)
				secondStartupLockWaitObserved.TrySetResult(true);
		};

		// The first request drives the startup and parks inside the blocked start call while it holds
		// the provider start lock; the second request then waits for that lock. Both must surface the
		// documented disposal fallback when the provider is disposed while the startup is pending.
		Task<HoverInformation?> firstHoverTask = provider.GetHoverAsync(
			filePath: firstFilePath,
			documentText: "local value = 1",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		await TestPolling
			.UntilAsync(() => client.StartCallCount > 0, TestPolling.DefaultTimeout, "The first request must reach the blocked startup.")
			.ConfigureAwait(false);

		Assert.AreEqual(1, client.StartCallCount);

		Task<HoverInformation?> secondHoverTask = provider.GetHoverAsync(
			filePath: secondFilePath,
			documentText: "local value = 2",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		// The observer fires once per caller reaching the start lock, so the second request is provably parked
		// on the shared startup - whose holder is still inside the blocked start call - before the disposal.
		await secondStartupLockWaitObserved.Task.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);

		provider.Dispose();
		client.ReleaseEnsureStartedAsync();

		Assert.IsNull(await firstHoverTask.ConfigureAwait(false));
		Assert.IsNull(await secondHoverTask.ConfigureAwait(false));
		Assert.AreEqual(1, client.DisposeCallCount);

		// No startup may complete once the provider is disposed. The second request can surface one
		// extra canceled start attempt when its start-lock acquisition races the disposal token
		// cancellation, so only the completed-start invariant is asserted here.
		Assert.AreEqual(0L, client.ConnectionGeneration);
	}

	[TestMethod]
	public async Task GetHoverAsync_DisposeDuringInFlightRequest_ReturnsNullWithoutLateResult()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
		};

		client.BlockNextHoverRequest();

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		Task<HoverInformation?> hoverTask = provider.GetHoverAsync(
			filePath,
			documentText: "local value = 1",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/hover", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));

		provider.Dispose();
		client.ReleaseHoverRequest();

		Assert.IsNull(await hoverTask.ConfigureAwait(false));
	}

	[TestMethod]
	public async Task GetHoverAsync_DisposeDuringBlockedStart_ReturnsNullWithoutLeakingStartTask()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			IsReady = false,
			HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
		};

		client.BlockNextEnsureStartedAsync();

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		Task<HoverInformation?> hoverTask = provider.GetHoverAsync(
			filePath,
			documentText: "local value = 1",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		// The fake records the start call before parking on the gate, so the call count is the
		// observable signal that the hover task reached the blocked startup before disposal.
		await TestPolling
			.UntilAsync(() => client.StartCallCount > 0, TestPolling.DefaultTimeout, "The hover task must reach the blocked startup before disposal.")
			.ConfigureAwait(false);

		Assert.AreEqual(1, client.StartCallCount);

		provider.Dispose();

		Assert.IsNull(await hoverTask.ConfigureAwait(false));
		Assert.AreEqual(1, client.StartCallCount);
		Assert.HasCount(1, client.StartCancellationTokenCanBeCanceled);
	}

	[TestMethod]
	public void Dispose_CancelsTheDisposalTokenAndLeavesItLinkable()
	{
		string workspaceRoot = TestPaths.Root;

		var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		CancellationToken disposeToken = provider.Composition.Disposal.Token;

		provider.Dispose();
		provider.Dispose();

		Assert.AreEqual(1, client.DisposeCallCount);
		Assert.IsTrue(disposeToken.IsCancellationRequested);

		// The token must stay linkable after Dispose so an in-flight link returns the documented fallback
		// instead of throwing ObjectDisposedException.
		using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(disposeToken);
		Assert.IsTrue(linkedSource.IsCancellationRequested);
	}

	[TestMethod]
	public async Task GetDiagnosticsAndSemanticTokens_AfterDisposeOrUnresolvablePath_ReturnEmpty()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = true, SemanticTokenTypes = ["variable"] };

		LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);
		var tokensPublished = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) => tokensPublished.TrySetResult(eventArgs.SemanticTokens);

		// Populate both caches before disposing so the post-disposal reads prove the guards, not an
		// empty tracker.
		provider.OpenDocument(filePath, "local value = 1");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 1, TestPolling.DefaultTimeout));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		Task tokensCompletedTask = await Task.WhenAny(tokensPublished.Task, Task.Delay(TestPolling.DefaultTimeout, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreSame(tokensPublished.Task, tokensCompletedTask);

		client.PublishDiagnostics(CreateDiagnostics(filePath, version: 1, startCharacter: 6, endCharacter: 11, message: "Current warning."));

		Assert.HasCount(1, provider.GetDiagnostics(filePath));
		Assert.HasCount(1, provider.GetSemanticTokens(filePath));

		// A path that cannot be normalized to a local file is rejected before any lookup.
		Assert.IsEmpty(provider.GetDiagnostics("  "));
		Assert.IsEmpty(provider.GetSemanticTokens("  "));

		provider.Dispose();

		Assert.IsEmpty(provider.GetDiagnostics(filePath));
		Assert.IsEmpty(provider.GetSemanticTokens(filePath));

		// A null path argument is an error on both reads.
		Assert.ThrowsExactly<ArgumentNullException>(() => provider.GetDiagnostics(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => provider.GetSemanticTokens(null!));
	}

	public TestContext? TestContext { get; set; }
}
