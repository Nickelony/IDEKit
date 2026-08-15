using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

public sealed partial class WorkspaceDocumentManagerTests
{
	[TestMethod]
	public async Task OpenWithView_DispatchesViewAccessThroughTheDelegate()
	{
		await using var fixture = new ManagerFixture();

		await fixture.OpenViewAsync();

		// The attach runs inside a dispatched action: the view observes the dispatch context while it
		// adopts the loaded snapshot.
		Assert.IsTrue(fixture.View.ReceivedSnapshotInDispatch);
		Assert.AreEqual("initial", fixture.View.Text);
	}

	[TestMethod]
	public async Task OpenWithoutView_ReturnsManagerResultWithSnapshot()
	{
		await using var fixture = new ManagerFixture();

		WorkspaceDocumentManagerOpenResult opened = await fixture.Manager.OpenAsync(fixture.DocumentPath, s_openOptions);

		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.Opened, opened.Outcome);
		Assert.IsNotNull(opened.Snapshot);
		Assert.AreEqual("initial", opened.Snapshot.Content);
		Assert.IsNull(opened.Failure);

		// A second open reports the store's already-open state through the same result family.
		WorkspaceDocumentManagerOpenResult reopened = await fixture.Manager.OpenAsync(fixture.DocumentPath, s_openOptions);

		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.AlreadyOpen, reopened.Outcome);
		Assert.IsNotNull(reopened.Snapshot);
	}

	[TestMethod]
	public async Task OpenWithoutView_InvalidPath_ReturnsManagerFailureOutcome()
	{
		await using var fixture = new ManagerFixture();

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenAsync(null, s_openOptions);

		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.InvalidPath, result.Outcome);
		Assert.IsNull(result.Snapshot);
	}

	[TestMethod]
	public async Task UnregisterOpenView_NullViewIsAnArgumentError()
	{
		await using var fixture = new ManagerFixture();

		Assert.ThrowsExactly<ArgumentNullException>(() => fixture.Manager.UnregisterOpenView(null!));
	}

	[TestMethod]
	public async Task OpenWithView_NullViewThrowsSynchronously()
	{
		await using var fixture = new ManagerFixture();

		Assert.ThrowsExactly<ArgumentNullException>(() => fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			null!));
	}

	[TestMethod]
	public async Task OpenWithView_InvalidPathAndPendingEditsAreRejectedBeforeOpening()
	{
		await using var fixture = new ManagerFixture();

		WorkspaceDocumentManagerOpenResult invalidPath = await fixture.Manager.OpenWithViewAsync(
			"   ",
			s_openOptions,
			fixture.View);
		fixture.View.HasPendingEdits = true;
		WorkspaceDocumentManagerOpenResult conflict = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.InvalidPath, invalidPath.Outcome);
		Assert.IsNull(invalidPath.Snapshot);
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.ViewUnavailable, conflict.Outcome);
		Assert.IsNull(conflict.Snapshot);
		Assert.AreEqual(0, fixture.View.CloseCount);
	}

	[TestMethod]
	public async Task OpenWithView_ViewWithAConflictIsUnavailable()
	{
		await using var fixture = new ManagerFixture();
		fixture.View.HasConflict = true;

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		// An unresolved conflict is a state-based unavailability input on its own, so the view is rejected
		// before the document is loaded and is never closed (it was never attached).
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.ViewUnavailable, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(0, fixture.View.CloseCount);
	}

	[TestMethod]
	public async Task OpenWithView_ViewAlreadyBoundToADocumentIsUnavailable()
	{
		await using var fixture = new ManagerFixture();
		fixture.View.DocumentKeyOverride = new WorkspaceDocumentKey(Guid.NewGuid());

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		// A view that already reports a document is unavailable for a different one, so the manager must not
		// load the requested document for it.
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.ViewUnavailable, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(0, fixture.View.CloseCount);
	}

	[TestMethod]
	public async Task OpenWithView_SameRegisteredInstanceReportsAlreadyOpen()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot opened = await fixture.OpenViewAsync();

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		// A view that is already registered is a no-op: the view is not attached a second time, and
		// the result carries the snapshot of the document the view is attached to.
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.AlreadyOpen, result.Outcome);
		Assert.IsNotNull(result.Snapshot);
		Assert.AreEqual(opened.DocumentId, result.Snapshot.DocumentId);
		Assert.AreEqual("initial", result.Snapshot.Content);
		Assert.AreEqual(1, fixture.View.OpenCount, "A registered view is not attached a second time.");
	}

	[TestMethod]
	public async Task OpenWithView_DifferentViewWithDuplicateViewIdReportsViewInUse()
	{
		await using var fixture = new ManagerFixture();
		await fixture.OpenViewAsync();
		var duplicate = new TestView(fixture, fixture.View.ViewId);

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			duplicate);

		// A different instance that duplicates a registered view id cannot be attached.
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.ViewInUse, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(0, duplicate.CloseCount);
	}

	[TestMethod]
	public async Task OpenWithView_ViewThatReportsAlreadyOpenIsRejectedAsViewInUse()
	{
		await using var fixture = new ManagerFixture();
		fixture.View.OpenOutcome = WorkspaceDocumentViewOpenOutcome.AlreadyOpen;
		fixture.View.OpenFailure = new WorkspaceOperationFailure("ViewElsewhere", "The view is attached to another document.");

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		// The document was loaded, but the view reports that it is attached elsewhere; the snapshot
		// is carried so the caller can decide what to do with the loaded content, and the view's own
		// failure detail is passed through.
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.ViewInUse, result.Outcome);
		Assert.IsNotNull(result.Snapshot);
		Assert.AreEqual("The view is attached to another document.", result.Failure!.Message);
	}

	[TestMethod]
	public async Task OpenWithView_MissingPathWithoutCreateIfMissingReportsNotFound()
	{
		await using var fixture = new ManagerFixture();
		string missingPath = Path.Combine(fixture.DirectoryPath, "missing.txt");
		WorkspaceDocumentOpenOptions options = new(
			TextEncodingKind.Utf8,
			new TextFileFormat(TextEncodingKind.Utf8, false, TextNewlineStyle.Lf),
			CreateIfMissing: false);

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			missingPath,
			options,
			fixture.View);

		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.NotFound, result.Outcome);
		Assert.IsNull(result.Snapshot);
	}

	[TestMethod]
	public async Task OpenWithView_UnexpectedViewExceptionReportsOpenFailedWithoutSnapshot()
	{
		await using var fixture = new ManagerFixture();
		fixture.View.ThrowOnOpen = true;

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		// An unexpected exception on the open path is an open failure, unlike a view that reports a
		// rejection outcome; the partially attached view is closed again.
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.OpenFailed, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.OpenFailed, result.Failure!.Code);
		Assert.AreEqual(1, fixture.View.CloseCount);
	}

	[TestMethod]
	public async Task OpenWithView_LoadFailurePropagatesFailureCode()
	{
		await using var fixture = new ManagerFixture();
		string invalidPath = Path.Combine(fixture.DirectoryPath, "invalid.lua");
		File.WriteAllBytes(invalidPath, [0xC3, 0x28]);

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			invalidPath,
			s_openOptions,
			fixture.View);

		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.LoadFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.InvalidEncoding, result.Failure!.Code);
		Assert.IsNull(result.Snapshot);
	}

	[TestMethod]
	public async Task OpenWithView_CanceledTokenReportsCanceled()
	{
		await using var fixture = new ManagerFixture();
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View,
			cancellation.Token);

		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.Canceled, result.Outcome);
		Assert.IsNull(result.Snapshot);
	}

	[TestMethod]
	public async Task OpenWithView_UsesTheResultCapturedByTheDispatchedAction()
	{
		await using var fixture = new ManagerFixture();
		fixture.View.OpenOutcome = WorkspaceDocumentViewOpenOutcome.Unavailable;
		fixture.View.OpenFailure = new WorkspaceOperationFailure("AttachRejected", "Attach rejected.");

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		// The manager consumes the attach result the dispatched action captured after the delegate's
		// task completes; a delegate that completed before running the action would leave it unset and
		// surface a different failure.
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.ViewRejected, result.Outcome);
		Assert.IsNotNull(result.Snapshot);
		Assert.AreEqual("Attach rejected.", result.Failure!.Message);
	}

	[TestMethod]
	public async Task OpenWithView_FailingEventSubscriptionRollsBackRegistration()
	{
		await using var fixture = new ManagerFixture();
		fixture.View.ThrowOnSubscribe = true;

		WorkspaceDocumentManagerOpenResult failed = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.OpenFailed, failed.Outcome);
		Assert.AreEqual(1, fixture.View.CloseCount);

		// The failed registration is rolled back, so attaching the same view succeeds once the
		// subscription works instead of reporting the view as already open or in use.
		fixture.View.ThrowOnSubscribe = false;
		WorkspaceDocumentManagerOpenResult opened = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.Opened, opened.Outcome);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task OpenWithView_UnregisteredBetweenRegistrationAndSubscription_LeavesNoSubscription()
	{
		await using var fixture = new ManagerFixture();
		fixture.View.SubscribeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		fixture.View.SubscribeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<WorkspaceDocumentManagerOpenResult> open = fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		// The registration is published before the subscription add runs; park inside the add so the
		// unregister removes the registration and runs its own `-=` first, before the add lands.
		await fixture.View.SubscribeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
		fixture.Manager.UnregisterOpenView(fixture.View);
		fixture.View.SubscribeGate.SetResult();

		WorkspaceDocumentManagerOpenResult result = await open;

		// The manager re-checks the registration after the add and undoes the subscription, so no handler
		// keeps the unregistered view alive.
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.AlreadyOpen, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(0, fixture.View.ApplySubscriberCount);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task OpenWithView_ReattachAfterUnregisterWhileSubscribing_KeepsOnlyTheLiveSubscription()
	{
		await using var fixture = new ManagerFixture();
		var firstGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		fixture.View.SubscribeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		fixture.View.SubscribeGate = firstGate;

		// The first attach registers the view and parks inside the subscription add. It is started on the
		// pool so the parked add cannot block the test thread.
		Task<WorkspaceDocumentManagerOpenResult> first = Task.Run(() => fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View));
		await fixture.View.SubscribeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

		// The unregister removes the registration and runs its own removal before the parked add lands.
		// The host then closes the view, and a re-attach of the same instance registers it again and
		// subscribes with its own token; the re-attach must not park.
		fixture.Manager.UnregisterOpenView(fixture.View);
		fixture.View.Close();
		fixture.View.SubscribeGate = null;

		WorkspaceDocumentManagerOpenResult second = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.Opened, second.Outcome);

		// Releasing the first add lets the abandoned attach finish. Its undo removes its own token by
		// identity, so it cannot strip the re-attach's live subscription.
		firstGate.SetResult();
		WorkspaceDocumentManagerOpenResult firstResult = await first;

		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.AlreadyOpen, firstResult.Outcome);
		Assert.AreEqual(1, fixture.View.ApplySubscriberCount, "Only the re-attach's subscription stays live.");

		// The one remaining subscription is the re-attach's own token, so unregistering removes it and
		// leaves nothing behind; a leaked first-attach handler would survive this removal.
		fixture.Manager.UnregisterOpenView(fixture.View);
		Assert.AreEqual(0, fixture.View.ApplySubscriberCount);
	}

	[TestMethod]
	public async Task OpenWithView_ViewWhoseIdGetterThrowsIsUnavailable()
	{
		await using var fixture = new ManagerFixture();
		fixture.View.ViewIdProvider = () => throw new InvalidOperationException("The view cannot report its id.");

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.ViewUnavailable, result.Outcome);
		Assert.AreEqual(0, fixture.View.CloseCount);
	}

	[TestMethod]
	[DataRow("")]
	[DataRow("   ")]
	public async Task OpenWithView_ViewWithBlankIdIsUnavailable(string viewId)
	{
		await using var fixture = new ManagerFixture();
		fixture.View.ViewIdProvider = () => viewId;

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		// A blank or whitespace id cannot be indexed for duplicate detection, so the view is rejected
		// before the document is loaded, with the same failure the attach path reports.
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.ViewUnavailable, result.Outcome);
		Assert.AreEqual(0, fixture.View.CloseCount);
		Assert.IsNull(result.StoreResult);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.OpenFailed, result.Failure!.Code);
		Assert.AreEqual("The view did not report a view id.", result.Failure.Message);
	}

	[TestMethod]
	public async Task OpenWithView_ViewThatLosesItsIdDuringOpenIsRejectedAndClosed()
	{
		await using var fixture = new ManagerFixture();
		int viewIdReads = 0;
		fixture.View.ViewIdProvider = () => ++viewIdReads == 1 ? "live-view" : string.Empty;

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		// The id was present when the view was validated but gone after the attach; the view cannot be
		// indexed for duplicate detection, so it is rejected and closed.
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.ViewRejected, result.Outcome);
		Assert.AreEqual(1, fixture.View.CloseCount);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.OpenFailed, result.Failure!.Code);
		Assert.AreEqual("The view did not report a view id.", result.Failure.Message);
	}

	[TestMethod]
	public async Task UnregisterOpenView_RemovesBlockingStateAndIsIdempotent()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();
		fixture.View.HasPendingEdits = true;

		WorkspaceDocumentManagerReloadResult blocked = await fixture.Manager.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)));

		fixture.Manager.UnregisterOpenView(fixture.View);
		fixture.Manager.UnregisterOpenView(fixture.View);

		WorkspaceDocumentManagerReloadResult reloaded = await fixture.Manager.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)));

		Assert.IsNull(blocked.StoreResult);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, blocked.ViewSynchronization.Outcome);
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Unchanged, reloaded.Outcome);
	}

	[TestMethod]
	public async Task StopAsync_DetachesTheApplyRequestedSubscription()
	{
		await using var fixture = new ManagerFixture();
		await fixture.OpenViewAsync();

		await fixture.Manager.StopAsync();

		Assert.AreEqual(0, fixture.View.ApplySubscriberCount, "The stop path must remove the apply handler.");
	}

	[TestMethod]
	public async Task OpenAsync_EveryStoreOpenOutcomeIsProducedAndMapped()
	{
		await using var fixture = new ManagerFixture();

		// The cases below produce every WorkspaceDocumentOpenOutcome value; the enumeration assertion
		// fails as soon as the store gains an outcome that is not covered here, which keeps the
		// manager's explicit outcome mapping total.
		List<(WorkspaceDocumentOpenOutcome Store, WorkspaceDocumentManagerOpenOutcome Manager)> observed = [];

		WorkspaceDocumentManagerOpenResult opened = await fixture.Manager.OpenAsync(fixture.DocumentPath, s_openOptions);
		observed.Add((WorkspaceDocumentOpenOutcome.Opened, opened.Outcome));

		WorkspaceDocumentManagerOpenResult reopened = await fixture.Manager.OpenAsync(fixture.DocumentPath, s_openOptions);
		observed.Add((WorkspaceDocumentOpenOutcome.AlreadyOpen, reopened.Outcome));

		WorkspaceDocumentManagerOpenResult invalidPath = await fixture.Manager.OpenAsync("   ", s_openOptions);
		observed.Add((WorkspaceDocumentOpenOutcome.InvalidPath, invalidPath.Outcome));

		string missingPath = Path.Combine(fixture.DirectoryPath, "missing.txt");
		WorkspaceDocumentOpenOptions withoutCreate = new(
			TextEncodingKind.Utf8,
			new TextFileFormat(TextEncodingKind.Utf8, false, TextNewlineStyle.Lf),
			CreateIfMissing: false);
		WorkspaceDocumentManagerOpenResult notFound = await fixture.Manager.OpenAsync(missingPath, withoutCreate);
		observed.Add((WorkspaceDocumentOpenOutcome.NotFound, notFound.Outcome));

		string directoryPath = Path.Combine(fixture.DirectoryPath, "subdirectory");
		Directory.CreateDirectory(directoryPath);
		WorkspaceDocumentManagerOpenResult isDirectory = await fixture.Manager.OpenAsync(directoryPath, s_openOptions);
		observed.Add((WorkspaceDocumentOpenOutcome.IsDirectory, isDirectory.Outcome));

		string undecodablePath = Path.Combine(fixture.DirectoryPath, "invalid.lua");
		File.WriteAllBytes(undecodablePath, [0xC3, 0x28]);
		WorkspaceDocumentManagerOpenResult loadFailed = await fixture.Manager.OpenAsync(undecodablePath, s_openOptions);
		observed.Add((WorkspaceDocumentOpenOutcome.LoadFailed, loadFailed.Outcome));

		string canceledPath = Path.Combine(fixture.DirectoryPath, "canceled.txt");
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		WorkspaceDocumentManagerOpenResult canceled = await fixture.Manager.OpenAsync(canceledPath, s_openOptions, cancellation.Token);
		observed.Add((WorkspaceDocumentOpenOutcome.Canceled, canceled.Outcome));

		CollectionAssert.AreEquivalent(
			Enum.GetValues<WorkspaceDocumentOpenOutcome>(),
			observed.Select(pair => pair.Store).ToArray(),
			"Every store open outcome must be produced by a case in this test.");

		// The manager outcome names mirror the store outcome names for the seven shared values, so a
		// mapping that silently degrades into OpenFailed fails here.
		foreach ((WorkspaceDocumentOpenOutcome store, WorkspaceDocumentManagerOpenOutcome manager) in observed)
			Assert.AreEqual(store.ToString(), manager.ToString(), $"{store} must map to the manager outcome of the same name.");
	}

	[TestMethod]
	public async Task OpenAsync_InvalidOptionsThrowInsteadOfProducingAnOpenFailedOutcome()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentOpenOptions invalid = new(
			(TextEncodingKind)int.MaxValue,
			new TextFileFormat(TextEncodingKind.Utf8, false, TextNewlineStyle.Lf));

		// The store contract treats invalid options as argument errors; both open paths keep that
		// contract instead of degrading the error into an outcome.
		await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => fixture.Manager.OpenAsync(
			fixture.DocumentPath,
			invalid));
		await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			invalid,
			fixture.View));
	}

	[TestMethod]
	public async Task DeferredDispatch_CompletesBeforeTheManagerConsumesViewResults()
	{
		await using var fixture = new ManagerFixture(deferredDispatch: true);
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// The delegate completes asynchronously; the manager awaits the task instead of blocking, so
		// the view results are still consumed after the action ran.
		WorkspaceDocumentManagerMutationResult replaced = await fixture.Manager.ReplaceAsync(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"deferred edit",
			snapshot.FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.Outcome);
		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Synchronized, replaced.ViewSynchronization.Outcome);
		Assert.AreEqual("deferred edit", fixture.View.Text);
	}

	[TestMethod]
	public async Task OpenWithView_ThrowingDispatchDelegateReportsOpenFailed()
	{
		await using var fixture = new ManagerFixture(throwingDispatch: true);

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		// The delegate contract requires the action to run; a delegate that faults before running it
		// cannot be interpreted as a view rejection, so the open path reports an open failure with the
		// delegate's detail.
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.OpenFailed, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.OpenFailed, result.Failure!.Code);
	}

	[TestMethod]
	public async Task OpenWithView_ReportsTheDocumentAuthorityOutcome()
	{
		await using var fixture = new ManagerFixture();
		string freshPath = Path.Combine(fixture.DirectoryPath, "fresh.txt");

		// A fresh open carries the store outcome; an already-open document that merely receives the
		// view keeps that distinction visible through the store outcome.
		WorkspaceDocumentManagerOpenResult fresh = await fixture.Manager.OpenAsync(freshPath, s_openOptions);
		WorkspaceDocumentSnapshot initial = await fixture.OpenViewAsync();
		var secondView = new TestView(fixture, "second-view");
		WorkspaceDocumentManagerOpenResult attached = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			secondView);

		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.Opened, fresh.Outcome);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, fresh.StoreResult!.Outcome);
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.Opened, attached.Outcome);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.AlreadyOpen, attached.StoreResult!.Outcome);
		Assert.AreEqual(initial.DocumentId, attached.Snapshot!.DocumentId);

		// The short-circuit paths answer before the load, so no store outcome exists.
		WorkspaceDocumentManagerOpenResult repeated = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.AlreadyOpen, repeated.Outcome);
		Assert.IsNull(repeated.StoreResult);
	}

	[TestMethod]
	public async Task OpenWithView_DirectoryPathReportsIsDirectory()
	{
		await using var fixture = new ManagerFixture();
		string directoryPath = Path.Combine(fixture.DirectoryPath, "subdirectory");
		Directory.CreateDirectory(directoryPath);

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			directoryPath,
			s_openOptions,
			fixture.View);

		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.IsDirectory, result.Outcome);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.IsDirectory, result.StoreResult!.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(0, fixture.View.OpenCount);
		Assert.AreEqual(0, fixture.View.CloseCount);
	}

	[TestMethod]
	public async Task OpenWithView_ViewWithDocumentKeyOrConflictIsUnavailable()
	{
		await using var fixture = new ManagerFixture();
		var loadedView = new TestView(fixture, "loaded-view")
		{
			DocumentKeyOverride = new WorkspaceDocumentKey(Guid.NewGuid())
		};
		var conflictedView = new TestView(fixture, "conflicted-view") { HasConflict = true };

		WorkspaceDocumentManagerOpenResult loaded = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			loadedView);
		WorkspaceDocumentManagerOpenResult conflicted = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			conflictedView);

		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.ViewUnavailable, loaded.Outcome);
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.ViewUnavailable, conflicted.Outcome);
		Assert.AreEqual(0, loadedView.OpenCount);
		Assert.AreEqual(0, conflictedView.OpenCount);
	}

	[TestMethod]
	public async Task OpenWithView_ViewIdThatThrowsAfterValidationReportsOpenFailed()
	{
		await using var fixture = new ManagerFixture();
		int viewIdReads = 0;
		fixture.View.ViewIdProvider = () => ++viewIdReads == 1
			? "live-view"
			: throw new InvalidOperationException("The view cannot report its id.");

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		// The attach path reads the id again; a throwing getter is an unexpected exception on the
		// attach path, so the open fails and the partially attached view is closed again.
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.OpenFailed, result.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.OpenFailed, result.Failure!.Code);
		Assert.AreEqual(1, fixture.View.CloseCount);
	}

	[TestMethod]
	public async Task OpenWithView_RegisteredViewWhoseDocumentIsGoneReportsAlreadyOpenWithoutSnapshot()
	{
		await using var fixture = new ManagerFixture();
		WorkspaceDocumentSnapshot snapshot = await fixture.OpenViewAsync();

		// The document is removed behind the manager's back, which is what another manager over the
		// same store produces; the registered view stays registered and the open is a no-op without a
		// snapshot.
		WorkspaceDocumentDeleteResult deleted = await fixture.Store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, deleted.Outcome);

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.AlreadyOpen, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.IsNull(result.Failure);
		Assert.AreEqual(1, fixture.View.OpenCount);
	}

	[TestMethod]
	public async Task OpenAsync_StoreFailureFaultsTheTask()
	{
		await using var fixture = new ManagerFixture();
		await fixture.Store.DisposeAsync();

		// Unlike OpenWithViewAsync, the view-less open path has no outcome for an unexpected store
		// failure: the exception surfaces on the returned task.
		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => fixture.Manager.OpenAsync(
			fixture.DocumentPath,
			s_openOptions));
	}

	[TestMethod]
	public async Task OpenWithView_ViewRejectionWithoutFailureReportsSynthesizedDetail()
	{
		await using var fixture = new ManagerFixture();
		fixture.View.OpenOutcome = WorkspaceDocumentViewOpenOutcome.Unavailable;

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		// The view rejected the attach without supplying a failure detail, so the manager synthesizes
		// the explanation and still closes the rejected view.
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.ViewRejected, result.Outcome);
		Assert.IsNotNull(result.Snapshot);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.OpenFailed, result.Failure!.Code);
		Assert.AreEqual("The view did not accept the workspace document attachment.", result.Failure.Message);
		Assert.AreEqual(1, fixture.View.CloseCount);
	}

	[TestMethod]
	public async Task OpenWithView_ThrowingStateProbeReportsTheExceptionDetail()
	{
		await using var fixture = new ManagerFixture();
		fixture.View.ThrowOnStateProbe = true;

		WorkspaceDocumentManagerOpenResult result = await fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View);

		// A view that cannot report its availability is unavailable, and the exception detail is
		// surfaced instead of being swallowed.
		Assert.AreEqual(WorkspaceDocumentManagerOpenOutcome.ViewUnavailable, result.Outcome);
		Assert.AreEqual(0, fixture.View.CloseCount);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.OpenFailed, result.Failure!.Code);
		Assert.AreEqual("The view cannot report its pending-edit state.", result.Failure.Message);
	}

	[TestMethod]
	public async Task OpenWithView_DisposedStoreFaultsTheTask()
	{
		await using var fixture = new ManagerFixture();
		await fixture.Store.DisposeAsync();

		// A store that fails outside the manager's outcome vocabulary faults the task, matching the
		// view-less open path.
		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => fixture.Manager.OpenWithViewAsync(
			fixture.DocumentPath,
			s_openOptions,
			fixture.View));
	}
}
