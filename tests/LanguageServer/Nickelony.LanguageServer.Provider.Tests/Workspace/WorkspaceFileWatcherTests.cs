using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Provider.Tests;

// Timing note: these tests drive real FileSystemWatcher callbacks, so OS event delivery decides when the
// assertions run. Waits are generous upper bounds (5-20 s) that only fail red when an event never arrives, and
// iteration loops repeat scenarios to cover scheduling interleavings without holding a timing assumption of their
// own. The short "must not have happened yet" windows are negative probes that cannot flake red.
[TestClass]
public sealed class WorkspaceFileWatcherTests
{
	[TestMethod]
	public async Task DispatchPendingChangesForTestAsync_PreservesDeleteThenCreatePairForSamePath()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherCoalesce_");
		string workspaceRoot = workspace.Path;
		WorkspaceWatchSpecification[] watchSpecifications = [new("*.ext", IncludeSubdirectories: true)];
		FileChangeBatch? dispatchedBatch = null;

		await using var watcher = new WorkspaceFileWatcher(workspaceRoot, (batch, _) =>
		{
			dispatchedBatch = batch;
			return Task.CompletedTask;
		}, watchSpecifications);

		string filePath = Path.Combine(workspaceRoot, "test.ext");

		QueueChangeForTest(watcher, filePath, FileChangeKind.Deleted);
		QueueChangeForTest(watcher, filePath, FileChangeKind.Created);
		await DispatchPendingChangesForTestAsync(watcher).ConfigureAwait(false);

		Assert.IsNotNull(dispatchedBatch);
		Assert.AreEqual(2, dispatchedBatch.Count);
		Assert.AreEqual(filePath, dispatchedBatch.Entries[0].Path);
		Assert.AreEqual(FileChangeKind.Deleted, dispatchedBatch.Entries[0].Kind);
		Assert.AreEqual(filePath, dispatchedBatch.Entries[1].Path);
		Assert.AreEqual(FileChangeKind.Created, dispatchedBatch.Entries[1].Kind);
	}

	[TestMethod]
	public async Task DispatchPendingChangesForTestAsync_NormalizesEquivalentPathFormsBeforeCoalescing()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherNormalize_");
		string workspaceRoot = workspace.Path;
		WorkspaceWatchSpecification[] watchSpecifications = [new("*.ext", IncludeSubdirectories: true)];
		FileChangeBatch? dispatchedBatch = null;

		Directory.CreateDirectory(Path.Combine(workspaceRoot, "Scripts"));

		await using var watcher = new WorkspaceFileWatcher(workspaceRoot, (batch, _) =>
		{
			dispatchedBatch = batch;
			return Task.CompletedTask;
		}, watchSpecifications);

		string normalizedPath = Path.Combine(workspaceRoot, "Scripts", "test.ext");
		string alternatePath = normalizedPath.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		QueueChangeForTest(watcher, normalizedPath, FileChangeKind.Changed);
		QueueChangeForTest(watcher, alternatePath, FileChangeKind.Changed);
		await DispatchPendingChangesForTestAsync(watcher).ConfigureAwait(false);

		Assert.IsNotNull(dispatchedBatch);
		Assert.AreEqual(1, dispatchedBatch.Count);
		Assert.AreEqual(LanguageServerPaths.NormalizeLocalPath(normalizedPath), dispatchedBatch.Entries[0].Path);
		Assert.AreEqual(FileChangeKind.Changed, dispatchedBatch.Entries[0].Kind);
	}

	[TestMethod]
	public async Task DispatchPendingChangesForTestAsync_WhenDeleteCreateRetryIsNeeded_PreservesBothEntries()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherRetryPair_");
		string workspaceRoot = workspace.Path;
		WorkspaceWatchSpecification[] watchSpecifications = [new("*.ext", IncludeSubdirectories: true)];
		FileChangeBatch? dispatchedBatch = null;
		int dispatchAttemptCount = 0;

		await using var watcher = new WorkspaceFileWatcher(workspaceRoot, (batch, _) =>
		{
			dispatchAttemptCount++;

			if (dispatchAttemptCount == 1)
				throw new IOException("Simulated dispatch failure.");

			dispatchedBatch = batch;
			return Task.CompletedTask;
		}, watchSpecifications);

		string filePath = Path.Combine(workspaceRoot, "test.ext");

		QueueChangeForTest(watcher, filePath, FileChangeKind.Deleted);
		QueueChangeForTest(watcher, filePath, FileChangeKind.Created);
		await DispatchPendingChangesForTestAsync(watcher).ConfigureAwait(false);
		await DispatchPendingChangesForTestAsync(watcher).ConfigureAwait(false);

		Assert.AreEqual(2, dispatchAttemptCount);
		Assert.IsNotNull(dispatchedBatch);
		Assert.AreEqual(2, dispatchedBatch.Count);
		Assert.AreEqual(FileChangeKind.Deleted, dispatchedBatch.Entries[0].Kind);
		Assert.AreEqual(FileChangeKind.Created, dispatchedBatch.Entries[1].Kind);
	}

	[TestMethod]
	public void Start_MissingWorkspaceRoot_ReportsMissingRootWithoutStartingWatchers()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "WorkspaceWatcherMissing_" + Guid.NewGuid().ToString("N"));

		using var watcher = new WorkspaceFileWatcher(
			workspaceRoot,
			(_, _) => Task.CompletedTask,
			[new WorkspaceWatchSpecification("*.ext", IncludeSubdirectories: true)]);

		WorkspaceWatcherStartResult startResult = watcher.Start();

		Assert.AreEqual(WorkspaceWatcherStartOutcome.WorkspaceRootMissing, startResult.Outcome);
		Assert.IsNull(startResult.StartupException);
		Assert.AreEqual(0, watcher.ActiveWatcherCount);
	}

	[TestMethod]
	public void Start_FileSystemWatcherFactoryThrows_ReturnsStartupFailedAndException()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherFactoryThrow_");
		string workspaceRoot = workspace.Path;

		using var watcher = new WorkspaceFileWatcher(
			workspaceRoot,
			(_, _) => Task.CompletedTask,
			[new WorkspaceWatchSpecification("*.ext", IncludeSubdirectories: false)])
		{
			TestHooks = new WorkspaceFileWatcherTestHooks
			{
				FileSystemWatcherFactory = static (_, _) => throw new InvalidOperationException("Simulated watcher creation failure.")
			}
		};

		WorkspaceWatcherStartResult startResult = watcher.Start();

		Assert.AreEqual(WorkspaceWatcherStartOutcome.StartupFailed, startResult.Outcome);
		Assert.IsNotNull(startResult.StartupException);
		Assert.IsTrue(watcher.IsDisposed);
	}

	[TestMethod]
	public void Start_FileSystemWatcherFactoryReturnsNull_ReturnsStartupFailedAndDisposesWatcher()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherFactoryNull_");
		string workspaceRoot = workspace.Path;

		using var watcher = new WorkspaceFileWatcher(
			workspaceRoot,
			(_, _) => Task.CompletedTask,
			[new WorkspaceWatchSpecification("*.ext", IncludeSubdirectories: false)])
		{
			TestHooks = new WorkspaceFileWatcherTestHooks
			{
				FileSystemWatcherFactory = static (_, _) => null!
			}
		};

		WorkspaceWatcherStartResult startResult = watcher.Start();

		Assert.AreEqual(WorkspaceWatcherStartOutcome.StartupFailed, startResult.Outcome);
		Assert.IsNotNull(startResult.StartupException);
		Assert.IsTrue(watcher.IsDisposed);
	}

	[TestMethod]
	public void Start_WhenAWatcherReportsAnErrorWhileItIsStarting_StopsAndNotifiesInsteadOfDroppingTheError()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherActivationError_");
		string workspaceRoot = workspace.Path;
		int watcherFailedCallCount = 0;

		WorkspaceFileWatcher watcher = null!;

		// Raise the error inside the exact window between registering the watcher and activating it: a watcher that
		// is not registered yet is treated as an unknown (superseded) watcher and its failure would be dropped.
		watcher = new WorkspaceFileWatcher(
			workspaceRoot,
			(_, _) => Task.CompletedTask,
			[new WorkspaceWatchSpecification("*.ext", IncludeSubdirectories: false)],
			(_, _) => Interlocked.Increment(ref watcherFailedCallCount))
		{
			TestHooks = new WorkspaceFileWatcherTestHooks
			{
				WatcherActivation = activated => watcher.HandleWatcherError(new IOException("Simulated activation failure."), activated)
			}
		};

		using (watcher)
		{
			WorkspaceWatcherStartOutcome startOutcome = watcher.Start().Outcome;

			Assert.AreEqual(WorkspaceWatcherStartOutcome.StartupFailed, startOutcome);
			Assert.AreEqual(1, Volatile.Read(ref watcherFailedCallCount));
			Assert.AreEqual(0, watcher.ActiveWatcherCount);
			Assert.IsTrue(watcher.IsDisposed);
		}
	}

	[TestMethod]
	public void Start_AfterStartupFailureOnSameInstance_ReturnsDisposed()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherRetryAfterFailure_");
		string workspaceRoot = workspace.Path;

		using var watcher = new WorkspaceFileWatcher(
			workspaceRoot,
			(_, _) => Task.CompletedTask,
			[new WorkspaceWatchSpecification("*.ext", IncludeSubdirectories: false)])
		{
			TestHooks = new WorkspaceFileWatcherTestHooks
			{
				FileSystemWatcherFactory = static (_, _) => throw new InvalidOperationException("Simulated watcher creation failure.")
			}
		};

		WorkspaceWatcherStartResult firstStartResult = watcher.Start();
		WorkspaceWatcherStartResult retryResult = watcher.Start();

		Assert.AreEqual(WorkspaceWatcherStartOutcome.StartupFailed, firstStartResult.Outcome);
		Assert.IsNotNull(firstStartResult.StartupException);
		Assert.AreEqual(WorkspaceWatcherStartOutcome.Disposed, retryResult.Outcome);
		Assert.IsNull(retryResult.StartupException);
		Assert.IsTrue(watcher.IsDisposed);
	}

	[TestMethod]
	public async Task DispatchPendingChangesForTestAsync_WhenDispatchFails_RetainsBatchForRetry()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherRetry_");
		string workspaceRoot = workspace.Path;
		WorkspaceWatchSpecification[] watchSpecifications = [new("*.ext", IncludeSubdirectories: true)];
		FileChangeBatch? dispatchedBatch = null;
		int dispatchAttemptCount = 0;

		using var logScope = new TestLoggerScope(LogLevel.Debug);

		await using var watcher = new WorkspaceFileWatcher(workspaceRoot, (batch, _) =>
		{
			dispatchAttemptCount++;

			if (dispatchAttemptCount == 1)
				throw new IOException("Simulated dispatch failure.");

			dispatchedBatch = batch;
			return Task.CompletedTask;
		}, watchSpecifications, logger: logScope.CreateLogger<WorkspaceFileWatcher>());

		string filePath = Path.Combine(workspaceRoot, "test.ext");

		QueueChangeForTest(watcher, filePath, FileChangeKind.Changed);
		await DispatchPendingChangesForTestAsync(watcher).ConfigureAwait(false);
		await DispatchPendingChangesForTestAsync(watcher).ConfigureAwait(false);

		Assert.AreEqual(2, dispatchAttemptCount);
		Assert.IsNotNull(dispatchedBatch);
		Assert.AreEqual(1, dispatchedBatch.Count);
		Assert.AreEqual(filePath, dispatchedBatch.Entries[0].Path);
		Assert.AreEqual(FileChangeKind.Changed, dispatchedBatch.Entries[0].Kind);

		Assert.IsTrue(logScope.Logs.Any(log => log.Contains("Workspace file watcher dispatch failed", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("Simulated dispatch failure.", StringComparison.Ordinal)
			&& log.Contains(workspaceRoot, StringComparison.OrdinalIgnoreCase)
			&& log.Contains("1 queued change", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public async Task DispatchPendingChangesForTestAsync_WhenDispatchIsCanceled_RetainsBatchForRetry()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherCanceled_");
		string workspaceRoot = workspace.Path;
		WorkspaceWatchSpecification[] watchSpecifications = [new("*.ext", IncludeSubdirectories: true)];
		FileChangeBatch? dispatchedBatch = null;
		int dispatchAttemptCount = 0;

		using var logScope = new TestLoggerScope(LogLevel.Debug);

		await using var watcher = new WorkspaceFileWatcher(workspaceRoot, (batch, _) =>
		{
			dispatchAttemptCount++;

			if (dispatchAttemptCount == 1)
				throw new OperationCanceledException("Simulated dispatch cancellation.");

			dispatchedBatch = batch;
			return Task.CompletedTask;
		}, watchSpecifications, logger: logScope.CreateLogger<WorkspaceFileWatcher>());

		string filePath = Path.Combine(workspaceRoot, "test.ext");

		QueueChangeForTest(watcher, filePath, FileChangeKind.Changed);
		await DispatchPendingChangesForTestAsync(watcher).ConfigureAwait(false);
		await DispatchPendingChangesForTestAsync(watcher).ConfigureAwait(false);

		Assert.AreEqual(2, dispatchAttemptCount);
		Assert.IsNotNull(dispatchedBatch);
		Assert.AreEqual(1, dispatchedBatch.Count);
		Assert.AreEqual(filePath, dispatchedBatch.Entries[0].Path);
		Assert.AreEqual(FileChangeKind.Changed, dispatchedBatch.Entries[0].Kind);

		Assert.IsTrue(logScope.Logs.Any(log => log.Contains("Workspace file watcher dispatch failed", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("Simulated dispatch cancellation.", StringComparison.Ordinal)
			&& log.Contains("retrying in 250 ms", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public async Task DispatchPendingChangesForTestAsync_WhenDispatchKeepsFailing_LogsBoundedRetryAndGivesUp()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherBackoff_");
		string workspaceRoot = workspace.Path;
		WorkspaceWatchSpecification[] watchSpecifications = [new("*.ext", IncludeSubdirectories: true)];
		int watcherFailedCallCount = 0;

		using var logScope = new TestLoggerScope(LogLevel.Debug);

		await using var watcher = new WorkspaceFileWatcher(workspaceRoot, (_, _) => throw new IOException("Persistent dispatch failure."), watchSpecifications,
			(_, _) => Interlocked.Increment(ref watcherFailedCallCount),
			logger: logScope.CreateLogger<WorkspaceFileWatcher>());

		string filePath = Path.Combine(workspaceRoot, "test.ext");

		QueueChangeForTest(watcher, filePath, FileChangeKind.Changed);
		await DispatchPendingChangesForTestAsync(watcher).ConfigureAwait(false);
		await DispatchPendingChangesForTestAsync(watcher).ConfigureAwait(false);
		await DispatchPendingChangesForTestAsync(watcher).ConfigureAwait(false);

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Debug|", StringComparison.Ordinal)
			&& log.Contains("retrying in 250 ms", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Debug|", StringComparison.Ordinal)
			&& log.Contains("retrying in 500 ms", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("3 times in a row", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("were dropped", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("notified", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));

		// The bounded retry gives up after the final attempt and notifies the owner exactly once.
		Assert.AreEqual(1, Volatile.Read(ref watcherFailedCallCount));
	}

	[TestMethod]
	public async Task DispatchPendingChangesForTestAsync_WhenDispatchKeepsFailing_ReportsWatcherFailureAfterBoundedRetries()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherEscalate_");
		string workspaceRoot = workspace.Path;
		WorkspaceWatchSpecification[] watchSpecifications = [new("*.ext", IncludeSubdirectories: true)];
		int watcherFailedCallCount = 0;
		Exception? reportedException = null;
		var watcherFailed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		await using var watcher = new WorkspaceFileWatcher(
			workspaceRoot,
			(_, _) => throw new IOException("Persistent dispatch failure."),
			watchSpecifications,
			(_, exception) =>
			{
				watcherFailedCallCount++;
				reportedException = exception;
				watcherFailed.TrySetResult(true);
			});

		Assert.AreEqual(WorkspaceWatcherStartOutcome.Started, watcher.Start().Outcome);

		string filePath = Path.Combine(workspaceRoot, "test.ext");

		QueueChangeForTest(watcher, filePath, FileChangeKind.Changed);

		for (int i = 0; i < 5; i++)
			await DispatchPendingChangesForTestAsync(watcher).ConfigureAwait(false);

		// The escalation notification runs outside the dispatch operation so the owner can dispose from it.
		await watcherFailed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.AreEqual(1, watcherFailedCallCount);
		Assert.IsInstanceOfType(reportedException, typeof(IOException));
		Assert.AreEqual(0, watcher.ActiveWatcherCount);
	}

	[TestMethod]
	public async Task DispatchPendingChangesForTestAsync_WhenDispatchEscalates_StopsWatchingAndDropsTheBatch()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherEscalationDrop_");
		string workspaceRoot = workspace.Path;
		WorkspaceWatchSpecification[] watchSpecifications = [new("*.ext", IncludeSubdirectories: true)];
		int watcherFailedCallCount = 0;
		int dispatchAttemptCount = 0;

		await using var watcher = new WorkspaceFileWatcher(
			workspaceRoot,
			(_, _) =>
			{
				Interlocked.Increment(ref dispatchAttemptCount);
				throw new IOException("Persistent dispatch failure.");
			},
			watchSpecifications,
			(_, _) => Interlocked.Increment(ref watcherFailedCallCount));

		Assert.AreEqual(WorkspaceWatcherStartOutcome.Started, watcher.Start().Outcome);

		string filePath = Path.Combine(workspaceRoot, "test.ext");

		QueueChangeForTest(watcher, filePath, FileChangeKind.Changed);

		for (int i = 0; i < 3; i++)
			await DispatchPendingChangesForTestAsync(watcher).ConfigureAwait(false);

		Assert.AreEqual(1, Volatile.Read(ref watcherFailedCallCount));
		Assert.AreEqual(3, Volatile.Read(ref dispatchAttemptCount));
		Assert.AreEqual(0, watcher.ActiveWatcherCount);

		// Bounded retry: the dropped batch is not delivered by a later attempt, and the owner is not notified again.
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.AreEqual(3, Volatile.Read(ref dispatchAttemptCount));
		Assert.AreEqual(1, Volatile.Read(ref watcherFailedCallCount));
	}

	[TestMethod]
	public async Task Dispose_DuringActiveDispatch_DoesNotFaultDispatch()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherDispose_");
		string workspaceRoot = workspace.Path;
		WorkspaceWatchSpecification[] watchSpecifications = [new("*.ext", IncludeSubdirectories: true)];
		var dispatchStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowDispatchToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		await using var watcher = new WorkspaceFileWatcher(workspaceRoot, async (_, _) =>
		{
			dispatchStarted.TrySetResult(true);
			await allowDispatchToFinish.Task.ConfigureAwait(false);
		}, watchSpecifications);

		QueueChangeForTest(watcher, Path.Combine(workspaceRoot, "test.ext"), FileChangeKind.Changed);
		Task dispatchTask = DispatchPendingChangesForTestAsync(watcher);

		Task completedTask = await Task.WhenAny(dispatchStarted.Task, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
		Assert.AreSame(dispatchStarted.Task, completedTask);

		// Disposal is non-blocking: it completes without waiting for the in-flight dispatch.
		Task disposeTask = Task.Run(watcher.Dispose);

		await disposeTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
		Assert.IsFalse(dispatchTask.IsCompleted);

		allowDispatchToFinish.TrySetResult(true);

		await dispatchTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task DispatchPendingChangesForTestAsync_WhenDispatchFailsAfterDisposal_DropsTheDrainedBatch()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherDisposeNoFlush_");
		string workspaceRoot = workspace.Path;
		WorkspaceWatchSpecification[] watchSpecifications = [new("*.ext", IncludeSubdirectories: true)];
		var dispatchStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstDispatchToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		FileChangeBatch? dispatchedBatch = null;
		int dispatchAttemptCount = 0;

		await using var watcher = new WorkspaceFileWatcher(workspaceRoot, async (batch, _) =>
		{
			dispatchAttemptCount++;

			if (dispatchAttemptCount == 1)
			{
				dispatchStarted.TrySetResult(true);
				await allowFirstDispatchToFinish.Task.ConfigureAwait(false);

				throw new IOException("Simulated dispatch failure after disposal.");
			}

			dispatchedBatch = batch;
		}, watchSpecifications);

		string filePath = Path.Combine(workspaceRoot, "test.ext");

		QueueChangeForTest(watcher, filePath, FileChangeKind.Changed);
		Task dispatchTask = DispatchPendingChangesForTestAsync(watcher);

		await dispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Task disposeTask = Task.Run(watcher.Dispose);

		await disposeTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		allowFirstDispatchToFinish.TrySetResult(true);

		await dispatchTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		// Disposal drops the drained batch: it is neither requeued nor delivered by another attempt.
		Assert.AreEqual(1, dispatchAttemptCount);
		Assert.IsNull(dispatchedBatch);
	}

	[TestMethod]
	public async Task DisposeAsync_DuringActiveDispatch_DropsRequeuedBatch()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherDisposeRetry_");
		string workspaceRoot = workspace.Path;
		WorkspaceWatchSpecification[] watchSpecifications = [new("*.ext", IncludeSubdirectories: true)];
		var dispatchStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstDispatchToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		FileChangeBatch? dispatchedBatch = null;
		int dispatchAttemptCount = 0;

		await using var watcher = new WorkspaceFileWatcher(workspaceRoot, async (batch, _) =>
		{
			dispatchAttemptCount++;

			if (dispatchAttemptCount == 1)
			{
				dispatchStarted.TrySetResult(true);
				await allowFirstDispatchToFinish.Task.ConfigureAwait(false);

				throw new IOException("Simulated dispatch failure during disposal.");
			}

			dispatchedBatch = batch;
		}, watchSpecifications);

		string filePath = Path.Combine(workspaceRoot, "test.ext");

		QueueChangeForTest(watcher, filePath, FileChangeKind.Changed);
		Task dispatchTask = DispatchPendingChangesForTestAsync(watcher);

		await dispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Task disposeTask = watcher.DisposeAsync().AsTask();

		await disposeTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		allowFirstDispatchToFinish.TrySetResult(true);

		await dispatchTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		// Disposal drops the drained batch: it is neither requeued nor delivered by another attempt.
		Assert.AreEqual(1, dispatchAttemptCount);
		Assert.IsNull(dispatchedBatch);
	}

	[TestMethod]
	public async Task DisposeAsync_ConcurrentCallers_BothCompleteWithoutFaults()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherConcurrentDispose_");
		string workspaceRoot = workspace.Path;
		WorkspaceWatchSpecification[] watchSpecifications = [new("*.ext", IncludeSubdirectories: true)];
		var dispatchStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowDispatchToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		await using var watcher = new WorkspaceFileWatcher(workspaceRoot, async (_, _) =>
		{
			dispatchStarted.TrySetResult(true);
			await allowDispatchToFinish.Task.ConfigureAwait(false);
		}, watchSpecifications);

		QueueChangeForTest(watcher, Path.Combine(workspaceRoot, "test.ext"), FileChangeKind.Changed);
		Task dispatchTask = DispatchPendingChangesForTestAsync(watcher);

		await dispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Task firstDisposeTask = watcher.DisposeAsync().AsTask();
		Task secondDisposeTask = watcher.DisposeAsync().AsTask();

		await Task.WhenAll(firstDisposeTask, secondDisposeTask).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.IsTrue(watcher.IsDisposed);

		allowDispatchToFinish.TrySetResult(true);

		await dispatchTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task Dispose_WithinDispatchCallback_CompletesWithoutBlockingDispatch()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherReentrant_");
		string workspaceRoot = workspace.Path;
		WorkspaceWatchSpecification[] watchSpecifications = [new("*.ext", IncludeSubdirectories: true)];
		WorkspaceFileWatcher? watcher = null;

		watcher = new WorkspaceFileWatcher(workspaceRoot, (_, _) =>
		{
			// Disposal is non-blocking, so the dispatch callback may dispose its own watcher.
			watcher!.Dispose();
			throw new IOException("Dispatch stops after the reentrant disposal.");
		}, watchSpecifications);

		QueueChangeForTest(watcher, Path.Combine(workspaceRoot, "test.ext"), FileChangeKind.Changed);

		Task dispatchTask = DispatchPendingChangesForTestAsync(watcher);

		await dispatchTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.IsTrue(watcher.IsDisposed);
	}

	[TestMethod]
	public async Task Dispose_DuringActiveDispatch_CancelsTheDispatchCancellationToken()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherDisposeToken_");
		string workspaceRoot = workspace.Path;
		WorkspaceWatchSpecification[] watchSpecifications = [new("*.ext", IncludeSubdirectories: true)];
		var dispatchStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var tokenCancellationObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowDispatchToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		await using var watcher = new WorkspaceFileWatcher(workspaceRoot, async (_, cancellationToken) =>
		{
			using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(
				() => tokenCancellationObserved.TrySetResult(true));

			dispatchStarted.TrySetResult(true);

			await allowDispatchToFinish.Task.ConfigureAwait(false);
		}, watchSpecifications);

		QueueChangeForTest(watcher, Path.Combine(workspaceRoot, "test.ext"), FileChangeKind.Changed);
		Task dispatchTask = DispatchPendingChangesForTestAsync(watcher);

		await dispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		await watcher.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		// Disposal cancels the lifetime token so an in-flight dispatch can observe it and unwind promptly.
		await tokenCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		allowDispatchToFinish.TrySetResult(true);

		await dispatchTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ReportErrorForTest_WithPendingChanges_WaitsForFailureHandlerBeforeRecoveryDispatch()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherRecoveryOrdering_");
		string workspaceRoot = workspace.Path;
		WorkspaceWatchSpecification[] watchSpecifications = [new("*.ext", IncludeSubdirectories: true)];
		var failureHandlerEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFailureHandlerToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var recoveryDispatchStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int dispatchObservedBeforeFailureHandlerFinished = 0;

		await using var watcher = new WorkspaceFileWatcher(
			workspaceRoot,
			(_, _) =>
			{
				if (!allowFailureHandlerToFinish.Task.IsCompleted)
					Interlocked.Exchange(ref dispatchObservedBeforeFailureHandlerFinished, 1);

				recoveryDispatchStarted.TrySetResult(true);
				return Task.CompletedTask;
			},
			watchSpecifications,
			(_, _) =>
			{
				failureHandlerEntered.TrySetResult(true);
				allowFailureHandlerToFinish.Task.GetAwaiter().GetResult();
			});

		Assert.AreEqual(WorkspaceWatcherStartOutcome.Started, watcher.Start().Outcome);

		QueueChangeForTest(watcher, Path.Combine(workspaceRoot, "test.ext"), FileChangeKind.Changed);
		Task errorTask = Task.Run(() => ReportErrorForTest(watcher, new IOException("Simulated watcher failure.")));

		await failureHandlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		// The dispatch callback latches whether it started before the failure handler returned, so releasing the
		// handler and completing the recovery proves the ordering without a bounded timing window.
		allowFailureHandlerToFinish.TrySetResult(true);

		await errorTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
		await recoveryDispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
		Assert.AreEqual(0, Volatile.Read(ref dispatchObservedBeforeFailureHandlerFinished));
	}

	[TestMethod]
	public void Dispose_WhenPendingChangesExistAndNoDispatchIsActive_DropsBufferedBatch()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherDisposeFlush_");
		string workspaceRoot = workspace.Path;
		WorkspaceWatchSpecification[] watchSpecifications = [new("*.ext", IncludeSubdirectories: true)];
		FileChangeBatch? dispatchedBatch = null;

		using var watcher = new WorkspaceFileWatcher(workspaceRoot, (batch, _) =>
		{
			dispatchedBatch = batch;
			return Task.CompletedTask;
		}, watchSpecifications);

		QueueChangeForTest(watcher, Path.Combine(workspaceRoot, "test.ext"), FileChangeKind.Changed);

		watcher.Dispose();

		Assert.IsNull(dispatchedBatch);
	}

	[TestMethod]
	public void Dispose_WhenBufferedChangesExist_DoesNotDeadlockCallerContext()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherDisposeContext_");
		string workspaceRoot = workspace.Path;
		WorkspaceWatchSpecification[] watchSpecifications = [new("*.ext", IncludeSubdirectories: true)];
		var disposeCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		Exception? failure = null;

		var thread = new Thread(() =>
		{
			SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());

			try
			{
				using var watcher = new WorkspaceFileWatcher(
					workspaceRoot,
					async (_, _) => await Task.Yield(),
					watchSpecifications);

				QueueChangeForTest(watcher, Path.Combine(workspaceRoot, "test.ext"), FileChangeKind.Changed);

				watcher.Dispose();
				disposeCompleted.TrySetResult(true);
			}
			catch (Exception exception)
			{
				failure = exception;
				disposeCompleted.TrySetException(exception);
			}
			finally
			{
				SynchronizationContext.SetSynchronizationContext(null);
			}
		})
		{
			IsBackground = true
		};

		thread.Start();

		Assert.IsTrue(disposeCompleted.Task.Wait(TimeSpan.FromSeconds(5)));
		Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(1)));
		Assert.IsNull(failure);
	}

	[TestMethod]
	public void Start_UsesConfiguredWatchSpecifications()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherSpecs_");
		string workspaceRoot = workspace.Path;

		using var watcher = new WorkspaceFileWatcher(
			workspaceRoot,
			(_, _) => Task.CompletedTask,
			watchSpecifications:
			[
				new WorkspaceWatchSpecification("*.ext", IncludeSubdirectories: true),
				new WorkspaceWatchSpecification(".example.*", IncludeSubdirectories: false)
			]);

		Assert.AreEqual(WorkspaceWatcherStartOutcome.Started, watcher.Start().Outcome);
		Assert.AreEqual(2, watcher.ActiveWatcherCount);
	}

	[TestMethod]
	public async Task ReportErrorForTest_ConcurrentWithDispose_LeavesNoActiveWatchers()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherErrorDispose_");
		string workspaceRoot = workspace.Path;

		for (int i = 0; i < 50; i++)
		{
			await using var watcher = new WorkspaceFileWatcher(
				workspaceRoot,
				(_, _) => Task.CompletedTask,
				[new WorkspaceWatchSpecification("*.ext", IncludeSubdirectories: true)]);

			Assert.AreEqual(WorkspaceWatcherStartOutcome.Started, watcher.Start().Outcome);

			Task errorTask = Task.Run(() => ReportErrorForTest(watcher, new IOException("Simulated watcher failure.")));
			Task disposeTask = Task.Run(watcher.Dispose);

			await Task.WhenAll(errorTask, disposeTask).ConfigureAwait(false);

			Assert.IsTrue(watcher.IsDisposed);
			Assert.AreEqual(0, watcher.ActiveWatcherCount);
		}
	}

	[TestMethod]
	[TestCategory(TestCategories.Integration)]
	[Timeout(120000)]
	public async Task Start_WhenWatchedDirectoryIsDeleted_StopsWatchersFromTheWatcherCallback()
	{
		if (!OperatingSystem.IsWindows())
			Assert.Inconclusive("Real FileSystemWatcher error raising in this test is only exercised on Windows.");

		int observedFailureCount = 0;

		// Whether deleting a watched directory raises a real FileSystemWatcher error is a race with handle
		// invalidation and is not contractual, so a missed event is tolerated; the probe requires at least one
		// observed callback instead of forcing every iteration to win the race.
		for (int iteration = 0; iteration < 10 && observedFailureCount == 0; iteration++)
		{
			using var workspace = new TemporaryDirectory("WorkspaceWatcherRealError_");
			string watchedDirectory = Path.Combine(workspace.Path, "watched");
			Directory.CreateDirectory(watchedDirectory);

			var failureObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			await using var watcher = new WorkspaceFileWatcher(
				watchedDirectory,
				(_, _) => Task.CompletedTask,
				[new WorkspaceWatchSpecification("*.ext", IncludeSubdirectories: true)],
				(_, _) => failureObserved.TrySetResult(true));

			Assert.AreEqual(WorkspaceWatcherStartOutcome.Started, watcher.Start().Outcome);

			// Deleting the watched directory raises a real FileSystemWatcher error on the watcher callback thread;
			// the handler disposes the watchers synchronously from that callback and must not deadlock.
			Directory.Delete(watchedDirectory, recursive: true);

			Task completedTask = await Task.WhenAny(failureObserved.Task, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);

			if (ReferenceEquals(completedTask, failureObserved.Task))
			{
				observedFailureCount++;

				Assert.AreEqual(0, watcher.ActiveWatcherCount);
			}
		}

		Assert.IsTrue(observedFailureCount >= 1,
			"At least one iteration must observe a real watcher error callback on the callback thread.");
	}

	[TestMethod]
	public void ReportErrorForTest_WhenFailureHandlerThrows_LogsWarningAndStopsWatching()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherFailureCallback_");
		string workspaceRoot = workspace.Path;
		using var logScope = new TestLoggerScope(LogLevel.Warning);

		using var watcher = new WorkspaceFileWatcher(
			workspaceRoot,
			(_, _) => Task.CompletedTask,
			[new WorkspaceWatchSpecification("*.ext", IncludeSubdirectories: true)],
			(_, _) => throw new InvalidOperationException("Simulated watcher failure callback exception."),
			logger: logScope.CreateLogger<WorkspaceFileWatcher>());

		Assert.AreEqual(WorkspaceWatcherStartOutcome.Started, watcher.Start().Outcome);

		ReportErrorForTest(watcher, new IOException("Simulated watcher failure."));

		Assert.AreEqual(0, watcher.ActiveWatcherCount);

		Assert.IsTrue(logScope.Logs.Any(log => log.Contains("Workspace watcher failure handler threw.", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("Simulated watcher failure callback exception.", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void Start_AfterFailure_ResetsFailureReportingForNextFailureSequence()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherRestart_");
		string workspaceRoot = workspace.Path;
		int failureCount = 0;

		using var watcher = new WorkspaceFileWatcher(
			workspaceRoot,
			(_, _) => Task.CompletedTask,
			[new WorkspaceWatchSpecification("*.ext", IncludeSubdirectories: true)],
			(_, _) => failureCount++);

		Assert.AreEqual(WorkspaceWatcherStartOutcome.Started, watcher.Start().Outcome);

		ReportErrorForTest(watcher, new IOException("Simulated watcher failure 1."));

		Assert.AreEqual(1, failureCount);
		Assert.AreEqual(0, watcher.ActiveWatcherCount);

		Assert.AreEqual(WorkspaceWatcherStartOutcome.Started, watcher.Start().Outcome);

		ReportErrorForTest(watcher, new IOException("Simulated watcher failure 2."));

		Assert.AreEqual(2, failureCount);
		Assert.AreEqual(0, watcher.ActiveWatcherCount);
	}

	[TestMethod]
	public async Task Start_ConcurrentWithDispose_LeavesNoActiveWatchers()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherStartDispose_");
		string workspaceRoot = workspace.Path;

		for (int i = 0; i < 50; i++)
		{
			var watcher = new WorkspaceFileWatcher(
				workspaceRoot,
				(_, _) => Task.CompletedTask,
				[new WorkspaceWatchSpecification("*.ext", IncludeSubdirectories: true)]);

			Task<WorkspaceWatcherStartOutcome> startTask = Task.Run(() => watcher.Start().Outcome);
			Task disposeTask = Task.Run(watcher.Dispose);

			await Task.WhenAll(startTask, disposeTask).ConfigureAwait(false);

			Assert.IsTrue(watcher.IsDisposed);
			Assert.AreEqual(0, watcher.ActiveWatcherCount);

			watcher.Dispose();
		}
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void Start_WhenAlreadyStarted_ReportsAlreadyRunning()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherAlreadyRunning_");
		using var watcher = new WorkspaceFileWatcher(workspace.Path, (_, _) => Task.CompletedTask,
			[new WorkspaceWatchSpecification("*.ext", IncludeSubdirectories: true)]);

		Assert.AreEqual(WorkspaceWatcherStartOutcome.Started, watcher.Start().Outcome);
		Assert.AreEqual(WorkspaceWatcherStartOutcome.AlreadyRunning, watcher.Start().Outcome);
	}

	[TestMethod]
	[TestCategory(TestCategories.Integration)]
	[OSCondition(OperatingSystems.Windows)]
	[Timeout(120000)]
	public async Task Start_RealFileSystemWatcher_ForwardsCreatedFileThroughDebouncedDispatch()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherRealEvents_");
		var dispatchedBatch = new TaskCompletionSource<FileChangeBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
		string filePath = Path.Combine(workspace.Path, "created.ext");

		await using var watcher = new WorkspaceFileWatcher(workspace.Path,
			(batch, _) =>
			{
				dispatchedBatch.TrySetResult(batch);
				return Task.CompletedTask;
			},
			[new WorkspaceWatchSpecification("*.ext", IncludeSubdirectories: true)]);

		Assert.AreEqual(WorkspaceWatcherStartOutcome.Started, watcher.Start().Outcome);

		FileChangeBatch batch = await RunFileOperationWithEventRetryAsync(
			() => File.WriteAllText(filePath, "return 1"),
			() => dispatchedBatch.Task).ConfigureAwait(false);
		string normalizedFilePath = LanguageServerPaths.NormalizeLocalPath(filePath);

		Assert.IsTrue(batch.Entries.Any(entry => string.Equals(entry.Path, normalizedFilePath, StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, batch.Entries.Select(entry => entry.Path)));
	}

	[TestMethod]
	[TestCategory(TestCategories.Integration)]
	[OSCondition(OperatingSystems.Windows)]
	[Timeout(120000)]
	public async Task Start_RealFileSystemWatcher_ForwardsRenamedFileThroughDebouncedDispatch()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherRealRename_");
		string originalPath = Path.Combine(workspace.Path, "original.ext");
		string renamedPath = Path.Combine(workspace.Path, "renamed.ext");
		string normalizedRenamedPath = LanguageServerPaths.NormalizeLocalPath(renamedPath);
		var renamedEndpointDispatched = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		await using var watcher = new WorkspaceFileWatcher(workspace.Path,
			(batch, _) =>
			{
				if (batch.Entries.Any(entry => string.Equals(entry.Path, normalizedRenamedPath, StringComparison.OrdinalIgnoreCase)))
					renamedEndpointDispatched.TrySetResult(true);

				return Task.CompletedTask;
			},
			[new WorkspaceWatchSpecification("*.ext", IncludeSubdirectories: true)]);

		Assert.AreEqual(WorkspaceWatcherStartOutcome.Started, watcher.Start().Outcome);

		await RunFileOperationWithEventRetryAsync(
			() =>
			{
				File.WriteAllText(originalPath, "return 1");
				File.Move(originalPath, renamedPath, overwrite: true);
			},
			() => renamedEndpointDispatched.Task).ConfigureAwait(false);
	}

	[TestMethod]
	[TestCategory(TestCategories.Integration)]
	[OSCondition(OperatingSystems.Windows)]
	[Timeout(120000)]
	public async Task Start_RealFileSystemWatcher_RenameIntoTheWatchedSet_SuppressesTheNonMatchingEndpoint()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherRealRenameFilterIn_");
		string originalPath = Path.Combine(workspace.Path, "original.other");
		string renamedPath = Path.Combine(workspace.Path, "renamed.ext");
		string normalizedOriginalPath = LanguageServerPaths.NormalizeLocalPath(originalPath);
		string normalizedRenamedPath = LanguageServerPaths.NormalizeLocalPath(renamedPath);
		var matchingEndpointDispatched = new TaskCompletionSource<FileChangeBatch>(TaskCreationOptions.RunContinuationsAsynchronously);

		await using var watcher = new WorkspaceFileWatcher(workspace.Path,
			(batch, _) =>
			{
				if (batch.Entries.Any(entry => string.Equals(entry.Path, normalizedRenamedPath, StringComparison.OrdinalIgnoreCase)
					&& entry.Kind == FileChangeKind.Created))
				{
					matchingEndpointDispatched.TrySetResult(batch);
				}

				return Task.CompletedTask;
			},
			[new WorkspaceWatchSpecification("*.ext", IncludeSubdirectories: true)]);

		Assert.AreEqual(WorkspaceWatcherStartOutcome.Started, watcher.Start().Outcome);

		// The FileSystemWatcher raises the Renamed event only when the new name matches its filter, so this is the
		// endpoint pair the specification can observe: the create of the matching new name is forwarded and the
		// delete of the old, non-matching name is suppressed.
		FileChangeBatch batch = await RunFileOperationWithEventRetryAsync(
			() =>
			{
				File.WriteAllText(originalPath, "return 1");
				File.Move(originalPath, renamedPath, overwrite: true);
			},
			() => matchingEndpointDispatched.Task).ConfigureAwait(false);

		Assert.IsTrue(batch.Entries.Any(entry => string.Equals(entry.Path, normalizedRenamedPath, StringComparison.OrdinalIgnoreCase)
			&& entry.Kind == FileChangeKind.Created));
		Assert.IsFalse(batch.Entries.Any(entry => string.Equals(entry.Path, normalizedOriginalPath, StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, batch.Entries.Select(entry => entry.Path)));
	}

	[TestMethod]
	public void Constructor_WithEmptyWatchSpecifications_ThrowsArgumentException()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherValidation_");

		Assert.ThrowsExactly<ArgumentException>(() => new WorkspaceFileWatcher(
			workspace.Path, (_, _) => Task.CompletedTask, []));
	}

	[TestMethod]
	public void Constructor_WithBlankWorkspaceRoot_ThrowsArgumentException()
	{
		Assert.ThrowsExactly<ArgumentException>(() => new WorkspaceFileWatcher(
			" ", (_, _) => Task.CompletedTask, [new WorkspaceWatchSpecification("*.ext", IncludeSubdirectories: true)]));
	}

	[TestMethod]
	public async Task DisposeAsync_WhenALifetimeCancellationCallbackThrows_StillCompletesDisposal()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherThrowingCancelCallback_");
		string workspaceRoot = workspace.Path;
		WorkspaceWatchSpecification[] watchSpecifications = [new("*.ext", IncludeSubdirectories: true)];
		CancellationToken dispatchToken = default;

		await using var watcher = new WorkspaceFileWatcher(workspaceRoot, (_, cancellationToken) =>
		{
			dispatchToken = cancellationToken;
			return Task.CompletedTask;
		}, watchSpecifications);

		QueueChangeForTest(watcher, Path.Combine(workspaceRoot, "test.ext"), FileChangeKind.Changed);
		await DispatchPendingChangesForTestAsync(watcher).ConfigureAwait(false);

		using CancellationTokenRegistration registration = dispatchToken.Register(
			static () => throw new InvalidOperationException("Simulated throwing cancellation callback."));

		await watcher.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.IsTrue(watcher.IsDisposed);
	}

	[TestMethod]
	public async Task Dispose_DuringFailedDispatch_DropsTheDrainedBatch()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherDisposeRace_");
		string workspaceRoot = workspace.Path;
		WorkspaceWatchSpecification[] watchSpecifications = [new("*.ext", IncludeSubdirectories: true)];

		for (int iteration = 0; iteration < 25; iteration++)
		{
			var dispatchDrained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var allowDispatchFailure = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			FileChangeBatch? laterBatch = null;
			int dispatchAttemptCount = 0;

			var watcher = new WorkspaceFileWatcher(workspaceRoot, async (batch, _) =>
			{
				if (Interlocked.Increment(ref dispatchAttemptCount) == 1)
				{
					dispatchDrained.TrySetResult(true);
					await allowDispatchFailure.Task.ConfigureAwait(false);

					throw new IOException("Simulated dispatch failure.");
				}

				laterBatch = batch;
			}, watchSpecifications);

			try
			{
				string filePath = Path.Combine(workspaceRoot, "test.ext");

				QueueChangeForTest(watcher, filePath, FileChangeKind.Changed);
				Task dispatchTask = DispatchPendingChangesForTestAsync(watcher);

				await dispatchDrained.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

				Task disposeTask = watcher.DisposeAsync().AsTask();

				allowDispatchFailure.TrySetResult(true);

				await Task.WhenAll(dispatchTask, disposeTask.WaitAsync(TimeSpan.FromSeconds(5))).ConfigureAwait(false);

				// Disposal drops the drained batch instead of flushing or requeueing it.
				Assert.AreEqual(1, dispatchAttemptCount);
				Assert.IsNull(laterBatch);
			}
			finally
			{
				await watcher.DisposeAsync().ConfigureAwait(false);
			}
		}
	}

	[TestMethod]
	public async Task DispatchPendingChangesForTestAsync_WhenOwnerDisposesFromTheFailureNotification_DisposesWithoutThrowing()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherOwnerDisposeRecovery_");
		string workspaceRoot = workspace.Path;
		WorkspaceWatchSpecification[] watchSpecifications = [new("*.ext", IncludeSubdirectories: true)];
		var ownerNotified = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		Exception? ownerDisposeFailure = null;
		WorkspaceFileWatcher? watcher = null;

		try
		{
			watcher = new WorkspaceFileWatcher(
				workspaceRoot,
				(_, _) => throw new IOException("Persistent dispatch failure."),
				watchSpecifications,
				(failedWatcher, _) =>
				{
					try
					{
						failedWatcher.Dispose();
					}
					catch (Exception exception)
					{
						ownerDisposeFailure = exception;
					}

					ownerNotified.TrySetResult(true);
				});

			QueueChangeForTest(watcher, Path.Combine(workspaceRoot, "test.ext"), FileChangeKind.Changed);

			for (int i = 0; i < 5; i++)
				await DispatchPendingChangesForTestAsync(watcher).ConfigureAwait(false);

			await ownerNotified.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

			Assert.IsNull(ownerDisposeFailure, ownerDisposeFailure?.ToString());
			Assert.IsTrue(watcher.IsDisposed);
			Assert.AreEqual(0, watcher.ActiveWatcherCount);
		}
		finally
		{
			if (watcher is not null)
				await watcher.DisposeAsync().ConfigureAwait(false);
		}
	}

	[TestMethod]
	public async Task DispatchPendingChangesForTestAsync_WhenOwnerRestartsTheWatcher_NotifiesAgainAfterANewFailureSequence()
	{
		using var workspace = new TemporaryDirectory("WorkspaceWatcherEscalationRestart_");
		string workspaceRoot = workspace.Path;
		WorkspaceWatchSpecification[] watchSpecifications = [new("*.ext", IncludeSubdirectories: true)];
		int notificationCount = 0;
		var secondNotification = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		await using var watcher = new WorkspaceFileWatcher(
			workspaceRoot,
			(_, _) => throw new IOException("Persistent dispatch failure."),
			watchSpecifications,
			(_, _) =>
			{
				if (Interlocked.Increment(ref notificationCount) >= 2)
					secondNotification.TrySetResult(true);
			});

		Assert.AreEqual(WorkspaceWatcherStartOutcome.Started, watcher.Start().Outcome);

		string filePath = Path.Combine(workspaceRoot, "test.ext");

		QueueChangeForTest(watcher, filePath, FileChangeKind.Changed);

		for (int i = 0; i < 3; i++)
			await DispatchPendingChangesForTestAsync(watcher).ConfigureAwait(false);

		await TestPolling.UntilAsync(() => Volatile.Read(ref notificationCount) >= 1, TimeSpan.FromSeconds(10)).ConfigureAwait(false);

		Assert.AreEqual(1, Volatile.Read(ref notificationCount));

		// Restarting the watcher re-arms the failure notification; the next failure sequence notifies again.
		Assert.AreEqual(WorkspaceWatcherStartOutcome.Started, watcher.Start().Outcome);

		QueueChangeForTest(watcher, filePath, FileChangeKind.Changed);

		for (int i = 0; i < 3; i++)
			await DispatchPendingChangesForTestAsync(watcher).ConfigureAwait(false);

		await secondNotification.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);

		Assert.IsTrue(Volatile.Read(ref notificationCount) >= 2);
	}

	/// <summary>
	/// Runs a file-system mutation and waits for its watcher effect, repeating both once when the first wait
	/// observes no event.
	/// </summary>
	/// <typeparam name="TResult">The effect the watcher is expected to dispatch.</typeparam>
	/// <param name="fileOperation">The file-system mutation that raises the watcher event.</param>
	/// <param name="waitForDispatch">Waits for the debounced dispatch that observes the effect.</param>
	/// <returns>The dispatched effect observed by the watcher.</returns>
	/// <remarks>
	/// <see cref="FileSystemWatcher"/> can drop a single event under load, and the operating system's delivery is
	/// the only timing input these tests have, so one fresh attempt re-observes the effect instead of failing the
	/// test red. The mutation is repeated verbatim, so it raises the same event kind on the retry.
	/// </remarks>
	private static async Task<TResult> RunFileOperationWithEventRetryAsync<TResult>(
		Action fileOperation,
		Func<Task<TResult>> waitForDispatch)
	{
		const int MaxAttempts = 2;

		for (int attempt = 1; ; attempt++)
		{
			fileOperation();

			try
			{
				return await waitForDispatch().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
			}
			catch (TimeoutException) when (attempt < MaxAttempts)
			{
				// The event was missed; redo the mutation and wait once more.
			}
		}
	}

	private static void QueueChangeForTest(WorkspaceFileWatcher watcher, string filePath, FileChangeKind changeKind)
		=> watcher.QueueChange(filePath, changeKind);

	private static Task DispatchPendingChangesForTestAsync(WorkspaceFileWatcher watcher)
		=> watcher.DispatchPendingChangesAsync();

	private static void ReportErrorForTest(WorkspaceFileWatcher watcher, Exception exception)
		=> watcher.HandleWatcherError(exception);

	private sealed class NonPumpingSynchronizationContext : SynchronizationContext
	{
		public override void Post(SendOrPostCallback d, object? state)
		{
			// Posted continuations are intentionally ignored.
		}

		public override void Send(SendOrPostCallback d, object? state)
			=> d(state);
	}
}
