using System.Diagnostics.CodeAnalysis;
using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Reloading;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
public sealed partial class FileReloadCoordinatorTests
{
	private const string ReloadPromptResult = "reload";
	private const string KeepPromptResult = "keep";

	[TestMethod]
	public async Task QueueFiles_IgnoresBlankAndDuplicatePaths()
	{
		var reloadedPaths = new List<string>();

		FileReloadCoordinator<string> coordinator = CreateCoordinator(
			CreateHooks(filePath =>
			{
				reloadedPaths.Add(filePath);
				return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
			})
		);

		coordinator.QueueFiles([string.Empty, "  ", @"workspace/scripts/script.txt", @"workspace/scripts/script.txt"]);
		await coordinator.ProcessQueuedFilesAsync(TestContext.CancellationToken);

		// The duplicate queue attempt must not produce a second reload; counting invocations is what
		// makes this test fail if de-duplication regresses.
		Assert.AreSequenceEqual([@"workspace/scripts/script.txt"], [.. reloadedPaths]);
	}

	[TestMethod]
	[DataRow(true, 1)]
	[DataRow(false, 2)]
	public async Task QueueFiles_DuplicateComparison_FollowsTheStorePolicy(bool ignoreCase, int expectedReloads)
	{
		int reloads = 0;
		var documents = new TrackingReader(new WorkspaceOptions { PathComparison = new LocalPathComparisonPolicy(ignoreCase) });

		var coordinator = new FileReloadCoordinator<string>(
			documents,

			CreateHooks(filePath =>
			{
				reloads++;
				return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
			})
		);

		coordinator.QueueFiles([@"workspace/scripts/script.txt", @"workspace/scripts/SCRIPT.TXT"]);
		await coordinator.ProcessQueuedFilesAsync(TestContext.CancellationToken);

		Assert.AreEqual(expectedReloads, reloads);
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_ReloadedAndUnchangedResults_AreSkipped()
	{
		var failures = new List<WorkspaceDocumentReloadResult>();

		FileReloadCoordinator<string> coordinator = CreateCoordinator(
			CreateHooks(
				filePath =>
					CreateReloadResult(
						filePath,

						filePath.EndsWith("reloaded.txt", StringComparison.Ordinal) ? WorkspaceDocumentReloadOutcome.Reloaded
							: filePath.EndsWith("unchanged.txt", StringComparison.Ordinal) ? WorkspaceDocumentReloadOutcome.Unchanged
							: WorkspaceDocumentReloadOutcome.ReadFailed
					),

				failures.Add
			)
		);

		coordinator.QueueFiles([@"workspace/scripts/reloaded.txt", @"workspace/scripts/unchanged.txt", @"workspace/scripts/failed.txt"]);
		await coordinator.ProcessQueuedFilesAsync(TestContext.CancellationToken);

		Assert.HasCount(1, failures);
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ReadFailed, failures[0].Outcome);
		Assert.AreEqual(@"workspace/scripts/failed.txt", failures[0].RequestedIdentity.DocumentId);
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_AnUntrackedPath_IsDroppedWithoutAReload()
	{
		var reloaded = new List<string>();
		var documents = new TrackingReader(WorkspaceOptions.Default, untracked: @"workspace/scripts/closed.txt");

		var coordinator = new FileReloadCoordinator<string>(
			documents,

			CreateHooks(filePath =>
			{
				reloaded.Add(filePath);
				return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
			})
		);

		coordinator.QueueFiles([@"workspace/scripts/closed.txt", @"workspace/scripts/open.txt"]);
		await coordinator.ProcessQueuedFilesAsync(TestContext.CancellationToken);

		Assert.AreSequenceEqual([@"workspace/scripts/open.txt"], [.. reloaded]);
		Assert.AreEqual(0, coordinator.PendingFileCount);
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_ResolvedConflict_SkipsFailureReporting()
	{
		string? promptedDocumentId = null;
		WorkspaceDocumentReloadOutcome? promptedOutcome = null;
		string? forwardedChoice = null;

		var failures = new List<WorkspaceDocumentReloadResult>();

		FileReloadCoordinator<string> coordinator = CreateCoordinator(
			new FileReloadHooks<string>
			{
				DecideReload = (result, _) =>
				{
					promptedDocumentId = result.RequestedIdentity.DocumentId;
					promptedOutcome = result.Outcome;
					return Task.FromResult(KeepPromptResult);
				},

				ReloadDocument = (filePath, _) => Task.FromResult(CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.ExternalFileConflict)),

				ReportReloadFailure = (result, _) =>
				{
					failures.Add(result);
					return Task.CompletedTask;
				},

				ResolveConflict = (result, choice, _) =>
				{
					// The stub resolves with logical content regardless of the prompt result, but records
					// the forwarded choice so the prompt-to-resolver data flow is pinned.
					forwardedChoice = choice;

					return Task.FromResult<WorkspaceDocumentConflictResolutionResult?>(
						new WorkspaceDocumentConflictResolutionResult(
							Outcome: WorkspaceDocumentConflictResolutionOutcome.ResolvedWithLogical,
							Mode: WorkspaceDocumentConflictResolutionMode.ResolveWithLogical,
							RequestedIdentity: result.RequestedIdentity,
							Snapshot: result.Snapshot,
							ObservedOnDiskStamp: null,
							Failure: null
						)
					);
				},
			}
		);

		coordinator.QueueFiles([@"workspace/scripts/conflict.txt"]);
		await coordinator.ProcessQueuedFilesAsync(TestContext.CancellationToken);

		Assert.AreEqual(@"workspace/scripts/conflict.txt", promptedDocumentId);
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ExternalFileConflict, promptedOutcome);
		Assert.AreEqual(KeepPromptResult, forwardedChoice);
		Assert.IsEmpty(failures);
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_UnresolvedConflict_IsReported()
	{
		var failures = new List<WorkspaceDocumentReloadResult>();

		FileReloadCoordinator<string> coordinator = CreateCoordinator(
			CreateHooks(
				filePath => CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.ExternalFileConflict),
				failures.Add,
				(result, _) => null
			)
		);

		coordinator.QueueFiles([@"workspace/scripts/conflict.txt"]);
		await coordinator.ProcessQueuedFilesAsync(TestContext.CancellationToken);

		Assert.HasCount(1, failures);
		Assert.AreEqual(@"workspace/scripts/conflict.txt", failures[0].RequestedIdentity.DocumentId);
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_ConflictWithoutResolver_IsReported()
	{
		var failures = new List<WorkspaceDocumentReloadResult>();
		int promptCount = 0;

		// A coordinator without a resolver reports the conflict through the failure callback without
		// prompting: an answer that no resolver would consume must not reach the host.
		FileReloadCoordinator<string> coordinator = CreateCoordinator(
			new FileReloadHooks<string>
			{
				DecideReload = (_, _) =>
				{
					promptCount++;
					return Task.FromResult(ReloadPromptResult);
				},

				ReloadDocument = (filePath, _) => Task.FromResult(CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.ExternalFileConflict)),

				ReportReloadFailure = (result, _) =>
				{
					failures.Add(result);
					return Task.CompletedTask;
				},
			}
		);

		coordinator.QueueFiles([@"workspace/scripts/conflict.txt"]);
		await coordinator.ProcessQueuedFilesAsync(TestContext.CancellationToken);

		Assert.AreEqual(0, promptCount);
		Assert.HasCount(1, failures);
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ExternalFileConflict, failures[0].Outcome);
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_FileQueuedDuringThePass_IsReloadedByTheSamePass()
	{
		var reloaded = new List<string>();

		FileReloadCoordinator<string>? coordinator = null;
		int pendingCountSeenInsideThePass = -1;

		coordinator = CreateCoordinator(
			CreateHooks(filePath =>
			{
				if (filePath.EndsWith("one.txt", StringComparison.Ordinal))
				{
					coordinator!.QueueFiles([@"workspace/scripts/two.txt"]);
					pendingCountSeenInsideThePass = coordinator.PendingFileCount;
				}

				reloaded.Add(filePath);
				return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
			})
		);

		coordinator.QueueFiles([@"workspace/scripts/one.txt"]);
		await coordinator.ProcessQueuedFilesAsync(TestContext.CancellationToken);

		// The file being reloaded stays counted until it is done, beside the one queued during its reload.
		Assert.AreEqual(2, pendingCountSeenInsideThePass);
		Assert.AreSequenceEqual([@"workspace/scripts/one.txt", @"workspace/scripts/two.txt"], [.. reloaded]);
		Assert.AreEqual(0, coordinator.PendingFileCount);
		Assert.IsFalse(coordinator.IsRunning);
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_PathRequeuedDuringItsOwnReload_IsReloadedAgain()
	{
		var reloaded = new List<string>();
		FileReloadCoordinator<string>? coordinator = null;

		coordinator = CreateCoordinator(
			CreateHooks(filePath =>
			{
				// The file can change again while it is reloaded, for example when a resolution writes it; the re-queue must not be dropped as a
				// duplicate of the entry being reloaded.
				if (reloaded.Count == 0)
					coordinator!.QueueFiles([filePath]);

				reloaded.Add(filePath);
				return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
			})
		);

		coordinator.QueueFiles([@"workspace/scripts/one.txt"]);
		await coordinator.ProcessQueuedFilesAsync(TestContext.CancellationToken);

		Assert.AreSequenceEqual([@"workspace/scripts/one.txt", @"workspace/scripts/one.txt"], [.. reloaded]);
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_ACallWhileAPassRuns_JoinsIt_AndThePassStartsOnTheCallerThread()
	{
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var reloaded = new List<string>();

		int callerThread = Environment.CurrentManagedThreadId;
		int hookThread = -1;
		int firstHookThread = -1;

		FileReloadCoordinator<string> coordinator = CreateCoordinator(
			new FileReloadHooks<string>
			{
				ReloadDocument = async (filePath, _) =>
				{
					hookThread = Environment.CurrentManagedThreadId;

					if (firstHookThread < 0)
						firstHookThread = hookThread;

					await release.Task;

					lock (reloaded)
						reloaded.Add(filePath);

					return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
				},
			}
		);

		coordinator.QueueFiles([@"workspace/scripts/one.txt"]);
		Task first = coordinator.ProcessQueuedFilesAsync(TestContext.CancellationToken);

		coordinator.QueueFiles([@"workspace/scripts/two.txt"]);
		Task joined = coordinator.ProcessQueuedFilesAsync(TestContext.CancellationToken);

		Assert.IsTrue(coordinator.IsRunning);
		release.SetResult();
		await Task.WhenAll(first, joined);

		Assert.AreSequenceEqual([@"workspace/scripts/one.txt", @"workspace/scripts/two.txt"], [.. reloaded]);

		// A host whose hooks need its UI thread starts the pass there; the hooks after the first await may run elsewhere.
		Assert.AreEqual(callerThread, firstHookThread);
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_PathRequeuedDuringItsOwnThrowingReload_IsNotDuplicated()
	{
		var reloaded = new List<string>();

		bool fail = true;
		FileReloadCoordinator<string>? coordinator = null;

		coordinator = CreateCoordinator(
			CreateHooks(filePath =>
			{
				if (fail)
				{
					// The callback re-queues its own path and then fails: the failed pass keeps the path
					// queued, and the re-queue must not add a second entry for it.
					coordinator!.QueueFiles([filePath]);
					throw new InvalidOperationException("Host callback failure.");
				}

				reloaded.Add(filePath);
				return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
			})
		);

		coordinator.QueueFiles([@"workspace/scripts/one.txt"]);
		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => coordinator.ProcessQueuedFilesAsync(TestContext.CancellationToken));

		fail = false;
		await coordinator.ProcessQueuedFilesAsync(TestContext.CancellationToken);

		// One entry per path: the re-queued copy was folded into the entry the failed pass retained.
		Assert.AreSequenceEqual([@"workspace/scripts/one.txt"], [.. reloaded]);
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_CallbackThrows_RetainsPendingEntriesAndEndsThePass()
	{
		var reloaded = new List<string>();
		bool fail = true;

		FileReloadCoordinator<string> coordinator = CreateCoordinator(
			CreateHooks(filePath =>
			{
				if (fail && filePath.EndsWith("throw.txt", StringComparison.Ordinal))
					throw new InvalidOperationException("Host callback failure.");

				reloaded.Add(filePath);
				return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
			})
		);

		coordinator.QueueFiles([@"workspace/scripts/one.txt", @"workspace/scripts/throw.txt", @"workspace/scripts/three.txt"]);
		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => coordinator.ProcessQueuedFilesAsync(TestContext.CancellationToken));

		Assert.IsFalse(coordinator.IsRunning);
		Assert.AreSequenceEqual([@"workspace/scripts/one.txt"], [.. reloaded]);

		// The entry whose callback threw and the entries behind it stay queued for the next pass.
		fail = false;
		await coordinator.ProcessQueuedFilesAsync(TestContext.CancellationToken);

		Assert.AreSequenceEqual([@"workspace/scripts/one.txt", @"workspace/scripts/throw.txt", @"workspace/scripts/three.txt"], [.. reloaded]);
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_FailedResolution_IsReportedAsOriginalConflict()
	{
		var failures = new List<WorkspaceDocumentReloadResult>();

		FileReloadCoordinator<string> coordinator = CreateCoordinator(
			CreateHooks(
				filePath => CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.ExternalFileConflict),
				failures.Add,

				(result, _) =>
					new WorkspaceDocumentConflictResolutionResult(
						Outcome: WorkspaceDocumentConflictResolutionOutcome.ExternalFileConflict,
						Mode: WorkspaceDocumentConflictResolutionMode.ResolveWithDisk,
						RequestedIdentity: result.RequestedIdentity,
						Snapshot: result.Snapshot,
						ObservedOnDiskStamp: null,
						Failure: null
					)
			)
		);

		coordinator.QueueFiles([@"workspace/scripts/conflict.txt"]);
		await coordinator.ProcessQueuedFilesAsync(TestContext.CancellationToken);

		// The coordinator reports the original conflict; the resolution outcome is not surfaced.
		Assert.HasCount(1, failures);
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ExternalFileConflict, failures[0].Outcome);
	}

	[TestMethod]
	public void QueueFiles_ANullPath_Throws()
	{
		FileReloadCoordinator<string> coordinator = CreateCoordinator(
			CreateHooks(filePath => CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded))
		);

		Assert.ThrowsExactly<ArgumentNullException>(() => coordinator.QueueFiles(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => coordinator.QueueFiles([null!]));
	}

	[TestMethod]
	public void Constructor_AResolverWithoutADecision_Throws()
	{
		// A missing decision delegate is an error only when a resolver would consume the decision.
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			CreateCoordinator(
				new FileReloadHooks<string>
				{
					ReloadDocument = (_, _) => Task.FromResult(CreateReloadResult("one.txt", WorkspaceDocumentReloadOutcome.Reloaded)),
					ResolveConflict = (_, _, _) => Task.FromResult<WorkspaceDocumentConflictResolutionResult?>(null),
				}
			)
		);
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_CanceledResult_StopsPassWithoutReportingFailure()
	{
		var reloaded = new List<string>();
		var reported = new List<WorkspaceDocumentReloadResult>();

		WorkspaceDocumentReloadOutcome outcome = WorkspaceDocumentReloadOutcome.Canceled;

		FileReloadCoordinator<string> coordinator = CreateCoordinator(
			CreateHooks(
				filePath =>
				{
					// The store reports Canceled for the first path - for example because it was disposed while
					// the reload ran; the pass stops without reporting a failure.
					reloaded.Add(filePath);
					return CreateReloadResult(filePath, outcome);
				},

				reportReloadFailure: reported.Add
			)
		);

		coordinator.QueueFiles([@"workspace/scripts/one.txt", @"workspace/scripts/two.txt"]);
		await coordinator.ProcessQueuedFilesAsync(TestContext.CancellationToken);

		Assert.AreSequenceEqual([@"workspace/scripts/one.txt"], [.. reloaded]);
		Assert.IsEmpty(reported);

		// The canceled entry and the entries after it stayed queued for a later pass.
		outcome = WorkspaceDocumentReloadOutcome.Reloaded;
		await coordinator.ProcessQueuedFilesAsync(TestContext.CancellationToken);

		Assert.AreSequenceEqual([@"workspace/scripts/one.txt", @"workspace/scripts/one.txt", @"workspace/scripts/two.txt"], [.. reloaded]);
	}

	[TestMethod]
	public async Task ProcessQueuedFilesAsync_CanceledMidPass_KeepsRemainingEntriesAndEndsThePass()
	{
		var reloaded = new List<string>();
		using var cancellation = new CancellationTokenSource();

		FileReloadCoordinator<string> coordinator = CreateCoordinator(
			CreateHooks(filePath =>
			{
				// The host cancels while the first path is processed; the pass stops before the second
				// path and must retain it for the next pass.
				reloaded.Add(filePath);
				cancellation.Cancel();
				return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
			})
		);

		coordinator.QueueFiles([@"workspace/scripts/one.txt", @"workspace/scripts/two.txt"]);
		await Assert.ThrowsAsync<OperationCanceledException>(() => coordinator.ProcessQueuedFilesAsync(cancellation.Token));

		Assert.IsFalse(coordinator.IsRunning);
		Assert.AreSequenceEqual([@"workspace/scripts/one.txt"], [.. reloaded]);

		await coordinator.ProcessQueuedFilesAsync(TestContext.CancellationToken);

		Assert.AreSequenceEqual([@"workspace/scripts/one.txt", @"workspace/scripts/two.txt"], [.. reloaded]);
	}

	private static FileReloadCoordinator<string> CreateCoordinator(FileReloadHooks<string> hooks) =>
		new(new TrackingReader(WorkspaceOptions.Default), hooks);

	private static FileReloadHooks<string> CreateHooks(
		Func<string, WorkspaceDocumentReloadResult> reloadDocument,
		Action<WorkspaceDocumentReloadResult>? reportReloadFailure = null,
		Func<WorkspaceDocumentReloadResult, string, WorkspaceDocumentConflictResolutionResult?>? resolveConflict = null
	)
	{
		return new FileReloadHooks<string>
		{
			DecideReload = (_, _) => Task.FromResult(ReloadPromptResult),
			ReloadDocument = (filePath, _) => Task.FromResult(reloadDocument(filePath)),

			ReportReloadFailure = reportReloadFailure is null
				? null
				: (result, _) =>
				{
					reportReloadFailure(result);
					return Task.CompletedTask;
				},

			ResolveConflict = resolveConflict is null ? null : (result, choice, _) => Task.FromResult(resolveConflict(result, choice)),
		};
	}

	private static WorkspaceDocumentReloadResult CreateReloadResult(string filePath, WorkspaceDocumentReloadOutcome outcome) =>
		new(outcome, new(new WorkspaceDocumentKey(Guid.NewGuid()), filePath, 0), null);

	public TestContext? TestContext { get; set; }

	// Tracks every path except the one named untracked, so the coordinator reloads what a test queues.
	private sealed class TrackingReader(WorkspaceOptions options, string? untracked = null) : IWorkspaceDocumentReader
	{
		public WorkspaceOptions Options => options;

		public IReadOnlyList<WorkspaceDocumentSnapshot> GetSnapshotsUnderDirectory(string? directoryPath) => [];

		public bool TryGetSnapshot(string? filePath, [NotNullWhen(true)] out WorkspaceDocumentSnapshot? snapshot)
		{
			snapshot = filePath is not null && filePath != untracked ? TestSnapshots.Create(filePath, string.Empty) : null;
			return snapshot is not null;
		}
	}
}
