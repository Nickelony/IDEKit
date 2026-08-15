using Nickelony.IDEKit.IntelliSense.Hover;
using System.Text.Json;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers the provider lifecycle: disposal ordering and idempotency, callback admission closure, and the
/// transport-unavailable wiring with restart and tracked-document reopen.
/// </summary>
[TestClass]
public sealed class LanguageServerIntelliSenseProviderBaseLifecycleTests
{
	private const string Content = "line one";

	private static readonly string s_workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-lifecycle-" + Guid.NewGuid().ToString("N"));
	private static readonly string s_filePath = Path.Combine(s_workspaceRoot, "Scripts", "test.test");
	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(2);

	[TestMethod]
	public void Dispose_IsIdempotent_AndDisposesTheClientOnce()
	{
		var client = new FakeLanguageServerClient();
		var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.Dispose();
		provider.Dispose();

		Assert.AreEqual(1, client.DisposeCallCount);
		Assert.AreEqual(1, provider.OnDisposingCallCount);
		Assert.AreEqual(false, provider.ClientWasDisposedAtOnDisposing);
		Assert.AreEqual(LanguageServerProviderState.Disposed, provider.State);
		Assert.IsFalse(provider.IsAvailable);
	}

	[TestMethod]
	public async Task DisposeAsync_WhenAnotherCallerOwnsTeardown_CompletesOnlyAfterTeardownFinishes()
	{
		var disposeGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var client = new FakeLanguageServerClient { DisposeGate = disposeGate.Task };
		var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		try
		{
			// The first caller owns teardown and blocks in the client's disposal until this test releases the gate.
			Task firstDisposeTask = provider.DisposeAsync().AsTask();
			Assert.IsTrue(await TestPolling.ForConditionAsync(() => client.DisposeCallCount == 1, s_waitTimeout).ConfigureAwait(false));

			// A later caller must observe the teardown the first caller started instead of returning while it continues.
			Task secondDisposeTask = provider.DisposeAsync().AsTask();
			Task completedTask = await Task.WhenAny(secondDisposeTask, Task.Delay(TestPolling.AbsenceWindow)).ConfigureAwait(false);

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
		var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		try
		{
			// The asynchronous caller becomes the teardown owner before the synchronous caller is started on the pool,
			// so the synchronous caller is deterministically a later caller.
			Task firstDisposeTask = provider.DisposeAsync().AsTask();
			Assert.IsTrue(await TestPolling.ForConditionAsync(() => client.DisposeCallCount == 1, s_waitTimeout).ConfigureAwait(false));

			Task secondDisposeTask = Task.Run(provider.Dispose);
			Task completedTask = await Task.WhenAny(secondDisposeTask, Task.Delay(TestPolling.AbsenceWindow)).ConfigureAwait(false);

			Assert.AreNotSame(secondDisposeTask, completedTask, "A later synchronous dispose caller must wait for the teardown that is still running.");

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
		var provider = new TestLanguageServerProvider([s_workspaceRoot], client);
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

		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.AreEqual(1, diagnosticsUpdates);
	}

	[TestMethod]
	public async Task Dispose_WhileADocumentUpdateIsRunning_CancelsTheRunningUpdate()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);
		var updateEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var cancellationObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		await provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0)).ConfigureAwait(false);

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

		await updateEntered.Task.WaitAsync(s_waitTimeout).ConfigureAwait(false);

		// Disposal cancels every active latest-only update, including the one that is already running; the
		// queued-not-started half of that call is pinned by the scheduler suite.
		provider.Dispose();

		Assert.IsTrue(await cancellationObserved.Task.WaitAsync(s_waitTimeout).ConfigureAwait(false),
			"A running document update must observe the disposal-driven cancellation.");
	}

	[TestMethod]
	public async Task MoveDocument_WhenTheClientIsNotReady_RekeysWithoutTrafficAndReopensOnTheNextRequest()
	{
		string renamedFilePath = Path.Combine(s_workspaceRoot, "Scripts", "renamed.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		client.RaiseTransportUnavailable(client.TransportGeneration);
		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);

		// The transport is down, so the move only rekeys the tracked record: no close or open traffic is sent, and
		// the record is marked as not synchronized on the server.
		provider.MoveDocument(s_filePath, renamedFilePath, Content);

		Assert.IsTrue(await TestPolling.ForConditionAsync(() => provider.MovedPaths.Contains(renamedFilePath), s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(0, client.GetSentMethodCount("textDocument/didClose"));

		// The next request restarts the transport and reopens the document under its new path.
		Assert.IsNull(await provider.GetHoverAsync(renamedFilePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(new Uri(renamedFilePath).AbsoluteUri, client.GetLastNotificationParameters("textDocument/didOpen")
			.GetProperty("textDocument").GetProperty("uri").GetString());
	}

	[TestMethod]
	public async Task TransportUnavailable_MarksUnavailable_AndNextRequestRestartsWithReopen()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);
		int capabilitiesChangedCount = 0;

		provider.CapabilitiesChanged += (_, _) => capabilitiesChangedCount++;

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
		Assert.IsTrue(provider.IsAvailable);

		long lostGeneration = client.TransportGeneration;
		client.RaiseTransportUnavailable(lostGeneration);

		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);
		Assert.IsFalse(provider.IsAvailable);
		Assert.IsFalse(provider.SupportsReferences);

		// The next request completes the documented fallback value and restarts the transport; the tracked
		// document is reopened on the new generation.
		Assert.IsNull(await provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));

		Assert.AreEqual(2, client.StartCallCount);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);

		// Ready after the open, unavailable on transport loss, and ready again after the restart.
		Assert.AreEqual(3, capabilitiesChangedCount);
	}

	[TestMethod]
	public async Task MissingClient_DocumentMembersReportThePersistentFailureWithoutThrowing()
	{
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client: null);

		provider.OpenDocument(s_filePath, Content);
		provider.UpdateDocument(s_filePath, "line two");
		provider.CloseDocument(s_filePath);

		// The members are fire-and-forget: they must not throw, and the provider reports the persistent failure.
		Assert.IsTrue(await TestPolling.ForConditionAsync(
			() => provider.State == LanguageServerProviderState.Failed,
			TimeSpan.FromSeconds(2)).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task FailedRestartReplay_IsResumedByTheNextStartEvenWhileTheClientStaysReady()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		client.RaiseTransportUnavailable(client.TransportGeneration);
		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);

		bool failReopenSend = true;

		client.SendNotificationHandler = (method, _, _) =>
		{
			if (failReopenSend && string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal))
				throw new IOException("Simulated reopen failure.");

			return Task.CompletedTask;
		};

		// The restart succeeds but the replay fails: the provider reports the transient failure and keeps the
		// document for a later resume instead of leaving a record that claims a server-open document.
		Assert.IsNull(await provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));

		Assert.AreEqual(1, client.GetAttemptedNotificationMethodNames().Count(method => string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal)));

		failReopenSend = false;

		// The next start resumes the replay even though the client is already ready again.
		Assert.IsNull(await provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
		Assert.IsNotNull(provider.GetTrackedSnapshot(s_filePath));
	}

	[TestMethod]
	public async Task CloseDuringRestartReplay_StillClosesAReopenedServerDocument()
	{
		string secondFilePath = Path.Combine(s_workspaceRoot, "Scripts", "second.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		provider.OpenDocument(secondFilePath, "line two");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));

		client.RaiseTransportUnavailable(client.TransportGeneration);

		var replayGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int reopenAttempts = 0;

		client.SendNotificationHandler = async (method, _, cancellationToken) =>
		{
			if (string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal)
				&& Interlocked.Increment(ref reopenAttempts) == 2)
			{
				// Hold the second reopen so the close below lands while the replay still owns the startup flow.
				await replayGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
			}
		};

		Task<TextHoverInfo?> hoverTask = provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0));

		// Wait until the first reopened document was delivered; the second reopen is blocked.
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => client.GetSentMethodCount("textDocument/didOpen") == 3, s_waitTimeout).ConfigureAwait(false));

		string reopenedUri = client.GetLastNotificationParameters("textDocument/didOpen").GetProperty("textDocument").GetProperty("uri").GetString()
			?? throw new AssertFailedException("Expected a reopened document URI.");
		string reopenedPath = string.Equals(reopenedUri, new Uri(s_filePath).AbsoluteUri, StringComparison.Ordinal) ? s_filePath : secondFilePath;

		provider.CloseDocument(reopenedPath);

		// The close must close the server copy the replay reopened, instead of being suppressed while the startup
		// flow is still running.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, s_waitTimeout).ConfigureAwait(false));

		replayGate.TrySetResult(true);
		Assert.IsNull(await hoverTask.ConfigureAwait(false));

		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
	}

	[TestMethod]
	public async Task UpdateDocument_AfterTransportLoss_RestartsAndReopensWithoutEscapingTheUpdatePath()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		client.RaiseTransportUnavailable(client.TransportGeneration);
		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);

		// The update entry point ensures the transport outside the document scheduler slot, so the restart
		// replay can reopen the tracked document without violating the scheduler's reentrancy contract.
		provider.UpdateDocument(s_filePath, "line two");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
		Assert.IsTrue(provider.IsAvailable);

		JsonElement change = client.GetLastNotificationParameters("textDocument/didChange");

		Assert.AreEqual("two", change.GetProperty("contentChanges")[0].GetProperty("text").GetString());
	}

	[TestMethod]
	public async Task OpenDocument_AfterTransportLoss_StartsTheRestartAndOpensTheNewDocument()
	{
		string secondFilePath = Path.Combine(s_workspaceRoot, "Scripts", "second.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		client.RaiseTransportUnavailable(client.TransportGeneration);
		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);

		// The open entry point restarts the transport outside the per-document scheduler slot, replays the first
		// document, and then opens the second one on the restarted transport.
		provider.OpenDocument(secondFilePath, "line two");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 3, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);

		string? lastOpenedUri = client.GetLastNotificationParameters("textDocument/didOpen")
			.GetProperty("textDocument").GetProperty("uri").GetString();

		Assert.AreEqual(new Uri(secondFilePath).AbsoluteUri, lastOpenedUri);
	}

	[TestMethod]
	public async Task UpdateDocument_DuringTransportRestartReplay_CompletesWithoutDeadlockingTheDocumentChain()
	{
		string secondFilePath = Path.Combine(s_workspaceRoot, "Scripts", "second.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		provider.OpenDocument(secondFilePath, "line two");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));

		client.RaiseTransportUnavailable(client.TransportGeneration);

		var replayGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int reopenAttempts = 0;

		client.SendNotificationHandler = async (method, _, cancellationToken) =>
		{
			if (string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal)
				&& Interlocked.Increment(ref reopenAttempts) == 1)
			{
				// Hold the first replayed reopen so the concurrent updates below land while the replay still owns
				// the startup flow and the startup lock.
				await replayGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
			}
		};

		// A request starts the restart; its replay blocks on the first tracked document.
		Task<TextHoverInfo?> hoverTask = provider.GetHoverAsync(secondFilePath, "line four", new TextPosition(0, 0));

		Assert.IsTrue(await TestPolling.ForConditionAsync(() => Volatile.Read(ref reopenAttempts) == 1, s_waitTimeout).ConfigureAwait(false));

		// Both updates must queue behind the startup flow instead of deadlocking their document chains.
		provider.UpdateDocument(s_filePath, "line three");
		provider.UpdateDocument(secondFilePath, "line four");

		replayGate.TrySetResult(true);

		Assert.IsNull(await hoverTask.ConfigureAwait(false));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 2, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
	}

	[TestMethod]
	public async Task RestartStartedByACanceledRequest_StillCompletesAndReopensTheDocument()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		client.RaiseTransportUnavailable(client.TransportGeneration);
		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);

		var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		client.StartAsyncHandler = async cancellationToken =>
		{
			await startGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
			return true;
		};

		using var cancellationTokenSource = new CancellationTokenSource();
		Task<TextHoverInfo?> hoverTask = provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0), cancellationTokenSource.Token);

		// Wait until the restart is in flight, then cancel the triggering request: the restart serves every
		// consumer of the provider, so it must still complete and reopen the tracked document, while the request
		// itself surfaces the caller's cancellation.
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => client.StartCallCount == 2, s_waitTimeout).ConfigureAwait(false));

		cancellationTokenSource.Cancel();
		startGate.TrySetResult(true);

		await Assert.ThrowsAsync<OperationCanceledException>(async () => await hoverTask.ConfigureAwait(false)).ConfigureAwait(false);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
	}

	[TestMethod]
	public async Task ColdStart_ReportsTheStartingStateWhileStartupIsInFlight()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		client.StartAsyncHandler = async cancellationToken =>
		{
			await startGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
			return true;
		};

		provider.OpenDocument(s_filePath, Content);

		// The lifecycle member triggers the lazy start; while the gated start is in flight, the provider must
		// report Starting rather than the initial Unavailable state.
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => provider.State == LanguageServerProviderState.Starting, s_waitTimeout).ConfigureAwait(false));

		startGate.TrySetResult(true);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
	}

	[TestMethod]
	public async Task FailedRestartReplay_ResumesWithTheRemainingDocumentsOnly()
	{
		string secondFilePath = Path.Combine(s_workspaceRoot, "Scripts", "second.test");
		string thirdFilePath = Path.Combine(s_workspaceRoot, "Scripts", "third.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		provider.OpenDocument(secondFilePath, "line two");
		provider.OpenDocument(thirdFilePath, "line three");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 3, s_waitTimeout).ConfigureAwait(false));

		client.RaiseTransportUnavailable(client.TransportGeneration);

		int reopenAttempts = 0;

		client.SendNotificationHandler = (method, _, _) =>
		{
			if (string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal)
				&& Interlocked.Increment(ref reopenAttempts) == 2)
			{
				throw new IOException("Simulated reopen failure.");
			}

			return Task.CompletedTask;
		};

		// The restart replay reopens the first document and fails on the second one: the failed document and
		// everything after it stay pending, and the provider does not report ready.
		Assert.IsNull(await provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));
		Assert.IsFalse(provider.IsAvailable);

		// The next start resumes the replay with the remaining documents only and reports ready.
		Assert.IsNull(await provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 6, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
	}

	[TestMethod]
	public async Task FailedRestartReplay_WhenTheSessionIsReplaced_ReopensEveryTrackedDocument()
	{
		string secondFilePath = Path.Combine(s_workspaceRoot, "Scripts", "second.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		provider.OpenDocument(secondFilePath, "line two");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));

		client.RaiseTransportUnavailable(client.TransportGeneration);

		int reopenAttempts = 0;

		client.SendNotificationHandler = (method, _, _) =>
		{
			if (string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal)
				&& Interlocked.Increment(ref reopenAttempts) == 2)
			{
				throw new IOException("Simulated reopen failure.");
			}

			return Task.CompletedTask;
		};

		// The restart replay reopens one document and fails on the second one, leaving a partial pending list
		// while the transport stays ready.
		Assert.IsNull(await provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);

		// A start handler always runs and activates a fresh generation, so the resume below replaces the session
		// the partial list was captured for. The framework must capture the complete set again: both tracked
		// documents need a didOpen on the new session.
		client.StartAsyncHandler = static _ => Task.FromResult(true);

		var reopenedUris = new List<string>();

		client.SendNotificationHandler = (method, parameters, _) =>
		{
			if (string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal))
			{
				reopenedUris.Add(JsonSerializer.SerializeToElement(parameters)
					.GetProperty("textDocument").GetProperty("uri").GetString()!);
			}

			return Task.CompletedTask;
		};

		Assert.IsNull(await provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));

		Assert.IsTrue(await TestPolling.ForConditionAsync(() => reopenedUris.Count >= 2, s_waitTimeout).ConfigureAwait(false),
			$"Expected both tracked documents to be reopened on the replacement session. Reopened: {string.Join(", ", reopenedUris)}");

		CollectionAssert.AreEquivalent(
			new[] { new Uri(s_filePath).AbsoluteUri, new Uri(secondFilePath).AbsoluteUri },
			reopenedUris);

		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
	}

	[TestMethod]
	public async Task DisposeAsync_DisposesTheClientOnce()
	{
		var client = new FakeLanguageServerClient();
		var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		await provider.DisposeAsync().ConfigureAwait(false);
		await provider.DisposeAsync().ConfigureAwait(false);
		provider.Dispose();

		Assert.AreEqual(1, client.DisposeCallCount);
		Assert.IsTrue(client.IsDisposed);
		Assert.AreEqual(LanguageServerProviderState.Disposed, provider.State);
		Assert.IsFalse(provider.IsAvailable);
	}

	[TestMethod]
	public async Task UnexpectedStartupException_CountsTowardTheHardFailureThreshold()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client,
			new LanguageServerProviderOptions { HardStartupFailureThreshold = 2 });
		var failures = new List<LanguageServerStartupFailure>();

		provider.StartupFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

		client.StartAsyncHandler = static _ => throw new IOException("Simulated startup fault.");

		// An unexpected startup exception counts like a false start result: the first fault reports a transient
		// failure and leaves the provider retrying.
		Assert.IsNull(await provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));

		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);
		Assert.AreEqual(1, failures.Count);
		Assert.IsFalse(failures[0].IsPersistent);

		// The second fault reaches the threshold and disables IntelliSense until the provider is recreated.
		Assert.IsNull(await provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));

		Assert.AreEqual(LanguageServerProviderState.Failed, provider.State);
		Assert.AreEqual(2, failures.Count);
		Assert.IsTrue(failures[1].IsPersistent);

		// The failed state stops further startup attempts.
		Assert.IsNull(await provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));
		Assert.AreEqual(2, client.StartCallCount);
	}
}
