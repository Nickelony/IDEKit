namespace Nickelony.IDEKit.FileWatching.Tests;

[TestClass]
public sealed class FileSnapshotTrackerTests
{
	[TestMethod]
	public void ApplyChanges_WithNullChanges_ThrowsBeforeClaimingAnOrderingTicket()
	{
		using var workspace = new TemporaryDirectory("WorkspaceSnapshotNullChanges_");

		FileSnapshotTracker tracker = CreateTracker(workspace.Path);

		Assert.ThrowsExactly<ArgumentNullException>(() => tracker.ApplyChanges(null!));

		// A later apply still commits: the rejected call left no ticket behind to hold the commit order back.
		string filePath = Path.Combine(workspace.Path, "tracked.txt");

		File.WriteAllText(filePath, "return 1");
		tracker.ApplyChanges([new FileChange(LocalPath.Normalize(filePath), FileChangeKind.Created)]);

		Assert.IsTrue(tracker.CloneTrackedSnapshot().ContainsKey(LocalPath.Normalize(filePath)));
	}

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

			FileSnapshotTracker tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();
			Dictionary<string, FileSnapshotEntry> previousSnapshot = tracker.CloneTrackedSnapshot();

			File.WriteAllText(changedFilePath, "return 123456");
			File.Delete(deletedFilePath);
			File.WriteAllText(createdFilePath, "return 3");

			Dictionary<string, FileSnapshotEntry> currentSnapshot = tracker.ReplaceTrackedSnapshotWithCurrent();
			FileChangeBatch batch = tracker.BuildDeltaBatch(previousSnapshot, currentSnapshot);

			string normalizedChangedFilePath = LocalPath.Normalize(changedFilePath);
			string normalizedDeletedFilePath = LocalPath.Normalize(deletedFilePath);
			string normalizedCreatedFilePath = LocalPath.Normalize(createdFilePath);

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

			FileSnapshotTracker tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();
			Dictionary<string, FileSnapshotEntry> previousSnapshot = tracker.CloneTrackedSnapshot();
			var currentSnapshot = new Dictionary<string, FileSnapshotEntry>(StringComparer.OrdinalIgnoreCase);

			FileChangeBatch batch = tracker.BuildDeltaBatch(previousSnapshot, currentSnapshot);

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

			FileSnapshotTracker tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();
			Dictionary<string, FileSnapshotEntry> previousSnapshot = tracker.CloneTrackedSnapshot();

			File.WriteAllText(changedFilePath, "return 2");
			File.SetLastWriteTimeUtc(changedFilePath, baselineWriteTime);

			Dictionary<string, FileSnapshotEntry> currentSnapshot = tracker.ReplaceTrackedSnapshotWithCurrent();
			FileChangeBatch batch = tracker.BuildDeltaBatch(previousSnapshot, currentSnapshot);

			string normalizedChangedFilePath = LocalPath.Normalize(changedFilePath);

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

			FileSnapshotTracker tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();
			Dictionary<string, FileSnapshotEntry> previousSnapshot = tracker.CloneTrackedSnapshot();

			File.WriteAllText(changedFilePath, new string(changedContent));
			File.SetLastWriteTimeUtc(changedFilePath, baselineWriteTime);

			Dictionary<string, FileSnapshotEntry> currentSnapshot = tracker.ReplaceTrackedSnapshotWithCurrent();
			FileChangeBatch batch = tracker.BuildDeltaBatch(previousSnapshot, currentSnapshot);

			string normalizedChangedFilePath = LocalPath.Normalize(changedFilePath);

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

			FileSnapshotTracker tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();

			File.WriteAllText(createdFilePath, "return 1");
			string normalizedCreatedFilePath = LocalPath.Normalize(createdFilePath);

			tracker.ApplyChanges([new FileChange(normalizedCreatedFilePath, FileChangeKind.Created)]);

			Dictionary<string, FileSnapshotEntry> previousSnapshot = tracker.CloneTrackedSnapshot();
			File.Delete(createdFilePath);

			Dictionary<string, FileSnapshotEntry> currentSnapshot = tracker.ReplaceTrackedSnapshotWithCurrent();
			FileChangeBatch batch = tracker.BuildDeltaBatch(previousSnapshot, currentSnapshot);

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

			string normalizedFirstFilePath = LocalPath.Normalize(firstFilePath);
			string normalizedSecondFilePath = LocalPath.Normalize(secondFilePath);

			// Hold the older batch between computing its updates and committing them, run the newer batch
			// completely, then let the older batch commit.
			using var olderBatchReachedCommit = new ManualResetEventSlim(false);
			using var releaseOlderBatch = new ManualResetEventSlim(false);

			bool holdOlderBatch = true;

			FileSnapshotTracker tracker = null!;

			tracker = CreateTracker(
				workspaceRoot,

				new FileSnapshotTrackerTestHooks
				{
					BeforeApplyCommit = () =>
					{
						if (!Volatile.Read(ref holdOlderBatch))
							return;

						olderBatchReachedCommit.Set();
						releaseOlderBatch.Wait(TestContext.CancellationToken);
					},
				}
			);

			tracker.CaptureTrackedSnapshot();

			File.WriteAllText(firstFilePath, "return 2");
			File.WriteAllText(secondFilePath, "return 2");

			var olderBatch = Task.Run(
				() =>
					tracker.ApplyChanges([
						new FileChange(normalizedFirstFilePath, FileChangeKind.Changed),
						new FileChange(normalizedSecondFilePath, FileChangeKind.Changed),
					]),

				TestContext.CancellationToken
			);

			Assert.IsTrue(olderBatchReachedCommit.Wait(TimeSpan.FromSeconds(10), TestContext.CancellationToken));

			// The newer batch commits first: it deletes the first path and leaves the second path untouched.
			Volatile.Write(ref holdOlderBatch, false);
			File.Delete(firstFilePath);

			tracker.ApplyChanges([new FileChange(normalizedFirstFilePath, FileChangeKind.Deleted)]);

			releaseOlderBatch.Set();
			olderBatch.Wait(TimeSpan.FromSeconds(10), TestContext.CancellationToken);

			Dictionary<string, FileSnapshotEntry> trackedSnapshot = tracker.CloneTrackedSnapshot();

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
		if (LocalPathComparisonPolicy.ForCurrentPlatform.IgnoreCase)
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

			FileSnapshotTracker tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();

			Dictionary<string, FileSnapshotEntry> snapshot = tracker.CloneTrackedSnapshot();

			Assert.HasCount(2, snapshot);
			Assert.IsTrue(snapshot.ContainsKey(LocalPath.Normalize(firstFilePath)));
			Assert.IsTrue(snapshot.ContainsKey(LocalPath.Normalize(secondFilePath)));
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

			FileSnapshotTracker tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();

			// The file-system watcher observes hidden files, so capture must include them or recovery could never
			// reconcile a hidden watched file after an outage.
			Assert.IsTrue(tracker.CloneTrackedSnapshot().ContainsKey(LocalPath.Normalize(hiddenFilePath)));
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

			FileSnapshotTracker tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();

			Dictionary<string, FileSnapshotEntry> snapshot = tracker.CloneTrackedSnapshot();

			Assert.IsTrue(snapshot.ContainsKey(LocalPath.Normalize(targetFilePath)));

			Assert.IsFalse(
				snapshot.ContainsKey(LocalPath.Normalize(linkFilePath)),
				"Reparse points are excluded because neither capture nor the watcher follows them consistently."
			);
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

		FileSnapshotTracker tracker = CreateTracker(workspaceRoot);
		tracker.CaptureTrackedSnapshot();

		Assert.IsEmpty(tracker.CloneTrackedSnapshot());
	}

	[TestMethod]
	public void ApplyChanges_WhenSnapshotWasReplacedAfterTheTicketWasClaimed_KeepsTheNewerObservation()
	{
		var workspace = new TemporaryDirectory("WorkspaceSnapshotVersion_");

		string workspaceRoot = workspace.Path;
		string changedFilePath = Path.Combine(workspaceRoot, "changed.txt");

		try
		{
			Directory.CreateDirectory(workspaceRoot);
			File.WriteAllText(changedFilePath, "return 1");

			string normalizedChangedFilePath = LocalPath.Normalize(changedFilePath);

			// Replace the tracked snapshot after the apply claimed its ticket but before it stamps its observation
			// version (simulating a preemption between the two), then change the file again. The capture read the
			// pre-modification file, while the apply's own observation is newer, so the newer observation must win.
			FileSnapshotTracker tracker = null!;

			tracker = CreateTracker(
				workspaceRoot,

				new FileSnapshotTrackerTestHooks
				{
					BeforeSnapshotVersionRead = () =>
					{
						tracker.CaptureTrackedSnapshot();
						File.WriteAllText(changedFilePath, "return 22222");
					},
				}
			);

			tracker.CaptureTrackedSnapshot();

			tracker.ApplyChanges([new FileChange(normalizedChangedFilePath, FileChangeKind.Changed)]);

			// The intermediate capture recorded the pre-modification file ("return 1", 8 bytes); the apply observed
			// the 12-byte state afterwards, which is newer than the capture and must be committed.
			Assert.AreEqual(12, tracker.CloneTrackedSnapshot()[normalizedChangedFilePath].Length);
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

			string normalizedChangedFilePath = LocalPath.Normalize(changedFilePath);
			bool captureHookEnabled = false;

			FileSnapshotTracker tracker = null!;

			tracker = CreateTracker(
				workspaceRoot,

				new FileSnapshotTrackerTestHooks
				{
					BeforeCapturePublish = () =>
					{
						if (!Volatile.Read(ref captureHookEnabled))
							return;

						File.WriteAllText(changedFilePath, "return 22222");
						tracker.ApplyChanges([new FileChange(normalizedChangedFilePath, FileChangeKind.Changed)]);
					},
				}
			);

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

			FileSnapshotTracker tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();

			string normalizedLinkPath = LocalPath.Normalize(linkFilePath);
			tracker.ApplyChanges([new FileChange(normalizedLinkPath, FileChangeKind.Changed)]);

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

			FileSnapshotTracker tracker = CreateTracker(workspaceRoot);
			Dictionary<string, FileSnapshotEntry> capturedSnapshot = tracker.ReplaceTrackedSnapshotWithCurrent();

			int capturedCount = capturedSnapshot.Count;

			File.WriteAllText(secondFilePath, "return 2");
			tracker.ApplyChanges([new FileChange(LocalPath.Normalize(secondFilePath), FileChangeKind.Created)]);

			Assert.HasCount(capturedCount, capturedSnapshot, "The returned snapshot must not be the live tracked dictionary.");
			Assert.HasCount(capturedCount + 1, tracker.CloneTrackedSnapshot());
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

			FileSnapshotTracker tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();

			using var startGate = new ManualResetEventSlim(false);

			var replaceTask = Task.Run(
				() =>
				{
					startGate.Wait(TestContext.CancellationToken);

					for (int i = 0; i < 120; i++)
						tracker.ReplaceTrackedSnapshotWithCurrent();
				},

				TestContext.CancellationToken
			);

			var applyTask = Task.Run(
				() =>
				{
					startGate.Wait(TestContext.CancellationToken);

					for (int i = 0; i < 120; i++)
					{
						string path = Path.Combine(scriptsDirectoryPath, "new" + i + ".txt");
						File.WriteAllText(path, "return " + i);

						tracker.ApplyChanges([new FileChange(LocalPath.Normalize(path), FileChangeKind.Created)]);
					}
				},

				TestContext.CancellationToken
			);

			startGate.Set();

			// A clone that enumerates the live tracked dictionary outside the snapshot lock can collide with an
			// in-place apply commit and throw InvalidOperationException; the tracker must stay consistent instead.
			await Task.WhenAll(replaceTask, applyTask).WaitAsync(TimeSpan.FromSeconds(30), TestContext.CancellationToken).ConfigureAwait(false);

			Assert.IsGreaterThanOrEqualTo(32, tracker.CloneTrackedSnapshot().Count);
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

			FileSnapshotTracker tracker = CreateTracker(workspaceRoot);

			for (int i = 0; i < 32; i++)
			{
				string filePath = Path.Combine(scriptsDirectoryPath, "tracked" + i + ".txt");
				File.WriteAllText(filePath, "return " + i);

				tracker.ApplyChanges([new FileChange(LocalPath.Normalize(filePath), FileChangeKind.Created)]);
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

			string normalizedFilePath = LocalPath.Normalize(filePath);
			int walkAttempts = 0;

			// A failure after the ticket is claimed must release it: a leaked low ticket keeps the in-flight floor
			// from ever draining, so the per-path commit markers would never be pruned.
			FileSnapshotTracker tracker = CreateTracker(
				workspaceRoot,

				new FileSnapshotTrackerTestHooks
				{
					BeforeSnapshotVersionRead = () =>
					{
						if (Interlocked.Increment(ref walkAttempts) == 1)
							throw new InvalidOperationException("update walk failed");
					},
				}
			);

			Assert.ThrowsExactly<InvalidOperationException>(() => tracker.ApplyChanges([new FileChange(normalizedFilePath, FileChangeKind.Created)]));

			File.WriteAllText(filePath, "return 2");
			tracker.ApplyChanges([new FileChange(normalizedFilePath, FileChangeKind.Created)]);

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

			FileSnapshotTracker tracker = CreateTracker(workspaceRoot);
			tracker.CaptureTrackedSnapshot();

			// A capture against a briefly-missing root must not install an empty snapshot; the previous capture
			// survives, so the later delta does not report every file as created.
			Directory.Delete(workspaceRoot, recursive: true);

			Dictionary<string, FileSnapshotEntry> snapshot = tracker.ReplaceTrackedSnapshotWithCurrent();

			Assert.HasCount(1, snapshot);
			Assert.IsTrue(snapshot.ContainsKey(LocalPath.Normalize(trackedFilePath)));
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public void CaptureTrackedSnapshot_ForARecursiveLiteralFilter_RecordsSubdirectoryCopies()
	{
		var workspace = new TemporaryDirectory("WorkspaceSnapshotLiteralRecursive_");

		string workspaceRoot = workspace.Path;
		string rootFilePath = Path.Combine(workspaceRoot, "myconfig.json");
		string nestedDirectoryPath = Path.Combine(workspaceRoot, "Nested", "Deeper");
		string nestedFilePath = Path.Combine(nestedDirectoryPath, "myconfig.json");
		string unrelatedFilePath = Path.Combine(nestedDirectoryPath, "other.json");

		try
		{
			Directory.CreateDirectory(nestedDirectoryPath);

			File.WriteAllText(rootFilePath, "{}");
			File.WriteAllText(nestedFilePath, "{}");
			File.WriteAllText(unrelatedFilePath, "{}");

			// The watcher observes a literal filter's copies in any subdirectory when it recurses, so the capture
			// must record them too; a root-only capture would make a recovery delta report the nested copies as
			// deleted.
			var tracker = new FileSnapshotTracker(
				LocalPath.Normalize(workspaceRoot),
				[new FileWatchSpecification("myconfig.json", IncludeSubdirectories: true)]
			);

			tracker.CaptureTrackedSnapshot();

			Dictionary<string, FileSnapshotEntry> snapshot = tracker.CloneTrackedSnapshot();

			Assert.IsTrue(snapshot.ContainsKey(LocalPath.Normalize(rootFilePath)));
			Assert.IsTrue(snapshot.ContainsKey(LocalPath.Normalize(nestedFilePath)));
			Assert.IsFalse(snapshot.ContainsKey(LocalPath.Normalize(unrelatedFilePath)));
			Assert.HasCount(2, snapshot);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	private static FileSnapshotTracker CreateTracker(string workspaceRootDirectoryPath, FileSnapshotTrackerTestHooks? testHooks = null)
	{
		return new(
			LocalPath.Normalize(workspaceRootDirectoryPath),

			[
				new FileWatchSpecification("*.txt", IncludeSubdirectories: true),
				new FileWatchSpecification("Config", IncludeSubdirectories: false),
				new FileWatchSpecification(".settings.*", IncludeSubdirectories: false),
			]
		)
		{
			TestHooks = testHooks ?? FileSnapshotTrackerTestHooks.None,
		};
	}

	private static FileChange GetChange(FileChangeBatch batch, string filePath) =>
		batch.Entries.First(change => string.Equals(change.Path, filePath, StringComparison.OrdinalIgnoreCase));

	public TestContext? TestContext { get; set; }
}
