using Microsoft.Extensions.Logging;

namespace Nickelony.IDEKit.FileWatching.Tests;

/// <summary>Covers the watcher lifecycle of one workspace root: start, failure recovery, reconciliation and disposal.</summary>
[TestClass]
public sealed class FileWatchSupervisorTests
{
	private static readonly FileSnapshotEntry s_entry = new(IsDirectory: false, LastWriteUtcTicks: 1, Length: 10, ContentFingerprint: 100);

	private sealed class TestHost : IDisposable
	{
		private readonly object _syncRoot = new();
		private readonly List<FileWatchFailure> _failures = [];
		private readonly List<DispatchCall> _dispatches = [];
		private readonly List<(string Root, IReadOnlyList<FileWatchSpecification> Specifications)> _trackerCreations = [];

		internal TestHost(string? root = null, bool isEnabled = true)
		{
			Root = root ?? TestPaths.Root;
			IsEnabled = isEnabled;

			var hooks = new FileWatchSupervisorHooks
			{
				IsEnabled = () => IsEnabled,
				IsDisposed = () => IsDisposed,

				ReportFailure = failure =>
				{
					lock (_syncRoot)
						_failures.Add(failure);
				},

				DispatchAsync = (supervisor, batch, cancellationToken) =>
				{
					lock (_syncRoot)
						_dispatches.Add(new DispatchCall(supervisor, batch, cancellationToken));

					return Task.CompletedTask;
				},

				ObserveBackgroundTask = static (task, _) => ObserveFault(task),
			};

			Supervisor = new FileWatchSupervisor(
				new FileWatchSupervisorSpecification(
					WorkspaceRootDirectoryPath: Root,
					DisplayName: "Test",
					WatchSpecifications: [new FileWatchSpecification("*.test", IncludeSubdirectories: true)],
					FileWatcherFactory: Watchers.Factory,

					FileSnapshotTrackerFactory: (root, specifications) =>
					{
						lock (_syncRoot)
							_trackerCreations.Add((root, specifications));

						return Tracker;
					}
				),

				hooks,
				Logger
			);
		}

		internal string Root { get; }

		private static void ObserveFault(Task task) => _ = task.ContinueWith(static completed => completed.Exception, TaskScheduler.Default);

		internal bool IsEnabled { get; set; }

		internal bool IsDisposed { get; set; }

		internal RecordingLogger Logger { get; } = new();

		internal RecordingFileWatcherFactory Watchers { get; } = new();

		internal RecordingFileSnapshotTracker Tracker { get; } = new();

		internal FileWatchSupervisor Supervisor { get; }

		internal IReadOnlyList<FileWatchFailure> Failures
		{
			get
			{
				lock (_syncRoot)
					return [.. _failures];
			}
		}

		internal IReadOnlyList<DispatchCall> Dispatches
		{
			get
			{
				lock (_syncRoot)
					return [.. _dispatches];
			}
		}

		internal IReadOnlyList<(string Root, IReadOnlyList<FileWatchSpecification> Specifications)> TrackerCreations
		{
			get
			{
				lock (_syncRoot)
					return [.. _trackerCreations];
			}
		}

		internal StubFileWatcher EnqueueWatcher(StubFileWatcher? watcher = null) => Watchers.Enqueue(Root, watcher ?? new StubFileWatcher());

		internal static StubFileWatcher FailingWatcher(FileWatcherStartOutcome outcome = FileWatcherStartOutcome.StartupFailed) =>
			new() { StartResult = new FileWatcherStartResult(outcome, new IOException("The watcher could not start.")) };

		public void Dispose()
		{
			Supervisor.Dispose();
			Logger.Dispose();
		}
	}

	private readonly record struct DispatchCall(FileWatchSupervisor Supervisor, FileChangeBatch Batch, CancellationToken CancellationToken);

	[TestMethod]
	public async Task EnsureWatcherStartedAsync_StartsOneWatcherForTheRootAndCapturesTheBaselineOnce()
	{
		using var fixture = new TestHost();

		StubFileWatcher watcher = fixture.EnqueueWatcher();

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);
		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		Assert.AreEqual(fixture.Root, Assert.ContainsSingle(fixture.Watchers.Creations).WorkspaceRootDirectoryPath);
		Assert.AreEqual(1, watcher.StartCount);
		Assert.IsTrue(fixture.Supervisor.IsWatcherActive);
		Assert.AreEqual(1, fixture.Tracker.CaptureCount);
	}

	[TestMethod]
	public async Task EnsureWatcherStartedAsync_BuildsTheTrackerForTheRootAndTheWatchSpecifications()
	{
		using var fixture = new TestHost();

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		(string root, IReadOnlyList<FileWatchSpecification> specifications) = Assert.ContainsSingle(fixture.TrackerCreations);

		Assert.AreEqual(fixture.Root, root);
		Assert.AreEqual(new FileWatchSpecification("*.test", IncludeSubdirectories: true), Assert.ContainsSingle(specifications));
	}

	[TestMethod]
	public async Task EnsureWatcherStartedAsync_WhenTheOwnerDisablesWatching_StartsNothing()
	{
		using var fixture = new TestHost(isEnabled: false);

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		Assert.IsEmpty(fixture.Watchers.Creations);
		Assert.IsFalse(fixture.Supervisor.IsWatcherActive);
		Assert.IsEmpty(fixture.TrackerCreations);
	}

	[TestMethod]
	public async Task EnsureWatcherStartedAsync_ForAnEmptyRoot_StartsNothing()
	{
		using var fixture = new TestHost(root: string.Empty);

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		Assert.IsEmpty(fixture.Watchers.Creations);
		Assert.IsFalse(fixture.Supervisor.IsWatcherActive);
	}

	[TestMethod]
	public async Task EnsureWatcherStartedAsync_AfterTheOwnerWasDisposed_StartsNothing()
	{
		using var fixture = new TestHost();

		fixture.IsDisposed = true;

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		Assert.IsEmpty(fixture.Watchers.Creations);
		Assert.IsFalse(fixture.Supervisor.IsWatcherActive);
	}

	[TestMethod]
	public async Task EnsureWatcherStartedAsync_WhenTheWatcherFailsToStart_ReportsTheFailureAndDisposesTheWatcher()
	{
		using var fixture = new TestHost();

		StubFileWatcher watcher = fixture.EnqueueWatcher(TestHost.FailingWatcher());

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		FileWatchFailure failure = Assert.ContainsSingle(fixture.Failures);

		Assert.AreEqual(fixture.Root, failure.WorkspaceRootDirectoryPath);
		Assert.AreEqual(FileWatchFailureCodes.StartFailed, failure.Code);

		RecordingLogger.LogEntry entry = Assert.ContainsSingle(fixture.Logger.Entries);

		Assert.AreEqual(LogLevel.Warning, entry.Level);
		Assert.AreEqual("The watcher could not start.", entry.ExceptionMessage);
		Assert.AreEqual(1, watcher.DisposeCount);
		Assert.IsFalse(fixture.Supervisor.IsWatcherActive);
		Assert.IsEmpty(fixture.TrackerCreations);
	}

	[TestMethod]
	public async Task EnsureWatcherStartedAsync_WhenTheWatcherFactoryThrows_ReportsTheFailureInsteadOfThrowing()
	{
		using var fixture = new TestHost();

		fixture.Watchers.EnqueueFailure(fixture.Root, new InvalidOperationException("The factory failed."));

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		Assert.AreEqual(FileWatchFailureCodes.StartFailed, Assert.ContainsSingle(fixture.Failures).Code);
		Assert.AreEqual("The factory failed.", Assert.ContainsSingle(fixture.Logger.Entries).ExceptionMessage);
		Assert.IsFalse(fixture.Supervisor.IsWatcherActive);
	}

	[TestMethod]
	public async Task EnsureWatcherStartedAsync_WhenTheRootIsMissing_StaysQuietAndDisposesTheWatcher()
	{
		using var fixture = new TestHost();

		StubFileWatcher watcher = fixture.EnqueueWatcher(TestHost.FailingWatcher(FileWatcherStartOutcome.WorkspaceRootMissing));

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		Assert.IsEmpty(fixture.Failures);
		Assert.IsEmpty(fixture.Logger.Entries);
		Assert.AreEqual(1, watcher.DisposeCount);
		Assert.IsFalse(fixture.Supervisor.IsWatcherActive);
	}

	[TestMethod]
	public async Task EnsureWatcherStartedAsync_ADisposeFailureOfTheUnstartedWatcher_IsContainedAndLoggedAtDebugLevel()
	{
		using var fixture = new TestHost();

		StubFileWatcher watcher = TestHost.FailingWatcher(FileWatcherStartOutcome.WorkspaceRootMissing);

		watcher.DisposeException = new IOException("The dispose failed.");
		fixture.EnqueueWatcher(watcher);

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		RecordingLogger.LogEntry entry = Assert.ContainsSingle(fixture.Logger.Entries);

		Assert.AreEqual(LogLevel.Debug, entry.Level);
		Assert.Contains("Failed to dispose an unstarted Test workspace file watcher.", entry.Message);
		Assert.AreEqual("The dispose failed.", entry.ExceptionMessage);
	}

	[TestMethod]
	public async Task EnsureWatcherStartedAsync_RepeatedStartupFailures_AreReportedOnce()
	{
		using var fixture = new TestHost();

		fixture.EnqueueWatcher(TestHost.FailingWatcher());
		fixture.EnqueueWatcher(TestHost.FailingWatcher());

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);
		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		Assert.HasCount(2, fixture.Watchers.Creations);
		Assert.ContainsSingle(fixture.Failures);
	}

	[TestMethod]
	public async Task EnsureWatcherStartedAsync_ASuccessfulStartRearmsTheFailureReport()
	{
		using var fixture = new TestHost();

		fixture.EnqueueWatcher(TestHost.FailingWatcher());

		StubFileWatcher started = fixture.EnqueueWatcher();

		fixture.EnqueueWatcher(TestHost.FailingWatcher());

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);
		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		Assert.ContainsSingle(fixture.Failures);

		fixture.Supervisor.HandleWatcherFailure(started, new IOException("The watcher died."));

		Assert.HasCount(2, fixture.Failures);
	}

	[TestMethod]
	public async Task EnsureWatcherStartedAsync_AFailureRaisedWhileStarting_IsAbsorbedAndReportedOnceByTheStartAttempt()
	{
		using var fixture = new TestHost();

		var watcher = StubFileWatcher.FailingAtStart(fixture.Supervisor.HandleWatcherFailure);

		fixture.EnqueueWatcher(watcher);
		fixture.EnqueueWatcher();

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		Assert.ContainsSingle(fixture.Watchers.Creations);
		Assert.ContainsSingle(fixture.Failures);
		Assert.Contains(entry => entry.Level == LogLevel.Debug && entry.Message.Contains("Absorbed"), fixture.Logger.Entries);
		Assert.IsFalse(fixture.Supervisor.IsWatcherActive);
	}

	[TestMethod]
	public async Task EnsureWatcherStartedAsync_CapturesTheBaselineOutsideTheSupervisorLock()
	{
		using var fixture = new TestHost();
		using var captureEntered = new ManualResetEventSlim();
		using var releaseCapture = new ManualResetEventSlim();

		fixture.Tracker.OnCapture = () =>
		{
			captureEntered.Set();
			releaseCapture.Wait(TestContext.CancellationToken);
		};

		Task ensure = fixture.Supervisor.EnsureWatcherStartedAsync();

		Assert.IsTrue(captureEntered.Wait(TestPolling.DefaultTimeout, TestContext.CancellationToken));

		bool isActive = await Task.Run(() => fixture.Supervisor.IsWatcherActive, TestContext.CancellationToken)
			.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken)
			.ConfigureAwait(false);

		releaseCapture.Set();

		await ensure.ConfigureAwait(false);

		Assert.IsTrue(isActive);
	}

	[TestMethod]
	public async Task Watcher_ForwardsItsBatchesToTheDispatchCallbackNamingTheSupervisor()
	{
		using var fixture = new TestHost();
		using var cancellation = new CancellationTokenSource();

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		var batch = new FileChangeBatch([new FileChange(TestPaths.Script("a.test"), FileChangeKind.Changed)]);

		await Assert.ContainsSingle(fixture.Watchers.Creations).DispatchAsync(batch, cancellation.Token).ConfigureAwait(false);

		DispatchCall call = Assert.ContainsSingle(fixture.Dispatches);

		Assert.AreSame(fixture.Supervisor, call.Supervisor);
		Assert.AreSame(batch, call.Batch);
		Assert.AreEqual(cancellation.Token, call.CancellationToken);
	}

	[TestMethod]
	public async Task HandleWatcherFailure_StartsAReplacementAndDisposesTheFailedWatcher()
	{
		using var fixture = new TestHost();

		StubFileWatcher failed = fixture.EnqueueWatcher();
		StubFileWatcher replacement = fixture.EnqueueWatcher();

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		fixture.Supervisor.HandleWatcherFailure(failed, new IOException("The watcher died."));

		Assert.HasCount(2, fixture.Watchers.Creations);
		Assert.AreEqual(1, replacement.StartCount);
		Assert.IsTrue(failed.IsDisposed);
		Assert.IsFalse(replacement.IsDisposed);
		Assert.IsTrue(fixture.Supervisor.IsWatcherActive);
		Assert.IsEmpty(fixture.Failures);

		Assert.Contains(entry => entry.Level == LogLevel.Warning && entry.ExceptionMessage == "The watcher died.", fixture.Logger.Entries);
		Assert.Contains(entry => entry.Level == LogLevel.Information && entry.Message.Contains("recovered successfully"), fixture.Logger.Entries);
	}

	[TestMethod]
	public async Task HandleWatcherFailure_ThroughTheCallbackTheFactoryReceived_RunsTheRecovery()
	{
		using var fixture = new TestHost();

		StubFileWatcher failed = fixture.EnqueueWatcher();

		fixture.EnqueueWatcher();

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		Assert.ContainsSingle(fixture.Watchers.Creations).OnWatcherFailed(failed, null);

		Assert.HasCount(2, fixture.Watchers.Creations);
		Assert.IsTrue(failed.IsDisposed);
	}

	[TestMethod]
	public async Task HandleWatcherFailure_ReplaysTheDeltaMissedWhileTheWatcherWasUnavailable()
	{
		using var fixture = new TestHost();

		string changedPath = TestPaths.Script("changed.test");
		string createdPath = TestPaths.Script("created.test");
		string deletedPath = TestPaths.Script("deleted.test");

		FileSnapshotEntry modified = s_entry with { LastWriteUtcTicks = 2 };

		fixture.Tracker.TrackedSnapshot = new Dictionary<string, FileSnapshotEntry> { [changedPath] = s_entry, [deletedPath] = s_entry };
		fixture.Tracker.CurrentSnapshot = new Dictionary<string, FileSnapshotEntry> { [changedPath] = modified, [createdPath] = modified };

		StubFileWatcher failed = fixture.EnqueueWatcher();

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		fixture.Supervisor.HandleWatcherFailure(failed, new IOException("The watcher died."));

		await TestPolling.UntilAsync(() => fixture.Dispatches.Count > 0, TestPolling.DefaultTimeout).ConfigureAwait(false);

		DispatchCall call = Assert.ContainsSingle(fixture.Dispatches);

		Assert.AreSame(fixture.Supervisor, call.Supervisor);
		Assert.AreEqual(CancellationToken.None, call.CancellationToken);

		Assert.AreSequenceEqual(
			[
				new FileChange(changedPath, FileChangeKind.Changed),
				new FileChange(createdPath, FileChangeKind.Created),
				new FileChange(deletedPath, FileChangeKind.Deleted),
			],

			[.. call.Batch.Entries]
		);

		Assert.AreEqual(1, fixture.Tracker.CloneCount);
		Assert.AreEqual(1, fixture.Tracker.ReplaceCount);
	}

	[TestMethod]
	public async Task RescanAsync_DispatchesWhatTheTrackedSnapshotLacksAndKeepsTheWatcher()
	{
		using var fixture = new TestHost();

		string changedPath = TestPaths.Script("changed.test");
		string createdPath = TestPaths.Script("created.test");

		fixture.Tracker.TrackedSnapshot = new Dictionary<string, FileSnapshotEntry> { [changedPath] = s_entry };

		fixture.Tracker.CurrentSnapshot = new Dictionary<string, FileSnapshotEntry>
		{
			[changedPath] = s_entry with { LastWriteUtcTicks = 2 },
			[createdPath] = s_entry,
		};

		StubFileWatcher watcher = fixture.EnqueueWatcher();

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);
		await fixture.Supervisor.RescanAsync().ConfigureAwait(false);

		DispatchCall call = Assert.ContainsSingle(fixture.Dispatches);

		Assert.AreSame(fixture.Supervisor, call.Supervisor);

		Assert.AreSequenceEqual(
			[new FileChange(changedPath, FileChangeKind.Changed), new FileChange(createdPath, FileChangeKind.Created)],
			[.. call.Batch.Entries]
		);

		Assert.AreEqual(1, fixture.Tracker.ReplaceCount);
		Assert.IsTrue(fixture.Supervisor.IsWatcherActive);
		Assert.AreEqual(0, watcher.DisposeCount);
	}

	[TestMethod]
	public async Task RescanAsync_WithoutARunningWatcher_DoesNothing()
	{
		using var fixture = new TestHost();

		fixture.Tracker.CurrentSnapshot = new Dictionary<string, FileSnapshotEntry> { [TestPaths.Script("created.test")] = s_entry };

		await fixture.Supervisor.RescanAsync().ConfigureAwait(false);

		Assert.IsEmpty(fixture.Dispatches);
		Assert.AreEqual(0, fixture.Tracker.CloneCount);
		Assert.AreEqual(0, fixture.Tracker.ReplaceCount);
	}

	[TestMethod]
	public async Task HandleWatcherFailure_DecidesADeletionThroughTheTracker_NotTheFileSystem()
	{
		using var fixture = new TestHost();
		using var workspace = new TemporaryDirectory("WatchSupervisorDelta_");

		// The file exists on the real disk, but the scripted file system of the tracker lists no path, so only the tracker can call it deleted.
		string stillOnDisk = LocalPath.Normalize(Path.Combine(workspace.Path, "still-there.test"));
		File.WriteAllText(stillOnDisk, "return 1");

		fixture.Tracker.TrackedSnapshot = new Dictionary<string, FileSnapshotEntry> { [stillOnDisk] = s_entry };
		fixture.Tracker.CurrentSnapshot = new Dictionary<string, FileSnapshotEntry>();

		StubFileWatcher failed = fixture.EnqueueWatcher();

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		fixture.Supervisor.HandleWatcherFailure(failed, new IOException("The watcher died."));

		await TestPolling.UntilAsync(() => fixture.Dispatches.Count > 0, TestPolling.DefaultTimeout).ConfigureAwait(false);

		DispatchCall call = Assert.ContainsSingle(fixture.Dispatches);

		Assert.AreSequenceEqual([new FileChange(stillOnDisk, FileChangeKind.Deleted)], [.. call.Batch.Entries]);
	}

	[TestMethod]
	public async Task HandleWatcherFailure_WhenTheTrackerSaysAMissingPathStillExists_ReplaysNoDeletion()
	{
		using var fixture = new TestHost();

		string path = TestPaths.Script("skipped.test");

		fixture.Tracker.TrackedSnapshot = new Dictionary<string, FileSnapshotEntry> { [path] = s_entry };
		fixture.Tracker.CurrentSnapshot = new Dictionary<string, FileSnapshotEntry>();
		fixture.Tracker.ExistingPaths.Add(path);

		StubFileWatcher failed = fixture.EnqueueWatcher();

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		fixture.Supervisor.HandleWatcherFailure(failed, new IOException("The watcher died."));

		await TestPolling.UntilAsync(() => fixture.Tracker.ReplaceCount == 1, TestPolling.DefaultTimeout).ConfigureAwait(false);
		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsEmpty(fixture.Dispatches);
	}

	[TestMethod]
	public async Task HandleWatcherFailure_WhenNothingChangedWhileTheWatcherWasUnavailable_ReplaysNothing()
	{
		using var fixture = new TestHost();

		string path = TestPaths.Script("same.test");

		fixture.Tracker.TrackedSnapshot = new Dictionary<string, FileSnapshotEntry> { [path] = s_entry };
		fixture.Tracker.CurrentSnapshot = new Dictionary<string, FileSnapshotEntry> { [path] = s_entry };

		StubFileWatcher failed = fixture.EnqueueWatcher();

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		fixture.Supervisor.HandleWatcherFailure(failed, new IOException("The watcher died."));

		await TestPolling.UntilAsync(() => fixture.Tracker.ReplaceCount == 1, TestPolling.DefaultTimeout).ConfigureAwait(false);
		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsEmpty(fixture.Dispatches);
	}

	[TestMethod]
	public async Task HandleWatcherFailure_ReturnsBeforeTheRecoveryCaptureFinishes()
	{
		using var fixture = new TestHost();
		using var captureEntered = new ManualResetEventSlim();
		using var releaseCapture = new ManualResetEventSlim();

		bool captureFinished = false;

		fixture.Tracker.OnReplace = () =>
		{
			captureEntered.Set();
			releaseCapture.Wait(TestPolling.DefaultTimeout, TestContext.CancellationToken);
			Volatile.Write(ref captureFinished, true);
		};

		StubFileWatcher failed = fixture.EnqueueWatcher();

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		fixture.Supervisor.HandleWatcherFailure(failed, new IOException("The watcher died."));

		Assert.IsFalse(Volatile.Read(ref captureFinished));
		Assert.IsTrue(captureEntered.Wait(TestPolling.DefaultTimeout, TestContext.CancellationToken));
		Assert.IsTrue(fixture.Supervisor.IsWatcherActive);

		releaseCapture.Set();

		await TestPolling.UntilAsync(() => Volatile.Read(ref captureFinished), TestPolling.DefaultTimeout).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task HandleWatcherFailure_FromAWatcherThatWasAlreadyReplaced_IsIgnored()
	{
		using var fixture = new TestHost();

		StubFileWatcher failed = fixture.EnqueueWatcher();
		StubFileWatcher replacement = fixture.EnqueueWatcher();

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		fixture.Supervisor.HandleWatcherFailure(failed, new IOException("The watcher died."));
		fixture.Supervisor.HandleWatcherFailure(failed, new IOException("The watcher died again."));

		Assert.HasCount(2, fixture.Watchers.Creations);
		Assert.IsFalse(replacement.IsDisposed);
		Assert.IsTrue(fixture.Supervisor.IsWatcherActive);
		Assert.Contains(entry => entry.Level == LogLevel.Debug && entry.Message.Contains("stale"), fixture.Logger.Entries);
	}

	[TestMethod]
	public async Task HandleWatcherFailure_AfterTheOwnerWasDisposed_DoesNothing()
	{
		using var fixture = new TestHost();

		StubFileWatcher watcher = fixture.EnqueueWatcher();

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		fixture.IsDisposed = true;
		fixture.Supervisor.HandleWatcherFailure(watcher, new IOException("The watcher died."));

		Assert.ContainsSingle(fixture.Watchers.Creations);
		Assert.IsFalse(watcher.IsDisposed);
		Assert.IsEmpty(fixture.Logger.Entries);
		Assert.IsEmpty(fixture.Failures);
	}

	[TestMethod]
	public async Task HandleWatcherFailure_WhenTheReplacementCannotStart_ReportsTheFailureOnce()
	{
		using var fixture = new TestHost();

		StubFileWatcher failed = fixture.EnqueueWatcher();
		StubFileWatcher firstReplacement = fixture.EnqueueWatcher(TestHost.FailingWatcher());

		fixture.EnqueueWatcher(TestHost.FailingWatcher());

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		fixture.Supervisor.HandleWatcherFailure(failed, new IOException("The watcher died."));

		FileWatchFailure failure = Assert.ContainsSingle(fixture.Failures);

		Assert.AreEqual(fixture.Root, failure.WorkspaceRootDirectoryPath);
		Assert.AreEqual(FileWatchFailureCodes.RecoveryFailed, failure.Code);
		Assert.IsTrue(firstReplacement.IsDisposed);
		Assert.IsFalse(fixture.Supervisor.IsWatcherActive);
		Assert.Contains(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("could not be restarted"), fixture.Logger.Entries);

		// A later failure callback finds no active watcher, so it retries the replacement without reporting the root a second time.
		fixture.Supervisor.HandleWatcherFailure(failed, new IOException("The watcher died again."));

		Assert.HasCount(3, fixture.Watchers.Creations);
		Assert.ContainsSingle(fixture.Failures);
	}

	[TestMethod]
	public async Task HandleWatcherFailure_WhenTheWatcherFactoryThrowsDuringRecovery_ReportsTheFailure()
	{
		using var fixture = new TestHost();

		StubFileWatcher failed = fixture.EnqueueWatcher();

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		fixture.Watchers.EnqueueFailure(fixture.Root, new InvalidOperationException("The factory failed."));
		fixture.Supervisor.HandleWatcherFailure(failed, new IOException("The watcher died."));

		Assert.AreEqual(FileWatchFailureCodes.RecoveryFailed, Assert.ContainsSingle(fixture.Failures).Code);
		Assert.IsTrue(failed.IsDisposed);
		Assert.IsFalse(fixture.Supervisor.IsWatcherActive);
		Assert.Contains(entry => entry.ExceptionMessage == "The factory failed.", fixture.Logger.Entries);
	}

	[TestMethod]
	public async Task HandleWatcherFailure_WhenTheRootIsMissing_ReportsNothingAndStaysUnwatched()
	{
		using var fixture = new TestHost();

		StubFileWatcher failed = fixture.EnqueueWatcher();
		StubFileWatcher replacement = fixture.EnqueueWatcher(TestHost.FailingWatcher(FileWatcherStartOutcome.WorkspaceRootMissing));

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		fixture.Supervisor.HandleWatcherFailure(failed, new IOException("The watcher died."));

		Assert.IsEmpty(fixture.Failures);
		Assert.IsTrue(replacement.IsDisposed);
		Assert.IsFalse(fixture.Supervisor.IsWatcherActive);
		Assert.Contains(entry => entry.Level == LogLevel.Information && entry.Message.Contains("remains unavailable"), fixture.Logger.Entries);
	}

	[TestMethod]
	public async Task HandleWatcherFailure_AFailureRaisedWhileTheReplacementStarts_IsAbsorbedWithoutANestedRecovery()
	{
		using var fixture = new TestHost();

		StubFileWatcher failed = fixture.EnqueueWatcher();

		var replacement = StubFileWatcher.FailingAtStart(fixture.Supervisor.HandleWatcherFailure);

		fixture.EnqueueWatcher(replacement);
		fixture.EnqueueWatcher();

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		fixture.Supervisor.HandleWatcherFailure(failed, new IOException("The watcher died."));

		Assert.HasCount(2, fixture.Watchers.Creations);
		Assert.ContainsSingle(fixture.Failures);
		Assert.IsTrue(replacement.IsDisposed);
		Assert.IsFalse(fixture.Supervisor.IsWatcherActive);
	}

	[TestMethod]
	public async Task HandleWatcherFailure_ASuccessfulRecoveryRearmsTheFailureReport()
	{
		using var fixture = new TestHost();

		StubFileWatcher first = fixture.EnqueueWatcher();

		fixture.EnqueueWatcher(TestHost.FailingWatcher());

		StubFileWatcher recovered = fixture.EnqueueWatcher();

		fixture.EnqueueWatcher(TestHost.FailingWatcher());

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		fixture.Supervisor.HandleWatcherFailure(first, new IOException("The watcher died."));

		Assert.ContainsSingle(fixture.Failures);

		fixture.Supervisor.HandleWatcherFailure(first, new IOException("The watcher died again."));

		Assert.IsTrue(fixture.Supervisor.IsWatcherActive);
		Assert.ContainsSingle(fixture.Failures);

		fixture.Supervisor.HandleWatcherFailure(recovered, new IOException("The recovered watcher died."));

		Assert.HasCount(2, fixture.Failures);
	}

	[TestMethod]
	public void ApplyTrackedChanges_HandsTheChangesToTheSnapshotTracker()
	{
		using var fixture = new TestHost();

		FileChange[] changes = [new(TestPaths.Script("a.test"), FileChangeKind.Changed), new(TestPaths.Script("b.test"), FileChangeKind.Deleted)];

		fixture.Supervisor.ApplyTrackedChanges(changes);

		Assert.AreEqual(1, fixture.Tracker.ApplyCount);
		Assert.AreSequenceEqual(changes, [.. fixture.Tracker.AppliedChanges]);
	}

	[TestMethod]
	public void Constructor_DoesNotCreateTheSnapshotTracker()
	{
		using var fixture = new TestHost();

		Assert.IsFalse(fixture.Supervisor.IsWatcherActive);

		fixture.Supervisor.Dispose();

		Assert.IsEmpty(fixture.TrackerCreations);
	}

	[TestMethod]
	public async Task Dispose_DisposesTheActiveWatcherOnceAndDeactivatesTheSupervisor()
	{
		using var fixture = new TestHost();

		StubFileWatcher watcher = fixture.EnqueueWatcher();

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		fixture.Supervisor.Dispose();
		fixture.Supervisor.Dispose();

		Assert.AreEqual(1, watcher.DisposeCount);
		Assert.IsFalse(fixture.Supervisor.IsWatcherActive);
	}

	[TestMethod]
	public async Task Dispose_AWatcherDisposeFailure_IsContainedAndLoggedAtDebugLevel()
	{
		using var fixture = new TestHost();

		StubFileWatcher watcher = fixture.EnqueueWatcher(new StubFileWatcher { DisposeException = new IOException("The dispose failed.") });

		await fixture.Supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		fixture.Supervisor.Dispose();

		RecordingLogger.LogEntry entry = Assert.ContainsSingle(fixture.Logger.Entries);

		Assert.AreEqual(1, watcher.DisposeCount);
		Assert.AreEqual(LogLevel.Debug, entry.Level);
		Assert.Contains("Failed to dispose the Test workspace file watcher.", entry.Message);
		Assert.AreEqual("The dispose failed.", entry.ExceptionMessage);
	}

	public required TestContext TestContext { get; set; }
}
