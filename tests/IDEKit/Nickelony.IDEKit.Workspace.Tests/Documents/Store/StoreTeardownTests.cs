using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>Covers the teardown directly: the wait for registered work, the cancellation, the repeated call and the state it releases.</summary>
[TestClass]
public sealed class StoreTeardownTests
{
	[TestMethod]
	public async Task StopAsync_WithNothingInFlight_ClearsTheTrackedStateAndCompletesBeforeReturning()
	{
		var host = new StoreCollaboratorsTestHost();
		host.AddDocument("doc");

		ValueTask stop = host.Teardown.StopAsync();

		Assert.IsTrue(stop.IsCompleted);
		await stop.ConfigureAwait(false);
		Assert.IsEmpty(host.Locked(tracker => tracker.Documents.ToList()));
	}

	[TestMethod]
	public async Task StopAsync_WithARegisteredOperation_CancelsTheLifetimeAndWaitsForTheOperationToComplete()
	{
		var host = new StoreCollaboratorsTestHost();
		LogicalDocument document = host.AddDocument("doc");

		// The operation is admitted on a pool thread, because the thread that admits it is marked as inside the operation and cannot request the stop.
		OperationLease operation = await Task.Run(() =>
				host.OperationGate.Begin(StoreCollaboratorsTestHost.IdentityOf(document), captureSnapshot: false).Operation
				?? throw new InvalidOperationException("No operation was admitted.")
			)
			.ConfigureAwait(false);

		using CancellationTokenSource linked = host.Lifetime.CreateLinkedCancellation(CancellationToken.None);

		ValueTask stop = host.Teardown.StopAsync();

		Assert.IsFalse(stop.IsCompleted);
		Assert.IsTrue(linked.IsCancellationRequested);

		host.OperationGate.Complete(operation);
		await stop.ConfigureAwait(false);

		Assert.IsEmpty(host.Locked(tracker => tracker.Documents.ToList()));
	}

	[TestMethod]
	public async Task StopAsync_CalledAgain_ObservesTheSameStop()
	{
		var host = new StoreCollaboratorsTestHost();

		await host.Teardown.StopAsync().ConfigureAwait(false);
		ValueTask second = host.Teardown.StopAsync();

		Assert.IsTrue(second.IsCompleted);
		await second.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task StopAsync_WaitsForAnOpenReservationInFlight()
	{
		var host = new StoreCollaboratorsTestHost();
		OpenReservation reservation = host.Locked(tracker => tracker.ReserveOpen("doc"));

		ValueTask stop = host.Teardown.StopAsync();

		Assert.IsFalse(stop.IsCompleted);

		host.Locked(tracker =>
			tracker.CompleteOpenReservation("doc", reservation, new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.Canceled, null))
		);

		await stop.ConfigureAwait(false);
	}
}
