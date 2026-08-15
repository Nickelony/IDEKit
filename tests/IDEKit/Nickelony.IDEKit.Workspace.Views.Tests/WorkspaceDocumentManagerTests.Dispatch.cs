using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceDocumentManagerTests
{
	[TestMethod]
	public async Task PumpDispatch_RunsViewAccessOnThePumpThread()
	{
		await using var fixture = new ManagerFixture(pumpDispatch: true);
		int testThreadId = Environment.CurrentManagedThreadId;
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		WorkspaceDocumentManagerMutationResult replaced = await fixture.Manager.ReplaceAsync(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"edited",
			snapshot.FileFormat));
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.Outcome);

		await fixture.Manager.StopAsync();

		// The dedicated pump thread is the only thread that touches view members: the attach, the
		// refresh, the close, and the subscription removal during the stop all ran there.
		Assert.AreNotEqual(testThreadId, fixture.PumpThreadId, "The pump thread must differ from the calling thread.");
		Assert.AreEqual(fixture.PumpThreadId, fixture.View.LastOpenThreadId, "The attach runs on the pump thread.");
		Assert.AreEqual(fixture.PumpThreadId, fixture.View.LastRefreshThreadId, "The refresh runs on the pump thread.");
		Assert.AreEqual(fixture.PumpThreadId, fixture.View.LastCloseThreadId, "The stop closes views on the pump thread.");
		Assert.AreEqual(fixture.PumpThreadId, fixture.View.ApplyUnsubscribeThreadId, "The stop removes the subscription on the pump thread.");

		// The subscription add is the one view touch that is not dispatched: it runs on the thread
		// that resumes the attach, which is a pool thread for this queued delegate, not the pump.
		Assert.IsNotNull(fixture.View.ApplySubscribeThreadId);
		Assert.AreNotEqual(fixture.PumpThreadId, fixture.View.ApplySubscribeThreadId, "The subscription add is not dispatched.");
	}

	[TestMethod]
	public async Task PumpDispatch_ApplyRaisedOffThreadRunsTheAcknowledgmentOnThePump()
	{
		await using var fixture = new ManagerFixture(pumpDispatch: true);
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		int refreshBefore = fixture.View.RefreshCount;

		// The raise happens on the test thread while the apply pipeline runs on the pump, the shape a
		// real host uses when a background operation publishes through a view. The store mutation runs
		// on the raising thread and the acknowledgment runs on the pump; waiting for the refresh count
		// observes the acknowledgment instead of racing it.
		fixture.View.RaiseApply(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"edited",
			snapshot.FileFormat));

		Assert.IsTrue(
			SpinWait.SpinUntil(
				() => fixture.View.RefreshCount > refreshBefore,
				TimeSpan.FromSeconds(10)),
			"The apply acknowledgment must run through the pump.");
		Assert.IsTrue(fixture.Store.TryGetSnapshot(snapshot.DocumentId, out WorkspaceDocumentSnapshot? current));
		Assert.AreEqual("edited", current!.Content);
		Assert.AreEqual("edited", fixture.View.Text);
		Assert.AreEqual(fixture.PumpThreadId, fixture.View.LastRefreshThreadId, "The apply acknowledgment runs on the pump thread.");
	}

	[TestMethod]
	public async Task UnregisterOpenView_RemovesTheSubscriptionOnTheCallingThread()
	{
		await using var fixture = new ManagerFixture();
		await fixture.OpenViewAsync();
		Assert.AreEqual(1, fixture.View.ApplySubscriberCount);

		int callerThreadId = Environment.CurrentManagedThreadId;
		fixture.Manager.UnregisterOpenView(fixture.View);

		Assert.AreEqual(0, fixture.View.ApplySubscriberCount);
		Assert.AreEqual(callerThreadId, fixture.View.ApplyUnsubscribeThreadId);
	}

	[TestMethod]
	public async Task UnregisterOpenView_ThrowingEventAccessorIsIsolated()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.ThrowOnUnsubscribe = true;

		// The registration is removed before the accessor runs, so a throwing removal is ignored and a
		// later raise cannot reach the document.
		fixture.Manager.UnregisterOpenView(fixture.View);
		Assert.IsNull(fixture.View.ApplyUnsubscribeThreadId, "The throwing accessor did not complete.");

		fixture.View.RaiseApply(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"ignored",
			snapshot.FileFormat));

		Assert.IsTrue(fixture.Store.TryGetSnapshot(snapshot.DocumentId, out WorkspaceDocumentSnapshot? current));
		Assert.AreEqual("initial", current!.Content);
	}

	[TestMethod]
	public async Task StopAsync_DelegateFaultDuringTeardownFaultsTheStopCompletion()
	{
		await using var fixture = new ManagerFixture();
		await fixture.OpenViewAsync();
		fixture.FailDispatch = true;

		Task stop = fixture.Manager.StopAsync();
		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => stop);

		// The faulted completion is memoized: the manager stays stopped and the views stay registered
		// because the delegate never ran the teardown action.
		Assert.AreSame(stop, fixture.Manager.StopAsync());
		Assert.AreEqual(0, fixture.View.CloseCount);
		Assert.AreEqual(1, fixture.View.ApplySubscriberCount);
	}

	[TestMethod]
	public async Task Replace_DelegateFaultFaultsTheOperation()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.FailDispatch = true;

		// The delegate contract requires the action to run; a fault surfaces as a failure of the
		// operation (the store mutation itself already ran on the caller's context).
		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Manager.ReplaceAsync(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"edited",
			snapshot.FileFormat)));
	}

	[TestMethod]
	public async Task OpenWithView_InlineDispatchAddsTheSubscriptionOnTheResumingThread()
	{
		await using var fixture = new ManagerFixture();

		await fixture.OpenViewAsync();

		// The add is not dispatched: it runs on the thread that resumes the attach after the dispatched
		// action completes. With the inline delegate that is the thread that ran the attach action,
		// which is a thread-pool continuation of the store's asynchronous open rather than the
		// caller's thread.
		Assert.IsNotNull(fixture.View.LastOpenThreadId);
		Assert.AreEqual(fixture.View.LastOpenThreadId, fixture.View.ApplySubscribeThreadId);
	}

	[TestMethod]
	public async Task ApplyRequested_RaisedFromInsideADispatchedRefreshRunsTheNestedPipeline()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		int refreshBefore = fixture.View.RefreshCount;

		// The hook raises the view's apply from inside the peer refresh of a manager Replace, which is
		// the documented reentrancy shape for an inline delegate: the apply pipeline runs nested inside
		// the dispatched action. The raise publishes the view's own (now stale) identity, so the store
		// rejects it and the acknowledgment adopts the current state.
		fixture.View.OnRefresh = () =>
		{
			fixture.View.OnRefresh = null;
			fixture.View.RaiseApply(new WorkspaceDocumentReplaceRequest(
				new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
				"nested publish",
				snapshot.FileFormat));
		};

		WorkspaceDocumentManagerMutationResult replaced = await fixture.Manager.ReplaceAsync(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"outer edit",
			snapshot.FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, replaced.ViewSynchronization.Outcome);
		Assert.AreEqual("outer edit", fixture.View.Text);
		Assert.IsTrue(fixture.Store.TryGetSnapshot(snapshot.DocumentId, out WorkspaceDocumentSnapshot? current));
		Assert.AreEqual("outer edit", current!.Content, "The nested stale publish must not overwrite the outer edit.");
		Assert.AreEqual(refreshBefore + 2, fixture.View.RefreshCount, "The outer refresh and the nested acknowledgment each refreshed the view.");
	}
}
