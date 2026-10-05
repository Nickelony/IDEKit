using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers how forwarded workspace changes are routed to the root that owns each path, forwarded, buffered and applied to the tracked snapshots.
/// </summary>
[TestClass]
public sealed class WorkspaceChangeDispatcherTests
{
	private const string WatchedFiles = "workspace/didChangeWatchedFiles";

	private static readonly string s_rootA = TestPaths.Root;
	private static readonly string s_rootB = TestPaths.SecondaryRoot;
	private static readonly string s_nestedRoot = Path.Combine(TestPaths.Root, "Nested");

	private sealed class TestHost : IDisposable
	{
		private readonly object _syncRoot = new();
		private readonly List<long> _unhealthyGenerations = [];
		private readonly Dictionary<string, RecordingFileSnapshotTracker> _trackers = new(LocalPathComparisonPolicy.ForCurrentPlatform.Comparer);

		internal TestHost(IReadOnlyList<string>? roots = null, bool hasWatchSpecifications = true)
		{
			var owner = new WorkspaceChangeOwnerContext(
				ClientAccessor: () => HasClient ? Client : null,
				Disposal,
				EnsureConnectionStartedAsync: cancellationToken => EnsureConnectionStarted(cancellationToken),

				MarkConnectionUnhealthy: generation =>
				{
					lock (_syncRoot)
						_unhealthyGenerations.Add(generation);
				},

				RaiseWorkspaceFileWatchFailed: static _ => { }
			);

			IReadOnlyList<string> workspaceRoots = roots ?? [s_rootA];

			Dispatcher = new WorkspaceChangeDispatcher(
				new WorkspaceChangeDispatcherContext(
					WorkspaceRootDirectoryPaths: workspaceRoots,
					WorkspaceRootsDisplayText: string.Join(", ", workspaceRoots),
					ProviderDisplayName: "Test",
					WatchSpecifications: hasWatchSpecifications ? [new FileWatchSpecification("*.test", IncludeSubdirectories: true)] : [],
					FileWatcherFactory: Watchers.Factory,
					FileSnapshotTrackerFactory: (root, _) => TrackerFor(root),
					Sender: new WorkspaceChangeSender(owner, isConfigurationPath: static _ => false, providerDisplayName: "Test", Logger),
					owner,
					ObserveBackgroundTask: TestProviderContext.Create(client: null, Logger).ObserveBackgroundTask,
					Options: LanguageServerFeatureProviderOptions.Default,
					Logger
				)
			);
		}

		internal FakeLanguageServerClient Client { get; } = new() { ConnectionGeneration = 1 };

		internal bool HasClient { get; set; } = true;

		internal Func<CancellationToken, Task<bool>> EnsureConnectionStarted { get; set; } = static _ => Task.FromResult(true);

		internal ProviderLifetime Disposal { get; } = new();

		internal RecordingLogger Logger { get; } = new();

		internal RecordingFileWatcherFactory Watchers { get; } = new();

		internal WorkspaceChangeDispatcher Dispatcher { get; }

		internal int ForwardedBatchCount => Client.GetSentMethodCount(WatchedFiles);

		internal IReadOnlyList<long> UnhealthyGenerations
		{
			get
			{
				lock (_syncRoot)
					return [.. _unhealthyGenerations];
			}
		}

		internal RecordingFileSnapshotTracker TrackerFor(string root)
		{
			lock (_syncRoot)
			{
				if (!_trackers.TryGetValue(root, out RecordingFileSnapshotTracker? tracker))
				{
					tracker = new RecordingFileSnapshotTracker();
					_trackers.Add(root, tracker);
				}

				return tracker;
			}
		}

		/// <summary>
		/// Delivers a batch the way the watcher of one root does, through the dispatch delegate its supervisor handed to the factory.
		/// </summary>
		internal Task DeliverAsync(string root, params FileChange[] changes)
		{
			var batch = new FileChangeBatch(changes);
			return Watchers.CreationsFor(root)[0].DispatchAsync(batch, CancellationToken.None);
		}

		/// <summary>Returns the URIs of the most recent watched-files notification.</summary>
		internal IReadOnlyList<string?> ForwardedUris()
		{
			JsonElement changes = Client.GetLastNotificationParameters(WatchedFiles).GetProperty("changes");
			return [.. changes.EnumerateArray().Select(change => change.GetProperty("uri").GetString())];
		}

		public void Dispose()
		{
			Dispatcher.Dispose();
			Client.Dispose();
			Logger.Dispose();
		}
	}

	private static FileChange Change(string root, string fileName, FileChangeKind kind = FileChangeKind.Changed) =>
		new(Path.Combine(root, fileName), kind);

	private static string Uri(FileChange change) => DocumentUriConversion.CreateFileUri(change.Path);

	[TestMethod]
	public async Task EnsureWorkspaceFileWatcherStartedAsync_StartsTheWatcherOfEveryRootInOrderAndCapturesEachBaseline()
	{
		using var fixture = new TestHost([s_rootA, s_rootB]);

		await fixture.Dispatcher.EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);

		Assert.AreSequenceEqual([s_rootA, s_rootB], [.. fixture.Watchers.Creations.Select(creation => creation.WorkspaceRootDirectoryPath)]);
		Assert.AreEqual(1, fixture.TrackerFor(s_rootA).CaptureCount);
		Assert.AreEqual(1, fixture.TrackerFor(s_rootB).CaptureCount);
	}

	[TestMethod]
	public async Task EnsureWorkspaceFileWatcherStartedAsync_WithoutWatchSpecifications_CreatesNoWatcherOrTracker()
	{
		using var fixture = new TestHost([s_rootA, s_rootB], hasWatchSpecifications: false);

		await fixture.Dispatcher.EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);

		Assert.IsEmpty(fixture.Watchers.Creations);
	}

	[TestMethod]
	public async Task DispatchWorkspaceChangesAsync_ForwardsAChangeOfTheDeliveringRootAndAppliesItToItsTracker()
	{
		using var fixture = new TestHost();

		FileChange change = Change(s_rootA, "a.test", FileChangeKind.Created);

		await fixture.Dispatcher.EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);
		await fixture.DeliverAsync(s_rootA, change).ConfigureAwait(false);

		Assert.AreEqual(1, fixture.ForwardedBatchCount);
		Assert.AreSequenceEqual([Uri(change)], [.. fixture.ForwardedUris()]);
		Assert.AreSequenceEqual([change], [.. fixture.TrackerFor(s_rootA).AppliedChanges]);
	}

	[TestMethod]
	public async Task DispatchWorkspaceChangesAsync_DropsAPathOwnedByAnotherActiveRoot()
	{
		using var fixture = new TestHost([s_rootA, s_rootB]);

		FileChange own = Change(s_rootA, "own.test");
		FileChange foreign = Change(s_rootB, "foreign.test");

		await fixture.Dispatcher.EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);
		await fixture.DeliverAsync(s_rootA, own, foreign).ConfigureAwait(false);

		Assert.AreSequenceEqual([Uri(own)], [.. fixture.ForwardedUris()]);
		Assert.AreSequenceEqual([own], [.. fixture.TrackerFor(s_rootA).AppliedChanges]);
		Assert.AreEqual(0, fixture.TrackerFor(s_rootB).ApplyCount);
	}

	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public async Task DispatchWorkspaceChangesAsync_ForNestedRoots_ForwardsThePathOnlyThroughTheNestedRootsOwnWatcher(bool nestedRootFirst)
	{
		using var fixture = new TestHost(nestedRootFirst ? [s_nestedRoot, s_rootA] : [s_rootA, s_nestedRoot]);

		FileChange nestedChange = Change(s_nestedRoot, "nested.test");

		await fixture.Dispatcher.EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);
		await fixture.DeliverAsync(s_rootA, nestedChange).ConfigureAwait(false);

		Assert.AreEqual(0, fixture.ForwardedBatchCount);

		await fixture.DeliverAsync(s_nestedRoot, nestedChange).ConfigureAwait(false);

		Assert.AreEqual(1, fixture.ForwardedBatchCount);
		Assert.AreSequenceEqual([nestedChange], [.. fixture.TrackerFor(s_nestedRoot).AppliedChanges]);
		Assert.AreEqual(0, fixture.TrackerFor(s_rootA).ApplyCount);
	}

	[TestMethod]
	public async Task DispatchWorkspaceChangesAsync_ForwardsAPathOfANestedRootWhoseWatcherIsInactive()
	{
		using var fixture = new TestHost([s_rootA, s_nestedRoot]);

		FileChange nestedChange = Change(s_nestedRoot, "nested.test");

		fixture.Watchers.Enqueue(s_rootA, new StubFileWatcher());

		fixture.Watchers.Enqueue(
			s_nestedRoot,
			new StubFileWatcher { StartResult = new FileWatcherStartResult(FileWatcherStartOutcome.StartupFailed, null) }
		);

		await fixture.Dispatcher.EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);
		await fixture.DeliverAsync(s_rootA, nestedChange).ConfigureAwait(false);

		Assert.AreSequenceEqual([Uri(nestedChange)], [.. fixture.ForwardedUris()]);
		Assert.AreSequenceEqual([nestedChange], [.. fixture.TrackerFor(s_nestedRoot).AppliedChanges]);
		Assert.AreEqual(0, fixture.TrackerFor(s_rootA).ApplyCount);
	}

	[TestMethod]
	public async Task DispatchWorkspaceChangesAsync_ForwardsAPathOutsideEveryRootAsDeliveredWithoutTrackingIt()
	{
		using var fixture = new TestHost();

		FileChange outside = Change(s_rootB, "outside.test");

		await fixture.Dispatcher.EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);
		await fixture.DeliverAsync(s_rootA, outside).ConfigureAwait(false);

		Assert.AreSequenceEqual([Uri(outside)], [.. fixture.ForwardedUris()]);
		Assert.AreEqual(0, fixture.TrackerFor(s_rootA).ApplyCount);
	}

	[TestMethod]
	public async Task DispatchWorkspaceChangesAsync_WithoutAClient_ForwardsNothing()
	{
		using var fixture = new TestHost();

		await fixture.Dispatcher.EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);

		fixture.HasClient = false;

		await fixture.DeliverAsync(s_rootA, Change(s_rootA, "a.test")).ConfigureAwait(false);

		Assert.AreEqual(0, fixture.ForwardedBatchCount);
		Assert.AreEqual(0, fixture.TrackerFor(s_rootA).ApplyCount);
	}

	[TestMethod]
	public async Task DispatchWorkspaceChangesAsync_AfterTheOwnerWasDisposed_ForwardsNothing()
	{
		using var fixture = new TestHost();

		await fixture.Dispatcher.EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);

		fixture.Disposal.Signal();

		await fixture.DeliverAsync(s_rootA, Change(s_rootA, "a.test")).ConfigureAwait(false);

		Assert.AreEqual(0, fixture.ForwardedBatchCount);
		Assert.AreEqual(0, fixture.TrackerFor(s_rootA).ApplyCount);
	}

	[TestMethod]
	public async Task DispatchWorkspaceChangesAsync_WithAnEmptyBatch_ForwardsNothing()
	{
		using var fixture = new TestHost();

		await fixture.Dispatcher.EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);
		await fixture.DeliverAsync(s_rootA).ConfigureAwait(false);

		Assert.AreEqual(0, fixture.ForwardedBatchCount);
	}

	[TestMethod]
	public async Task DispatchWorkspaceChangesAsync_AfterARecoverableConnectionFailure_BuffersTheBatchAndReplaysItOnRequest()
	{
		using var fixture = new TestHost();

		FileChange change = Change(s_rootA, "a.test");

		fixture.Client.SendNotificationHandler = static (_, _, _) => Task.FromException(new IOException("The connection is down."));

		await fixture.Dispatcher.EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);
		await fixture.DeliverAsync(s_rootA, change).ConfigureAwait(false);

		Assert.AreEqual(0, fixture.ForwardedBatchCount);
		Assert.AreEqual(0, fixture.TrackerFor(s_rootA).ApplyCount);
		Assert.AreSequenceEqual([1L], [.. fixture.UnhealthyGenerations]);

		Assert.Contains(
			entry => entry.Level == LogLevel.Debug && entry.Message.Contains("the batch was buffered for replay"),
			fixture.Logger.Entries
		);

		fixture.Client.SendNotificationHandler = null;

		await fixture.Dispatcher.ReplayBufferedWorkspaceChangesAsync(CancellationToken.None).ConfigureAwait(false);

		Assert.AreSequenceEqual([Uri(change)], [.. fixture.ForwardedUris()]);
		Assert.AreSequenceEqual([change], [.. fixture.TrackerFor(s_rootA).AppliedChanges]);
	}

	[TestMethod]
	public async Task DispatchWorkspaceChangesAsync_AfterAnUnexpectedFailure_DropsTheBatchAndLogsAWarning()
	{
		using var fixture = new TestHost();

		FileChange change = Change(s_rootA, "a.test");

		fixture.Client.SendNotificationHandler = static (_, _, _) => Task.FromException(new InvalidOperationException("The protocol was violated."));

		await fixture.Dispatcher.EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);
		await fixture.DeliverAsync(s_rootA, change).ConfigureAwait(false);

		fixture.Client.SendNotificationHandler = null;

		await fixture.Dispatcher.ReplayBufferedWorkspaceChangesAsync(CancellationToken.None).ConfigureAwait(false);

		RecordingLogger.LogEntry warning = Assert.ContainsSingle(entry => entry.Level == LogLevel.Warning, fixture.Logger.Entries);

		Assert.Contains("Dropped 1 Test workspace change(s)", warning.Message);
		Assert.Contains(change.Path, warning.Message);
		Assert.AreEqual("The protocol was violated.", warning.ExceptionMessage);
		Assert.AreEqual(0, fixture.ForwardedBatchCount);
		Assert.AreEqual(0, fixture.TrackerFor(s_rootA).ApplyCount);
	}

	[TestMethod]
	public async Task DispatchWorkspaceChangesAsync_WhenTheConnectionCannotStart_BuffersTheBatchForReplay()
	{
		using var fixture = new TestHost();

		FileChange change = Change(s_rootA, "a.test");

		fixture.EnsureConnectionStarted = static _ => Task.FromResult(false);

		await fixture.Dispatcher.EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);
		await fixture.DeliverAsync(s_rootA, change).ConfigureAwait(false);

		Assert.AreEqual(0, fixture.ForwardedBatchCount);

		fixture.EnsureConnectionStarted = static _ => Task.FromResult(true);

		await fixture.Dispatcher.ReplayBufferedWorkspaceChangesAsync(CancellationToken.None).ConfigureAwait(false);

		Assert.AreSequenceEqual([Uri(change)], [.. fixture.ForwardedUris()]);
		Assert.AreSequenceEqual([change], [.. fixture.TrackerFor(s_rootA).AppliedChanges]);
	}

	[TestMethod]
	public async Task ReplayBufferedWorkspaceChangesAsync_WithoutAClient_KeepsTheBufferedChanges()
	{
		using var fixture = new TestHost();

		FileChange change = Change(s_rootA, "a.test");

		fixture.EnsureConnectionStarted = static _ => Task.FromResult(false);

		await fixture.Dispatcher.EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);
		await fixture.DeliverAsync(s_rootA, change).ConfigureAwait(false);

		fixture.HasClient = false;

		await fixture.Dispatcher.ReplayBufferedWorkspaceChangesAsync(CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, fixture.ForwardedBatchCount);

		fixture.HasClient = true;

		await fixture.Dispatcher.ReplayBufferedWorkspaceChangesAsync(CancellationToken.None).ConfigureAwait(false);

		Assert.AreSequenceEqual([Uri(change)], [.. fixture.ForwardedUris()]);
	}

	[TestMethod]
	public async Task ReplayBufferedWorkspaceChangesAsync_AppliesTheReplayedChangesToTheirOwningRoots()
	{
		using var fixture = new TestHost([s_rootA, s_rootB]);

		FileChange inA = Change(s_rootA, "a.test");
		FileChange inB = Change(s_rootB, "b.test", FileChangeKind.Deleted);

		fixture.EnsureConnectionStarted = static _ => Task.FromResult(false);

		await fixture.Dispatcher.EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);
		await fixture.DeliverAsync(s_rootA, inA).ConfigureAwait(false);
		await fixture.DeliverAsync(s_rootB, inB).ConfigureAwait(false);

		fixture.EnsureConnectionStarted = static _ => Task.FromResult(true);

		await fixture.Dispatcher.ReplayBufferedWorkspaceChangesAsync(CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(1, fixture.ForwardedBatchCount);
		Assert.AreSequenceEqual([Uri(inA), Uri(inB)], [.. fixture.ForwardedUris()]);
		Assert.AreSequenceEqual([inA], [.. fixture.TrackerFor(s_rootA).AppliedChanges]);
		Assert.AreSequenceEqual([inB], [.. fixture.TrackerFor(s_rootB).AppliedChanges]);
	}

	[TestMethod]
	public async Task ReplayBufferedWorkspaceChangesAsync_AfterTheBufferPassedItsLimit_RescansTheRootsOfTheCollapsedChanges()
	{
		using var fixture = new TestHost([s_rootA, s_rootB]);

		FileChange inB = Change(s_rootB, "b.test");
		FileChange[] inA = [.. Enumerable.Range(0, WorkspaceChangeForwarder.DefaultBufferedPathLimit).Select(i => Change(s_rootA, $"a{i}.test"))];
		FileSnapshotEntry entry = new(IsDirectory: false, LastWriteUtcTicks: 1, Length: 10, ContentFingerprint: 100);

		fixture.EnsureConnectionStarted = static _ => Task.FromResult(false);

		await fixture.Dispatcher.EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);
		await fixture.DeliverAsync(s_rootB, inB).ConfigureAwait(false);
		await fixture.DeliverAsync(s_rootA, inA).ConfigureAwait(false);

		// The rescan finds what each root's tracked snapshot lacks; here one file per root stands for the collapsed changes.
		FileChange rescannedA = Change(s_rootA, "a0.test", FileChangeKind.Created);
		FileChange rescannedB = Change(s_rootB, "b.test", FileChangeKind.Created);

		fixture.TrackerFor(s_rootA).CurrentSnapshot = new Dictionary<string, FileSnapshotEntry> { [rescannedA.Path] = entry };
		fixture.TrackerFor(s_rootB).CurrentSnapshot = new Dictionary<string, FileSnapshotEntry> { [rescannedB.Path] = entry };
		fixture.EnsureConnectionStarted = static _ => Task.FromResult(true);

		await fixture.Dispatcher.ReplayBufferedWorkspaceChangesAsync(CancellationToken.None).ConfigureAwait(false);
		Assert.IsTrue(await fixture.Client.WaitForMethodCountAsync(WatchedFiles, 2, TestPolling.DefaultTimeout).ConfigureAwait(false));

		Assert.AreEqual(1, fixture.TrackerFor(s_rootA).ReplaceCount);
		Assert.AreEqual(1, fixture.TrackerFor(s_rootB).ReplaceCount);
		Assert.AreEqual(2, fixture.ForwardedBatchCount, "Only the two rescans reach the server; the collapsed buffer replays nothing.");
		Assert.Contains(message => message.Contains("into a rescan of their roots", StringComparison.Ordinal), fixture.Logger.Messages);
	}

	[TestMethod]
	public async Task ReplayBufferedWorkspaceChangesAsync_WithoutBufferedChanges_ForwardsNothing()
	{
		using var fixture = new TestHost();

		await fixture.Dispatcher.EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);
		await fixture.Dispatcher.ReplayBufferedWorkspaceChangesAsync(CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(0, fixture.ForwardedBatchCount);
	}

	[TestMethod]
	public async Task Dispose_DisposesTheWatcherOfEveryRootAndStopsForwarding()
	{
		using var fixture = new TestHost([s_rootA, s_rootB]);

		StubFileWatcher watcherA = fixture.Watchers.Enqueue(s_rootA, new StubFileWatcher());
		StubFileWatcher watcherB = fixture.Watchers.Enqueue(s_rootB, new StubFileWatcher());

		await fixture.Dispatcher.EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);

		fixture.Dispatcher.Dispose();

		Assert.AreEqual(1, watcherA.DisposeCount);
		Assert.AreEqual(1, watcherB.DisposeCount);

		await fixture.DeliverAsync(s_rootA, Change(s_rootA, "a.test")).ConfigureAwait(false);

		Assert.AreEqual(0, fixture.ForwardedBatchCount);
	}
}
