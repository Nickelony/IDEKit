using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceDocumentTextViewBaseTests
{
	[TestMethod]
	public void Open_WhenTheEditorCannotReplaceItsText_IsUnavailableWithTheException_AndLeavesTheViewDetached()
	{
		var view = new TestTextView { ThrowOnReplace = true };

		WorkspaceDocumentViewOpenResult opened = view.Open(Snapshot("print(1)"));

		Assert.AreEqual(WorkspaceDocumentViewOpenOutcome.Unavailable, opened.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.OpenFailed, opened.Failure?.Code);
		Assert.IsInstanceOfType<InvalidOperationException>(opened.Failure?.Exception);
		Assert.IsNull(view.DocumentKey);
	}

	[TestMethod]
	public void Refresh_WhenTheEditorCannotReplaceItsText_FailsWithTheException_AndKeepsTheSnapshotItHad()
	{
		var view = new TestTextView();
		WorkspaceDocumentSnapshot opened = Snapshot("print(1)");
		view.Open(opened);
		view.ThrowOnReplace = true;

		WorkspaceDocumentViewRefreshResult refreshed = view.Refresh(Snapshot("print(2)", version: 1, key: opened.DocumentKey));

		Assert.AreEqual(WorkspaceDocumentViewRefreshOutcome.Failed, refreshed.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewRefreshFailed, refreshed.Failure?.Code);
		Assert.IsInstanceOfType<InvalidOperationException>(refreshed.Failure?.Exception);
		Assert.AreEqual("print(1)", view.SynchronizedText);
		Assert.IsFalse(view.HasPendingEdits);
	}

	[TestMethod]
	public void Refresh_WhenTheReplacementThrowsAfterTheEditorShowsTheNewText_FailsButRecordsTheSnapshot()
	{
		var view = new TestTextView();
		WorkspaceDocumentSnapshot opened = Snapshot("print(1)");
		view.Open(opened);
		view.ThrowAfterReplace = true;

		WorkspaceDocumentViewRefreshResult refreshed = view.Refresh(Snapshot("print(2)", version: 1, key: opened.DocumentKey));

		Assert.AreEqual(WorkspaceDocumentViewRefreshOutcome.Failed, refreshed.Outcome);
		Assert.IsInstanceOfType<InvalidOperationException>(refreshed.Failure?.Exception);
		Assert.AreEqual("print(2)", view.SynchronizedText);
		Assert.IsFalse(view.HasPendingEdits);
	}

	[TestMethod]
	public void AcknowledgeIdentity_WhenTheEditorCannotReplaceItsText_FailsWithTheException_AndKeepsTheOldIdentity()
	{
		var view = new TestTextView();
		WorkspaceDocumentSnapshot opened = Snapshot("print(1)");
		view.Open(opened);
		view.ThrowOnReplace = true;

		WorkspaceDocumentSnapshot renamed = TestSnapshots.Create(
			documentId: "renamed.lua",
			content: "print(2)",
			version: 1,
			documentKey: opened.DocumentKey
		);

		WorkspaceDocumentViewIdentityResult acknowledged = view.AcknowledgeIdentity(
			new WorkspaceDocumentViewIdentityChange(opened.DocumentKey, opened.DocumentId, renamed)
		);

		Assert.AreEqual(WorkspaceDocumentViewIdentityOutcome.Failed, acknowledged.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewIdentityUpdateFailed, acknowledged.Failure?.Code);
		Assert.IsInstanceOfType<InvalidOperationException>(acknowledged.Failure?.Exception);
		Assert.AreEqual(DocumentId, view.DocumentId);
	}

	[TestMethod]
	public void AcknowledgeApply_WhenTheEditorCannotReplaceItsText_FailsWithTheException()
	{
		var view = new TestTextView();
		WorkspaceDocumentSnapshot opened = Snapshot("print(1)");
		view.Open(opened);
		view.ThrowOnReplace = true;

		WorkspaceDocumentSnapshot discarded = Snapshot("print(0)", version: 1, key: opened.DocumentKey);
		WorkspaceDocumentViewApplyResult acknowledged = view.AcknowledgeApply(Mutation(WorkspaceDocumentMutationOutcome.Changed, opened, discarded));

		Assert.AreEqual(WorkspaceDocumentViewApplyOutcome.Failed, acknowledged.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewAcknowledgeFailed, acknowledged.Failure?.Code);
		Assert.IsInstanceOfType<InvalidOperationException>(acknowledged.Failure?.Exception);
		Assert.AreEqual("print(1)", view.SynchronizedText);
	}

	[TestMethod]
	public void DeleteLease_WhenTheEditorCannotChangeItsReadOnlyState_FailsWithTheException_AndHoldsNoEntry()
	{
		var view = new TestTextView { ThrowOnReadOnly = true };
		view.Open(Snapshot("print(1)"));

		WorkspaceDocumentViewDeleteLeaseResult entered = view.EnterDeleteLease();

		Assert.AreEqual(WorkspaceDocumentViewDeleteLeaseOutcome.Failed, entered.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.DeleteLeaseFailed, entered.Failure?.Code);
		Assert.IsInstanceOfType<InvalidOperationException>(entered.Failure?.Exception);

		// No entry is held, so a release does not touch the editor.
		Assert.AreEqual(WorkspaceDocumentViewDeleteLeaseOutcome.Released, view.ReleaseDeleteLease().Outcome);
	}

	[TestMethod]
	public async Task DeleteLease_WhoseLastReleaseThrows_CountsDown_SoTheNextMoveRestoresTheEditor()
	{
		await using var host = new CoordinatorTestHost();
		var view = new TestTextView { ThrowOnReadOnlyWrite = 2 };

		await host.Coordinator.OpenWithViewAsync(host.DocumentPath, s_openOptions, view);

		using var destination = new TemporaryDirectory();

		string first = Path.Combine(destination.Path, "first");
		string second = Path.Combine(destination.Path, "second");

		WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult> moved = await host.Coordinator.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(host.DirectoryPath, first),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, moved.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.DeleteLeaseFailed, moved.ViewSynchronization.Issues.Single().Failure?.Code);
		Assert.IsTrue(view.ReadOnly);

		// The coordinator never retries a release, so the next lease of the view retries the restore.
		moved = await host.Coordinator.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(first, second),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, moved.StoreResult?.Outcome);
		Assert.IsFalse(view.ReadOnly);
		Assert.AreSequenceEqual([true, false], view.ReadOnlyWrites);
	}

	[TestMethod]
	public void Close_RetriesTheRestoreThatAFailedLeaseReleaseLeftPending()
	{
		var view = new TestTextView { ThrowOnReadOnlyWrite = 2 };
		view.Open(Snapshot("print(1)"));
		view.EnterDeleteLease();

		WorkspaceDocumentViewDeleteLeaseResult released = view.ReleaseDeleteLease();

		Assert.AreEqual(WorkspaceDocumentViewDeleteLeaseOutcome.Failed, released.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.DeleteLeaseFailed, released.Failure?.Code);
		Assert.IsInstanceOfType<InvalidOperationException>(released.Failure?.Exception);
		Assert.IsTrue(view.ReadOnly);

		view.Close();

		Assert.IsFalse(view.ReadOnly);
		Assert.AreSequenceEqual([true, false], view.ReadOnlyWrites);
	}

	[TestMethod]
	public async Task PublishPendingEditsAsync_AfterAnApplyThatThrew_PublishesTheTextAgain()
	{
		await using var host = new CoordinatorTestHost();
		var view = new TestTextView();

		_ = await host.Coordinator.OpenWithViewAsync(host.DocumentPath, s_openOptions, view);
		List<WorkspaceDocumentReplaceRequest> published = CapturePublications(view);

		// A disposed store makes the apply path throw, so no acknowledgment reaches the view.
		await host.Store.DisposeAsync();
		view.Type("edited");

		_ = await host.Coordinator.PublishPendingEditsAsync(DocumentIdOf(view), TestContext.CancellationToken);
		_ = await host.Coordinator.PublishPendingEditsAsync(DocumentIdOf(view), TestContext.CancellationToken);

		Assert.HasCount(2, published, "The publication whose apply threw does not hold the next one back.");
		Assert.IsTrue(view.HasPendingEdits);
	}

	[TestMethod]
	public async Task PublishPendingEditsAsync_WhenTheDispatcherRejectsTheEndOfALostPublication_ReportsTheViewInsteadOfThrowing()
	{
		using var directory = new TemporaryDirectory();
		string path = Path.Combine(directory.Path, "document.txt");
		File.WriteAllText(path, "initial");

		int depth = 0;
		bool rejectsNestedActions = false;

		// An action that a raise dispatches from inside another action faults once the raise returned, as a dispatcher that rejected it reports.
		var dispatcher = new ScriptedUiDispatcher(action =>
		{
			if (rejectsNestedActions && depth > 0)
				return RejectLaterAsync();

			depth++;

			try
			{
				action();
			}
			finally
			{
				depth--;
			}

			return Task.CompletedTask;
		});

		var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());
		await using var coordinator = new WorkspaceViewCoordinator(store, dispatcher);
		var view = new TestTextView();

		_ = await coordinator.OpenWithViewAsync(filePath: path, options: s_openOptions, view, cancellationToken: TestContext.CancellationToken);

		// A disposed store makes the apply throw, so the coordinator ends the lost publication through the dispatcher.
		await store.DisposeAsync();
		view.Type("edited");
		rejectsNestedActions = true;

		WorkspaceViewCoordinatorPublicationResult published = await coordinator.PublishPendingEditsAsync(path, TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, published.ViewSynchronization.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewApplyFailed, published.ViewSynchronization.Issues.Single().Failure?.Code);

		static async Task RejectLaterAsync()
		{
			await Task.Yield();
			throw new InvalidOperationException("The dispatcher rejected the action.");
		}
	}

	private static string DocumentIdOf(TestTextView view) => view.DocumentId ?? throw new AssertFailedException("The view has no open document.");
}
