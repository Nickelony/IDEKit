namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers <see cref="DefaultWorkspaceFileWatcherAdapter"/> directly: the argument guards it adds and the
/// lifecycle it forwards to the wrapped <see cref="WorkspaceFileWatcher"/>.
/// </summary>
[TestClass]
public sealed class DefaultWorkspaceFileWatcherAdapterTests
{
	private static readonly IReadOnlyList<WorkspaceWatchSpecification> s_specifications =
		[new WorkspaceWatchSpecification("*.ext", IncludeSubdirectories: false)];

	[TestMethod]
	public void Constructor_NullArguments_ThrowArgumentNullException()
	{
		using var workspace = new TemporaryDirectory("WatcherAdapterNull_");

		Assert.ThrowsExactly<ArgumentNullException>(() => new DefaultWorkspaceFileWatcherAdapter(
			workspace.Path, dispatchAsync: null!, s_specifications, (_, _) => { }));

		Assert.ThrowsExactly<ArgumentNullException>(() => new DefaultWorkspaceFileWatcherAdapter(
			workspace.Path, (_, _) => Task.CompletedTask, watchSpecifications: null!, (_, _) => { }));

		Assert.ThrowsExactly<ArgumentNullException>(() => new DefaultWorkspaceFileWatcherAdapter(
			workspace.Path, (_, _) => Task.CompletedTask, s_specifications, onWatcherFailed: null!));
	}

	[TestMethod]
	public void Start_MissingWorkspaceRoot_ReportsMissingRootWithoutStarting()
	{
		string missingRoot = Path.Combine(Path.GetTempPath(), "WatcherAdapterMissing_" + Guid.NewGuid().ToString("N"));

		using var adapter = new DefaultWorkspaceFileWatcherAdapter(
			missingRoot, (_, _) => Task.CompletedTask, s_specifications, (_, _) => { });

		WorkspaceWatcherStartResult result = adapter.Start();

		Assert.AreEqual(WorkspaceWatcherStartOutcome.WorkspaceRootMissing, result.Outcome);
	}

	[TestMethod]
	public void Start_ExistingWorkspaceRoot_ReportsStartedThenAlreadyRunning_AndDisposeIsTerminal()
	{
		using var workspace = new TemporaryDirectory("WatcherAdapterLifecycle_");

		var adapter = new DefaultWorkspaceFileWatcherAdapter(
			workspace.Path, (_, _) => Task.CompletedTask, s_specifications, (_, _) => { });

		Assert.AreEqual(WorkspaceWatcherStartOutcome.Started, adapter.Start().Outcome);
		Assert.AreEqual(WorkspaceWatcherStartOutcome.AlreadyRunning, adapter.Start().Outcome);

		adapter.Dispose();
		adapter.Dispose();

		Assert.AreEqual(WorkspaceWatcherStartOutcome.Disposed, adapter.Start().Outcome);
	}
}
