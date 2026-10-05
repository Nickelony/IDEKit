namespace Nickelony.IDEKit.FileWatching.Tests;

/// <summary>Covers the argument checks and the optional logger of the supervisor constructor.</summary>
[TestClass]
public sealed class FileWatchSupervisorSpecificationTests
{
	private static FileWatchSupervisorSpecification CreateSpecification()
	{
		return new(
			WorkspaceRootDirectoryPath: TestPaths.Root,
			DisplayName: "Test",
			WatchSpecifications: [new FileWatchSpecification("*.test", IncludeSubdirectories: true)],
			FileWatcherFactory: new RecordingFileWatcherFactory().Factory,
			FileSnapshotTrackerFactory: static (_, _) => new RecordingFileSnapshotTracker()
		);
	}

	private static FileWatchSupervisorHooks CreateHooks()
	{
		return new()
		{
			IsEnabled = static () => true,
			IsDisposed = static () => false,
			ReportFailure = static _ => { },
			DispatchAsync = static (_, _, _) => Task.CompletedTask,
			ObserveBackgroundTask = static (_, _) => { },
		};
	}

	[TestMethod]
	public void Constructor_WithACompleteSpecification_Succeeds()
	{
		using var supervisor = new FileWatchSupervisor(CreateSpecification(), CreateHooks());
		Assert.AreEqual(TestPaths.Root, supervisor.WorkspaceRootDirectoryPath);
	}

	[TestMethod]
	public async Task Constructor_WithoutALogger_StillReportsAFailureItWouldHaveLogged()
	{
		var watchers = new RecordingFileWatcherFactory();
		watchers.EnqueueFailure(TestPaths.Root, new InvalidOperationException("The factory failed."));

		var failures = new List<FileWatchFailure>();

		FileWatchSupervisorHooks hooks = new()
		{
			IsEnabled = static () => true,
			IsDisposed = static () => false,
			ReportFailure = failures.Add,
			DispatchAsync = static (_, _, _) => Task.CompletedTask,
			ObserveBackgroundTask = static (_, _) => { },
		};

		using var supervisor = new FileWatchSupervisor(CreateSpecification() with { FileWatcherFactory = watchers.Factory }, hooks);

		await supervisor.EnsureWatcherStartedAsync().ConfigureAwait(false);

		Assert.AreEqual(FileWatchFailureCodes.StartFailed, Assert.ContainsSingle(failures).Code);
	}

	[TestMethod]
	public void Constructor_WithoutASpecification_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new FileWatchSupervisor(null!, CreateHooks()));

	[TestMethod]
	[DataRow(0, "WorkspaceRootDirectoryPath")]
	[DataRow(1, "DisplayName")]
	[DataRow(2, "WatchSpecifications")]
	[DataRow(3, "FileWatcherFactory")]
	[DataRow(4, "FileSnapshotTrackerFactory")]
	public void Constructor_WithANullMemberOfTheSpecification_Throws(int member, string name)
	{
		FileWatchSupervisorSpecification specification = CreateSpecification();

		FileWatchSupervisorSpecification invalid = member switch
		{
			0 => specification with { WorkspaceRootDirectoryPath = null! },
			1 => specification with { DisplayName = null! },
			2 => specification with { WatchSpecifications = null! },
			3 => specification with { FileWatcherFactory = null! },
			_ => specification with { FileSnapshotTrackerFactory = null! },
		};

		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() => new FileWatchSupervisor(invalid, CreateHooks()));

		Assert.AreEqual(name, exception.ParamName?.Split('.').Last(), "The guard names the member that is null.");
	}

	[TestMethod]
	public void Constructor_WithoutHooks_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new FileWatchSupervisor(CreateSpecification(), null!));

	[TestMethod]
	[DataRow(0, "IsEnabled")]
	[DataRow(1, "IsDisposed")]
	[DataRow(2, "ReportFailure")]
	[DataRow(3, "DispatchAsync")]
	[DataRow(4, "ObserveBackgroundTask")]
	public void Constructor_WithANullCallbackOfTheHooks_Throws(int member, string name)
	{
		FileWatchSupervisorHooks complete = CreateHooks();

		FileWatchSupervisorHooks invalid = new()
		{
			IsEnabled = member == 0 ? null! : complete.IsEnabled,
			IsDisposed = member == 1 ? null! : complete.IsDisposed,
			ReportFailure = member == 2 ? null! : complete.ReportFailure,
			DispatchAsync = member == 3 ? null! : complete.DispatchAsync,
			ObserveBackgroundTask = member == 4 ? null! : complete.ObserveBackgroundTask,
		};

		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() => new FileWatchSupervisor(CreateSpecification(), invalid));

		Assert.AreEqual(name, exception.ParamName?.Split('.').Last(), "The guard names the callback that is null.");
	}
}
