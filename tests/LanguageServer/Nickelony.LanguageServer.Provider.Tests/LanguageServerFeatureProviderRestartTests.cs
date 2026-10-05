using System.Text.Json;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers how tracked documents and request timeouts behave across a connection restart.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderRestartTests
{
	[TestMethod]
	public async Task GetHoverAsync_RestartsAfterConsecutiveTimeoutsOnSameConnectionGeneration()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient
		{
			TimedOutHoverRequestsRemaining = 2,
			HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create(
			[workspaceRoot],
			client,
			options: new LanguageServerFeatureProviderOptions { RequestTimeout = TimeSpan.FromMilliseconds(250), RequestTimeoutRestartThreshold = 2 }
		);

		HoverInformation? firstHover = await provider.GetHoverAsync(
			filePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		HoverInformation? secondHover = await provider.GetHoverAsync(
			filePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		HoverInformation? thirdHover = await provider.GetHoverAsync(
			filePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.IsNull(firstHover);
		Assert.IsNull(secondHover);
		Assert.IsNotNull(thirdHover);
		Assert.AreEqual("Hover docs.", thirdHover.Content.Value);
		Assert.AreEqual(1, client.MarkConnectionUnhealthyCallCount);
		Assert.AreEqual(2, client.StartCallCount);

		client.TimedOutHoverRequestsRemaining = 1;

		HoverInformation? fourthHover = await provider.GetHoverAsync(
			filePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.IsNull(fourthHover);
		Assert.AreEqual(1, client.MarkConnectionUnhealthyCallCount);

		Assert.AreSequenceEqual(
			["textDocument/didOpen", "textDocument/hover", "textDocument/hover", "textDocument/didOpen", "textDocument/hover", "textDocument/hover"],
			client.GetSentMethodNames()
		);
	}

	[TestMethod]
	public async Task GetHoverAsync_TimeoutFromSupersededGeneration_DoesNotInvalidateReplacementConnection()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient
		{
			TimedOutHoverRequestsRemaining = 1,
			HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create(
			[workspaceRoot],
			client,
			options: new LanguageServerFeatureProviderOptions { RequestTimeout = TimeSpan.FromMilliseconds(200), RequestTimeoutRestartThreshold = 1 }
		);

		Task<HoverInformation?> timedOutHoverTask = provider.GetHoverAsync(
			filePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/hover", 1, TestPolling.DefaultTimeout));

		client.TryMarkConnectionUnhealthy(client.ConnectionGeneration);

		HoverInformation? restartedHover = await provider.GetHoverAsync(
			filePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		HoverInformation? timedOutHover = await timedOutHoverTask;

		HoverInformation? thirdHover = await provider.GetHoverAsync(
			filePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.IsNotNull(restartedHover);
		Assert.AreEqual("Hover docs.", restartedHover.Content.Value);
		Assert.IsNull(timedOutHover);
		Assert.IsNotNull(thirdHover);
		Assert.AreEqual("Hover docs.", thirdHover.Content.Value);
		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(2, client.StartCallCount);

		Assert.AreSequenceEqual(
			["textDocument/didOpen", "textDocument/hover", "textDocument/didOpen", "textDocument/hover", "textDocument/hover"],
			client.GetSentMethodNames()
		);
	}

	[TestMethod]
	public async Task GetHoverAsync_RequestCancellationAfterConnectionDrop_DoesNotForceRestartAttempt()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient
		{
			FailStartWhenCancellationRequested = true,
			HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		await provider.GetHoverAsync(
			filePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		client.IsReady = false;

		using var cancellationTokenSource = new CancellationTokenSource();
		cancellationTokenSource.Cancel();

		Task<HoverInformation?> hoverTask = provider.GetHoverAsync(
			filePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: cancellationTokenSource.Token
		);

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => hoverTask);

		Assert.AreEqual(1, client.StartCallCount);
		Assert.HasCount(1, client.StartCancellationTokenCanBeCanceled);
	}

	[TestMethod]
	public async Task GetHoverAsync_InternalRequestCancellation_DoesNotCountAsTimeoutOrRestart()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient
		{
			CancelNextHoverRequestWithoutTimeout = true,
			HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create(
			[workspaceRoot],
			client,
			options: new LanguageServerFeatureProviderOptions { RequestTimeout = TimeSpan.FromMilliseconds(250), RequestTimeoutRestartThreshold = 1 }
		);

		HoverInformation? canceledHover = await provider.GetHoverAsync(
			filePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		HoverInformation? recoveredHover = await provider.GetHoverAsync(
			filePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.IsNull(canceledHover);
		Assert.IsNotNull(recoveredHover);
		Assert.AreEqual("Hover docs.", recoveredHover.Content.Value);
		Assert.AreEqual(0, client.MarkConnectionUnhealthyCallCount);
		Assert.AreEqual(1, client.StartCallCount);

		Assert.AreSequenceEqual(["textDocument/didOpen", "textDocument/hover", "textDocument/hover"], client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task UpdateDocument_WithUnchangedContentAfterConnectionFailure_ReopensWithFullSemanticTokensRefresh()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = true, SemanticTokenTypes = ["variable"] };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(filePath, "local value = 1");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 1, TestPolling.DefaultTimeout));

		client.ThrowIOExceptionOnNextDidChange = true;

		provider.UpdateDocument(filePath, "local value = 2");
		Assert.IsTrue(await client.WaitForNotificationAsync("textDocument/didChange", TestPolling.DefaultTimeout));

		// Wait for the failed update's cleanup to mark the connection unavailable before re-issuing; an earlier
		// retrigger would be superseded and canceled.
		await TestPolling
			.WaitForAsync(
				action: () => Task.FromResult(provider.State),
				isSatisfied: static state => state == LanguageServerFeatureProviderState.Unavailable,
				timeout: TestPolling.DefaultTimeout,
				failureMessage: "Expected the failed update to mark the connection unavailable.",
				describeLastResult: static state => $"state={state}"
			)
			.ConfigureAwait(false);

		provider.UpdateDocument(filePath, "local value = 2");

		await TestPolling
			.WaitForAsync(
				action: () => Task.FromResult(client.GetSentMethodCount("textDocument/didOpen")),
				isSatisfied: static didOpenCount => didOpenCount >= 2,
				timeout: TestPolling.DefaultTimeout,
				failureMessage: "Expected the provider to re-establish the document after the connection failure.",
				describeLastResult: static didOpenCount => $"didOpenCount={didOpenCount}"
			)
			.ConfigureAwait(false);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 2, TestPolling.DefaultTimeout));

		Assert.AreSequenceEqual(
			[
				"textDocument/didOpen",
				"textDocument/semanticTokens/full",
				"textDocument/didChange",
				"textDocument/didOpen",
				"textDocument/semanticTokens/full",
			],

			client.GetSentMethodNames()
		);
	}

	[TestMethod]
	public async Task GetHoverAsync_ReplaysTrackedDocumentsAfterLanguageServerRestart()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		await provider.GetHoverAsync(
			filePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreSequenceEqual(["textDocument/didOpen", "textDocument/hover"], client.GetSentMethodNames());

		Assert.AreEqual(1, client.StartCallCount);

		client.IsReady = false;

		await provider.GetHoverAsync(
			filePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreSequenceEqual(
			["textDocument/didOpen", "textDocument/hover", "textDocument/didOpen", "textDocument/hover"],
			client.GetSentMethodNames()
		);

		Assert.AreEqual(2, client.StartCallCount);
	}

	[TestMethod]
	public async Task OpenDocument_DuringStartupFailure_ReplaysTrackedDocumentAfterRecovery()
	{
		string workspaceRoot = TestPaths.Root;
		string openedFilePath = TestPaths.Script("opened.test");
		string requestFilePath = TestPaths.Script("request.test");

		const string openedContent = "local opened = 1";

		using var client = new FakeStatefulLanguageServerClient
		{
			StartResult = false,
			HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		// StartupFailed is the completion signal so the assertions do not depend on a fixed wait; only the first
		// start fails here.
		var startupFailed = new TaskCompletionSource<StartupFailure>(TaskCreationOptions.RunContinuationsAsynchronously);
		provider.StartupFailed += (_, eventArgs) => startupFailed.TrySetResult(eventArgs.Failure);

		provider.OpenDocument(openedFilePath, openedContent);

		Task failedCompletedTask = await Task.WhenAny(startupFailed.Task, Task.Delay(TestPolling.DefaultTimeout, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreSame(startupFailed.Task, failedCompletedTask);

		Assert.AreEqual(1, client.StartCallCount);
		Assert.AreEqual(0, client.GetSentMethodCount("textDocument/didOpen"));

		// StartupFailed fires before the open's slot records the document locally, so the recovery can begin before the
		// record exists. Either the recovery replays the recorded document, or the open applies itself once the recovery settles.
		client.StartResult = true;

		HoverInformation? hover = await provider.GetHoverAsync(
			filePath: requestFilePath,
			documentText: "local request = 1",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.IsNotNull(hover);
		Assert.AreEqual(2, client.StartCallCount);

		// The deferred open is applied once the recovery start settles; the test pins only that the request
		// document is opened before its hover is dispatched.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, TestPolling.DefaultTimeout));

		string[] sentMethodNames = client.GetSentMethodNames();
		var openedUris = new List<string>();

		foreach (JsonElement didOpenParameters in client.GetNotificationParameters("textDocument/didOpen"))
			openedUris.Add(didOpenParameters.GetProperty("textDocument").GetProperty("uri").GetString()!);

		Assert.AreSequenceEqual(
			[new Uri(openedFilePath).AbsoluteUri, new Uri(requestFilePath).AbsoluteUri],
			openedUris,
			Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder
		);

		int requestOpenIndex = openedUris.IndexOf(new Uri(requestFilePath).AbsoluteUri);
		int hoverIndex = Array.IndexOf(sentMethodNames, "textDocument/hover");

		Assert.IsGreaterThan(requestOpenIndex, hoverIndex, "The request document must be opened before its hover request is dispatched.");
	}

	[TestMethod]
	public async Task OpenDocument_WhileRecoveryStartOwnsTrackedDocuments_IsAppliedAfterTheStartSettles()
	{
		string workspaceRoot = TestPaths.Root;
		string trackedFilePath = TestPaths.Script("tracked.test");
		string requestFilePath = TestPaths.Script("request.test");

		const string firstContent = "local tracked = 1";
		const string secondContent = "local tracked = 2";
		const string thirdContent = "local tracked = 3";

		using var client = new FakeStatefulLanguageServerClient
		{
			HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		// Tracks the document without an open lease and then parks its lane inside a blocked change
		// notification, so the open below queues behind that slot and cannot run before the change is released.
		provider.UpdateDocument(trackedFilePath, firstContent);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));
		Assert.AreEqual(1, client.StartCallCount);

		client.BlockNextChangeNotification();
		provider.UpdateDocument(trackedFilePath, secondContent);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, TestPolling.DefaultTimeout));

		// The open takes a failed startup attempt and then waits behind the parked change, while the test
		// synchronizes on the reported failure so the recovery start always observes the succeeded result.
		var startupFailed = new TaskCompletionSource<StartupFailure>(TaskCreationOptions.RunContinuationsAsynchronously);
		provider.StartupFailed += (_, eventArgs) => startupFailed.TrySetResult(eventArgs.Failure);

		client.IsReady = false;
		client.StartResult = false;

		provider.OpenDocument(trackedFilePath, thirdContent);

		Task failedCompletedTask = await Task.WhenAny(startupFailed.Task, Task.Delay(TestPolling.DefaultTimeout, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreSame(startupFailed.Task, failedCompletedTask);
		Assert.AreEqual(2, client.StartCallCount);

		// The recovery start is held at its gate: when the parked change releases the lane, the open's slot is
		// guaranteed to observe the startup window instead of racing it.
		client.StartResult = true;
		client.BlockNextEnsureStartedAsync();

		Task<HoverInformation?> hoverTask = provider.GetHoverAsync(
			filePath: requestFilePath,
			documentText: "local request = 1",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		await TestPolling.WaitForAsync(
			action: () => Task.FromResult(client.StartCallCount),
			isSatisfied: startCallCount => startCallCount >= 3,
			timeout: TestPolling.DefaultTimeout,
			failureMessage: "The recovery startup was not started.",
			describeLastResult: startCallCount => $"StartCallCount={startCallCount}"
		);

		var deferralStartupLockWaitObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.TestHooks.StartupLockWaitObserver = () => deferralStartupLockWaitObserved.TrySetResult(true);

		client.ReleaseChangeNotification();

		// The open's slot now runs while the recovery startup is still held: the connection is not ready and the
		// startup owns the tracked records, so the operation is deferred instead of dropped, and its retry reaches
		// the held start lock. Observing that lock wait proves the deferral happened before the startup window is
		// closed, instead of relying on a fixed settle.
		await deferralStartupLockWaitObserved.Task.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);

		client.ReleaseEnsureStartedAsync();

		HoverInformation? hover = await hoverTask;

		Assert.IsNotNull(hover);

		// The deferred open is applied once the recovery start settles: the tracked document is reopened with the
		// content the host supplied, and the deferral joins the in-flight startup instead of starting another
		// attempt.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 3, TestPolling.DefaultTimeout));
		Assert.AreEqual(3, client.StartCallCount);

		var reopenedTexts = new List<string>();
		string[] sentMethodNames = client.GetSentMethodNames();

		foreach (JsonElement didOpenParameters in client.GetNotificationParameters("textDocument/didOpen"))
		{
			JsonElement textDocument = didOpenParameters.GetProperty("textDocument");

			if (textDocument.GetProperty("uri").GetString() == new Uri(trackedFilePath).AbsoluteUri)
				reopenedTexts.Add(textDocument.GetProperty("text").GetString()!);
		}

		Assert.AreSequenceEqual([firstContent, thirdContent], reopenedTexts);
		Assert.IsGreaterThanOrEqualTo(0, Array.IndexOf(sentMethodNames, "textDocument/hover"), "The hover request was not dispatched.");
	}

	[TestMethod]
	public async Task GetHoverAsync_ReplaysUntouchedTrackedDocumentsAfterFailedRestartRetry()
	{
		string workspaceRoot = TestPaths.Root;
		string firstFilePath = TestPaths.Script("first.test");
		string secondFilePath = TestPaths.Script("second.test");

		const string firstContent = "local first = 1";
		const string secondContent = "local second = 2";

		using var client = new FakeStatefulLanguageServerClient
		{
			HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(firstFilePath, firstContent);
		provider.OpenDocument(secondFilePath, secondContent);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, TestPolling.DefaultTimeout));
		Assert.AreEqual(1, client.StartCallCount);

		client.IsReady = false;
		client.StartResult = false;

		HoverInformation? failedHover = await provider.GetHoverAsync(
			filePath: firstFilePath,
			documentText: firstContent,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.IsNull(failedHover);
		Assert.AreEqual(2, client.StartCallCount);

		client.StartResult = true;

		HoverInformation? recoveredHover = await provider.GetHoverAsync(
			filePath: firstFilePath,
			documentText: firstContent,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.IsNotNull(recoveredHover);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 4, TestPolling.DefaultTimeout));
		Assert.AreEqual(3, client.StartCallCount);

		Assert.AreSequenceEqual(
			["textDocument/didOpen", "textDocument/didOpen", "textDocument/didOpen", "textDocument/didOpen", "textDocument/hover"],
			client.GetSentMethodNames()
		);
	}

	[TestMethod]
	public async Task GetHoverAsync_ReopensDocumentAfterIncrementalChangeConnectionFailure()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { ThrowIOExceptionOnNextDidChange = true };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		await provider.GetHoverAsync(
			filePath,
			documentText: "local value = 1",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		provider.UpdateDocument(filePath, "local value = 2");

		Assert.IsTrue(await client.WaitForNotificationAsync("textDocument/didChange", TestPolling.DefaultTimeout));

		// Wait for the failed update's cleanup so the hover observes the reopen, not the transient fallback.
		await TestPolling
			.WaitForAsync(
				action: () => Task.FromResult(provider.State),
				isSatisfied: static state => state == LanguageServerFeatureProviderState.Unavailable,
				timeout: TestPolling.DefaultTimeout,
				failureMessage: "Expected the failed update to mark the connection unavailable.",
				describeLastResult: static state => $"state={state}"
			)
			.ConfigureAwait(false);

		await provider.GetHoverAsync(
			filePath,
			documentText: "local value = 2",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreSequenceEqual(
			["textDocument/didOpen", "textDocument/hover", "textDocument/didChange", "textDocument/didOpen", "textDocument/hover"],
			client.GetSentMethodNames()
		);
	}

	[TestMethod]
	public async Task GetHoverAsync_TwoRoots_ReplaysTrackedDocumentsFromEveryRootAfterRestart()
	{
		string primaryRoot = TestPaths.Root;
		string secondaryRoot = TestPaths.SecondaryRoot;
		string primaryFilePath = TestPaths.Script("primary.test");
		string secondaryFilePath = Path.Combine(TestPaths.SecondaryRoot, "Scripts", "secondary.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([primaryRoot, secondaryRoot], client);

		provider.OpenDocument(primaryFilePath, content);
		provider.OpenDocument(secondaryFilePath, content);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, TestPolling.DefaultTimeout));
		Assert.AreEqual(1, client.StartCallCount);

		client.IsReady = false;

		await provider.GetHoverAsync(
			filePath: secondaryFilePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(2, client.StartCallCount);
		Assert.AreEqual(4, client.GetSentMethodCount("textDocument/didOpen"));

		JsonElement[] didOpenNotifications = client.GetNotificationParameters("textDocument/didOpen");
		var reopenedDocumentUris = new List<string>();

		for (int i = 2; i < didOpenNotifications.Length; i++)
			reopenedDocumentUris.Add(didOpenNotifications[i].GetProperty("textDocument").GetProperty("uri").GetString()!);

		Assert.AreSequenceEqual(
			[DocumentUriConversion.CreateFileUri(primaryFilePath), DocumentUriConversion.CreateFileUri(secondaryFilePath)],
			reopenedDocumentUris,
			Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder
		);
	}

	public TestContext? TestContext { get; set; }
}
