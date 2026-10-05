using Microsoft.Extensions.Logging.Abstractions;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the choice between the replacement watcher and tracker factories and the framework defaults.</summary>
[TestClass]
public sealed class WorkspaceWatchingFactoryTests
{
	private static readonly string s_root = TestPaths.Root;

	private static readonly IReadOnlyList<FileWatchSpecification> s_specifications = [new("*.test", IncludeSubdirectories: true)];

	[TestMethod]
	public void CreateFileWatcher_WithAReplacementFactory_ReturnsItsWatcherAndPassesTheArgumentsOn()
	{
		var watcher = new StubFileWatcher();

		string? receivedRoot = null;

		// One delegate instance each, so the identity assertions below compare what the factory received with what it was given.
		Func<FileChangeBatch, CancellationToken, Task> dispatch = DispatchAsync;
		Action<IFileWatcher, Exception?> onFailed = OnWatcherFailed;

		Func<FileChangeBatch, CancellationToken, Task>? receivedDispatch = null;
		Action<IFileWatcher, Exception?>? receivedOnFailed = null;

		WorkspaceWatchingFactory factory = CreateFactory(
			workspaceFileWatcherFactory: (root, dispatchAsync, onWatcherFailed) =>
			{
				receivedRoot = root;
				receivedDispatch = dispatchAsync;
				receivedOnFailed = onWatcherFailed;

				return watcher;
			}
		);

		IFileWatcher created = factory.CreateFileWatcher(s_root, dispatch, onFailed);

		Assert.AreSame(watcher, created);
		Assert.AreEqual(s_root, receivedRoot);
		Assert.AreSame(dispatch, receivedDispatch);
		Assert.AreSame(onFailed, receivedOnFailed);

		static Task DispatchAsync(FileChangeBatch batch, CancellationToken cancellationToken) => Task.CompletedTask;

		static void OnWatcherFailed(IFileWatcher watcher, Exception? exception)
		{ }
	}

	[TestMethod]
	public void CreateFileWatcher_WithoutAReplacementFactory_ReturnsTheDefaultWatcher()
	{
		WorkspaceWatchingFactory factory = CreateFactory();

		using IFileWatcher created = factory.CreateFileWatcher(s_root, static (_, _) => Task.CompletedTask, static (_, _) => { });

		Assert.IsInstanceOfType<FileWatcher>(created);
	}

	[TestMethod]
	public void CreateSnapshotTracker_WithAReplacementFactory_ReturnsItsTrackerAndPassesTheArgumentsOn()
	{
		var tracker = new FileSnapshotTracker(s_root, s_specifications, logger: NullLogger.Instance);

		string? receivedRoot = null;
		IReadOnlyList<FileWatchSpecification>? receivedSpecifications = null;
		IReadOnlyList<FileWatchSpecification> requested = [new("*.json", IncludeSubdirectories: false)];

		WorkspaceWatchingFactory factory = CreateFactory(
			workspaceSnapshotTrackerFactory: (root, specifications) =>
			{
				receivedRoot = root;
				receivedSpecifications = specifications;

				return tracker;
			}
		);

		IFileSnapshotTracker created = factory.CreateSnapshotTracker(s_root, requested);

		Assert.AreSame(tracker, created);
		Assert.AreEqual(s_root, receivedRoot);
		Assert.AreSame(requested, receivedSpecifications);
	}

	[TestMethod]
	public void CreateSnapshotTracker_WithoutAReplacementFactory_ReturnsTheDefaultTracker()
	{
		WorkspaceWatchingFactory factory = CreateFactory();

		IFileSnapshotTracker created = factory.CreateSnapshotTracker(s_root, s_specifications);

		Assert.IsInstanceOfType<FileSnapshotTracker>(created);
	}

	private static WorkspaceWatchingFactory CreateFactory(
		FileWatcherFactory? workspaceFileWatcherFactory = null,
		FileSnapshotTrackerFactory? workspaceSnapshotTrackerFactory = null
	)
	{
		return new(
			options: LanguageServerFeatureProviderOptions.Default,
			watchSpecifications: s_specifications,
			workspaceFileWatcherFactory,
			workspaceSnapshotTrackerFactory,
			logger: NullLogger.Instance
		);
	}
}
