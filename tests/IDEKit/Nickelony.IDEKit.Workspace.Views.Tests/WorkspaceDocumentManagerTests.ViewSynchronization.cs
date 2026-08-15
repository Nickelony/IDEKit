using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceDocumentManagerTests
{
	[TestMethod]
	public async Task ApplyRequested_ReplacesDocumentWithPublishedContent()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		fixture.View.RaiseApply(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"edited",
			snapshot.FileFormat));

		Assert.IsTrue(fixture.Store.TryGetSnapshot(snapshot.DocumentId, out WorkspaceDocumentSnapshot? replaced));
		Assert.IsNotNull(replaced);
		Assert.AreEqual("edited", replaced.Content);
		Assert.IsTrue(replaced.IsDirty);
		Assert.AreEqual("edited", fixture.View.Text);
	}

	[TestMethod]
	public async Task ApplyRequested_ViewWithPendingEditsPublishesThemAndClearsTheBlockingState()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		string destinationPath = Path.Combine(fixture.DirectoryPath, "renamed.txt");
		fixture.View.HasPendingEdits = true;

		// The apply is the view publishing its own pending edits; the acknowledgment applies the
		// replacement and must clear the pending state instead of leaving the view blocking.
		fixture.View.RaiseApply(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"edited",
			snapshot.FileFormat));

		Assert.IsTrue(fixture.Store.TryGetSnapshot(snapshot.DocumentId, out WorkspaceDocumentSnapshot? replaced));
		Assert.AreEqual("edited", replaced!.Content);
		Assert.IsFalse(fixture.View.HasPendingEdits);

		WorkspaceDocumentManagerRenameResult renamed = await fixture.Manager.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(replaced.DocumentKey, replaced.DocumentId, replaced.Version),
			replaced.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, renamed.Outcome);
		Assert.IsTrue(File.Exists(destinationPath));
	}

	[TestMethod]
	public async Task ApplyRequested_AfterUnregisterIsIgnored()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.Manager.UnregisterOpenView(fixture.View);

		// The view is no longer coordinated: a raise after unregistration must not mutate the document.
		fixture.View.RaiseApply(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"ignored",
			snapshot.FileFormat));

		Assert.IsTrue(fixture.Store.TryGetSnapshot(snapshot.DocumentId, out WorkspaceDocumentSnapshot? current));
		Assert.AreEqual("initial", current!.Content);
	}

	[TestMethod]
	public async Task Discard_RestoresBaselineAndRefreshesAttachedView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		WorkspaceDocumentMutationResult edited = fixture.Store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"edited",
			snapshot.FileFormat));
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, edited.Outcome);

		// Stale text proves the discard acknowledgment actually refreshed the view.
		fixture.View.SetText("stale view text");
		int refreshCountBefore = fixture.View.RefreshCount;

		WorkspaceDocumentManagerMutationResult discarded = await fixture.Manager.DiscardAsync(new WorkspaceDocumentDiscardRequest(
			new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version)));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, discarded.Outcome);
		Assert.IsFalse(discarded.Snapshot!.IsDirty);
		Assert.AreEqual("initial", fixture.View.Text);
		Assert.AreEqual(refreshCountBefore + 1, fixture.View.RefreshCount);
	}

	[TestMethod]
	public async Task Replace_WithoutSourceViewMutatesDocumentAndRefreshesFallenBehindView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// Advance the store behind the view so the manager sees an older peer to refresh.
		WorkspaceDocumentMutationResult behind = fixture.Store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"behind the view",
			snapshot.FileFormat));
		WorkspaceDocumentSnapshot behindSnapshot = behind.Snapshot!;
		int refreshBefore = fixture.View.RefreshCount;

		WorkspaceDocumentManagerMutationResult result = await fixture.Manager.ReplaceAsync(new WorkspaceDocumentReplaceRequest(
			new(behindSnapshot.DocumentKey, behindSnapshot.DocumentId, behindSnapshot.Version),
			"manager edit",
			behindSnapshot.FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, result.Outcome);
		Assert.AreEqual(refreshBefore + 1, fixture.View.RefreshCount);
		Assert.AreEqual("manager edit", fixture.View.Text);
	}

	[TestMethod]
	public async Task Replace_NonThrowingRefreshOutcomesMarkViewsUnsynchronizedAndBlockLaterDiskOperations()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		var secondView = new TestView(fixture, "second-view");
		WorkspaceDocumentManagerOpenResult opened = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			secondView);
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.Opened, opened.Outcome);

		// Dirty the store behind both views so the Replace has fallen-behind peers to refresh.
		WorkspaceDocumentMutationResult behind = fixture.Store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"behind the views",
			snapshot.FileFormat));
		WorkspaceDocumentSnapshot behindSnapshot = behind.Snapshot!;
		fixture.View.RefreshOutcome = WorkspaceDocumentViewRefreshOutcome.MarkedStale;
		secondView.RefreshOutcome = WorkspaceDocumentViewRefreshOutcome.Failed;

		WorkspaceDocumentManagerMutationResult result = await fixture.Manager.ReplaceAsync(new WorkspaceDocumentReplaceRequest(
			new(behindSnapshot.DocumentKey, behindSnapshot.DocumentId, behindSnapshot.Version),
			"manager edit",
			behindSnapshot.FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, result.Outcome);

		// A refresh result that is not Refreshed marks the view unsynchronized even when the view
		// reports it without throwing, so a later delete reports both views as blocking.
		WorkspaceDocumentManagerDeleteResult blocked = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(result.Snapshot!.DocumentKey, result.Snapshot.DocumentId, result.Snapshot.Version),
			result.Snapshot.OnDiskStamp));

		Assert.IsNull(blocked.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, blocked.ViewSynchronization.Outcome);

		// The ids are normalized ordinally, so the reported order does not depend on registration
		// order: "second-view" sorts before "test-view".
		CollectionAssert.AreEqual(
			new[] { secondView.ViewId, fixture.View.ViewId },
			ViewIdsOf(blocked.ViewSynchronization));
		Assert.IsTrue(
			blocked.ViewSynchronization.Issues.All(issue => issue.Failure is not null),
			"A recorded synchronization failure is reported with its detail.");
		Assert.IsTrue(File.Exists(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task SaveAs_ProceedsWhileAViewHasConflict()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		string destinationPath = Path.Combine(fixture.DirectoryPath, "saved-as.txt");

		// View state cannot be lost by a save-as, so a conflict does not block it; the view is
		// rekeyed to the new identity afterwards.
		fixture.View.HasConflict = true;

		WorkspaceDocumentManagerSaveAsResult result = await fixture.Manager.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp,
			destinationPath));

		Assert.IsNotNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, result.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.ViewSynchronization.Outcome);
		Assert.IsTrue(File.Exists(destinationPath));
		Assert.AreEqual(result.Snapshot!.DocumentId, fixture.View.DocumentId);
	}

	[TestMethod]
	public async Task Reload_RefreshesAttachedViewOnReloadedResult()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		File.WriteAllText(fixture.DocumentPath, "changed externally");
		int refreshCountBefore = fixture.View.RefreshCount;

		WorkspaceDocumentManagerReloadResult result = await fixture.Manager.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)));

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, result.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.ViewSynchronization.Outcome);
		Assert.AreEqual("changed externally", fixture.View.Text);
		Assert.AreEqual(refreshCountBefore + 1, fixture.View.RefreshCount);
	}

	[TestMethod]
	public async Task Reload_BlockedByPendingEditsLeavesTheDocumentUnmodified()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.HasPendingEdits = true;

		WorkspaceDocumentManagerReloadResult result = await fixture.Manager.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)));

		// A view with pending edits would lose them when the content is replaced from disk, so the
		// reload is blocked before the store is reached.
		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, result.ViewSynchronization.Outcome);
		CollectionAssert.Contains(ViewIdsOf(result.ViewSynchronization), fixture.View.ViewId);
		Assert.AreEqual("initial", fixture.View.Text);
	}

	[TestMethod]
	public async Task Commit_BlockedByPendingEditsReportsBlockedViewWithoutSaving()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		WorkspaceDocumentMutationResult edited = fixture.Store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"edited",
			snapshot.FileFormat));
		fixture.View.HasPendingEdits = true;

		// The view still holds unpublished edits, so a commit would write content the view has not
		// published; the commit is blocked before the store is reached.
		WorkspaceDocumentManagerCommitResult result = await fixture.Manager.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp));

		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, result.ViewSynchronization.Outcome);
		CollectionAssert.Contains(ViewIdsOf(result.ViewSynchronization), fixture.View.ViewId);
		Assert.AreEqual("initial", File.ReadAllText(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task Reload_ViewRefreshFailureReportsUnsynchronizedView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		File.WriteAllText(fixture.DocumentPath, "changed externally");
		fixture.View.FailRefresh = true;

		WorkspaceDocumentManagerReloadResult result = await fixture.Manager.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)));

		// The store reload succeeded, but the view could not refresh, so it stays unsynchronized.
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, result.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.ViewSynchronization.Outcome);
		CollectionAssert.Contains(ViewIdsOf(result.ViewSynchronization), fixture.View.ViewId);
		Assert.IsTrue(fixture.Store.TryGetSnapshot(snapshot.DocumentId, out WorkspaceDocumentSnapshot? reloaded));
		Assert.AreEqual("changed externally", reloaded!.Content);

		// The reported issue carries the failure the manager synthesized from the throwing member.
		WorkspaceDocumentViewSynchronizationIssue issue = result.ViewSynchronization.Issues.Single();
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewRefreshFailed, issue.Failure!.Code);
	}

	[TestMethod]
	public async Task Commit_WhenTheStoreRejectsTheCommit_StillReportsAPreviouslyUnsynchronizedView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// A first commit writes the document but leaves the view unsynchronized, so the view is retained
		// as unsynchronized state for later operations.
		WorkspaceDocumentMutationResult replaced = fixture.Store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"edited",
			snapshot.FileFormat));
		WorkspaceDocumentSnapshot updated = replaced.Snapshot!;
		fixture.View.FailRefresh = true;

		WorkspaceDocumentManagerCommitResult committed = await fixture.Manager.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(updated.DocumentKey, updated.DocumentId, updated.Version),
			updated.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, committed.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, committed.ViewSynchronization.Outcome);

		// The second commit carries a stamp that disagrees with the state the store last observed, so
		// the store rejects it and the operation never reaches its post-commit refresh.
		var staleExpectedStamp = new FileStamp(true, 4, DateTime.UnixEpoch.AddDays(-1), "stale");

		WorkspaceDocumentManagerCommitResult rejected = await fixture.Manager.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(updated.DocumentKey, updated.DocumentId, updated.Version),
			staleExpectedStamp));

		Assert.AreNotEqual(WorkspaceDocumentCommitOutcome.Committed, rejected.Outcome);

		// The report is composed from the current view state: the view is still unsynchronized from the
		// earlier failed refresh, so a clean report would be wrong.
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, rejected.ViewSynchronization.Outcome);
		CollectionAssert.Contains(ViewIdsOf(rejected.ViewSynchronization), fixture.View.ViewId);
	}

	[TestMethod]
	public async Task Commit_FailedViewRefreshReportsUnsynchronizedViewAndRecoversOnRetry()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// Dirty the document behind the view so the commit writes and the view falls behind.
		WorkspaceDocumentMutationResult replaced = fixture.Store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"edited",
			snapshot.FileFormat));
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.Outcome);
		WorkspaceDocumentSnapshot updated = replaced.Snapshot!;
		fixture.View.FailRefresh = true;

		WorkspaceDocumentManagerCommitResult failed = await fixture.Manager.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(updated.DocumentKey, updated.DocumentId, updated.Version),
			updated.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, failed.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, failed.ViewSynchronization.Outcome);
		CollectionAssert.Contains(ViewIdsOf(failed.ViewSynchronization), fixture.View.ViewId);

		fixture.View.FailRefresh = false;

		// The retry must not be rejected by the pre-check: the commit itself is a clean no-op and
		// the post-commit refresh retries the failed view synchronization.
		WorkspaceDocumentManagerCommitResult recovered = await fixture.Manager.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(updated.DocumentKey, updated.DocumentId, updated.Version),
			failed.Snapshot!.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, recovered.Outcome);
		Assert.AreEqual("edited", fixture.View.Text);
	}

	[TestMethod]
	public async Task Commit_RetriesSynchronizationForUnsynchronizedViewAtCurrentVersion()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// The view is marked unsynchronized without falling behind the document version, so only the
		// synchronization retry can clear the state; the manually staled text proves the retry ran.
		await fixture.MarkViewUnsynchronizedAsync(snapshot);
		fixture.View.SetText("stale view text");

		WorkspaceDocumentManagerCommitResult result = await fixture.Manager.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, result.Outcome);
		Assert.AreEqual("initial", fixture.View.Text);
	}

	[TestMethod]
	public async Task SaveAs_ViewIdentityUpdateFailureReportsUnsynchronizedView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		string destinationPath = Path.Combine(fixture.DirectoryPath, "saved-as.txt");
		fixture.View.FailIdentityAck = true;

		WorkspaceDocumentManagerSaveAsResult result = await fixture.Manager.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, result.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.ViewSynchronization.Outcome);
		CollectionAssert.Contains(ViewIdsOf(result.ViewSynchronization), fixture.View.ViewId);
		Assert.IsTrue(File.Exists(destinationPath));
	}

	[TestMethod]
	public async Task SaveAs_SucceedsAndRekeysSynchronizedView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		string destinationPath = Path.Combine(fixture.DirectoryPath, "saved-as.txt");

		WorkspaceDocumentManagerSaveAsResult result = await fixture.Manager.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, result.Outcome);
		Assert.IsNotNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.ViewSynchronization.Outcome);
		Assert.AreEqual(0, result.ViewSynchronization.Issues.Count);
		Assert.AreEqual(Path.GetFullPath(destinationPath), result.Snapshot!.DocumentId);
		Assert.AreEqual(result.Snapshot.DocumentId, fixture.View.DocumentId);
		Assert.IsTrue(File.Exists(destinationPath));

		// The view is tracked under the destination identity, so a later mutation of the retargeted
		// document still reaches it.
		int refreshBefore = fixture.View.RefreshCount;
		WorkspaceDocumentManagerMutationResult replaced = await fixture.Manager.ReplaceAsync(new WorkspaceDocumentReplaceRequest(
			new(result.Snapshot.DocumentKey, result.Snapshot.DocumentId, result.Snapshot.Version),
			"after save-as",
			result.Snapshot.FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.Outcome);
		Assert.AreEqual(refreshBefore + 1, fixture.View.RefreshCount);
		Assert.AreEqual("after save-as", fixture.View.Text);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_ViewRefreshFailureReportsUnsynchronizedView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// Dirty the document and change the file externally to create a conflict.
		WorkspaceDocumentMutationResult edited = fixture.Store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"edited",
			snapshot.FileFormat));
		WorkspaceDocumentSnapshot dirty = edited.Snapshot!;
		File.WriteAllText(fixture.DocumentPath, "external");

		WorkspaceDocumentManagerReloadResult conflict = await fixture.Manager.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(dirty.DocumentKey, dirty.DocumentId, dirty.Version)));
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ExternalFileConflict, conflict.Outcome);

		fixture.View.FailRefresh = true;

		WorkspaceDocumentManagerConflictResolutionResult result = await fixture.Manager.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(dirty.DocumentKey, dirty.DocumentId, dirty.Version),
				conflict.StoreResult!.ObservedOnDiskStamp!.Value,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ResolvedWithDisk, result.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.ViewSynchronization.Outcome);
		CollectionAssert.Contains(ViewIdsOf(result.ViewSynchronization), fixture.View.ViewId);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseLogicalCommitsAndKeepsViewSynchronized()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// Dirty the document and change the file externally to create a conflict.
		WorkspaceDocumentMutationResult edited = fixture.Store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"edited",
			snapshot.FileFormat));
		WorkspaceDocumentSnapshot dirty = edited.Snapshot!;
		File.WriteAllText(fixture.DocumentPath, "external");

		WorkspaceDocumentManagerReloadResult conflict = await fixture.Manager.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(dirty.DocumentKey, dirty.DocumentId, dirty.Version)));
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ExternalFileConflict, conflict.Outcome);

		WorkspaceDocumentManagerConflictResolutionResult result = await fixture.Manager.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(dirty.DocumentKey, dirty.DocumentId, dirty.Version),
				conflict.StoreResult!.ObservedOnDiskStamp!.Value,
				WorkspaceDocumentConflictResolutionChoice.UseLogical));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ResolvedWithLogical, result.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.ViewSynchronization.Outcome);
		Assert.IsFalse(result.Snapshot!.IsDirty);
		Assert.AreEqual("edited", File.ReadAllText(fixture.DocumentPath));
		Assert.AreEqual("edited", fixture.View.Text);
	}

	[TestMethod]
	public async Task Rename_IdentityAcknowledgmentClearsUnsynchronizedState()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		await fixture.MarkViewUnsynchronizedAsync(snapshot);
		string destinationPath = Path.Combine(fixture.DirectoryPath, "renamed.txt");

		WorkspaceDocumentManagerRenameResult renamed = await fixture.Manager.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp,
			destinationPath));
		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, renamed.Outcome);

		// The identity acknowledgment resynchronized the view, so the latch is cleared: the delete is
		// not blocked by a failure that no longer exists.
		WorkspaceDocumentManagerDeleteResult deleted = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(renamed.Snapshot!.DocumentKey, renamed.Snapshot.DocumentId, renamed.Snapshot.Version),
			renamed.Snapshot.OnDiskStamp));

		Assert.IsNotNull(deleted.StoreResult);
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, deleted.Outcome);
		Assert.IsFalse(File.Exists(destinationPath));
	}

	[TestMethod]
	public async Task Reload_RetriesTheRefreshOfAnUnsynchronizedView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		await fixture.MarkViewUnsynchronizedAsync(snapshot);
		File.WriteAllText(fixture.DocumentPath, "changed externally");

		WorkspaceDocumentManagerReloadResult result = await fixture.Manager.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)));

		// A view whose only outstanding state is a prior synchronization failure does not block the
		// reload; the refresh retries the failed synchronization and clears it.
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, result.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.ViewSynchronization.Outcome);
		Assert.AreEqual("changed externally", fixture.View.Text);
	}

	[TestMethod]
	public async Task Reload_BlockedByConflictLeavesTheDocumentUnmodified()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		File.WriteAllText(fixture.DocumentPath, "changed externally");
		fixture.View.HasConflict = true;

		WorkspaceDocumentManagerReloadResult result = await fixture.Manager.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)));

		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, result.ViewSynchronization.Outcome);
		CollectionAssert.Contains(ViewIdsOf(result.ViewSynchronization), fixture.View.ViewId);
		Assert.AreEqual("initial", fixture.View.Text);
	}

	[TestMethod]
	public async Task ResolveConflict_RetriesTheRefreshOfAnUnsynchronizedView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		await fixture.MarkViewUnsynchronizedAsync(snapshot);

		// Dirty the document and change the file externally to create a conflict.
		WorkspaceDocumentMutationResult edited = fixture.Store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"edited",
			snapshot.FileFormat));
		WorkspaceDocumentSnapshot dirty = edited.Snapshot!;
		File.WriteAllText(fixture.DocumentPath, "external");

		WorkspaceDocumentManagerReloadResult conflict = await fixture.Manager.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(dirty.DocumentKey, dirty.DocumentId, dirty.Version)));
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ExternalFileConflict, conflict.Outcome);

		WorkspaceDocumentManagerConflictResolutionResult result = await fixture.Manager.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(dirty.DocumentKey, dirty.DocumentId, dirty.Version),
				conflict.StoreResult!.ObservedOnDiskStamp!.Value,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		// The prior synchronization failure does not block the resolution; the post-resolution refresh
		// retries it and clears it.
		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ResolvedWithDisk, result.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, result.ViewSynchronization.Outcome);
		Assert.AreEqual("external", fixture.View.Text);
	}

	[TestMethod]
	public async Task Discard_AcknowledgeFailureReportsViewAcknowledgeFailed()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		WorkspaceDocumentMutationResult edited = fixture.Store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"edited",
			snapshot.FileFormat));
		fixture.View.ThrowOnAcknowledgeApply = true;

		WorkspaceDocumentManagerMutationResult discarded = await fixture.Manager.DiscardAsync(new WorkspaceDocumentDiscardRequest(
			new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version)));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, discarded.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, discarded.ViewSynchronization.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewAcknowledgeFailed, discarded.ViewSynchronization.Issues.Single().Failure!.Code);
	}

	[TestMethod]
	public async Task Rename_ThrowingIdentityAcknowledgmentReportsViewIdentityUpdateFailed()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.ThrowOnIdentityAck = true;
		string destinationPath = Path.Combine(fixture.DirectoryPath, "renamed.txt");

		WorkspaceDocumentManagerRenameResult renamed = await fixture.Manager.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, renamed.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, renamed.ViewSynchronization.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewIdentityUpdateFailed, renamed.ViewSynchronization.Issues.Single().Failure!.Code);
		Assert.AreEqual(snapshot.DocumentId, fixture.View.DocumentId, "A failed acknowledgment keeps the prior binding.");
	}

	[TestMethod]
	public async Task ApplyRequested_ApplyPathFailureIsRecordedAndBlocksLaterOperations()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// The store rejects the published replacement (a null content is an argument error); the event
		// has no caller to fault, so the failure is recorded with its detail and blocks the next
		// destructive operation.
		fixture.View.RaiseApply(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			null!,
			snapshot.FileFormat));

		WorkspaceDocumentManagerDeleteResult blocked = await fixture.Manager.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));

		Assert.IsNull(blocked.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, blocked.ViewSynchronization.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewApplyFailed, blocked.ViewSynchronization.Issues.Single().Failure!.Code);
		Assert.IsTrue(File.Exists(fixture.DocumentPath));
	}

	[TestMethod]
	public async Task Rename_RekeysEveryAttachedPeerView()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		var secondView = new TestView(fixture, "second-view");
		WorkspaceDocumentManagerOpenResult opened = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			secondView);
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.Opened, opened.Outcome);
		string destinationPath = Path.Combine(fixture.DirectoryPath, "renamed.txt");

		WorkspaceDocumentManagerRenameResult renamed = await fixture.Manager.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, renamed.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, renamed.ViewSynchronization.Outcome);
		Assert.AreEqual(renamed.Snapshot!.DocumentId, fixture.View.DocumentId);
		Assert.AreEqual(renamed.Snapshot.DocumentId, secondView.DocumentId);

		// Both views are tracked under the destination identity, so a later mutation reaches each.
		int firstRefreshBefore = fixture.View.RefreshCount;
		int secondRefreshBefore = secondView.RefreshCount;
		WorkspaceDocumentManagerMutationResult replaced = await fixture.Manager.ReplaceAsync(new WorkspaceDocumentReplaceRequest(
			new(renamed.Snapshot.DocumentKey, renamed.Snapshot.DocumentId, renamed.Snapshot.Version),
			"after rename",
			renamed.Snapshot.FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.Outcome);
		Assert.AreEqual(firstRefreshBefore + 1, fixture.View.RefreshCount);
		Assert.AreEqual(secondRefreshBefore + 1, secondView.RefreshCount);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Rename_LateViewAtVacatedPathIsNotRekeyed()
	{
		var dispatch = new GatedDispatch();
		await using var fixture = new ManagerFixture(customDispatch: dispatch.Dispatch);
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		var lateView = new TestView(fixture, "late-view");
		string destinationPath = Path.Combine(fixture.DirectoryPath, "renamed.txt");

		// Hold the rename's identity acknowledgment so the vacated path can be reopened after the store
		// has completed the rename but before the acknowledgment runs.
		dispatch.HoldNext();
		Task<WorkspaceDocumentManagerRenameResult> rename = fixture.Manager.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp,
			destinationPath));
		await dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));

		// A new file appears at the vacated path and a view attaches to the new document: same id
		// string, a different document key.
		File.WriteAllText(fixture.DocumentPath, "replacement");
		WorkspaceDocumentManagerOpenResult lateOpen = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			lateView);
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.Opened, lateOpen.Outcome);
		Assert.AreEqual(snapshot.DocumentId, lateOpen.Snapshot!.DocumentId, "The new document reuses the vacated id string.");
		Assert.AreNotEqual(snapshot.DocumentKey, lateOpen.Snapshot.DocumentKey, "The new document is a different instance.");

		dispatch.Release();
		WorkspaceDocumentManagerRenameResult renamed = await rename;

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, renamed.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, renamed.ViewSynchronization.Outcome);

		// The attached peer is rekeyed to the destination; the view registered at the vacated path keeps
		// its own document identity instead of being rekeyed with the renamed document's snapshot.
		Assert.AreEqual(renamed.Snapshot!.DocumentId, fixture.View.DocumentId);
		Assert.AreEqual(lateOpen.Snapshot.DocumentId, lateView.DocumentId);
		Assert.AreEqual(lateOpen.Snapshot.DocumentKey, lateView.DocumentKey);
	}

	// Runs dispatched actions inline, holding exactly one arm()ed action until it is released, so a
	// test can interleave another manager operation before the held action runs.
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
