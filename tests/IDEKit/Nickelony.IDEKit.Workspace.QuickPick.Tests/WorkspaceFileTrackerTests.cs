using System.Diagnostics;
using Nickelony.IDEKit.FileWatching;
using Nickelony.QuickPick;

namespace Nickelony.IDEKit.Workspace.QuickPick.Tests;

/// <summary>
/// Covers the list of files below the workspace roots: the first walk, the exclusions, the bound, the changes a watcher reports,
/// the reports that arrive during a walk, the recovery of a failed watcher, and the thread the walk runs on.
/// </summary>
[TestClass]
public sealed class WorkspaceFileTrackerTests
{
	private static readonly TimeSpan s_wait = TimeSpan.FromSeconds(10);

	private static string[] LabelsOf(WorkspaceFileTracker tracker) => [.. tracker.Capture().Items.Select(item => item.Label)];

	[TestMethod]
	public async Task EnsureStartedAsync_WalksTheRootOnceAndListsItsFilesByFolderAndName()
	{
		using var host = new WorkspaceFileTestHost();
		string top = host.Write("b.txt");

		host.Write("sub/a.txt");
		host.Write("a.txt");

		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);
		await tracker.EnsureStartedAsync(CancellationToken.None);

		QuickPickItem[] items = tracker.Capture().Items;

		Assert.AreEqual(1, tracker.WalkCount);
		Assert.AreSequenceEqual(["a.txt", "b.txt", "a.txt"], items.Select(item => item.Label));
		Assert.IsNull(items[0].Description);
		Assert.AreEqual("sub", items[2].Description);
		Assert.AreEqual(top, items[1].Key);
		Assert.IsTrue(items.All(item => ReferenceEquals(item.Group, WorkspaceFileTestHost.FileGroup)));
	}

	[TestMethod]
	public async Task EnsureStartedAsync_StartsTheWatcherBeforeTheWalk()
	{
		using var host = new WorkspaceFileTestHost();
		int startsWhenWalking = -1;

		host.Write("a.txt");

		host.Enumerator = (directory, root, filter, token) =>
		{
			IReadOnlyList<RecordingFileWatcherFactory.Creation> creations = host.Watchers.CreationsFor(host.Root);

			startsWhenWalking = creations.Count == 0 ? 0 : ((StubFileWatcher)creations[0].Watcher!).StartCount;

			return WorkspaceFileWalker.List(directory, root, filter, cancellationToken: token);
		};

		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);

		Assert.AreEqual(1, startsWhenWalking);
	}

	[TestMethod]
	public async Task Capture_AfterTheFirstWalk_ReturnsTheSameListUntilAFileChanges()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("a.txt");

		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);

		WorkspaceFileSnapshot first = tracker.Capture();

		Assert.AreSame(first, tracker.Capture());

		string created = host.Write("b.txt");

		await host.DeliverAsync(new FileChange(created, FileChangeKind.Created));

		Assert.AreNotSame(first, tracker.Capture());
		Assert.AreSequenceEqual([0, 1], tracker.Capture().IndexByPath.Values.Order());
	}

	[TestMethod]
	public async Task EnsureStartedAsync_SeveralRoots_NameTheRootInTheFolderOfAFile()
	{
		using var host = new WorkspaceFileTestHost();
		using var second = new TemporaryDirectory("quickpick-second-");

		host.Write("a.txt");
		host.Write("sub/b.txt");

		File.WriteAllText(Path.Combine(second.Path, "c.txt"), "text");

		WorkspaceFileTracker tracker = host.CreateTracker([host.Root, second.Path]);

		await tracker.EnsureStartedAsync(CancellationToken.None);

		string firstName = Path.GetFileName(host.Root);
		string secondName = Path.GetFileName(second.Path);

		Assert.AreSequenceEqual(
			new[] { $"{firstName}|a.txt", $"{firstName}/sub|b.txt", $"{secondName}|c.txt" }.Order(StringComparer.OrdinalIgnoreCase),
			tracker.Capture().Items.Select(item => $"{item.Description}|{item.Label}").Order(StringComparer.OrdinalIgnoreCase)
		);

		Assert.HasCount(2, host.Watchers.Creations);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_ARootThatDoesNotExist_ListsNothingAndReportsNoFault()
	{
		using var host = new WorkspaceFileTestHost();
		WorkspaceFileTracker tracker = host.CreateTracker([host.PathOf("missing")]);

		await tracker.EnsureStartedAsync(CancellationToken.None);

		Assert.IsEmpty(tracker.Capture().Items);
		Assert.IsEmpty(host.Faults);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_ExcludedFolders_AreNeitherWalkedNorListed()
	{
		using var host = new WorkspaceFileTestHost();
		var walked = new List<string>();

		host.Write("bin/a.dll");
		host.Write("src/obj/b.dll");
		host.Write("src/c.cs");
		host.Write("notes.tmp");

		host.Enumerator = (directory, root, filter, token) =>
		{
			walked.Add(directory);
			return WorkspaceFileWalker.List(directory, root, filter, cancellationToken: token);
		};

		WorkspaceFileTracker tracker = host.CreateTracker(excludeGlobs: ["bin", "obj", "*.tmp"]);

		await tracker.EnsureStartedAsync(CancellationToken.None);

		Assert.AreSequenceEqual(["c.cs"], LabelsOf(tracker));
		Assert.AreSequenceEqual([host.Root], walked);
	}

	[TestMethod]
	public async Task DeliverAsync_ACreatedFileInAnExcludedFolder_IsNotListed()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("src/a.cs");

		WorkspaceFileTracker tracker = host.CreateTracker(excludeGlobs: ["bin", "docs/build"]);

		await tracker.EnsureStartedAsync(CancellationToken.None);

		string inBin = host.Write("src/bin/x.dll");
		string inDocs = host.Write("docs/build/y.html");
		string allowed = host.Write("docs/z.md");

		await host.DeliverAsync(
			new FileChange(inBin, FileChangeKind.Created),
			new FileChange(inDocs, FileChangeKind.Created),
			new FileChange(allowed, FileChangeKind.Created)
		);

		Assert.AreSequenceEqual(
			new[] { host.PathOf("src/a.cs"), allowed }.Order(StringComparer.Ordinal),
			WorkspaceFileTestHost.PathsOf(tracker).Order(StringComparer.Ordinal)
		);
	}

	[TestMethod]
	public async Task DeliverAsync_ACreatedFile_ShowsUpWithoutAWalk()
	{
		using var host = new WorkspaceFileTestHost();
		var walked = new List<string>();

		host.Write("a.txt");

		host.Enumerator = (directory, root, filter, token) =>
		{
			walked.Add(directory);
			return WorkspaceFileWalker.List(directory, root, filter, cancellationToken: token);
		};

		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);

		string created = host.Write("sub/new.txt");

		await host.DeliverAsync(new FileChange(created, FileChangeKind.Created));

		Assert.Contains(created, WorkspaceFileTestHost.PathsOf(tracker));
		Assert.AreEqual("sub", tracker.Capture().Items.Single(item => item.Key == created).Description);
		Assert.AreEqual(1, tracker.WalkCount);
		Assert.AreSequenceEqual([host.Root], walked);
	}

	[TestMethod]
	public async Task DeliverAsync_ADeletedFile_GoesAwayWithoutAWalk()
	{
		using var host = new WorkspaceFileTestHost();
		string doomed = host.Write("sub/doomed.txt");

		host.Write("a.txt");

		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);

		File.Delete(doomed);
		await host.DeliverAsync(new FileChange(doomed, FileChangeKind.Deleted));

		Assert.AreSequenceEqual(["a.txt"], LabelsOf(tracker));
		Assert.AreEqual(1, tracker.WalkCount);
	}

	[TestMethod]
	public async Task DeliverAsync_ACreatedFolder_ListsTheFilesInIt()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("a.txt");

		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);

		host.Write("moved/one.txt");
		host.Write("moved/deep/two.txt");

		await host.DeliverAsync(new FileChange(host.PathOf("moved"), FileChangeKind.Created));

		Assert.AreSequenceEqual(["a.txt", "one.txt", "two.txt"], LabelsOf(tracker).Order(StringComparer.Ordinal));
		Assert.AreEqual(1, tracker.WalkCount);
	}

	[TestMethod]
	public async Task DeliverAsync_ADeletedFolder_RemovesTheFilesBelowIt()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("keep/a.txt");
		host.Write("gone/b.txt");
		host.Write("gone/deep/c.txt");

		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);

		Directory.Delete(host.PathOf("gone"), recursive: true);
		await host.DeliverAsync(new FileChange(host.PathOf("gone"), FileChangeKind.Deleted));

		Assert.AreSequenceEqual(["a.txt"], LabelsOf(tracker));
	}

	[TestMethod]
	public async Task DeliverAsync_ADeletedFolderUnderADriveRoot_RemovesTheFilesBelowItAndNotThoseOfASiblingWithTheSamePrefix()
	{
		using var host = new WorkspaceFileTestHost();

		// The roots end in a separator here, so the walk is a stub that lists paths no folder holds, and the report names a folder that is gone.
		string driveRoot = Path.GetPathRoot(host.Root)!;
		string gone = Path.Combine(driveRoot, $"quickpick-{Guid.NewGuid():N}");
		string sibling = gone + "-sibling";

		host.Enumerator = (_, _, _, _) =>
			[Path.Combine(gone, "a.txt"), Path.Combine(gone, "deep", "b.txt"), Path.Combine(sibling, "c.txt"), Path.Combine(driveRoot, "d.txt")];

		WorkspaceFileTracker tracker = host.CreateTracker([driveRoot]);

		await tracker.EnsureStartedAsync(CancellationToken.None);

		Assert.AreSequenceEqual(["a.txt", "b.txt", "c.txt", "d.txt"], [.. LabelsOf(tracker).Order(StringComparer.Ordinal)]);

		await host.DeliverAsync(driveRoot, new FileChange(gone, FileChangeKind.Deleted));

		Assert.AreSequenceEqual(["c.txt", "d.txt"], [.. LabelsOf(tracker).Order(StringComparer.Ordinal)]);
	}

	[TestMethod]
	public async Task DeliverAsync_AMovedFolder_ListsItsFilesAtTheNewPathOnly()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("old/a.txt");
		host.Write("old/deep/b.txt");

		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);

		Directory.Move(host.PathOf("old"), host.PathOf("renamed"));

		await host.DeliverAsync(
			new FileChange(host.PathOf("old"), FileChangeKind.Deleted),
			new FileChange(host.PathOf("renamed"), FileChangeKind.Created)
		);

		Assert.AreSequenceEqual(
			new[] { host.PathOf("renamed/a.txt"), host.PathOf("renamed/deep/b.txt") }.Order(StringComparer.Ordinal),
			WorkspaceFileTestHost.PathsOf(tracker).Order(StringComparer.Ordinal)
		);

		Assert.AreEqual(1, tracker.WalkCount);
	}

	[TestMethod]
	public async Task DeliverAsync_AReportThatArrivesAfterTheFileWasDeleted_ListsNothing()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("a.txt");

		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);

		string flash = host.Write("flash.txt");

		File.Delete(flash);
		await host.DeliverAsync(new FileChange(flash, FileChangeKind.Created));

		Assert.AreSequenceEqual(["a.txt"], LabelsOf(tracker));
	}

	[TestMethod]
	public async Task DeliverAsync_ADeleteReportForAFileThatIsBack_KeepsTheFile()
	{
		using var host = new WorkspaceFileTestHost();

		string path = host.Write("a.txt");
		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);
		await host.DeliverAsync(new FileChange(path, FileChangeKind.Deleted));

		Assert.AreSequenceEqual([path], WorkspaceFileTestHost.PathsOf(tracker));
	}

	[TestMethod]
	public async Task DeliverAsync_AChangeOfContent_ChangesNothing()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("a.txt");

		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);

		WorkspaceFileSnapshot before = tracker.Capture();
		string untracked = host.Write("b.txt");

		await host.DeliverAsync(new FileChange(host.PathOf("a.txt"), FileChangeKind.Changed), new FileChange(untracked, FileChangeKind.Changed));

		Assert.AreSame(before, tracker.Capture());
	}

	[TestMethod]
	public async Task DeliverAsync_APathOutsideTheRoots_IsIgnored()
	{
		using var host = new WorkspaceFileTestHost();
		using var elsewhere = new TemporaryDirectory("quickpick-elsewhere-");

		string outside = Path.Combine(elsewhere.Path, "x.txt");

		File.WriteAllText(outside, "text");

		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);
		await host.DeliverAsync(new FileChange(outside, FileChangeKind.Created));

		Assert.IsEmpty(tracker.Capture().Items);
	}

	[TestMethod]
	public async Task DeliverAsync_ThePathSpelledWithAnotherCase_FollowsThePathComparison()
	{
		if (!WorkspaceFileTestHost.Comparison.IgnoreCase)
			Assert.Inconclusive("The paths of this platform compare by case.");

		using var host = new WorkspaceFileTestHost();

		string path = host.Write("Sub/A.txt");
		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);

		File.Delete(path);
		await host.DeliverAsync(new FileChange(path.ToUpperInvariant(), FileChangeKind.Deleted));

		Assert.IsEmpty(tracker.Capture().Items);
	}

	[TestMethod]
	public async Task Changed_IsRaisedWhenABatchChangesTheListAndNotWhenItDoesNot()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("a.txt");

		WorkspaceFileTracker tracker = host.CreateTracker();
		int raised = 0;

		tracker.Changed += (_, _) => Interlocked.Increment(ref raised);

		await tracker.EnsureStartedAsync(CancellationToken.None);
		await host.DeliverAsync(new FileChange(host.PathOf("unknown.txt"), FileChangeKind.Deleted));

		Assert.AreEqual(0, raised);

		string created = host.Write("b.txt");

		await host.DeliverAsync(new FileChange(created, FileChangeKind.Created));

		Assert.AreEqual(1, raised);
	}

	[TestMethod]
	public async Task Changed_AHandlerThatThrows_IsContainedAndReported()
	{
		using var host = new WorkspaceFileTestHost();
		WorkspaceFileTracker tracker = host.CreateTracker();

		tracker.Changed += (_, _) => throw new InvalidOperationException("handler");

		await tracker.EnsureStartedAsync(CancellationToken.None);
		await host.DeliverAsync(new FileChange(host.Write("a.txt"), FileChangeKind.Created));

		Assert.AreEqual("handler", host.Faults.Single().Message);
		Assert.AreSequenceEqual(["a.txt"], LabelsOf(tracker));
	}

	[TestMethod]
	public async Task DeliverAsync_ABatchDuringTheWalk_WaitsForTheWalkAndIsCheckedAgainAfterIt()
	{
		using var host = new WorkspaceFileTestHost();
		using var walked = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();

		string doomed = host.Write("doomed.txt");

		host.Write("kept.txt");

		host.Enumerator = (directory, root, filter, token) =>
			HoldAfterListing(WorkspaceFileWalker.List(directory, root, filter, cancellationToken: token), walked, release);

		WorkspaceFileTracker tracker = host.CreateTracker();
		Task ready = tracker.EnsureStartedAsync(CancellationToken.None);

		Assert.IsTrue(await Task.Run(() => walked.Wait(s_wait)));

		// The walk has listed both files and has not installed its list; the reports below come in meanwhile.
		string created = host.Write("created.txt");

		File.Delete(doomed);
		await host.DeliverAsync(new FileChange(created, FileChangeKind.Created), new FileChange(doomed, FileChangeKind.Deleted));

		Assert.IsEmpty(tracker.Capture().Items);

		release.Set();
		await ready;

		Assert.AreSequenceEqual(["created.txt", "kept.txt"], LabelsOf(tracker).Order(StringComparer.Ordinal));
		Assert.AreEqual(1, tracker.WalkCount);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_TheWalkRunsOnAnotherThreadAndTheCallDoesNotWaitForIt()
	{
		using var host = new WorkspaceFileTestHost();
		using var started = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();

		int walkThread = -1;

		host.Write("a.txt");

		host.Enumerator = (directory, root, filter, token) =>
		{
			walkThread = Environment.CurrentManagedThreadId;
			started.Set();
			release.Wait(s_wait, token);

			return WorkspaceFileWalker.List(directory, root, filter, cancellationToken: token);
		};

		WorkspaceFileTracker tracker = host.CreateTracker();
		Task ready = tracker.EnsureStartedAsync(CancellationToken.None);

		Assert.IsFalse(ready.IsCompleted);
		Assert.IsTrue(await Task.Run(() => started.Wait(s_wait)));
		Assert.AreNotEqual(Environment.CurrentManagedThreadId, walkThread);
		Assert.IsFalse(ready.IsCompleted);

		release.Set();
		await ready;

		Assert.AreSequenceEqual(["a.txt"], LabelsOf(tracker));
	}

	[TestMethod]
	public async Task EnsureStartedAsync_ACanceledQuery_StopsWaitingAndLeavesTheWalkRunning()
	{
		using var host = new WorkspaceFileTestHost();
		using var started = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		using var query = new CancellationTokenSource();

		host.Write("a.txt");

		host.Enumerator = (directory, root, filter, token) =>
			HoldAfterListing(WorkspaceFileWalker.List(directory, root, filter, cancellationToken: token), started, release);

		WorkspaceFileTracker tracker = host.CreateTracker();
		Task canceled = tracker.EnsureStartedAsync(query.Token);

		Assert.IsTrue(await Task.Run(() => started.Wait(s_wait)));

		query.Cancel();
		await Assert.ThrowsAsync<OperationCanceledException>(() => canceled);

		release.Set();
		await tracker.EnsureStartedAsync(CancellationToken.None);

		Assert.AreSequenceEqual(["a.txt"], LabelsOf(tracker));
		Assert.AreEqual(1, tracker.WalkCount);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_TheBound_StopsTheWalkAndIgnoresFurtherFilesUntilRoomIsFree()
	{
		using var host = new WorkspaceFileTestHost();

		for (int index = 0; index < 6; index++)
			host.Write($"f{index}.txt");

		WorkspaceFileTracker tracker = host.CreateTracker(maxFileCount: 3);

		await tracker.EnsureStartedAsync(CancellationToken.None);

		Assert.HasCount(3, tracker.Capture().Items);

		string beyond = host.Write("beyond.txt");

		await host.DeliverAsync(new FileChange(beyond, FileChangeKind.Created));

		Assert.HasCount(3, tracker.Capture().Items);
		Assert.DoesNotContain(beyond, WorkspaceFileTestHost.PathsOf(tracker));

		string listed = WorkspaceFileTestHost.PathsOf(tracker)[0];

		File.Delete(listed);
		await host.DeliverAsync(new FileChange(listed, FileChangeKind.Deleted));

		Assert.HasCount(2, tracker.Capture().Items);

		await host.DeliverAsync(new FileChange(beyond, FileChangeKind.Created));

		Assert.HasCount(3, tracker.Capture().Items);
		Assert.Contains(beyond, WorkspaceFileTestHost.PathsOf(tracker));
	}

	[TestMethod]
	public async Task EnsureStartedAsync_TheBoundIsSharedByTheRootsInTheOrderTheyAreGiven()
	{
		using var host = new WorkspaceFileTestHost();
		using var second = new TemporaryDirectory("quickpick-second-");

		for (int index = 0; index < 3; index++)
		{
			host.Write($"a{index}.txt");
			File.WriteAllText(Path.Combine(second.Path, $"b{index}.txt"), "text");
		}

		WorkspaceFileTracker tracker = host.CreateTracker([host.Root, second.Path], maxFileCount: 4);

		await tracker.EnsureStartedAsync(CancellationToken.None);

		string[] paths = WorkspaceFileTestHost.PathsOf(tracker);

		// The first root takes its three files, and the second gets what is left.
		Assert.HasCount(4, paths);
		Assert.AreEqual(3, paths.Count(path => path.StartsWith(host.Root, StringComparison.OrdinalIgnoreCase)));
		Assert.ContainsSingle(path => path.StartsWith(second.Path, StringComparison.OrdinalIgnoreCase), paths);
	}

	[TestMethod]
	public async Task HandleWatcherFailure_ReplacesTheWatcherAndWalksAgainForTheFilesMissedMeanwhile()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("a.txt");

		WorkspaceFileTracker tracker = host.CreateTracker();
		int raised = 0;

		tracker.Changed += (_, _) => Interlocked.Increment(ref raised);

		await tracker.EnsureStartedAsync(CancellationToken.None);

		RecordingFileWatcherFactory.Creation first = host.Watchers.CreationsFor(host.Root).Single();
		string missed = host.Write("missed.txt");

		first.OnWatcherFailed(first.Watcher!, new IOException("buffer overflow"));

		await TestPolling.UntilAsync(() => tracker.WalkCount == 2, s_wait);

		Assert.IsTrue(((StubFileWatcher)first.Watcher!).IsDisposed);
		Assert.HasCount(2, host.Watchers.CreationsFor(host.Root));
		Assert.Contains(missed, WorkspaceFileTestHost.PathsOf(tracker));
		Assert.AreEqual("buffer overflow", host.Faults.Single().Message);

		await TestPolling.UntilAsync(() => Volatile.Read(ref raised) == 1, s_wait);
	}

	[TestMethod]
	public async Task HandleWatcherFailure_AReportOfAWatcherThatWasReplaced_IsIgnored()
	{
		using var host = new WorkspaceFileTestHost();
		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);

		RecordingFileWatcherFactory.Creation first = host.Watchers.CreationsFor(host.Root).Single();

		first.OnWatcherFailed(first.Watcher!, null);

		await TestPolling.UntilAsync(() => tracker.WalkCount == 2, s_wait);

		first.OnWatcherFailed(first.Watcher!, new IOException("late"));

		Assert.IsFalse(
			await TestPolling.ForConditionAsync(
				() => host.Watchers.CreationsFor(host.Root).Count > 2 || tracker.WalkCount > 2,
				TestPolling.AbsenceWindow
			)
		);

		Assert.IsEmpty(host.Faults);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_AWatcherThatCannotStart_KeepsTheWalkedFilesAndTriesAgainWithTheNextQuery()
	{
		using var host = new WorkspaceFileTestHost();

		var broken = new StubFileWatcher
		{
			StartResult = new FileWatcherStartResult(FileWatcherStartOutcome.StartupFailed, new IOException("no watcher")),
		};

		host.Write("a.txt");
		host.Watchers.Enqueue(host.Root, broken);

		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);

		Assert.AreSequenceEqual(["a.txt"], LabelsOf(tracker));
		Assert.IsTrue(broken.IsDisposed);
		Assert.AreEqual("no watcher", host.Faults.Single().Message);

		host.Write("b.txt");
		await tracker.EnsureStartedAsync(CancellationToken.None);

		// A query before the retry interval has passed does not try again.
		Assert.IsFalse(await TestPolling.ForConditionAsync(() => host.Watchers.CreationsFor(host.Root).Count > 1, TestPolling.AbsenceWindow));

		host.Clock.Advance(WorkspaceRootWatchSupervisor.RetryInterval);
		await tracker.EnsureStartedAsync(CancellationToken.None);
		await TestPolling.UntilAsync(() => tracker.WalkCount == 2, s_wait);

		Assert.HasCount(2, host.Watchers.CreationsFor(host.Root));
		Assert.AreSequenceEqual(["a.txt", "b.txt"], LabelsOf(tracker));
	}

	[TestMethod]
	public async Task EnsureStartedAsync_AWatcherThatFailsToStartAgainTheSameWay_IsReportedOnce()
	{
		using var host = new WorkspaceFileTestHost();

		for (int attempt = 0; attempt < 3; attempt++)
		{
			host.Watchers.Enqueue(
				host.Root,
				new StubFileWatcher { StartResult = new FileWatcherStartResult(FileWatcherStartOutcome.StartupFailed, new IOException("no watcher")) }
			);
		}

		host.Watchers.Enqueue(
			host.Root,

			new StubFileWatcher
			{
				StartResult = new FileWatcherStartResult(FileWatcherStartOutcome.StartupFailed, new IOException("another failure")),
			}
		);

		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);

		await UntilAttemptedAsync(host, tracker, creations: 2);
		await UntilAttemptedAsync(host, tracker, creations: 3);

		Assert.AreSequenceEqual(["no watcher"], host.Faults.Select(fault => fault.Message));

		await UntilAttemptedAsync(host, tracker, creations: 4);
		await TestPolling.UntilAsync(() => host.Faults.Count == 2, s_wait);

		Assert.AreSequenceEqual(["no watcher", "another failure"], host.Faults.Select(fault => fault.Message));
	}

	[TestMethod]
	public async Task EnsureStartedAsync_AWatcherFactoryThatThrows_IsHeldToTheRetryIntervalAndReportedOnce()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("a.txt");

		host.Watchers.EnqueueFailure(host.Root, new IOException("no handles"));
		host.Watchers.EnqueueFailure(host.Root, new IOException("no handles"));

		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);

		Assert.AreSequenceEqual(["a.txt"], LabelsOf(tracker));
		Assert.AreEqual("no handles", host.Faults.Single().Message);

		await tracker.EnsureStartedAsync(CancellationToken.None);

		// A query before the retry interval has passed does not call the factory again.
		Assert.IsFalse(await TestPolling.ForConditionAsync(() => host.Watchers.CreationsFor(host.Root).Count > 1, TestPolling.AbsenceWindow));

		await UntilAttemptedAsync(host, tracker, creations: 2);

		Assert.IsFalse(await TestPolling.ForConditionAsync(() => host.Faults.Count > 1, TestPolling.AbsenceWindow));
	}

	// A retry runs on a pool thread, and a root takes no new one while the last still runs, so the query repeats until the attempt shows.
	private static Task UntilAttemptedAsync(WorkspaceFileTestHost host, WorkspaceFileTracker tracker, int creations)
	{
		return TestPolling.UntilAsync(
			() =>
			{
				if (host.Watchers.CreationsFor(host.Root).Count < creations)
				{
					host.Clock.Advance(WorkspaceRootWatchSupervisor.RetryInterval);
					tracker.EnsureStartedAsync(CancellationToken.None).GetAwaiter().GetResult();
				}

				return host.Watchers.CreationsFor(host.Root).Count >= creations;
			},

			s_wait
		);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_ARootWithoutAWatcher_IsTriedAfterTheIntervalAndNotWalkedAgainWhileItStaysUnwatched()
	{
		using var host = new WorkspaceFileTestHost();
		var missing = new StubFileWatcher { StartResult = new FileWatcherStartResult(FileWatcherStartOutcome.WorkspaceRootMissing, null) };

		host.Watchers.Enqueue(host.Root, missing);

		host.Watchers.Enqueue(
			host.Root,
			new StubFileWatcher { StartResult = new FileWatcherStartResult(FileWatcherStartOutcome.WorkspaceRootMissing, null) }
		);

		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);
		await tracker.EnsureStartedAsync(CancellationToken.None);

		Assert.HasCount(1, host.Watchers.CreationsFor(host.Root));

		host.Clock.Advance(WorkspaceRootWatchSupervisor.RetryInterval);
		await tracker.EnsureStartedAsync(CancellationToken.None);
		await TestPolling.UntilAsync(() => host.Watchers.CreationsFor(host.Root).Count == 2, s_wait);

		Assert.AreEqual(1, tracker.WalkCount);
		Assert.IsEmpty(host.Faults);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_AnEnumeratorThatThrows_IsReportedAndTheOtherRootsStillList()
	{
		using var host = new WorkspaceFileTestHost();
		using var second = new TemporaryDirectory("quickpick-second-");

		File.WriteAllText(Path.Combine(second.Path, "ok.txt"), "text");

		host.Enumerator = (directory, root, filter, token) =>
			root == host.Root
				? throw new InvalidOperationException("walk")
				: WorkspaceFileWalker.List(directory, root, filter, cancellationToken: token);

		WorkspaceFileTracker tracker = host.CreateTracker([host.Root, second.Path]);

		await tracker.EnsureStartedAsync(CancellationToken.None);

		Assert.AreSequenceEqual(["ok.txt"], LabelsOf(tracker));
		Assert.AreEqual("walk", host.Faults.Single().Message);
	}

	[TestMethod]
	public async Task Dispose_StopsTheWatchersAndEmptiesTheList()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("a.txt");

		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);

		tracker.Dispose();
		tracker.Dispose();

		Assert.IsTrue(((StubFileWatcher)host.Watchers.CreationsFor(host.Root).Single().Watcher!).IsDisposed);
		Assert.IsEmpty(tracker.Capture().Items);
		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => tracker.EnsureStartedAsync(CancellationToken.None));
	}

	[TestMethod]
	public async Task Dispose_DuringTheWalk_EndsItWithoutAList()
	{
		using var host = new WorkspaceFileTestHost();
		using var started = new ManualResetEventSlim();

		host.Write("a.txt");
		host.Enumerator = (directory, root, filter, token) => WaitForCancellation(started, token);

		WorkspaceFileTracker tracker = host.CreateTracker();
		Task ready = tracker.EnsureStartedAsync(CancellationToken.None);

		Assert.IsTrue(await Task.Run(() => started.Wait(s_wait)));

		tracker.Dispose();

		await Assert.ThrowsAsync<OperationCanceledException>(() => ready.WaitAsync(s_wait));

		Assert.AreEqual(0, tracker.WalkCount);
		Assert.IsEmpty(tracker.Capture().Items);
		Assert.IsEmpty(host.Faults);
	}

	[TestMethod]
	public async Task DeliverAsync_AfterDispose_ChangesNothing()
	{
		using var host = new WorkspaceFileTestHost();
		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);

		string created = host.Write("a.txt");

		tracker.Dispose();
		await host.DeliverAsync(new FileChange(created, FileChangeKind.Created));

		Assert.IsEmpty(tracker.Capture().Items);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_ARootThatVanishes_LosesItsFilesWhileAnotherRootKeepsItsOwn()
	{
		using var host = new WorkspaceFileTestHost();
		var second = new TemporaryDirectory("quickpick-second-");

		host.Write("kept.txt");
		File.WriteAllText(Path.Combine(second.Path, "gone.txt"), "text");

		WorkspaceFileTracker tracker = host.CreateTracker([host.Root, second.Path]);
		int raised = 0;

		tracker.Changed += (_, _) => Interlocked.Increment(ref raised);

		await tracker.EnsureStartedAsync(CancellationToken.None);

		Assert.HasCount(2, tracker.Capture().Items);

		RecordingFileWatcherFactory.Creation creation = host.Watchers.CreationsFor(second.Path).Single();

		// The replacement watcher finds no root, as a watcher on a deleted folder does.
		host.Watchers.Enqueue(second.Path, new StubFileWatcher { RequiredDirectory = second.Path });
		second.Dispose();
		creation.OnWatcherFailed(creation.Watcher!, null);

		await TestPolling.UntilAsync(() => tracker.Capture().Items.Length == 1, s_wait);

		Assert.AreSequenceEqual(["kept.txt"], LabelsOf(tracker));
		await TestPolling.UntilAsync(() => Volatile.Read(ref raised) == 1, s_wait);
		Assert.IsEmpty(host.Faults);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_ARootThatComesBack_ListsItsFilesAgainWithTheNextQueryAfterTheInterval()
	{
		using var host = new WorkspaceFileTestHost();
		string root = host.PathOf("ws");

		Directory.CreateDirectory(root);
		File.WriteAllText(Path.Combine(root, "first.txt"), "text");

		WorkspaceFileTracker tracker = host.CreateTracker([root]);

		await tracker.EnsureStartedAsync(CancellationToken.None);

		RecordingFileWatcherFactory.Creation creation = host.Watchers.CreationsFor(root).Single();

		host.Watchers.Enqueue(root, new StubFileWatcher { RequiredDirectory = root });
		Directory.Delete(root, recursive: true);
		creation.OnWatcherFailed(creation.Watcher!, null);

		await TestPolling.UntilAsync(() => tracker.Capture().Items.Length == 0, s_wait);

		Directory.CreateDirectory(root);
		File.WriteAllText(Path.Combine(root, "second.txt"), "text");
		host.Clock.Advance(WorkspaceRootWatchSupervisor.RetryInterval);

		// The recovery that dropped the files may still hold its flag for a moment, so the query is repeated until the retry starts.
		await TestPolling.UntilAsync(
			() =>
			{
				_ = tracker.EnsureStartedAsync(CancellationToken.None);
				return tracker.WalkCount == 2;
			},

			s_wait
		);

		Assert.AreSequenceEqual(["second.txt"], LabelsOf(tracker));
	}

	[TestMethod]
	public async Task EnsureStartedAsync_AWatcherThatFailsOnARootThatStillExists_KeepsTheFiles()
	{
		using var host = new WorkspaceFileTestHost();

		host.Write("a.txt");

		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);

		RecordingFileWatcherFactory.Creation creation = host.Watchers.CreationsFor(host.Root).Single();

		host.Watchers.Enqueue(
			host.Root,
			new StubFileWatcher { StartResult = new FileWatcherStartResult(FileWatcherStartOutcome.StartupFailed, new IOException("cannot watch")) }
		);

		creation.OnWatcherFailed(creation.Watcher!, null);

		await TestPolling.UntilAsync(() => host.Faults.Count == 1, s_wait);
		await Task.Delay(TestPolling.AbsenceWindow);

		Assert.AreSequenceEqual(["a.txt"], LabelsOf(tracker));
	}

	[TestMethod]
	public async Task DeliverAsync_ACaseOnlyRenameOfAFile_UpdatesTheSpellingOfTheEntry()
	{
		if (!WorkspaceFileTestHost.Comparison.IgnoreCase)
			Assert.Inconclusive("The paths of this platform compare by case, so a case-only rename is a move.");

		using var host = new WorkspaceFileTestHost();
		string old = host.Write("sub/Foo.cs");

		host.Write("other.cs");

		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);

		string renamed = host.PathOf("sub/foo.cs");

		File.Move(old, renamed);
		await host.DeliverAsync(new FileChange(old, FileChangeKind.Deleted), new FileChange(renamed, FileChangeKind.Created));

		QuickPickItem item = tracker.Capture().Items.Single(candidate => candidate.Description == "sub");

		Assert.AreEqual(renamed, item.Key);
		Assert.AreEqual("foo.cs", item.Label);
		Assert.HasCount(2, tracker.Capture().Items);
	}

	[TestMethod]
	public async Task DeliverAsync_ACaseOnlyRenameReportedByTheOldSpellingOnly_StillUpdatesTheEntry()
	{
		if (!WorkspaceFileTestHost.Comparison.IgnoreCase)
			Assert.Inconclusive("The paths of this platform compare by case, so a case-only rename is a move.");

		using var host = new WorkspaceFileTestHost();

		string old = host.Write("Foo.cs");
		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);

		string renamed = host.PathOf("foo.cs");

		File.Move(old, renamed);
		await host.DeliverAsync(new FileChange(old, FileChangeKind.Deleted));

		Assert.AreSequenceEqual([renamed], WorkspaceFileTestHost.PathsOf(tracker));
	}

	[TestMethod]
	public async Task DeliverAsync_ACaseOnlyRenameOfAFolder_UpdatesTheSpellingOfItsFiles()
	{
		if (!WorkspaceFileTestHost.Comparison.IgnoreCase)
			Assert.Inconclusive("The paths of this platform compare by case, so a case-only rename is a move.");

		using var host = new WorkspaceFileTestHost();

		host.Write("Dir/a.cs");

		WorkspaceFileTracker tracker = host.CreateTracker();

		await tracker.EnsureStartedAsync(CancellationToken.None);

		string temporary = host.PathOf("tmp-dir");

		Directory.Move(host.PathOf("Dir"), temporary);
		Directory.Move(temporary, host.PathOf("dir"));

		await host.DeliverAsync(
			new FileChange(host.PathOf("Dir"), FileChangeKind.Deleted),
			new FileChange(host.PathOf("dir"), FileChangeKind.Created)
		);

		QuickPickItem item = tracker.Capture().Items.Single();

		Assert.AreEqual(host.PathOf("dir/a.cs"), item.Key);
		Assert.AreEqual("dir", item.Description);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_TheTrackerDisposedWhileAQueryWaits_EndsTheWaitAsCanceled()
	{
		using var host = new WorkspaceFileTestHost();
		using var started = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();

		host.Write("a.txt");

		host.Enumerator = (directory, root, filter, token) =>
			HoldAfterListing(WorkspaceFileWalker.List(directory, root, filter, cancellationToken: token), started, release);

		WorkspaceFileTracker tracker = host.CreateTracker();
		Task waiting = tracker.EnsureStartedAsync(CancellationToken.None);

		Assert.IsTrue(await Task.Run(() => started.Wait(s_wait)));

		tracker.Dispose();
		release.Set();

		await Assert.ThrowsAsync<OperationCanceledException>(() => waiting);
		Assert.ThrowsExactly<OperationCanceledException>(tracker.ThrowIfStopped);
	}

	// Lists the files, then holds the walk at its end until the test releases it.
	private static IEnumerable<string> HoldAfterListing(IEnumerable<string> files, ManualResetEventSlim walked, ManualResetEventSlim release)
	{
		foreach (string file in files)
			yield return file;

		walked.Set();
		release.Wait(s_wait);
	}

	private static IEnumerable<string> WaitForCancellation(ManualResetEventSlim started, CancellationToken token)
	{
		started.Set();

		var stopwatch = Stopwatch.StartNew();

		while (!token.IsCancellationRequested && stopwatch.Elapsed < s_wait)
			Thread.Sleep(5);

		token.ThrowIfCancellationRequested();

		yield break;
	}
}
