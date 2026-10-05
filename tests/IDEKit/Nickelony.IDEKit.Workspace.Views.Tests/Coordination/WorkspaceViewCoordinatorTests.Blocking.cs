using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceViewCoordinatorTests
{
	[TestMethod]
	public async Task Rename_ProceedsWhileAViewHasPendingEditsAndConflict()
	{
		await using var fixture = new CoordinatorTestHost();

		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		string destinationPath = Path.Combine(fixture.DirectoryPath, "renamed.txt");

		// View state cannot be lost by a rename, so pending edits and a conflict do not block it; the
		// view is rekeyed to the new identity afterwards.
		fixture.View.HasPendingEdits = true;
		fixture.View.HasConflict = true;

		WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentRenameResult> result = await fixture.Coordinator.RenameAsync(
			new WorkspaceDocumentRenameRequest(
				new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
				snapshot.OnDiskStamp,
				destinationPath
			),

			TestContext.CancellationToken
		);

		Assert.IsNotNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, result.StoreResult.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.ViewSynchronization.Outcome);
		Assert.IsTrue(File.Exists(destinationPath));
		Assert.IsFalse(File.Exists(fixture.DocumentPath));
		Assert.AreEqual(result.StoreResult.Snapshot!.DocumentId, fixture.View.DocumentId);
	}

	[TestMethod]
	public async Task DirectoryRename_ProceedsWhileAViewIsUnsynchronized()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		using var destination = new TemporaryDirectory();
		string destinationPath = Path.Combine(destination.Path, "moved");

		// An unsynchronized view does not block a directory rename: the move cannot lose view state,
		// and the identity acknowledgment gives the view a chance to re-synchronize.
		await fixture.MarkViewUnsynchronizedAsync(snapshot);

		WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult> result = await fixture.Coordinator.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(fixture.DirectoryPath, destinationPath),
			TestContext.CancellationToken
		);

		Assert.IsNotNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.ViewSynchronization.Outcome);
		Assert.IsTrue(Directory.Exists(destinationPath));
		Assert.IsFalse(Directory.Exists(fixture.DirectoryPath));
		Assert.AreEqual(result.StoreResult!.Snapshots[0].DocumentId, fixture.View.DocumentId);
	}

	[TestMethod]
	public async Task DirectoryDelete_BlockedByUnsynchronizedView()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		await fixture.MarkViewUnsynchronizedAsync(snapshot);

		WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryDeleteResult> result = await fixture.Coordinator.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(fixture.DirectoryPath),
			TestContext.CancellationToken
		);

		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, result.ViewSynchronization.Outcome);
		Assert.Contains(fixture.View.ViewId, ViewIdsOf(result.ViewSynchronization));
		Assert.IsTrue(File.Exists(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task Delete_LeaseRollbackFailureIsRecordedWithoutFaulting()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		var secondView = new FakeCoordinatorView(fixture, "second-view");

		WorkspaceViewCoordinatorOpenResult opened = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: secondView,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Opened, opened.Outcome);

		fixture.View.ThrowOnDeleteLeaseRelease = true;
		secondView.BlockDeleteLease = true;
		secondView.DeleteLeaseFailure = new WorkspaceOperationFailure("DeleteLeaseBlocked", "The second view refuses the lease.");

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> result = await fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, result.ViewSynchronization.Outcome);
		Assert.Contains("second-view", ViewIdsOf(result.ViewSynchronization));

		// The blocked issue carries the failure the rejecting view reported.
		WorkspaceDocumentViewSynchronizationIssue leaseIssue = result.ViewSynchronization.Issues.Single(issue => issue.ViewId == "second-view");

		Assert.AreEqual("DeleteLeaseBlocked", leaseIssue.Failure!.Code);
		Assert.AreEqual(1, fixture.View.DeleteLeaseReleaseCount, "The rollback must attempt to release entered leases.");
		Assert.IsTrue(File.Exists(fixture.DocumentPath));

		// The failed release leaves both views unsynchronized, so the next destructive operation is
		// rejected by the pre-check instead of running against views with unknown state.
		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> retry = await fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.IsNull(retry.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, retry.ViewSynchronization.Outcome);
	}

	[TestMethod]
	public async Task DirectoryRename_LeaseRejectionBlocksAndNothingMoves()
	{
		await using var fixture = new CoordinatorTestHost();
		await fixture.OpenViewAsync();
		using var destination = new TemporaryDirectory();
		string destinationPath = Path.Combine(destination.Path, "moved");
		fixture.View.BlockDeleteLease = true;

		// A view that rejects the delete lease blocks the directory rename before the store call; the
		// rejected lease was never applied, so the rollback has nothing to release.
		WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult> result = await fixture.Coordinator.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(fixture.DirectoryPath, destinationPath),
			TestContext.CancellationToken
		);

		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, result.ViewSynchronization.Outcome);
		Assert.Contains(fixture.View.ViewId, ViewIdsOf(result.ViewSynchronization));
		Assert.AreEqual(0, fixture.View.DeleteLeaseReleaseCount);
		Assert.IsTrue(Directory.Exists(fixture.DirectoryPath));
		Assert.IsFalse(Directory.Exists(destinationPath));
	}

	[TestMethod]
	public async Task DirectoryRename_IdentityAckFailureReportsUnsynchronizedView()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		using var destination = new TemporaryDirectory();
		string destinationPath = Path.Combine(destination.Path, "moved");
		fixture.View.FailIdentityAck = true;

		WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult> result = await fixture.Coordinator.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(fixture.DirectoryPath, destinationPath),
			TestContext.CancellationToken
		);

		// The move succeeded, but the view rejected the identity change, so it stays attached to the
		// vacated id and is reported as unsynchronized.
		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.ViewSynchronization.Outcome);
		Assert.Contains(fixture.View.ViewId, ViewIdsOf(result.ViewSynchronization));
		Assert.AreEqual(snapshot.DocumentId, fixture.View.DocumentId);
		Assert.IsTrue(Directory.Exists(destinationPath));
		Assert.IsFalse(Directory.Exists(fixture.DirectoryPath));
	}

	[TestMethod]
	public async Task DirectoryRename_LateViewIsAcknowledgedAndRekeyed()
	{
		await using var fixture = new CoordinatorTestHost();
		await fixture.OpenViewAsync();
		using var destination = new TemporaryDirectory();
		string destinationPath = Path.Combine(destination.Path, "moved");
		var lateView = new FakeCoordinatorView(fixture, "late-view");
		Task<WorkspaceViewCoordinatorOpenResult> attachTask = fixture.AttachViewDuringDeleteLease(lateView);

		WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult> result = await fixture.Coordinator.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(fixture.DirectoryPath, destinationPath),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Opened, (await attachTask).Outcome);
		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.ViewSynchronization.Outcome);
		Assert.AreEqual(0, lateView.CloseCount, "A view attached during the move must not be closed by it.");

		// The late view is matched by its document key and rekeyed to the moved identity instead of
		// staying attached to the vacated id.
		Assert.AreEqual(result.StoreResult!.Snapshots[0].DocumentId, lateView.DocumentId);

		// It is reachable for later operations: a replacement refreshes it through the new identity.
		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await fixture.Coordinator.ReplaceAsync(
			new WorkspaceDocumentReplaceRequest(
				new(
					result.StoreResult!.Snapshots[0].DocumentKey,
					result.StoreResult!.Snapshots[0].DocumentId,
					result.StoreResult!.Snapshots[0].Version
				),

				"after move",
				result.StoreResult!.Snapshots[0].FileFormat
			)
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.StoreResult.Outcome);
		Assert.AreEqual(1, lateView.RefreshCount);
		Assert.AreEqual("after move", lateView.Text);
	}

	[TestMethod]
	public async Task DeleteDirectory_LeaseReleaseFailureIsReportedAsUnsynchronizedView()
	{
		await using var fixture = new CoordinatorTestHost();
		await fixture.OpenViewAsync();
		fixture.View.FailDeleteLeaseRelease = true;

		WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryDeleteResult> result = await fixture.Coordinator.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(fixture.DirectoryPath),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Deleted, result.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.ViewSynchronization.Outcome);
		Assert.Contains(fixture.View.ViewId, ViewIdsOf(result.ViewSynchronization));
		Assert.AreEqual(1, fixture.View.CloseCount);
		Assert.IsFalse(Directory.Exists(fixture.DirectoryPath));
	}

	[TestMethod]
	public async Task Delete_ViewAttachedAfterCaptureButBeforeStoreDelete_IsClosedAndDetached()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		var lateView = new FakeCoordinatorView(fixture, "late-view");
		Task<WorkspaceViewCoordinatorOpenResult> attachTask = fixture.AttachViewDuringDeleteLease(lateView);

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> result = await fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		WorkspaceViewCoordinatorOpenResult attached = await attachTask;

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Opened, attached.Outcome);
		Assert.AreEqual(snapshot.DocumentKey, attached.Snapshot!.DocumentKey);
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, result.StoreResult?.Outcome);
		Assert.AreEqual(1, fixture.View.CloseCount);
		Assert.AreEqual(1, lateView.CloseCount, "A view registered during the delete must be closed with the deleted document.");

		// The late view must also be detached: stopping the coordinator must not close it a second time.
		await fixture.Coordinator.StopAsync();
		Assert.AreEqual(1, lateView.CloseCount);
	}

	[TestMethod]
	public async Task Delete_ViewThatCannotReportItsKeyIsClosedAsDeletedInstance()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		var lateView = new FakeCoordinatorView(fixture, "late-view")
		{
			// The first key read is the attachment validation; the sweep's read throws.
			ThrowAfterDocumentKeyReads = 1,
		};

		Task<WorkspaceViewCoordinatorOpenResult> attachTask = fixture.AttachViewDuringDeleteLease(lateView);

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> result = await fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Opened, (await attachTask).Outcome);
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, result.StoreResult?.Outcome);
		Assert.AreEqual(1, lateView.CloseCount, "A view that cannot report its key must be treated as attached to the removed instance.");
	}

	[TestMethod]
	public async Task DeleteDirectory_LateAttachedViewIsClosedWithTheRemovedDescendant()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		var lateView = new FakeCoordinatorView(fixture, "late-view");
		Task<WorkspaceViewCoordinatorOpenResult> attachTask = fixture.AttachViewDuringDeleteLease(lateView);

		WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryDeleteResult> result = await fixture.Coordinator.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(fixture.DirectoryPath),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Opened, (await attachTask).Outcome);
		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Deleted, result.StoreResult?.Outcome);
		Assert.AreEqual(1, fixture.View.CloseCount);
		Assert.AreEqual(1, lateView.CloseCount, "A view registered during the directory delete must be closed with the removed descendant.");
	}

	[TestMethod]
	public async Task Delete_HappyPathClosesViewAndRemovesTracking()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> result = await fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, result.StoreResult?.Outcome);
		Assert.AreEqual(1, fixture.View.CloseCount);
		Assert.AreEqual(1, fixture.View.DeleteLeaseReleaseCount);
		Assert.IsFalse(File.Exists(fixture.DocumentPath));
		Assert.IsFalse(fixture.Store.TryGetSnapshot(snapshot.DocumentId, out _));
	}

	[TestMethod]
	public async Task Delete_AViewWhoseCloseThrows_IsDetachedAndReportedWithTheCloseFailure()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.ThrowOnClose = true;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> result = await fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, result.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.ViewSynchronization.Outcome);

		WorkspaceDocumentViewSynchronizationIssue issue = result.ViewSynchronization.Issues.Single();

		Assert.AreEqual(fixture.View.ViewId, issue.ViewId);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewCloseFailed, issue.Failure?.Code);
		Assert.IsInstanceOfType<InvalidOperationException>(issue.Failure?.Exception);
	}

	[TestMethod]
	public async Task Delete_StoreFailureStillReleasesEnteredLeases()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// The leases were already entered when the store call runs; a failing call must still release
		// them through the finally block instead of leaving the views latched.
		await fixture.Store.DisposeAsync();

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
			fixture.Coordinator.DeleteAsync(
				new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
				TestContext.CancellationToken
			)
		);

		Assert.AreEqual(1, fixture.View.DeleteLeaseReleaseCount, "Entered leases must be released when the store call throws.");
		Assert.IsTrue(File.Exists(fixture.DocumentPath));
	}

	[TestMethod]
	[DataRow(true, false, DisplayName = "Delete_BlockedByPendingEdits")]
	[DataRow(false, true, DisplayName = "Delete_BlockedByConflict")]
	public async Task Delete_BlockedByViewState(bool hasPendingEdits, bool hasConflict)
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.HasPendingEdits = hasPendingEdits;
		fixture.View.HasConflict = hasConflict;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> result = await fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

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
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.ViewIdProvider = () => throw new InvalidOperationException("The view cannot report its id.");
		fixture.View.HasPendingEdits = true;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> result = await fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		// A registered view whose id getter throws is still reported: the issue uses the documented
		// marker plus a per-instance discriminator instead of failing the operation.
		Assert.IsNull(result.StoreResult);

		Assert.IsTrue(
			result.ViewSynchronization.Issues.Single().ViewId.StartsWith("(unidentified view", StringComparison.Ordinal),
			"The issue must report the unidentified-view marker with a per-instance discriminator."
		);
	}

	[TestMethod]
	public async Task Delete_ThrowingStateProbeBlocksAndReportsTheFailureDetail()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.ThrowOnStateProbe = true;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> result = await fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		// A view that cannot report its state is not assumed synchronized: it blocks the operation and is
		// recorded as unsynchronized with the probe failure, so the host can tell why it blocked.
		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, result.ViewSynchronization.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewStateProbeFailed, result.ViewSynchronization.Issues.Single().Failure?.Code);
		Assert.IsNotNull(result.ViewSynchronization.Issues.Single().Failure?.Exception);
		Assert.IsTrue(File.Exists(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task Delete_StoreRejectsDeleteAndReportsLeaseReleaseFailure()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.FailDeleteLeaseRelease = true;
		fixture.View.DeleteLeaseFailure = new WorkspaceOperationFailure("DeleteLeaseReleaseFailed", "The view cannot release its lease.");

		// The file changed behind the store, so the delete is rejected with a non-Deleted outcome after
		// the leases were entered: the leases are released, the release failure is reported, and the
		// view stays open with the file intact.
		File.WriteAllText(fixture.DocumentPath, "changed externally");

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> result = await fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.IsNotNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.ExternalFileConflict, result.StoreResult?.Outcome);
		Assert.AreEqual(1, fixture.View.DeleteLeaseReleaseCount);
		Assert.AreEqual(0, fixture.View.CloseCount, "A rejected delete leaves the view open.");
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.ViewSynchronization.Outcome);
		Assert.AreEqual("DeleteLeaseReleaseFailed", result.ViewSynchronization.Issues.Single().Failure!.Code);
		Assert.IsTrue(File.Exists(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task Delete_ViewThrowsWhileEnteringTheLeaseBlocksAndReportsTheFailureDetail()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.ThrowOnDeleteLeaseEntry = true;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> result = await fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		// A view that throws while entering its delete lease blocks the operation: the entry is
		// reported with the lease failure code and its exception detail, the view is never released
		// because it never entered, and the file is untouched.
		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, result.ViewSynchronization.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.DeleteLeaseFailed, result.ViewSynchronization.Issues.Single().Failure?.Code);
		Assert.IsNotNull(result.ViewSynchronization.Issues.Single().Failure?.Exception);
		Assert.AreEqual(0, fixture.View.DeleteLeaseReleaseCount, "A view that never entered a lease is not released.");
		Assert.IsTrue(File.Exists(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task Delete_ViewThrowsWhileReleasingTheLeaseReportsTheFailureDetail()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.ThrowOnDeleteLeaseRelease = true;

		// The file changed behind the store, so the delete is rejected with a non-Deleted outcome after
		// the lease was entered: the release throws, the failure is reported with its exception detail,
		// and the view stays open with the file intact.
		File.WriteAllText(fixture.DocumentPath, "changed externally");

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> result = await fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.IsNotNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.ExternalFileConflict, result.StoreResult?.Outcome);
		Assert.AreEqual(1, fixture.View.DeleteLeaseReleaseCount);
		Assert.AreEqual(0, fixture.View.CloseCount, "A rejected delete leaves the view open.");
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.ViewSynchronization.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.DeleteLeaseFailed, result.ViewSynchronization.Issues.Single().Failure!.Code);
		Assert.IsNotNull(result.ViewSynchronization.Issues.Single().Failure!.Exception);
		Assert.IsTrue(File.Exists(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task DeleteDirectory_StoreFailureStillReleasesEnteredLeases()
	{
		await using var fixture = new CoordinatorTestHost();
		await fixture.OpenViewAsync();

		// The lease callback runs after the store snapshots were captured and before the recursive
		// delete, so the store can be torn down inside the operation's lease window; disposal completes
		// synchronously because no store operation is in flight yet.
		fixture.View.OnDeleteLeaseEntered = () => fixture.Store.DisposeAsync().AsTask().GetAwaiter().GetResult();

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
			fixture.Coordinator.DeleteDirectoryAsync(
				new WorkspaceDocumentDirectoryDeleteRequest(fixture.DirectoryPath),
				TestContext.CancellationToken
			)
		);

		Assert.AreEqual(1, fixture.View.DeleteLeaseReleaseCount, "Entered leases must be released when the store call throws.");
		Assert.IsTrue(Directory.Exists(fixture.DirectoryPath));
	}

	[TestMethod]
	public async Task DeleteDirectory_ClosesEveryRemovedDescendant()
	{
		await using var fixture = new CoordinatorTestHost();
		await fixture.OpenViewAsync();
		string secondPath = Path.Combine(fixture.DirectoryPath, "second.txt");
		File.WriteAllText(secondPath, "second");
		var secondView = new FakeCoordinatorView(fixture, "second-view");

		WorkspaceViewCoordinatorOpenResult second = await fixture.Coordinator.OpenWithViewAsync(
			filePath: secondPath,
			options: s_openOptions,
			view: secondView,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Opened, second.Outcome);

		WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryDeleteResult> result = await fixture.Coordinator.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(fixture.DirectoryPath),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Deleted, result.StoreResult?.Outcome);
		Assert.HasCount(2, result.StoreResult?.Snapshots);
		Assert.AreEqual(1, fixture.View.CloseCount);
		Assert.AreEqual(1, secondView.CloseCount);
		Assert.IsFalse(Directory.Exists(fixture.DirectoryPath));
	}

	[TestMethod]
	public async Task Delete_DetachDuringInFlightDeleteStillClosesTheViewOnce()
	{
		var fileSystem = new GatedWorkspaceFileSystem(new LocalWorkspaceFileSystem());
		await using var fixture = new CoordinatorTestHost(fileSystem);

		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		Task<WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult>> delete = fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		await fileSystem.DeleteEntered.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);

		// The host detaches the view while the delete is in flight: the captured view is still closed
		// by the operation exactly once, and the stop does not close it again.
		fixture.Coordinator.DetachView(fixture.View);
		fileSystem.ReleaseDelete();

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> result = await delete;

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, result.StoreResult?.Outcome);
		Assert.AreEqual(1, fixture.View.CloseCount);

		await fixture.Coordinator.StopAsync();
		Assert.AreEqual(1, fixture.View.CloseCount);
	}

	public TestContext? TestContext { get; set; }
}
