namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class WorkspaceSnapshotTrackerTests
{
	[TestMethod]
	public void BuildDeltaBatch_ReportsCreatedChangedAndDeletedPaths()
	{
		var workspace = new TemporaryDirectory("WorkspaceSnapshot_");
		string workspaceRoot = workspace.Path;
		string scriptsDirectoryPath = Path.Combine(workspaceRoot, "Scripts");
		string changedFilePath = Path.Combine(scriptsDirectoryPath, "changed.txt");
		string deletedFilePath = Path.Combine(scriptsDirectoryPath, "deleted.txt");
		string createdFilePath = Path.Combine(scriptsDirectoryPath, "created.txt");

		try
		{
			Directory.CreateDirectory(scriptsDirectoryPath);
			File.WriteAllText(changedFilePath, "return 1");
			File.WriteAllText(deletedFilePath, "return 2");

			var tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();
			Dictionary<string, WorkspaceSnapshotEntry> previousSnapshot = tracker.CloneTrackedSnapshot();

			File.WriteAllText(changedFilePath, "return 123456");
			File.Delete(deletedFilePath);
			File.WriteAllText(createdFilePath, "return 3");

			Dictionary<string, WorkspaceSnapshotEntry> currentSnapshot = tracker.ReplaceTrackedSnapshotWithCurrent();
			FileChangeBatch batch = WorkspaceSnapshotTracker.BuildDeltaBatch(previousSnapshot, currentSnapshot);

			string normalizedChangedFilePath = LanguageServerPaths.NormalizeLocalPath(changedFilePath);
			string normalizedDeletedFilePath = LanguageServerPaths.NormalizeLocalPath(deletedFilePath);
			string normalizedCreatedFilePath = LanguageServerPaths.NormalizeLocalPath(createdFilePath);

			Assert.AreEqual(3, batch.Count);
			Assert.AreEqual(FileChangeKind.Changed, GetChange(batch, normalizedChangedFilePath).Kind);
			Assert.AreEqual(FileChangeKind.Deleted, GetChange(batch, normalizedDeletedFilePath).Kind);
			Assert.AreEqual(FileChangeKind.Created, GetChange(batch, normalizedCreatedFilePath).Kind);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public void BuildDeltaBatch_DoesNotReportDeletionWhenTrackedPathStillExistsButCurrentSnapshotMissesIt()
	{
		var workspace = new TemporaryDirectory("WorkspaceSnapshotMissingCurrent_");
		string workspaceRoot = workspace.Path;
		string scriptsDirectoryPath = Path.Combine(workspaceRoot, "Scripts");
		string existingFilePath = Path.Combine(scriptsDirectoryPath, "existing.txt");

		try
		{
			Directory.CreateDirectory(scriptsDirectoryPath);
			File.WriteAllText(existingFilePath, "return 1");

			var tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();
			Dictionary<string, WorkspaceSnapshotEntry> previousSnapshot = tracker.CloneTrackedSnapshot();
			var currentSnapshot = new Dictionary<string, WorkspaceSnapshotEntry>(StringComparer.OrdinalIgnoreCase);

			FileChangeBatch batch = WorkspaceSnapshotTracker.BuildDeltaBatch(previousSnapshot, currentSnapshot);

			Assert.AreEqual(0, batch.Count);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public void BuildDeltaBatch_ReportsChangedPathWhenContentChangesWithoutLengthOrTimestampChange()
	{
		var workspace = new TemporaryDirectory("WorkspaceSnapshotFingerprint_");
		string workspaceRoot = workspace.Path;
		string scriptsDirectoryPath = Path.Combine(workspaceRoot, "Scripts");
		string changedFilePath = Path.Combine(scriptsDirectoryPath, "changed.txt");

		try
		{
			Directory.CreateDirectory(scriptsDirectoryPath);
			File.WriteAllText(changedFilePath, "return 1");
			DateTime baselineWriteTime = File.GetLastWriteTimeUtc(changedFilePath);

			var tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();
			Dictionary<string, WorkspaceSnapshotEntry> previousSnapshot = tracker.CloneTrackedSnapshot();

			File.WriteAllText(changedFilePath, "return 2");
			File.SetLastWriteTimeUtc(changedFilePath, baselineWriteTime);

			Dictionary<string, WorkspaceSnapshotEntry> currentSnapshot = tracker.ReplaceTrackedSnapshotWithCurrent();
			FileChangeBatch batch = WorkspaceSnapshotTracker.BuildDeltaBatch(previousSnapshot, currentSnapshot);

			string normalizedChangedFilePath = LanguageServerPaths.NormalizeLocalPath(changedFilePath);

			Assert.AreEqual(1, batch.Count);
			Assert.AreEqual(FileChangeKind.Changed, GetChange(batch, normalizedChangedFilePath).Kind);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	public void BuildDeltaBatch_ReportsChangedPathWhenASampledEdgeOfALargeFileChanges(bool changeHead)
	{
		var workspace = new TemporaryDirectory("WorkspaceSnapshotLargeFingerprint_");
		string workspaceRoot = workspace.Path;
		string scriptsDirectoryPath = Path.Combine(workspaceRoot, "Scripts");
		string changedFilePath = Path.Combine(scriptsDirectoryPath, "large.txt");

		try
		{
			Directory.CreateDirectory(scriptsDirectoryPath);

			// A file larger than the fingerprint sample window forces the bounded head-and-tail fingerprint path.
			// A same-length change to either sampled edge, with the timestamp reset, must still be reported.
			char[] baselineContent = new string('x', 64 * 1024).ToCharArray();
			char[] changedContent = (char[])baselineContent.Clone();
			changedContent[changeHead ? 0 : changedContent.Length - 1] = 'y';

			File.WriteAllText(changedFilePath, new string(baselineContent));
			DateTime baselineWriteTime = File.GetLastWriteTimeUtc(changedFilePath);

			var tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();
			Dictionary<string, WorkspaceSnapshotEntry> previousSnapshot = tracker.CloneTrackedSnapshot();

			File.WriteAllText(changedFilePath, new string(changedContent));
			File.SetLastWriteTimeUtc(changedFilePath, baselineWriteTime);

			Dictionary<string, WorkspaceSnapshotEntry> currentSnapshot = tracker.ReplaceTrackedSnapshotWithCurrent();
			FileChangeBatch batch = WorkspaceSnapshotTracker.BuildDeltaBatch(previousSnapshot, currentSnapshot);

			string normalizedChangedFilePath = LanguageServerPaths.NormalizeLocalPath(changedFilePath);

			Assert.AreEqual(1, batch.Count);
			Assert.AreEqual(FileChangeKind.Changed, GetChange(batch, normalizedChangedFilePath).Kind);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public void ApplyChanges_UpdatesTrackedSnapshotForSubsequentRecoveryDiff()
	{
		var workspace = new TemporaryDirectory("WorkspaceSnapshotApply_");
		string workspaceRoot = workspace.Path;
		string scriptsDirectoryPath = Path.Combine(workspaceRoot, "Scripts");
		string createdFilePath = Path.Combine(scriptsDirectoryPath, "created.txt");

		try
		{
			Directory.CreateDirectory(scriptsDirectoryPath);

			var tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();

			File.WriteAllText(createdFilePath, "return 1");
			string normalizedCreatedFilePath = LanguageServerPaths.NormalizeLocalPath(createdFilePath);

			tracker.ApplyChanges(
			[
				new WorkspaceFileChange(normalizedCreatedFilePath, FileChangeKind.Created)
			]);

			Dictionary<string, WorkspaceSnapshotEntry> previousSnapshot = tracker.CloneTrackedSnapshot();
			File.Delete(createdFilePath);

			Dictionary<string, WorkspaceSnapshotEntry> currentSnapshot = tracker.ReplaceTrackedSnapshotWithCurrent();
			FileChangeBatch batch = WorkspaceSnapshotTracker.BuildDeltaBatch(previousSnapshot, currentSnapshot);

			Assert.AreEqual(1, batch.Count);
			Assert.AreEqual(FileChangeKind.Deleted, batch.Entries[0].Kind);
			Assert.AreEqual(normalizedCreatedFilePath, batch.Entries[0].Path);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public void ApplyChanges_WhenAnOlderBatchCommitsAfterANewerOne_KeepsUnrelatedPathsAndSkipsSupersededOnes()
	{
		var workspace = new TemporaryDirectory("WorkspaceSnapshotTickets_");
		string workspaceRoot = workspace.Path;
		string scriptsDirectoryPath = Path.Combine(workspaceRoot, "Scripts");
		string firstFilePath = Path.Combine(scriptsDirectoryPath, "first.txt");
		string secondFilePath = Path.Combine(scriptsDirectoryPath, "second.txt");

		try
		{
			Directory.CreateDirectory(scriptsDirectoryPath);
			File.WriteAllText(firstFilePath, "return 1");
			File.WriteAllText(secondFilePath, "return 1");

			string normalizedFirstFilePath = LanguageServerPaths.NormalizeLocalPath(firstFilePath);
			string normalizedSecondFilePath = LanguageServerPaths.NormalizeLocalPath(secondFilePath);

			// Hold the older batch between computing its updates and committing them, run the newer batch
			// completely, then let the older batch commit.
			using var olderBatchReachedCommit = new ManualResetEventSlim(false);
			using var releaseOlderBatch = new ManualResetEventSlim(false);
			bool holdOlderBatch = true;

			WorkspaceSnapshotTracker tracker = null!;
			tracker = CreateTracker(workspaceRoot, new WorkspaceSnapshotTrackerTestHooks
			{
				BeforeApplyCommit = () =>
				{
					if (!Volatile.Read(ref holdOlderBatch))
						return;

					olderBatchReachedCommit.Set();
					releaseOlderBatch.Wait();
				}
			});
			tracker.CaptureTrackedSnapshot();

			File.WriteAllText(firstFilePath, "return 2");
			File.WriteAllText(secondFilePath, "return 2");

			Task olderBatch = Task.Run(() => tracker.ApplyChanges(
			[
				new WorkspaceFileChange(normalizedFirstFilePath, FileChangeKind.Changed),
				new WorkspaceFileChange(normalizedSecondFilePath, FileChangeKind.Changed)
			]));

			Assert.IsTrue(olderBatchReachedCommit.Wait(TimeSpan.FromSeconds(10)));

			// The newer batch commits first: it deletes the first path and leaves the second path untouched.
			Volatile.Write(ref holdOlderBatch, false);
			File.Delete(firstFilePath);

			tracker.ApplyChanges(
			[
				new WorkspaceFileChange(normalizedFirstFilePath, FileChangeKind.Deleted)
			]);

			releaseOlderBatch.Set();
			olderBatch.Wait(TimeSpan.FromSeconds(10));

			Dictionary<string, WorkspaceSnapshotEntry> trackedSnapshot = tracker.CloneTrackedSnapshot();

			// The older observation for the deleted path must not resurrect it from the newer one...
			Assert.IsFalse(trackedSnapshot.ContainsKey(normalizedFirstFilePath));

			// ...while the unrelated path from the same older batch is still applied.
			Assert.IsTrue(trackedSnapshot.ContainsKey(normalizedSecondFilePath));
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public void CaptureTrackedSnapshot_PreservesCaseDistinctPathsOnCaseSensitiveHosts()
	{
		if (!LanguageServerPaths.UsesCaseSensitiveLocalPaths)
			Assert.Inconclusive("The test requires case-sensitive local-path identity on the current host.");

		var workspace = new TemporaryDirectory("WorkspaceSnapshotCase_");
		string workspaceRoot = workspace.Path;
		string firstFilePath = Path.Combine(workspaceRoot, "Case.txt");
		string secondFilePath = Path.Combine(workspaceRoot, "case.txt");

		try
		{
			Directory.CreateDirectory(workspaceRoot);
			File.WriteAllText(firstFilePath, "return 1");
			File.WriteAllText(secondFilePath, "return 2");

			var tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();

			Dictionary<string, WorkspaceSnapshotEntry> snapshot = tracker.CloneTrackedSnapshot();

			Assert.AreEqual(2, snapshot.Count);
			Assert.IsTrue(snapshot.ContainsKey(LanguageServerPaths.NormalizeLocalPath(firstFilePath)));
			Assert.IsTrue(snapshot.ContainsKey(LanguageServerPaths.NormalizeLocalPath(secondFilePath)));
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void CaptureTrackedSnapshot_IncludesHiddenFiles()
	{
		var workspace = new TemporaryDirectory("WorkspaceSnapshotHidden_");
		string workspaceRoot = workspace.Path;
		string hiddenFilePath = Path.Combine(workspaceRoot, "hidden.txt");

		try
		{
			Directory.CreateDirectory(workspaceRoot);
			File.WriteAllText(hiddenFilePath, "return 1");
			File.SetAttributes(hiddenFilePath, File.GetAttributes(hiddenFilePath) | FileAttributes.Hidden);

			var tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();

			// The file-system watcher observes hidden files, so capture must include them or recovery could never
			// reconcile a hidden watched file after an outage.
			Assert.IsTrue(tracker.CloneTrackedSnapshot().ContainsKey(LanguageServerPaths.NormalizeLocalPath(hiddenFilePath)));
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public void CaptureTrackedSnapshot_ExcludesReparsePoints()
	{
		var workspace = new TemporaryDirectory("WorkspaceSnapshotReparse_");
		string workspaceRoot = workspace.Path;
		string targetFilePath = Path.Combine(workspaceRoot, "target.txt");
		string linkFilePath = Path.Combine(workspaceRoot, "link.txt");

		try
		{
			Directory.CreateDirectory(workspaceRoot);
			File.WriteAllText(targetFilePath, "return 1");
			File.CreateSymbolicLink(linkFilePath, targetFilePath);

			var tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();

			Dictionary<string, WorkspaceSnapshotEntry> snapshot = tracker.CloneTrackedSnapshot();

			Assert.IsTrue(snapshot.ContainsKey(LanguageServerPaths.NormalizeLocalPath(targetFilePath)));
			Assert.IsFalse(snapshot.ContainsKey(LanguageServerPaths.NormalizeLocalPath(linkFilePath)),
				"Reparse points are excluded because neither capture nor the watcher follows them consistently.");
		}
		catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or NotSupportedException)
		{
			Assert.Inconclusive("Symbolic link creation is not available on this host: " + exception.Message);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public void CaptureTrackedSnapshot_WhenWorkspaceRootIsMissing_ReturnsEmptySnapshot()
	{
		// The capture must observe a root that does not exist, so the path is deliberately not a
		// TemporaryDirectory (which creates the directory); nothing is created, so nothing needs deleting.
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "WorkspaceSnapshotMissingRoot_" + Guid.NewGuid().ToString("N"));

		var tracker = CreateTracker(workspaceRoot);
		tracker.CaptureTrackedSnapshot();

		Assert.AreEqual(0, tracker.CloneTrackedSnapshot().Count);
	}

	[TestMethod]
	public void ApplyChanges_WhenSnapshotWasReplacedWhileComputing_SkipsStaleUpdates()
	{
		var workspace = new TemporaryDirectory("WorkspaceSnapshotVersion_");
		string workspaceRoot = workspace.Path;
		string changedFilePath = Path.Combine(workspaceRoot, "changed.txt");

		try
		{
			Directory.CreateDirectory(workspaceRoot);
			File.WriteAllText(changedFilePath, "return 1");

			string normalizedChangedFilePath = LanguageServerPaths.NormalizeLocalPath(changedFilePath);

			// Replace the tracked snapshot exactly between the version read and the update computation, then change
			// the file again so a stale apply would record a different state than the intermediate capture did.
			WorkspaceSnapshotTracker tracker = null!;
			tracker = CreateTracker(workspaceRoot, new WorkspaceSnapshotTrackerTestHooks
			{
				SnapshotVersionRead = () =>
				{
					tracker.CaptureTrackedSnapshot();
					File.WriteAllText(changedFilePath, "return 22222");
				}
			});
			tracker.CaptureTrackedSnapshot();

			tracker.ApplyChanges([new WorkspaceFileChange(normalizedChangedFilePath, FileChangeKind.Changed)]);

			// The intermediate capture recorded the pre-modification file ("return 1", 8 bytes); the skipped stale
			// update must not have replaced it with the 12-byte state read afterwards.
			Assert.AreEqual(8, tracker.CloneTrackedSnapshot()[normalizedChangedFilePath].Length);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public void CaptureTrackedSnapshot_WhenAnApplyCommitsDuringTheWalk_KeepsTheNewerObservation()
	{
		var workspace = new TemporaryDirectory("WorkspaceSnapshotCaptureRace_");
		string workspaceRoot = workspace.Path;
		string changedFilePath = Path.Combine(workspaceRoot, "changed.txt");

		try
		{
			Directory.CreateDirectory(workspaceRoot);
			File.WriteAllText(changedFilePath, "return 1");

			string normalizedChangedFilePath = LanguageServerPaths.NormalizeLocalPath(changedFilePath);
			bool captureHookEnabled = false;

			WorkspaceSnapshotTracker tracker = null!;
			tracker = CreateTracker(workspaceRoot, new WorkspaceSnapshotTrackerTestHooks
			{
				BeforeCapturePublish = () =>
				{
					if (!Volatile.Read(ref captureHookEnabled))
						return;

					File.WriteAllText(changedFilePath, "return 22222");
					tracker.ApplyChanges([new WorkspaceFileChange(normalizedChangedFilePath, FileChangeKind.Changed)]);
				}
			});
			tracker.CaptureTrackedSnapshot();

			// While the capture walks, the file changes and an apply commits the newer observation. The capture
			// read the older state; the apply's commit is newer by ticket order and must survive the replacement.
			Volatile.Write(ref captureHookEnabled, true);
			tracker.CaptureTrackedSnapshot();

			Assert.AreEqual(12, tracker.CloneTrackedSnapshot()[normalizedChangedFilePath].Length);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public void ApplyChanges_ForAReparsePoint_RecordsNothing()
	{
		var workspace = new TemporaryDirectory("WorkspaceSnapshotApplyReparse_");
		string workspaceRoot = workspace.Path;
		string targetFilePath = Path.Combine(workspaceRoot, "target.txt");
		string linkFilePath = Path.Combine(workspaceRoot, "link.txt");

		try
		{
			Directory.CreateDirectory(workspaceRoot);
			File.WriteAllText(targetFilePath, "return 1");
			File.CreateSymbolicLink(linkFilePath, targetFilePath);

			var tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();

			string normalizedLinkPath = LanguageServerPaths.NormalizeLocalPath(linkFilePath);
			tracker.ApplyChanges([new WorkspaceFileChange(normalizedLinkPath, FileChangeKind.Changed)]);

			// The apply path enforces the same reparse-point exclusion as the capture walk, so a watcher event for
			// a symbolic link cannot create an entry that a later full capture would drop again.
			Assert.IsFalse(tracker.CloneTrackedSnapshot().ContainsKey(normalizedLinkPath));
		}
		catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or NotSupportedException)
		{
			Assert.Inconclusive("Symbolic link creation is not available on this host: " + exception.Message);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public void Constructor_NullOrBlankArguments_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceSnapshotTracker(null!, []));
		Assert.ThrowsExactly<ArgumentNullException>(() => new WorkspaceSnapshotTracker("C:\\Workspace", null!));
		Assert.ThrowsExactly<ArgumentException>(() => new WorkspaceSnapshotTracker(" ", []));
	}

	[TestMethod]
	public void ReplaceTrackedSnapshotWithCurrent_ReturnsACloneThatLaterChangesDoNotMutate()
	{
		var workspace = new TemporaryDirectory("WorkspaceSnapshotClone_");
		string workspaceRoot = workspace.Path;
		string firstFilePath = Path.Combine(workspaceRoot, "first.txt");
		string secondFilePath = Path.Combine(workspaceRoot, "second.txt");

		try
		{
			Directory.CreateDirectory(workspaceRoot);
			File.WriteAllText(firstFilePath, "return 1");

			var tracker = CreateTracker(workspaceRoot);
			Dictionary<string, WorkspaceSnapshotEntry> capturedSnapshot = tracker.ReplaceTrackedSnapshotWithCurrent();

			int capturedCount = capturedSnapshot.Count;

			File.WriteAllText(secondFilePath, "return 2");
			tracker.ApplyChanges([new WorkspaceFileChange(LanguageServerPaths.NormalizeLocalPath(secondFilePath), FileChangeKind.Created)]);

			Assert.AreEqual(capturedCount, capturedSnapshot.Count,
				"The returned snapshot must not be the live tracked dictionary.");
			Assert.AreEqual(capturedCount + 1, tracker.CloneTrackedSnapshot().Count);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public async Task ReplaceTrackedSnapshotWithCurrent_ConcurrentWithApplyChanges_StaysConsistent()
	{
		var workspace = new TemporaryDirectory("WorkspaceSnapshotRace_");
		string workspaceRoot = workspace.Path;
		string scriptsDirectoryPath = Path.Combine(workspaceRoot, "Scripts");

		try
		{
			Directory.CreateDirectory(scriptsDirectoryPath);

			for (int i = 0; i < 32; i++)
				File.WriteAllText(Path.Combine(scriptsDirectoryPath, "file" + i + ".txt"), "return " + i);

			var tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();

			using var startGate = new ManualResetEventSlim(false);

			Task replaceTask = Task.Run(() =>
			{
				startGate.Wait();

				for (int i = 0; i < 120; i++)
					tracker.ReplaceTrackedSnapshotWithCurrent();
			});

			Task applyTask = Task.Run(() =>
			{
				startGate.Wait();

				for (int i = 0; i < 120; i++)
				{
					string path = Path.Combine(scriptsDirectoryPath, "new" + i + ".txt");
					File.WriteAllText(path, "return " + i);

					tracker.ApplyChanges([new WorkspaceFileChange(LanguageServerPaths.NormalizeLocalPath(path), FileChangeKind.Created)]);
				}
			});

			startGate.Set();

			// A clone that enumerates the live tracked dictionary outside the snapshot lock can collide with an
			// in-place apply commit and throw InvalidOperationException; the tracker must stay consistent instead.
			await Task.WhenAll(replaceTask, applyTask).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

			Assert.IsTrue(tracker.CloneTrackedSnapshot().Count >= 32);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public void ApplyChanges_ManyDistinctPaths_DoesNotAccumulateCommitMarkers()
	{
		var workspace = new TemporaryDirectory("WorkspaceSnapshotTicketBound_");
		string workspaceRoot = workspace.Path;
		string scriptsDirectoryPath = Path.Combine(workspaceRoot, "Scripts");

		try
		{
			Directory.CreateDirectory(scriptsDirectoryPath);

			var tracker = CreateTracker(workspaceRoot);

			for (int i = 0; i < 32; i++)
			{
				string filePath = Path.Combine(scriptsDirectoryPath, "tracked" + i + ".txt");
				File.WriteAllText(filePath, "return " + i);

				tracker.ApplyChanges([new WorkspaceFileChange(LanguageServerPaths.NormalizeLocalPath(filePath), FileChangeKind.Created)]);
			}

			// A completed apply with no overlapping capture or apply releases its per-path markers, so the map cannot
			// grow with the number of distinct paths committed between captures.
			Assert.AreEqual(0, tracker.LastCommittedApplyTicketCount);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public void ApplyChanges_WhenTheUpdateWalkThrows_DoesNotLeakTheInFlightTicket()
	{
		var workspace = new TemporaryDirectory("WorkspaceSnapshotTicketLeak_");
		string workspaceRoot = workspace.Path;
		string scriptsDirectoryPath = Path.Combine(workspaceRoot, "Scripts");

		try
		{
			Directory.CreateDirectory(scriptsDirectoryPath);

			string filePath = Path.Combine(scriptsDirectoryPath, "tracked.txt");
			File.WriteAllText(filePath, "return 1");

			string normalizedFilePath = LanguageServerPaths.NormalizeLocalPath(filePath);
			int walkAttempts = 0;

			// A failure after the ticket is claimed must release it: a leaked low ticket keeps the in-flight floor
			// from ever draining, so the per-path commit markers would never be pruned.
			var tracker = CreateTracker(workspaceRoot, new WorkspaceSnapshotTrackerTestHooks
			{
				SnapshotVersionRead = () =>
				{
					if (Interlocked.Increment(ref walkAttempts) == 1)
						throw new InvalidOperationException("update walk failed");
				}
			});

			Assert.ThrowsExactly<InvalidOperationException>(() =>
				tracker.ApplyChanges([new WorkspaceFileChange(normalizedFilePath, FileChangeKind.Created)]));

			File.WriteAllText(filePath, "return 2");
			tracker.ApplyChanges([new WorkspaceFileChange(normalizedFilePath, FileChangeKind.Created)]);

			Assert.AreEqual(0, tracker.LastCommittedApplyTicketCount);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public void CaptureTrackedSnapshot_WhenRootIsAbsent_KeepsThePreviousSnapshot()
	{
		var workspace = new TemporaryDirectory("WorkspaceSnapshotMissingRoot_");
		string workspaceRoot = workspace.Path;
		string scriptsDirectoryPath = Path.Combine(workspaceRoot, "Scripts");
		string trackedFilePath = Path.Combine(scriptsDirectoryPath, "tracked.txt");

		try
		{
			Directory.CreateDirectory(scriptsDirectoryPath);
			File.WriteAllText(trackedFilePath, "return 1");

			var tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();

			// A capture against a briefly-missing root must not install an empty snapshot; the previous capture
			// survives, so the later delta does not report every file as created.
			Directory.Delete(workspaceRoot, recursive: true);
			Dictionary<string, WorkspaceSnapshotEntry> snapshot = tracker.ReplaceTrackedSnapshotWithCurrent();

			Assert.AreEqual(1, snapshot.Count);
			Assert.IsTrue(snapshot.ContainsKey(LanguageServerPaths.NormalizeLocalPath(trackedFilePath)));
		}
		finally
		{
			workspace.Dispose();
		}
	}

	private static WorkspaceSnapshotTracker CreateTracker(string workspaceRootDirectoryPath, WorkspaceSnapshotTrackerTestHooks? testHooks = null) => new(
		LanguageServerPaths.NormalizeLocalPath(workspaceRootDirectoryPath),
		[
			new WorkspaceWatchSpecification("*.txt", IncludeSubdirectories: true),
			new WorkspaceWatchSpecification("Config", IncludeSubdirectories: false),
			new WorkspaceWatchSpecification(".settings.*", IncludeSubdirectories: false)
		])
	{
		TestHooks = testHooks ?? WorkspaceSnapshotTrackerTestHooks.None
	};

	private static WorkspaceFileChange GetChange(FileChangeBatch batch, string filePath)
		=> batch.Entries.First(change => string.Equals(change.Path, filePath, StringComparison.OrdinalIgnoreCase));
}
