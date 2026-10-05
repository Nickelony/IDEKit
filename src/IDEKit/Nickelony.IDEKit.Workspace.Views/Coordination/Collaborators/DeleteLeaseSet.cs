namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Holds the delete leases that one leased operation entered, and releases the ones still held when it is disposed.</summary>
/// <param name="coordinator">The coordinator that enters and releases the leases.</param>
/// <remarks>
/// A view left leased would block every later synchronization, so disposing the set releases the leases that are still held.
/// An operation that released the leases itself (<see cref="MarkReleased"/>) or whose entry rollback already released them leaves nothing to release.
/// </remarks>
internal sealed class DeleteLeaseSet(DeleteLeaseCoordinator coordinator) : IAsyncDisposable
{
	/// <summary>The leases this operation entered and has not released.</summary>
	internal List<IWorkspaceDocumentDeleteLeaseView> EnteredLeases { get; } = [];

	/// <summary>Records that the operation released the leases itself, so the exit release has nothing left to do.</summary>
	internal void MarkReleased() => EnteredLeases.Clear();

	/// <summary>Releases whatever leases are still held.</summary>
	public async ValueTask DisposeAsync()
	{
		if (EnteredLeases.Count == 0)
			return;

		await coordinator.ReleaseViaDispatchAsync(EnteredLeases).ConfigureAwait(false);
	}
}
