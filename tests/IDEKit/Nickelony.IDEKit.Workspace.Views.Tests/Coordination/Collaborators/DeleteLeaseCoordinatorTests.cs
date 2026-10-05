using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

/// <summary>Covers the delete-lease coordinator's entry, rollback and release rules directly.</summary>
[TestClass]
public sealed class DeleteLeaseCoordinatorTests
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

			Leases = new DeleteLeaseCoordinator(Tracker, dispatch ?? Inline);
		}

		internal object SyncRoot { get; } = new();

		internal StopGate<Task> Gate { get; }

		internal ViewTracker Tracker { get; }

		internal DeleteLeaseCoordinator Leases { get; }

		internal static Task Inline(Action action)
		{
			action();
			return Task.CompletedTask;
		}

		internal T Register<T>(T view)
			where T : FakeWorkspaceView
		{
			Tracker.Register(view, view.ViewId, TestSnapshots.Create("doc", "content"));
			return view;
		}
	}

	[TestMethod]
	public async Task EnterAsync_EntersTheLeaseOfEveryView()
	{
		var fixture = new TestHost();

		FakeLeaseWorkspaceView first = fixture.Register(new FakeLeaseWorkspaceView("a"));
		FakeLeaseWorkspaceView second = fixture.Register(new FakeLeaseWorkspaceView("b"));

		List<IWorkspaceDocumentDeleteLeaseView> entered = [];

		List<WorkspaceDocumentViewSynchronizationIssue> issues = await fixture.Leases.EnterAsync([first, second], entered).ConfigureAwait(false);

		Assert.IsEmpty(issues);
		Assert.AreSequenceEqual([first, second], entered);
	}

	[TestMethod]
	public async Task EnterAsync_SkipsAViewWithoutTheLeaseCapability()
	{
		var fixture = new TestHost();
		var plain = new FakeWorkspaceView("plain");

		List<IWorkspaceDocumentDeleteLeaseView> entered = [];

		List<WorkspaceDocumentViewSynchronizationIssue> issues = await fixture.Leases.EnterAsync([plain], entered).ConfigureAwait(false);

		Assert.IsEmpty(issues);
		Assert.IsEmpty(entered);
	}

	[TestMethod]
	public async Task EnterAsync_WhenAViewRejects_RollsBackTheEnteredLeasesAndReportsTheRejection()
	{
		var fixture = new TestHost();
		FakeLeaseWorkspaceView first = fixture.Register(new FakeLeaseWorkspaceView("a"));
		var failure = new WorkspaceOperationFailure("rejected", "The view holds edits.");

		FakeLeaseWorkspaceView second = fixture.Register(
			new FakeLeaseWorkspaceView("b") { EnterLeaseResult = new(WorkspaceDocumentViewDeleteLeaseOutcome.Failed, failure) }
		);

		List<IWorkspaceDocumentDeleteLeaseView> entered = [];

		List<WorkspaceDocumentViewSynchronizationIssue> issues = await fixture.Leases.EnterAsync([first, second], entered).ConfigureAwait(false);

		Assert.AreEqual("b", issues.Single().ViewId);
		Assert.AreSame(failure, issues.Single().Failure);
		Assert.AreEqual(1, first.ReleaseLeaseCount);
		Assert.AreEqual(0, second.ReleaseLeaseCount);
		Assert.IsEmpty(entered);
	}

	[TestMethod]
	public async Task EnterAsync_WhenAViewThrows_ReportsTheDeleteLeaseFailureWithTheException()
	{
		var fixture = new TestHost();

		FakeLeaseWorkspaceView view = fixture.Register(new FakeLeaseWorkspaceView("a") { ThrowOnEnterLease = true });
		List<IWorkspaceDocumentDeleteLeaseView> entered = [];

		List<WorkspaceDocumentViewSynchronizationIssue> issues = await fixture.Leases.EnterAsync([view], entered).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceViewOperationFailureCodes.DeleteLeaseFailed, issues.Single().Failure?.Code);
		Assert.IsInstanceOfType<InvalidOperationException>(issues.Single().Failure?.Exception);
	}

	[TestMethod]
	public async Task EnterAsync_WhenTheRollbackReleaseFails_RecordsTheViewAsUnsynchronizedAndReportsIt()
	{
		var fixture = new TestHost();

		FakeLeaseWorkspaceView first = fixture.Register(new FakeLeaseWorkspaceView("a") { ThrowOnReleaseLease = true });

		FakeLeaseWorkspaceView second = fixture.Register(
			new FakeLeaseWorkspaceView("b") { EnterLeaseResult = new(WorkspaceDocumentViewDeleteLeaseOutcome.Failed) }
		);

		List<WorkspaceDocumentViewSynchronizationIssue> issues = await fixture.Leases.EnterAsync([first, second], []).ConfigureAwait(false);

		Assert.HasCount(2, issues);
		Assert.IsTrue(fixture.Tracker.IsMarkedUnsynchronized(first));
	}

	[TestMethod]
	public async Task ReleaseAsync_ReleasesEveryLease()
	{
		var fixture = new TestHost();

		FakeLeaseWorkspaceView first = fixture.Register(new FakeLeaseWorkspaceView("a"));
		FakeLeaseWorkspaceView second = fixture.Register(new FakeLeaseWorkspaceView("b"));

		List<WorkspaceDocumentViewSynchronizationIssue> issues = [];

		await fixture.Leases.ReleaseAsync([first, second], issues).ConfigureAwait(false);

		Assert.IsEmpty(issues);
		Assert.AreEqual(1, first.ReleaseLeaseCount);
		Assert.AreEqual(1, second.ReleaseLeaseCount);
	}

	[TestMethod]
	public async Task ReleaseAsync_WhenAReleaseFails_RecordsTheViewAsUnsynchronizedAndReportsIt()
	{
		var fixture = new TestHost();
		var failure = new WorkspaceOperationFailure("stuck", "The lease stayed.");

		FakeLeaseWorkspaceView view = fixture.Register(
			new FakeLeaseWorkspaceView("a") { ReleaseLeaseResult = new(WorkspaceDocumentViewDeleteLeaseOutcome.Failed, failure) }
		);

		List<WorkspaceDocumentViewSynchronizationIssue> issues = [];

		await fixture.Leases.ReleaseAsync([view], issues).ConfigureAwait(false);

		Assert.AreSame(failure, issues.Single().Failure);
		Assert.IsTrue(fixture.Tracker.TryGetUnsynchronized(view, out WorkspaceOperationFailure? recorded));
		Assert.AreSame(failure, recorded);
	}

	[TestMethod]
	public void Release_WithoutRecordingState_ReportsTheFailureButLeavesTheViewSynchronized()
	{
		var fixture = new TestHost();

		FakeLeaseWorkspaceView view = fixture.Register(new FakeLeaseWorkspaceView("a") { ThrowOnReleaseLease = true });
		List<WorkspaceDocumentViewSynchronizationIssue> issues = [];

		fixture.Leases.Release([view], issues, recordState: false);

		Assert.AreEqual(WorkspaceViewOperationFailureCodes.DeleteLeaseFailed, issues.Single().Failure?.Code);
		Assert.IsFalse(fixture.Tracker.IsMarkedUnsynchronized(view));
	}

	[TestMethod]
	public async Task Scope_ReleaseOnExit_ReleasesTheLeasesStillHeld()
	{
		var fixture = new TestHost();

		FakeLeaseWorkspaceView view = fixture.Register(new FakeLeaseWorkspaceView("a"));
		DeleteLeaseSet scope = new(fixture.Leases);

		await fixture.Leases.EnterAsync([view], scope.EnteredLeases).ConfigureAwait(false);
		await scope.DisposeAsync().ConfigureAwait(false);

		Assert.AreEqual(1, view.ReleaseLeaseCount);
	}

	[TestMethod]
	public async Task Scope_AfterMarkReleased_ReleasesNothingOnExit()
	{
		var fixture = new TestHost();

		FakeLeaseWorkspaceView view = fixture.Register(new FakeLeaseWorkspaceView("a"));
		DeleteLeaseSet scope = new(fixture.Leases);

		await fixture.Leases.EnterAsync([view], scope.EnteredLeases).ConfigureAwait(false);

		scope.MarkReleased();
		await scope.DisposeAsync().ConfigureAwait(false);

		Assert.AreEqual(0, view.ReleaseLeaseCount);
	}

	[TestMethod]
	public async Task ReleaseViaDispatchAsync_WhenTheDispatcherFaultsBeforeTheAction_MarksEveryViewUnsynchronized()
	{
		var fixture = new TestHost(static _ => throw new InvalidOperationException("The dispatcher failed."));

		FakeLeaseWorkspaceView first = fixture.Register(new FakeLeaseWorkspaceView("a"));
		FakeLeaseWorkspaceView second = fixture.Register(new FakeLeaseWorkspaceView("b"));

		await fixture.Leases.ReleaseViaDispatchAsync([first, second]).ConfigureAwait(false);

		Assert.IsTrue(fixture.Tracker.IsMarkedUnsynchronized(first));
		Assert.IsTrue(fixture.Tracker.IsMarkedUnsynchronized(second));
		Assert.AreEqual(0, first.ReleaseLeaseCount);
	}

	[TestMethod]
	public async Task ReleaseViaDispatchAsync_WhenTheDispatcherFaultsAfterTheActionRan_DoesNotMarkTheReleasedViews()
	{
		var fixture = new TestHost(action =>
		{
			action();
			throw new InvalidOperationException("The dispatcher failed after the action.");
		});

		FakeLeaseWorkspaceView view = fixture.Register(new FakeLeaseWorkspaceView("a"));

		await fixture.Leases.ReleaseViaDispatchAsync([view]).ConfigureAwait(false);

		Assert.AreEqual(1, view.ReleaseLeaseCount);
		Assert.IsFalse(fixture.Tracker.IsMarkedUnsynchronized(view));
	}

	private static LeasedOperationDescriptor<string, string> PlanFor(
		IEnumerable<IWorkspaceDocumentView> views,
		List<string> log,
		DeleteLeaseReleaseMode release,
		bool applied = true,
		Func<Task<string>>? executeStore = null
	)
	{
		return new LeasedOperationDescriptor<string, string>
		{
			Views = views,

			ExecuteStoreAsync =
				executeStore
				?? (
					() =>
					{
						log.Add("store");
						return Task.FromResult("stored");
					}
				),

			IsApplied = _ => applied,
			UpdateViews = (_, _) => log.Add("update"),
			Release = release,
			CreateBlocked = issues => $"blocked:{issues.Count}",
			CreateResult = (store, issues) => $"{store}:{issues.Count}",
		};
	}

	[TestMethod]
	public async Task RunAsync_ReleasesTheLeasesBeforeTheViewsUpdateWhenTheViewsClose()
	{
		var fixture = new TestHost();

		FakeLeaseWorkspaceView view = fixture.Register(new FakeLeaseWorkspaceView("a"));
		List<string> log = [];

		var observed = new List<string>();

		Task<string> store()
		{
			observed.Add($"leased:{view.EnterLeaseCount - view.ReleaseLeaseCount}");
			return Task.FromResult("stored");
		}

		string result = await fixture
			.Leases.RunAsync(PlanFor(views: [view], log, release: DeleteLeaseReleaseMode.BeforeViewUpdates, executeStore: store))
			.ConfigureAwait(false);

		Assert.AreEqual("stored:0", result);
		Assert.AreSequenceEqual(["leased:1"], observed);
		Assert.AreEqual(1, view.ReleaseLeaseCount);
		Assert.AreSequenceEqual(["update"], log);
	}

	[TestMethod]
	public async Task RunAsync_ReleasesTheLeasesAfterTheViewsUpdateWhenTheViewsStayAttached()
	{
		var fixture = new TestHost();

		FakeLeaseWorkspaceView view = fixture.Register(new FakeLeaseWorkspaceView("a"));
		List<string> log = [];
		int releasesDuringUpdate = -1;

		LeasedOperationDescriptor<string, string> plan = new()
		{
			Views = [view],
			ExecuteStoreAsync = () => Task.FromResult("stored"),
			IsApplied = _ => true,
			UpdateViews = (_, _) => releasesDuringUpdate = view.ReleaseLeaseCount,
			Release = DeleteLeaseReleaseMode.AfterViewUpdates,
			CreateBlocked = _ => "blocked",
			CreateResult = (store, _) => store,
		};

		await fixture.Leases.RunAsync(plan).ConfigureAwait(false);

		Assert.AreEqual(0, releasesDuringUpdate);
		Assert.AreEqual(1, view.ReleaseLeaseCount);
		Assert.IsEmpty(log);
	}

	[TestMethod]
	public async Task RunAsync_WhenAViewRejectsItsLease_ReturnsTheBlockedResultAndNeverRunsTheStore()
	{
		var fixture = new TestHost();

		FakeLeaseWorkspaceView entered = fixture.Register(new FakeLeaseWorkspaceView("a"));
		FakeLeaseWorkspaceView rejecting = fixture.Register(new FakeLeaseWorkspaceView("b"));

		rejecting.EnterLeaseResult = new(WorkspaceDocumentViewDeleteLeaseOutcome.Failed);
		List<string> log = [];

		string result = await fixture
			.Leases.RunAsync(PlanFor([entered, rejecting], log, DeleteLeaseReleaseMode.BeforeViewUpdates))
			.ConfigureAwait(false);

		Assert.AreEqual("blocked:1", result);
		Assert.IsEmpty(log);
		Assert.AreEqual(1, entered.ReleaseLeaseCount);
	}

	[TestMethod]
	public async Task RunAsync_WhenTheStoreDidNotTakeEffect_ReleasesTheLeasesWithoutUpdatingTheViews()
	{
		var fixture = new TestHost();

		FakeLeaseWorkspaceView view = fixture.Register(new FakeLeaseWorkspaceView("a"));
		List<string> log = [];

		string result = await fixture
			.Leases.RunAsync(PlanFor(views: [view], log, release: DeleteLeaseReleaseMode.BeforeViewUpdates, applied: false))
			.ConfigureAwait(false);

		Assert.AreEqual("stored:0", result);
		Assert.AreSequenceEqual(["store"], log);
		Assert.AreEqual(1, view.ReleaseLeaseCount);
	}

	[TestMethod]
	public async Task RunAsync_WhenTheStoreThrows_ReleasesTheLeasesAndPropagates()
	{
		var fixture = new TestHost();

		FakeLeaseWorkspaceView view = fixture.Register(new FakeLeaseWorkspaceView("a"));
		List<string> log = [];

		LeasedOperationDescriptor<string, string> plan = PlanFor(
			views: [view],
			log,
			release: DeleteLeaseReleaseMode.AfterViewUpdates,
			executeStore: () => throw new InvalidOperationException("boom")
		);

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Leases.RunAsync(plan)).ConfigureAwait(false);

		Assert.AreEqual(1, view.ReleaseLeaseCount);
	}
}
