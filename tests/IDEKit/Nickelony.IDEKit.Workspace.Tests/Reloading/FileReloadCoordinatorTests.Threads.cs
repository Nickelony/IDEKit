using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Reloading;

namespace Nickelony.IDEKit.Workspace.Tests;

// A dedicated thread stands for the dispatcher thread of a host: the pass starts there, and each hook runs where its documentation says.
public sealed partial class FileReloadCoordinatorTests
{
	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public async Task ReloadDocument_RunsOnTheStartingThread_UntilAHookCompletesAsynchronously(bool earlierHookIsAsynchronous)
	{
		var hookThreads = new List<Thread>();

		FileReloadCoordinator<string> coordinator = CreateCoordinator(
			new FileReloadHooks<string>
			{
				ReloadDocument = async (filePath, _) =>
				{
					lock (hookThreads)
						hookThreads.Add(Thread.CurrentThread);

					if (earlierHookIsAsynchronous)
						await Task.Yield();

					return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.Reloaded);
				},
			}
		);

		Thread starter = await StartPassOnADedicatedThreadAsync(coordinator, @"workspace/scripts/one.txt", @"workspace/scripts/two.txt");

		Assert.AreSame(starter, hookThreads[0]);
		AssertRanAfter(hookThreads[1], starter, earlierHookIsAsynchronous);
	}

	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public async Task DecideReload_RunsOnTheStartingThread_UntilAHookCompletesAsynchronously(bool earlierHookIsAsynchronous)
	{
		Thread? hookThread = null;

		FileReloadCoordinator<string> coordinator = CreateCoordinator(
			new FileReloadHooks<string>
			{
				ReloadDocument = async (filePath, _) =>
				{
					if (earlierHookIsAsynchronous)
						await Task.Yield();

					return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.ExternalFileConflict);
				},

				DecideReload = (_, _) =>
				{
					hookThread = Thread.CurrentThread;
					return Task.FromResult(KeepPromptResult);
				},

				ResolveConflict = (_, _, _) => Task.FromResult<WorkspaceDocumentConflictResolutionResult?>(null),
			}
		);

		Thread starter = await StartPassOnADedicatedThreadAsync(coordinator, @"workspace/scripts/conflict.txt");

		AssertRanAfter(hookThread, starter, earlierHookIsAsynchronous);
	}

	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public async Task ResolveConflict_RunsOnTheStartingThread_UntilAHookCompletesAsynchronously(bool earlierHookIsAsynchronous)
	{
		Thread? hookThread = null;

		FileReloadCoordinator<string> coordinator = CreateCoordinator(
			new FileReloadHooks<string>
			{
				ReloadDocument = (filePath, _) => Task.FromResult(CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.ExternalFileConflict)),

				DecideReload = async (_, _) =>
				{
					if (earlierHookIsAsynchronous)
						await Task.Yield();

					return KeepPromptResult;
				},

				ResolveConflict = (_, _, _) =>
				{
					hookThread = Thread.CurrentThread;
					return Task.FromResult<WorkspaceDocumentConflictResolutionResult?>(null);
				},
			}
		);

		Thread starter = await StartPassOnADedicatedThreadAsync(coordinator, @"workspace/scripts/conflict.txt");

		AssertRanAfter(hookThread, starter, earlierHookIsAsynchronous);
	}

	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public async Task ReportReloadFailure_RunsOnTheStartingThread_UntilAHookCompletesAsynchronously(bool earlierHookIsAsynchronous)
	{
		Thread? hookThread = null;

		FileReloadCoordinator<string> coordinator = CreateCoordinator(
			new FileReloadHooks<string>
			{
				ReloadDocument = async (filePath, _) =>
				{
					if (earlierHookIsAsynchronous)
						await Task.Yield();

					return CreateReloadResult(filePath, WorkspaceDocumentReloadOutcome.ReadFailed);
				},

				ReportReloadFailure = (_, _) =>
				{
					hookThread = Thread.CurrentThread;
					return Task.CompletedTask;
				},
			}
		);

		Thread starter = await StartPassOnADedicatedThreadAsync(coordinator, @"workspace/scripts/failed.txt");

		AssertRanAfter(hookThread, starter, earlierHookIsAsynchronous);
	}

	private async Task<Thread> StartPassOnADedicatedThreadAsync(FileReloadCoordinator<string> coordinator, params string[] filePaths)
	{
		Thread? starter = null;
		Task? pass = null;

		coordinator.QueueFiles(filePaths);

		TestThreads.Run(() =>
		{
			starter = Thread.CurrentThread;
			pass = coordinator.ProcessQueuedFilesAsync(TestContext.CancellationToken);
		});

		await pass!;

		return starter!;
	}

	private static void AssertRanAfter(Thread? hookThread, Thread starter, bool earlierHookIsAsynchronous)
	{
		Assert.IsNotNull(hookThread);

		if (earlierHookIsAsynchronous)
		{
			Assert.IsTrue(hookThread.IsThreadPoolThread, "A hook after an asynchronous one runs on the thread pool.");
			Assert.AreNotSame(starter, hookThread);
		}
		else
		{
			Assert.AreSame(starter, hookThread, "A hook runs on the starting thread until one completes asynchronously.");
		}
	}
}
