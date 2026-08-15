using Nickelony.IDEKit.Core.Pathing;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.Reloading;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
public sealed class FileReloadCoordinatorTests
{
	private const string ReloadPromptResult = "reload";
	private const string KeepPromptResult = "keep";

	[TestMethod]
	public async Task QueueFile_IgnoresBlankAndDuplicatePaths()
	{
		var coordinator = new FileReloadCoordinator<string>();
		var reloadedPaths = new List<string>();

		coordinator.QueueFile(string.Empty);
		coordinator.QueueFile("  ");
		coordinator.QueueFile(@"workspace/scripts/script.txt");
		coordinator.QueueFile(@"workspace/scripts/script.txt");

		await coordinator.ProcessQueuedFilesAsync(CreateCallbacks(filePath =>
		{
			reloadedPaths.Add(filePath);
			return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
		}));

		// The duplicate queue attempt must not produce a second reload; counting invocations is what
		// makes this test fail if de-duplication regresses.
		CollectionAssert.AreEqual(new[] { @"workspace/scripts/script.txt" }, reloadedPaths.ToArray());
	}

	[TestMethod]
	public async Task QueueFile_DuplicateComparison_FollowsConfiguredPolicy()
	{
		var caseInsensitive = new FileReloadCoordinator<string>(
			new WorkspaceDocumentOptions { PathComparison = LocalPathComparisonPolicy.CaseInsensitive });
		int insensitiveReloads = 0;
		caseInsensitive.QueueFile(@"workspace/scripts/script.txt");
		caseInsensitive.QueueFile(@"workspace/scripts/SCRIPT.TXT");
		await caseInsensitive.ProcessQueuedFilesAsync(CreateCallbacks(filePath =>
		{
			insensitiveReloads++;
			return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
		}));

		var caseSensitive = new FileReloadCoordinator<string>(
			new WorkspaceDocumentOptions { PathComparison = LocalPathComparisonPolicy.CaseSensitive });
		int sensitiveReloads = 0;
		caseSensitive.QueueFile(@"workspace/scripts/script.txt");
		caseSensitive.QueueFile(@"workspace/scripts/SCRIPT.TXT");
		await caseSensitive.ProcessQueuedFilesAsync(CreateCallbacks(filePath =>
		{
			sensitiveReloads++;
			return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
		}));

		Assert.AreEqual(1, insensitiveReloads);
		Assert.AreEqual(2, sensitiveReloads);
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_ReloadedAndUnchangedResults_AreSkipped()
	{
		var coordinator = new FileReloadCoordinator<string>();
		var failures = new List<WorkspaceDocumentReloadResult>();

		coordinator.QueueFile(@"workspace/scripts/reloaded.txt");
		coordinator.QueueFile(@"workspace/scripts/unchanged.txt");
		coordinator.QueueFile(@"workspace/scripts/failed.txt");

		await coordinator.ProcessQueuedFilesAsync(CreateCallbacks(
			filePath => CreateReloadResult(
				filePath,
				filePath.EndsWith("reloaded.txt", StringComparison.Ordinal)
					? WorkspaceDocumentReloadOutcome.Reloaded
					: filePath.EndsWith("unchanged.txt", StringComparison.Ordinal)
						? WorkspaceDocumentReloadOutcome.Unchanged
						: WorkspaceDocumentReloadOutcome.ReadFailed),
			failures.Add));

		Assert.AreEqual(1, failures.Count);
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ReadFailed, failures[0].Outcome);
		Assert.AreEqual(@"workspace/scripts/failed.txt", failures[0].RequestedIdentity.DocumentId);
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_ResolvedConflict_SkipsFailureReporting()
	{
		var coordinator = new FileReloadCoordinator<string>();
		string? promptedDocumentId = null;
		WorkspaceDocumentReloadOutcome? promptedOutcome = null;
		string? forwardedChoice = null;
		var failures = new List<WorkspaceDocumentReloadResult>();

		coordinator.QueueFile(@"workspace/scripts/conflict.txt");

		await coordinator.ProcessQueuedFilesAsync(new FileReloadHooks<string>(
			(result, _) =>
			{
				promptedDocumentId = result.RequestedIdentity.DocumentId;
				promptedOutcome = result.Outcome;
				return Task.FromResult(KeepPromptResult);
			},
			(filePath, _) => Task.FromResult(CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.ExternalFileConflict)),
			(result, _) =>
			{
				failures.Add(result);
				return Task.CompletedTask;
			},
			(result, choice, _) =>
			{
				// The stub resolves with logical content regardless of the prompt result, but records
				// the forwarded choice so the prompt-to-resolver data flow is pinned.
				forwardedChoice = choice;
				return Task.FromResult<WorkspaceDocumentConflictResolutionResult?>(new WorkspaceDocumentConflictResolutionResult(
					WorkspaceDocumentConflictResolutionOutcome.ResolvedWithLogical,
					WorkspaceDocumentConflictResolutionChoice.UseLogical,
					result.RequestedIdentity,
					result.Snapshot,
					ObservedOnDiskStamp: null,
					Failure: null));
			}));

		Assert.AreEqual(@"workspace/scripts/conflict.txt", promptedDocumentId);
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ExternalFileConflict, promptedOutcome);
		Assert.AreEqual(KeepPromptResult, forwardedChoice);
		Assert.AreEqual(0, failures.Count);
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_UnresolvedConflict_IsReported()
	{
		var coordinator = new FileReloadCoordinator<string>();
		var failures = new List<WorkspaceDocumentReloadResult>();

		coordinator.QueueFile(@"workspace/scripts/conflict.txt");

		await coordinator.ProcessQueuedFilesAsync(CreateCallbacks(
			filePath => CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.ExternalFileConflict),
			failures.Add,
			(result, _) => null));

		Assert.AreEqual(1, failures.Count);
		Assert.AreEqual(@"workspace/scripts/conflict.txt", failures[0].RequestedIdentity.DocumentId);
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_ConflictWithoutResolver_IsReported()
	{
		var coordinator = new FileReloadCoordinator<string>();
		var failures = new List<WorkspaceDocumentReloadResult>();
		int promptCount = 0;

		coordinator.QueueFile(@"workspace/scripts/conflict.txt");

		// A coordinator without a resolver reports the conflict through the failure callback without
		// prompting: an answer that no resolver would consume must not reach the host.
		await coordinator.ProcessQueuedFilesAsync(new FileReloadHooks<string>(
			(_, _) =>
			{
				promptCount++;
				return Task.FromResult(ReloadPromptResult);
			},
			(filePath, _) => Task.FromResult(CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.ExternalFileConflict)),
			(result, _) =>
			{
				failures.Add(result);
				return Task.CompletedTask;
			}));

		Assert.AreEqual(0, promptCount);
		Assert.AreEqual(1, failures.Count);
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ExternalFileConflict, failures[0].Outcome);
		Assert.AreEqual(@"workspace/scripts/conflict.txt", failures[0].RequestedIdentity.DocumentId);
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_PathQueuedByCallback_StaysForTheNextPass()
	{
		var coordinator = new FileReloadCoordinator<string>();
		int reloadCount = 0;

		coordinator.QueueFile(@"workspace/scripts/one.txt");

		await coordinator.ProcessQueuedFilesAsync(new FileReloadHooks<string>(
			(_, _) => Task.FromResult(ReloadPromptResult),
			async (filePath, cancellationToken) =>
			{
				coordinator.QueueFile(@"workspace/scripts/two.txt");

				// A pass that observes another pass already running returns without processing.
				await coordinator.ProcessQueuedFilesAsync(new FileReloadHooks<string>(
					(_, _) => Task.FromResult(ReloadPromptResult),
					(path, _) =>
					{
						reloadCount++;
						return Task.FromResult(CreateReloadResult(path, WorkspaceDocumentReloadOutcome.Reloaded));
					}));

				reloadCount++;
				return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
			}));

		Assert.AreEqual(1, reloadCount);
		Assert.IsFalse(coordinator.IsRunning);

		// The path queued while the pass ran stayed queued and is processed by the next pass.
		await coordinator.ProcessQueuedFilesAsync(CreateCallbacks(filePath =>
		{
			reloadCount++;
			return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
		}));

		Assert.AreEqual(2, reloadCount);
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_ReentrantCallWhileRunning_LeavesWorkVisibleThroughPendingFileCount()
	{
		var coordinator = new FileReloadCoordinator<string>();
		int pendingCountSeenInsideThePass = -1;

		coordinator.QueueFile(@"workspace/scripts/one.txt");

		await coordinator.ProcessQueuedFilesAsync(new FileReloadHooks<string>(
			(_, _) => Task.FromResult(ReloadPromptResult),
			async (filePath, cancellationToken) =>
			{
				coordinator.QueueFile(@"workspace/scripts/two.txt");

				// A call that lands while the pass runs returns without processing; the pending count keeps
				// the queued work visible - the file still being processed plus the one queued just now.
				await coordinator.ProcessQueuedFilesAsync(new FileReloadHooks<string>(
					(_, _) => Task.FromResult(ReloadPromptResult),
					(path, _) => Task.FromResult(CreateReloadResult(path, WorkspaceDocumentReloadOutcome.Reloaded))));

				pendingCountSeenInsideThePass = coordinator.PendingFileCount;

				return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
			}));

		Assert.AreEqual(2, pendingCountSeenInsideThePass);
		Assert.AreEqual(1, coordinator.PendingFileCount, "Only the file queued during the pass remains pending afterwards.");
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_FileQueuedDuringPass_IsProcessedByNextPass()
	{
		var coordinator = new FileReloadCoordinator<string>();
		var reloaded = new List<string>();

		coordinator.QueueFile(@"workspace/scripts/one.txt");
		await coordinator.ProcessQueuedFilesAsync(CreateCallbacks(filePath =>
		{
			if (filePath.EndsWith("one.txt", StringComparison.Ordinal))
				coordinator.QueueFile(@"workspace/scripts/two.txt");

			reloaded.Add(filePath);
			return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
		}));

		CollectionAssert.AreEqual(new[] { @"workspace/scripts/one.txt" }, reloaded.ToArray());

		await coordinator.ProcessQueuedFilesAsync(CreateCallbacks(filePath =>
		{
			reloaded.Add(filePath);
			return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
		}));

		CollectionAssert.AreEqual(
			new[] { @"workspace/scripts/one.txt", @"workspace/scripts/two.txt" },
			reloaded.ToArray());
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_PathRequeuedDuringItsOwnPass_IsProcessedByNextPass()
	{
		var coordinator = new FileReloadCoordinator<string>();
		var reloaded = new List<string>();

		coordinator.QueueFile(@"workspace/scripts/one.txt");
		await coordinator.ProcessQueuedFilesAsync(CreateCallbacks(filePath =>
		{
			// The path currently being processed can change again; the re-queue must not be dropped as
			// a duplicate of the entry the pass already dequeued.
			coordinator.QueueFile(@"workspace/scripts/one.txt");
			reloaded.Add(filePath);
			return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
		}));

		CollectionAssert.AreEqual(new[] { @"workspace/scripts/one.txt" }, reloaded.ToArray());

		await coordinator.ProcessQueuedFilesAsync(CreateCallbacks(filePath =>
		{
			reloaded.Add(filePath);
			return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
		}));

		// The path queued during its own pass is processed by the next pass instead of being lost.
		CollectionAssert.AreEqual(new[] { @"workspace/scripts/one.txt", @"workspace/scripts/one.txt" }, reloaded.ToArray());
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_PathRequeuedDuringItsOwnThrowingPass_IsNotDuplicated()
	{
		var coordinator = new FileReloadCoordinator<string>();
		var reloaded = new List<string>();

		coordinator.QueueFile(@"workspace/scripts/one.txt");

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => coordinator.ProcessQueuedFilesAsync(new FileReloadHooks<string>(
			(_, _) => Task.FromResult(ReloadPromptResult),
			(filePath, _) =>
			{
				// The callback re-queues its own path and then fails: the failed pass keeps the path
				// queued, and the re-queue must not add a second entry for it.
				coordinator.QueueFile(filePath);
				throw new InvalidOperationException("Host callback failure.");
			})));

		await coordinator.ProcessQueuedFilesAsync(CreateCallbacks(filePath =>
		{
			reloaded.Add(filePath);
			return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
		}));

		// One entry per path: the re-queued copy was folded into the entry the failed pass retained.
		CollectionAssert.AreEqual(new[] { @"workspace/scripts/one.txt" }, reloaded.ToArray());
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_CallbackThrows_RetainsPendingEntriesAndResetsRunning()
	{
		var coordinator = new FileReloadCoordinator<string>();
		var reloaded = new List<string>();

		coordinator.QueueFile(@"workspace/scripts/one.txt");
		coordinator.QueueFile(@"workspace/scripts/throw.txt");
		coordinator.QueueFile(@"workspace/scripts/three.txt");

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => coordinator.ProcessQueuedFilesAsync(CreateCallbacks(filePath =>
		{
			if (filePath.EndsWith("throw.txt", StringComparison.Ordinal))
				throw new InvalidOperationException("Host callback failure.");

			reloaded.Add(filePath);
			return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
		})));

		Assert.IsFalse(coordinator.IsRunning);
		CollectionAssert.AreEqual(new[] { @"workspace/scripts/one.txt" }, reloaded.ToArray());

		// The entry whose callback threw and the entries behind it stay queued for the next pass.
		await coordinator.ProcessQueuedFilesAsync(CreateCallbacks(filePath =>
		{
			reloaded.Add(filePath);
			return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
		}));

		CollectionAssert.AreEqual(
			new[] { @"workspace/scripts/one.txt", @"workspace/scripts/throw.txt", @"workspace/scripts/three.txt" },
			reloaded.ToArray());
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_FailedResolution_IsReportedAsOriginalConflict()
	{
		var coordinator = new FileReloadCoordinator<string>();
		var failures = new List<WorkspaceDocumentReloadResult>();

		coordinator.QueueFile(@"workspace/scripts/conflict.txt");

		await coordinator.ProcessQueuedFilesAsync(CreateCallbacks(
			filePath => CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.ExternalFileConflict),
			failures.Add,
			(result, _) => new WorkspaceDocumentConflictResolutionResult(
				WorkspaceDocumentConflictResolutionOutcome.ExternalFileConflict,
				WorkspaceDocumentConflictResolutionChoice.UseDisk,
				result.RequestedIdentity,
				result.Snapshot,
				ObservedOnDiskStamp: null,
				Failure: null)));

		// The coordinator reports the original conflict; the resolution outcome is not surfaced.
		Assert.AreEqual(1, failures.Count);
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ExternalFileConflict, failures[0].Outcome);
		Assert.AreEqual(@"workspace/scripts/conflict.txt", failures[0].RequestedIdentity.DocumentId);
	}

	[TestMethod]
	public async Task QueueFile_NullPath_ThrowsAndBlankPathIsIgnoredByTheNextPass()
	{
		var coordinator = new FileReloadCoordinator<string>();
		var reloaded = new List<string>();

		Assert.ThrowsExactly<ArgumentNullException>(() => coordinator.QueueFile(null!));
		coordinator.QueueFile("   ");

		// A blank path is not queued, so a pass over the coordinator has nothing to process.
		await coordinator.ProcessQueuedFilesAsync(CreateCallbacks(filePath =>
		{
			reloaded.Add(filePath);
			return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
		}));

		Assert.IsEmpty(reloaded);
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_NullReloadDocument_AlwaysThrowsAndNullDecisionThrowsOnlyWithResolver()
	{
		var coordinator = new FileReloadCoordinator<string>();

		// A missing reload delegate is always an error.
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => coordinator.ProcessQueuedFilesAsync(
			new FileReloadHooks<string>((_, _) => Task.FromResult(ReloadPromptResult), null!)));

		// A missing decision delegate is an error only when a resolver would consume the decision.
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => coordinator.ProcessQueuedFilesAsync(
			new FileReloadHooks<string>(
				null,
				(_, _) => Task.FromResult(CreateReloadResult("one.txt", WorkspaceDocumentReloadOutcome.Reloaded)),
				ReportReloadFailure: null,
				ResolveConflict: (_, _, _) => Task.FromResult<WorkspaceDocumentConflictResolutionResult?>(null))));
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_CanceledResult_StopsPassWithoutReportingFailure()
	{
		var coordinator = new FileReloadCoordinator<string>();
		var reloaded = new List<string>();
		var reported = new List<WorkspaceDocumentReloadResult>();

		coordinator.QueueFile(@"workspace/scripts/one.txt");
		coordinator.QueueFile(@"workspace/scripts/two.txt");

		await coordinator.ProcessQueuedFilesAsync(CreateCallbacks(
			filePath =>
			{
				// The store reports Canceled for the first path - for example because it was disposed while
				// the reload ran; the pass stops without reporting a failure.
				reloaded.Add(filePath);
				return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Canceled);
			},
			reportReloadFailure: reported.Add));

		CollectionAssert.AreEqual(new[] { @"workspace/scripts/one.txt" }, reloaded.ToArray());
		Assert.IsEmpty(reported);

		// The canceled entry and the entries after it stayed queued for a later pass.
		await coordinator.ProcessQueuedFilesAsync(CreateCallbacks(filePath =>
		{
			reloaded.Add(filePath);
			return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
		}));

		CollectionAssert.AreEqual(
			new[] { @"workspace/scripts/one.txt", @"workspace/scripts/one.txt", @"workspace/scripts/two.txt" },
			reloaded.ToArray());
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_CanceledMidPass_KeepsRemainingEntriesAndResetsRunning()
	{
		var coordinator = new FileReloadCoordinator<string>();
		var reloaded = new List<string>();
		using var cancellation = new CancellationTokenSource();

		coordinator.QueueFile(@"workspace/scripts/one.txt");
		coordinator.QueueFile(@"workspace/scripts/two.txt");

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => coordinator.ProcessQueuedFilesAsync(
			CreateCallbacks(filePath =>
			{
				// The host cancels while the first path is processed; the pass stops before the second
				// path and must retain it for the next pass.
				reloaded.Add(filePath);
				cancellation.Cancel();
				return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
			}),
			cancellation.Token));

		Assert.IsFalse(coordinator.IsRunning);
		CollectionAssert.AreEqual(new[] { @"workspace/scripts/one.txt" }, reloaded.ToArray());

		await coordinator.ProcessQueuedFilesAsync(CreateCallbacks(filePath =>
		{
			reloaded.Add(filePath);
			return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
		}));

		CollectionAssert.AreEqual(
			new[] { @"workspace/scripts/one.txt", @"workspace/scripts/two.txt" },
			reloaded.ToArray());
	}

	private static FileReloadHooks<string> CreateCallbacks(
		Func<string, WorkspaceDocumentReloadResult> reloadDocument,
		Action<WorkspaceDocumentReloadResult>? reportReloadFailure = null,
		Func<WorkspaceDocumentReloadResult, string, WorkspaceDocumentConflictResolutionResult?>? resolveConflict = null)
		=> new(
			(_, _) => Task.FromResult(ReloadPromptResult),
			(filePath, _) => Task.FromResult(reloadDocument(filePath)),
			reportReloadFailure is null
				? null
				: (result, _) =>
				{
					reportReloadFailure(result);
					return Task.CompletedTask;
				},
			resolveConflict is null
				? null
				: (result, choice, _) => Task.FromResult(resolveConflict(result, choice)));

	private static WorkspaceDocumentReloadResult CreateReloadResult(string filePath, WorkspaceDocumentReloadOutcome outcome)
		=> new(outcome, new(new WorkspaceDocumentKey(Guid.NewGuid()), filePath, 0), null);
}
