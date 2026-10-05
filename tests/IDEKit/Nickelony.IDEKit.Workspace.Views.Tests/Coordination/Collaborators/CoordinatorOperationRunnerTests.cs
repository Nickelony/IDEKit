using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

/// <summary>Covers the admission of an operation and the stop that waits for it directly.</summary>
[TestClass]
public sealed class CoordinatorOperationRunnerTests
{
	private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

	private sealed class TestHost
	{
		internal TestHost()
		{
			Gate = new StopGate<Task>(SyncRoot);

			Tracker = new ViewTracker(
				owner: this,
				SyncRoot,
				stopGate: Gate,
				options: new WorkspaceOptions { PathComparison = LocalPathComparisonPolicy.CaseSensitive },
				onApplyRequested: static (_, _) => { }
			);

			var changes = new AttachedDocumentChangeForwarder(
				SyncRoot,
				views: Tracker,
				dispatchViewAction: Dispatch,
				sender: this,
				readHandlers: () => ChangeRaised,
				post: static action => action()
			);

			Operations = new CoordinatorOperationRunner(owner: this, SyncRoot, stopGate: Gate, attachedChanges: changes);
			Teardown = new CoordinatorTeardown(SyncRoot, stopGate: Gate, views: Tracker, dispatchViewAction: Dispatch, attachedChanges: changes);
			ChangeRaised += (_, change) => Changes.Add(change);
		}

		internal event EventHandler<AttachedDocumentChangedEventArgs>? ChangeRaised;

		internal object SyncRoot { get; } = new();

		internal StopGate<Task> Gate { get; }

		internal ViewTracker Tracker { get; }

		internal CoordinatorOperationRunner Operations { get; }

		internal CoordinatorTeardown Teardown { get; }

		/// <summary>The changes the forwarder raised, in order.</summary>
		internal List<AttachedDocumentChangedEventArgs> Changes { get; } = [];

		/// <summary>Whether the dispatcher faults before it runs an action, as a dispatcher that is shutting down does.</summary>
		internal bool FailDispatch { get; set; }

		/// <summary>Queues an attached-document change, as the attach of a first view does.</summary>
		internal void QueueChange() => Tracker.Register(new FakeWorkspaceView("view"), "view", TestSnapshots.Create("doc", "content"));

		private Task Dispatch(Action action)
		{
			if (FailDispatch)
				return Task.FromException(new InvalidOperationException("The dispatcher failed."));

			action();
			return Task.CompletedTask;
		}
	}

	[TestMethod]
	public async Task RunAsync_StopAsyncWaitsForARunningOperation()
	{
		var host = new TestHost();

		TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<int> operation = host.Operations.RunAsync(async () =>
		{
			entered.SetResult();
			await release.Task.ConfigureAwait(false);
			return 7;
		});

		await entered.Task.WaitAsync(s_timeout, TestContext.CancellationToken);

		Task stop = host.Teardown.StopAsync();
		Assert.IsFalse(stop.IsCompleted);

		release.SetResult();

		Assert.AreEqual(7, await operation.WaitAsync(s_timeout, TestContext.CancellationToken));
		await stop.WaitAsync(s_timeout, TestContext.CancellationToken);
	}

	[TestMethod]
	public async Task RunAsync_AnOperationThatThrows_EndsItsRegistration()
	{
		var host = new TestHost();

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
			host.Operations.RunAsync<int>(() => throw new InvalidOperationException("The operation failed."))
		);

		await host.Teardown.StopAsync().WaitAsync(s_timeout, TestContext.CancellationToken);
	}

	[TestMethod]
	public async Task RunAsync_AFinishedOperation_IsNoLongerRegistered_ForAFlowItStarted()
	{
		var host = new TestHost();

		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Task? lateStop = null;

		// The flow that the operation starts carries the operation's mark beyond its end, and a stop from that flow is an ordinary stop then.
		await host.Operations.RunAsync(() =>
		{
			lateStop = Task.Run(
				async () =>
				{
					await release.Task.ConfigureAwait(false);
					await host.Teardown.StopAsync().ConfigureAwait(false);
				},

				TestContext.CancellationToken
			);

			return Task.FromResult(0);
		});

		release.SetResult();

		await lateStop!.WaitAsync(s_timeout, TestContext.CancellationToken);
	}

	[TestMethod]
	public async Task RunAsync_AfterTheStopBegan_RejectsTheOperationWithoutRunningIt()
	{
		var host = new TestHost();
		bool ran = false;

		await host.Teardown.StopAsync().WaitAsync(s_timeout, TestContext.CancellationToken);

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
			host.Operations.RunAsync(() =>
			{
				ran = true;
				return Task.FromResult(0);
			})
		);

		Assert.IsFalse(ran);
	}

	[TestMethod]
	public async Task RunAsync_RaisesTheQueuedChangesBeforeItReturns()
	{
		var host = new TestHost();

		int result = await host.Operations.RunAsync(() =>
		{
			host.QueueChange();
			return Task.FromResult(3);
		});

		Assert.AreEqual(3, result);
		Assert.AreEqual(AttachedDocumentChangeKind.Opened, host.Changes.Single().Kind);
	}

	[TestMethod]
	public async Task RunAsync_AnOperationThatThrows_RaisesItsChangesAndThrowsItsOwnFault()
	{
		var host = new TestHost();

		InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
			host.Operations.RunAsync<int>(() =>
			{
				host.QueueChange();
				throw new InvalidOperationException("The operation failed.");
			})
		);

		Assert.AreEqual("The operation failed.", exception.Message);
		Assert.AreEqual(AttachedDocumentChangeKind.Opened, host.Changes.Single().Kind);
	}

	[TestMethod]
	public async Task RunAsync_AnOperationThatThrowsWhileTheRaiseFaults_ThrowsTheFaultOfTheOperation()
	{
		var host = new TestHost();

		InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
			host.Operations.RunAsync<int>(() =>
			{
				host.QueueChange();
				host.FailDispatch = true;
				throw new InvalidOperationException("The operation failed.");
			})
		);

		Assert.AreEqual("The operation failed.", exception.Message);
	}

	[TestMethod]
	public async Task RunAsync_ContainingTheRaiseFault_ReturnsTheResultAndLeavesTheChangesForTheNextOperation()
	{
		var host = new TestHost();

		int result = await host.Operations.RunAsync(
			() =>
			{
				host.QueueChange();
				host.FailDispatch = true;
				return Task.FromResult(5);
			},

			containRaiseFault: true
		);

		Assert.AreEqual(5, result);
		Assert.IsEmpty(host.Changes);

		host.FailDispatch = false;
		await host.Operations.RunAsync(() => Task.FromResult(0));

		Assert.AreEqual(AttachedDocumentChangeKind.Opened, host.Changes.Single().Kind);
	}

	[TestMethod]
	public async Task RunAsync_NotContainingTheRaiseFault_ThrowsIt()
	{
		var host = new TestHost();

		InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
			host.Operations.RunAsync(() =>
			{
				host.QueueChange();
				host.FailDispatch = true;
				return Task.FromResult(5);
			})
		);

		Assert.AreEqual("The dispatcher failed.", exception.Message);
	}

	public required TestContext TestContext { get; set; }
}
