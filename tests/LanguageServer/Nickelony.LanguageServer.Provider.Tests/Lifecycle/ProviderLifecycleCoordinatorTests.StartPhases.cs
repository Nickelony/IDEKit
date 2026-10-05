namespace Nickelony.LanguageServer.Provider.Tests;

public sealed partial class ProviderLifecycleCoordinatorTests
{
	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenTheFailureLatchIsSet_RefusesTheCallerBeforeItQueuesOnTheStartupLock()
	{
		using var fixture = new TestHost(hardStartupFailureThreshold: 1);

		fixture.RequiredClient.StartResult = false;

		Assert.IsFalse(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerFeatureProviderState.Failed, fixture.StartupState.State);

		int lockWaits = 0;

		fixture.TestHooks.StartupLockWaitObserver = () => Interlocked.Increment(ref lockWaits);

		Assert.IsFalse(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		Assert.AreEqual(0, lockWaits, "A latched provider must be refused before it queues on the startup lock.");
		Assert.AreEqual(LanguageServerFeatureProviderState.Failed, fixture.StartupState.State);
		Assert.AreEqual(1, fixture.RequiredClient.StartCallCount);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenTheStartItQueuedBehindReachedTheFailureLatch_DoesNotStartAgain()
	{
		using var fixture = new TestHost(hardStartupFailureThreshold: 1);

		var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondCallerWaiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		int lockWaits = 0;

		fixture.RequiredClient.EnsureStartedAsyncHandler = _ => startGate.Task;

		fixture.TestHooks.StartupLockWaitObserver = () =>
		{
			if (Interlocked.Increment(ref lockWaits) >= 2)
				secondCallerWaiting.TrySetResult();
		};

		Task<bool> first = fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None);
		Task<bool> second = fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None);

		await secondCallerWaiting.Task.ConfigureAwait(false);

		// The first start fails and reaches the latch while the second caller is queued on the startup lock.
		startGate.SetResult(false);

		Assert.IsFalse(await first.ConfigureAwait(false));
		Assert.IsFalse(await second.ConfigureAwait(false));

		Assert.AreEqual(1, fixture.RequiredClient.StartCallCount);
		Assert.AreEqual(1, fixture.StartupState.ConsecutiveStartupFailures);
		Assert.AreEqual(LanguageServerFeatureProviderState.Failed, fixture.StartupState.State);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenTheLatchIsReachedWhileTheCallerQueues_ReportsTheFailedState()
	{
		using var fixture = new TestHost(hardStartupFailureThreshold: 1);

		// Another caller's start fails and reaches the latch after this caller decided to start.
		fixture.TestHooks.StartupLockWaitObserver = () => fixture.StartupState.RegisterStartupFailure();

		Assert.IsFalse(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		Assert.AreEqual(LanguageServerFeatureProviderState.Failed, fixture.StartupState.State);
		Assert.AreEqual(0, fixture.RequiredClient.StartCallCount);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenTheProviderInvalidatedAReadyConnection_StartsAgainUnderTheStartupLock()
	{
		using var fixture = new TestHost();

		await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false);

		int lockWaits = 0;

		fixture.TestHooks.StartupLockWaitObserver = () => Interlocked.Increment(ref lockWaits);

		// A failure the client never reported clears the provider's startup flag while the client keeps its session.
		fixture.Coordinator.MarkConnectionUnavailable();

		Assert.IsTrue(fixture.RequiredClient.IsReady);
		Assert.IsTrue(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		Assert.AreEqual(1, lockWaits, "An invalidated connection must not be reused by the fast path.");
		Assert.AreEqual(2, fixture.RequiredClient.StartCallCount);
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, fixture.StartupState.State);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenTheClientIsReadyButTheProviderInvalidatedIt_StaysCancelableByTheCaller()
	{
		using var fixture = new TestHost();
		using var callerSource = new CancellationTokenSource();

		await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false);

		fixture.Coordinator.MarkConnectionUnavailable();
		callerSource.Cancel();

		await Assert
			.ThrowsAsync<OperationCanceledException>(() => fixture.Coordinator.EnsureConnectionStartedAsync(callerSource.Token))
			.ConfigureAwait(false);

		Assert.AreEqual(1, fixture.RequiredClient.StartCallCount);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenTheCallerCancelsDuringARestart_CompletesTheRestart()
	{
		using var fixture = new TestHost();
		using var callerSource = new CancellationTokenSource();

		fixture.Track("a.lua");

		await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false);

		fixture.ReopenCalls.Clear();
		fixture.RequiredClient.IsReady = false;

		// The caller gives up as it reaches the startup lock.
		fixture.TestHooks.StartupLockWaitObserver = callerSource.Cancel;

		Assert.IsTrue(await fixture.Coordinator.EnsureConnectionStartedAsync(callerSource.Token).ConfigureAwait(false));

		Assert.HasCount(1, fixture.ReopenCalls);
		Assert.IsNull(fixture.Coordinator.PendingReopenDocuments);
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, fixture.StartupState.State);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenDisposalStartsDuringARestart_ReturnsFalseAndKeepsTheReplayPending()
	{
		using var fixture = new TestHost();
		using var callerSource = new CancellationTokenSource();

		fixture.Track("a.lua");

		await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false);

		var startEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		fixture.RequiredClient.IsReady = false;

		fixture.RequiredClient.EnsureStartedAsyncHandler = async token =>
		{
			startEntered.SetResult();

			await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);

			return true;
		};

		// The caller's token never fires, so only the disposal link can end the shielded restart.
		Task<bool> restart = fixture.Coordinator.EnsureConnectionStartedAsync(callerSource.Token);

		await startEntered.Task.ConfigureAwait(false);

		fixture.Disposal.Signal();

		Assert.IsFalse(await restart.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));

		Assert.IsNotNull(fixture.Coordinator.PendingReopenDocuments, "The interrupted restart still owes its replay.");
		Assert.AreEqual(0, fixture.StartupState.ConsecutiveStartupFailures);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenAStartCompletesWhileTheCallerQueues_JoinsItWithoutStartingAgain()
	{
		using var fixture = new TestHost();

		bool otherStartCompleted = false;

		// Another caller's start completes after this caller decided to start.
		fixture.TestHooks.StartupLockWaitObserver = () =>
		{
			fixture.RequiredClient.ConnectionGeneration = 1;
			fixture.RequiredClient.IsReady = true;

			otherStartCompleted = fixture.StartupState.TryCompleteSuccessfulStart(1, out _);

			fixture.Timeouts.Reset(1);
			_ = fixture.Timeouts.RecordTimeout(1);
		};

		Assert.IsTrue(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		Assert.IsTrue(otherStartCompleted);
		Assert.AreEqual(0, fixture.RequiredClient.StartCallCount);
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, fixture.StartupState.State);
		Assert.AreEqual(0, fixture.CapabilitiesChangedCount, "A joined start announces nothing of its own.");
		Assert.AreEqual(1, fixture.RestoreCount);
		Assert.AreEqual(1, fixture.Timeouts.ConsecutiveTimeouts, "A joined start must not reset the timeout streak.");
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenTheClientIsReadyButTheProviderInvalidatedIt_ReopensNoDocuments()
	{
		using var fixture = new TestHost();

		fixture.Track("a.lua");

		await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false);

		fixture.ReopenCalls.Clear();
		fixture.Coordinator.MarkConnectionUnavailable();

		Assert.IsTrue(fixture.RequiredClient.IsReady);
		Assert.IsTrue(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		Assert.IsEmpty(fixture.ReopenCalls, "Only a start that finds the client down takes a restart snapshot.");
		Assert.IsNull(fixture.Coordinator.PendingReopenDocuments);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenAStartFaultsAfterTheClientBecameReady_ResumesTheReopenOnTheNextStart()
	{
		using var fixture = new TestHost();

		DocumentSnapshot document = fixture.Track("a.lua");

		// The session is replaced, then the start faults before the replay runs.
		fixture.RequiredClient.EnsureStartedAsyncHandler = _ =>
		{
			fixture.RequiredClient.ConnectionGeneration++;
			fixture.RequiredClient.IsReady = true;

			throw new InvalidOperationException("start interrupted");
		};

		await Assert
			.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None))
			.ConfigureAwait(false);

		Assert.IsNotNull(fixture.Coordinator.PendingReopenDocuments);

		fixture.RequiredClient.EnsureStartedAsyncHandler = null;

		Assert.IsTrue(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		Assert.HasCount(1, fixture.ReopenCalls);
		Assert.AreEqual(document.FilePath, fixture.ReopenCalls[0].Single().FilePath);
		Assert.IsNull(fixture.Coordinator.PendingReopenDocuments);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenTheSessionIsReplacedWhileResuming_ReplaysEveryTrackedDocument()
	{
		using var fixture = new TestHost();

		DocumentSnapshot first = fixture.Track("a.lua");
		DocumentSnapshot second = fixture.Track("b.lua");

		// The first replay delivers one document and stops, so the next start owes the other.
		fixture.ReopenResult = static documents => [.. documents.Skip(1)];

		Assert.IsFalse(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));
		Assert.HasCount(1, fixture.Coordinator.PendingReopenDocuments ?? []);

		// The next start finds the client ready, and then replaces its session.
		fixture.ReopenResult = static _ => null;
		fixture.RequiredClient.EnsureStartedAsyncHandler = static _ => Task.FromResult(true);

		Assert.IsTrue(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		Assert.HasCount(2, fixture.ReopenCalls);
		Assert.AreSequenceEqual([first.FilePath, second.FilePath], fixture.ReopenCalls[1].Select(static document => document.FilePath).Order());
		Assert.IsNull(fixture.Coordinator.PendingReopenDocuments);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenTheReplayFaultsAfterTheListWasCapturedAgain_KeepsTheCompleteListPending()
	{
		using var fixture = new TestHost();

		fixture.Track("a.lua");
		fixture.Track("b.lua");

		fixture.ReopenResult = static documents => [.. documents.Skip(1)];

		Assert.IsFalse(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		// The next start replaces the session, captures the complete list again, and faults in the replay.
		fixture.RequiredClient.EnsureStartedAsyncHandler = static _ => Task.FromResult(true);
		fixture.ReopenResult = static _ => throw new InvalidOperationException("replay fault");

		await Assert
			.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None))
			.ConfigureAwait(false);

		Assert.HasCount(2, fixture.Coordinator.PendingReopenDocuments ?? []);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenTheSessionIsUnchangedWhileResuming_ReplaysOnlyThePendingRemainder()
	{
		using var fixture = new TestHost();

		fixture.Track("a.lua");
		fixture.Track("b.lua");

		fixture.ReopenResult = static documents => [.. documents.Skip(1)];

		Assert.IsFalse(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		DocumentSnapshot remainder = (fixture.Coordinator.PendingReopenDocuments ?? []).Single();

		fixture.ReopenResult = static _ => null;

		Assert.IsTrue(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		Assert.HasCount(2, fixture.ReopenCalls);
		Assert.AreEqual(remainder.FilePath, fixture.ReopenCalls[1].Single().FilePath);
		Assert.IsNull(fixture.Coordinator.PendingReopenDocuments);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenAReplayReturnsAnEmptyRemainder_ClearsThePendingListOnTheNextStart()
	{
		using var fixture = new TestHost();

		fixture.Track("a.lua");

		// An empty remainder breaks the replay contract, and the empty list must not stay pending afterwards.
		fixture.ReopenResult = static _ => [];

		Assert.IsFalse(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));
		Assert.IsTrue(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		Assert.HasCount(1, fixture.ReopenCalls);
		Assert.IsNull(fixture.Coordinator.PendingReopenDocuments);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_AfterAConnectionDrop_DoesNotCaptureTheCompleteListAgain()
	{
		using var fixture = new TestHost();

		fixture.Track("a.lua");

		await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false);

		fixture.RequiredClient.IsReady = false;

		Assert.IsTrue(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		Assert.DoesNotContain(
			static entry => entry.Message.Contains("was replaced while resuming", StringComparison.Ordinal),
			fixture.Logger.Entries
		);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenTheSessionIsReplacedDuringTheReplay_FailsTheStart()
	{
		using var fixture = new TestHost();

		fixture.Track("a.lua");

		// A concurrent invalidation replaces the session after the start read its generation.
		fixture.ReopenResult = _ =>
		{
			fixture.RequiredClient.ConnectionGeneration++;
			return null;
		};

		Assert.IsFalse(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		Assert.IsFalse(fixture.StartupState.StartupSucceeded);
		Assert.AreEqual(1, fixture.StartupState.ConsecutiveStartupFailures);
		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, fixture.StartupState.State);
		Assert.AreEqual(0, fixture.RestoreCount);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenTheStartThrows_ReleasesTheStartupLock()
	{
		using var fixture = new TestHost();

		fixture.RequiredClient.EnsureStartedAsyncHandler = static _ => throw new InvalidOperationException("startup failure");

		await Assert
			.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None))
			.ConfigureAwait(false);

		bool commitRan = false;

		Assert.IsTrue(fixture.Coordinator.TryCommitWhileUnavailable(() => commitRan = true), "The startup lock must be released.");
		Assert.IsTrue(commitRan);
	}
}
