using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceDocumentTextViewBaseTests
{
	[TestMethod]
	public void PublishPendingEdits_RaisesTheApplyRequestWithTheEditorText_AndItsAcknowledgmentMovesTheBase()
	{
		var view = new TestTextView();
		WorkspaceDocumentSnapshot opened = Snapshot("print(1)");
		view.Open(opened);
		List<WorkspaceDocumentReplaceRequest> published = CapturePublications(view);
		view.Type("print(2)");

		view.PublishPendingEdits();

		Assert.AreEqual("print(2)", published.Single().Content);
		Assert.AreEqual(opened.Identity, published[0].Identity);

		WorkspaceDocumentSnapshot applied = Snapshot("print(2)", version: 1, key: opened.DocumentKey);

		WorkspaceDocumentViewApplyResult acknowledged = view.AcknowledgeApply(
			Acknowledgment(published[0], WorkspaceDocumentMutationOutcome.Changed, applied)
		);

		Assert.AreEqual(WorkspaceDocumentViewApplyOutcome.Applied, acknowledged.Outcome);
		Assert.IsFalse(view.HasPendingEdits);
		Assert.AreEqual("print(2)", view.SynchronizedText);
	}

	[TestMethod]
	public void PublishPendingEdits_OfADetachedViewOrOneWithoutPendingEdits_RaisesNothing()
	{
		var view = new TestTextView();
		List<WorkspaceDocumentReplaceRequest> published = CapturePublications(view);

		view.PublishPendingEdits();
		view.Open(Snapshot("print(1)"));
		view.PublishPendingEdits();

		Assert.IsEmpty(published);
	}

	[TestMethod]
	public void PublishPendingEdits_WhileAPublicationIsInFlight_HoldsBack_AndRepublishesAgainstTheNewBaseOnceItIsAcknowledged()
	{
		var view = new TestTextView();
		WorkspaceDocumentSnapshot opened = Snapshot("print(1)");
		view.Open(opened);
		List<WorkspaceDocumentReplaceRequest> published = CapturePublications(view);

		view.Type("print(A)");
		view.PublishPendingEdits();
		view.Type("print(B)");
		view.PublishPendingEdits();

		Assert.HasCount(1, published);

		WorkspaceDocumentSnapshot appliedA = Snapshot("print(A)", version: 1, key: opened.DocumentKey);
		view.AcknowledgeApply(Acknowledgment(published[0], WorkspaceDocumentMutationOutcome.Changed, appliedA));

		Assert.HasCount(2, published);
		Assert.AreEqual("print(B)", published[1].Content);
		Assert.AreEqual(appliedA.Identity, published[1].Identity);
	}

	[TestMethod]
	public void AcknowledgeApply_OfAnotherRequestOrOtherText_DoesNotEndThePublicationInFlight()
	{
		var view = new TestTextView();
		WorkspaceDocumentSnapshot opened = Snapshot("print(1)");
		view.Open(opened);
		List<WorkspaceDocumentReplaceRequest> published = CapturePublications(view);
		view.Type("print(A)");
		view.PublishPendingEdits();

		// A rejection of a request at another version is not this publication.
		WorkspaceDocumentSnapshot stale = Snapshot("print(1)", version: 0, key: opened.DocumentKey);

		view.AcknowledgeApply(
			new WorkspaceDocumentMutationResult(WorkspaceDocumentMutationOutcome.StaleDocument, stale.Identity with { Version = 7 }, stale)
		);

		// A discard requested against the same version carries other text, so it is not this publication either.
		WorkspaceDocumentSnapshot discarded = Snapshot("print(1)", version: 1, key: opened.DocumentKey);
		view.AcknowledgeApply(Acknowledgment(published[0], WorkspaceDocumentMutationOutcome.Changed, discarded));

		view.Type("print(B)");
		view.PublishPendingEdits();

		Assert.HasCount(1, published);
	}

	[TestMethod]
	public void Close_ForgetsThePublicationInFlight()
	{
		var view = new TestTextView();
		view.Open(Snapshot("print(1)"));
		List<WorkspaceDocumentReplaceRequest> published = CapturePublications(view);
		view.Type("print(A)");
		view.PublishPendingEdits();

		view.Close();
		view.Open(Snapshot("print(1)"));
		view.Type("print(B)");
		view.PublishPendingEdits();

		Assert.HasCount(2, published);
		Assert.AreEqual("print(B)", published[1].Content);
	}

	[TestMethod]
	public async Task PublishPendingEdits_TwiceBeforeAQueuedAcknowledgment_LeavesTheLatestTextInTheStore()
	{
		var queue = new QueuedDispatch();
		await using var host = new CoordinatorTestHost(queue);
		var view = new TestTextView();

		await queue.RunUntilAsync(host.Coordinator.OpenWithViewAsync(host.DocumentPath, s_openOptions, view), TestContext.CancellationToken);

		view.Type("A");
		view.PublishPendingEdits();
		view.Type("B");
		view.PublishPendingEdits();

		await queue.RunUntilAsync(TestPolling.UntilAsync(() => !view.HasPendingEdits, TestPolling.DefaultTimeout), TestContext.CancellationToken);

		Assert.IsTrue(host.Store.TryGetSnapshot(view.DocumentId!, out WorkspaceDocumentSnapshot? current));
		Assert.AreEqual("B", current.Content);
		Assert.AreEqual(2, current.Version);
	}

	[TestMethod]
	public async Task PublishPendingEditsAsync_AfterAnUnacknowledgedPublication_WaitsForTheTextThatWasHeldBack()
	{
		// The newest action runs first, so the blocking probe would run before the acknowledgment of the republished text unless the call waits
		// for that apply too.
		var queue = new QueuedDispatch(runsNewestFirst: true);
		await using var host = new CoordinatorTestHost(queue);
		var view = new TestTextView();

		await queue.RunUntilAsync(host.Coordinator.OpenWithViewAsync(host.DocumentPath, s_openOptions, view), TestContext.CancellationToken);

		view.Type("A");
		view.PublishPendingEdits();
		view.Type("B");

		// The acknowledgment of the first publication is queued before the call asks, so the call meets the publication in flight.
		await TestPolling.UntilAsync(() => queue.Count == 1, TestPolling.DefaultTimeout);

		WorkspaceViewCoordinatorPublicationResult published = await queue.RunUntilAsync(
			host.Coordinator.PublishPendingEditsAsync(view.DocumentId!, TestContext.CancellationToken),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, published.ViewSynchronization.Outcome);
		Assert.IsTrue(host.Store.TryGetSnapshot(view.DocumentId!, out WorkspaceDocumentSnapshot? current));
		Assert.AreEqual("B", current.Content);
	}

	[TestMethod]
	public async Task PublishPendingEdits_ThroughTheInlineCoordinator_IsAcknowledgedBeforeItReturns()
	{
		await using var host = new CoordinatorTestHost();
		var view = new TestTextView();

		await host.Coordinator.OpenWithViewAsync(host.DocumentPath, s_openOptions, view);
		view.Type("published");

		view.PublishPendingEdits();

		Assert.IsFalse(view.HasPendingEdits);
		Assert.IsTrue(host.Store.TryGetSnapshot(view.DocumentId!, out WorkspaceDocumentSnapshot? current));
		Assert.AreEqual("published", current.Content);

		// The acknowledgment ended the publication, so the next one is raised at once.
		view.Type("again");
		view.PublishPendingEdits();

		Assert.IsTrue(host.Store.TryGetSnapshot(view.DocumentId!, out current));
		Assert.AreEqual("again", current.Content);
	}

	[TestMethod]
	public async Task NoPublishDelay_HoldsTypedTextBack_UntilTheCoordinatorAsksForIt()
	{
		await using var host = new CoordinatorTestHost();
		var view = new TestTextView();

		WorkspaceDocumentSnapshot opened = (await host.Coordinator.OpenWithViewAsync(host.DocumentPath, s_openOptions, view)).Snapshot!;

		int raised = 0;
		view.ApplyRequested += (_, _) => raised++;
		view.Type("typed");

		Assert.AreEqual(0, raised);
		Assert.IsTrue(view.HasPendingEdits);

		WorkspaceViewCoordinatorPublicationResult published = await host.Coordinator.PublishPendingEditsAsync(
			opened.DocumentId,
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, published.ViewSynchronization.Outcome);
		Assert.AreEqual(1, raised);
		Assert.IsFalse(view.HasPendingEdits);
		Assert.IsTrue(host.Store.TryGetSnapshot(opened.DocumentId, out WorkspaceDocumentSnapshot? current));
		Assert.AreEqual("typed", current.Content);
	}

	[TestMethod]
	public async Task ZeroPublishDelay_PublishesEachChange()
	{
		await using var host = new CoordinatorTestHost();
		var view = new TestTextView(publishDelay: TimeSpan.Zero);

		WorkspaceDocumentSnapshot opened = (await host.Coordinator.OpenWithViewAsync(host.DocumentPath, s_openOptions, view)).Snapshot!;

		view.Type("first");
		view.Type("second");

		Assert.IsFalse(view.HasPendingEdits);
		Assert.IsTrue(host.Store.TryGetSnapshot(opened.DocumentId, out WorkspaceDocumentSnapshot? current));
		Assert.AreEqual("second", current.Content);
		Assert.AreEqual(2, current.Version);
		Assert.IsTrue(view.IsDirty);
	}

	[TestMethod]
	public void ZeroPublishDelay_DoesNotPublishTheTextThatAnAdoptedSnapshotReplaced()
	{
		var view = new TestTextView(publishDelay: TimeSpan.Zero);
		view.Open(Snapshot("print(1)"));

		int raised = 0;
		view.ApplyRequested += (_, _) => raised++;

		view.Refresh(Snapshot("print(2)", version: 1));

		Assert.AreEqual("print(2)", view.Text);
		Assert.AreEqual(0, raised);
	}

	[TestMethod]
	public void PositivePublishDelay_DebouncesTyping_AndPublishesOnceWhenTheDelayElapses()
	{
		var dispatcher = new FakeUiDispatcher();
		var delay = TimeSpan.FromMilliseconds(300);
		var view = new TestTextView(dispatcher: dispatcher, publishDelay: delay);

		view.Open(Snapshot("print(1)"));
		List<WorkspaceDocumentReplaceRequest> published = CapturePublications(view);

		view.Type("print(2)");
		view.Type("print(3)");

		Assert.IsEmpty(published);
		Assert.AreEqual(1, dispatcher.PendingCount);
		Assert.AreSequenceEqual([delay, delay], dispatcher.RequestedDelays);

		dispatcher.RunPending();

		Assert.AreEqual("print(3)", published.Single().Content);
	}

	[TestMethod]
	public void PositivePublishDelay_ADirectPublicationCancelsTheDebouncedOne()
	{
		var dispatcher = new FakeUiDispatcher();
		var view = new TestTextView(dispatcher: dispatcher, publishDelay: TimeSpan.FromMilliseconds(300));

		view.Open(Snapshot("print(1)"));
		List<WorkspaceDocumentReplaceRequest> published = CapturePublications(view);

		view.Type("print(2)");
		view.PublishPendingEdits();

		Assert.HasCount(1, published);
		Assert.AreEqual(0, dispatcher.PendingCount);
	}

	[TestMethod]
	public void PositivePublishDelay_ATickWithNothingPendingOrAfterDisposal_PublishesNothing()
	{
		var dispatcher = new FakeUiDispatcher { RunsCanceledCallbacks = true };
		var view = new TestTextView(dispatcher: dispatcher, publishDelay: TimeSpan.FromMilliseconds(300));

		view.Open(Snapshot("print(1)"));
		List<WorkspaceDocumentReplaceRequest> published = CapturePublications(view);

		view.Type("print(2)");
		view.Type("print(1)");

		dispatcher.RunPending();

		view.Type("print(3)");
		view.Dispose();
		dispatcher.RunPending();

		Assert.IsEmpty(published);
	}
}
