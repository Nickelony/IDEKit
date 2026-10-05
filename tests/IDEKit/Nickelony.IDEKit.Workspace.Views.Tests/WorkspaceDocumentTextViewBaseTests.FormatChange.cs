using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceDocumentTextViewBaseTests
{
	[TestMethod]
	[DataRow("one\r\ntwo\r\n", LineTerminatorStyle.CrLf)]
	[DataRow("one\ntwo\n", LineTerminatorStyle.Lf)]
	[DataRow("one\rtwo\r", LineTerminatorStyle.Cr)]
	[DataRow("one\ntwo\r\n", LineTerminatorStyle.Mixed)]
	public void PublishPendingEdits_CarriesTheLineTerminatorStyleOfTheEditorText(string text, LineTerminatorStyle expected)
	{
		var view = new TestTextView();
		view.Open(Snapshot("print(1)"));
		List<WorkspaceDocumentReplaceRequest> published = CapturePublications(view);

		view.Type(text);
		view.PublishPendingEdits();

		Assert.AreEqual(text, published.Single().Content);
		Assert.AreEqual(expected, published.Single().FileFormat.LineTerminatorStyle);
		Assert.AreEqual(TestSnapshots.FileFormat.Encoding, published.Single().FileFormat.Encoding);
		Assert.AreEqual(TestSnapshots.FileFormat.HasBom, published.Single().FileFormat.HasBom);
	}

	[TestMethod]
	public void PublishPendingEdits_OfATextWithoutALineTerminator_KeepsTheStyleTheDocumentRecorded()
	{
		var view = new TestTextView();
		view.Open(Snapshot("print(1)"));
		List<WorkspaceDocumentReplaceRequest> published = CapturePublications(view);

		view.Type("print(2)");
		view.PublishPendingEdits();

		Assert.AreEqual(TestSnapshots.FileFormat, published.Single().FileFormat);
	}

	[TestMethod]
	public void PublishPendingEdits_AfterTheTerminatorsWereConvertedAndUndone_CarriesTheStyleOfTheRestoredText()
	{
		var view = new TestTextView();
		WorkspaceDocumentSnapshot opened = Snapshot("one\r\ntwo\r\n");
		view.Open(opened);
		List<WorkspaceDocumentReplaceRequest> published = CapturePublications(view);

		view.Type("one\ntwo\n");
		view.PublishPendingEdits();

		view.AcknowledgeApply(
			Acknowledgment(
				published[0],
				WorkspaceDocumentMutationOutcome.Changed,

				Snapshot("one\ntwo\n", version: 1, key: opened.DocumentKey) with
				{
					FileFormat = published[0].FileFormat,
				}
			)
		);

		view.Type("one\r\ntwo\r\n");
		view.PublishPendingEdits();

		Assert.AreEqual(LineTerminatorStyle.Lf, published[0].FileFormat.LineTerminatorStyle);
		Assert.AreEqual(LineTerminatorStyle.CrLf, published[1].FileFormat.LineTerminatorStyle);
	}

	[TestMethod]
	public async Task ConvertingTheTerminatorsInTheEditor_MarksTheDocumentDirty_AndASaveWritesTheNewTerminatorsAndStyle()
	{
		await using var host = new CoordinatorTestHost();

		File.WriteAllBytes(host.DocumentPath, "one\r\ntwo\r\n"u8.ToArray());

		var view = new TestTextView();
		WorkspaceViewCoordinatorOpenResult opened = await host.Coordinator.OpenWithViewAsync(host.DocumentPath, s_openOptions, view);

		Assert.AreEqual(LineTerminatorStyle.CrLf, opened.Snapshot!.FileFormat.LineTerminatorStyle);
		Assert.IsFalse(view.IsDirty);

		view.Type("one\ntwo\n");

		Assert.IsTrue(view.IsDirty);

		WorkspaceViewCoordinatorPublicationResult publication = await host.Coordinator.PublishPendingEditsAsync(
			host.DocumentPath,
			TestContext.CancellationToken
		);

		Assert.AreEqual(LineTerminatorStyle.Lf, publication.Snapshot!.FileFormat.LineTerminatorStyle);
		Assert.IsTrue(publication.Snapshot.IsDirty);

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult> committed = await host.Coordinator.CommitAsync(
			new WorkspaceDocumentCommitRequest(publication.Snapshot.Identity, publication.Snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, committed.StoreResult?.Outcome);
		Assert.AreSequenceEqual("one\ntwo\n"u8.ToArray(), File.ReadAllBytes(host.DocumentPath));
		Assert.AreEqual(LineTerminatorStyle.Lf, committed.StoreResult!.Snapshot!.FileFormat.LineTerminatorStyle);
		Assert.IsFalse(view.IsDirty);
	}

	[TestMethod]
	public async Task ConvertingTheTerminatorsInTheEditorAndUndoingIt_LeavesTheDocumentClean()
	{
		await using var host = new CoordinatorTestHost();

		File.WriteAllBytes(host.DocumentPath, "one\r\ntwo\r\n"u8.ToArray());

		var view = new TestTextView();
		await host.Coordinator.OpenWithViewAsync(host.DocumentPath, s_openOptions, view);

		view.Type("one\ntwo\n");
		await host.Coordinator.PublishPendingEditsAsync(host.DocumentPath, TestContext.CancellationToken);
		view.Type("one\r\ntwo\r\n");

		WorkspaceViewCoordinatorPublicationResult publication = await host.Coordinator.PublishPendingEditsAsync(
			host.DocumentPath,
			TestContext.CancellationToken
		);

		Assert.AreEqual(LineTerminatorStyle.CrLf, publication.Snapshot!.FileFormat.LineTerminatorStyle);
		Assert.IsFalse(publication.Snapshot.IsDirty);
		Assert.IsFalse(view.IsDirty);
	}

	[TestMethod]
	public async Task ReplaceAsync_WithAnotherFormat_GivesTheViewsLaterPublicationsThatFormat_AndASaveWritesIt()
	{
		await using var host = new CoordinatorTestHost();
		var view = new TestTextView();

		WorkspaceViewCoordinatorOpenResult opened = await host.Coordinator.OpenWithViewAsync(host.DocumentPath, s_openOptions, view);
		List<WorkspaceDocumentReplaceRequest> published = CapturePublications(view);

		TextFileFormat format = opened.Snapshot!.FileFormat with
		{
			Encoding = TextEncoding.Utf16LittleEndian,
			HasBom = true,
			LineTerminatorStyle = LineTerminatorStyle.CrLf,
		};

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await host.Coordinator.ReplaceAsync(
			new WorkspaceDocumentReplaceRequest(opened.Snapshot.Identity, opened.Snapshot.Content, format),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.StoreResult?.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, replaced.ViewSynchronization.Outcome);
		Assert.AreEqual("initial", view.Text);
		Assert.IsTrue(view.IsDirty, "The format is unsaved state, so the view reports the document dirty.");

		view.Type("one\r\ntwo");

		WorkspaceViewCoordinatorPublicationResult publication = await host.Coordinator.PublishPendingEditsAsync(
			host.DocumentPath,
			TestContext.CancellationToken
		);

		Assert.AreEqual(format, published.Last().FileFormat);

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult> committed = await host.Coordinator.CommitAsync(
			new WorkspaceDocumentCommitRequest(publication.Snapshot!.Identity, publication.Snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, committed.StoreResult?.Outcome);
		Assert.AreSequenceEqual(WorkspaceTextCodec.Encode("one\r\ntwo", format), File.ReadAllBytes(host.DocumentPath));
		Assert.IsFalse(view.IsDirty);
	}

	[TestMethod]
	public async Task ReplaceAsync_WithAnotherFormat_WhileTheViewHoldsPendingEdits_IsBlockedUntilTheEditsArePublished()
	{
		await using var host = new CoordinatorTestHost();
		var view = new TestTextView();

		WorkspaceViewCoordinatorOpenResult opened = await host.Coordinator.OpenWithViewAsync(host.DocumentPath, s_openOptions, view);
		TextFileFormat format = opened.Snapshot!.FileFormat with { LineTerminatorStyle = LineTerminatorStyle.Lf };

		view.Type("typed");

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> blocked = await host.Coordinator.ReplaceAsync(
			new WorkspaceDocumentReplaceRequest(opened.Snapshot.Identity, opened.Snapshot.Content, format),
			TestContext.CancellationToken
		);

		Assert.IsNull(blocked.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, blocked.ViewSynchronization.Outcome);
		Assert.AreEqual("typed", view.Text);

		WorkspaceViewCoordinatorPublicationResult publication = await host.Coordinator.PublishPendingEditsAsync(
			host.DocumentPath,
			TestContext.CancellationToken
		);

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await host.Coordinator.ReplaceAsync(
			new WorkspaceDocumentReplaceRequest(publication.Snapshot!.Identity, publication.Snapshot.Content, format),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.StoreResult?.Outcome);
		Assert.AreEqual("typed", replaced.StoreResult!.Snapshot!.Content);
		Assert.AreEqual(format, replaced.StoreResult.Snapshot.FileFormat);
		Assert.AreEqual("typed", view.Text);
	}
}
