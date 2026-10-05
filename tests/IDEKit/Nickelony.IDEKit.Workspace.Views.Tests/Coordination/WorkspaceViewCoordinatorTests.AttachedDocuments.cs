using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceViewCoordinatorTests
{
	[TestMethod]
	public async Task AttachedDocumentChanged_TwoViews_OpenTheDocumentOnceAndCloseItAfterTheLastDetach()
	{
		await using var fixture = new CoordinatorTestHost();
		List<string> changes = RecordChanges(fixture.Coordinator);
		var secondView = new FakeCoordinatorView(fixture, "second-view");

		await fixture.OpenViewAsync();
		await fixture.Coordinator.OpenWithViewAsync(fixture.DocumentPath, s_openOptions, secondView);

		Assert.AreSequenceEqual(["Opened document.txt"], changes);
		Assert.HasCount(1, fixture.Coordinator.AttachedDocuments);
		Assert.AreEqual(fixture.DocumentPath, fixture.Coordinator.AttachedDocuments[0].DocumentId, ignoreCase: true);

		fixture.Coordinator.DetachView(fixture.View);
		Assert.AreSequenceEqual(["Opened document.txt"], changes);

		fixture.Coordinator.DetachView(secondView);

		Assert.AreSequenceEqual(["Opened document.txt", "Closed document.txt"], changes);
		Assert.IsEmpty(fixture.Coordinator.AttachedDocuments);
	}

	[TestMethod]
	public async Task AttachedDocumentChanged_RaisesTheChangesInOperationOrder()
	{
		await using var fixture = new CoordinatorTestHost();

		List<string> changes = RecordChanges(fixture.Coordinator);
		string renamedPath = Path.Combine(fixture.DirectoryPath, "renamed.txt");

		WorkspaceDocumentSnapshot opened = await fixture.OpenViewAsync();

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await fixture.Coordinator.ReplaceAsync(
			new WorkspaceDocumentReplaceRequest(opened.Identity, "edited", opened.FileFormat)
		);

		WorkspaceDocumentSnapshot edited = replaced.StoreResult.Snapshot!;

		WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentRenameResult> renamed = await fixture.Coordinator.RenameAsync(
			new WorkspaceDocumentRenameRequest(edited.Identity, edited.OnDiskStamp, renamedPath)
		);

		WorkspaceDocumentSnapshot moved = renamed.StoreResult.Snapshot!;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> deleted = await fixture.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(moved.Identity, moved.OnDiskStamp)
		);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, deleted.StoreResult?.Outcome);

		Assert.AreSequenceEqual(
			["Opened document.txt", "ContentChanged document.txt: edited", "Renamed renamed.txt from document.txt: edited", "Closed renamed.txt"],
			changes
		);
	}

	[TestMethod]
	public async Task AttachedDocumentChanged_AReplacementWithTheSameTextOrACommit_RaisesNothing()
	{
		await using var fixture = new CoordinatorTestHost();

		WorkspaceDocumentSnapshot opened = await fixture.OpenViewAsync();
		List<string> changes = RecordChanges(fixture.Coordinator);

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await fixture.Coordinator.ReplaceAsync(
			new WorkspaceDocumentReplaceRequest(opened.Identity, "initial", opened.FileFormat)
		);

		WorkspaceDocumentSnapshot current = replaced.StoreResult.Snapshot!;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult> committed = await fixture.Coordinator.CommitAsync(
			new WorkspaceDocumentCommitRequest(current.Identity, current.OnDiskStamp)
		);

		Assert.IsNotNull(committed.StoreResult);
		Assert.IsEmpty(changes);
	}

	[TestMethod]
	public async Task AttachedDocumentChanged_ADirectoryRename_MovesTheAttachedDocument()
	{
		await using var fixture = new CoordinatorTestHost();
		using var destination = new TemporaryDirectory();

		string destinationPath = Path.Combine(destination.Path, "moved");

		await fixture.OpenViewAsync();
		List<AttachedDocumentChangedEventArgs> changes = [];
		fixture.Coordinator.AttachedDocumentChanged += (_, change) => changes.Add(change);

		WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult> renamed = await fixture.Coordinator.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(fixture.DirectoryPath, destinationPath)
		);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, renamed.StoreResult?.Outcome);
		Assert.HasCount(1, changes);
		Assert.AreEqual(AttachedDocumentChangeKind.Renamed, changes[0].Kind);
		Assert.AreEqual(fixture.DocumentPath, changes[0].OldDocumentId, ignoreCase: true);
		Assert.AreEqual(Path.Combine(destinationPath, "document.txt"), changes[0].Snapshot.DocumentId, ignoreCase: true);
	}

	[TestMethod]
	public async Task AttachedDocumentChanged_AStop_ClosesEveryAttachedDocument()
	{
		await using var fixture = new CoordinatorTestHost();
		List<string> changes = RecordChanges(fixture.Coordinator);

		await fixture.OpenViewAsync();
		await fixture.Coordinator.StopAsync();

		Assert.AreSequenceEqual(["Opened document.txt", "Closed document.txt"], changes);
		Assert.IsEmpty(fixture.Coordinator.AttachedDocuments);
	}

	[TestMethod]
	public async Task AttachedDocumentChanged_AChangeAHandlerCauses_IsRaisedAfterTheHandlerReturns()
	{
		await using var fixture = new CoordinatorTestHost();
		List<string> changes = [];

		fixture.Coordinator.AttachedDocumentChanged += (_, change) =>
		{
			changes.Add($"enter {change.Kind}");

			// The detach closes the document, and the coordinator raises that close only after this handler returns.
			if (change.Kind == AttachedDocumentChangeKind.Opened)
				fixture.Coordinator.DetachView(fixture.View);

			changes.Add($"exit {change.Kind}");
		};

		await fixture.OpenViewAsync();

		Assert.AreSequenceEqual(["enter Opened", "exit Opened", "enter Closed", "exit Closed"], changes);
	}

	[TestMethod]
	public async Task AttachedDocumentChanged_IsRaisedOnTheDispatcherBeforeTheOperationCompletes()
	{
		await using var fixture = new CoordinatorTestHost(pumpDispatch: true);
		int? raisedThreadId = null;

		fixture.Coordinator.AttachedDocumentChanged += (_, _) => raisedThreadId = Environment.CurrentManagedThreadId;

		await fixture.OpenViewAsync();

		Assert.IsNotNull(raisedThreadId, "The open is raised before the attach completes.");
		Assert.AreEqual(fixture.PumpThreadId, raisedThreadId);
	}

	[TestMethod]
	public async Task AttachedDocumentChanged_AnOpenWhoseHandlerThrows_StillReportsOpenedAndPostsTheFault()
	{
		await using var fixture = new CoordinatorTestHost();
		var fault = new InvalidOperationException("The handler failed.");

		fixture.Coordinator.AttachedDocumentChanged += (_, _) => throw fault;
		List<string> changes = RecordChanges(fixture.Coordinator);

		WorkspaceViewCoordinatorOpenResult opened = await fixture.Coordinator.OpenWithViewAsync(fixture.DocumentPath, s_openOptions, fixture.View);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Opened, opened.Outcome);
		Assert.AreSequenceEqual(["Opened document.txt"], changes);

		// The fault reaches the dispatcher out of band, where the posted action rethrows it for the toolkit.
		Assert.HasCount(1, fixture.Posted);
		Assert.AreSame(fault, Assert.ThrowsExactly<InvalidOperationException>(fixture.Posted[0]));
	}

	[TestMethod]
	public async Task AttachedDocumentChanged_AHandlerThatThrowsOnAPublication_LeavesTheViewSynchronized()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot opened = await fixture.OpenViewAsync();

		fixture.Coordinator.AttachedDocumentChanged += (_, _) => throw new InvalidOperationException("The handler failed.");
		fixture.View.RaiseApply(new WorkspaceDocumentReplaceRequest(opened.Identity, "typed", opened.FileFormat));

		await TestPolling.UntilAsync(
			() => fixture.Store.TryGetSnapshot(fixture.DocumentPath, out WorkspaceDocumentSnapshot? current) && current.Content == "typed",
			TimeSpan.FromSeconds(10)
		);

		WorkspaceViewCoordinatorPublicationResult published = await fixture.Coordinator.PublishPendingEditsAsync(fixture.DocumentPath);

		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, published.ViewSynchronization.Outcome);
	}

	[TestMethod]
	public async Task AttachedDocumentChanged_AHandlerThatThrowsOnADetach_CostsTheNextHandlerNoChange()
	{
		await using var fixture = new CoordinatorTestHost();
		await fixture.OpenViewAsync();

		fixture.Coordinator.AttachedDocumentChanged += (_, _) => throw new InvalidOperationException("The handler failed.");
		List<string> changes = RecordChanges(fixture.Coordinator);

		fixture.Coordinator.DetachView(fixture.View);

		Assert.AreSequenceEqual(["Closed document.txt"], changes);
	}

	[TestMethod]
	public async Task AttachedDocumentChanged_AThrowDuringTheStop_CompletesTheStopAndRaisesEveryClose()
	{
		await using var fixture = new CoordinatorTestHost();
		string secondPath = Path.Combine(fixture.DirectoryPath, "second.txt");
		File.WriteAllText(secondPath, "second");

		await fixture.OpenViewAsync();
		await fixture.Coordinator.OpenWithViewAsync(secondPath, s_openOptions, new FakeCoordinatorView(fixture, "second-view"));

		int closes = 0;

		fixture.Coordinator.AttachedDocumentChanged += (_, change) =>
		{
			if (change.Kind == AttachedDocumentChangeKind.Closed && closes++ == 0)
				throw new InvalidOperationException("The close failed.");
		};

		List<string> changes = RecordChanges(fixture.Coordinator);

		await fixture.Coordinator.StopAsync();
		await fixture.Coordinator.DisposeAsync();

		Assert.AreSequenceEqual(["Closed document.txt", "Closed second.txt"], changes.Order(StringComparer.Ordinal));
	}

	[TestMethod]
	public async Task AttachedDocumentChanged_ASaveAs_MovesTheDocument()
	{
		await using var fixture = new CoordinatorTestHost();

		WorkspaceDocumentSnapshot opened = await fixture.OpenViewAsync();
		List<string> changes = RecordChanges(fixture.Coordinator);

		WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentSaveAsResult> saved = await fixture.Coordinator.SaveAsAsync(
			new WorkspaceDocumentSaveAsRequest(opened.Identity, opened.OnDiskStamp, Path.Combine(fixture.DirectoryPath, "copy.txt"))
		);

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, saved.StoreResult.Outcome);
		Assert.AreSequenceEqual(["Renamed copy.txt from document.txt: initial"], changes);
	}

	[TestMethod]
	public async Task AttachedDocumentChanged_ADiscard_ReportsTheRestoredText()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot opened = await fixture.OpenViewAsync();

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await fixture.Coordinator.ReplaceAsync(
			new WorkspaceDocumentReplaceRequest(opened.Identity, "edited", opened.FileFormat)
		);

		List<string> changes = RecordChanges(fixture.Coordinator);

		WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentMutationResult> discarded = await fixture.Coordinator.DiscardAsync(
			new WorkspaceDocumentDiscardRequest(replaced.StoreResult.Snapshot!.Identity)
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, discarded.StoreResult.Outcome);
		Assert.AreSequenceEqual(["ContentChanged document.txt: initial"], changes);
	}

	[TestMethod]
	public async Task AttachedDocumentChanged_ADirectoryDelete_ClosesTheDocument()
	{
		await using var fixture = new CoordinatorTestHost();
		await fixture.OpenViewAsync();
		List<string> changes = RecordChanges(fixture.Coordinator);

		WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryDeleteResult> deleted = await fixture.Coordinator.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(fixture.DirectoryPath)
		);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Deleted, deleted.StoreResult?.Outcome);
		Assert.AreSequenceEqual(["Closed document.txt"], changes);
	}

	[TestMethod]
	public void AttachedDocumentChangedEventArgs_NamesTheOldIdForARenameOnly()
	{
		WorkspaceDocumentSnapshot snapshot = TestSnapshots.Create("doc", "text");

		Assert.ThrowsExactly<ArgumentException>(() =>
			new AttachedDocumentChangedEventArgs(AttachedDocumentChangeKind.Renamed, snapshot, oldDocumentId: null)
		);

		Assert.ThrowsExactly<ArgumentException>(() => new AttachedDocumentChangedEventArgs(AttachedDocumentChangeKind.Opened, snapshot, "old"));

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new AttachedDocumentChangedEventArgs(AttachedDocumentChangeKind.Closed, null!, oldDocumentId: null)
		);
	}

	// Records each change as its kind and file name, with the old file name of a rename and the text of a change that carries new text.
	private static List<string> RecordChanges(IWorkspaceViewCoordinator coordinator)
	{
		List<string> changes = [];

		coordinator.AttachedDocumentChanged += (_, change) =>
		{
			string name = Path.GetFileName(change.Snapshot.DocumentId);

			changes.Add(
				change.Kind switch
				{
					AttachedDocumentChangeKind.ContentChanged => $"{change.Kind} {name}: {change.Snapshot.Content}",

					AttachedDocumentChangeKind.Renamed =>
						$"{change.Kind} {name} from {Path.GetFileName(change.OldDocumentId)}: {change.Snapshot.Content}",

					_ => $"{change.Kind} {name}",
				}
			);
		};

		return changes;
	}
}
