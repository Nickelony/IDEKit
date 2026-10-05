using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceViewCoordinatorTests
{
	[TestMethod]
	public async Task OpenWithView_DispatchesViewAccessThroughTheDelegate()
	{
		await using var fixture = new CoordinatorTestHost();

		await fixture.OpenViewAsync();

		// The attach runs inside a dispatched action: the view observes the dispatch context while it
		// adopts the loaded snapshot.
		Assert.IsTrue(fixture.View.ReceivedSnapshotInDispatch);
		Assert.AreEqual("initial", fixture.View.Text);
	}

	[TestMethod]
	public async Task OpenWithoutView_ReturnsCoordinatorResultWithSnapshot()
	{
		await using var fixture = new CoordinatorTestHost();

		WorkspaceViewCoordinatorOpenResult opened = await fixture.Coordinator.OpenAsync(
			fixture.DocumentPath,
			s_openOptions,
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Opened, opened.Outcome);
		Assert.IsNotNull(opened.Snapshot);
		Assert.AreEqual("initial", opened.Snapshot.Content);
		Assert.IsNull(opened.Failure);

		// A second open reports the store's already-open state through the same result family.
		WorkspaceViewCoordinatorOpenResult reopened = await fixture.Coordinator.OpenAsync(
			fixture.DocumentPath,
			s_openOptions,
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen, reopened.Outcome);
		Assert.IsNotNull(reopened.Snapshot);
	}

	[TestMethod]
	public async Task OpenWithoutView_InvalidPath_ReturnsCoordinatorFailureOutcome()
	{
		await using var fixture = new CoordinatorTestHost();

		WorkspaceViewCoordinatorOpenResult result = await fixture.Coordinator.OpenAsync(null, s_openOptions, TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.InvalidPath, result.Outcome);
		Assert.IsNull(result.Snapshot);
	}

	[TestMethod]
	public async Task DetachView_NullViewIsAnArgumentError()
	{
		await using var fixture = new CoordinatorTestHost();
		Assert.ThrowsExactly<ArgumentNullException>(() => fixture.Coordinator.DetachView(null!));
	}

	[TestMethod]
	public async Task OpenWithView_NullViewThrowsSynchronously()
	{
		await using var fixture = new CoordinatorTestHost();

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			fixture.Coordinator.OpenWithViewAsync(
				filePath: fixture.DocumentPath,
				options: s_openOptions,
				view: null!,
				cancellationToken: TestContext.CancellationToken
			)
		);
	}

	[TestMethod]
	public async Task OpenWithView_NullOptionsThrowSynchronously()
	{
		await using var fixture = new CoordinatorTestHost();

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			fixture.Coordinator.OpenWithViewAsync(
				filePath: fixture.DocumentPath,
				options: null!,
				view: fixture.View,
				cancellationToken: TestContext.CancellationToken
			)
		);
	}

	[TestMethod]
	public async Task Open_NullOptionsOfAStoppedCoordinator_AreAnArgumentErrorBeforeTheStopIsChecked()
	{
		await using var fixture = new CoordinatorTestHost();

		await fixture.Coordinator.StopAsync();

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
			fixture.Coordinator.OpenAsync(fixture.DocumentPath, null!, TestContext.CancellationToken)
		);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			fixture.Coordinator.OpenWithViewAsync(
				filePath: fixture.DocumentPath,
				options: null!,
				view: fixture.View,
				cancellationToken: TestContext.CancellationToken
			)
		);
	}

	[TestMethod]
	public async Task OpenWithView_InvalidPathAndPendingEditsAreRejectedBeforeOpening()
	{
		await using var fixture = new CoordinatorTestHost();

		WorkspaceViewCoordinatorOpenResult invalidPath = await fixture.Coordinator.OpenWithViewAsync(
			filePath: "   ",
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		fixture.View.HasPendingEdits = true;

		WorkspaceViewCoordinatorOpenResult conflict = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.InvalidPath, invalidPath.Outcome);
		Assert.IsNull(invalidPath.Snapshot);
		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewUnavailable, conflict.Outcome);
		Assert.IsNull(conflict.Snapshot);
		Assert.AreEqual(0, fixture.View.CloseCount);
	}

	[TestMethod]
	public async Task OpenWithView_ViewWithAConflictIsUnavailable()
	{
		await using var fixture = new CoordinatorTestHost();
		fixture.View.HasConflict = true;

		WorkspaceViewCoordinatorOpenResult result = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		// An unresolved conflict is a state-based unavailability input on its own, so the view is rejected
		// before the document is loaded and is never closed (it was never attached).
		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewUnavailable, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(0, fixture.View.CloseCount);
	}

	[TestMethod]
	public async Task OpenWithView_ViewAlreadyBoundToADocumentIsUnavailable()
	{
		await using var fixture = new CoordinatorTestHost();
		fixture.View.DocumentKeyOverride = new WorkspaceDocumentKey(Guid.NewGuid());

		WorkspaceViewCoordinatorOpenResult result = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		// A view that already reports a document is unavailable for a different one, so the coordinator must not
		// load the requested document for it.
		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewUnavailable, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(0, fixture.View.CloseCount);
	}

	[TestMethod]
	public async Task OpenWithView_SameRegisteredInstanceReportsAlreadyOpen()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot opened = await fixture.OpenViewAsync();

		WorkspaceViewCoordinatorOpenResult result = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		// A view that is already registered is a no-op: the view is not attached a second time, and
		// the result carries the snapshot of the document the view is attached to.
		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen, result.Outcome);
		Assert.IsNotNull(result.Snapshot);
		Assert.AreEqual(opened.DocumentId, result.Snapshot.DocumentId);
		Assert.AreEqual("initial", result.Snapshot.Content);
		Assert.AreEqual(1, fixture.View.OpenCount, "A registered view is not attached a second time.");
	}

	[TestMethod]
	public async Task OpenWithView_DifferentViewWithDuplicateViewIdReportsViewInUse()
	{
		await using var fixture = new CoordinatorTestHost();
		await fixture.OpenViewAsync();
		var duplicate = new FakeCoordinatorView(fixture, fixture.View.ViewId);

		WorkspaceViewCoordinatorOpenResult result = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: duplicate,
			cancellationToken: TestContext.CancellationToken
		);

		// A different instance that duplicates a registered view id cannot be attached.
		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewInUse, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(0, duplicate.CloseCount);
	}

	[TestMethod]
	[DataRow(
		WorkspaceDocumentViewOpenOutcome.AlreadyOpen,
		WorkspaceViewCoordinatorOpenOutcome.ViewInUse,
		"ViewElsewhere",
		"The view is attached to another document.",
		DisplayName = "A view that reports it is attached elsewhere is rejected as in use"
	)]
	[DataRow(
		WorkspaceDocumentViewOpenOutcome.Unavailable,
		WorkspaceViewCoordinatorOpenOutcome.ViewRejected,
		"AttachRejected",
		"Attach rejected.",
		DisplayName = "A view that rejects the attach is rejected"
	)]
	public async Task OpenWithView_AViewThatDoesNotAttach_ReportsItsOutcomeAndPassesItsFailureThrough(
		WorkspaceDocumentViewOpenOutcome viewOutcome,
		WorkspaceViewCoordinatorOpenOutcome expected,
		string failureCode,
		string failureMessage
	)
	{
		await using var fixture = new CoordinatorTestHost();
		fixture.View.OpenOutcome = viewOutcome;
		fixture.View.OpenFailure = new WorkspaceOperationFailure(failureCode, failureMessage);

		WorkspaceViewCoordinatorOpenResult result = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		// The document was loaded, but the view does not take the attach; the snapshot is carried so
		// the caller can decide what to do with the loaded content, and the view's own failure detail
		// is passed through. The coordinator consumes the attach result that the dispatched action
		// captured after the delegate's task completes, so a delegate that completed before running
		// the action would leave it unset and surface a different failure.
		Assert.AreEqual(expected, result.Outcome);
		Assert.IsNotNull(result.Snapshot);
		Assert.AreEqual(failureMessage, result.Failure!.Message);
	}

	[TestMethod]
	public async Task OpenWithView_MissingPathWithoutCreateIfMissingReportsNotFound()
	{
		await using var fixture = new CoordinatorTestHost();

		string missingPath = Path.Combine(fixture.DirectoryPath, "missing.txt");

		WorkspaceDocumentOpenOptions options = new(
			[TextEncoding.Utf8],
			new TextFileFormat(TextEncoding.Utf8, false, LineTerminatorStyle.Lf),
			CreateIfMissing: false
		);

		WorkspaceViewCoordinatorOpenResult result = await fixture.Coordinator.OpenWithViewAsync(
			filePath: missingPath,
			options,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.NotFound, result.Outcome);
		Assert.IsNull(result.Snapshot);
	}

	[TestMethod]
	public async Task OpenWithView_UnexpectedViewExceptionReportsOpenFailedWithoutSnapshot()
	{
		await using var fixture = new CoordinatorTestHost();
		fixture.View.ThrowOnOpen = true;

		WorkspaceViewCoordinatorOpenResult result = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		// An unexpected exception on the open path is an open failure, unlike a view that reports a
		// rejection outcome; the partially attached view is closed again.
		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.OpenFailed, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.OpenFailed, result.Failure!.Code);
		Assert.AreEqual(1, fixture.View.CloseCount);
	}

	[TestMethod]
	public async Task OpenWithView_LoadFailurePropagatesFailureCode()
	{
		await using var fixture = new CoordinatorTestHost();
		string invalidPath = Path.Combine(fixture.DirectoryPath, "invalid.lua");
		File.WriteAllBytes(invalidPath, [0xC3, 0x28]);

		WorkspaceViewCoordinatorOpenResult result = await fixture.Coordinator.OpenWithViewAsync(
			filePath: invalidPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.LoadFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.InvalidEncoding, result.Failure!.Code);
		Assert.IsNull(result.Snapshot);
	}

	[TestMethod]
	public async Task OpenWithView_CanceledTokenReportsCanceled()
	{
		await using var fixture = new CoordinatorTestHost();
		using var cancellation = new CancellationTokenSource();

		cancellation.Cancel();

		WorkspaceViewCoordinatorOpenResult result = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: cancellation.Token
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Canceled, result.Outcome);
		Assert.IsNull(result.Snapshot);
	}

	[TestMethod]
	public async Task OpenWithView_FailingEventSubscriptionRollsBackRegistration()
	{
		await using var fixture = new CoordinatorTestHost();
		fixture.View.ThrowOnSubscribe = true;

		WorkspaceViewCoordinatorOpenResult failed = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.OpenFailed, failed.Outcome);
		Assert.AreEqual(1, fixture.View.CloseCount);

		// The failed registration is rolled back, so attaching the same view succeeds once the
		// subscription works instead of reporting the view as already open or in use.
		fixture.View.ThrowOnSubscribe = false;

		WorkspaceViewCoordinatorOpenResult opened = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Opened, opened.Outcome);
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task OpenWithView_DetachedBetweenRegistrationAndSubscription_LeavesNoSubscription()
	{
		await using var fixture = new CoordinatorTestHost();
		fixture.View.SubscribeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		fixture.View.SubscribeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<WorkspaceViewCoordinatorOpenResult> open = fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		// The registration is published before the subscription add runs; park inside the add so the
		// detach removes the registration and runs its own `-=` first, before the add lands.
		await fixture.View.SubscribeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
		fixture.Coordinator.DetachView(fixture.View);
		fixture.View.SubscribeGate.SetResult();

		WorkspaceViewCoordinatorOpenResult result = await open;

		// The coordinator re-checks the registration after the add and undoes the subscription, so no handler
		// keeps the detached view alive.
		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(0, fixture.View.ApplySubscriberCount);
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task OpenWithView_ReattachAfterDetachWhileSubscribing_KeepsOnlyTheLiveSubscription()
	{
		await using var fixture = new CoordinatorTestHost();
		var firstGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		fixture.View.SubscribeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		fixture.View.SubscribeGate = firstGate;

		// The first attach registers the view and parks inside the subscription add. It is started on the
		// pool so the parked add cannot block the test thread.
		Task<WorkspaceViewCoordinatorOpenResult> first = Task.Run(() =>
			fixture.Coordinator.OpenWithViewAsync(
				filePath: fixture.DocumentPath,
				options: s_openOptions,
				view: fixture.View,
				cancellationToken: TestContext.CancellationToken
			)
		);

		await fixture.View.SubscribeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);

		// The detach removes the registration and runs its own removal before the parked add lands.
		// The host then closes the view, and a re-attach of the same instance registers it again and
		// subscribes with its own token; the re-attach must not park.
		fixture.Coordinator.DetachView(fixture.View);
		fixture.View.Close();
		fixture.View.SubscribeGate = null;

		WorkspaceViewCoordinatorOpenResult second = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Opened, second.Outcome);

		// Releasing the first add lets the abandoned attach finish. Its undo removes its own token by
		// identity, so it cannot strip the re-attach's live subscription.
		firstGate.SetResult();

		WorkspaceViewCoordinatorOpenResult firstResult = await first;

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen, firstResult.Outcome);
		Assert.AreEqual(1, fixture.View.ApplySubscriberCount, "Only the re-attach's subscription stays live.");

		// The one remaining subscription is the re-attach's own token, so detaching removes it and
		// leaves nothing behind; a leaked first-attach handler would survive this removal.
		fixture.Coordinator.DetachView(fixture.View);
		Assert.AreEqual(0, fixture.View.ApplySubscriberCount);
	}

	[TestMethod]
	public async Task OpenWithView_ViewWhoseIdGetterThrowsIsUnavailable()
	{
		await using var fixture = new CoordinatorTestHost();
		fixture.View.ViewIdProvider = () => throw new InvalidOperationException("The view cannot report its id.");

		WorkspaceViewCoordinatorOpenResult result = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewUnavailable, result.Outcome);
		Assert.AreEqual(0, fixture.View.CloseCount);
	}

	[TestMethod]
	[DataRow("")]
	[DataRow("   ")]
	public async Task OpenWithView_ViewWithBlankIdIsUnavailable(string viewId)
	{
		await using var fixture = new CoordinatorTestHost();
		fixture.View.ViewIdProvider = () => viewId;

		WorkspaceViewCoordinatorOpenResult result = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		// A blank or whitespace id cannot be indexed for duplicate detection, so the view is rejected
		// before the document is loaded, with the same failure the attach path reports.
		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewUnavailable, result.Outcome);
		Assert.AreEqual(0, fixture.View.CloseCount);
		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.OpenFailed, result.Failure!.Code);
		Assert.AreEqual("The view did not report a view id.", result.Failure.Message);
	}

	[TestMethod]
	public async Task OpenWithView_ViewThatLosesItsIdDuringOpenIsRejectedAndClosed()
	{
		await using var fixture = new CoordinatorTestHost();
		int viewIdReads = 0;
		fixture.View.ViewIdProvider = () => ++viewIdReads == 1 ? "live-view" : string.Empty;

		WorkspaceViewCoordinatorOpenResult result = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		// The id was present when the view was validated but gone after the attach; the view cannot be
		// indexed for duplicate detection, so it is rejected and closed.
		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewRejected, result.Outcome);
		Assert.AreEqual(1, fixture.View.CloseCount);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.OpenFailed, result.Failure!.Code);
		Assert.AreEqual("The view did not report a view id.", result.Failure.Message);
	}

	[TestMethod]
	public async Task DetachView_RemovesBlockingStateAndIsIdempotent()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.HasPendingEdits = true;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult> blocked = await fixture.Coordinator.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)),
			TestContext.CancellationToken
		);

		fixture.Coordinator.DetachView(fixture.View);
		fixture.Coordinator.DetachView(fixture.View);

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult> reloaded = await fixture.Coordinator.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)),
			TestContext.CancellationToken
		);

		Assert.IsNull(blocked.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, blocked.ViewSynchronization.Outcome);
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Unchanged, reloaded.StoreResult?.Outcome);
	}

	[TestMethod]
	public async Task StopAsync_DetachesTheApplyRequestedForwarder()
	{
		await using var fixture = new CoordinatorTestHost();
		await fixture.OpenViewAsync();

		await fixture.Coordinator.StopAsync();

		Assert.AreEqual(0, fixture.View.ApplySubscriberCount, "The stop path must remove the apply handler.");
	}

	[TestMethod]
	public async Task OpenAsync_EveryStoreOpenOutcomeIsProducedAndMapped()
	{
		await using var fixture = new CoordinatorTestHost();

		// The cases below produce every WorkspaceDocumentOpenOutcome value; the enumeration assertion
		// fails as soon as the store gains an outcome that is not covered here, which keeps the
		// coordinator's explicit outcome mapping total.
		List<(WorkspaceDocumentOpenOutcome Store, WorkspaceViewCoordinatorOpenOutcome Coordinator)> observed = [];

		WorkspaceViewCoordinatorOpenResult opened = await fixture.Coordinator.OpenAsync(
			fixture.DocumentPath,
			s_openOptions,
			TestContext.CancellationToken
		);

		observed.Add((WorkspaceDocumentOpenOutcome.Opened, opened.Outcome));

		WorkspaceViewCoordinatorOpenResult reopened = await fixture.Coordinator.OpenAsync(
			fixture.DocumentPath,
			s_openOptions,
			TestContext.CancellationToken
		);

		observed.Add((WorkspaceDocumentOpenOutcome.AlreadyOpen, reopened.Outcome));

		WorkspaceViewCoordinatorOpenResult invalidPath = await fixture.Coordinator.OpenAsync("   ", s_openOptions, TestContext.CancellationToken);
		observed.Add((WorkspaceDocumentOpenOutcome.InvalidPath, invalidPath.Outcome));

		string missingPath = Path.Combine(fixture.DirectoryPath, "missing.txt");

		WorkspaceDocumentOpenOptions withoutCreate = new(
			[TextEncoding.Utf8],
			new TextFileFormat(TextEncoding.Utf8, false, LineTerminatorStyle.Lf),
			CreateIfMissing: false
		);

		WorkspaceViewCoordinatorOpenResult notFound = await fixture.Coordinator.OpenAsync(missingPath, withoutCreate, TestContext.CancellationToken);

		observed.Add((WorkspaceDocumentOpenOutcome.NotFound, notFound.Outcome));

		string directoryPath = Path.Combine(fixture.DirectoryPath, "subdirectory");
		Directory.CreateDirectory(directoryPath);

		WorkspaceViewCoordinatorOpenResult isDirectory = await fixture.Coordinator.OpenAsync(
			directoryPath,
			s_openOptions,
			TestContext.CancellationToken
		);

		observed.Add((WorkspaceDocumentOpenOutcome.IsDirectory, isDirectory.Outcome));

		string undecodablePath = Path.Combine(fixture.DirectoryPath, "invalid.lua");
		File.WriteAllBytes(undecodablePath, [0xC3, 0x28]);

		WorkspaceViewCoordinatorOpenResult loadFailed = await fixture.Coordinator.OpenAsync(
			undecodablePath,
			s_openOptions,
			TestContext.CancellationToken
		);

		observed.Add((WorkspaceDocumentOpenOutcome.LoadFailed, loadFailed.Outcome));

		string canceledPath = Path.Combine(fixture.DirectoryPath, "canceled.txt");
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		WorkspaceViewCoordinatorOpenResult canceled = await fixture.Coordinator.OpenAsync(canceledPath, s_openOptions, cancellation.Token);
		observed.Add((WorkspaceDocumentOpenOutcome.Canceled, canceled.Outcome));

		Assert.AreSequenceEqual(
			expected: Enum.GetValues<WorkspaceDocumentOpenOutcome>(),
			actual: [.. observed.Select(pair => pair.Store)],
			order: Microsoft.VisualStudio.TestTools.UnitTesting.SequenceOrder.InAnyOrder,
			message: "Every store open outcome must be produced by a case in this test."
		);

		// The coordinator outcome names mirror the store outcome names for every shared value, so a
		// mapping that silently degrades into OpenFailed fails here.
		foreach ((WorkspaceDocumentOpenOutcome store, WorkspaceViewCoordinatorOpenOutcome coordinator) in observed)
			Assert.AreEqual(store.ToString(), coordinator.ToString(), $"{store} must map to the coordinator outcome of the same name.");
	}

	[TestMethod]
	public async Task OpenAsync_MissingOptionsThrowInsteadOfProducingAnOpenFailedOutcome()
	{
		await using var fixture = new CoordinatorTestHost();

		// The store contract treats missing options as an argument error; both open paths keep that
		// contract instead of degrading the error into an outcome.
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
			fixture.Coordinator.OpenAsync(fixture.DocumentPath, null!, TestContext.CancellationToken)
		);

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
			fixture.Coordinator.OpenWithViewAsync(
				filePath: fixture.DocumentPath,
				options: null!,
				view: fixture.View,
				cancellationToken: TestContext.CancellationToken
			)
		);
	}

	[TestMethod]
	public async Task DeferredDispatch_CompletesBeforeTheCoordinatorConsumesViewResults()
	{
		await using var fixture = new CoordinatorTestHost(deferredDispatch: true);
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// The delegate completes asynchronously; the coordinator awaits the task instead of blocking, so
		// the view results are still consumed after the action ran.
		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult> replaced = await fixture.Coordinator.ReplaceAsync(
			new WorkspaceDocumentReplaceRequest(
				new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
				"deferred edit",
				snapshot.FileFormat
			)
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.StoreResult.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, replaced.ViewSynchronization.Outcome);
		Assert.AreEqual("deferred edit", fixture.View.Text);
	}

	[TestMethod]
	public async Task OpenWithView_ThrowingDispatchDelegateReportsOpenFailed()
	{
		await using var fixture = new CoordinatorTestHost(throwingDispatch: true);

		WorkspaceViewCoordinatorOpenResult result = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		// The delegate contract requires the action to run; a delegate that faults before running it
		// cannot be interpreted as a view rejection, so the open path reports an open failure with the
		// delegate's detail.
		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.OpenFailed, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.OpenFailed, result.Failure!.Code);
	}

	[TestMethod]
	public async Task OpenWithView_ReportsTheDocumentAuthorityOutcome()
	{
		await using var fixture = new CoordinatorTestHost();

		string freshPath = Path.Combine(fixture.DirectoryPath, "fresh.txt");

		// A fresh open carries the store outcome; an already-open document that merely receives the
		// view keeps that distinction visible through the store outcome.
		WorkspaceViewCoordinatorOpenResult fresh = await fixture.Coordinator.OpenAsync(freshPath, s_openOptions, TestContext.CancellationToken);
		WorkspaceDocumentSnapshot initial = await fixture.OpenViewAsync();

		var secondView = new FakeCoordinatorView(fixture, "second-view");

		WorkspaceViewCoordinatorOpenResult attached = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: secondView,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Opened, fresh.Outcome);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, fresh.StoreResult!.Outcome);
		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.Opened, attached.Outcome);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.AlreadyOpen, attached.StoreResult!.Outcome);
		Assert.AreEqual(initial.DocumentId, attached.Snapshot!.DocumentId);

		// The short-circuit paths answer before the load, so no store outcome exists.
		WorkspaceViewCoordinatorOpenResult repeated = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen, repeated.Outcome);
		Assert.IsNull(repeated.StoreResult);
	}

	[TestMethod]
	public async Task OpenWithView_DirectoryPathReportsIsDirectory()
	{
		await using var fixture = new CoordinatorTestHost();
		string directoryPath = Path.Combine(fixture.DirectoryPath, "subdirectory");
		Directory.CreateDirectory(directoryPath);

		WorkspaceViewCoordinatorOpenResult result = await fixture.Coordinator.OpenWithViewAsync(
			filePath: directoryPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.IsDirectory, result.Outcome);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.IsDirectory, result.StoreResult!.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(0, fixture.View.OpenCount);
		Assert.AreEqual(0, fixture.View.CloseCount);
	}

	[TestMethod]
	public async Task OpenWithView_ViewWithDocumentKeyOrConflictIsUnavailable()
	{
		await using var fixture = new CoordinatorTestHost();
		var loadedView = new FakeCoordinatorView(fixture, "loaded-view") { DocumentKeyOverride = new WorkspaceDocumentKey(Guid.NewGuid()) };
		var conflictedView = new FakeCoordinatorView(fixture, "conflicted-view") { HasConflict = true };

		WorkspaceViewCoordinatorOpenResult loaded = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: loadedView,
			cancellationToken: TestContext.CancellationToken
		);

		WorkspaceViewCoordinatorOpenResult conflicted = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: conflictedView,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewUnavailable, loaded.Outcome);
		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewUnavailable, conflicted.Outcome);
		Assert.AreEqual(0, loadedView.OpenCount);
		Assert.AreEqual(0, conflictedView.OpenCount);
	}

	[TestMethod]
	public async Task OpenWithView_ViewIdThatThrowsAfterValidationReportsOpenFailed()
	{
		await using var fixture = new CoordinatorTestHost();
		int viewIdReads = 0;
		fixture.View.ViewIdProvider = () => ++viewIdReads == 1 ? "live-view" : throw new InvalidOperationException("The view cannot report its id.");

		WorkspaceViewCoordinatorOpenResult result = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		// The attach path reads the id again; a throwing getter is an unexpected exception on the
		// attach path, so the open fails and the partially attached view is closed again.
		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.OpenFailed, result.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.OpenFailed, result.Failure!.Code);
		Assert.AreEqual(1, fixture.View.CloseCount);
	}

	[TestMethod]
	public async Task OpenWithView_RegisteredViewWhoseDocumentIsGoneReportsAlreadyOpenWithoutSnapshot()
	{
		await using var fixture = new CoordinatorTestHost();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// The document is removed behind the coordinator's back, which is what another coordinator over the
		// same store produces; the registered view stays registered and the open is a no-op without a
		// snapshot.
		WorkspaceDocumentDeleteResult deleted = await fixture.Store.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, deleted.Outcome);

		WorkspaceViewCoordinatorOpenResult result = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.IsNull(result.Failure);
		Assert.AreEqual(1, fixture.View.OpenCount);
	}

	[TestMethod]
	public async Task OpenAsync_StoreFailureFaultsTheTask()
	{
		await using var fixture = new CoordinatorTestHost();
		await fixture.Store.DisposeAsync();

		// Unlike OpenWithViewAsync, the view-less open path has no outcome for an unexpected store
		// failure: the exception surfaces on the returned task.
		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
			fixture.Coordinator.OpenAsync(fixture.DocumentPath, s_openOptions, TestContext.CancellationToken)
		);
	}

	[TestMethod]
	public async Task OpenWithView_ViewRejectionWithoutFailureReportsSynthesizedDetail()
	{
		await using var fixture = new CoordinatorTestHost();
		fixture.View.OpenOutcome = WorkspaceDocumentViewOpenOutcome.Unavailable;

		WorkspaceViewCoordinatorOpenResult result = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		// The view rejected the attach without supplying a failure detail, so the coordinator synthesizes
		// the explanation and still closes the rejected view.
		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewRejected, result.Outcome);
		Assert.IsNotNull(result.Snapshot);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.OpenFailed, result.Failure!.Code);
		Assert.AreEqual("The view did not accept the workspace document attachment.", result.Failure.Message);
		Assert.AreEqual(1, fixture.View.CloseCount);
	}

	[TestMethod]
	public async Task OpenWithView_ThrowingStateProbeReportsTheExceptionDetail()
	{
		await using var fixture = new CoordinatorTestHost();
		fixture.View.ThrowOnStateProbe = true;

		WorkspaceViewCoordinatorOpenResult result = await fixture.Coordinator.OpenWithViewAsync(
			filePath: fixture.DocumentPath,
			options: s_openOptions,
			view: fixture.View,
			cancellationToken: TestContext.CancellationToken
		);

		// A view that cannot report its availability is unavailable, and the exception detail is
		// surfaced instead of being swallowed.
		Assert.AreEqual(WorkspaceViewCoordinatorOpenOutcome.ViewUnavailable, result.Outcome);
		Assert.AreEqual(0, fixture.View.CloseCount);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.OpenFailed, result.Failure!.Code);
		Assert.AreEqual("The view cannot report its pending-edit state.", result.Failure.Message);
	}

	[TestMethod]
	public async Task OpenWithView_DisposedStoreFaultsTheTask()
	{
		await using var fixture = new CoordinatorTestHost();
		await fixture.Store.DisposeAsync();

		// A store that fails outside the coordinator's outcome vocabulary faults the task, matching the
		// view-less open path.
		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
			fixture.Coordinator.OpenWithViewAsync(
				filePath: fixture.DocumentPath,
				options: s_openOptions,
				view: fixture.View,
				cancellationToken: TestContext.CancellationToken
			)
		);
	}
}
