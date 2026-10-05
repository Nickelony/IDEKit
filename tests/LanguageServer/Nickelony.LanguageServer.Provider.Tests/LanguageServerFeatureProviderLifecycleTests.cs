using System.Runtime.CompilerServices;
using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers provider lifecycle: disposal, callback admission, and connection-unavailable recovery.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderLifecycleTests
{
	private const string Content = "line one";

	/// <summary>What the provider looked like at the moment its owned client was disposed.</summary>
	private sealed class ClientDisposalObservation
	{
		private bool _observed;
		private bool _signaled;

		private LanguageServerFeatureProviderState _state;

		private int _clientSubscribers;

		internal static ClientDisposalObservation Attach(LanguageServerFeatureProvider provider, FakeLanguageServerClient client)
		{
			var observation = new ClientDisposalObservation();

			client.DisposeObserver = () =>
			{
				observation._observed = true;
				observation._signaled = provider.Composition.Disposal.IsDisposed;
				observation._state = provider.State;

				observation._clientSubscribers =
					client.DiagnosticsPublishedSubscriberCount
					+ client.SemanticTokensRefreshRequestedSubscriberCount
					+ client.DiagnosticRefreshRequestedSubscriberCount
					+ client.ConnectionUnavailableSubscriberCount;
			};

			return observation;
		}

		internal void AssertProviderWasTornDown()
		{
			Assert.IsTrue(_observed, "The client was never disposed.");
			Assert.IsTrue(_signaled, "The disposal was not signaled before the client was disposed.");
			Assert.AreEqual(LanguageServerFeatureProviderState.Disposed, _state);
			Assert.AreEqual(0, _clientSubscribers, "The provider still listened to the client when the client was disposed.");
		}
	}

	private static readonly string s_workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-lifecycle-" + Guid.NewGuid().ToString("N"));
	private static readonly string s_filePath = Path.Combine(s_workspaceRoot, "Scripts", "test.test");

	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan s_longWaitTimeout = TimeSpan.FromSeconds(10);

	[TestMethod]
	public void Dispose_IsIdempotent_AndDisposesTheClientOnce()
	{
		var client = new FakeLanguageServerClient();
		LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		provider.Dispose();
		provider.Dispose();

		Assert.AreEqual(1, client.DisposeCallCount);
		Assert.AreEqual(LanguageServerFeatureProviderState.Disposed, provider.State);
		Assert.IsFalse(provider.IsAvailable);
	}

	[TestMethod]
	public void Dispose_DetachesEveryClientEvent()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		Assert.AreEqual(1, client.DiagnosticsPublishedSubscriberCount);
		Assert.AreEqual(1, client.SemanticTokensRefreshRequestedSubscriberCount);
		Assert.AreEqual(1, client.DiagnosticRefreshRequestedSubscriberCount);
		Assert.AreEqual(1, client.ConnectionUnavailableSubscriberCount);

		provider.Dispose();

		Assert.AreEqual(0, client.DiagnosticsPublishedSubscriberCount);
		Assert.AreEqual(0, client.SemanticTokensRefreshRequestedSubscriberCount);
		Assert.AreEqual(0, client.DiagnosticRefreshRequestedSubscriberCount);
		Assert.AreEqual(0, client.ConnectionUnavailableSubscriberCount);
	}

	[TestMethod]
	public void Dispose_TearsTheProviderDownBeforeItDisposesTheClient()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		var observation = ClientDisposalObservation.Attach(provider, client);

		provider.Dispose();

		observation.AssertProviderWasTornDown();
	}

	[TestMethod]
	public async Task DisposeAsync_TearsTheProviderDownBeforeItDisposesTheClient()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		var observation = ClientDisposalObservation.Attach(provider, client);

		await provider.DisposeAsync().ConfigureAwait(false);

		observation.AssertProviderWasTornDown();
	}

	[TestMethod]
	public void Dispose_ReleasesTheEventSubscribersOfTheHost()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		WeakReference subscriber = Subscribe(provider);

		provider.Dispose();

		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		Assert.IsFalse(subscriber.IsAlive, "A disposed provider must not keep a reference to a host handler.");

		// The provider stays reachable during the collection, so the assertion cannot pass because the whole object was collected.
		GC.KeepAlive(provider);
	}

	[TestMethod]
	public async Task Dispose_CancelsARequestInFlightInsteadOfWaitingForItsTimeout()
	{
		var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		using var client = new FakeLanguageServerClient
		{
			IsReady = false,

			SendRequestHandler = async (_, _, cancellationToken) =>
			{
				requestStarted.TrySetResult();
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);

				return null;
			},
		};

		var host = new LanguageServerFeatureProviderTestHost(
			[s_workspaceRoot],
			client,
			new LanguageServerFeatureProviderOptions { RequestTimeout = TimeSpan.FromMinutes(5), RequestTimeoutRestartThreshold = 1 }
		);

		LanguageServerFeatureProvider provider = host.Provider;

		Task<HoverInformation?> hover = host.GetHoverAtOffsetAsync(
			filePath: s_filePath,
			Content,
			offset: 0,
			cancellationToken: TestContext.CancellationToken
		);

		await requestStarted.Task.WaitAsync(s_waitTimeout).ConfigureAwait(false);

		provider.Dispose();

		// Disposal cancels the disposal token the request is linked to, so the request returns its fallback at once.
		Assert.IsNull(await hover.WaitAsync(s_waitTimeout).ConfigureAwait(false));

		// The cancellation belongs to the disposal, not to the request timeout, so it cannot count toward a connection restart.
		Assert.IsEmpty(client.MarkedUnhealthyGenerations);
	}

	[TestMethod]
	public async Task OpenDocument_AfterDispose_StartsAndSendsNothing()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		provider.Dispose();
		provider.OpenDocument(s_filePath, Content);

		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(0, client.StartCallCount);
		Assert.AreEqual(0, client.GetSentMethodCount("textDocument/didOpen"));
	}

	[TestMethod]
	public async Task DisposeAsync_WhenAnotherCallerOwnsTeardown_CompletesOnlyAfterTeardownFinishes()
	{
		var disposeGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var client = new FakeLanguageServerClient { DisposeGate = disposeGate.Task };

		LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		try
		{
			// The first caller owns teardown and blocks in the client's disposal until this test releases the gate.
			Task firstDisposeTask = provider.DisposeAsync().AsTask();
			Assert.IsTrue(await TestPolling.ForConditionAsync(() => client.DisposeCallCount == 1, s_waitTimeout).ConfigureAwait(false));

			// A later caller must observe the teardown the first caller started instead of returning while it continues.
			Task secondDisposeTask = provider.DisposeAsync().AsTask();

			Task completedTask = await Task.WhenAny(secondDisposeTask, Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken))
				.ConfigureAwait(false);

			Assert.AreNotSame(secondDisposeTask, completedTask, "A later dispose caller must wait for the teardown that is still running.");

			disposeGate.TrySetResult(true);

			await firstDisposeTask.ConfigureAwait(false);
			await secondDisposeTask.ConfigureAwait(false);

			Assert.AreEqual(1, client.DisposeCallCount);
		}
		finally
		{
			// Release the gate even when an assertion failed, so the client disposed at scope end cannot block.
			disposeGate.TrySetResult(true);
		}
	}

	[TestMethod]
	public async Task Dispose_WhenAnotherCallerOwnsTeardown_BlocksUntilTeardownFinishes()
	{
		var disposeGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var client = new FakeLanguageServerClient { DisposeGate = disposeGate.Task };

		LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		try
		{
			// The asynchronous caller becomes the teardown owner before the synchronous caller is started on the pool,
			// so the synchronous caller is deterministically a later caller.
			Task firstDisposeTask = provider.DisposeAsync().AsTask();
			Assert.IsTrue(await TestPolling.ForConditionAsync(() => client.DisposeCallCount == 1, s_waitTimeout).ConfigureAwait(false));

			var secondDisposeTask = Task.Run(provider.Dispose, TestContext.CancellationToken);

			Task completedTask = await Task.WhenAny(secondDisposeTask, Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken))
				.ConfigureAwait(false);

			Assert.AreNotSame(
				secondDisposeTask,
				completedTask,
				"A later synchronous dispose caller must wait for the teardown that is still running."
			);

			disposeGate.TrySetResult(true);

			await firstDisposeTask.ConfigureAwait(false);
			await secondDisposeTask.ConfigureAwait(false);

			Assert.AreEqual(1, client.DisposeCallCount);
		}
		finally
		{
			// Release the gate even when an assertion failed, so the client disposed at scope end cannot block.
			disposeGate.TrySetResult(true);
		}
	}

	[TestMethod]
	public async Task Dispose_ClosesCallbackAdmissionAndDetachesClientEvents()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		int diagnosticsUpdates = 0;

		provider.DiagnosticsUpdated += (_, _) => diagnosticsUpdates++;

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		client.PublishDiagnostics(new PublishDiagnosticsParams(new Uri(s_filePath).AbsoluteUri, null, []));
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => diagnosticsUpdates == 1, s_waitTimeout).ConfigureAwait(false));

		provider.Dispose();

		// Raising diagnostics after disposal must neither invoke subscribers nor throw, and a late
		// subscription is dropped by the closed admission path.
		client.PublishDiagnostics(new PublishDiagnosticsParams(new Uri(s_filePath).AbsoluteUri, null, []));
		provider.DiagnosticsUpdated += (_, _) => diagnosticsUpdates++;

		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(1, diagnosticsUpdates);
	}

	[TestMethod]
	public async Task Dispose_WhileADocumentUpdateIsRunning_CancelsTheRunningUpdate()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };

		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		var updateEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var cancellationObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		await provider
			.GetHoverAsync(
				filePath: s_filePath,
				documentText: Content,
				position: new TextPosition(0, 0),
				cancellationToken: TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		// The change notification is gated so the latest-only update stays running until the test disposes the
		// provider, and the handler records that its token observed the cancellation.
		client.SendNotificationHandler = async (method, _, cancellationToken) =>
		{
			if (!string.Equals(method, "textDocument/didChange", StringComparison.Ordinal))
				return;

			updateEntered.TrySetResult(true);

			try
			{
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				cancellationObserved.TrySetResult(true);
				throw;
			}
		};

		provider.UpdateDocument(s_filePath, "line two");

		await updateEntered.Task.WaitAsync(s_waitTimeout, TestContext.CancellationToken).ConfigureAwait(false);

		// Disposal cancels every active latest-only update, including the one that is already running.
		provider.Dispose();

		Assert.IsTrue(
			await cancellationObserved.Task.WaitAsync(s_waitTimeout, TestContext.CancellationToken).ConfigureAwait(false),
			"A running document update must observe the disposal-driven cancellation."
		);
	}

	[TestMethod]
	public async Task RenameDocument_WhenTheClientIsNotReady_RenamesWithoutTrafficAndReopensOnTheNextRequest()
	{
		string renamedFilePath = Path.Combine(s_workspaceRoot, "Scripts", "renamed.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var host = new LanguageServerFeatureProviderTestHost([s_workspaceRoot], client);

		LanguageServerFeatureProvider provider = host.Provider;

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		client.RaiseConnectionUnavailable(client.ConnectionGeneration);
		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, provider.State);

		// The connection is down, so the rename only renames the tracked record: no close or open traffic is sent, and
		// the record is marked as not synchronized on the server.
		provider.RenameDocument(s_filePath, renamedFilePath, Content);

		Assert.IsTrue(await TestPolling.ForConditionAsync(() => host.RenamedPaths.Contains(renamedFilePath), s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(0, client.GetSentMethodCount("textDocument/didClose"));

		// The next request restarts the connection and reopens the document under its new path.
		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: renamedFilePath,
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));

		Assert.AreEqual(
			new Uri(renamedFilePath).AbsoluteUri,
			client.GetLastNotificationParameters("textDocument/didOpen").GetProperty("textDocument").GetProperty("uri").GetString()
		);
	}

	[TestMethod]
	public async Task ConnectionUnavailable_MarksUnavailable_AndNextRequestRestartsWithReopen()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		int capabilitiesChangedCount = 0;

		provider.CapabilitiesChanged += (_, _) => capabilitiesChangedCount++;

		provider.OpenDocument(s_filePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);
		Assert.IsTrue(provider.IsAvailable);

		long lostGeneration = client.ConnectionGeneration;
		client.RaiseConnectionUnavailable(lostGeneration);

		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, provider.State);
		Assert.IsFalse(provider.IsAvailable);
		Assert.IsFalse(provider.Capabilities.SupportsReferences);

		// The next request completes the documented fallback value and restarts the connection; the tracked
		// document is reopened on the new generation.
		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.AreEqual(2, client.StartCallCount);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);

		// Ready after the open, unavailable on connection loss, and ready again after the restart.
		Assert.AreEqual(3, capabilitiesChangedCount);
	}

	[TestMethod]
	public async Task MissingClient_DocumentMembersReportThePersistentFailureWithoutThrowing()
	{
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client: null);

		provider.OpenDocument(s_filePath, Content);
		provider.UpdateDocument(s_filePath, "line two");
		provider.CloseDocument(s_filePath);

		// The members are fire-and-forget: they must not throw, and the provider reports the persistent failure.
		Assert.IsTrue(
			await TestPolling
				.ForConditionAsync(() => provider.State == LanguageServerFeatureProviderState.Failed, TimeSpan.FromSeconds(2))
				.ConfigureAwait(false)
		);
	}

	[TestMethod]
	public async Task FailedRestartReplay_IsResumedByTheNextStartEvenWhileTheClientStaysReady()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		client.RaiseConnectionUnavailable(client.ConnectionGeneration);
		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, provider.State);

		bool failReopenSend = true;

		client.SendNotificationHandler = (method, _, _) =>
		{
			if (failReopenSend && string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal))
				throw new IOException("Simulated reopen failure.");

			return Task.CompletedTask;
		};

		// The restart succeeds but the replay fails: the provider reports the transient failure and keeps the
		// document for a later resume instead of leaving a record that claims a server-open document.
		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.ContainsSingle(
			method => string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal),
			client.GetAttemptedNotificationMethodNames()
		);

		failReopenSend = false;

		// The next start resumes the replay even though the client is already ready again.
		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);
		Assert.IsNotNull(provider.GetTrackedSnapshot(s_filePath));
	}

	[TestMethod]
	public async Task CloseDuringRestartReplay_StillClosesAReopenedServerDocument()
	{
		string secondFilePath = Path.Combine(s_workspaceRoot, "Scripts", "second.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		provider.OpenDocument(secondFilePath, "line two");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));

		client.RaiseConnectionUnavailable(client.ConnectionGeneration);

		var replayGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int reopenAttempts = 0;

		client.SendNotificationHandler = async (method, _, cancellationToken) =>
		{
			if (string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal) && Interlocked.Increment(ref reopenAttempts) == 2)
			{
				// Hold the second reopen so the close below lands while the replay still owns the startup flow.
				await replayGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
			}
		};

		Task<HoverInformation?> hoverTask = provider.GetHoverAsync(
			filePath: s_filePath,
			documentText: Content,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		// Wait until the first reopened document was delivered; the second reopen is blocked.
		Assert.IsTrue(
			await TestPolling.ForConditionAsync(() => client.GetSentMethodCount("textDocument/didOpen") == 3, s_waitTimeout).ConfigureAwait(false)
		);

		string reopenedUri =
			client.GetLastNotificationParameters("textDocument/didOpen").GetProperty("textDocument").GetProperty("uri").GetString()
			?? throw new AssertFailedException("Expected a reopened document URI.");

		string reopenedPath = string.Equals(reopenedUri, new Uri(s_filePath).AbsoluteUri, StringComparison.Ordinal) ? s_filePath : secondFilePath;

		provider.CloseDocument(reopenedPath);

		// The close must close the server copy the replay reopened, instead of being suppressed while the startup
		// flow is still running.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, s_waitTimeout).ConfigureAwait(false));

		replayGate.TrySetResult(true);
		Assert.IsNull(await hoverTask.ConfigureAwait(false));

		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);
	}

	[TestMethod]
	public async Task UpdateDocument_AfterConnectionLoss_RestartsAndReopensWithoutEscapingTheUpdatePath()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		client.RaiseConnectionUnavailable(client.ConnectionGeneration);
		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, provider.State);

		// The update entry point ensures the connection outside the document scheduler slot, so the restart
		// replay can reopen the tracked document without violating the scheduler's reentrancy contract.
		provider.UpdateDocument(s_filePath, "line two");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);
		Assert.IsTrue(provider.IsAvailable);

		JsonElement change = client.GetLastNotificationParameters("textDocument/didChange");

		Assert.AreEqual("two", change.GetProperty("contentChanges")[0].GetProperty("text").GetString());
	}

	[TestMethod]
	public async Task OpenDocument_AfterConnectionLoss_StartsTheRestartAndOpensTheNewDocument()
	{
		string secondFilePath = Path.Combine(s_workspaceRoot, "Scripts", "second.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		client.RaiseConnectionUnavailable(client.ConnectionGeneration);
		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, provider.State);

		// The open entry point restarts the connection outside the per-document scheduler slot, replays the first
		// document, and then opens the second one on the restarted connection.
		provider.OpenDocument(secondFilePath, "line two");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 3, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);

		string? lastOpenedUri = client
			.GetLastNotificationParameters("textDocument/didOpen")
			.GetProperty("textDocument")
			.GetProperty("uri")
			.GetString();

		Assert.AreEqual(new Uri(secondFilePath).AbsoluteUri, lastOpenedUri);
	}

	[TestMethod]
	public async Task UpdateDocument_DuringConnectionRestartReplay_CompletesWithoutDeadlockingTheDocumentLane()
	{
		string secondFilePath = Path.Combine(s_workspaceRoot, "Scripts", "second.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		provider.OpenDocument(secondFilePath, "line two");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));

		client.RaiseConnectionUnavailable(client.ConnectionGeneration);

		var replayGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int reopenAttempts = 0;

		client.SendNotificationHandler = async (method, _, cancellationToken) =>
		{
			if (string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal) && Interlocked.Increment(ref reopenAttempts) == 1)
			{
				// Hold the first replayed reopen so the concurrent updates below land while the replay still owns
				// the startup flow and the startup lock.
				await replayGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
			}
		};

		// A request starts the restart; its replay blocks on the first tracked document.
		Task<HoverInformation?> hoverTask = provider.GetHoverAsync(
			filePath: secondFilePath,
			documentText: "line four",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.IsTrue(await TestPolling.ForConditionAsync(() => Volatile.Read(ref reopenAttempts) == 1, s_waitTimeout).ConfigureAwait(false));

		// Both updates must queue behind the startup flow instead of deadlocking their document lanes.
		provider.UpdateDocument(s_filePath, "line three");
		provider.UpdateDocument(secondFilePath, "line four");

		replayGate.TrySetResult(true);

		Assert.IsNull(await hoverTask.ConfigureAwait(false));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 2, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);
	}

	[TestMethod]
	public async Task RestartStartedByACanceledRequest_StillCompletesAndReopensTheDocument()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		client.RaiseConnectionUnavailable(client.ConnectionGeneration);
		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, provider.State);

		var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		client.EnsureStartedAsyncHandler = async cancellationToken =>
		{
			await startGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
			return true;
		};

		using var cancellationTokenSource = new CancellationTokenSource();

		Task<HoverInformation?> hoverTask = provider.GetHoverAsync(
			filePath: s_filePath,
			documentText: Content,
			position: new TextPosition(0, 0),
			cancellationToken: cancellationTokenSource.Token
		);

		// Wait until the restart is in flight, then cancel the triggering request: the restart serves every
		// consumer of the provider, so it must still complete and reopen the tracked document, while the request
		// itself surfaces the caller's cancellation.
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => client.StartCallCount == 2, s_waitTimeout).ConfigureAwait(false));

		cancellationTokenSource.Cancel();
		startGate.TrySetResult(true);

		await Assert.ThrowsAsync<OperationCanceledException>(async () => await hoverTask.ConfigureAwait(false)).ConfigureAwait(false);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);
	}

	[TestMethod]
	public async Task ColdStart_ReportsTheStartingStateWhileStartupIsInFlight()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		client.EnsureStartedAsyncHandler = async cancellationToken =>
		{
			await startGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
			return true;
		};

		provider.OpenDocument(s_filePath, Content);

		// The lifecycle member triggers the lazy start; while the gated start is in flight, the provider must
		// report Starting rather than the initial Unavailable state.
		Assert.IsTrue(
			await TestPolling
				.ForConditionAsync(() => provider.State == LanguageServerFeatureProviderState.Starting, s_waitTimeout)
				.ConfigureAwait(false)
		);

		startGate.TrySetResult(true);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);
	}

	[TestMethod]
	public async Task FailedRestartReplay_ResumesWithTheRemainingDocumentsOnly()
	{
		string secondFilePath = Path.Combine(s_workspaceRoot, "Scripts", "second.test");
		string thirdFilePath = Path.Combine(s_workspaceRoot, "Scripts", "third.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		provider.OpenDocument(secondFilePath, "line two");
		provider.OpenDocument(thirdFilePath, "line three");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 3, s_waitTimeout).ConfigureAwait(false));

		client.RaiseConnectionUnavailable(client.ConnectionGeneration);

		int reopenAttempts = 0;

		client.SendNotificationHandler = (method, _, _) =>
		{
			if (string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal) && Interlocked.Increment(ref reopenAttempts) == 2)
				throw new IOException("Simulated reopen failure.");

			return Task.CompletedTask;
		};

		// The restart replay reopens the first document and fails on the second one: the failed document and
		// everything after it stay pending, and the provider does not report ready.
		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.IsFalse(provider.IsAvailable);

		// The next start resumes the replay with the remaining documents only and reports ready.
		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 6, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);
	}

	[TestMethod]
	public async Task FailedRestartReplay_WhenTheSessionIsReplaced_ReopensEveryTrackedDocument()
	{
		string secondFilePath = Path.Combine(s_workspaceRoot, "Scripts", "second.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		provider.OpenDocument(secondFilePath, "line two");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));

		client.RaiseConnectionUnavailable(client.ConnectionGeneration);

		int reopenAttempts = 0;

		client.SendNotificationHandler = (method, _, _) =>
		{
			if (string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal) && Interlocked.Increment(ref reopenAttempts) == 2)
				throw new IOException("Simulated reopen failure.");

			return Task.CompletedTask;
		};

		// The restart replay reopens one document and fails on the second one, leaving a partial pending list
		// while the connection stays ready.
		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, provider.State);

		// A start handler always runs and activates a fresh generation, so the resume below replaces the session
		// the partial list was captured for. The framework must capture the complete set again: both tracked
		// documents need a didOpen on the new session.
		client.EnsureStartedAsyncHandler = static _ => Task.FromResult(true);

		var reopenedUris = new List<string>();

		client.SendNotificationHandler = (method, parameters, _) =>
		{
			if (string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal))
				reopenedUris.Add(JsonSerializer.SerializeToElement(parameters).GetProperty("textDocument").GetProperty("uri").GetString()!);

			return Task.CompletedTask;
		};

		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.IsTrue(
			await TestPolling.ForConditionAsync(() => reopenedUris.Count >= 2, s_waitTimeout).ConfigureAwait(false),
			$"Expected both tracked documents to be reopened on the replacement session. Reopened: {string.Join(", ", reopenedUris)}"
		);

		Assert.AreSequenceEqual(
			[new Uri(s_filePath).AbsoluteUri, new Uri(secondFilePath).AbsoluteUri],
			reopenedUris,
			Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder
		);

		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);
	}

	[TestMethod]
	public async Task DisposeAsync_DisposesTheClientOnce()
	{
		var client = new FakeLanguageServerClient();
		LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		await provider.DisposeAsync().ConfigureAwait(false);
		await provider.DisposeAsync().ConfigureAwait(false);

		provider.Dispose();

		Assert.AreEqual(1, client.DisposeCallCount);
		Assert.IsTrue(client.IsDisposed);
		Assert.AreEqual(LanguageServerFeatureProviderState.Disposed, provider.State);
		Assert.IsFalse(provider.IsAvailable);
	}

	[TestMethod]
	public async Task UnexpectedStartupException_CountsTowardTheHardFailureThreshold()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };

		using LanguageServerFeatureProvider provider = TestProviders.Create(
			[s_workspaceRoot],
			client,
			new LanguageServerFeatureProviderOptions { HardStartupFailureThreshold = 2 }
		);

		var failures = new List<StartupFailure>();

		provider.StartupFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

		client.EnsureStartedAsyncHandler = static _ => throw new IOException("Simulated startup fault.");

		// An unexpected startup exception counts like a false start result: the first fault reports a transient
		// failure and leaves the provider retrying.
		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, provider.State);
		Assert.HasCount(1, failures);
		Assert.IsFalse(failures[0].IsPersistent);

		// The second fault reaches the threshold and disables the language features until the provider is recreated.
		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.AreEqual(LanguageServerFeatureProviderState.Failed, provider.State);
		Assert.HasCount(2, failures);
		Assert.IsTrue(failures[1].IsPersistent);

		// The failed state stops further startup attempts.
		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.AreEqual(2, client.StartCallCount);
	}

	[TestMethod]
	public async Task RequestQueuedBehindTheStart_CanceledWhileTheStartRestoresWatching_LeavesTheProviderAvailable()
	{
		string queuedFilePath = Path.Combine(s_workspaceRoot, "Scripts", "queued.test");
		var position = new TextPosition(0, 0);

		using var client = new FakeLanguageServerClient { IsReady = false, SupportsHover = true };
		using var tracker = new CaptureGatedSnapshotTracker();
		using var requestSource = new CancellationTokenSource();

		var startEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var requestQueued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		int lockWaits = 0;
		int capabilitiesChanges = 0;

		client.EnsureStartedAsyncHandler = async _ =>
		{
			startEntered.TrySetResult();
			return await startGate.Task.ConfigureAwait(false);
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create(
			workspaceRootDirectoryPaths: [s_workspaceRoot],
			client,
			workspaceFileWatcherFactory: new RecordingFileWatcherFactory().Factory,
			workspaceSnapshotTrackerFactory: (_, _) => tracker,

			testHooks: new LanguageServerFeatureProviderTestHooks
			{
				StartupLockWaitObserver = () =>
				{
					if (Interlocked.Increment(ref lockWaits) >= 2)
						requestQueued.TrySetResult();
				},
			}
		);

		provider.CapabilitiesChanged += (_, _) => Interlocked.Increment(ref capabilitiesChanges);

		try
		{
			// The first request starts the server; the second one, with a token of its own, queues behind that start.
			Task startingRequest = provider.GetHoverAsync(
				filePath: s_filePath,
				documentText: Content,
				position,
				cancellationToken: CancellationToken.None
			);

			await startEntered.Task.WaitAsync(s_longWaitTimeout, TestContext.CancellationToken).ConfigureAwait(false);

			Task<HoverInformation?> queuedRequest = provider.GetHoverAsync(
				filePath: queuedFilePath,
				documentText: Content,
				position,
				cancellationToken: requestSource.Token
			);

			await requestQueued.Task.WaitAsync(s_longWaitTimeout, TestContext.CancellationToken).ConfigureAwait(false);

			startGate.SetResult(true);

			// The start completed, and the first request restores workspace watching while it holds the startup lock.
			Assert.IsTrue(
				await TestPolling.ForConditionAsync(() => tracker.CaptureStarted.IsSet, s_longWaitTimeout).ConfigureAwait(false),
				"The start never reached the workspace snapshot capture."
			);

			requestSource.Cancel();

			await Assert.ThrowsAsync<OperationCanceledException>(() => queuedRequest).ConfigureAwait(false);

			Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);
			Assert.IsTrue(provider.IsAvailable);
			Assert.IsTrue(provider.Capabilities.SupportsHover);
			Assert.AreEqual(1, Volatile.Read(ref capabilitiesChanges), "Only the start announces its completion.");

			tracker.ReleaseCapture.Set();

			await startingRequest.ConfigureAwait(false);

			Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);
			Assert.IsTrue(provider.IsAvailable);
			Assert.AreEqual(1, client.StartCallCount);
		}
		finally
		{
			startGate.TrySetResult(true);
			tracker.ReleaseCapture.Set();
		}
	}

	/// <summary>A snapshot tracker whose first capture blocks until it is released, standing in for a slow capture of a large workspace.</summary>
	private sealed class CaptureGatedSnapshotTracker : IFileSnapshotTracker, IDisposable
	{
		public ManualResetEventSlim CaptureStarted { get; } = new(false);

		public ManualResetEventSlim ReleaseCapture { get; } = new(false);

		public void CaptureTrackedSnapshot()
		{
			CaptureStarted.Set();
			ReleaseCapture.Wait(TimeSpan.FromSeconds(30));
		}

		public Dictionary<string, FileSnapshotEntry> CloneTrackedSnapshot() => new(LocalPathComparisonPolicy.ForCurrentPlatform.Comparer);

		public Dictionary<string, FileSnapshotEntry> ReplaceTrackedSnapshotWithCurrent() => CloneTrackedSnapshot();

		public void ApplyChanges(IReadOnlyList<FileChange> changes) { }

		public FileChangeBatch BuildDeltaBatch(
			IReadOnlyDictionary<string, FileSnapshotEntry> previousSnapshot,
			IReadOnlyDictionary<string, FileSnapshotEntry> currentSnapshot
		) => new([]);

		public void Dispose()
		{
			CaptureStarted.Dispose();
			ReleaseCapture.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference Subscribe(LanguageServerFeatureProvider provider)
	{
		object target = new();

		provider.CapabilitiesChanged += (_, _) => GC.KeepAlive(target);

		return new WeakReference(target);
	}

	public TestContext? TestContext { get; set; }
}
