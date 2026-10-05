using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceViewCoordinatorTests
{
	[TestMethod]
	public async Task ApplyRequested_ReplacesDocumentWithPublishedContent()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		fixture.View.RaiseApply(
			new WorkspaceDocumentReplaceRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), "edited", snapshot.FileFormat)
		);

		Assert.IsTrue(fixture.Store.TryGetSnapshot(snapshot.DocumentId, out WorkspaceDocumentSnapshot? replaced));
		Assert.IsNotNull(replaced);
		Assert.AreEqual("edited", replaced.Content);
		Assert.IsTrue(replaced.IsDirty);
		Assert.AreEqual("edited", fixture.View.Text);
	}

	[TestMethod]
	public async Task ApplyRequested_ViewWithPendingEditsPublishesThemAndClearsTheBlockingState()
	{
		await using var fixture = new CoordinatorTestHost();

		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		string destinationPath = Path.Combine(fixture.DirectoryPath, "renamed.txt");

		fixture.View.HasPendingEdits = true;

		// The apply is the view publishing its own pending edits; the acknowledgment applies the
		// replacement and must clear the pending state instead of leaving the view blocking.
		fixture.View.RaiseApply(
			new WorkspaceDocumentReplaceRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), "edited", snapshot.FileFormat)
		);

		Assert.IsTrue(fixture.Store.TryGetSnapshot(snapshot.DocumentId, out WorkspaceDocumentSnapshot? replaced));
		Assert.AreEqual("edited", replaced!.Content);
		Assert.IsFalse(fixture.View.HasPendingEdits);

		WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentRenameResult> renamed = await fixture.Coordinator.RenameAsync(
			new WorkspaceDocumentRenameRequest(
				new(replaced.DocumentKey, replaced.DocumentId, replaced.Version),
				replaced.OnDiskStamp,
				destinationPath
			),

			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, renamed.StoreResult.Outcome);
		Assert.IsTrue(File.Exists(destinationPath));
	}

	[TestMethod]
	public async Task ApplyRequested_AfterDetachIsIgnored()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.Coordinator.DetachView(fixture.View);

		// The view is uncoordinated: a raise after detachment must not mutate the document.
		fixture.View.RaiseApply(
			new WorkspaceDocumentReplaceRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), "ignored", snapshot.FileFormat)
		);

		Assert.IsTrue(fixture.Store.TryGetSnapshot(snapshot.DocumentId, out WorkspaceDocumentSnapshot? current));
		Assert.AreEqual("initial", current!.Content);
	}

	[TestMethod]
	public async Task Discard_RestoresBaselineAndRefreshesAttachedView()
	{
		await using var fixture = new CoordinatorTestHost();

		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		WorkspaceDocumentMutationResult edited = fixture.Store.Replace(
			new WorkspaceDocumentReplaceRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), "edited", snapshot.FileFormat)
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, edited.Outcome);

		// Stale text proves the discard acknowledgment actually refreshed the view.
		fixture.View.SetText("stale view text");
		int refreshCountBefore = fixture.View.RefreshCount;

		WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentMutationResult> discarded = await fixture.Coordinator.DiscardAsync(
			new WorkspaceDocumentDiscardRequest(new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version))
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, discarded.StoreResult.Outcome);
		Assert.IsFalse(discarded.StoreResult.Snapshot!.IsDirty);
		Assert.AreEqual("initial", fixture.View.Text);
		Assert.AreEqual(refreshCountBefore + 1, fixture.View.RefreshCount);
	}

	[TestMethod]
	public async Task Replace_WithoutSourceViewMutatesDocumentAndRefreshesFallenBehindView()
	{
		await using var fixture = new CoordinatorTestHost();

		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// Advance the store behind the view so the coordinator sees an older peer to refresh.
		WorkspaceDocumentMutationResult behind = fixture.Store.Replace(
			new WorkspaceDocumentReplaceRequest(
				new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
				"behind the view",
				snapshot.FileFormat
			)
		);

		WorkspaceDocumentSnapshot behindSnapshot = behind.Snapshot!;
		int refreshBefore = fixture.View.RefreshCount;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> result = await fixture.Coordinator.ReplaceAsync(
			new WorkspaceDocumentReplaceRequest(
				new(behindSnapshot.DocumentKey, behindSnapshot.DocumentId, behindSnapshot.Version),
				"coordinator edit",
				behindSnapshot.FileFormat
			)
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, result.StoreResult.Outcome);
		Assert.AreEqual(refreshBefore + 1, fixture.View.RefreshCount);
		Assert.AreEqual("coordinator edit", fixture.View.Text);
	}

	[TestMethod]
	public async Task Replace_NonThrowingRefreshOutcomesMarkViewsUnsynchronizedAndBlockLaterDiskOperations()
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

		// Dirty the store behind both views so the Replace has fallen-behind peers to refresh.
		WorkspaceDocumentMutationResult behind = fixture.Store.Replace(
			new WorkspaceDocumentReplaceRequest(
				new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
				"behind the views",
				snapshot.FileFormat
			)
		);

		WorkspaceDocumentSnapshot behindSnapshot = behind.Snapshot!;

		fixture.View.RefreshOutcome = WorkspaceDocumentViewRefreshOutcome.MarkedStale;
		secondView.RefreshOutcome = WorkspaceDocumentViewRefreshOutcome.Failed;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> result = await fixture.Coordinator.ReplaceAsync(
			new WorkspaceDocumentReplaceRequest(
				new(behindSnapshot.DocumentKey, behindSnapshot.DocumentId, behindSnapshot.Version),
				"coordinator edit",
				behindSnapshot.FileFormat
			)
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, result.StoreResult.Outcome);

		// A refresh result that is not Refreshed marks the view unsynchronized even when the view
		// reports it without throwing, so a later delete reports both views as blocking.
		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> blocked = await fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(
				new(result.StoreResult.Snapshot!.DocumentKey, result.StoreResult.Snapshot.DocumentId, result.StoreResult.Snapshot.Version),
				result.StoreResult.Snapshot.OnDiskStamp
			),

			TestContext.CancellationToken
		);

		Assert.IsNull(blocked.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, blocked.ViewSynchronization.Outcome);

		// The ids are normalized ordinally, so the reported order does not depend on registration
		// order: "second-view" sorts before "test-view".
		Assert.AreSequenceEqual([secondView.ViewId, fixture.View.ViewId], ViewIdsOf(blocked.ViewSynchronization));

		Assert.IsTrue(
			blocked.ViewSynchronization.Issues.All(issue => issue.Failure is not null),
			"A recorded synchronization failure is reported with its detail."
		);

		Assert.IsTrue(File.Exists(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task SaveAs_ProceedsWhileAViewHasConflict()
	{
		await using var fixture = new CoordinatorTestHost();

		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		string destinationPath = Path.Combine(fixture.DirectoryPath, "saved-as.txt");

		// View state cannot be lost by a save-as, so a conflict does not block it; the view is
		// rekeyed to the new identity afterwards.
		fixture.View.HasConflict = true;

		WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentSaveAsResult> result = await fixture.Coordinator.SaveAsAsync(
			new WorkspaceDocumentSaveAsRequest(
				new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
				snapshot.OnDiskStamp,
				destinationPath
			),

			TestContext.CancellationToken
		);

		Assert.IsNotNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, result.StoreResult.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.ViewSynchronization.Outcome);
		Assert.IsTrue(File.Exists(destinationPath));
		Assert.AreEqual(result.StoreResult.Snapshot!.DocumentId, fixture.View.DocumentId);
	}

	[TestMethod]
	public async Task Reload_RefreshesAttachedViewOnReloadedResult()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		File.WriteAllText(fixture.DocumentPath, "changed externally");
		int refreshCountBefore = fixture.View.RefreshCount;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult> result = await fixture.Coordinator.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, result.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.ViewSynchronization.Outcome);
		Assert.AreEqual("changed externally", fixture.View.Text);
		Assert.AreEqual(refreshCountBefore + 1, fixture.View.RefreshCount);
	}

	[TestMethod]
	public async Task Reload_BlockedByPendingEditsLeavesTheDocumentUnmodified()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.HasPendingEdits = true;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult> result = await fixture.Coordinator.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)),
			TestContext.CancellationToken
		);

		// A view with pending edits would lose them when the content is replaced from disk, so the
		// reload is blocked before the store is reached.
		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, result.ViewSynchronization.Outcome);
		Assert.Contains(fixture.View.ViewId, ViewIdsOf(result.ViewSynchronization));
		Assert.AreEqual("initial", fixture.View.Text);
	}

	[TestMethod]
	public async Task Commit_BlockedByPendingEditsReportsBlockedViewWithoutSaving()
	{
		await using var fixture = new CoordinatorTestHost();

		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		WorkspaceDocumentMutationResult edited = fixture.Store.Replace(
			new WorkspaceDocumentReplaceRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), "edited", snapshot.FileFormat)
		);

		fixture.View.HasPendingEdits = true;

		// The view still holds unpublished edits, so a commit would write content the view has not
		// published; the commit is blocked before the store is reached.
		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult> result = await fixture.Coordinator.CommitAsync(
			new WorkspaceDocumentCommitRequest(
				new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
				edited.Snapshot.OnDiskStamp
			),

			TestContext.CancellationToken
		);

		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, result.ViewSynchronization.Outcome);
		Assert.Contains(fixture.View.ViewId, ViewIdsOf(result.ViewSynchronization));
		Assert.AreEqual("initial", File.ReadAllText(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task Reload_ViewRefreshFailureReportsUnsynchronizedView()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		File.WriteAllText(fixture.DocumentPath, "changed externally");
		fixture.View.FailRefresh = true;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult> result = await fixture.Coordinator.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)),
			TestContext.CancellationToken
		);

		// The store reload succeeded, but the view could not refresh, so it stays unsynchronized.
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, result.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.ViewSynchronization.Outcome);
		Assert.Contains(fixture.View.ViewId, ViewIdsOf(result.ViewSynchronization));
		Assert.IsTrue(fixture.Store.TryGetSnapshot(snapshot.DocumentId, out WorkspaceDocumentSnapshot? reloaded));
		Assert.AreEqual("changed externally", reloaded!.Content);

		// The reported issue carries the failure the coordinator synthesized from the throwing member.
		WorkspaceDocumentViewSynchronizationIssue issue = result.ViewSynchronization.Issues.Single();
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewRefreshFailed, issue.Failure!.Code);
	}

	[TestMethod]
	public async Task Commit_WhenTheStoreRejectsTheCommit_StillReportsAPreviouslyUnsynchronizedView()
	{
		await using var fixture = new CoordinatorTestHost();

		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// A first commit writes the document but leaves the view unsynchronized, so the view is retained
		// as unsynchronized state for later operations.
		WorkspaceDocumentMutationResult replaced = fixture.Store.Replace(
			new WorkspaceDocumentReplaceRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), "edited", snapshot.FileFormat)
		);

		WorkspaceDocumentSnapshot updated = replaced.Snapshot!;

		fixture.View.FailRefresh = true;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult> committed = await fixture.Coordinator.CommitAsync(
			new WorkspaceDocumentCommitRequest(new(updated.DocumentKey, updated.DocumentId, updated.Version), updated.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, committed.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, committed.ViewSynchronization.Outcome);

		// The second commit carries a stamp that disagrees with the state the store last observed, so
		// the store rejects it and the operation never reaches its post-commit refresh.
		var staleExpectedStamp = new FileStamp(Exists: true, Length: 4, LastWriteTimeUtc: DateTime.UnixEpoch.AddDays(-1), ContentHash: "stale");

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult> rejected = await fixture.Coordinator.CommitAsync(
			new WorkspaceDocumentCommitRequest(new(updated.DocumentKey, updated.DocumentId, updated.Version), staleExpectedStamp),
			TestContext.CancellationToken
		);

		Assert.AreNotEqual(WorkspaceDocumentCommitOutcome.Committed, rejected.StoreResult?.Outcome);

		// The report is composed from the current view state: the view is still unsynchronized from the
		// earlier failed refresh, so a clean report would be wrong.
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, rejected.ViewSynchronization.Outcome);
		Assert.Contains(fixture.View.ViewId, ViewIdsOf(rejected.ViewSynchronization));
	}

	[TestMethod]
	public async Task Commit_FailedViewRefreshReportsUnsynchronizedViewAndRecoversOnRetry()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// Dirty the document behind the view so the commit writes and the view falls behind.
		WorkspaceDocumentMutationResult replaced = fixture.Store.Replace(
			new WorkspaceDocumentReplaceRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), "edited", snapshot.FileFormat)
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.Outcome);
		WorkspaceDocumentSnapshot updated = replaced.Snapshot!;
		fixture.View.FailRefresh = true;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult> failed = await fixture.Coordinator.CommitAsync(
			new WorkspaceDocumentCommitRequest(new(updated.DocumentKey, updated.DocumentId, updated.Version), updated.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, failed.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, failed.ViewSynchronization.Outcome);
		Assert.Contains(fixture.View.ViewId, ViewIdsOf(failed.ViewSynchronization));

		fixture.View.FailRefresh = false;

		// The retry must not be rejected by the pre-check: the commit itself is a clean no-op and
		// the post-commit refresh retries the failed view synchronization.
		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult> recovered = await fixture.Coordinator.CommitAsync(
			new WorkspaceDocumentCommitRequest(
				new(updated.DocumentKey, updated.DocumentId, updated.Version),
				failed.StoreResult!.Snapshot!.OnDiskStamp
			),

			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, recovered.StoreResult?.Outcome);
		Assert.AreEqual("edited", fixture.View.Text);
	}

	[TestMethod]
	public async Task Commit_RetriesSynchronizationForUnsynchronizedViewAtCurrentVersion()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// The view is marked unsynchronized without falling behind the document version, so only the
		// synchronization retry can clear the state; the manually staled text proves the retry ran.
		await fixture.MarkViewUnsynchronizedAsync(snapshot);
		fixture.View.SetText("stale view text");

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult> result = await fixture.Coordinator.CommitAsync(
			new WorkspaceDocumentCommitRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, result.StoreResult?.Outcome);
		Assert.AreEqual("initial", fixture.View.Text);
	}

	[TestMethod]
	public async Task SaveAs_ViewIdentityUpdateFailureReportsUnsynchronizedView()
	{
		await using var fixture = new CoordinatorTestHost();

		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		string destinationPath = Path.Combine(fixture.DirectoryPath, "saved-as.txt");

		fixture.View.FailIdentityAck = true;

		WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentSaveAsResult> result = await fixture.Coordinator.SaveAsAsync(
			new WorkspaceDocumentSaveAsRequest(
				new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
				snapshot.OnDiskStamp,
				destinationPath
			),

			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, result.StoreResult.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.ViewSynchronization.Outcome);
		Assert.Contains(fixture.View.ViewId, ViewIdsOf(result.ViewSynchronization));
		Assert.IsTrue(File.Exists(destinationPath));
	}

	[TestMethod]
	public async Task SaveAs_SucceedsAndRekeysSynchronizedView()
	{
		await using var fixture = new CoordinatorTestHost();

		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		string destinationPath = Path.Combine(fixture.DirectoryPath, "saved-as.txt");

		WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentSaveAsResult> result = await fixture.Coordinator.SaveAsAsync(
			new WorkspaceDocumentSaveAsRequest(
				new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
				snapshot.OnDiskStamp,
				destinationPath
			),

			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, result.StoreResult.Outcome);
		Assert.IsNotNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.ViewSynchronization.Outcome);
		Assert.IsEmpty(result.ViewSynchronization.Issues);
		Assert.AreEqual(Path.GetFullPath(destinationPath), result.StoreResult.Snapshot!.DocumentId);
		Assert.AreEqual(result.StoreResult.Snapshot.DocumentId, fixture.View.DocumentId);
		Assert.IsTrue(File.Exists(destinationPath));

		// The view is tracked under the destination identity, so a later mutation of the retargeted
		// document still reaches it.
		int refreshBefore = fixture.View.RefreshCount;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await fixture.Coordinator.ReplaceAsync(
			new WorkspaceDocumentReplaceRequest(
				new(result.StoreResult.Snapshot.DocumentKey, result.StoreResult.Snapshot.DocumentId, result.StoreResult.Snapshot.Version),
				"after save-as",
				result.StoreResult.Snapshot.FileFormat
			)
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.StoreResult.Outcome);
		Assert.AreEqual(refreshBefore + 1, fixture.View.RefreshCount);
		Assert.AreEqual("after save-as", fixture.View.Text);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_ViewRefreshFailureReportsUnsynchronizedView()
	{
		await using var fixture = new CoordinatorTestHost();

		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// Dirty the document and change the file externally to create a conflict.
		WorkspaceDocumentMutationResult edited = fixture.Store.Replace(
			new WorkspaceDocumentReplaceRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), "edited", snapshot.FileFormat)
		);

		WorkspaceDocumentSnapshot dirty = edited.Snapshot!;

		File.WriteAllText(fixture.DocumentPath, "external");

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult> conflict = await fixture.Coordinator.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(dirty.DocumentKey, dirty.DocumentId, dirty.Version)),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ExternalFileConflict, conflict.StoreResult?.Outcome);

		fixture.View.FailRefresh = true;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentConflictResolutionResult> result =
			await fixture.Coordinator.ResolveExternalConflictAsync(
				new WorkspaceDocumentConflictResolutionRequest(
					new(dirty.DocumentKey, dirty.DocumentId, dirty.Version),
					conflict.StoreResult!.ObservedOnDiskStamp!.Value,
					WorkspaceDocumentConflictResolutionMode.ResolveWithDisk
				),

				TestContext.CancellationToken
			);

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ResolvedWithDisk, result.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.ViewSynchronization.Outcome);
		Assert.Contains(fixture.View.ViewId, ViewIdsOf(result.ViewSynchronization));
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseLogicalCommitsAndKeepsViewSynchronized()
	{
		await using var fixture = new CoordinatorTestHost();

		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// Dirty the document and change the file externally to create a conflict.
		WorkspaceDocumentMutationResult edited = fixture.Store.Replace(
			new WorkspaceDocumentReplaceRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), "edited", snapshot.FileFormat)
		);

		WorkspaceDocumentSnapshot dirty = edited.Snapshot!;

		File.WriteAllText(fixture.DocumentPath, "external");

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult> conflict = await fixture.Coordinator.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(dirty.DocumentKey, dirty.DocumentId, dirty.Version)),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ExternalFileConflict, conflict.StoreResult?.Outcome);

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentConflictResolutionResult> result =
			await fixture.Coordinator.ResolveExternalConflictAsync(
				new WorkspaceDocumentConflictResolutionRequest(
					new(dirty.DocumentKey, dirty.DocumentId, dirty.Version),
					conflict.StoreResult!.ObservedOnDiskStamp!.Value,
					WorkspaceDocumentConflictResolutionMode.ResolveWithLogical
				),

				TestContext.CancellationToken
			);

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ResolvedWithLogical, result.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.ViewSynchronization.Outcome);
		Assert.IsFalse(result.StoreResult?.Snapshot!.IsDirty);
		Assert.AreEqual("edited", File.ReadAllText(fixture.DocumentPath));
		Assert.AreEqual("edited", fixture.View.Text);
	}

	[TestMethod]
	public async Task Rename_IdentityAcknowledgmentClearsUnsynchronizedState()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		await fixture.MarkViewUnsynchronizedAsync(snapshot);
		string destinationPath = Path.Combine(fixture.DirectoryPath, "renamed.txt");

		WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentRenameResult> renamed = await fixture.Coordinator.RenameAsync(
			new WorkspaceDocumentRenameRequest(
				new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
				snapshot.OnDiskStamp,
				destinationPath
			),

			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, renamed.StoreResult.Outcome);

		// The identity acknowledgment resynchronized the view, so the latch is cleared: the delete is
		// not blocked by a failure that has since been cleared.
		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> deleted = await fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(
				new(renamed.StoreResult.Snapshot!.DocumentKey, renamed.StoreResult.Snapshot.DocumentId, renamed.StoreResult.Snapshot.Version),
				renamed.StoreResult.Snapshot.OnDiskStamp
			),

			TestContext.CancellationToken
		);

		Assert.IsNotNull(deleted.StoreResult);
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, deleted.StoreResult?.Outcome);
		Assert.IsFalse(File.Exists(destinationPath));
	}

	[TestMethod]
	public async Task Reload_RetriesTheRefreshOfAnUnsynchronizedView()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		await fixture.MarkViewUnsynchronizedAsync(snapshot);
		File.WriteAllText(fixture.DocumentPath, "changed externally");

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult> result = await fixture.Coordinator.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)),
			TestContext.CancellationToken
		);

		// A view whose only outstanding state is a prior synchronization failure does not block the
		// reload; the refresh retries the failed synchronization and clears it.
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, result.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.ViewSynchronization.Outcome);
		Assert.AreEqual("changed externally", fixture.View.Text);
	}

	[TestMethod]
	public async Task Reload_BlockedByConflictLeavesTheDocumentUnmodified()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		File.WriteAllText(fixture.DocumentPath, "changed externally");
		fixture.View.HasConflict = true;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult> result = await fixture.Coordinator.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)),
			TestContext.CancellationToken
		);

		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, result.ViewSynchronization.Outcome);
		Assert.Contains(fixture.View.ViewId, ViewIdsOf(result.ViewSynchronization));
		Assert.AreEqual("initial", fixture.View.Text);
	}

	[TestMethod]
	public async Task ResolveConflict_RetriesTheRefreshOfAnUnsynchronizedView()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		await fixture.MarkViewUnsynchronizedAsync(snapshot);

		// Dirty the document and change the file externally to create a conflict.
		WorkspaceDocumentMutationResult edited = fixture.Store.Replace(
			new WorkspaceDocumentReplaceRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), "edited", snapshot.FileFormat)
		);

		WorkspaceDocumentSnapshot dirty = edited.Snapshot!;

		File.WriteAllText(fixture.DocumentPath, "external");

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult> conflict = await fixture.Coordinator.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(dirty.DocumentKey, dirty.DocumentId, dirty.Version)),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ExternalFileConflict, conflict.StoreResult?.Outcome);

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentConflictResolutionResult> result =
			await fixture.Coordinator.ResolveExternalConflictAsync(
				new WorkspaceDocumentConflictResolutionRequest(
					new(dirty.DocumentKey, dirty.DocumentId, dirty.Version),
					conflict.StoreResult!.ObservedOnDiskStamp!.Value,
					WorkspaceDocumentConflictResolutionMode.ResolveWithDisk
				),

				TestContext.CancellationToken
			);

		// The prior synchronization failure does not block the resolution; the post-resolution refresh
		// retries it and clears it.
		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ResolvedWithDisk, result.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.ViewSynchronization.Outcome);
		Assert.AreEqual("external", fixture.View.Text);
	}

	[TestMethod]
	public async Task Discard_AcknowledgeFailureReportsViewAcknowledgeFailed()
	{
		await using var fixture = new CoordinatorTestHost();

		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		WorkspaceDocumentMutationResult edited = fixture.Store.Replace(
			new WorkspaceDocumentReplaceRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), "edited", snapshot.FileFormat)
		);

		fixture.View.ThrowOnAcknowledgeApply = true;

		WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentMutationResult> discarded = await fixture.Coordinator.DiscardAsync(
			new WorkspaceDocumentDiscardRequest(new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version))
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, discarded.StoreResult.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, discarded.ViewSynchronization.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewAcknowledgeFailed, discarded.ViewSynchronization.Issues.Single().Failure!.Code);
	}

	[TestMethod]
	public async Task Rename_ThrowingIdentityAcknowledgmentReportsViewIdentityUpdateFailed()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.ThrowOnIdentityAck = true;
		string destinationPath = Path.Combine(fixture.DirectoryPath, "renamed.txt");

		WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentRenameResult> renamed = await fixture.Coordinator.RenameAsync(
			new WorkspaceDocumentRenameRequest(
				new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
				snapshot.OnDiskStamp,
				destinationPath
			),

			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, renamed.StoreResult.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, renamed.ViewSynchronization.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewIdentityUpdateFailed, renamed.ViewSynchronization.Issues.Single().Failure!.Code);
		Assert.AreEqual(snapshot.DocumentId, fixture.View.DocumentId, "A failed acknowledgment keeps the prior registration.");
	}

	[TestMethod]
	public async Task ApplyRequested_ApplyPathFailureIsRecordedAndBlocksLaterOperations()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// The store rejects the published replacement (a null content is an argument error); the event
		// has no caller to fault, so the failure is recorded with its detail and blocks the next
		// destructive operation.
		fixture.View.RaiseApply(
			new WorkspaceDocumentReplaceRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), null!, snapshot.FileFormat)
		);

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> blocked = await fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.IsNull(blocked.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, blocked.ViewSynchronization.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewApplyFailed, blocked.ViewSynchronization.Issues.Single().Failure!.Code);
		Assert.IsTrue(File.Exists(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task Rename_RekeysEveryAttachedPeerView()
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
		string destinationPath = Path.Combine(fixture.DirectoryPath, "renamed.txt");

		WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentRenameResult> renamed = await fixture.Coordinator.RenameAsync(
			new WorkspaceDocumentRenameRequest(
				new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
				snapshot.OnDiskStamp,
				destinationPath
			),

			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, renamed.StoreResult.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, renamed.ViewSynchronization.Outcome);
		Assert.AreEqual(renamed.StoreResult.Snapshot!.DocumentId, fixture.View.DocumentId);
		Assert.AreEqual(renamed.StoreResult.Snapshot.DocumentId, secondView.DocumentId);

		// Both views are tracked under the destination identity, so a later mutation reaches each.
		int firstRefreshBefore = fixture.View.RefreshCount;
		int secondRefreshBefore = secondView.RefreshCount;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await fixture.Coordinator.ReplaceAsync(
			new WorkspaceDocumentReplaceRequest(
				new(renamed.StoreResult.Snapshot.DocumentKey, renamed.StoreResult.Snapshot.DocumentId, renamed.StoreResult.Snapshot.Version),
				"after rename",
				renamed.StoreResult.Snapshot.FileFormat
			)
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.StoreResult.Outcome);
		Assert.AreEqual(firstRefreshBefore + 1, fixture.View.RefreshCount);
		Assert.AreEqual(secondRefreshBefore + 1, secondView.RefreshCount);
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Rename_LateViewAtVacatedPathIsNotRekeyed()
	{
		var dispatch = new GatedDispatch();
		await using var fixture = new CoordinatorTestHost(customDispatch: dispatch.Dispatch);

		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		var lateView = new FakeCoordinatorView(fixture, "late-view");
		string destinationPath = Path.Combine(fixture.DirectoryPath, "renamed.txt");

		// Hold the rename's identity acknowledgment so the vacated path can be reopened after the store
		// has completed the rename but before the acknowledgment runs.
		dispatch.HoldNext();

		Task<WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentRenameResult>> rename = fixture.Coordinator.RenameAsync(
			new WorkspaceDocumentRenameRequest(
				new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
				snapshot.OnDiskStamp,
				destinationPath
			),

			TestContext.CancellationToken
		);

		await dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);

		// A new file appears at the vacated path and a view attaches to the new document: same id
		// string, a different document key.
		File.WriteAllText(fixture.DocumentPath, "replacement");

		WorkspaceViewCoordinatorOpenResult lateOpen = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: lateView,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Opened, lateOpen.Outcome);
		Assert.AreEqual(snapshot.DocumentId, lateOpen.Snapshot!.DocumentId, "The new document reuses the vacated id string.");
		Assert.AreNotEqual(snapshot.DocumentKey, lateOpen.Snapshot.DocumentKey, "The new document is a different instance.");

		dispatch.Release();

		WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentRenameResult> renamed = await rename;

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, renamed.StoreResult.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, renamed.ViewSynchronization.Outcome);

		// The attached peer is rekeyed to the destination; the view registered at the vacated path keeps
		// its own document identity instead of being rekeyed with the renamed document's snapshot.
		Assert.AreEqual(renamed.StoreResult.Snapshot!.DocumentId, fixture.View.DocumentId);
		Assert.AreEqual(lateOpen.Snapshot.DocumentId, lateView.DocumentId);
		Assert.AreEqual(lateOpen.Snapshot.DocumentKey, lateView.DocumentKey);
	}

	// Runs dispatched actions inline, holding exactly one arm()ed action until it is released, so a
	// test can interleave another coordinator operation before the held action runs.
	private sealed class GatedDispatch
	{
		private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

		private int _hold;

		public Task Entered => _entered.Task;

		public void HoldNext() => Interlocked.Exchange(ref _hold, 1);

		public void Release() => _release.TrySetResult();

		public async Task Dispatch(Action action)
		{
			if (Interlocked.Exchange(ref _hold, 0) == 1)
			{
				_entered.TrySetResult();
				await _release.Task.ConfigureAwait(false);
			}

			action();
		}
	}
}
