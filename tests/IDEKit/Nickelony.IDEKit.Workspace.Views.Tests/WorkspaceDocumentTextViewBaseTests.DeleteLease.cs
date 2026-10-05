using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceDocumentTextViewBaseTests
{
	[TestMethod]
	public void DeleteLease_Nested_SetsReadOnlyOnTheFirstEntry_AndRestoresItOnceOnTheLastRelease()
	{
		var view = new TestTextView();
		view.Open(Snapshot("print(1)"));

		Assert.AreEqual(WorkspaceDocumentViewDeleteLeaseOutcome.Entered, view.EnterDeleteLease().Outcome);
		Assert.AreEqual(WorkspaceDocumentViewDeleteLeaseOutcome.Entered, view.EnterDeleteLease().Outcome);
		Assert.IsTrue(view.ReadOnly);

		Assert.AreEqual(WorkspaceDocumentViewDeleteLeaseOutcome.Released, view.ReleaseDeleteLease().Outcome);
		Assert.IsTrue(view.ReadOnly);

		Assert.AreEqual(WorkspaceDocumentViewDeleteLeaseOutcome.Released, view.ReleaseDeleteLease().Outcome);
		Assert.IsFalse(view.ReadOnly);
		Assert.AreSequenceEqual([true, false], view.ReadOnlyWrites);
	}

	[TestMethod]
	public void DeleteLease_ReleasedWithNoneHeld_ReportsReleasedAndLeavesTheEditorAlone()
	{
		var view = new TestTextView();
		view.Open(Snapshot("print(1)"));

		Assert.AreEqual(WorkspaceDocumentViewDeleteLeaseOutcome.Released, view.ReleaseDeleteLease().Outcome);
		Assert.IsEmpty(view.ReadOnlyWrites);

		view.EnterDeleteLease();
		view.ReleaseDeleteLease();

		Assert.AreEqual(WorkspaceDocumentViewDeleteLeaseOutcome.Released, view.ReleaseDeleteLease().Outcome);
		Assert.AreSequenceEqual([true, false], view.ReadOnlyWrites);
	}

	[TestMethod]
	public void DeleteLease_OfAClosedView_FailsAndLeavesTheEditorAlone()
	{
		var view = new TestTextView();
		view.Open(Snapshot("print(1)"));
		view.Close();

		WorkspaceDocumentViewDeleteLeaseResult entered = view.EnterDeleteLease();

		Assert.AreEqual(WorkspaceDocumentViewDeleteLeaseOutcome.Failed, entered.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.DeleteLeaseFailed, entered.Failure?.Code);
		Assert.IsEmpty(view.ReadOnlyWrites);
	}

	[TestMethod]
	public void DeleteLease_EnteredBeforeAClose_IsReleasedAfterIt()
	{
		var view = new TestTextView();
		view.Open(Snapshot("print(1)"));
		view.EnterDeleteLease();
		view.Close();

		Assert.AreEqual(WorkspaceDocumentViewDeleteLeaseOutcome.Released, view.ReleaseDeleteLease().Outcome);
		Assert.IsFalse(view.ReadOnly);
	}

	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public async Task Delete_LeavesNoActionBetweenTheCheckAndTheLease_SoTypingBetweenActionsIsNeverLost(bool deletesTheDirectory)
	{
		using var directory = new TemporaryDirectory();
		string folder = Path.Combine(directory.Path, "folder");
		Directory.CreateDirectory(folder);
		string path = Path.Combine(folder, "document.txt");
		File.WriteAllText(path, "initial");

		TestTextView? typist = null;
		int keystrokes = 0;

		// The dispatcher runs a keystroke between every two of its actions while the view is open, as a message loop does, and a read-only editor refuses it.
		var dispatcher = new ScriptedUiDispatcher(action =>
		{
			action();

			if (typist is { DocumentId: not null } && typist.TypeUnlessReadOnly($"unsaved {keystrokes}"))
				keystrokes++;

			return Task.CompletedTask;
		});

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());
		await using var coordinator = new WorkspaceViewCoordinator(store, dispatcher);
		var view = new TestTextView();

		WorkspaceViewCoordinatorOpenResult opened = await coordinator.OpenWithViewAsync(
			filePath: path,
			options: s_openOptions,
			view,
			cancellationToken: TestContext.CancellationToken
		);

		WorkspaceDocumentSnapshot snapshot = opened.Snapshot!;

		typist = view;

		bool deleted = deletesTheDirectory
			? (await coordinator.DeleteDirectoryAsync(new WorkspaceDocumentDirectoryDeleteRequest(folder), TestContext.CancellationToken))
				.StoreResult
				?.Outcome == WorkspaceDocumentDirectoryDeleteOutcome.Deleted
			: (
				await coordinator.DeleteAsync(
					new WorkspaceDocumentDeleteRequest(snapshot.Identity, snapshot.OnDiskStamp),
					TestContext.CancellationToken
				)
			)
				.StoreResult
				?.Outcome == WorkspaceDocumentDeleteOutcome.Deleted;

		typist = null;

		// The first action of the delete enters the lease and checks the view together, so every keystroke after it is refused.
		Assert.IsTrue(deleted);
		Assert.AreEqual(0, keystrokes, "A keystroke landed after the check and before the lease, and the delete lost it.");
	}

	[TestMethod]
	public async Task Delete_OfADocumentWhoseViewTheHostDetachedAndClosedMeanwhile_LeasesAndChecksOnlyTheViewsStillAttached()
	{
		using var directory = new TemporaryDirectory();
		string path = Path.Combine(directory.Path, "document.txt");
		File.WriteAllText(path, "initial");
		Action? beforeNextAction = null;

		var dispatcher = new ScriptedUiDispatcher(action =>
		{
			Action? hostWork = beforeNextAction;
			beforeNextAction = null;
			hostWork?.Invoke();
			action();
			return Task.CompletedTask;
		});

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());
		await using var coordinator = new WorkspaceViewCoordinator(store, dispatcher);
		var kept = new TestTextView("kept");
		var closed = new TestTextView("closed");

		WorkspaceDocumentSnapshot snapshot = (
			await coordinator.OpenWithViewAsync(filePath: path, options: s_openOptions, view: kept, cancellationToken: TestContext.CancellationToken)
		).Snapshot!;

		await coordinator.OpenWithViewAsync(filePath: path, options: s_openOptions, view: closed, cancellationToken: TestContext.CancellationToken);

		// The host closes a tab after the delete captured the views and before the action that leases them.
		beforeNextAction = () =>
		{
			coordinator.DetachView(closed);
			closed.Close();
		};

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> deleted = await coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(snapshot.Identity, snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, deleted.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, deleted.ViewSynchronization.Outcome);
		Assert.IsFalse(File.Exists(path));
		Assert.IsEmpty(closed.ReadOnlyWrites);
		Assert.AreSequenceEqual([true, false], kept.ReadOnlyWrites);
	}

	[TestMethod]
	public async Task Delete_BlockedByAViewWithPendingEdits_ReleasesTheLeasesItEntered()
	{
		await using var host = new CoordinatorTestHost();
		var editing = new TestTextView("editing");
		var clean = new TestTextView("clean");

		await host.Coordinator.OpenWithViewAsync(
			filePath: host.DocumentPath,
			options: s_openOptions,
			view: editing,
			cancellationToken: TestContext.CancellationToken
		);

		await host.Coordinator.OpenWithViewAsync(
			filePath: host.DocumentPath,
			options: s_openOptions,
			view: clean,
			cancellationToken: TestContext.CancellationToken
		);

		editing.Type("unsaved");
		Assert.IsTrue(host.Store.TryGetSnapshot(host.DocumentPath, out WorkspaceDocumentSnapshot? snapshot));

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> deleted = await host.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(snapshot.Identity, snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, deleted.ViewSynchronization.Outcome);
		Assert.AreEqual("editing", deleted.ViewSynchronization.Issues.Single().ViewId);
		Assert.IsTrue(File.Exists(host.DocumentPath));

		// The check runs once the leases are entered, so the blocked delete rolls them back.
		Assert.AreSequenceEqual([true, false], clean.ReadOnlyWrites);
		Assert.IsFalse(clean.ReadOnly);
		Assert.IsFalse(editing.ReadOnly);
	}

	[TestMethod]
	public void DeleteLease_OfAnEditorThatWasAlreadyReadOnly_LeavesItReadOnlyAfterTheRelease()
	{
		var view = new TestTextView();
		view.Open(Snapshot("print(1)"));
		view.MakeReadOnly();

		view.EnterDeleteLease();
		view.ReleaseDeleteLease();

		Assert.IsTrue(view.ReadOnly);
	}
}
