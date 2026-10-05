using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceDocumentTextViewBaseTests
{
	private const string ThreeLines = "one\ntwo\nthree\n";

	[TestMethod]
	[DataRow("one\ntwo\nTHREE!\n", "first\ntwo\nthree\n", "first\ntwo\nTHREE!\n")]
	[DataRow("ONE\ntwo\nthree\n", "one\ntwo\nthe third\n", "ONE\ntwo\nthe third\n")]
	public void AcknowledgeApply_OfAPublicationThatAChangeOfAnotherLineOvertook_ShowsBothChangesAndPublishesThem(
		string typed,
		string other,
		string merged
	)
	{
		var view = new TestTextView();
		WorkspaceDocumentSnapshot opened = Snapshot(ThreeLines);
		view.Open(opened);
		List<WorkspaceDocumentReplaceRequest> published = CapturePublications(view);
		view.Type(typed);
		view.PublishPendingEdits();

		WorkspaceDocumentSnapshot current = Snapshot(other, version: 1, key: opened.DocumentKey);

		WorkspaceDocumentViewApplyResult acknowledged = view.AcknowledgeApply(
			Acknowledgment(published[0], WorkspaceDocumentMutationOutcome.StaleDocument, current)
		);

		Assert.AreEqual(WorkspaceDocumentViewApplyOutcome.Applied, acknowledged.Outcome);
		Assert.IsFalse(view.HasConflict);
		Assert.AreEqual(merged, view.Text);
		Assert.AreEqual(other, view.SynchronizedText);
		Assert.HasCount(2, published);
		Assert.AreEqual(1, published[1].Identity.Version);
		Assert.AreEqual(merged, published[1].Content);
	}

	[TestMethod]
	public async Task TwoViews_TypingInDifferentLines_MergeBothEditsInTheStoreAndInBothViews()
	{
		await using var host = new CoordinatorTestHost();
		File.WriteAllText(host.DocumentPath, ThreeLines);

		var first = new TestTextView("first");
		var second = new TestTextView("second");

		await host.Coordinator.OpenWithViewAsync(
			filePath: host.DocumentPath,
			options: s_openOptions,
			view: first,
			cancellationToken: TestContext.CancellationToken
		);

		await host.Coordinator.OpenWithViewAsync(
			filePath: host.DocumentPath,
			options: s_openOptions,
			view: second,
			cancellationToken: TestContext.CancellationToken
		);

		first.Type("ONE\ntwo\nthree\n");
		second.Type("one\ntwo\nTHREE\n");

		WorkspaceViewCoordinatorPublicationResult published = await host.Coordinator.PublishPendingEditsAsync(
			host.DocumentPath,
			TestContext.CancellationToken
		);

		const string Merged = "ONE\ntwo\nTHREE\n";

		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, published.ViewSynchronization.Outcome);
		Assert.AreEqual(Merged, published.Snapshot?.Content);
		Assert.AreEqual(Merged, first.Text);
		Assert.AreEqual(Merged, second.Text);
		Assert.IsFalse(first.HasPendingEdits);
		Assert.IsFalse(second.HasPendingEdits);
		Assert.IsFalse(second.HasConflict);
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	public async Task TwoViews_TypingAtTheSamePlace_ReportAConflict_AndEachResolutionLeavesOneTextEverywhere(bool keepsViewText)
	{
		const string FirstText = "base\nline typed in first\n";
		const string SecondText = "base\nline typed in second\n";

		await using var host = new CoordinatorTestHost();
		File.WriteAllText(host.DocumentPath, "base\n");

		var first = new TestTextView("first");
		var second = new TestTextView("second");

		await host.Coordinator.OpenWithViewAsync(
			filePath: host.DocumentPath,
			options: s_openOptions,
			view: first,
			cancellationToken: TestContext.CancellationToken
		);

		await host.Coordinator.OpenWithViewAsync(
			filePath: host.DocumentPath,
			options: s_openOptions,
			view: second,
			cancellationToken: TestContext.CancellationToken
		);

		int conflictChanges = 0;
		second.ConflictChanged += (_, _) => conflictChanges++;

		first.Type(FirstText);
		second.Type(SecondText);

		WorkspaceViewCoordinatorPublicationResult published = await host.Coordinator.PublishPendingEditsAsync(
			host.DocumentPath,
			TestContext.CancellationToken
		);

		// The first publication reached the store, and the second view keeps its text instead of replacing it.
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, published.ViewSynchronization.Outcome);
		Assert.AreEqual("second", published.ViewSynchronization.Issues.Single().ViewId);
		Assert.IsTrue(second.HasConflict);
		Assert.AreEqual(1, conflictChanges);
		Assert.IsTrue(host.Store.TryGetSnapshot(host.DocumentPath, out WorkspaceDocumentSnapshot? current));
		Assert.AreEqual(FirstText, current.Content);
		Assert.AreEqual(FirstText, first.Text);
		Assert.AreEqual(SecondText, second.Text);

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> deleted = await host.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(current.Identity, current.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, deleted.ViewSynchronization.Outcome);

		if (keepsViewText)
			second.KeepViewText();
		else
			second.TakeStoreText();

		string expected = keepsViewText ? SecondText : FirstText;

		Assert.IsFalse(second.HasConflict);
		Assert.AreEqual(2, conflictChanges);
		Assert.IsTrue(host.Store.TryGetSnapshot(host.DocumentPath, out current));
		Assert.AreEqual(expected, current.Content);
		Assert.AreEqual(expected, first.Text);
		Assert.AreEqual(expected, second.Text);

		published = await host.Coordinator.PublishPendingEditsAsync(host.DocumentPath, TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, published.ViewSynchronization.Outcome);
	}

	[TestMethod]
	public async Task TakeStoreText_AfterTheStoreChangedAgain_ShowsTheNewestText()
	{
		const string Newest = "base\nline typed in first\nmore\n";

		await using var host = new CoordinatorTestHost();
		File.WriteAllText(host.DocumentPath, "base\n");

		var first = new TestTextView("first");
		var second = new TestTextView("second");

		await host.Coordinator.OpenWithViewAsync(
			filePath: host.DocumentPath,
			options: s_openOptions,
			view: first,
			cancellationToken: TestContext.CancellationToken
		);

		await host.Coordinator.OpenWithViewAsync(
			filePath: host.DocumentPath,
			options: s_openOptions,
			view: second,
			cancellationToken: TestContext.CancellationToken
		);

		first.Type("base\nline typed in first\n");
		second.Type("base\nline typed in second\n");

		await host.Coordinator.PublishPendingEditsAsync(host.DocumentPath, TestContext.CancellationToken);

		Assert.IsTrue(second.HasConflict);

		// The conflicted view skips the refresh of this publication, so it still holds the text of the conflict.
		first.Type(Newest);
		await host.Coordinator.PublishPendingEditsAsync(host.DocumentPath, TestContext.CancellationToken);
		second.TakeStoreText();

		Assert.IsFalse(second.HasConflict);
		Assert.IsFalse(second.HasPendingEdits);
		Assert.AreEqual(Newest, second.Text);
		Assert.IsTrue(host.Store.TryGetSnapshot(host.DocumentPath, out WorkspaceDocumentSnapshot? current));
		Assert.AreEqual(Newest, current.Content);
	}

	[TestMethod]
	public void AcknowledgeApply_OfAPublicationWhoseTextTheStoreAlreadyHolds_AdoptsItWithoutAConflict()
	{
		// The store took the publication, but its acknowledgment never reached the view, so the next publication is stale against the same text.
		var view = new TestTextView();
		WorkspaceDocumentSnapshot opened = Snapshot("one\n");
		view.Open(opened);
		List<WorkspaceDocumentReplaceRequest> published = CapturePublications(view);
		view.Type("two\n");
		view.PublishPendingEdits();

		WorkspaceDocumentViewApplyResult acknowledged = view.AcknowledgeApply(
			Acknowledgment(published[0], WorkspaceDocumentMutationOutcome.StaleDocument, Snapshot("two\n", version: 1, key: opened.DocumentKey))
		);

		Assert.AreEqual(WorkspaceDocumentViewApplyOutcome.Applied, acknowledged.Outcome);
		Assert.IsFalse(view.HasConflict);
		Assert.IsFalse(view.HasPendingEdits);
		Assert.AreEqual("two\n", view.SynchronizedText);
		Assert.HasCount(1, published);
	}

	[TestMethod]
	public void AcknowledgeApply_WhenTheMergedTextCannotBeShown_FailsAndKeepsTheBase_SoTheNextPublicationMergesAgain()
	{
		var view = new TestTextView();
		WorkspaceDocumentSnapshot opened = Snapshot(ThreeLines);
		view.Open(opened);
		List<WorkspaceDocumentReplaceRequest> published = CapturePublications(view);
		view.Type("one\ntwo\nTHREE\n");
		view.PublishPendingEdits();
		view.ThrowOnReplace = true;

		WorkspaceDocumentViewApplyResult acknowledged = view.AcknowledgeApply(
			Acknowledgment(
				published[0],
				WorkspaceDocumentMutationOutcome.StaleDocument,
				Snapshot("ONE\ntwo\nthree\n", version: 1, key: opened.DocumentKey)
			)
		);

		Assert.AreEqual(WorkspaceDocumentViewApplyOutcome.Failed, acknowledged.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewAcknowledgeFailed, acknowledged.Failure?.Code);
		Assert.IsFalse(view.HasConflict);
		Assert.AreEqual(ThreeLines, view.SynchronizedText);
		Assert.AreEqual("one\ntwo\nTHREE\n", view.Text);

		view.ThrowOnReplace = false;
		view.PublishPendingEdits();

		Assert.HasCount(2, published);
		Assert.AreEqual(opened.Version, published[1].Identity.Version);
	}

	[TestMethod]
	[DataRow(true, "V1\n2\nV3\n4\nS5\n")]
	[DataRow(false, "V1\n2\nS3\n4\nS5\n")]
	public void Resolution_TakesOneSideOfTheConflictingLines_AndKeepsEveryOtherChangeOfBothSides(bool keepsViewText, string expected)
	{
		var view = new TestTextView();
		WorkspaceDocumentSnapshot opened = Snapshot("1\n2\n3\n4\n5\n");
		view.Open(opened);
		List<WorkspaceDocumentReplaceRequest> published = CapturePublications(view);
		view.Type("V1\n2\nV3\n4\n5\n");
		view.PublishPendingEdits();
		WorkspaceDocumentSnapshot current = Snapshot("1\n2\nS3\n4\nS5\n", version: 1, key: opened.DocumentKey);
		view.AcknowledgeApply(Acknowledgment(published[0], WorkspaceDocumentMutationOutcome.StaleDocument, current));

		Assert.IsTrue(view.HasConflict);

		if (keepsViewText)
			view.KeepViewText();
		else
			view.TakeStoreText();

		Assert.IsFalse(view.HasConflict);
		Assert.AreEqual(expected, view.Text);
		Assert.HasCount(2, published);
		Assert.AreEqual(expected, published[1].Content);
		Assert.AreEqual(1, published[1].Identity.Version);
	}

	[TestMethod]
	[DataRow(IdentityChange.Rename, false)]
	[DataRow(IdentityChange.Rename, true)]
	[DataRow(IdentityChange.SaveAs, false)]
	[DataRow(IdentityChange.SaveAs, true)]
	[DataRow(IdentityChange.DirectoryRename, false)]
	[DataRow(IdentityChange.DirectoryRename, true)]
	public async Task IdentityChange_OfAViewWithPendingEdits_MergesTheChangeAnotherViewPublished_OrReportsAConflict(
		IdentityChange kind,
		bool conflicts
	)
	{
		await using var host = new CoordinatorTestHost();
		File.WriteAllText(host.DocumentPath, ThreeLines);

		var first = new TestTextView("first");
		var second = new TestTextView("second");

		await host.Coordinator.OpenWithViewAsync(
			filePath: host.DocumentPath,
			options: s_openOptions,
			view: first,
			cancellationToken: TestContext.CancellationToken
		);

		await host.Coordinator.OpenWithViewAsync(
			filePath: host.DocumentPath,
			options: s_openOptions,
			view: second,
			cancellationToken: TestContext.CancellationToken
		);

		// The second view publishes, and the refresh skips the first view because it holds pending edits.
		first.Type("ONE\ntwo\nthree\n");
		second.Type(conflicts ? "One\ntwo\nthree\n" : "one\ntwo\nTHREE\n");

		second.PublishPendingEdits();

		using var destination = new TemporaryDirectory();
		string newPath = await ChangeIdentityAsync(host, kind, destination.Path);

		Assert.AreEqual(newPath, first.DocumentId);
		Assert.AreEqual(conflicts, first.HasConflict);

		WorkspaceViewCoordinatorPublicationResult published = await host.Coordinator.PublishPendingEditsAsync(newPath, TestContext.CancellationToken);

		if (conflicts)
		{
			Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, published.ViewSynchronization.Outcome);
			Assert.AreEqual("ONE\ntwo\nthree\n", first.Text);

			first.KeepViewText();
		}
		else
		{
			Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, published.ViewSynchronization.Outcome);
		}

		string expected = conflicts ? "ONE\ntwo\nthree\n" : "ONE\ntwo\nTHREE\n";

		Assert.IsTrue(host.Store.TryGetSnapshot(newPath, out WorkspaceDocumentSnapshot? current));
		Assert.AreEqual(expected, current.Content);
		Assert.AreEqual(expected, first.Text);
		Assert.AreEqual(expected, second.Text);
	}

	[TestMethod]
	public async Task IdentityChange_DuringAnOpenConflict_KeepsTheConflict_AndItsResolutionKeepsTheLaterChangeOfTheOtherView()
	{
		await using var host = new CoordinatorTestHost();
		File.WriteAllText(host.DocumentPath, "1\n2\n3\n4\n5\n");

		var other = new TestTextView("other");
		var conflicted = new TestTextView("conflicted");

		await host.Coordinator.OpenWithViewAsync(
			filePath: host.DocumentPath,
			options: s_openOptions,
			view: other,
			cancellationToken: TestContext.CancellationToken
		);

		await host.Coordinator.OpenWithViewAsync(
			filePath: host.DocumentPath,
			options: s_openOptions,
			view: conflicted,
			cancellationToken: TestContext.CancellationToken
		);

		other.Type("1\nB\n3\n4\n5\n");
		conflicted.Type("1\nA\n3\n4\n5\n");

		other.PublishPendingEdits();
		conflicted.PublishPendingEdits();

		Assert.IsTrue(conflicted.HasConflict);

		other.Type("1\nB\n3\n4\nB5\n");
		other.PublishPendingEdits();
		using var destination = new TemporaryDirectory();
		string newPath = await ChangeIdentityAsync(host, IdentityChange.Rename, destination.Path);

		Assert.IsTrue(conflicted.HasConflict);
		Assert.AreEqual(newPath, conflicted.DocumentId);

		conflicted.KeepViewText();

		Assert.IsTrue(host.Store.TryGetSnapshot(newPath, out WorkspaceDocumentSnapshot? current));
		Assert.AreEqual("1\nA\n3\n4\nB5\n", current.Content);
		Assert.AreEqual(current.Content, conflicted.Text);
		Assert.AreEqual(current.Content, other.Text);
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	public async Task ConflictChanged_WhoseHandlerResolvesInlineOrPosted_LeavesNoViewBlocked(bool resolvesInline)
	{
		await using var host = new CoordinatorTestHost();
		File.WriteAllText(host.DocumentPath, "base\n");

		var first = new TestTextView("first");
		var second = new TestTextView("second");

		await host.Coordinator.OpenWithViewAsync(
			filePath: host.DocumentPath,
			options: s_openOptions,
			view: first,
			cancellationToken: TestContext.CancellationToken
		);

		await host.Coordinator.OpenWithViewAsync(
			filePath: host.DocumentPath,
			options: s_openOptions,
			view: second,
			cancellationToken: TestContext.CancellationToken
		);

		Queue<Action> posted = new();

		second.ConflictChanged += (_, _) =>
		{
			if (!second.HasConflict)
				return;

			if (resolvesInline)
				second.KeepViewText();
			else
				posted.Enqueue(second.KeepViewText);
		};

		first.Type("base\nfirst\n");
		second.Type("base\nsecond\n");

		WorkspaceViewCoordinatorPublicationResult published = await host.Coordinator.PublishPendingEditsAsync(
			host.DocumentPath,
			TestContext.CancellationToken
		);

		if (!resolvesInline)
		{
			// A conflict blocks by its state, so its issue carries no failure.
			Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, published.ViewSynchronization.Outcome);
			Assert.IsNull(published.ViewSynchronization.Issues.Single().Failure);

			while (posted.TryDequeue(out Action? resolve))
				resolve();
		}

		published = await host.Coordinator.PublishPendingEditsAsync(host.DocumentPath, TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, published.ViewSynchronization.Outcome);
		Assert.AreEqual("base\nsecond\n", published.Snapshot?.Content);
		Assert.AreEqual("base\nsecond\n", first.Text);

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult> deleted = await host.Coordinator.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(published.Snapshot!.Identity, published.Snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, deleted.StoreResult?.Outcome);
	}

	// The identity changes that hand the views a snapshot: each returns the new path of the document.
	public enum IdentityChange
	{
		Rename,
		SaveAs,
		DirectoryRename,
	}

	private async Task<string> ChangeIdentityAsync(CoordinatorTestHost host, IdentityChange kind, string destinationDirectory)
	{
		Assert.IsTrue(host.Store.TryGetSnapshot(host.DocumentPath, out WorkspaceDocumentSnapshot? current));

		switch (kind)
		{
			case IdentityChange.Rename:
			{
				string newPath = Path.Combine(host.DirectoryPath, "renamed.txt");

				WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentRenameResult> renamed = await host.Coordinator.RenameAsync(
					new WorkspaceDocumentRenameRequest(current.Identity, current.OnDiskStamp, newPath),
					TestContext.CancellationToken
				);

				Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, renamed.StoreResult.Outcome);
				return renamed.StoreResult.Snapshot!.DocumentId;
			}

			case IdentityChange.SaveAs:
			{
				string newPath = Path.Combine(host.DirectoryPath, "copy.txt");

				WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentSaveAsResult> saved = await host.Coordinator.SaveAsAsync(
					new WorkspaceDocumentSaveAsRequest(current.Identity, current.OnDiskStamp, newPath),
					TestContext.CancellationToken
				);

				Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, saved.StoreResult.Outcome);
				return saved.StoreResult.Snapshot!.DocumentId;
			}

			default:
			{
				string newDirectory = Path.Combine(destinationDirectory, "moved");

				WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult> moved = await host.Coordinator.RenameDirectoryAsync(
					new WorkspaceDocumentDirectoryRenameRequest(host.DirectoryPath, newDirectory),
					TestContext.CancellationToken
				);

				Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, moved.StoreResult?.Outcome);
				return moved.StoreResult!.Snapshots.Single().DocumentId;
			}
		}
	}
}
