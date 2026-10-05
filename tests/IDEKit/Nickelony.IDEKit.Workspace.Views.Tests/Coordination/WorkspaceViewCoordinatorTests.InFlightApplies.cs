using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceViewCoordinatorTests
{
	[TestMethod]
	public async Task PublishPendingEditsAsync_AnApplyThatAViewRaisedBeforeTheCallIsStillInFlight_WaitsForIt()
	{
		List<(Action Action, TaskCompletionSource Completion)> pending = [];
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

			lock (pending)
				pending.Add((action, completion));

			return completion.Task;
		});

		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
		deadline.CancelAfter(TimeSpan.FromSeconds(10));

		try
		{
			Task<WorkspaceViewCoordinatorOpenResult> opening = fixture.Coordinator.OpenWithViewAsync(
				fixture.DocumentPath,
				s_openOptions,
				fixture.View
			);

			await RunPendingUntilCompletedAsync(opening);

			WorkspaceDocumentSnapshot opened = (await opening).Snapshot!;

			// The store applied the text at once, and the acknowledgment waits for the dispatcher, so the apply is in flight.
			fixture.View.RaiseApply(ReplaceRequest(opened, "published"));
			await WaitForPendingAsync(1);

			Task<WorkspaceViewCoordinatorPublicationResult> publishing = fixture.Coordinator.PublishPendingEditsAsync(
				opened.DocumentId,
				deadline.Token
			);

			// The pending actions are the acknowledgment of the apply and the publication of the views; the dispatcher runs the second first.
			await WaitForPendingAsync(2);
			RunAt(1);
			await Task.Delay(100, deadline.Token);

			// The final check follows the apply, so it is not dispatched while the acknowledgment waits.
			Assert.AreEqual(1, PendingCount());
			Assert.IsFalse(publishing.IsCompleted);

			await RunPendingUntilCompletedAsync(publishing);

			WorkspaceViewCoordinatorPublicationResult published = await publishing;

			Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, published.ViewSynchronization.Outcome);
			Assert.AreEqual("published", published.Snapshot?.Content);
		}
		finally
		{
			inline = true;

			while (PendingCount() > 0)
				RunAt(0);
		}

		int PendingCount()
		{
			lock (pending)
				return pending.Count;
		}

		void RunAt(int index)
		{
			(Action Action, TaskCompletionSource Completion) next;

			lock (pending)
			{
				next = pending[index];
				pending.RemoveAt(index);
			}

			next.Action();
			next.Completion.TrySetResult();
		}

		async Task WaitForPendingAsync(int count)
		{
			while (PendingCount() < count)
				await Task.Delay(1, deadline.Token);
		}

		async Task RunPendingUntilCompletedAsync(Task task)
		{
			while (!task.IsCompleted)
			{
				if (PendingCount() > 0)
					RunAt(0);
				else
					await Task.Delay(1, deadline.Token);
			}
		}
	}
}
