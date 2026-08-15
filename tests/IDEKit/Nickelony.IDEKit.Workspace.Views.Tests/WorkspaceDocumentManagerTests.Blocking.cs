using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceDocumentManagerTests
{
	[TestMethod]
	public async Task Rename_ProceedsWhileAViewHasPendingEditsAndConflict()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		string destinationPath = Path.Combine(fixture.DirectoryPath, "renamed.txt");

		// View state cannot be lost by a rename, so pending edits and a conflict do not block it; the
		// view is rekeyed to the new identity afterwards.
		fixture.View.HasPendingEdits = true;
		fixture.View.HasConflict = true;

		WorkspaceDocumentManagerRenameResult result = await fixture.Manager.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp,
			destinationPath));

		Assert.IsNotNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.ViewSynchronization.Outcome);
		Assert.IsTrue(File.Exists(destinationPath));
		Assert.IsFalse(File.Exists(fixture.DocumentPath));
		Assert.AreEqual(result.Snapshot!.DocumentId, fixture.View.DocumentId);
	}

	[TestMethod]
	public async Task DirectoryRename_ProceedsWhileAViewIsUnsynchronized()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		using var destination = new TemporaryDirectory();
		string destinationPath = Path.Combine(destination.Path, "moved");

		// An unsynchronized view does not block a directory rename: the move cannot lose view state,
		// and the identity acknowledgment gives the view a chance to re-synchronize.
		await fixture.MarkViewUnsynchronizedAsync(snapshot);

		WorkspaceDocumentManagerDirectoryRenameResult result = await fixture.Manager.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(fixture.DirectoryPath, destinationPath));

		Assert.IsNotNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.ViewSynchronization.Outcome);
		Assert.IsTrue(Directory.Exists(destinationPath));
		Assert.IsFalse(Directory.Exists(fixture.DirectoryPath));
		Assert.AreEqual(result.Snapshots[0].DocumentId, fixture.View.DocumentId);
	}

	[TestMethod]
	public async Task DirectoryDelete_BlockedByUnsynchronizedView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		await fixture.MarkViewUnsynchronizedAsync(snapshot);

		WorkspaceDocumentManagerDirectoryDeleteResult result = await fixture.Manager.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(fixture.DirectoryPath));

		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, result.ViewSynchronization.Outcome);
		CollectionAssert.Contains(ViewIdsOf(result.ViewSynchronization), fixture.View.ViewId);
		Assert.IsTrue(File.Exists(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task Delete_GuardRollbackFailureIsRecordedWithoutFaulting()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		var secondView = new TestView(fixture, "second-view");
		WorkspaceDocumentManagerOpenResult opened = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			secondView);
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.Opened, opened.Outcome);

		fixture.View.ThrowOnDeleteGuardRelease = true;
		secondView.BlockDeleteGuard = true;
		secondView.DeleteGuardFailure = new WorkspaceOperationFailure("DeleteGuardBlocked", "The second view refuses the guard.");

		WorkspaceDocumentManagerDeleteResult result = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, result.ViewSynchronization.Outcome);
		CollectionAssert.Contains(ViewIdsOf(result.ViewSynchronization), "second-view");

		// The blocked issue carries the failure the rejecting view reported.
		WorkspaceDocumentViewSynchronizationIssue guardIssue = result.ViewSynchronization.Issues.Single(issue => issue.ViewId == "second-view");
		Assert.AreEqual("DeleteGuardBlocked", guardIssue.Failure!.Code);
		Assert.AreEqual(1, fixture.View.DeleteGuardReleaseCount, "The rollback must attempt to release entered guards.");
		Assert.IsTrue(File.Exists(fixture.DocumentPath));

		// The failed release leaves both views unsynchronized, so the next destructive operation is
		// rejected by the pre-check instead of running against views with unknown state.
		WorkspaceDocumentManagerDeleteResult retry = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		Assert.IsNull(retry.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, retry.ViewSynchronization.Outcome);
	}

	[TestMethod]
	public async Task DirectoryRename_GuardRejectionBlocksAndNothingMoves()
	{
		await using var fixture = new ManagerFixture();
		await fixture.OpenViewAsync();
		using var destination = new TemporaryDirectory();
		string destinationPath = Path.Combine(destination.Path, "moved");
		fixture.View.BlockDeleteGuard = true;

		// A view that rejects the delete guard blocks the directory rename before the store call; the
		// rejected guard was never applied, so the rollback has nothing to release.
		WorkspaceDocumentManagerDirectoryRenameResult result = await fixture.Manager.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(fixture.DirectoryPath, destinationPath));

		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, result.ViewSynchronization.Outcome);
		CollectionAssert.Contains(ViewIdsOf(result.ViewSynchronization), fixture.View.ViewId);
		Assert.AreEqual(0, fixture.View.DeleteGuardReleaseCount);
		Assert.IsTrue(Directory.Exists(fixture.DirectoryPath));
		Assert.IsFalse(Directory.Exists(destinationPath));
	}

	[TestMethod]
	public async Task DirectoryRename_IdentityAckFailureReportsUnsynchronizedView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		using var destination = new TemporaryDirectory();
		string destinationPath = Path.Combine(destination.Path, "moved");
		fixture.View.FailIdentityAck = true;

		WorkspaceDocumentManagerDirectoryRenameResult result = await fixture.Manager.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(fixture.DirectoryPath, destinationPath));

		// The move succeeded, but the view rejected the identity change, so it stays bound to the
		// vacated id and is reported as unsynchronized.
		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.ViewSynchronization.Outcome);
		CollectionAssert.Contains(ViewIdsOf(result.ViewSynchronization), fixture.View.ViewId);
		Assert.AreEqual(snapshot.DocumentId, fixture.View.DocumentId);
		Assert.IsTrue(Directory.Exists(destinationPath));
		Assert.IsFalse(Directory.Exists(fixture.DirectoryPath));
	}

	[TestMethod]
	public async Task DirectoryRename_LateViewIsAcknowledgedAndRekeyed()
	{
		await using var fixture = new ManagerFixture();
		await fixture.OpenViewAsync();
		using var destination = new TemporaryDirectory();
		string destinationPath = Path.Combine(destination.Path, "moved");
		var lateView = new TestView(fixture, "late-view");
		Task<WorkspaceDocumentManagerOpenResult> attachTask = fixture.AttachViewDuringDeleteGuard(lateView);

		WorkspaceDocumentManagerDirectoryRenameResult result = await fixture.Manager.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(fixture.DirectoryPath, destinationPath));

		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.Opened, (await attachTask).Outcome);
		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.ViewSynchronization.Outcome);
		Assert.AreEqual(0, lateView.CloseCount, "A view attached during the move must not be closed by it.");

		// The late view is matched by its document key and rekeyed to the moved identity instead of
		// staying bound to the vacated id.
		Assert.AreEqual(result.Snapshots[0].DocumentId, lateView.DocumentId);

		// It is reachable for later operations: a replacement refreshes it through the new identity.
		WorkspaceDocumentManagerMutationResult replaced = await fixture.Manager.ReplaceAsync(new WorkspaceDocumentReplaceRequest(
			new(result.Snapshots[0].DocumentKey, result.Snapshots[0].DocumentId, result.Snapshots[0].Version),
			"after move",
			result.Snapshots[0].FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.Outcome);
		Assert.AreEqual(1, lateView.RefreshCount);
		Assert.AreEqual("after move", lateView.Text);
	}

	[TestMethod]
	public async Task DeleteDirectory_GuardReleaseFailureIsReportedAsUnsynchronizedView()
	{
		await using var fixture = new ManagerFixture();
		await fixture.OpenViewAsync();
		fixture.View.FailDeleteGuardRelease = true;

		WorkspaceDocumentManagerDirectoryDeleteResult result = await fixture.Manager.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(fixture.DirectoryPath));

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Deleted, result.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.ViewSynchronization.Outcome);
		CollectionAssert.Contains(ViewIdsOf(result.ViewSynchronization), fixture.View.ViewId);
		Assert.AreEqual(1, fixture.View.CloseCount);
		Assert.IsFalse(Directory.Exists(fixture.DirectoryPath));
	}

	[TestMethod]
	public async Task Delete_ViewAttachedAfterCaptureButBeforeStoreDelete_IsClosedAndUnregistered()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		var lateView = new TestView(fixture, "late-view");
		Task<WorkspaceDocumentManagerOpenResult> attachTask = fixture.AttachViewDuringDeleteGuard(lateView);

		WorkspaceDocumentManagerDeleteResult result = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		WorkspaceDocumentManagerOpenResult attached = await attachTask;
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.Opened, attached.Outcome);
		Assert.AreEqual(snapshot.DocumentKey, attached.Snapshot!.DocumentKey);
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, result.Outcome);
		Assert.AreEqual(1, fixture.View.CloseCount);
		Assert.AreEqual(1, lateView.CloseCount, "A view registered during the delete must be closed with the deleted document.");

		// The late view must also be unregistered: stopping the manager must not close it a second time.
		await fixture.Manager.StopAsync();
		Assert.AreEqual(1, lateView.CloseCount);
	}

	[TestMethod]
	public async Task Delete_ViewThatCannotReportItsKeyIsClosedAsDeletedInstance()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		var lateView = new TestView(fixture, "late-view")
		{
			// The first key read is the attachment validation; the sweep's read throws.
			ThrowAfterDocumentKeyReads = 1
		};
		Task<WorkspaceDocumentManagerOpenResult> attachTask = fixture.AttachViewDuringDeleteGuard(lateView);

		WorkspaceDocumentManagerDeleteResult result = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.Opened, (await attachTask).Outcome);
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, result.Outcome);
		Assert.AreEqual(1, lateView.CloseCount, "A view that cannot report its key must be treated as bound to the removed instance.");
	}

	[TestMethod]
	public async Task DeleteDirectory_LateAttachedViewIsClosedWithTheRemovedDescendant()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		var lateView = new TestView(fixture, "late-view");
		Task<WorkspaceDocumentManagerOpenResult> attachTask = fixture.AttachViewDuringDeleteGuard(lateView);

		WorkspaceDocumentManagerDirectoryDeleteResult result = await fixture.Manager.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(fixture.DirectoryPath));

		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.Opened, (await attachTask).Outcome);
		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Deleted, result.Outcome);
		Assert.AreEqual(1, fixture.View.CloseCount);
		Assert.AreEqual(1, lateView.CloseCount, "A view registered during the directory delete must be closed with the removed descendant.");
	}

	[TestMethod]
	public async Task Delete_HappyPathClosesViewAndRemovesTracking()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		WorkspaceDocumentManagerDeleteResult result = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, result.Outcome);
		Assert.AreEqual(1, fixture.View.CloseCount);
		Assert.AreEqual(1, fixture.View.DeleteGuardReleaseCount);
		Assert.IsFalse(File.Exists(fixture.DocumentPath));
		Assert.IsFalse(fixture.Store.TryGetSnapshot(snapshot.DocumentId, out _));
	}

	[TestMethod]
	public async Task Delete_StoreFailureStillReleasesEnteredGuards()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// The guards were already entered when the store call runs; a failing call must still release
		// them through the finally block instead of leaving the views latched.
		await fixture.Store.DisposeAsync();

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp)));

		Assert.AreEqual(1, fixture.View.DeleteGuardReleaseCount, "Entered guards must be released when the store call throws.");
		Assert.IsTrue(File.Exists(fixture.DocumentPath));
	}

	[TestMethod]
	[DataRow(true, false, DisplayName = "Delete_BlockedByPendingEdits")]
	[DataRow(false, true, DisplayName = "Delete_BlockedByConflict")]
	public async Task Delete_BlockedByViewState(bool hasPendingEdits, bool hasConflict)
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.HasPendingEdits = hasPendingEdits;
		fixture.View.HasConflict = hasConflict;

		WorkspaceDocumentManagerDeleteResult result = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		// The delete destroys the document whose unsaved state the view still holds, so the view
		// blocks it before the store is reached. The issue carries no failure detail: the host can
		// inspect the blocking state on the view itself.
		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, result.ViewSynchronization.Outcome);
		WorkspaceDocumentViewSynchronizationIssue issue = result.ViewSynchronization.Issues.Single();
		Assert.AreEqual(fixture.View.ViewId, issue.ViewId);
		Assert.IsNull(issue.Failure);
		Assert.IsTrue(File.Exists(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task Delete_UnavailableViewIdIsReportedWithTheMarker()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.ViewIdProvider = () => throw new InvalidOperationException("The view cannot report its id.");
		fixture.View.HasPendingEdits = true;

		WorkspaceDocumentManagerDeleteResult result = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		// A registered view whose id getter throws is still reported: the issue uses the documented
		// marker plus a per-instance discriminator instead of failing the operation.
		Assert.IsNull(result.StoreResult);
		Assert.IsTrue(
			result.ViewSynchronization.Issues.Single().ViewId.StartsWith("(unidentified view", StringComparison.Ordinal),
			"The issue must report the unidentified-view marker with a per-instance discriminator.");
	}

	[TestMethod]
	public async Task Delete_ThrowingStateProbeBlocksAndReportsTheFailureDetail()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.ThrowOnStateProbe = true;

		WorkspaceDocumentManagerDeleteResult result = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		// A view that cannot report its state is not assumed synchronized: it blocks the operation and is
		// recorded as unsynchronized with the probe failure, so the host can tell why it blocked.
		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, result.ViewSynchronization.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewStateProbeFailed, result.ViewSynchronization.Issues.Single().Failure?.Code);
		Assert.IsNotNull(result.ViewSynchronization.Issues.Single().Failure?.Exception);
		Assert.IsTrue(File.Exists(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task Delete_StoreRejectsDeleteAndReportsGuardReleaseFailure()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.FailDeleteGuardRelease = true;
		fixture.View.DeleteGuardFailure = new WorkspaceOperationFailure("DeleteGuardReleaseFailed", "The view cannot release its guard.");

		// The file changed behind the store, so the delete is rejected with a non-Deleted outcome after
		// the guards were entered: the guards are released, the release failure is reported, and the
		// view stays open with the file intact.
		File.WriteAllText(fixture.DocumentPath, "changed externally");

		WorkspaceDocumentManagerDeleteResult result = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		Assert.IsNotNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(1, fixture.View.DeleteGuardReleaseCount);
		Assert.AreEqual(0, fixture.View.CloseCount, "A rejected delete leaves the view open.");
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.ViewSynchronization.Outcome);
		Assert.AreEqual("DeleteGuardReleaseFailed", result.ViewSynchronization.Issues.Single().Failure!.Code);
		Assert.IsTrue(File.Exists(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task Delete_ViewThrowsWhileEnteringTheGuardBlocksAndReportsTheFailureDetail()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.ThrowOnDeleteGuardEntry = true;

		WorkspaceDocumentManagerDeleteResult result = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		// A view that throws while entering its delete guard blocks the operation: the entry is
		// reported with the guard failure code and its exception detail, the view is never released
		// because it never entered, and the file is untouched.
		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, result.ViewSynchronization.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.DeleteGuardFailed, result.ViewSynchronization.Issues.Single().Failure?.Code);
		Assert.IsNotNull(result.ViewSynchronization.Issues.Single().Failure?.Exception);
		Assert.AreEqual(0, fixture.View.DeleteGuardReleaseCount, "A view that never entered a guard is not released.");
		Assert.IsTrue(File.Exists(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task Delete_ViewThrowsWhileReleasingTheGuardReportsTheFailureDetail()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.ThrowOnDeleteGuardRelease = true;

		// The file changed behind the store, so the delete is rejected with a non-Deleted outcome after
		// the guard was entered: the release throws, the failure is reported with its exception detail,
		// and the view stays open with the file intact.
		File.WriteAllText(fixture.DocumentPath, "changed externally");

		WorkspaceDocumentManagerDeleteResult result = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		Assert.IsNotNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(1, fixture.View.DeleteGuardReleaseCount);
		Assert.AreEqual(0, fixture.View.CloseCount, "A rejected delete leaves the view open.");
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.ViewSynchronization.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.DeleteGuardFailed, result.ViewSynchronization.Issues.Single().Failure!.Code);
		Assert.IsNotNull(result.ViewSynchronization.Issues.Single().Failure!.Exception);
		Assert.IsTrue(File.Exists(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task DeleteDirectory_StoreFailureStillReleasesEnteredGuards()
	{
		await using var fixture = new ManagerFixture();
		await fixture.OpenViewAsync();

		// The guard callback runs after the store snapshots were captured and before the recursive
		// delete, so the store can be torn down inside the operation's guard window; disposal completes
		// synchronously because no store operation is in flight yet.
		fixture.View.OnDeleteGuardEntered = () => fixture.Store.DisposeAsync().AsTask().GetAwaiter().GetResult();

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => fixture.Manager.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(fixture.DirectoryPath)));

		Assert.AreEqual(1, fixture.View.DeleteGuardReleaseCount, "Entered guards must be released when the store call throws.");
		Assert.IsTrue(Directory.Exists(fixture.DirectoryPath));
	}

	[TestMethod]
	public async Task DeleteDirectory_ClosesEveryRemovedDescendant()
	{
		await using var fixture = new ManagerFixture();
		await fixture.OpenViewAsync();
		string secondPath = Path.Combine(fixture.DirectoryPath, "second.txt");
		File.WriteAllText(secondPath, "second");
		var secondView = new TestView(fixture, "second-view");
		WorkspaceDocumentManagerOpenResult second = await fixture.Manager.OpenWithViewAsync(secondPath, s_openOptions, secondView);
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.Opened, second.Outcome);

		WorkspaceDocumentManagerDirectoryDeleteResult result = await fixture.Manager.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(fixture.DirectoryPath));

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Deleted, result.Outcome);
		Assert.AreEqual(2, result.Snapshots.Count);
		Assert.AreEqual(1, fixture.View.CloseCount);
		Assert.AreEqual(1, secondView.CloseCount);
		Assert.IsFalse(Directory.Exists(fixture.DirectoryPath));
	}

	[TestMethod]
	public async Task Delete_UnregisterDuringInFlightDeleteStillClosesTheViewOnce()
	{
		var fileSystem = new BlockingDeleteFileSystem(new LocalWorkspaceFileSystem());
		await using var fixture = new ManagerFixture(fileSystem: fileSystem);
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		Task<WorkspaceDocumentManagerDeleteResult> delete = fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));
		await fileSystem.DeleteEntered.WaitAsync(TimeSpan.FromSeconds(10));

		// The host closes and unregisters the view while the delete is in flight: the captured view is
		// still closed by the operation exactly once, and the stop does not close it again.
		fixture.Manager.UnregisterOpenView(fixture.View);
		fileSystem.ReleaseDelete();
		WorkspaceDocumentManagerDeleteResult result = await delete;

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, result.Outcome);
		Assert.AreEqual(1, fixture.View.CloseCount);
		await fixture.Manager.StopAsync();
		Assert.AreEqual(1, fixture.View.CloseCount);
	}
}
