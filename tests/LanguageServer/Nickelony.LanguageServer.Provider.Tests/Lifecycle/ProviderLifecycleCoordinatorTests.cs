using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the lifecycle coordinator's startup, restart replay and failed-start accounting directly.</summary>
[TestClass]
public sealed partial class ProviderLifecycleCoordinatorTests
{
	private sealed class TestHost : IDisposable, IDocumentRestartReplay, IWorkspaceWatchRestoration
	{
		private readonly object _syncRoot = new();

		private readonly List<long> _trackedGenerationsAtRestore = [];
		private readonly List<StartupFailure> _startupFailures = [];

		internal TestHost(
			FakeLanguageServerClient? client = null,
			int hardStartupFailureThreshold = 3,
			bool hasClient = true,
			string? startupFailureMessage = "start failed",
			string? missingClientFailureMessage = "no client"
		)
		{
			Client = hasClient ? client ?? new FakeLanguageServerClient { IsReady = false } : null;

			Events = new ProviderEvents(new object(), Disposal, Logger);

			StartupState = new StartupState(Client, Disposal);

			Events.CapabilitiesChanged += (_, _) => Interlocked.Increment(ref _capabilitiesChangedCount);

			Events.StartupFailed += (_, eventArguments) =>
			{
				lock (_syncRoot)
					_startupFailures.Add(eventArguments.Failure);
			};

			var options = new LanguageServerFeatureProviderOptions { HardStartupFailureThreshold = hardStartupFailureThreshold };

			var failureReporter = new StartupFailureReporter(
				new StartupFailureReporterContext(
					StartupState,
					Events,
					WorkspaceRootsDisplayText: "workspace",
					ProviderDisplayName: "Test",
					TransientStartupFailureMessage: DescribePersistence(startupFailureMessage, isPersistent: false),
					PersistentStartupFailureMessage: DescribePersistence(startupFailureMessage, isPersistent: true),
					missingClientFailureMessage,
					options,
					Logger
				)
			);

			Coordinator = new ProviderLifecycleCoordinator(
				new ProviderLifecycleContext(
					Provider: new ProviderContext(Client, Documents, Disposal, providerDisplayName: "Test", options, Logger),
					StartupState,
					Events,
					failureReporter,
					TestHooks,
					WorkspaceRootsDisplayText: "workspace",
					Timeouts
				)
			);

			Coordinator.Attach(this, this);
		}

		private static string? DescribePersistence(string? message, bool isPersistent) =>
			string.IsNullOrWhiteSpace(message) ? message : $"{message} persistent={isPersistent}";

		private int _capabilitiesChangedCount;
		private int _restoreCount;

		internal FakeLanguageServerClient? Client { get; }

		internal FakeLanguageServerClient RequiredClient => Client ?? throw new InvalidOperationException("The fixture has no client.");

		internal RecordingLogger Logger { get; } = new();

		internal ProviderLifetime Disposal { get; } = new();

		internal ProviderEvents Events { get; }

		internal StartupState StartupState { get; }

		internal DocumentTracker Documents { get; } = new();

		internal LanguageServerFeatureProviderTestHooks TestHooks { get; } = new();

		internal RequestTimeoutCounter Timeouts { get; } = new(restartThreshold: 3);

		internal ProviderLifecycleCoordinator Coordinator { get; }

		/// <summary>The documents each reopen call received, in call order.</summary>
		internal List<IReadOnlyList<DocumentSnapshot>> ReopenCalls { get; } = [];

		/// <summary>What the reopen delegate returns: <see langword="null"/> when every document was processed.</summary>
		internal Func<IReadOnlyList<DocumentSnapshot>, IReadOnlyList<DocumentSnapshot>?> ReopenResult { get; set; } = static _ => null;

		internal int CapabilitiesChangedCount => Volatile.Read(ref _capabilitiesChangedCount);

		internal int RestoreCount => Volatile.Read(ref _restoreCount);

		/// <summary>What each workspace-watching restore awaits, or <see langword="null"/> to complete at once.</summary>
		internal Func<CancellationToken, Task>? RestoreHandler { get; set; }

		/// <summary>The generation the timeout tracker applied to at each workspace-watching restore, in call order.</summary>
		internal IReadOnlyList<long> TrackedGenerationsAtRestore
		{
			get
			{
				lock (_syncRoot)
					return [.. _trackedGenerationsAtRestore];
			}
		}

		internal IReadOnlyList<StartupFailure> StartupFailures
		{
			get
			{
				lock (_syncRoot)
					return [.. _startupFailures];
			}
		}

		internal DocumentSnapshot Track(string fileName)
		{
			string filePath = TestPaths.Script(fileName);

			Documents.Synchronize(filePath, "content", leases: DocumentLeaseKinds.Open);

			return Documents.GetDocumentSnapshot(filePath) ?? throw new InvalidOperationException("The document was not tracked.");
		}

		public void Dispose() => Logger.Dispose();

		public Task<IReadOnlyList<DocumentSnapshot>?> ReopenTrackedDocumentsAsync(
			IReadOnlyList<DocumentSnapshot> documents,
			CancellationToken cancellationToken
		)
		{
			ReopenCalls.Add(documents);
			return Task.FromResult(ReopenResult(documents));
		}

		public Task RestoreWorkspaceWatchingAsync(CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _restoreCount);

			lock (_syncRoot)
				_trackedGenerationsAtRestore.Add(Timeouts.TrackedGeneration);

			return RestoreHandler?.Invoke(cancellationToken) ?? Task.CompletedTask;
		}
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WithoutAClient_ReturnsFalseAndRestoresNothing()
	{
		using var fixture = new TestHost(hasClient: false);

		Assert.IsFalse(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));
		Assert.AreEqual(0, fixture.RestoreCount);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_ColdStart_StartsTheClientAndReportsReady()
	{
		using var fixture = new TestHost();

		Assert.IsTrue(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		Assert.AreEqual(1, fixture.Client?.StartCallCount);
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, fixture.StartupState.State);
		Assert.IsTrue(fixture.StartupState.StartupSucceeded);
		Assert.AreEqual(1, fixture.CapabilitiesChangedCount);
		Assert.AreEqual(1, fixture.RestoreCount);
		Assert.AreEqual(fixture.Client?.ConnectionGeneration ?? 0, fixture.Timeouts.TrackedGeneration);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenTheConnectionIsHealthy_TakesTheFastPath()
	{
		using var fixture = new TestHost();

		int lockWaits = 0;

		fixture.TestHooks.StartupLockWaitObserver = () => Interlocked.Increment(ref lockWaits);

		await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false);

		// A timeout recorded after the start survives the fast path, which resets nothing.
		_ = fixture.Timeouts.RecordTimeout(fixture.Timeouts.TrackedGeneration);

		Assert.IsTrue(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		Assert.AreEqual(1, fixture.Client?.StartCallCount);
		Assert.AreEqual(1, lockWaits, "The fast path must not reach the startup lock.");
		Assert.AreEqual(1, fixture.CapabilitiesChangedCount);
		Assert.AreEqual(2, fixture.RestoreCount);
		Assert.AreEqual(1, fixture.Timeouts.ConsecutiveTimeouts);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_ColdStart_ResetsTimeoutTrackingBeforeRestoringWorkspaceWatching()
	{
		using var fixture = new TestHost();

		await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false);

		// The restore saw the tracker already applied to the started generation, so the reset ran first.
		Assert.AreSequenceEqual([fixture.Client?.ConnectionGeneration ?? 0], fixture.TrackedGenerationsAtRestore);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_ConcurrentCallers_ShareOneStart()
	{
		using var fixture = new TestHost();

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

		Assert.AreEqual(1, fixture.RequiredClient.StartCallCount);

		startGate.SetResult(true);

		Assert.IsTrue(await first.ConfigureAwait(false));
		Assert.IsTrue(await second.ConfigureAwait(false));
		Assert.AreEqual(1, fixture.RequiredClient.StartCallCount);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_AfterAConnectionDrop_ReplaysTheTrackedDocuments()
	{
		using var fixture = new TestHost();

		DocumentSnapshot document = fixture.Track("a.lua");

		await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false);

		fixture.ReopenCalls.Clear();
		fixture.RequiredClient.IsReady = false;

		Assert.IsTrue(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		Assert.HasCount(1, fixture.ReopenCalls);
		Assert.AreEqual(document.FilePath, fixture.ReopenCalls[0].Single().FilePath);
		Assert.IsNull(fixture.Coordinator.PendingReopenDocuments);
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, fixture.StartupState.State);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenTheReplayLeavesDocuments_KeepsThemPendingAndCountsAFailedStart()
	{
		using var fixture = new TestHost();

		fixture.Track("a.lua");
		fixture.ReopenResult = static documents => documents;

		Assert.IsFalse(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		Assert.HasCount(1, fixture.Coordinator.PendingReopenDocuments ?? []);
		Assert.AreEqual(1, fixture.StartupState.ConsecutiveStartupFailures);
		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, fixture.StartupState.State);
		Assert.HasCount(1, fixture.StartupFailures);
		Assert.IsFalse(fixture.StartupFailures[0].IsPersistent);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenTheClientStayedReady_ResumesAnInterruptedReplay()
	{
		using var fixture = new TestHost();

		fixture.Track("a.lua");

		bool failReplay = true;
		fixture.ReopenResult = documents => failReplay ? documents : null;

		Assert.IsFalse(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		failReplay = false;

		Assert.IsTrue(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		Assert.HasCount(2, fixture.ReopenCalls);
		Assert.IsNull(fixture.Coordinator.PendingReopenDocuments);
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, fixture.StartupState.State);
	}

	[TestMethod]
	public void TryCommitWhileUnavailable_WhenTheConnectionIsDown_RunsTheCommitUnderTheStartupLock()
	{
		using var fixture = new TestHost();

		bool nestedCommitRan = false;

		bool committed = fixture.Coordinator.TryCommitWhileUnavailable(() =>
			// The startup lock is held, so no start can take its replay snapshot until the commit returns.
			nestedCommitRan = fixture.Coordinator.TryCommitWhileUnavailable(() => { })
		);

		Assert.IsTrue(committed);
		Assert.IsFalse(nestedCommitRan);
		Assert.IsTrue(fixture.Coordinator.TryCommitWhileUnavailable(() => { }), "The commit must release the startup lock.");
	}

	[TestMethod]
	public async Task TryCommitWhileUnavailable_WhenAStartHoldsTheStartupLock_SkipsTheCommitWithoutWaiting()
	{
		using var fixture = new TestHost();

		var startEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		fixture.RequiredClient.EnsureStartedAsyncHandler = _ =>
		{
			startEntered.TrySetResult();
			return startGate.Task;
		};

		Task<bool> start = fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None);

		bool commitRan = false;
		bool committed = true;

		try
		{
			await startEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

			// A commit that waited for the lock would park a pool thread until the start returns, so the call is bounded.
			committed = await Task.Run(() => fixture.Coordinator.TryCommitWhileUnavailable(() => commitRan = true))
				.WaitAsync(TimeSpan.FromSeconds(5))
				.ConfigureAwait(false);
		}
		finally
		{
			startGate.TrySetResult(true);
		}

		Assert.IsFalse(committed);
		Assert.IsFalse(commitRan);
		Assert.IsTrue(await start.ConfigureAwait(false));
	}

	[TestMethod]
	public async Task TryCommitWhileUnavailable_WhenTheConnectionIsReady_SkipsTheCommit()
	{
		using var fixture = new TestHost();

		await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false);

		bool commitRan = false;

		Assert.IsFalse(fixture.Coordinator.TryCommitWhileUnavailable(() => commitRan = true));
		Assert.IsFalse(commitRan);
	}

	[TestMethod]
	public async Task IsConnectionReady_AfterDisposalStarted_IsFalse()
	{
		using var fixture = new TestHost();

		await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false);

		Assert.IsTrue(fixture.Coordinator.IsConnectionReady);

		fixture.Disposal.Signal();

		Assert.IsFalse(fixture.Coordinator.IsConnectionReady);
	}

	[TestMethod]
	public async Task TryCommitWhileUnavailable_WhenTheClientIsReadyButTheStartupFlagIsCleared_SkipsTheCommit()
	{
		using var fixture = new TestHost();

		await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false);

		// A failure the client never reported clears the provider's startup flag while the client keeps its session.
		fixture.Coordinator.MarkConnectionUnavailable();

		Assert.IsTrue(fixture.RequiredClient.IsReady);
		Assert.IsFalse(fixture.Coordinator.IsConnectionReady);

		bool commitRan = false;

		Assert.IsFalse(fixture.Coordinator.TryCommitWhileUnavailable(() => commitRan = true));
		Assert.IsFalse(commitRan);
	}

	[TestMethod]
	public async Task TryCommitWhileUnavailable_AfterTheClientLostItsSession_RunsTheCommit()
	{
		using var fixture = new TestHost();

		await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false);

		fixture.RequiredClient.IsReady = false;
		fixture.Coordinator.MarkConnectionUnavailable();

		bool commitRan = false;

		Assert.IsTrue(fixture.Coordinator.TryCommitWhileUnavailable(() => commitRan = true));
		Assert.IsTrue(commitRan);
	}

	[TestMethod]
	public void TryCommitWhileUnavailable_WithoutAClient_SkipsTheCommit()
	{
		using var fixture = new TestHost(hasClient: false);

		bool commitRan = false;

		Assert.IsFalse(fixture.Coordinator.TryCommitWhileUnavailable(() => commitRan = true));
		Assert.IsFalse(commitRan);
	}

	[TestMethod]
	public void TryCommitWhileUnavailable_WhileTheProviderIsStarting_SkipsTheCommit()
	{
		using var fixture = new TestHost();

		fixture.Coordinator.SetProviderState(LanguageServerFeatureProviderState.Starting);

		bool commitRan = false;

		Assert.IsFalse(fixture.Coordinator.TryCommitWhileUnavailable(() => commitRan = true));
		Assert.IsFalse(commitRan);
	}

	[TestMethod]
	public async Task TryCommitWhileUnavailable_AfterThePersistentFailureLatch_SkipsTheCommit()
	{
		using var fixture = new TestHost(hardStartupFailureThreshold: 1);

		fixture.RequiredClient.StartResult = false;

		Assert.IsFalse(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerFeatureProviderState.Failed, fixture.StartupState.State);

		bool commitRan = false;

		Assert.IsFalse(fixture.Coordinator.TryCommitWhileUnavailable(() => commitRan = true));
		Assert.IsFalse(commitRan);
	}

	[TestMethod]
	public async Task TryCommitWhileUnavailable_WhileAReplayIsPending_SkipsTheCommit()
	{
		using var fixture = new TestHost();

		fixture.Track("a.lua");
		fixture.ReopenResult = static documents => documents;

		Assert.IsFalse(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));
		Assert.IsNotNull(fixture.Coordinator.PendingReopenDocuments);

		bool commitRan = false;

		Assert.IsFalse(fixture.Coordinator.TryCommitWhileUnavailable(() => commitRan = true));
		Assert.IsFalse(commitRan);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_RepeatedFailures_ReachThePersistentFailureLatch()
	{
		using var fixture = new TestHost(hardStartupFailureThreshold: 2);

		fixture.RequiredClient.StartResult = false;

		Assert.IsFalse(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, fixture.StartupState.State);

		Assert.IsFalse(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerFeatureProviderState.Failed, fixture.StartupState.State);

		Assert.IsFalse(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		Assert.AreEqual(2, fixture.RequiredClient.StartCallCount);
		Assert.AreSequenceEqual([false, true], fixture.StartupFailures.Select(failure => failure.IsPersistent));

		Assert.AreSequenceEqual(
			["start failed persistent=False", "start failed persistent=True"],
			fixture.StartupFailures.Select(failure => failure.Message)
		);

		Assert.IsTrue(fixture.Logger.HasEntryAtLeast(LogLevel.Error));
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow("   ")]
	public async Task EnsureConnectionStartedAsync_WhenTheStartupMessageIsBlank_RaisesNoStartupFailure(string? blankMessage)
	{
		using var fixture = new TestHost(startupFailureMessage: blankMessage);

		fixture.RequiredClient.StartResult = false;

		await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false);

		Assert.IsEmpty(fixture.StartupFailures);
		Assert.AreEqual(1, fixture.StartupState.ConsecutiveStartupFailures);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_AfterASuccessfulStart_ReportsTheNextTransientFailureAgain()
	{
		using var fixture = new TestHost();

		fixture.RequiredClient.StartResult = false;

		Assert.IsFalse(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));
		Assert.IsFalse(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		// The second failure of the streak is not reported again.
		Assert.HasCount(1, fixture.StartupFailures);

		fixture.RequiredClient.StartResult = true;

		Assert.IsTrue(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		// The session drops and the restart fails: a new failure streak starts, and a successful start re-armed its report.
		fixture.RequiredClient.IsReady = false;
		fixture.RequiredClient.StartResult = false;

		Assert.IsFalse(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));

		Assert.HasCount(2, fixture.StartupFailures);
		Assert.IsTrue(fixture.StartupFailures.All(static failure => !failure.IsPersistent));
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenAnUnexpectedExceptionEscapes_CountsAFailedAttemptAndRethrows()
	{
		using var fixture = new TestHost();

		fixture.RequiredClient.EnsureStartedAsyncHandler = static _ => throw new InvalidOperationException("startup failure");

		await Assert
			.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None))
			.ConfigureAwait(false);

		Assert.AreEqual(1, fixture.StartupState.ConsecutiveStartupFailures);
		Assert.HasCount(1, fixture.StartupFailures);

		// The failure log carries the exception that escaped.
		Assert.Contains(
			static log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.EndsWith("|startup failure", StringComparison.Ordinal),
			fixture.Logger.Logs
		);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenTheCallerCancels_MarksTheProviderUnavailableAndRethrows()
	{
		using var fixture = new TestHost();
		using var callerSource = new CancellationTokenSource();

		// The caller owns the attempt and gives up while the client starts.
		fixture.RequiredClient.EnsureStartedAsyncHandler = token =>
		{
			callerSource.Cancel();
			token.ThrowIfCancellationRequested();

			return Task.FromResult(true);
		};

		await Assert
			.ThrowsAsync<OperationCanceledException>(() => fixture.Coordinator.EnsureConnectionStartedAsync(callerSource.Token))
			.ConfigureAwait(false);

		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, fixture.StartupState.State);
		Assert.AreEqual(0, fixture.StartupState.ConsecutiveStartupFailures);
		Assert.AreEqual(1, fixture.CapabilitiesChangedCount, "The move from the starting state to the unavailable state is announced.");
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenTheTokenIsCanceledBeforeTheStartupLock_RethrowsAndWritesNoState()
	{
		using var fixture = new TestHost();
		using var callerSource = new CancellationTokenSource();

		callerSource.Cancel();

		await Assert
			.ThrowsAsync<OperationCanceledException>(() => fixture.Coordinator.EnsureConnectionStartedAsync(callerSource.Token))
			.ConfigureAwait(false);

		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, fixture.StartupState.State);
		Assert.AreEqual(0, fixture.CapabilitiesChangedCount, "A caller that owns no attempt announces nothing.");
		Assert.AreEqual(0, fixture.RequiredClient.StartCallCount);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_AWaiterCanceledAfterTheStartCompleted_LeavesTheProviderReady()
	{
		using var fixture = new TestHost();
		using var waiterSource = new CancellationTokenSource();

		var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var restoreEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var restoreGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		fixture.RequiredClient.EnsureStartedAsyncHandler = _ => startGate.Task;

		fixture.RestoreHandler = _ =>
		{
			restoreEntered.TrySetResult();
			return restoreGate.Task;
		};

		try
		{
			// Both callers find the connection down, so the second queues on the startup lock behind the first.
			Task<bool> owner = fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None);
			Task<bool> waiter = fixture.Coordinator.EnsureConnectionStartedAsync(waiterSource.Token);

			startGate.SetResult(true);

			// The start completed, and its owner restores workspace watching while it still holds the startup lock.
			await restoreEntered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

			waiterSource.Cancel();

			await Assert.ThrowsAsync<OperationCanceledException>(() => waiter).ConfigureAwait(false);

			Assert.AreEqual(LanguageServerFeatureProviderState.Ready, fixture.StartupState.State);
			Assert.IsTrue(fixture.StartupState.StartupSucceeded);
			Assert.IsTrue(fixture.Coordinator.IsConnectionReady);
			Assert.AreEqual(1, fixture.CapabilitiesChangedCount, "Only the start announces its completion.");

			restoreGate.SetResult();

			Assert.IsTrue(await owner.ConfigureAwait(false));

			// A later call reuses the connection and writes no state, so the state it finds is the one the canceled waiter left.
			Assert.IsTrue(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));
			Assert.AreEqual(LanguageServerFeatureProviderState.Ready, fixture.StartupState.State);
			Assert.AreEqual(1, fixture.RequiredClient.StartCallCount);
		}
		finally
		{
			startGate.TrySetResult(true);
			restoreGate.TrySetResult();
		}
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_AWaiterCanceledWhileTheStartRuns_LeavesTheOwnersStartingState()
	{
		using var fixture = new TestHost();
		using var waiterSource = new CancellationTokenSource();

		var startGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		fixture.RequiredClient.EnsureStartedAsyncHandler = _ => startGate.Task;

		try
		{
			Task<bool> owner = fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None);
			Task<bool> waiter = fixture.Coordinator.EnsureConnectionStartedAsync(waiterSource.Token);

			waiterSource.Cancel();

			await Assert.ThrowsAsync<OperationCanceledException>(() => waiter).ConfigureAwait(false);

			Assert.AreEqual(LanguageServerFeatureProviderState.Starting, fixture.StartupState.State);
			Assert.AreEqual(0, fixture.CapabilitiesChangedCount);

			startGate.SetResult(true);

			Assert.IsTrue(await owner.ConfigureAwait(false));
			Assert.AreEqual(LanguageServerFeatureProviderState.Ready, fixture.StartupState.State);
		}
		finally
		{
			startGate.TrySetResult(true);
		}
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_AnOwnerCanceledAfterItsStartCompleted_LeavesTheProviderReady()
	{
		using var fixture = new TestHost();
		using var ownerSource = new CancellationTokenSource();

		// The owner gives up while it restores workspace watching, after its start completed on the current generation.
		fixture.RestoreHandler = token =>
		{
			ownerSource.Cancel();
			token.ThrowIfCancellationRequested();

			return Task.CompletedTask;
		};

		await Assert
			.ThrowsAsync<OperationCanceledException>(() => fixture.Coordinator.EnsureConnectionStartedAsync(ownerSource.Token))
			.ConfigureAwait(false);

		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, fixture.StartupState.State);
		Assert.IsTrue(fixture.StartupState.StartupSucceeded);
		Assert.IsTrue(fixture.Coordinator.IsConnectionReady);
		Assert.AreEqual(0, fixture.StartupState.ConsecutiveStartupFailures);

		fixture.RestoreHandler = null;

		Assert.IsTrue(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));
		Assert.AreEqual(1, fixture.RequiredClient.StartCallCount);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_AfterDisposalStarted_ReturnsFalseWithoutStarting()
	{
		using var fixture = new TestHost();

		fixture.Disposal.Signal();

		Assert.IsFalse(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));
		Assert.AreEqual(0, fixture.Client?.StartCallCount);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_AfterAStartedProviderIsDisposed_ReturnsFalseWithoutRestoringWatching()
	{
		using var fixture = new TestHost();

		await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false);

		int restoresBeforeDisposal = fixture.RestoreCount;

		fixture.Disposal.Signal();

		// A healthy client would take the fast path, which keeps the workspace watcher alive, so only the disposal check ends the call.
		Assert.IsFalse(await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false));
		Assert.AreEqual(restoresBeforeDisposal, fixture.RestoreCount);
	}

	[TestMethod]
	public async Task EnsureConnectionStartedAsync_WhenDisposalStartsDuringTheStart_ReturnsFalse()
	{
		using var fixture = new TestHost();

		var startEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		fixture.RequiredClient.EnsureStartedAsyncHandler = async token =>
		{
			startEntered.SetResult();

			await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);

			return true;
		};

		Task<bool> start = fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None);

		await startEntered.Task.ConfigureAwait(false);

		fixture.Disposal.Signal();

		Assert.IsFalse(await start.ConfigureAwait(false));
	}

	[TestMethod]
	public void SetProviderState_RaisesCapabilitiesChangedOnlyWhenRequestedAndChanged()
	{
		using var fixture = new TestHost();

		fixture.Coordinator.SetProviderState(LanguageServerFeatureProviderState.Starting);
		fixture.Coordinator.SetProviderState(LanguageServerFeatureProviderState.Starting, notifyCapabilitiesChanged: true);
		fixture.Coordinator.SetProviderState(LanguageServerFeatureProviderState.Failed, notifyCapabilitiesChanged: true);

		Assert.AreEqual(1, fixture.CapabilitiesChangedCount);
		Assert.AreEqual(LanguageServerFeatureProviderState.Failed, fixture.StartupState.State);
	}

	[TestMethod]
	public async Task MarkConnectionUnavailable_InvalidatesTheStartupAndRenamesToUnavailable()
	{
		using var fixture = new TestHost();

		await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false);

		fixture.Coordinator.MarkConnectionUnavailable();

		Assert.IsFalse(fixture.StartupState.StartupSucceeded);
		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, fixture.StartupState.State);
	}

	[TestMethod]
	public async Task MarkConnectionUnhealthy_WhenTheClientAgrees_RenamesToUnavailable()
	{
		using var fixture = new TestHost();

		await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false);

		long generation = fixture.RequiredClient.ConnectionGeneration;

		fixture.Coordinator.MarkConnectionUnhealthy(generation);

		Assert.AreSequenceEqual([generation], fixture.RequiredClient.MarkedUnhealthyGenerations);
		Assert.IsFalse(fixture.StartupState.StartupSucceeded);
		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, fixture.StartupState.State);
	}

	[TestMethod]
	public async Task MarkConnectionUnhealthy_WhenTheClientRefuses_KeepsTheReadyState()
	{
		using var fixture = new TestHost();

		await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false);

		fixture.RequiredClient.TryMarkConnectionUnhealthyResult = false;

		fixture.Coordinator.MarkConnectionUnhealthy(fixture.RequiredClient.ConnectionGeneration);

		Assert.IsTrue(fixture.StartupState.StartupSucceeded);
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, fixture.StartupState.State);
	}

	[TestMethod]
	public async Task MarkConnectionUnhealthy_WhenTheClientThrows_ContainsTheFaultAndKeepsTheReadyState()
	{
		using var fixture = new TestHost();

		await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false);

		fixture.RequiredClient.ThrowOnTryMarkConnectionUnhealthy = true;

		fixture.Coordinator.MarkConnectionUnhealthy(fixture.RequiredClient.ConnectionGeneration);

		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, fixture.StartupState.State);

		Assert.Contains(
			entry => entry.Level == LogLevel.Debug && entry.Message.Contains("unhealthy", StringComparison.Ordinal),
			fixture.Logger.Entries
		);
	}

	[TestMethod]
	public void MarkConnectionUnhealthy_WithoutAClient_DoesNothing()
	{
		using var fixture = new TestHost(hasClient: false);

		fixture.Coordinator.MarkConnectionUnhealthy(1);

		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, fixture.StartupState.State);
	}

	[TestMethod]
	public void MarkConnectionUnavailable_AfterThePersistentLatch_KeepsTheFailedState()
	{
		using var fixture = new TestHost();

		fixture.Coordinator.SetProviderState(LanguageServerFeatureProviderState.Failed);
		fixture.Coordinator.MarkConnectionUnavailable();

		Assert.AreEqual(LanguageServerFeatureProviderState.Failed, fixture.StartupState.State);
	}

	[TestMethod]
	public async Task HandleClientConnectionUnavailable_ForTheReadyGeneration_RenamesToUnavailable()
	{
		using var fixture = new TestHost();

		await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false);

		fixture.Coordinator.HandleClientConnectionUnavailable(fixture.RequiredClient.ConnectionGeneration);

		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, fixture.StartupState.State);
	}

	[TestMethod]
	public async Task HandleClientConnectionUnavailable_ForAnotherGeneration_KeepsTheProviderReady()
	{
		using var fixture = new TestHost();

		await fixture.Coordinator.EnsureConnectionStartedAsync(CancellationToken.None).ConfigureAwait(false);

		fixture.Coordinator.HandleClientConnectionUnavailable(fixture.RequiredClient.ConnectionGeneration + 1);

		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, fixture.StartupState.State);
	}

	[TestMethod]
	public void ReportMissingClientFailure_EntersTheFailedStateAndRaisesOnce()
	{
		using var fixture = new TestHost(hasClient: false);

		fixture.Coordinator.ReportMissingClientFailure();
		fixture.Coordinator.ReportMissingClientFailure();

		Assert.AreEqual(LanguageServerFeatureProviderState.Failed, fixture.StartupState.State);
		Assert.HasCount(1, fixture.StartupFailures);
		Assert.AreEqual("no client", fixture.StartupFailures[0].Message);
		Assert.IsTrue(fixture.StartupFailures[0].IsPersistent);
		Assert.IsTrue(fixture.Logger.HasEntryAtLeast(LogLevel.Error));
	}

	[TestMethod]
	public void ReportMissingClientFailure_WithAClient_DoesNothing()
	{
		using var fixture = new TestHost();

		fixture.Coordinator.ReportMissingClientFailure();

		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, fixture.StartupState.State);
		Assert.IsEmpty(fixture.StartupFailures);
	}

	[TestMethod]
	public void ReportMissingClientFailure_WhenTheMessageIsBlank_RaisesNothing()
	{
		using var fixture = new TestHost(hasClient: false, missingClientFailureMessage: " ");

		fixture.Coordinator.ReportMissingClientFailure();

		Assert.AreEqual(LanguageServerFeatureProviderState.Failed, fixture.StartupState.State);
		Assert.IsEmpty(fixture.StartupFailures);
	}
}
