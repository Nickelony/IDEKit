using Microsoft.Extensions.Logging.Abstractions;

namespace Nickelony.IDEKit.FileWatching.Tests;

[TestClass]
public sealed class FileSystemWatcherSetTests
{
	private static readonly FileWatchSpecification[] s_twoSpecifications =
	[
		new("*.one", IncludeSubdirectories: false),
		new("*.two", IncludeSubdirectories: true),
	];

	private sealed class TestHost : IDisposable
	{
		private readonly TemporaryDirectory _workspace = new("FileSystemWatcherSet_");

		internal TestHost(Func<string, FileWatchSpecification, FileSystemWatcher>? createFileSystemWatcher = null)
		{
			Set = new FileSystemWatcherSet(
				workspaceRootDirectoryPath: _workspace.Path,
				pathComparison: LocalPathComparisonPolicy.ForCurrentPlatform,

				queueChange: (path, kind) =>
				{
					lock (Changes)
						Changes.Add((path, kind));
				},

				handleError: (_, _) => { },
				createFileSystemWatcher,
				logger: NullLogger.Instance
			);
		}

		internal FileSystemWatcherSet Set { get; }

		internal List<(string Path, FileChangeKind Kind)> Changes { get; } = [];

		internal string WorkspacePath => _workspace.Path;

		internal bool HasChange(string path, FileChangeKind kind)
		{
			lock (Changes)
				return Changes.Any(change => change.Kind == kind && string.Equals(change.Path, path, StringComparison.OrdinalIgnoreCase));
		}

		internal Task<bool> WaitForChangeAsync(string path, FileChangeKind kind) =>
			TestPolling.ForConditionAsync(() => HasChange(path, kind), TestPolling.DefaultTimeout);

		internal FileSystemWatcherActivationOutcome Activate(
			Func<bool>? isDisposed = null,
			Action<FileSystemWatcher>? onRegistered = null,
			Action? onActivated = null
		)
		{
			return Set.Activate(
				specifications: s_twoSpecifications,
				isDisposed: isDisposed ?? (() => false),
				onRegistered,
				onActivated: onActivated ?? (() => { })
			);
		}

		public void Dispose()
		{
			Set.StopAll();
			_workspace.Dispose();
		}
	}

	[TestMethod]
	public void Activate_RegistersOneWatcherPerSpecificationAndRunsTheActivationCallbackOnce()
	{
		using var fixture = new TestHost();
		int activations = 0;

		FileSystemWatcherActivationOutcome outcome = fixture.Activate(onActivated: () => activations++);

		Assert.AreEqual(FileSystemWatcherActivationOutcome.Started, outcome);
		Assert.AreEqual(2, fixture.Set.Count);
		Assert.AreEqual(1, activations);
	}

	[TestMethod]
	public void Activate_WhenTheSetIsActive_ReportsAlreadyRunningWithoutCreatingOrNotifying()
	{
		using var fixture = new TestHost();
		int activations = 0;

		fixture.Activate(onActivated: () => activations++);

		FileSystemWatcherActivationOutcome outcome = fixture.Activate(onActivated: () => activations++);

		Assert.AreEqual(FileSystemWatcherActivationOutcome.AlreadyRunning, outcome);
		Assert.AreEqual(2, fixture.Set.Count);
		Assert.AreEqual(1, activations);
	}

	[TestMethod]
	public void Activate_WhenTheOwnerIsDisposedBeforehand_CreatesNothing()
	{
		int created = 0;

		using var fixture = new TestHost(
			(root, specification) =>
			{
				created++;
				return new FileSystemWatcher(root, specification.Filter);
			}
		);

		FileSystemWatcherActivationOutcome outcome = fixture.Activate(isDisposed: () => true);

		Assert.AreEqual(FileSystemWatcherActivationOutcome.Disposed, outcome);
		Assert.AreEqual(0, created);
		Assert.AreEqual(0, fixture.Set.Count);
	}

	[TestMethod]
	public void Activate_WhenTheOwnerIsDisposedWhileActivating_ReportsDisposedWithoutTheActivationCallback()
	{
		using var fixture = new TestHost();

		int activations = 0;
		int checks = 0;

		// The entry check passes, and the check after the last watcher started sees the disposal.
		FileSystemWatcherActivationOutcome outcome = fixture.Activate(isDisposed: () => ++checks > 1, onActivated: () => activations++);

		Assert.AreEqual(FileSystemWatcherActivationOutcome.Disposed, outcome);
		Assert.AreEqual(0, activations);
	}

	[TestMethod]
	public void Activate_WhenTheSetIsStoppedBetweenRegistrationAndActivation_PropagatesTheFaultOfTheDisposedWatcher()
	{
		using var fixture = new TestHost();
		int activations = 0;

		// An error handler that runs in this window stops the set and disposes the registered watcher, so enabling it faults; the owner
		// classifies the fault as a failed start.
		Assert.ThrowsExactly<ObjectDisposedException>(() =>
			fixture.Activate(onRegistered: _ => fixture.Set.StopAll(), onActivated: () => activations++)
		);

		Assert.AreEqual(0, fixture.Set.Count);
		Assert.AreEqual(0, activations);
	}

	[TestMethod]
	public void Activate_WhenTheCreatorThrows_PropagatesAndLeavesTheSetEmpty()
	{
		using var fixture = new TestHost(static (_, _) => throw new InvalidOperationException("Simulated creation failure."));

		Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Activate());
		Assert.AreEqual(0, fixture.Set.Count);
	}

	[TestMethod]
	public void StopAll_DisposesEveryWatcherAndForgetsThem()
	{
		var created = new List<FileSystemWatcher>();

		using var fixture = new TestHost(
			(root, specification) =>
			{
				var watcher = new FileSystemWatcher(root, specification.Filter);
				created.Add(watcher);
				return watcher;
			}
		);

		fixture.Activate();

		Assert.IsTrue(fixture.Set.Contains(created[0]));

		fixture.Set.StopAll();

		Assert.AreEqual(0, fixture.Set.Count);
		Assert.IsFalse(fixture.Set.Contains(created[0]));
		Assert.IsFalse(created[1].EnableRaisingEvents);
	}

	[TestMethod]
	public async Task Activate_ForwardsCreatedChangedAndDeletedEventsWithTheirKinds()
	{
		using var fixture = new TestHost();

		fixture.Activate();

		string filePath = Path.Combine(fixture.WorkspacePath, "watched.one");

		await File.WriteAllTextAsync(filePath, "content").ConfigureAwait(false);

		Assert.IsTrue(await fixture.WaitForChangeAsync(filePath, FileChangeKind.Created).ConfigureAwait(false));
		Assert.IsTrue(await fixture.WaitForChangeAsync(filePath, FileChangeKind.Changed).ConfigureAwait(false));

		File.Delete(filePath);

		Assert.IsTrue(await fixture.WaitForChangeAsync(filePath, FileChangeKind.Deleted).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task Activate_ForARenameWithinTheWatchedSet_ForwardsADeleteOfTheOldPathAndACreateOfTheNewPath()
	{
		using var fixture = new TestHost();

		string oldPath = Path.Combine(fixture.WorkspacePath, "before.one");
		string newPath = Path.Combine(fixture.WorkspacePath, "after.one");

		await File.WriteAllTextAsync(oldPath, "content").ConfigureAwait(false);

		fixture.Activate();
		File.Move(oldPath, newPath);

		Assert.IsTrue(await fixture.WaitForChangeAsync(oldPath, FileChangeKind.Deleted).ConfigureAwait(false));
		Assert.IsTrue(await fixture.WaitForChangeAsync(newPath, FileChangeKind.Created).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task Activate_ForARenameOutOfTheWatchedSet_ForwardsOnlyTheDeleteOfTheOldPath()
	{
		using var fixture = new TestHost();

		string oldPath = Path.Combine(fixture.WorkspacePath, "inside.one");
		string newPath = Path.Combine(fixture.WorkspacePath, "outside.other");

		await File.WriteAllTextAsync(oldPath, "content").ConfigureAwait(false);

		fixture.Activate();
		File.Move(oldPath, newPath);

		Assert.IsTrue(await fixture.WaitForChangeAsync(oldPath, FileChangeKind.Deleted).ConfigureAwait(false));

		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.IsFalse(fixture.HasChange(newPath, FileChangeKind.Created), "A path outside the watched set is never forwarded.");
	}

	[TestMethod]
	public async Task Activate_ForARenameIntoTheWatchedSet_ForwardsOnlyTheCreateOfTheNewPath()
	{
		using var fixture = new TestHost();

		string oldPath = Path.Combine(fixture.WorkspacePath, "outside.other");
		string newPath = Path.Combine(fixture.WorkspacePath, "inside.one");

		await File.WriteAllTextAsync(oldPath, "content").ConfigureAwait(false);

		fixture.Activate();
		File.Move(oldPath, newPath);

		Assert.IsTrue(await fixture.WaitForChangeAsync(newPath, FileChangeKind.Created).ConfigureAwait(false));

		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.IsFalse(fixture.HasChange(oldPath, FileChangeKind.Deleted), "A path outside the watched set is never forwarded.");
	}

	[TestMethod]
	public async Task Activate_WatchesSubdirectoriesOnlyWhereTheSpecificationRecurses()
	{
		using var fixture = new TestHost();
		string subdirectory = Path.Combine(fixture.WorkspacePath, "nested");

		Directory.CreateDirectory(subdirectory);
		fixture.Activate();

		string flatPath = Path.Combine(subdirectory, "flat.one");
		string recursivePath = Path.Combine(subdirectory, "recursive.two");

		await File.WriteAllTextAsync(flatPath, "content").ConfigureAwait(false);
		await File.WriteAllTextAsync(recursivePath, "content").ConfigureAwait(false);

		Assert.IsTrue(await fixture.WaitForChangeAsync(recursivePath, FileChangeKind.Created).ConfigureAwait(false));

		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.IsFalse(fixture.HasChange(flatPath, FileChangeKind.Created), "A specification without subdirectories ignores nested files.");
	}
}
