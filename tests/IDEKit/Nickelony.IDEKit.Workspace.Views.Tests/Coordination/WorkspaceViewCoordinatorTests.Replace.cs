using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceViewCoordinatorTests
{
	private static WorkspaceDocumentReplaceRequest ReplaceRequest(WorkspaceDocumentSnapshot snapshot, string text = "replaced") =>
		new(snapshot.Identity, text, snapshot.FileFormat);

	[TestMethod]
	public async Task ReplaceAsync_AViewWithPendingEdits_IsBlocked_AndTheStoreKeepsItsText()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot opened = await fixture.OpenViewAsync();
		fixture.View.HasPendingEdits = true;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await fixture.Coordinator.ReplaceAsync(
			ReplaceRequest(opened),
			TestContext.CancellationToken
		);

		Assert.IsNull(replaced.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, replaced.ViewSynchronization.Outcome);
		Assert.AreSequenceEqual([fixture.View.ViewId], ViewIdsOf(replaced.ViewSynchronization));
		Assert.AreEqual("initial", replaced.BlockedSnapshot?.Content);
		Assert.AreEqual(0, fixture.View.RefreshCount);
	}

	[TestMethod]
	public async Task ReplaceAsync_AViewWithAConflict_IsBlocked()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot opened = await fixture.OpenViewAsync();
		fixture.View.HasConflict = true;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await fixture.Coordinator.ReplaceAsync(
			ReplaceRequest(opened),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, replaced.ViewSynchronization.Outcome);
		Assert.IsTrue(fixture.Store.TryGetSnapshot(opened.DocumentId, out WorkspaceDocumentSnapshot? current));
		Assert.AreEqual("initial", current.Content);
	}

	[TestMethod]
	public async Task ReplaceAsync_AViewWhoseStateProbeThrows_IsBlockedWithTheException()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot opened = await fixture.OpenViewAsync();
		fixture.View.ThrowOnStateProbe = true;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await fixture.Coordinator.ReplaceAsync(
			ReplaceRequest(opened),
			TestContext.CancellationToken
		);

		WorkspaceDocumentViewSynchronizationIssue issue = Assert.ContainsSingle(replaced.ViewSynchronization.Issues);

		Assert.AreEqual(fixture.View.ViewId, issue.ViewId);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewStateProbeFailed, issue.Failure?.Code);
		Assert.IsInstanceOfType<InvalidOperationException>(issue.Failure?.Exception);
		Assert.IsNull(replaced.StoreResult);
	}

	[TestMethod]
	public async Task ReplaceAsync_AViewWhoseOnlyProblemIsAnEarlierFailedSynchronization_IsRefreshed()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot opened = await fixture.OpenViewAsync();
		await fixture.MarkViewUnsynchronizedAsync(opened);
		int refreshes = fixture.View.RefreshCount;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await fixture.Coordinator.ReplaceAsync(
			ReplaceRequest(opened),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, replaced.ViewSynchronization.Outcome);
		Assert.AreEqual(refreshes + 1, fixture.View.RefreshCount);
		Assert.AreEqual("replaced", fixture.View.Text);
	}

	[TestMethod]
	public async Task ReplaceAsync_ARefreshThatFails_IsReportedUnsynchronized()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot opened = await fixture.OpenViewAsync();
		fixture.View.FailRefresh = true;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await fixture.Coordinator.ReplaceAsync(
			ReplaceRequest(opened),
			TestContext.CancellationToken
		);

		fixture.View.FailRefresh = false;

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, replaced.ViewSynchronization.Outcome);
		Assert.AreSequenceEqual([fixture.View.ViewId], ViewIdsOf(replaced.ViewSynchronization));
	}

	[TestMethod]
	public async Task ReplaceAsync_ADisposedStore_ThrowsObjectDisposedException_AndRefreshesNoView()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot opened = await fixture.OpenViewAsync();
		await fixture.Store.DisposeAsync();

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
			fixture.Coordinator.ReplaceAsync(ReplaceRequest(opened), TestContext.CancellationToken)
		);

		Assert.AreEqual(0, fixture.View.RefreshCount);
	}

	[TestMethod]
	public async Task ReplaceAsync_ADispatcherThatFaultsAfterTheStoreChangedTheDocument_ReturnsTheResultAndReportsTheViews()
	{
		bool faultAfterRunning = false;

		await using var fixture = new CoordinatorTestHost(customDispatch: action =>
		{
			action();

			return faultAfterRunning
				? Task.FromException(new InvalidOperationException("The dispatcher faulted after the action."))
				: Task.CompletedTask;
		});

		WorkspaceDocumentSnapshot opened = await fixture.OpenViewAsync();
		faultAfterRunning = true;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await fixture.Coordinator.ReplaceAsync(
			ReplaceRequest(opened),
			TestContext.CancellationToken
		);

		faultAfterRunning = false;

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, replaced.ViewSynchronization.Outcome);

		WorkspaceDocumentViewSynchronizationIssue issue = Assert.ContainsSingle(replaced.ViewSynchronization.Issues);

		Assert.AreEqual(fixture.View.ViewId, issue.ViewId);
		Assert.IsInstanceOfType<InvalidOperationException>(issue.Failure?.Exception);
	}

	[TestMethod]
	public async Task ReplaceAsync_TypingQueuedBehindTheReplacement_RunsAfterTheCheckTheReplacementAndTheRefresh()
	{
		Queue<(Action Action, TaskCompletionSource Completion)> queue = new();
		bool inline = false;

		// Once the test body ends, the dispatcher runs each action inline, so the teardown of a failed test does not wait for a pump.
		await using var fixture = new CoordinatorTestHost(customDispatch: action =>
		{
			if (inline)
			{
				action();
				return Task.CompletedTask;
			}

			TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
			queue.Enqueue((action, completion));
			return completion.Task;
		});

		WorkspaceViewCoordinatorOpenResult open = await RunQueuedAsync(
			fixture.Coordinator.OpenWithViewAsync(fixture.DocumentPath, s_openOptions, fixture.View)
		);

		// The replacement queues its action before it returns, and the typing lands behind it on the views' thread.
		Task<WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult>> replacing = fixture.Coordinator.ReplaceAsync(
			ReplaceRequest(open.Snapshot!),
			TestContext.CancellationToken
		);

		int refreshesBeforeTyping = -1;

		queue.Enqueue(
			(
				() =>
				{
					refreshesBeforeTyping = fixture.View.RefreshCount;
					fixture.View.HasPendingEdits = true;
				},
				new TaskCompletionSource()
			)
		);

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await RunQueuedAsync(replacing);
		inline = true;

		while (queue.TryDequeue(out (Action Action, TaskCompletionSource Completion) next))
		{
			next.Action();
			next.Completion.TrySetResult();
		}

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, replaced.ViewSynchronization.Outcome);
		Assert.AreEqual(1, refreshesBeforeTyping);
		Assert.AreEqual("replaced", fixture.View.Text);

		fixture.View.HasPendingEdits = false;

		// Runs the queued actions until the task completes.
		async Task<T> RunQueuedAsync<T>(Task<T> task)
		{
			while (!task.IsCompleted)
			{
				if (queue.TryDequeue(out (Action Action, TaskCompletionSource Completion) next))
				{
					next.Action();
					next.Completion.TrySetResult();
				}
				else
				{
					await Task.Delay(1, TestContext.CancellationToken);
				}
			}

			return await task;
		}
	}
}
