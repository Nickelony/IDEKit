using System.Text.Json;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the synchronizer's document mirroring and its signals directly, without a provider.</summary>
[TestClass]
public sealed class DocumentSynchronizerTests
{
	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(5);

	private static Task<bool> WaitForSignalAsync(SynchronizerTestHost fixture, string signal) =>
		TestPolling.ForConditionAsync(() => fixture.Signals.Contains(signal), s_waitTimeout);

	private static int CountSent(FakeLanguageServerClient client, string method) => client.GetSentMethodNames().Count(name => name == method);

	[TestMethod]
	public async Task Open_SendsTheOpenAndSignalsTheSynchronizedDocument()
	{
		using var fixture = new SynchronizerTestHost();
		string filePath = TestPaths.Script("a.lua");

		fixture.Synchronizer.Open(filePath, "local a = 1");

		Assert.IsTrue(await fixture.Client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));
		Assert.IsTrue(await WaitForSignalAsync(fixture, "synchronized:a.lua:v1"));

		Assert.AreSequenceEqual(["synchronized:a.lua:v1"], fixture.Signals);
		Assert.IsTrue(fixture.Documents.IsTrackedNormalized(filePath));
	}

	[TestMethod]
	public async Task Update_SendsTheChangeAndSignalsTheNewVersion()
	{
		using var fixture = new SynchronizerTestHost();
		string filePath = TestPaths.Script("a.lua");

		fixture.Synchronizer.Open(filePath, "local a = 1");
		Assert.IsTrue(await fixture.Client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		fixture.Synchronizer.Update(filePath, "local a = 2");

		Assert.IsTrue(await fixture.Client.WaitForMethodCountAsync("textDocument/didChange", 1, s_waitTimeout));
		Assert.IsTrue(await WaitForSignalAsync(fixture, "synchronized:a.lua:v2"));
		Assert.AreSequenceEqual(["synchronized:a.lua:v1", "synchronized:a.lua:v2"], fixture.Signals);
	}

	[TestMethod]
	public async Task Update_WhenTheChangeSendFails_InvalidatesTheDocument()
	{
		using var fixture = new SynchronizerTestHost();
		string filePath = TestPaths.Script("a.lua");

		fixture.Synchronizer.Open(filePath, "local a = 1");
		Assert.IsTrue(await WaitForSignalAsync(fixture, "synchronized:a.lua:v1"));

		fixture.Client.SendNotificationHandler = static (method, _, _) =>
			string.Equals(method, "textDocument/didChange", StringComparison.Ordinal)
				? Task.FromException(new IOException("The connection closed."))
				: Task.CompletedTask;

		fixture.Synchronizer.Update(filePath, "local a = 2");

		// The failed send drops the tracked server copy, so a result in flight for the old copy must not be published.
		Assert.IsTrue(await WaitForSignalAsync(fixture, "invalidated:a.lua"));
		Assert.DoesNotContain("synchronized:a.lua:v2", fixture.Signals);
	}

	[TestMethod]
	public async Task Close_SendsTheCloseAndSignalsTheDocumentInvalidated()
	{
		using var fixture = new SynchronizerTestHost();
		string filePath = TestPaths.Script("a.lua");

		fixture.Synchronizer.Open(filePath, "local a = 1");
		Assert.IsTrue(await fixture.Client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		fixture.Synchronizer.Close(filePath);

		Assert.IsTrue(await fixture.Client.WaitForMethodCountAsync("textDocument/didClose", 1, s_waitTimeout));
		Assert.IsTrue(await WaitForSignalAsync(fixture, "invalidated:a.lua"));
		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(filePath));
	}

	[TestMethod]
	public async Task Rename_InvalidatesBothPathsThenSignalsTheRename()
	{
		using var fixture = new SynchronizerTestHost();

		string oldPath = TestPaths.Script("old.lua");
		string newPath = TestPaths.Script("new.lua");

		fixture.Synchronizer.Open(oldPath, "local a = 1");
		Assert.IsTrue(await fixture.Client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		fixture.Synchronizer.Rename(oldPath, newPath, "local a = 1");

		Assert.IsTrue(await WaitForSignalAsync(fixture, "moved:new.lua"));

		string[] afterOpen = [.. fixture.Signals.SkipWhile(signal => signal != "invalidated:old.lua")];

		Assert.AreSequenceEqual(["invalidated:old.lua", "invalidated:new.lua", "moved:new.lua"], afterOpen);
		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(oldPath));
		Assert.IsTrue(fixture.Documents.IsTrackedNormalized(newPath));
	}

	[TestMethod]
	public async Task Open_WhileTheConnectionCannotStart_RecordsTheDocumentLocallyAndSignalsNothing()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, StartResult = false };
		using var fixture = new SynchronizerTestHost(client);

		string filePath = TestPaths.Script("a.lua");

		fixture.Synchronizer.Open(filePath, "local a = 1");

		// The failed-start open commits the record locally; that is the barrier that proves the open ran.
		await TestPolling.UntilAsync(
			() => fixture.Documents.IsTrackedNormalized(filePath),
			s_waitTimeout,
			"The failed-startup open was never recorded."
		);

		Assert.IsEmpty(client.GetSentMethodNames());
		Assert.IsEmpty(fixture.Signals);
	}

	[TestMethod]
	public async Task Open_RecordedLocallyBeforeARecoveringStart_IsReplayedByThatStart()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, StartResult = false };
		using var fixture = new SynchronizerTestHost(client);

		string filePath = TestPaths.Script("a.lua");

		fixture.Synchronizer.Open(filePath, "local a = 1");

		await TestPolling.UntilAsync(
			() => fixture.Documents.IsTrackedNormalized(filePath),
			s_waitTimeout,
			"The failed-startup open was never recorded."
		);

		client.StartResult = true;

		Assert.IsTrue(await fixture.Lifecycle.EnsureConnectionStartedAsync(CancellationToken.None));

		// The start took its replay snapshot after the commit, so the replay sends the open once.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));
		Assert.AreEqual(1, CountSent(client, "textDocument/didOpen"));
	}

	[TestMethod]
	public async Task Open_WhileARecoveringStartHoldsTheStartupLock_OpensTheDocumentOnceTheStartSettles()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, StartResult = false };
		using var fixture = new SynchronizerTestHost(client);

		string filePath = TestPaths.Script("a.lua");

		var startHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseStart = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var retryAtTheLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<bool>? recovery = null;
		int decisions = 0;

		fixture.TestHooks.LocalCommitObserver = () =>
		{
			if (Interlocked.Increment(ref decisions) != 1)
				return;

			// A recovery start takes the startup lock and parks inside the connection start, so the decision that follows finds the lock held.
			client.EnsureStartedAsyncHandler = _ =>
			{
				startHeld.TrySetResult();
				return releaseStart.Task;
			};

			recovery = fixture.Lifecycle.EnsureConnectionStartedAsync(CancellationToken.None);
			startHeld.Task.Wait(s_waitTimeout);

			// The next caller to reach the lock is the skipped open's retry.
			fixture.TestHooks.StartupLockWaitObserver = () => retryAtTheLock.TrySetResult();
		};

		try
		{
			fixture.Synchronizer.Open(filePath, "local a = 1");

			await retryAtTheLock.Task.WaitAsync(s_waitTimeout);

			// The commit was skipped instead of waiting for the lock: no record exists and nothing was sent.
			Assert.IsFalse(fixture.Documents.IsTrackedNormalized(filePath));
			Assert.IsEmpty(client.GetSentMethodNames());
		}
		finally
		{
			releaseStart.TrySetResult(true);
		}

		Assert.IsNotNull(recovery);
		Assert.IsTrue(await recovery);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));
		Assert.AreEqual(1, CountSent(client, "textDocument/didOpen"));

		// The retry joined the recovery start instead of starting another attempt.
		Assert.AreEqual(2, client.StartCallCount);
	}

	[TestMethod]
	public async Task Open_WhenTheStartHoldingTheStartupLockFails_RecordsTheOpenForTheNextReplay()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, StartResult = false };
		using var fixture = new SynchronizerTestHost(client, new LanguageServerFeatureProviderOptions { HardStartupFailureThreshold = 10 });

		string filePath = TestPaths.Script("a.lua");

		var startHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseStart = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var retryAtTheLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<bool>? recovery = null;
		int decisions = 0;

		fixture.TestHooks.LocalCommitObserver = () =>
		{
			if (Interlocked.Increment(ref decisions) != 1)
				return;

			client.EnsureStartedAsyncHandler = _ =>
			{
				startHeld.TrySetResult();
				return releaseStart.Task;
			};

			recovery = fixture.Lifecycle.EnsureConnectionStartedAsync(CancellationToken.None);
			startHeld.Task.Wait(s_waitTimeout);

			fixture.TestHooks.StartupLockWaitObserver = () => retryAtTheLock.TrySetResult();
		};

		try
		{
			fixture.Synchronizer.Open(filePath, "local a = 1");
			await retryAtTheLock.Task.WaitAsync(s_waitTimeout);
		}
		finally
		{
			// The start that held the lock fails, and the retry's own start fails after it.
			releaseStart.TrySetResult(false);
		}

		Assert.IsNotNull(recovery);
		Assert.IsFalse(await recovery);

		// The skipped open still records itself once the failed starts settle, so the next start replays it.
		await TestPolling.UntilAsync(
			() => fixture.Documents.IsTrackedNormalized(filePath),
			s_waitTimeout,
			"The skipped open was dropped instead of recorded."
		);

		client.EnsureStartedAsyncHandler = null;
		client.StartResult = true;

		Assert.IsTrue(await fixture.Lifecycle.EnsureConnectionStartedAsync(CancellationToken.None));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));
		Assert.AreEqual(1, CountSent(client, "textDocument/didOpen"));
	}

	[TestMethod]
	public async Task Open_WhenARecoveringStartFinishesBeforeTheLocalCommitDecision_OpensTheDocumentOnTheServer()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, StartResult = false };
		using var fixture = new SynchronizerTestHost(client);

		string filePath = TestPaths.Script("a.lua");

		var decisionReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var decisionReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		int decisions = 0;

		fixture.TestHooks.LocalCommitObserver = () =>
		{
			if (Interlocked.Increment(ref decisions) != 1)
				return;

			decisionReached.TrySetResult();
			decisionReleased.Task.Wait(s_waitTimeout);
		};

		try
		{
			fixture.Synchronizer.Open(filePath, "local a = 1");

			// The open's own start failed, so its slot is parked at the decision with the connection still down.
			await decisionReached.Task.WaitAsync(s_waitTimeout);

			client.StartResult = true;

			// The recovery start runs to completion while the decision is parked, so its replay snapshot holds no record of the document.
			Assert.IsTrue(await fixture.Lifecycle.EnsureConnectionStartedAsync(CancellationToken.None));
			Assert.IsEmpty(client.GetSentMethodNames());
		}
		finally
		{
			decisionReleased.TrySetResult();
		}

		// The decision now sees a ready connection, so the document is mirrored instead of recorded locally and never sent.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));
		Assert.AreEqual(1, CountSent(client, "textDocument/didOpen"));
	}

	[TestMethod]
	public async Task Update_WhenARecoveringStartFinishesBeforeTheLocalCommitDecision_SendsTheChange()
	{
		using var fixture = new SynchronizerTestHost();

		FakeLanguageServerClient client = fixture.Client;
		string filePath = TestPaths.Script("a.lua");

		fixture.Synchronizer.Open(filePath, "local a = 1");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));
		Assert.IsTrue(await WaitForSignalAsync(fixture, "synchronized:a.lua:v1"));

		// The server session stays up, but the provider loses its startup flag and the next start fails, so the
		// update's slot reaches the decision with the connection down and no replay pending.
		fixture.Lifecycle.MarkConnectionUnavailable();
		client.EnsureStartedAsyncHandler = static _ => Task.FromResult(false);

		var decisionReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var decisionReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		int decisions = 0;

		fixture.TestHooks.LocalCommitObserver = () =>
		{
			if (Interlocked.Increment(ref decisions) != 1)
				return;

			decisionReached.TrySetResult();
			decisionReleased.Task.Wait(s_waitTimeout);
		};

		try
		{
			fixture.Synchronizer.Update(filePath, "local a = 2");

			await decisionReached.Task.WaitAsync(s_waitTimeout);

			// The recovery keeps the session it has: a ready client returns from its fast path, and no handler replaces the session.
			client.EnsureStartedAsyncHandler = null;

			Assert.IsTrue(await fixture.Lifecycle.EnsureConnectionStartedAsync(CancellationToken.None));
		}
		finally
		{
			decisionReleased.TrySetResult();
		}

		// The decision now sees a ready connection, so the change is mirrored instead of committed locally and never sent.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, s_waitTimeout));
		Assert.AreEqual(1, CountSent(client, "textDocument/didChange"));
		Assert.AreEqual(1, CountSent(client, "textDocument/didOpen"));
	}

	[TestMethod]
	public async Task Open_WhenTheStartupFlagIsClearedWhileTheClientStaysReady_OpensTheDocumentOnTheServer()
	{
		using var client = new FakeLanguageServerClient { IsReady = true, ConnectionGeneration = 1 };
		using var fixture = new SynchronizerTestHost(client);

		string filePath = TestPaths.Script("a.lua");

		var holdSlot = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		// The open's own start succeeds first, then the open waits in the document's slot behind this operation.
		Task<bool> blocker = fixture.Scheduler.EnqueuePerDocumentAsync(filePath, _ => holdSlot.Task, CancellationToken.None);

		try
		{
			fixture.Synchronizer.Open(filePath, "local a = 1");

			await TestPolling.UntilAsync(() => fixture.Lifecycle.IsConnectionReady, s_waitTimeout, "The open never started the connection.");

			// A failure the client never reported clears the startup flag while the open waits, and the client keeps its session.
			fixture.Lifecycle.MarkConnectionUnavailable();
		}
		finally
		{
			holdSlot.TrySetResult(true);
		}

		await blocker;

		// The slot skipped instead of recording the open locally, and the retry after its start opens the document.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));
		Assert.IsTrue(await WaitForSignalAsync(fixture, "synchronized:a.lua:v1"));
		Assert.AreEqual(1, CountSent(client, "textDocument/didOpen"));
	}

	[TestMethod]
	public async Task SynchronizeForRequestAsync_WhenTheStartupFlagIsClearedWhileTheClientStaysReady_LeavesTheTrackedCopyAsTheServerHasIt()
	{
		using var fixture = new SynchronizerTestHost();

		FakeLanguageServerClient client = fixture.Client;
		string filePath = TestPaths.Script("a.lua");

		fixture.Synchronizer.Open(filePath, "local a = 1");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));
		Assert.IsTrue(await WaitForSignalAsync(fixture, "synchronized:a.lua:v1"));

		// A failure the client never reported clears the startup flag, and the client keeps its session.
		fixture.Lifecycle.MarkConnectionUnavailable();

		var requestLease = new DocumentRequestLease();

		bool synchronized = await fixture.Synchronizer.SynchronizeForRequestAsync(
			filePath,
			content: "local a = 1\nlocal b = 2",
			requestLease,
			cancellationToken: CancellationToken.None
		);

		Assert.IsFalse(synchronized);

		// The skipped request committed nothing, so the tracked copy is still the one the server holds.
		Assert.AreEqual("local a = 1", fixture.Documents.GetDocumentSnapshot(filePath)?.Content);

		Assert.IsTrue(await fixture.Lifecycle.EnsureConnectionStartedAsync(CancellationToken.None));

		fixture.Synchronizer.Update(filePath, "local a = 1\nlocal b = 2\nlocal c = 3");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, s_waitTimeout));

		// The change is computed against the content the server holds, so it carries both appended lines.
		JsonElement change = client.GetLastNotificationParameters("textDocument/didChange").GetProperty("contentChanges")[0];

		Assert.AreEqual("\nlocal b = 2\nlocal c = 3", change.GetProperty("text").GetString());
	}

	[TestMethod]
	public async Task SynchronizeForRequestAsync_ForAnUntrackedDocumentWhileTheClientIsDown_TracksNothingAndSendsNothing()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, StartResult = false };
		using var fixture = new SynchronizerTestHost(client);

		string filePath = TestPaths.Script("a.lua");
		var requestLease = new DocumentRequestLease();

		Assert.IsFalse(
			await fixture.Synchronizer.SynchronizeForRequestAsync(
				filePath,
				content: "local a = 1",
				requestLease,
				cancellationToken: CancellationToken.None
			)
		);

		Assert.IsFalse(fixture.Documents.IsTrackedNormalized(filePath));
		Assert.IsEmpty(client.GetSentMethodNames());
	}
}
