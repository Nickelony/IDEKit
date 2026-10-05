using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceViewCoordinatorTests
{
	[TestMethod]
	public async Task StopAsync_FromAViewMemberOfAnActiveOperation_ThrowsAndTheOperationCompletes()
	{
		await using var fixture = new CoordinatorTestHost();

		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		InvalidOperationException? thrown = null;

		fixture.View.OnRefresh = () =>
		{
			fixture.View.OnRefresh = null;
			thrown = Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Coordinator.StopAsync());
		};

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await ReplaceAsync(fixture, snapshot);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.StoreResult.Outcome);
		Assert.IsNotNull(thrown);
		Assert.Contains("inside an operation", thrown.Message);

		// The refused call began no stop, and the same call after the operation ended is an ordinary stop.
		await fixture.Coordinator.StopAsync();

		Assert.AreEqual(1, fixture.View.CloseCount);
	}

	[TestMethod]
	public async Task StopAsync_FromADispatchedActionOnAnotherThread_Throws()
	{
		await using var fixture = new CoordinatorTestHost(pumpDispatch: true);

		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		InvalidOperationException? thrown = null;
		int actionThreadId = 0;

		fixture.View.OnRefresh = () =>
		{
			fixture.View.OnRefresh = null;
			actionThreadId = Environment.CurrentManagedThreadId;
			thrown = Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Coordinator.StopAsync());
		};

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await ReplaceAsync(fixture, snapshot);

		// The pump thread started before the operation, so the mark reaches it only through the coordinator's dispatch.
		Assert.AreEqual(fixture.PumpThreadId, actionThreadId);
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.StoreResult.Outcome);
		Assert.IsNotNull(thrown);

		await fixture.Coordinator.StopAsync();

		Assert.AreEqual(1, fixture.View.CloseCount);
	}

	[TestMethod]
	public async Task StopAsync_ForwardedThroughTaskRunFromAViewMember_Throws()
	{
		await using var fixture = new CoordinatorTestHost();

		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		InvalidOperationException? thrown = null;

		fixture.View.OnRefresh = () =>
		{
			fixture.View.OnRefresh = null;

			// The action waits for the forwarded call, so the operation is still in flight when the call runs.
			Task<InvalidOperationException?> forwarded = Task.Run(() => TryStop(fixture.Coordinator));

			thrown = forwarded.Wait(TimeSpan.FromSeconds(10)) ? forwarded.Result : null;
		};

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await ReplaceAsync(fixture, snapshot);

		// The call would not deadlock, but the mark follows the work an operation starts, and the contract is never to
		// request the stop from there.
		Assert.IsNotNull(thrown);
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.StoreResult.Outcome);

		await fixture.Coordinator.StopAsync();

		Assert.AreEqual(1, fixture.View.CloseCount);
	}

	[TestMethod]
	public async Task StopAsync_AwaitedByTheDispatchDelegateOfAnActiveOperation_FaultsTheOperationWithoutWaiting()
	{
		WorkspaceViewCoordinator? coordinator = null;
		bool stopInsideDispatch = false;

		await using var fixture = new CoordinatorTestHost(customDispatch: action =>
		{
			action();

			// The bound turns a missing lease into a fault of the operation instead of a stop that waits for the call that requested it.
			return stopInsideDispatch ? coordinator?.StopAsync().WaitAsync(TimeSpan.FromSeconds(10)) ?? Task.CompletedTask : Task.CompletedTask;
		});

		coordinator = fixture.Coordinator;

		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		stopInsideDispatch = true;

		// A replacement returns its result once the store changed the document, so a discard, which does not contain the fault, carries the stop.
		InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
			fixture.Coordinator.DiscardAsync(new WorkspaceDocumentDiscardRequest(snapshot.Identity))
		);

		stopInsideDispatch = false;

		Assert.Contains("inside an operation", exception.Message);

		await fixture.Coordinator.StopAsync();

		Assert.AreEqual(1, fixture.View.CloseCount);
	}

	[TestMethod]
	public async Task StopAsync_FromADeleteLeaseOnAnotherThread_Throws()
	{
		await using var fixture = new CoordinatorTestHost(pumpDispatch: true);

		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		InvalidOperationException? thrown = null;

		fixture.View.OnDeleteLeaseEntered = () =>
		{
			fixture.View.OnDeleteLeaseEntered = null;
			thrown = TryStop(fixture.Coordinator);
		};

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> deleted = await fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			CancellationToken.None
		);

		// The delete leases enter through the same dispatch the coordinator wraps for its own actions.
		Assert.IsNotNull(thrown);
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, deleted.StoreResult?.Outcome);
	}

	private static InvalidOperationException? TryStop(WorkspaceViewCoordinator coordinator)
	{
		try
		{
			_ = coordinator.StopAsync();
			return null;
		}
		catch (InvalidOperationException exception)
		{
			return exception;
		}
	}

	private static Task<WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult>> ReplaceAsync(
		CoordinatorTestHost fixture,
		WorkspaceDocumentSnapshot snapshot
	)
	{
		return fixture.Coordinator.ReplaceAsync(
			new WorkspaceDocumentReplaceRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), "edited", snapshot.FileFormat)
		);
	}
}
