using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the workspace snapshot-tracker seam: a host-supplied tracker replaces the framework default.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderSnapshotTrackerTests
{
	private const string Content = "line one";
	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(2);

	[TestMethod]
	public async Task SuppliedTrackerFactory_IsUsedForTheRootAndDrivesTheScope()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-snapshot-" + Guid.NewGuid().ToString("N"));

		Directory.CreateDirectory(workspaceRoot);

		try
		{
			var watchers = new RecordingFileWatcherFactory { RealWatcherSpecifications = TestProviders.WatchSpecifications };
			var trackerFactory = new RecordingSnapshotTrackerFactory();
			using var client = new FakeLanguageServerClient { IsReady = false };

			using LanguageServerFeatureProvider provider = TestProviders.Create(
				workspaceRootDirectoryPaths: [workspaceRoot],
				client,
				workspaceFileWatcherFactory: watchers.Factory,
				workspaceSnapshotTrackerFactory: trackerFactory.Create
			);

			string filePath = Path.Combine(workspaceRoot, "test.test");

			// Driving a request starts watching, which creates the tracker through the supplied factory and captures
			// the recovery baseline through it.
			await provider
				.GetHoverAsync(filePath, documentText: Content, position: new TextPosition(0, 0), cancellationToken: TestContext.CancellationToken)
				.ConfigureAwait(false);

			Assert.IsTrue(
				await TestPolling
					.ForConditionAsync(() => Volatile.Read(ref trackerFactory.Tracker.CaptureCount) >= 1, s_waitTimeout)
					.ConfigureAwait(false)
			);

			// The factory is invoked once with the normalized root and the provider's watch specifications.
			Assert.HasCount(1, trackerFactory.CreatedRoots);
			Assert.AreEqual(workspaceRoot, trackerFactory.CreatedRoots[0]);
			Assert.IsNotNull(trackerFactory.CreatedSpecifications[0]);

			// A forwarded change batch is applied to the supplied tracker instead of the framework default.
			Assert.HasCount(1, watchers.CreationsFor(workspaceRoot));

			await watchers
				.LatestFor(workspaceRoot)
				.DispatchAsync(new FileChangeBatch([new FileChange(filePath, FileChangeKind.Changed)]), CancellationToken.None)
				.ConfigureAwait(false);

			Assert.IsTrue(
				await TestPolling
					.ForConditionAsync(() => Volatile.Read(ref trackerFactory.Tracker.ApplyCount) >= 1, s_waitTimeout)
					.ConfigureAwait(false)
			);
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task ReplacementSnapshotTrackerFactory_IsUsedForEveryRoot()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-snapshot-options-" + Guid.NewGuid().ToString("N"));

		Directory.CreateDirectory(workspaceRoot);

		try
		{
			var trackerFactory = new RecordingSnapshotTrackerFactory();
			using var client = new FakeLanguageServerClient { IsReady = false };

			using LanguageServerFeatureProvider provider = TestProviders.Create(
				[workspaceRoot],
				client,
				workspaceSnapshotTrackerFactory: trackerFactory.Create
			);

			await provider
				.GetHoverAsync(
					filePath: Path.Combine(workspaceRoot, "test.test"),
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false);

			Assert.IsTrue(await TestPolling.ForConditionAsync(() => trackerFactory.CreatedRoots.Count >= 1, s_waitTimeout).ConfigureAwait(false));
			Assert.AreEqual(workspaceRoot, trackerFactory.CreatedRoots[0]);
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	private sealed class RecordingSnapshotTrackerFactory
	{
		public List<string> CreatedRoots { get; } = [];

		public List<IReadOnlyList<FileWatchSpecification>> CreatedSpecifications { get; } = [];

		public RecordingFileSnapshotTracker Tracker { get; } = new();

		public IFileSnapshotTracker Create(string workspaceRootDirectoryPath, IReadOnlyList<FileWatchSpecification> watchSpecifications)
		{
			CreatedRoots.Add(workspaceRootDirectoryPath);
			CreatedSpecifications.Add(watchSpecifications);

			return Tracker;
		}
	}

	public TestContext? TestContext { get; set; }
}
