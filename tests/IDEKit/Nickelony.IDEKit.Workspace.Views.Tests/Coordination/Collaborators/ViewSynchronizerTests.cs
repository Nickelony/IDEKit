using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

/// <summary>Covers the view synchronizer's blocking, refresh, acknowledgment and issue-normalization rules directly.</summary>
[TestClass]
public sealed class ViewSynchronizerTests
{
	private sealed class TestHost
	{
		internal TestHost(Func<Action, Task>? dispatch = null)
		{
			Gate = new StopGate<Task>(SyncRoot);

			Tracker = new ViewTracker(
				owner: this,
				SyncRoot,
				stopGate: Gate,
				options: new WorkspaceOptions { PathComparison = LocalPathComparisonPolicy.CaseSensitive },
				onApplyRequested: static (_, _) => { }
			);

			Synchronizer = new ViewSynchronizer(Tracker, dispatch ?? Probe.Dispatch);
		}

		internal object SyncRoot { get; } = new();

		internal StopGate<Task> Gate { get; }

		internal ViewTracker Tracker { get; }

		internal ViewSynchronizer Synchronizer { get; }

		internal RecordingViewDispatcher Probe { get; } = new();

		internal static WorkspaceDocumentSnapshot Snapshot(string documentId = "doc", long version = 1, WorkspaceDocumentKey? key = null) =>
			TestSnapshots.Create(documentId, content: "content", version, documentKey: key);

		internal static WorkspaceDocumentMutationResult MutationOf(WorkspaceDocumentSnapshot? snapshot)
		{
			var identity = new WorkspaceDocumentRequestIdentity(new WorkspaceDocumentKey(Guid.NewGuid()), "doc", 0);
			return new(WorkspaceDocumentMutationOutcome.Changed, identity, snapshot);
		}

		// Registers the view the way an attach does, then starts recording every view member that is served outside a dispatched action.
		internal T Register<T>(T view, string documentId = "doc", long version = 1, WorkspaceDocumentKey? key = null)
			where T : FakeWorkspaceView
		{
			WorkspaceDocumentSnapshot snapshot = Snapshot(documentId, version, key);

			view.Open(snapshot);
			Tracker.Register(view, view.ViewId, snapshot);

			return Probe.Watch(view);
		}
	}

	[TestMethod]
	public async Task GetBlockingIssuesAsync_WithPendingEditsOrAConflict_ReportsEachViewWithoutAFailure()
	{
		var fixture = new TestHost();

		fixture.Register(new FakeWorkspaceView("edited") { HasPendingEdits = true });
		fixture.Register(new FakeWorkspaceView("conflicted") { HasConflict = true });
		fixture.Register(new FakeWorkspaceView("clean"));

		IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>? issues = await fixture
			.Synchronizer.GetBlockingIssuesAsync("doc")
			.ConfigureAwait(false);

		Assert.IsNotNull(issues);
		Assert.AreSequenceEqual(["conflicted", "edited"], issues.Select(issue => issue.ViewId).Order(StringComparer.Ordinal));
		Assert.IsTrue(issues.All(issue => issue.Failure is null));
	}

	[TestMethod]
	public async Task GetBlockingIssuesAsync_WithOnlyCleanViews_ReturnsNull()
	{
		var fixture = new TestHost();

		fixture.Register(new FakeWorkspaceView("a"));
		fixture.Register(new FakeWorkspaceView("b"));

		Assert.IsNull(await fixture.Synchronizer.GetBlockingIssuesAsync("doc").ConfigureAwait(false));
	}

	[TestMethod]
	public async Task GetBlockingIssuesAsync_ForADocumentId_ChecksOnlyThePeersOfThatDocument()
	{
		var fixture = new TestHost();

		fixture.Register(new FakeWorkspaceView("other") { HasPendingEdits = true }, documentId: "other");
		fixture.Register(new FakeWorkspaceView("clean"));

		Assert.IsNull(await fixture.Synchronizer.GetBlockingIssuesAsync("doc").ConfigureAwait(false));
	}

	[TestMethod]
	public async Task GetBlockingIssuesAsync_ForGivenViews_ChecksOnlyThoseViews()
	{
		var fixture = new TestHost();

		FakeWorkspaceView edited = fixture.Register(new FakeWorkspaceView("edited") { HasPendingEdits = true });
		FakeWorkspaceView clean = fixture.Register(new FakeWorkspaceView("clean"));

		Assert.IsNull(await fixture.Synchronizer.GetBlockingIssuesAsync([clean]).ConfigureAwait(false));

		IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>? issues = await fixture
			.Synchronizer.GetBlockingIssuesAsync([edited, clean])
			.ConfigureAwait(false);

		Assert.AreEqual("edited", issues?.Single().ViewId);
	}

	[TestMethod]
	public async Task GetBlockingIssuesAsync_WithAnUnsynchronizedView_BlocksUnlessTheUnsynchronizedStateIsIgnored()
	{
		var fixture = new TestHost();
		FakeWorkspaceView view = fixture.Register(new FakeWorkspaceView("a"));
		var failure = new WorkspaceOperationFailure("code", "The view fell behind.");

		fixture.Tracker.RecordFailure(view, failure);

		IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>? blocking = await fixture
			.Synchronizer.GetBlockingIssuesAsync("doc")
			.ConfigureAwait(false);

		Assert.AreSame(failure, blocking?.Single().Failure);

		IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>? recovering = await fixture
			.Synchronizer.GetBlockingIssuesAsync("doc", includeUnsynchronizedState: false)
			.ConfigureAwait(false);

		Assert.IsNull(recovering);
	}

	[TestMethod]
	public async Task GetBlockingIssuesAsync_WhenTheStateProbeThrows_ReportsTheProbeFailureAndMarksTheViewUnsynchronized()
	{
		var fixture = new TestHost();
		FakeWorkspaceView view = fixture.Register(new FakeWorkspaceView("a") { ThrowOnStateProbe = true });

		IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>? issues = await fixture
			.Synchronizer.GetBlockingIssuesAsync("doc", includeUnsynchronizedState: false)
			.ConfigureAwait(false);

		WorkspaceDocumentViewSynchronizationIssue issue = issues!.Single();

		Assert.AreEqual("a", issue.ViewId);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewStateProbeFailed, issue.Failure?.Code);
		Assert.IsInstanceOfType<InvalidOperationException>(issue.Failure?.Exception);
		Assert.IsTrue(fixture.Tracker.TryGetUnsynchronized(view, out WorkspaceOperationFailure? recorded));
		Assert.AreSame(issue.Failure, recorded);
	}

	[TestMethod]
	public async Task GetBlockingIssuesAsync_ReadsTheViewsOnlyInsideOneDispatchedAction()
	{
		var fixture = new TestHost();

		fixture.Register(new FakeWorkspaceView("a") { HasPendingEdits = true });
		fixture.Register(new FakeWorkspaceView("b"));

		await fixture.Synchronizer.GetBlockingIssuesAsync("doc").ConfigureAwait(false);

		Assert.AreEqual(1, fixture.Probe.DispatchCount);
		Assert.IsEmpty(fixture.Probe.MembersOutsideDispatch);
	}

	[TestMethod]
	public async Task GetBlockingIssuesAsync_WaitsForADeferredDispatchBeforeAnswering()
	{
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		var fixture = new TestHost(dispatch: action =>
			Task.Run(async () =>
			{
				await release.Task.ConfigureAwait(false);
				action();
			})
		);

		fixture.Register(new FakeWorkspaceView("a") { HasPendingEdits = true });

		Task<IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>?> pending = fixture.Synchronizer.GetBlockingIssuesAsync("doc");

		Assert.IsFalse(pending.IsCompleted);

		release.SetResult();

		Assert.AreEqual("a", (await pending.ConfigureAwait(false))?.Single().ViewId);
	}

	[TestMethod]
	public async Task RefreshViewsAndCollectUnsynchronizedAsync_RefreshesAViewBehindTheSnapshotAndRecordsItsVersion()
	{
		var fixture = new TestHost();

		FakeWorkspaceView view = fixture.Register(new FakeWorkspaceView("a"), version: 1);
		WorkspaceDocumentSnapshot snapshot = TestHost.Snapshot(version: 5);

		IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue> issues = await fixture
			.Synchronizer.RefreshViewsAndCollectUnsynchronizedAsync("doc", snapshot)
			.ConfigureAwait(false);

		Assert.IsEmpty(issues);
		Assert.AreEqual(1, view.RefreshCount);
		Assert.AreSame(snapshot, view.LastRefreshSnapshot);
		Assert.IsFalse(fixture.Tracker.IsBehind(view, TestHost.Snapshot(version: 5)));
		Assert.IsTrue(fixture.Tracker.IsBehind(view, TestHost.Snapshot(version: 6)));
	}

	[TestMethod]
	public async Task RefreshViewsAndCollectUnsynchronizedAsync_SkipsASynchronizedViewThatIsCurrent()
	{
		var fixture = new TestHost();
		FakeWorkspaceView view = fixture.Register(new FakeWorkspaceView("a"), version: 5);

		await fixture.Synchronizer.RefreshViewsAndCollectUnsynchronizedAsync("doc", TestHost.Snapshot(version: 5)).ConfigureAwait(false);

		Assert.AreEqual(0, view.RefreshCount);
	}

	[TestMethod]
	public async Task RefreshViewsAndCollectUnsynchronizedAsync_RetriesAnUnsynchronizedViewEvenWhenItsVersionIsCurrent()
	{
		var fixture = new TestHost();
		FakeWorkspaceView view = fixture.Register(new FakeWorkspaceView("a"), version: 5);

		fixture.Tracker.RecordFailure(view, new WorkspaceOperationFailure("code", "The view fell behind."));

		IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue> issues = await fixture
			.Synchronizer.RefreshViewsAndCollectUnsynchronizedAsync("doc", TestHost.Snapshot(version: 5))
			.ConfigureAwait(false);

		Assert.IsEmpty(issues);
		Assert.AreEqual(1, view.RefreshCount);
		Assert.IsFalse(fixture.Tracker.IsMarkedUnsynchronized(view));
	}

	[TestMethod]
	public async Task RefreshViewsAndCollectUnsynchronizedAsync_SkipsAViewWithPendingEditsAndStillReportsIt()
	{
		var fixture = new TestHost();
		FakeWorkspaceView view = fixture.Register(new FakeWorkspaceView("a") { HasPendingEdits = true }, version: 1);

		IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue> issues = await fixture
			.Synchronizer.RefreshViewsAndCollectUnsynchronizedAsync("doc", TestHost.Snapshot(version: 5))
			.ConfigureAwait(false);

		Assert.AreEqual(0, view.RefreshCount);
		Assert.AreEqual("a", issues.Single().ViewId);
		Assert.IsNull(issues.Single().Failure);
	}

	[TestMethod]
	public async Task RefreshViewsAndCollectUnsynchronizedAsync_WhenARefreshFails_ReportsTheFailureAndKeepsTheViewUnsynchronized()
	{
		var fixture = new TestHost();
		var failure = new WorkspaceOperationFailure("code", "The view could not apply the snapshot.");

		FakeWorkspaceView view = fixture.Register(
			new FakeWorkspaceView("a") { RefreshResult = new(WorkspaceDocumentViewRefreshOutcome.Failed, failure) },
			version: 1
		);

		IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue> issues = await fixture
			.Synchronizer.RefreshViewsAndCollectUnsynchronizedAsync("doc", TestHost.Snapshot(version: 5))
			.ConfigureAwait(false);

		Assert.AreSame(failure, issues.Single().Failure);
		Assert.IsTrue(fixture.Tracker.TryGetUnsynchronized(view, out WorkspaceOperationFailure? recorded));
		Assert.AreSame(failure, recorded);
		Assert.IsTrue(fixture.Tracker.IsBehind(view, TestHost.Snapshot(version: 5)));
	}

	[TestMethod]
	public async Task RefreshViewsAndCollectUnsynchronizedAsync_WhenARefreshThrows_ReportsTheRefreshFailureCodeWithTheException()
	{
		var fixture = new TestHost();

		fixture.Register(new FakeWorkspaceView("a") { ThrowOnRefresh = true }, version: 1);

		IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue> issues = await fixture
			.Synchronizer.RefreshViewsAndCollectUnsynchronizedAsync("doc", TestHost.Snapshot(version: 5))
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewRefreshFailed, issues.Single().Failure?.Code);
		Assert.IsInstanceOfType<InvalidOperationException>(issues.Single().Failure?.Exception);
	}

	[TestMethod]
	public async Task RefreshViewsAndCollectUnsynchronizedAsync_WhenAViewMarksItselfStale_ReportsItAsUnsynchronized()
	{
		var fixture = new TestHost();

		FakeWorkspaceView view = fixture.Register(
			new FakeWorkspaceView("a") { RefreshResult = new(WorkspaceDocumentViewRefreshOutcome.MarkedStale) },
			version: 1
		);

		IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue> issues = await fixture
			.Synchronizer.RefreshViewsAndCollectUnsynchronizedAsync("doc", TestHost.Snapshot(version: 5))
			.ConfigureAwait(false);

		Assert.AreEqual("a", issues.Single().ViewId);
		Assert.IsNull(issues.Single().Failure);
		Assert.IsTrue(fixture.Tracker.IsMarkedUnsynchronized(view));
	}

	[TestMethod]
	public async Task RefreshViewsAndCollectUnsynchronizedAsync_WhenAViewRefreshes_ClearsItsRecordedFailure()
	{
		var fixture = new TestHost();
		FakeWorkspaceView view = fixture.Register(new FakeWorkspaceView("a"), version: 1);

		fixture.Tracker.RecordFailure(view, new WorkspaceOperationFailure("code", "The view fell behind."));

		await fixture.Synchronizer.RefreshViewsAndCollectUnsynchronizedAsync("doc", TestHost.Snapshot(version: 5)).ConfigureAwait(false);

		Assert.IsFalse(fixture.Tracker.TryGetUnsynchronized(view, out _));
	}

	[TestMethod]
	public async Task RefreshViewsAndCollectUnsynchronizedAsync_TouchesTheViewsInsideOneDispatchedAction()
	{
		var fixture = new TestHost();

		fixture.Register(new FakeWorkspaceView("a"), version: 1);
		fixture.Register(new FakeWorkspaceView("b") { HasConflict = true }, version: 1);

		await fixture.Synchronizer.RefreshViewsAndCollectUnsynchronizedAsync("doc", TestHost.Snapshot(version: 5)).ConfigureAwait(false);

		Assert.AreEqual(1, fixture.Probe.DispatchCount);
		Assert.IsEmpty(fixture.Probe.MembersOutsideDispatch);
	}

	[TestMethod]
	public async Task CollectUnsynchronizedIssuesAsync_ReportsPendingStateAndRecordedFailuresWithoutRefreshing()
	{
		var fixture = new TestHost();
		var failure = new WorkspaceOperationFailure("code", "The view fell behind.");

		FakeWorkspaceView lagging = fixture.Register(new FakeWorkspaceView("lagging"), version: 1);
		FakeWorkspaceView edited = fixture.Register(new FakeWorkspaceView("edited") { HasPendingEdits = true }, version: 1);

		fixture.Register(new FakeWorkspaceView("clean"), version: 1);
		fixture.Tracker.RecordFailure(lagging, failure);

		IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue> issues = await fixture
			.Synchronizer.CollectUnsynchronizedIssuesAsync("doc")
			.ConfigureAwait(false);

		Assert.AreSequenceEqual(["edited", "lagging"], issues.Select(issue => issue.ViewId).Order(StringComparer.Ordinal));
		Assert.AreSame(failure, issues.Single(issue => issue.ViewId == "lagging").Failure);
		Assert.AreEqual(0, lagging.RefreshCount + edited.RefreshCount);
	}

	[TestMethod]
	public void RefreshPeerViews_WithACollector_ReceivesTheViewsThatDidNotRefresh()
	{
		var fixture = new TestHost();
		var failure = new WorkspaceOperationFailure("code", "The view could not apply the snapshot.");

		FakeWorkspaceView failing = fixture.Register(
			new FakeWorkspaceView("failing") { RefreshResult = new(WorkspaceDocumentViewRefreshOutcome.Failed, failure) },
			version: 1
		);

		FakeWorkspaceView edited = fixture.Register(new FakeWorkspaceView("edited") { HasPendingEdits = true }, version: 1);
		FakeWorkspaceView healthy = fixture.Register(new FakeWorkspaceView("healthy"), version: 1);

		List<WorkspaceDocumentViewSynchronizationIssue> collected = [];

		fixture.Synchronizer.RefreshPeerViews([failing, edited, healthy], TestHost.Snapshot(version: 5), collected);

		Assert.AreSequenceEqual(["failing", "edited"], collected.Select(issue => issue.ViewId));
		Assert.AreEqual(1, healthy.RefreshCount);
		Assert.AreEqual(0, edited.RefreshCount);
	}

	[TestMethod]
	public async Task AcknowledgeDocumentViewsAsync_AsksEveryPeerOfTheDocumentAndRecordsTheSnapshotVersion()
	{
		var fixture = new TestHost();

		FakeWorkspaceView first = fixture.Register(new FakeWorkspaceView("a"), version: 1);
		FakeWorkspaceView second = fixture.Register(new FakeWorkspaceView("b"), version: 1);
		FakeWorkspaceView elsewhere = fixture.Register(new FakeWorkspaceView("c"), documentId: "other", version: 1);

		WorkspaceDocumentMutationResult mutation = TestHost.MutationOf(TestHost.Snapshot(version: 7));

		List<WorkspaceDocumentViewSynchronizationIssue> issues = await fixture
			.Synchronizer.AcknowledgeDocumentViewsAsync(mutation)
			.ConfigureAwait(false);

		Assert.IsEmpty(issues);
		Assert.AreSame(mutation, first.LastApplyResult);
		Assert.AreSame(mutation, second.LastApplyResult);
		Assert.IsNull(elsewhere.LastApplyResult);
		Assert.IsFalse(fixture.Tracker.IsBehind(first, TestHost.Snapshot(version: 7)));
		Assert.IsFalse(fixture.Tracker.IsBehind(second, TestHost.Snapshot(version: 7)));
	}

	[TestMethod]
	public async Task AcknowledgeDocumentViewsAsync_WhenAViewFails_ReportsItAndMarksItUnsynchronized()
	{
		var fixture = new TestHost();
		var failure = new WorkspaceOperationFailure("code", "The view could not adopt the state.");

		FakeWorkspaceView view = fixture.Register(
			new FakeWorkspaceView("a") { ApplyResult = new(WorkspaceDocumentViewApplyOutcome.Failed, failure) }
		);

		List<WorkspaceDocumentViewSynchronizationIssue> issues = await fixture
			.Synchronizer.AcknowledgeDocumentViewsAsync(TestHost.MutationOf(TestHost.Snapshot(version: 2)))
			.ConfigureAwait(false);

		Assert.AreSame(failure, issues.Single().Failure);
		Assert.IsTrue(fixture.Tracker.TryGetUnsynchronized(view, out WorkspaceOperationFailure? recorded));
		Assert.AreSame(failure, recorded);
	}

	[TestMethod]
	public async Task AcknowledgeDocumentViewsAsync_WithoutASnapshot_AsksNoViewAndNeverDispatches()
	{
		var fixture = new TestHost();
		FakeWorkspaceView view = fixture.Register(new FakeWorkspaceView("a"));

		List<WorkspaceDocumentViewSynchronizationIssue> issues = await fixture
			.Synchronizer.AcknowledgeDocumentViewsAsync(TestHost.MutationOf(snapshot: null))
			.ConfigureAwait(false);

		Assert.IsEmpty(issues);
		Assert.IsNull(view.LastApplyResult);
		Assert.AreEqual(0, fixture.Probe.DispatchCount);
	}

	[TestMethod]
	public void AcknowledgeApply_WhenTheViewThrows_ReturnsAFailedResultWithTheAcknowledgeFailureCode()
	{
		var view = new FakeWorkspaceView("a") { ThrowOnAcknowledgeApply = true };

		WorkspaceDocumentViewApplyResult result = ViewSynchronizer.AcknowledgeApply(view, TestHost.MutationOf(TestHost.Snapshot()));

		Assert.AreEqual(WorkspaceDocumentViewApplyOutcome.Failed, result.Outcome);
		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewAcknowledgeFailed, result.Failure?.Code);
		Assert.IsInstanceOfType<InvalidOperationException>(result.Failure?.Exception);
	}

	[TestMethod]
	public async Task SynchronizeIdentitiesAsync_MovesTheRegistrationOfEveryAcknowledgingView()
	{
		var fixture = new TestHost();

		WorkspaceDocumentKey key = new(Guid.NewGuid());

		FakeWorkspaceView first = fixture.Register(new FakeWorkspaceView("a"), documentId: "old", key: key);
		FakeWorkspaceView second = fixture.Register(new FakeWorkspaceView("b"), documentId: "old", key: key);

		WorkspaceDocumentSnapshot renamed = TestHost.Snapshot("new", version: 2, key);

		WorkspaceDocumentViewSynchronizationResult result = await fixture
			.Synchronizer.SynchronizeIdentitiesAsync("old", key, renamed)
			.ConfigureAwait(false);

		Assert.AreSame(WorkspaceDocumentViewSynchronizationResult.Synchronized, result);
		Assert.AreEqual("new", fixture.Tracker.GetDocumentId(first));
		Assert.AreEqual("new", fixture.Tracker.GetDocumentId(second));
		Assert.AreEqual("old", first.LastIdentityChange?.OldDocumentId);
		Assert.AreEqual(key, first.LastIdentityChange?.OldDocumentKey);
		Assert.AreSame(renamed, first.LastIdentityChange?.Snapshot);
		Assert.IsEmpty(fixture.Tracker.GetPeers("old", sourceView: null));
	}

	[TestMethod]
	public async Task SynchronizeIdentitiesAsync_MatchesPeersByTheOldDocumentKeyAndNotByTheDocumentId()
	{
		var fixture = new TestHost();

		WorkspaceDocumentKey renamedKey = new(Guid.NewGuid());
		WorkspaceDocumentKey reopenedKey = new(Guid.NewGuid());

		FakeWorkspaceView renamedView = fixture.Register(new FakeWorkspaceView("renamed"), documentId: "path", key: renamedKey);
		FakeWorkspaceView reopenedView = fixture.Register(new FakeWorkspaceView("reopened"), documentId: "path", key: reopenedKey);

		await fixture
			.Synchronizer.SynchronizeIdentitiesAsync("path", renamedKey, TestHost.Snapshot("moved", version: 2, key: renamedKey))
			.ConfigureAwait(false);

		Assert.AreEqual("moved", fixture.Tracker.GetDocumentId(renamedView));
		Assert.AreEqual("path", fixture.Tracker.GetDocumentId(reopenedView));
		Assert.IsNull(reopenedView.LastIdentityChange);
	}

	[TestMethod]
	public async Task SynchronizeIdentitiesAsync_WhenAViewFails_ReportsItAsUnsynchronizedAndKeepsItsRegistration()
	{
		var fixture = new TestHost();
		WorkspaceDocumentKey key = new(Guid.NewGuid());
		var failure = new WorkspaceOperationFailure("code", "The view rejected the new identity.");

		FakeWorkspaceView view = fixture.Register(
			new FakeWorkspaceView("a") { IdentityResult = new(WorkspaceDocumentViewIdentityOutcome.Failed, failure) },
			documentId: "old",
			key: key
		);

		WorkspaceDocumentViewSynchronizationResult result = await fixture
			.Synchronizer.SynchronizeIdentitiesAsync("old", key, TestHost.Snapshot("new", version: 2, key))
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.Outcome);
		Assert.AreSame(failure, result.Issues.Single().Failure);
		Assert.AreEqual("old", fixture.Tracker.GetDocumentId(view));
		Assert.IsTrue(fixture.Tracker.TryGetUnsynchronized(view, out WorkspaceOperationFailure? recorded));
		Assert.AreSame(failure, recorded);
	}

	[TestMethod]
	public async Task SynchronizeIdentitiesAsync_WhenAViewThrows_ReportsTheIdentityUpdateFailureCode()
	{
		var fixture = new TestHost();
		WorkspaceDocumentKey key = new(Guid.NewGuid());

		fixture.Register(new FakeWorkspaceView("a") { ThrowOnAcknowledgeIdentity = true }, documentId: "old", key: key);

		WorkspaceDocumentViewSynchronizationResult result = await fixture
			.Synchronizer.SynchronizeIdentitiesAsync("old", key, TestHost.Snapshot("new", version: 2, key))
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewIdentityUpdateFailed, result.Issues.Single().Failure?.Code);
	}

	[TestMethod]
	public void AcknowledgeIdentity_AfterAnEarlierFailure_ClearsTheUnsynchronizedState()
	{
		var fixture = new TestHost();

		WorkspaceDocumentKey key = new(Guid.NewGuid());
		FakeWorkspaceView view = fixture.Register(new FakeWorkspaceView("a"), documentId: "old", key: key);

		fixture.Tracker.RecordFailure(view, new WorkspaceOperationFailure("code", "The view fell behind."));

		WorkspaceDocumentSnapshot renamed = TestHost.Snapshot("new", version: 2, key);
		List<WorkspaceDocumentViewSynchronizationIssue> issues = [];

		fixture.Synchronizer.AcknowledgeIdentity(view, new WorkspaceDocumentViewIdentityChange(key, "old", renamed), issues);

		Assert.IsEmpty(issues);
		Assert.IsFalse(fixture.Tracker.IsMarkedUnsynchronized(view));
		Assert.IsFalse(fixture.Tracker.IsBehind(view, TestHost.Snapshot(version: 2)));
	}

	[TestMethod]
	public async Task SynchronizeIdentitiesAsync_TouchesTheViewsOnlyInsideOneDispatchedAction()
	{
		var fixture = new TestHost();

		WorkspaceDocumentKey key = new(Guid.NewGuid());

		fixture.Register(new FakeWorkspaceView("a"), documentId: "old", key: key);
		fixture.Register(new FakeWorkspaceView("b"), documentId: "old", key: key);

		await fixture.Synchronizer.SynchronizeIdentitiesAsync("old", key, TestHost.Snapshot("new", version: 2, key)).ConfigureAwait(false);

		Assert.AreEqual(1, fixture.Probe.DispatchCount);
		Assert.IsEmpty(fixture.Probe.MembersOutsideDispatch);
	}

	[TestMethod]
	public async Task AcknowledgeDocumentViewsAsync_TouchesTheViewsOnlyInsideOneDispatchedAction()
	{
		var fixture = new TestHost();

		fixture.Register(new FakeWorkspaceView("a"));
		fixture.Register(new FakeWorkspaceView("b") { ApplyResult = new(WorkspaceDocumentViewApplyOutcome.Failed) });

		await fixture.Synchronizer.AcknowledgeDocumentViewsAsync(TestHost.MutationOf(TestHost.Snapshot(version: 2))).ConfigureAwait(false);

		Assert.AreEqual(1, fixture.Probe.DispatchCount);
		Assert.IsEmpty(fixture.Probe.MembersOutsideDispatch);
	}

	[TestMethod]
	public void RefreshPeerViews_WhenTheStateProbeThrows_RecordsTheProbeFailureAndReportsIt()
	{
		var fixture = new TestHost();
		List<WorkspaceDocumentViewSynchronizationIssue> collected = [];

		FakeWorkspaceView view = fixture.Register(new FakeWorkspaceView("a") { ThrowOnStateProbe = true }, version: 1);

		fixture.Synchronizer.RefreshPeerViews([view], TestHost.Snapshot(version: 5), collected);

		WorkspaceDocumentViewSynchronizationIssue issue = collected.Single();

		Assert.AreEqual(WorkspaceViewOperationFailureCodes.ViewStateProbeFailed, issue.Failure?.Code);
		Assert.AreEqual(0, view.RefreshCount);
		Assert.IsTrue(fixture.Tracker.TryGetUnsynchronized(view, out WorkspaceOperationFailure? recorded));
		Assert.AreSame(issue.Failure, recorded);
	}

	[TestMethod]
	public void ComposeSynchronization_WithoutIssues_IsTheSynchronizedResult() =>
		Assert.AreSame(WorkspaceDocumentViewSynchronizationResult.Synchronized, ViewSynchronizer.ComposeSynchronization([]));

	[TestMethod]
	public void ComposeSynchronization_WithIssues_IsUnsynchronizedAndNormalized()
	{
		var failure = new WorkspaceOperationFailure("code", "The view failed.");

		WorkspaceDocumentViewSynchronizationResult result = ViewSynchronizer.ComposeSynchronization([
			new WorkspaceDocumentViewSynchronizationIssue("b"),
			new WorkspaceDocumentViewSynchronizationIssue("a"),
			new WorkspaceDocumentViewSynchronizationIssue("B"),
			new WorkspaceDocumentViewSynchronizationIssue("b", failure),
		]);

		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, result.Outcome);
		Assert.AreSequenceEqual(["B", "a", "b"], result.Issues.Select(issue => issue.ViewId));
		Assert.AreSame(failure, result.Issues.Single(issue => issue.ViewId == "b").Failure);
	}

	[TestMethod]
	public void Blocked_NormalizesTheIssuesLikeTheUnsynchronizedResult()
	{
		var result = WorkspaceDocumentViewSynchronizationResult.Blocked([
			new WorkspaceDocumentViewSynchronizationIssue("b"),
			new WorkspaceDocumentViewSynchronizationIssue("a"),
			new WorkspaceDocumentViewSynchronizationIssue("b"),
		]);

		Assert.AreEqual(WorkspaceDocumentViewSynchronizationOutcome.Blocked, result.Outcome);
		Assert.AreSequenceEqual(["a", "b"], result.Issues.Select(issue => issue.ViewId));
	}

	[TestMethod]
	public void GetSnapshotPairs_PairsEachViewWithTheSnapshotOfItsDocumentOnce()
	{
		var fixture = new TestHost();

		FakeWorkspaceView first = fixture.Register(new FakeWorkspaceView("a"), documentId: "one");
		FakeWorkspaceView second = fixture.Register(new FakeWorkspaceView("b"), documentId: "one");
		FakeWorkspaceView third = fixture.Register(new FakeWorkspaceView("c"), documentId: "two");

		WorkspaceDocumentSnapshot one = TestHost.Snapshot("one");
		WorkspaceDocumentSnapshot two = TestHost.Snapshot("two");

		List<ViewSnapshotPair> pairs = fixture.Synchronizer.GetSnapshotPairs([one, two, one]);

		Assert.HasCount(3, pairs);
		Assert.AreSame(one, pairs.Single(pair => ReferenceEquals(pair.View, first)).Snapshot);
		Assert.AreSame(one, pairs.Single(pair => ReferenceEquals(pair.View, second)).Snapshot);
		Assert.AreSame(two, pairs.Single(pair => ReferenceEquals(pair.View, third)).Snapshot);
	}

	[TestMethod]
	public void TryGetViewDocumentKey_ReturnsTheKeyOnlyForAViewThatReportsOne()
	{
		WorkspaceDocumentKey key = new(Guid.NewGuid());

		Assert.IsTrue(ViewSynchronizer.TryGetViewDocumentKey(new FakeWorkspaceView("a") { DocumentKey = key }, out WorkspaceDocumentKey? reported));
		Assert.AreEqual(key, reported);

		Assert.IsFalse(ViewSynchronizer.TryGetViewDocumentKey(new FakeWorkspaceView("b"), out _));
		Assert.IsFalse(ViewSynchronizer.TryGetViewDocumentKey(new FakeWorkspaceView("c") { ThrowOnDocumentKey = true }, out _));
	}
}
