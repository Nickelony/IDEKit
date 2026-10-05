using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceViewCoordinatorTests
{
	[TestMethod]
	public async Task OpenWithView_ConcurrentSameInstanceWithContractAbidingViewReportsAlreadyOpen()
	{
		// The pump serializes dispatched actions, which is the documented obligation for views that are
		// not thread-safe; a second open of the same instance must then resolve as a no-op without
		// calling the view a second time or treating the view as in use by someone else.
		await using var fixture = new CoordinatorTestHost(pumpDispatch: true);
		fixture.View.ReportAlreadyOpenWhenAttached = true;

		Task<WorkspaceViewCoordinatorOpenResult> first = fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		Task<WorkspaceViewCoordinatorOpenResult> second = fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		WorkspaceViewCoordinatorOpenResult[] results = await Task.WhenAll(first, second);
		Assert.ContainsSingle(result => result.Outcome == WorkspaceViewCoordinatorOpenOutcome.Opened, results);

		WorkspaceViewCoordinatorOpenResult alreadyOpen = results.Single(result => result.Outcome == WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen);

		Assert.IsNotNull(alreadyOpen.Snapshot);
		Assert.AreEqual("initial", alreadyOpen.Snapshot!.Content);
		Assert.AreEqual(0, fixture.View.CloseCount, "A concurrent same-instance open must not close the view.");
	}

	[TestMethod]
	public async Task OpenWithView_RacingDuplicateViewIdsDetachTheLoser()
	{
		await using var fixture = new CoordinatorTestHost(pumpDispatch: true);
		var firstView = new FakeCoordinatorView(fixture, "shared-view");
		var secondView = new FakeCoordinatorView(fixture, "shared-view");

		Task<WorkspaceViewCoordinatorOpenResult> first = fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: firstView,
			cancellationToken: TestContext.CancellationToken
		);

		Task<WorkspaceViewCoordinatorOpenResult> second = fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: secondView,
			cancellationToken: TestContext.CancellationToken
		);

		WorkspaceViewCoordinatorOpenResult[] results = await Task.WhenAll(first, second);
		Assert.ContainsSingle(result => result.Outcome == WorkspaceViewCoordinatorOpenOutcome.Opened, results);

		WorkspaceViewCoordinatorOpenResult inUse = results.Single(result => result.Outcome == WorkspaceViewCoordinatorOpenOutcome.ViewInUse);

		Assert.IsNotNull(inUse.Snapshot, "The duplicate was detected after the document was loaded.");
		Assert.AreEqual(1, firstView.CloseCount + secondView.CloseCount, "The losing view is detached again.");
		Assert.AreEqual(1, firstView.ApplySubscriberCount + secondView.ApplySubscriberCount, "Only the winning view stays subscribed.");
	}

	[TestMethod]
	public async Task Commit_ViewThatGainsPendingEditsDuringTheCommitIsReportedUnsynchronized()
	{
		var fileSystem = new GatedWorkspaceFileSystem(new LocalWorkspaceFileSystem());
		await using var fixture = new CoordinatorTestHost(fileSystem);

		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		WorkspaceDocumentMutationResult edited = fixture.Store.Replace(
			new WorkspaceDocumentReplaceRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), "edited", snapshot.FileFormat)
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, edited.Outcome);

		Task<WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult>> commit = fixture.Coordinator.CommitAsync(
			new WorkspaceDocumentCommitRequest(
				new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
				edited.Snapshot.OnDiskStamp
			),

			TestContext.CancellationToken
		);

		await fileSystem.WriteEntered.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);

		// The user keeps typing while the commit writes: the view has pending edits when the
		// post-operation report is composed, so the view is not in sync with the committed document.
		// The view holds its own unpublished buffer, which the refresh must not overwrite.
		fixture.View.SetText("unpublished");
		fixture.View.HasPendingEdits = true;
		fileSystem.ReleaseWrite();

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult> result = await commit;
		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, result.StoreResult?.Outcome);

		// The report covers view state, not only failures: the entry carries no failure detail,
		// because the blocking state itself is the reason and the host can inspect it on the view.
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.ViewSynchronization.Outcome);

		WorkspaceDocumentViewSynchronizationIssue issue = result.ViewSynchronization.Issues.Single();

		Assert.AreEqual(fixture.View.ViewId, issue.ViewId);
		Assert.IsNull(issue.Failure);
		Assert.IsTrue(fixture.View.HasPendingEdits, "The refresh does not clear the view's pending edits.");
		Assert.AreEqual("unpublished", fixture.View.Text, "A view with unpublished edits is not refreshed over.");
	}

	[TestMethod]
	public async Task ApplyRequested_StaleRejectionIsAcknowledgedAndDoesNotBlockLaterOperations()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// Advance the document behind the view so the published replacement is stale.
		WorkspaceDocumentMutationResult behind = fixture.Store.Replace(
			new WorkspaceDocumentReplaceRequest(
				new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
				"behind the view",
				snapshot.FileFormat
			)
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, behind.Outcome);
		Task refreshed = fixture.View.NextRefresh;

		fixture.View.RaiseApply(
			new WorkspaceDocumentReplaceRequest(
				new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
				"stale publish",
				snapshot.FileFormat
			)
		);

		// The rejected replacement is acknowledged to the view, which adopts the current state; the
		// rejection is not retained as unsynchronized view state, so later disk operations proceed.
		await refreshed.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
		Assert.AreEqual("behind the view", fixture.View.Text);

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult> committed = await fixture.Coordinator.CommitAsync(
			new WorkspaceDocumentCommitRequest(
				new(behind.Snapshot!.DocumentKey, behind.Snapshot.DocumentId, behind.Snapshot.Version),
				behind.Snapshot.OnDiskStamp
			),

			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, committed.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, committed.ViewSynchronization.Outcome);
		Assert.AreEqual("behind the view", File.ReadAllText(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task Delete_LateViewReboundToAnotherInstanceIsLeftAlone()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		var lateView = new FakeCoordinatorView(fixture, "late-view");

		// The late view attaches during the delete window and reports the key of another document
		// instance by the time the sweep runs, which is what a view rebound to a reopened document at
		// the same path reports; the sweep must leave it attached instead of closing it.
		lateView.OnAfterOpen = () => lateView.DocumentKeyOverride = new WorkspaceDocumentKey(Guid.NewGuid());
		Task<WorkspaceViewCoordinatorOpenResult> attachTask = fixture.AttachViewDuringDeleteLease(lateView);

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> result = await fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Opened, (await attachTask).Outcome);
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, result.StoreResult?.Outcome);
		Assert.AreEqual(1, fixture.View.CloseCount, "The captured view is closed with the removed instance.");
		Assert.AreEqual(0, lateView.CloseCount, "A late view rebound to another instance is left alone by the sweep.");
	}

	[TestMethod]
	public async Task Delete_ViewThatFailsLeaseReleaseAndCloseReportsOneIssue()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.FailDeleteLeaseRelease = true;
		fixture.View.DeleteLeaseFailure = new WorkspaceOperationFailure("DeleteLeaseReleaseFailed", "The view cannot release its lease.");
		fixture.View.ThrowOnClose = true;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> result = await fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, result.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.ViewSynchronization.Outcome);

		// The release failure and the close failure both report the same view; the issue list is
		// de-duplicated by view id and keeps the entry that carries the failure.
		Assert.HasCount(1, result.ViewSynchronization.Issues, "A view reported twice keeps one issue.");
		Assert.AreEqual(fixture.View.ViewId, result.ViewSynchronization.Issues[0].ViewId);
		Assert.AreEqual("DeleteLeaseReleaseFailed", result.ViewSynchronization.Issues[0].Failure!.Code);
	}

	[TestMethod]
	public async Task Delete_ViewWithoutDeleteLeaseCapabilityIsSkipped()
	{
		await using var fixture = new CoordinatorTestHost();
		var plainView = new PlainTestView();

		WorkspaceViewCoordinatorOpenResult opened = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: plainView,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Opened, opened.Outcome);

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> result = await fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(
				new(opened.Snapshot!.DocumentKey, opened.Snapshot.DocumentId, opened.Snapshot.Version),
				opened.Snapshot.OnDiskStamp
			),

			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, result.StoreResult?.Outcome);
		Assert.AreEqual(1, plainView.CloseCount, "A view without the lease capability is skipped, not leased.");
		Assert.IsFalse(File.Exists(fixture.DocumentPath));
	}

	// A view with only the base capability: delete operations must skip its missing lease instead of
	// failing the operation.
	private sealed class PlainTestView : IWorkspaceDocumentView
	{
		private EventHandler<ApplyRequestedEventArgs>? _applyRequested;
		private string _text = string.Empty;

		public event EventHandler<ApplyRequestedEventArgs>? ApplyRequested
		{
			add => _applyRequested += value;
			remove => _applyRequested -= value;
		}

		public string ViewId { get; } = "plain-view";

		public WorkspaceDocumentKey? DocumentKey { get; private set; }

		public bool HasPendingEdits { get; set; }

		public bool HasConflict { get; set; }

		public int CloseCount { get; private set; }

		public int RefreshCount { get; private set; }

		public WorkspaceDocumentViewOpenResult Open(WorkspaceDocumentSnapshot snapshot)
		{
			Adopt(snapshot);
			return new WorkspaceDocumentViewOpenResult(WorkspaceDocumentViewOpenOutcome.Opened);
		}

		public WorkspaceDocumentViewRefreshResult Refresh(WorkspaceDocumentSnapshot snapshot)
		{
			RefreshCount++;
			Adopt(snapshot);
			return new WorkspaceDocumentViewRefreshResult(WorkspaceDocumentViewRefreshOutcome.Refreshed);
		}

		public WorkspaceDocumentViewIdentityResult AcknowledgeIdentity(WorkspaceDocumentViewIdentityChange change)
		{
			Adopt(change.Snapshot);
			return new WorkspaceDocumentViewIdentityResult(WorkspaceDocumentViewIdentityOutcome.Updated);
		}

		public WorkspaceDocumentViewApplyResult AcknowledgeApply(WorkspaceDocumentMutationResult result)
		{
			if (result.Snapshot is not null)
				Adopt(result.Snapshot);

			return new WorkspaceDocumentViewApplyResult(WorkspaceDocumentViewApplyOutcome.Applied);
		}

		public void Close()
		{
			CloseCount++;
			DocumentKey = null;
		}

		private void Adopt(WorkspaceDocumentSnapshot snapshot)
		{
			DocumentKey = snapshot.DocumentKey;
			_text = snapshot.Content;
		}
	}
}
