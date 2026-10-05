using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceViewCoordinatorTests
{
	[TestMethod]
	public async Task RenameDirectoryAsync_ALeaseReleaseFailure_StaysRecordedAndBlocksALaterDelete()
	{
		await using var fixture = new CoordinatorTestHost();
		await fixture.OpenViewAsync();
		using var destination = new TemporaryDirectory();
		fixture.View.FailDeleteLeaseRelease = true;

		// The view stays attached to the moved document, so the failed release is recorded as the view's state and not only reported.
		WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult> renamed = await fixture.Coordinator.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(fixture.DirectoryPath, Path.Combine(destination.Path, "moved")),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, renamed.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, renamed.ViewSynchronization.Outcome);

		fixture.View.FailDeleteLeaseRelease = false;
		WorkspaceDocumentSnapshot moved = renamed.StoreResult!.Snapshots[0];

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> deleted = await fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(moved.Identity, moved.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.IsNull(deleted.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, deleted.ViewSynchronization.Outcome);
		Assert.Contains(fixture.View.ViewId, ViewIdsOf(deleted.ViewSynchronization));
	}
}
