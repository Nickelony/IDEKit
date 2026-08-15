using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceDocumentManagerTests
{
	[TestMethod]
	public async Task OpenWithView_ConcurrentSameInstanceWithContractAbidingViewReportsAlreadyOpen()
	{
		// The pump serializes dispatched actions, which is the documented obligation for views that are
		// not thread-safe; a second open of the same instance must then resolve as a no-op without
		// calling the view a second time or treating the view as in use by someone else.
		await using var fixture = new ManagerFixture(pumpDispatch: true);
		fixture.View.ReportAlreadyOpenWhenAttached = true;

		Task<WorkspaceDocumentManagerOpenResult> first = fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);
		Task<WorkspaceDocumentManagerOpenResult> second = fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		WorkspaceDocumentManagerOpenResult[] results = await Task.WhenAll(first, second);
		Assert.AreEqual(1, results.Count(result => result.Outcome == WorkspaceDocumentManagerOpenOutcome.Opened));
		WorkspaceDocumentManagerOpenResult alreadyOpen = results.Single(result => result.Outcome == WorkspaceDocumentManagerOpenOutcome.AlreadyOpen);
		Assert.IsNotNull(alreadyOpen.Snapshot);
		Assert.AreEqual("initial", alreadyOpen.Snapshot!.Content);
		Assert.AreEqual(0, fixture.View.CloseCount, "A concurrent same-instance open must not close the view.");
	}

	[TestMethod]
	public async Task OpenWithView_RacingDuplicateViewIdsDetachTheLoser()
	{
		await using var fixture = new ManagerFixture(pumpDispatch: true);
		var firstView = new TestView(fixture, "shared-view");
		var secondView = new TestView(fixture, "shared-view");

		Task<WorkspaceDocumentManagerOpenResult> first = fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			firstView);
		Task<WorkspaceDocumentManagerOpenResult> second = fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			secondView);

		WorkspaceDocumentManagerOpenResult[] results = await Task.WhenAll(first, second);
		Assert.AreEqual(1, results.Count(result => result.Outcome == WorkspaceDocumentManagerOpenOutcome.Opened));
		WorkspaceDocumentManagerOpenResult inUse = results.Single(result => result.Outcome == WorkspaceDocumentManagerOpenOutcome.ViewInUse);
		Assert.IsNotNull(inUse.Snapshot, "The duplicate was detected after the document was loaded.");
		Assert.AreEqual(1, firstView.CloseCount + secondView.CloseCount, "The losing view is detached again.");
		Assert.AreEqual(
			1,
			firstView.ApplySubscriberCount + secondView.ApplySubscriberCount,
			"Only the winning view stays subscribed.");
	}

	[TestMethod]
	public async Task Commit_ViewThatGainsPendingEditsDuringTheCommitIsReportedUnsynchronized()
	{
		var fileSystem = new CommitGateFileSystem(new LocalWorkspaceFileSystem());
		await using var fixture = new ManagerFixture(fileSystem: fileSystem);
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		WorkspaceDocumentMutationResult edited = fixture.Store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"edited",
			snapshot.FileFormat));
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, edited.Outcome);

		Task<WorkspaceDocumentManagerCommitResult> commit = fixture.Manager.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp));
		await fileSystem.WriteEntered.WaitAsync(TimeSpan.FromSeconds(10));

		// The user keeps typing while the commit writes: the view has pending edits when the
		// post-operation report is composed, so the view is not in sync with the committed document.
		// The view holds its own unpublished buffer, which the refresh must not overwrite.
		fixture.View.SetText("unpublished");
		fixture.View.HasPendingEdits = true;
		fileSystem.ReleaseWrite();

		WorkspaceDocumentManagerCommitResult result = await commit;
		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, result.Outcome);

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
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// Advance the document behind the view so the published replacement is stale.
		WorkspaceDocumentMutationResult behind = fixture.Store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"behind the view",
			snapshot.FileFormat));
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, behind.Outcome);

		fixture.View.RaiseApply(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"stale publish",
			snapshot.FileFormat));

		// The rejected replacement is acknowledged to the view, which adopts the current state; the
		// rejection is not retained as unsynchronized view state, so later disk operations proceed.
		Assert.IsTrue(SpinWait.SpinUntil(() => fixture.View.Text == "behind the view", TimeSpan.FromSeconds(10)));

		WorkspaceDocumentManagerCommitResult committed = await fixture.Manager.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(behind.Snapshot!.DocumentKey, behind.Snapshot.DocumentId, behind.Snapshot.Version),
			behind.Snapshot.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, committed.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, committed.ViewSynchronization.Outcome);
		Assert.AreEqual("behind the view", File.ReadAllText(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task Delete_LateViewReboundToAnotherInstanceIsLeftAlone()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		var lateView = new TestView(fixture, "late-view");

		// The late view attaches during the delete window and reports the key of another document
		// instance by the time the sweep runs, which is what a view rebound to a reopened document at
		// the same path reports; the sweep must leave it attached instead of closing it.
		lateView.OnAfterOpen = () => lateView.DocumentKeyOverride = new WorkspaceDocumentKey(Guid.NewGuid());
		Task<WorkspaceDocumentManagerOpenResult> attachTask = fixture.AttachViewDuringDeleteGuard(lateView);

		WorkspaceDocumentManagerDeleteResult result = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.Opened, (await attachTask).Outcome);
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, result.Outcome);
		Assert.AreEqual(1, fixture.View.CloseCount, "The captured view is closed with the removed instance.");
		Assert.AreEqual(0, lateView.CloseCount, "A late view rebound to another instance is left alone by the sweep.");
	}

	[TestMethod]
	public async Task Delete_ViewThatFailsGuardReleaseAndCloseReportsOneIssue()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.FailDeleteGuardRelease = true;
		fixture.View.DeleteGuardFailure = new WorkspaceOperationFailure("DeleteGuardReleaseFailed", "The view cannot release its guard.");
		fixture.View.ThrowOnClose = true;

		WorkspaceDocumentManagerDeleteResult result = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, result.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.ViewSynchronization.Outcome);

		// The release failure and the close failure both report the same view; the issue list is
		// de-duplicated by view id and keeps the entry that carries the failure.
		Assert.AreEqual(1, result.ViewSynchronization.Issues.Count, "A view reported twice keeps one issue.");
		Assert.AreEqual(fixture.View.ViewId, result.ViewSynchronization.Issues[0].ViewId);
		Assert.AreEqual("DeleteGuardReleaseFailed", result.ViewSynchronization.Issues[0].Failure!.Code);
	}

	[TestMethod]
	public async Task Delete_ViewWithoutDeleteGuardCapabilityIsSkipped()
	{
		await using var fixture = new ManagerFixture();
		var plainView = new PlainTestView();

		WorkspaceDocumentManagerOpenResult opened = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			plainView);
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.Opened, opened.Outcome);

		WorkspaceDocumentManagerDeleteResult result = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(opened.Snapshot!.DocumentKey, opened.Snapshot.DocumentId, opened.Snapshot.Version),
			opened.Snapshot.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, result.Outcome);
		Assert.AreEqual(1, plainView.CloseCount, "A view without the guard capability is skipped, not guarded.");
		Assert.IsFalse(File.Exists(fixture.DocumentPath));
	}

	// Freezes a write inside its file-system member so tests can change view state while the
	// operation is in flight.
	private sealed class CommitGateFileSystem(LocalWorkspaceFileSystem inner) : WorkspaceFileSystemDecorator(inner)
	{
		private readonly TaskCompletionSource _writeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _writeRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task WriteEntered => _writeEntered.Task;

		public void ReleaseWrite() => _writeRelease.TrySetResult();

		public override async Task<WorkspaceFileReplacementResult> WriteFileAsync(
			string destinationPath,
			ReadOnlyMemory<byte> content,
			FileStamp expectedStamp,
			CancellationToken cancellationToken = default)
		{
			_writeEntered.TrySetResult();
			await _writeRelease.Task.ConfigureAwait(false);
			return await Inner.WriteFileAsync(destinationPath, content, expectedStamp, cancellationToken).ConfigureAwait(false);
		}
	}

	// A view with only the base capability: delete operations must skip its missing guard instead of
	// failing the operation.
	private sealed class PlainTestView : IWorkspaceDocumentView
	{
		private EventHandler<WorkspaceDocumentViewApplyRequestedEventArgs>? _applyRequested;
		private string _text = string.Empty;
		private WorkspaceDocumentKey? _documentKey;

		public event EventHandler<WorkspaceDocumentViewApplyRequestedEventArgs>? ApplyRequested
		{
			add => _applyRequested += value;
			remove => _applyRequested -= value;
		}

		public string ViewId { get; } = "plain-view";

		public WorkspaceDocumentKey? DocumentKey => _documentKey;

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
			_documentKey = null;
		}

		private void Adopt(WorkspaceDocumentSnapshot snapshot)
		{
			_documentKey = snapshot.DocumentKey;
			_text = snapshot.Content;
		}
	}
}
