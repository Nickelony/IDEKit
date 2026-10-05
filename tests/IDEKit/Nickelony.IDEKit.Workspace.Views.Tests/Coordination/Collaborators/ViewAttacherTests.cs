using System.Diagnostics.CodeAnalysis;
using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

/// <summary>Covers the view attacher's rejection checks, registration step and subscription undo directly.</summary>
[TestClass]
public sealed class ViewAttacherTests
{
	private const string UnreportedIdMessage = "The view did not report a view id.";

	private sealed class StubDocumentReader : IWorkspaceDocumentReader
	{
		internal Dictionary<string, WorkspaceDocumentSnapshot> Snapshots { get; } = new(StringComparer.Ordinal);

		public WorkspaceOptions Options { get; } = new() { PathComparison = LocalPathComparisonPolicy.CaseSensitive };

		public bool TryGetSnapshot(string? filePath, [NotNullWhen(true)] out WorkspaceDocumentSnapshot? snapshot)
		{
			snapshot = null;
			return filePath is not null && Snapshots.TryGetValue(filePath, out snapshot);
		}

		public Task<WorkspaceDocumentOpenResult> OpenAsync(
			string? filePath,
			WorkspaceDocumentOpenOptions options,
			CancellationToken cancellationToken = default
		) => throw new NotSupportedException();

		public IReadOnlyList<WorkspaceDocumentSnapshot> GetSnapshotsUnderDirectory(string? directoryPath) => throw new NotSupportedException();
	}

	private sealed class TestHost
	{
		internal TestHost()
		{
			Gate = new StopGate<Task>(SyncRoot);

			Tracker = new ViewTracker(
				owner: this,
				SyncRoot,
				stopGate: Gate,
				options: new WorkspaceOptions { PathComparison = LocalPathComparisonPolicy.CaseSensitive },
				onApplyRequested: static (_, _) => { }
			);

			Attacher = new ViewAttacher(Tracker, Probe.Dispatch, Documents);
		}

		internal object SyncRoot { get; } = new();

		internal StopGate<Task> Gate { get; }

		internal ViewTracker Tracker { get; }

		internal RecordingViewDispatcher Probe { get; } = new();

		internal StubDocumentReader Documents { get; } = new();

		internal ViewAttacher Attacher { get; }

		// Creates a tracked document: the reader answers its snapshot, as the store does for a document it tracks.
		internal WorkspaceDocumentSnapshot Document(string documentId = "doc", long version = 1)
		{
			WorkspaceDocumentSnapshot snapshot = TestSnapshots.Create(documentId, content: "content", version);

			Documents.Snapshots[documentId] = snapshot;
			return snapshot;
		}

		internal static WorkspaceDocumentOpenResult Opened(WorkspaceDocumentSnapshot snapshot) => new(WorkspaceDocumentOpenOutcome.Opened, snapshot);

		internal ViewEntry Register(IWorkspaceDocumentView view, WorkspaceDocumentSnapshot snapshot) =>
			Tracker.Register(view, view.ViewId, snapshot).Entry ?? throw new InvalidOperationException("The view was not registered.");

		// Registers a view the way an attach leaves it: opened on the document, so it reports a document key.
		internal ViewEntry RegisterOpened(FakeWorkspaceView view, WorkspaceDocumentSnapshot snapshot)
		{
			view.Open(snapshot);
			return Register(view, snapshot);
		}
	}

	[TestMethod]
	public async Task FindRejectionAsync_ForACleanUnattachedView_ReturnsNull()
	{
		var fixture = new TestHost();

		FakeWorkspaceView view = fixture.Probe.Watch(new FakeWorkspaceView("a"));

		Assert.IsNull(await fixture.Attacher.FindRejectionAsync(view).ConfigureAwait(false));
		Assert.AreEqual(1, fixture.Probe.DispatchCount);
		Assert.IsEmpty(fixture.Probe.MembersOutsideDispatch);
	}

	[TestMethod]
	public async Task FindRejectionAsync_WhenTheViewIdThrows_RejectsTheViewWithTheOpenFailureAndTheException()
	{
		var fixture = new TestHost();

		WorkspaceViewCoordinatorOpenResult? rejection = await fixture
			.Attacher.FindRejectionAsync(new FakeWorkspaceView("a") { ThrowOnViewId = true })
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewUnavailable, rejection?.Outcome);
		Assert.IsNull(rejection?.Snapshot);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.OpenFailed, rejection?.Failure?.Code);
		Assert.IsInstanceOfType<InvalidOperationException>(rejection?.Failure?.Exception);
	}

	[TestMethod]
	[DataRow("")]
	[DataRow("   ")]
	public async Task FindRejectionAsync_WhenTheViewIdIsBlank_RejectsTheViewWithTheUnreportedIdMessage(string viewId)
	{
		var fixture = new TestHost();

		WorkspaceViewCoordinatorOpenResult? rejection = await fixture
			.Attacher.FindRejectionAsync(new FakeWorkspaceView(viewId))
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewUnavailable, rejection?.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.OpenFailed, rejection?.Failure?.Code);
		Assert.AreEqual(UnreportedIdMessage, rejection?.Failure?.Message);
	}

	[TestMethod]
	public async Task FindRejectionAsync_ForARegisteredView_ReportsAlreadyOpenWithTheSnapshotOfItsDocument()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a");

		WorkspaceDocumentSnapshot snapshot = fixture.Document();

		fixture.RegisterOpened(view, snapshot);

		WorkspaceViewCoordinatorOpenResult? rejection = await fixture.Attacher.FindRejectionAsync(view).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen, rejection?.Outcome);
		Assert.AreSame(snapshot, rejection?.Snapshot);
	}

	[TestMethod]
	public async Task FindRejectionAsync_ForARegisteredViewWhoseDocumentIsNotTracked_ReportsAlreadyOpenWithoutASnapshot()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a");

		fixture.RegisterOpened(view, TestSnapshots.Create("untracked", "content"));

		WorkspaceViewCoordinatorOpenResult? rejection = await fixture.Attacher.FindRejectionAsync(view).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen, rejection?.Outcome);
		Assert.IsNull(rejection?.Snapshot);
	}

	[TestMethod]
	public async Task FindRejectionAsync_ForAnotherViewHoldingTheViewId_ReportsViewInUse()
	{
		var fixture = new TestHost();

		fixture.Register(new FakeWorkspaceView("dup"), fixture.Document());

		WorkspaceViewCoordinatorOpenResult? rejection = await fixture.Attacher.FindRejectionAsync(new FakeWorkspaceView("dup")).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewInUse, rejection?.Outcome);
		Assert.IsNull(rejection?.Snapshot);
		Assert.IsNull(rejection?.Failure);
	}

	[TestMethod]
	public async Task FindRejectionAsync_ForAViewThatHoldsADocumentEditsOrAConflict_ReportsViewUnavailableWithoutAFailure()
	{
		var fixture = new TestHost();

		FakeWorkspaceView[] views =
		[
			new("attached") { DocumentKey = new WorkspaceDocumentKey(Guid.NewGuid()) },
			new("edited") { HasPendingEdits = true },
			new("conflicted") { HasConflict = true },
		];

		foreach (FakeWorkspaceView view in views)
		{
			WorkspaceViewCoordinatorOpenResult? rejection = await fixture.Attacher.FindRejectionAsync(view).ConfigureAwait(false);

			Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewUnavailable, rejection?.Outcome, view.ViewId);
			Assert.IsNull(rejection?.Failure, view.ViewId);
		}
	}

	[TestMethod]
	public async Task FindRejectionAsync_WhenTheStateProbeThrows_ReportsViewUnavailableWithTheException()
	{
		var fixture = new TestHost();

		WorkspaceViewCoordinatorOpenResult? rejection = await fixture
			.Attacher.FindRejectionAsync(new FakeWorkspaceView("a") { ThrowOnStateProbe = true })
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewUnavailable, rejection?.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.OpenFailed, rejection?.Failure?.Code);
		Assert.IsInstanceOfType<InvalidOperationException>(rejection?.Failure?.Exception);
	}

	[TestMethod]
	public async Task AttachAsync_ForAStoreOutcomeThatDidNotOpenTheDocument_ReportsTheMappedOutcomeWithTheStoreFailure()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a");
		var failure = new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.LoadFailed, "The file could not be read.");
		var missing = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.NotFound, null);
		var failed = new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.LoadFailed, null, failure);

		WorkspaceViewCoordinatorOpenResult notFound = await fixture.Attacher.AttachAsync(view, missing).ConfigureAwait(false);
		WorkspaceViewCoordinatorOpenResult loadFailed = await fixture.Attacher.AttachAsync(view, failed).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.NotFound, notFound.Outcome);
		Assert.AreSame(missing, notFound.StoreResult);
		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.LoadFailed, loadFailed.Outcome);
		Assert.AreSame(failure, loadFailed.Failure);
		Assert.IsNull(loadFailed.Snapshot);
		Assert.AreEqual(0, view.OpenCount);
		Assert.AreEqual(0, view.CloseCount);
	}

	[TestMethod]
	public async Task AttachAsync_ForAnOpenedDocumentWithoutASnapshot_Throws()
	{
		var fixture = new TestHost();

		await Assert
			.ThrowsExactlyAsync<InvalidOperationException>(() =>
				fixture.Attacher.AttachAsync(new FakeWorkspaceView("a"), new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.Opened, null))
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	[DataRow(WorkspaceDocumentOpenOutcome.Opened)]
	[DataRow(WorkspaceDocumentOpenOutcome.AlreadyOpen)]
	public async Task AttachAsync_ForAnOpenedDocument_RegistersTheViewAndSubscribesToItsApplyRequests(WorkspaceDocumentOpenOutcome storeOutcome)
	{
		var fixture = new TestHost();

		FakeWorkspaceView view = fixture.Probe.Watch(new FakeWorkspaceView("a"));
		WorkspaceDocumentSnapshot snapshot = fixture.Document();

		var openResult = new WorkspaceDocumentOpenResult(storeOutcome, snapshot);

		WorkspaceViewCoordinatorOpenResult result = await fixture.Attacher.AttachAsync(view, openResult).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Opened, result.Outcome);
		Assert.AreSame(snapshot, result.Snapshot);
		Assert.AreSame(openResult, result.StoreResult);
		Assert.AreEqual(snapshot.DocumentId, fixture.Tracker.GetDocumentId(view));
		Assert.AreEqual(1, view.OpenCount);
		Assert.AreEqual(1, view.SubscriberCount);
		Assert.AreEqual(0, view.CloseCount);
		Assert.AreEqual(1, fixture.Probe.DispatchCount);
		Assert.IsEmpty(fixture.Probe.MembersOutsideDispatch);
	}

	[TestMethod]
	public async Task AttachAsync_ForAnAlreadyRegisteredInstance_ReportsAlreadyOpenWithoutOpeningTheViewAgain()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a");

		WorkspaceDocumentSnapshot snapshot = fixture.Document();

		fixture.Register(view, snapshot);

		WorkspaceViewCoordinatorOpenResult result = await fixture.Attacher.AttachAsync(view, TestHost.Opened(snapshot)).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen, result.Outcome);
		Assert.AreSame(snapshot, result.Snapshot);
		Assert.AreEqual(0, view.OpenCount);
		Assert.AreEqual(0, view.CloseCount);
	}

	[TestMethod]
	public async Task AttachAsync_WhenTheViewReportsItIsAttachedElsewhere_ReportsViewInUseAndLeavesTheViewOpen()
	{
		var fixture = new TestHost();
		var failure = new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.OpenFailed, "The view is attached to another document.");

		var view = new FakeWorkspaceView("a") { OpenResult = new(WorkspaceDocumentViewOpenOutcome.AlreadyOpen, failure) };
		WorkspaceDocumentSnapshot snapshot = fixture.Document();

		WorkspaceViewCoordinatorOpenResult result = await fixture.Attacher.AttachAsync(view, TestHost.Opened(snapshot)).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewInUse, result.Outcome);
		Assert.AreSame(snapshot, result.Snapshot);
		Assert.AreSame(failure, result.Failure);
		Assert.AreEqual(0, view.CloseCount);
		Assert.IsFalse(fixture.Tracker.Contains(view));
	}

	[TestMethod]
	public async Task AttachAsync_WhenTheViewGetsRegisteredWhileItOpens_ReportsAlreadyOpenAndKeepsTheRegistration()
	{
		var fixture = new TestHost();
		WorkspaceDocumentSnapshot snapshot = fixture.Document();
		var view = new FakeWorkspaceView("a") { OpenResult = new(WorkspaceDocumentViewOpenOutcome.AlreadyOpen) };

		// A concurrent attach of the same instance registers it between this attach's admission and the view's answer.
		view.OnMember = member =>
		{
			if (member == nameof(FakeWorkspaceView.Open))
				fixture.Register(view, snapshot);
		};

		WorkspaceViewCoordinatorOpenResult result = await fixture.Attacher.AttachAsync(view, TestHost.Opened(snapshot)).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen, result.Outcome);
		Assert.AreSame(snapshot, result.Snapshot);
		Assert.IsTrue(fixture.Tracker.Contains(view));
		Assert.AreEqual(0, view.CloseCount);
	}

	[TestMethod]
	public async Task AttachAsync_WhenTheViewDoesNotAcceptTheAttachment_ClosesItAndReportsTheViewFailure()
	{
		var fixture = new TestHost();
		var failure = new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.OpenFailed, "The view cannot show the document.");

		FakeWorkspaceView view = fixture.Probe.Watch(
			new FakeWorkspaceView("a") { OpenResult = new(WorkspaceDocumentViewOpenOutcome.Unavailable, failure) }
		);

		WorkspaceDocumentSnapshot snapshot = fixture.Document();

		WorkspaceViewCoordinatorOpenResult result = await fixture.Attacher.AttachAsync(view, TestHost.Opened(snapshot)).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewRejected, result.Outcome);
		Assert.AreSame(snapshot, result.Snapshot);
		Assert.AreSame(failure, result.Failure);
		Assert.AreEqual(1, view.CloseCount);
		Assert.IsFalse(fixture.Tracker.Contains(view));
		Assert.AreEqual(0, view.SubscriberCount);
		Assert.AreEqual(2, fixture.Probe.DispatchCount);
		Assert.IsEmpty(fixture.Probe.MembersOutsideDispatch);
	}

	[TestMethod]
	public async Task AttachAsync_WhenTheViewGivesNoReasonForRejecting_ReportsTheDefaultOpenFailure()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a") { OpenResult = new(WorkspaceDocumentViewOpenOutcome.Unavailable) };

		WorkspaceViewCoordinatorOpenResult result = await fixture
			.Attacher.AttachAsync(view, TestHost.Opened(fixture.Document()))
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewRejected, result.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.OpenFailed, result.Failure?.Code);
		Assert.AreEqual("The view did not accept the workspace document attachment.", result.Failure?.Message);
	}

	[TestMethod]
	public async Task AttachAsync_WhenTheViewReportsNoUsableId_ClosesItAndReportsViewRejected()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("   ");

		WorkspaceViewCoordinatorOpenResult result = await fixture
			.Attacher.AttachAsync(view, TestHost.Opened(fixture.Document()))
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewRejected, result.Outcome);
		Assert.AreEqual(UnreportedIdMessage, result.Failure?.Message);
		Assert.AreEqual(1, view.CloseCount);
		Assert.AreEqual(0, view.SubscriberCount);
		Assert.IsFalse(fixture.Tracker.Contains(view));
	}

	[TestMethod]
	public async Task AttachAsync_ForADifferentViewWithTheSameViewId_ClosesTheDuplicateAndLeavesTheRegisteredViewAlone()
	{
		var fixture = new TestHost();

		WorkspaceDocumentSnapshot snapshot = fixture.Document();

		var registered = new FakeWorkspaceView("dup");
		var duplicate = new FakeWorkspaceView("dup");

		fixture.Register(registered, snapshot);

		WorkspaceViewCoordinatorOpenResult result = await fixture.Attacher.AttachAsync(duplicate, TestHost.Opened(snapshot)).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewInUse, result.Outcome);
		Assert.AreSame(snapshot, result.Snapshot);
		Assert.AreEqual(1, duplicate.CloseCount);
		Assert.IsFalse(fixture.Tracker.Contains(duplicate));
		Assert.AreEqual(0, registered.CloseCount);
		Assert.IsTrue(fixture.Tracker.Contains(registered));
	}

	[TestMethod]
	public async Task AttachAsync_WhenTheViewOpenThrows_ClosesTheViewAndRethrows()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a") { ThrowOnOpen = true };

		WorkspaceDocumentSnapshot snapshot = fixture.Document();

		await Assert
			.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Attacher.AttachAsync(view, TestHost.Opened(snapshot)))
			.ConfigureAwait(false);

		Assert.AreEqual(1, view.CloseCount);
		Assert.IsFalse(fixture.Tracker.Contains(view));
	}

	[TestMethod]
	public async Task AttachAsync_WhenTheViewOpenThrowsAndTheCloseThrowsToo_RethrowsTheOpenFailure()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a") { ThrowOnOpen = true, ThrowOnClose = true };

		InvalidOperationException exception = await Assert
			.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Attacher.AttachAsync(view, TestHost.Opened(fixture.Document())))
			.ConfigureAwait(false);

		Assert.AreEqual("The open failed.", exception.Message);
		Assert.AreEqual(1, view.CloseCount);
	}

	[TestMethod]
	public async Task AttachAsync_WhenClosingARejectedViewThrows_StillReportsViewRejected()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a") { OpenResult = new(WorkspaceDocumentViewOpenOutcome.Unavailable), ThrowOnClose = true };

		WorkspaceViewCoordinatorOpenResult result = await fixture
			.Attacher.AttachAsync(view, TestHost.Opened(fixture.Document()))
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewRejected, result.Outcome);
		Assert.AreEqual(1, view.CloseCount);
	}

	[TestMethod]
	public async Task AttachAsync_WhenTheEventSubscriptionThrows_RemovesTheRegistrationClosesTheViewAndRethrows()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a") { ThrowOnAddHandler = true };

		WorkspaceDocumentSnapshot snapshot = fixture.Document();

		await Assert
			.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Attacher.AttachAsync(view, TestHost.Opened(snapshot)))
			.ConfigureAwait(false);

		Assert.IsFalse(fixture.Tracker.Contains(view));
		Assert.AreEqual(1, view.CloseCount);
		Assert.AreEqual(0, view.SubscriberCount);
	}

	[TestMethod]
	public async Task AttachAsync_WhenADetachWinsBeforeTheSubscription_UndoesTheSubscriptionAndReportsAlreadyOpen()
	{
		var fixture = new TestHost();
		WorkspaceDocumentSnapshot snapshot = fixture.Document();
		var view = new FakeWorkspaceView("a");

		view.OnAddHandler = () => fixture.Tracker.Remove(view);

		WorkspaceViewCoordinatorOpenResult result = await fixture.Attacher.AttachAsync(view, TestHost.Opened(snapshot)).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.IsNotNull(result.StoreResult);
		Assert.AreEqual(0, view.SubscriberCount);
		Assert.IsFalse(fixture.Tracker.Contains(view));
	}

	[TestMethod]
	public async Task AttachAsync_WhenADetachWinsAndTheUndoThrows_StillReportsAlreadyOpen()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a") { ThrowOnRemoveHandler = true };

		WorkspaceDocumentSnapshot snapshot = fixture.Document();

		view.OnAddHandler = () => fixture.Tracker.Remove(view);

		WorkspaceViewCoordinatorOpenResult result = await fixture.Attacher.AttachAsync(view, TestHost.Opened(snapshot)).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.IsFalse(fixture.Tracker.Contains(view));
	}

	[TestMethod]
	public async Task AttachAsync_WhenAReattachReplacesTheRegistration_KeepsTheHandlerOfTheNewRegistration()
	{
		var fixture = new TestHost();
		WorkspaceDocumentSnapshot snapshot = fixture.Document();
		var view = new FakeWorkspaceView("a");

		// The view is detached and attached again while the first attach is still subscribing; the second attach subscribes its own token.
		view.OnAddHandler = () =>
		{
			view.OnAddHandler = null;
			fixture.Tracker.Remove(view);

			ViewEntry reattached = fixture.Register(view, snapshot);

			view.ApplyRequested += reattached.Forwarder.Handler;
		};

		WorkspaceViewCoordinatorOpenResult result = await fixture.Attacher.AttachAsync(view, TestHost.Opened(snapshot)).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen, result.Outcome);
		Assert.AreSame(snapshot, result.Snapshot);
		Assert.IsTrue(fixture.Tracker.Contains(view));
		Assert.AreEqual(1, view.SubscriberCount);
	}

	[TestMethod]
	public void Detach_RemovesTheRegistrationAndUnsubscribesItsOwnHandler()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a");

		ViewEntry registration = fixture.Register(view, TestSnapshots.Create("doc", content: "content"));

		view.ApplyRequested += registration.Forwarder.Handler;

		fixture.Attacher.Detach(view);
		fixture.Attacher.Detach(view);

		Assert.IsFalse(fixture.Tracker.Contains(view));
		Assert.AreEqual(0, view.SubscriberCount);
	}

	[TestMethod]
	public void Detach_WhenTheEventAccessorThrows_StillRemovesTheRegistration()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a");

		ViewEntry registration = fixture.Register(view, TestSnapshots.Create("doc", content: "content"));

		view.ApplyRequested += registration.Forwarder.Handler;
		view.ThrowOnRemoveHandler = true;

		fixture.Attacher.Detach(view);

		Assert.IsFalse(fixture.Tracker.Contains(view));
	}

	[TestMethod]
	public void CloseDeletedView_DetachesTheViewAndClosesIt()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a");

		List<WorkspaceDocumentViewSynchronizationIssue> issues = [];

		fixture.Register(view, TestSnapshots.Create("doc", content: "content"));
		fixture.Attacher.CloseDeletedView(view, issues);

		Assert.IsFalse(fixture.Tracker.Contains(view));
		Assert.AreEqual(1, view.CloseCount);
		Assert.IsEmpty(issues);
	}

	[TestMethod]
	public void CloseDeletedView_WhenTheCloseThrows_StillDetachesAndReportsTheView()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a") { ThrowOnClose = true };

		List<WorkspaceDocumentViewSynchronizationIssue> issues = [];

		fixture.Register(view, TestSnapshots.Create("doc", content: "content"));
		fixture.Attacher.CloseDeletedView(view, issues);

		Assert.IsFalse(fixture.Tracker.Contains(view));
		Assert.AreEqual("a", issues.Single().ViewId);
	}

	[TestMethod]
	public void CloseDeletedView_WhenTheCloseThrows_AttachesTheExceptionToTheIssue()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a") { ThrowOnClose = true };

		List<WorkspaceDocumentViewSynchronizationIssue> issues = [];

		fixture.Register(view, TestSnapshots.Create("doc", content: "content"));
		fixture.Attacher.CloseDeletedView(view, issues);

		WorkspaceOperationFailure? failure = issues.Single().Failure;

		Assert.IsNotNull(failure);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewCloseFailed, failure.Code);
		Assert.AreEqual("The close failed.", failure.Message);
		Assert.IsInstanceOfType<InvalidOperationException>(failure.Exception);
	}

	[TestMethod]
	public void CloseLateAttachedViews_ClosesOnlyTheViewsAttachedToTheDeletedInstance()
	{
		var fixture = new TestHost();
		var deletedKey = new WorkspaceDocumentKey(Guid.NewGuid());
		var reopenedKey = new WorkspaceDocumentKey(Guid.NewGuid());
		var attachedToDeleted = new FakeWorkspaceView("deleted") { DocumentKey = deletedKey };
		var attachedToReopened = new FakeWorkspaceView("reopened") { DocumentKey = reopenedKey };
		var withoutKey = new FakeWorkspaceView("without-key") { DocumentKey = null };

		fixture.Register(attachedToDeleted, TestSnapshots.Create("doc", content: "content", documentKey: deletedKey));
		fixture.Register(attachedToReopened, TestSnapshots.Create("doc", content: "content", documentKey: reopenedKey));
		fixture.Register(withoutKey, TestSnapshots.Create("doc", content: "content"));

		fixture.Attacher.CloseLateAttachedViews("doc", deletedKey, []);

		Assert.AreEqual(1, attachedToDeleted.CloseCount);
		Assert.AreEqual(0, attachedToReopened.CloseCount);
		Assert.AreEqual(1, withoutKey.CloseCount);
		Assert.IsTrue(fixture.Tracker.Contains(attachedToReopened));
		Assert.IsFalse(fixture.Tracker.Contains(attachedToDeleted));
		Assert.IsFalse(fixture.Tracker.Contains(withoutKey));
	}
}
