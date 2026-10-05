using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Views.Coordination;

namespace Nickelony.IDEKit.Workspace.Views.Tests;

/// <summary>Covers the view tracker's registration, indexing, recording and drain rules directly.</summary>
[TestClass]
public sealed class ViewTrackerTests
{
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
		}

		internal object SyncRoot { get; } = new();

		internal StopGate<Task> Gate { get; }

		internal ViewTracker Tracker { get; }

		internal static WorkspaceDocumentSnapshot Snapshot(string documentId, long version = 1, WorkspaceDocumentKey? key = null) =>
			TestSnapshots.Create(documentId, content: "content", version, documentKey: key);

		internal ViewEntry Register(IWorkspaceDocumentView view, WorkspaceDocumentSnapshot snapshot)
		{
			ViewTracker.RegisterResult result = Tracker.Register(view, view.ViewId, snapshot);
			return result.Entry ?? throw new InvalidOperationException("The view was not registered.");
		}

		internal void BeginStop()
		{
			lock (SyncRoot)
			{
				Gate.TryBeginStop(out _, out _);
				Gate.RecordStopTask(Task.CompletedTask);
			}
		}
	}

	[TestMethod]
	public void Register_IndexesTheViewByInstanceIdAndDocument()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a");

		fixture.Register(view, TestHost.Snapshot("doc"));

		Assert.IsTrue(fixture.Tracker.Contains(view));
		Assert.AreEqual("doc", fixture.Tracker.GetDocumentId(view));
		Assert.AreSequenceEqual([view], fixture.Tracker.GetPeers("doc", sourceView: null));
		Assert.AreEqual(ViewTracker.Claim.ViewRegistered, fixture.Tracker.FindClaim(view, "a"));
	}

	[TestMethod]
	public void Register_ForTheSameInstance_ReportsAlreadyRegisteredWithoutClosingIt()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a");

		fixture.Register(view, TestHost.Snapshot("doc"));

		ViewTracker.RegisterResult result = fixture.Tracker.Register(view, "a", TestHost.Snapshot("doc"));

		Assert.IsNull(result.Entry);
		Assert.IsTrue(result.AlreadyRegistered);
		Assert.IsFalse(result.CloseDuplicateAttachment);
	}

	[TestMethod]
	public void Register_ForAnotherViewWithTheSameId_AsksTheCallerToCloseTheDuplicate()
	{
		var fixture = new TestHost();

		fixture.Register(new FakeWorkspaceView("a"), TestHost.Snapshot("doc"));

		var duplicate = new FakeWorkspaceView("a");

		ViewTracker.RegisterResult result = fixture.Tracker.Register(duplicate, "a", TestHost.Snapshot("doc"));

		Assert.IsNull(result.Entry);
		Assert.IsTrue(result.AlreadyRegistered);
		Assert.IsTrue(result.CloseDuplicateAttachment);
		Assert.IsFalse(fixture.Tracker.Contains(duplicate));
		Assert.AreEqual(ViewTracker.Claim.ViewIdInUse, fixture.Tracker.FindClaim(duplicate, "a"));
	}

	[TestMethod]
	public void FindClaim_ForAnUnknownViewAndId_ReportsNone()
	{
		var fixture = new TestHost();
		Assert.AreEqual(ViewTracker.Claim.None, fixture.Tracker.FindClaim(new FakeWorkspaceView("a"), "a"));
	}

	[TestMethod]
	public void TryAdmitAttach_ForAnUnregisteredView_ReturnsTrue()
	{
		var fixture = new TestHost();
		Assert.IsTrue(fixture.Tracker.TryAdmitAttach(new FakeWorkspaceView("a")));
	}

	[TestMethod]
	public void TryAdmitAttach_ForARegisteredView_ReturnsFalse()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a");

		fixture.Register(view, TestHost.Snapshot("doc"));

		Assert.IsFalse(fixture.Tracker.TryAdmitAttach(view));
	}

	[TestMethod]
	public void TryAdmitAttach_AfterTheStopBegan_Throws()
	{
		var fixture = new TestHost();

		fixture.BeginStop();

		Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Tracker.TryAdmitAttach(new FakeWorkspaceView("a")));
	}

	[TestMethod]
	public void Register_AfterTheTeardownSnapshot_IsRejectedAndLeavesNothingRegistered()
	{
		var fixture = new TestHost();
		var early = new FakeWorkspaceView("early");

		fixture.Register(early, TestHost.Snapshot("doc"));
		fixture.BeginStop();

		KeyValuePair<IWorkspaceDocumentView, ViewEntry>[] drained;

		lock (fixture.SyncRoot)
			drained = fixture.Tracker.Drain();

		var late = new FakeWorkspaceView("late");

		Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Tracker.Register(late, "late", TestHost.Snapshot("doc")));

		Assert.HasCount(1, drained);
		Assert.AreSame(early, drained[0].Key);
		Assert.IsEmpty(fixture.Tracker.GetRegisteredViews());
		Assert.IsFalse(fixture.Tracker.Contains(late));
	}

	[TestMethod]
	public void Drain_ReturnsEveryRegistrationAndEmptiesEveryIndex()
	{
		var fixture = new TestHost();
		var first = new FakeWorkspaceView("a");
		var second = new FakeWorkspaceView("b");

		fixture.Register(first, TestHost.Snapshot("doc-one"));
		fixture.Register(second, TestHost.Snapshot("doc-two"));

		KeyValuePair<IWorkspaceDocumentView, ViewEntry>[] drained = fixture.Tracker.Drain();

		Assert.HasCount(2, drained);
		Assert.IsEmpty(fixture.Tracker.GetRegisteredViews());
		Assert.IsEmpty(fixture.Tracker.GetPeers("doc-one", sourceView: null));
		Assert.IsEmpty(fixture.Tracker.GetPeers("doc-two", sourceView: null));
		Assert.AreEqual(ViewTracker.Claim.None, fixture.Tracker.FindClaim(first, "a"));
		Assert.AreEqual(ViewTracker.Claim.None, fixture.Tracker.FindClaim(second, "b"));
	}

	[TestMethod]
	public void Register_WaitingForTheLockWhileTheStopDrains_IsRejectedAndLeavesNothingRegistered()
	{
		var fixture = new TestHost();
		var late = new FakeWorkspaceView("late");

		ObjectDisposedException? rejection = null;
		Thread registrar;

		using var registering = new ManualResetEventSlim();

		lock (fixture.SyncRoot)
		{
			registrar = new Thread(() =>
			{
				try
				{
					registering.Set();
					fixture.Tracker.Register(late, "late", TestHost.Snapshot("doc"));
				}
				catch (ObjectDisposedException exception)
				{
					rejection = exception;
				}
			});

			registrar.Start();

			// The registrar signals right before Register, whose first action is to take the lock that this thread holds, so it parks on the
			// lock until the stop has drained; whichever side reaches the lock first, the stop check inside the lock must see the stop.
			Assert.IsTrue(registering.Wait(TestPolling.DefaultTimeout), "The registrar must reach the Register call.");

			fixture.Gate.TryBeginStop(out _, out _);
			fixture.Gate.RecordStopTask(Task.CompletedTask);
			fixture.Tracker.Drain();
		}

		Assert.IsTrue(registrar.Join(TestPolling.DefaultTimeout), "The registrar must finish once the lock is free.");
		Assert.IsNotNull(rejection);
		Assert.Contains(nameof(ViewTrackerTests.TestHost), rejection.ObjectName ?? string.Empty);
		Assert.IsEmpty(fixture.Tracker.GetRegisteredViews());
		Assert.IsFalse(fixture.Tracker.Contains(late));
	}

	[TestMethod]
	public void IsRegisteredAndNotStopping_AfterTheStopBegan_ReturnsFalseForARegisteredView()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a");

		fixture.Register(view, TestHost.Snapshot("doc"));

		Assert.IsTrue(fixture.Tracker.IsRegisteredAndNotStopping(view));

		fixture.BeginStop();

		Assert.IsFalse(fixture.Tracker.IsRegisteredAndNotStopping(view));
		Assert.IsTrue(fixture.Tracker.Contains(view));
	}

	[TestMethod]
	public void Remove_ReturnsTheRegistrationAndClearsEveryIndex()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a");

		ViewEntry registration = fixture.Register(view, TestHost.Snapshot("doc"));

		Assert.AreSame(registration, fixture.Tracker.Remove(view));
		Assert.IsNull(fixture.Tracker.Remove(view));
		Assert.IsFalse(fixture.Tracker.Contains(view));
		Assert.IsEmpty(fixture.Tracker.GetPeers("doc", sourceView: null));
		Assert.AreEqual(ViewTracker.Claim.None, fixture.Tracker.FindClaim(view, "a"));
	}

	[TestMethod]
	public void RemoveIfCurrent_ForAReplacedRegistration_KeepsTheNewOne()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a");

		ViewEntry first = fixture.Register(view, TestHost.Snapshot("doc"));

		fixture.Tracker.Remove(view);

		ViewEntry second = fixture.Register(view, TestHost.Snapshot("doc"));

		Assert.IsFalse(fixture.Tracker.RemoveIfCurrent(view, first));
		Assert.IsTrue(fixture.Tracker.IsCurrent(view, second));
		Assert.IsFalse(fixture.Tracker.IsCurrent(view, first));
		Assert.IsTrue(fixture.Tracker.RemoveIfCurrent(view, second));
	}

	[TestMethod]
	public void GetPeers_WithASourceView_LeavesItOut()
	{
		var fixture = new TestHost();
		var source = new FakeWorkspaceView("a");
		var peer = new FakeWorkspaceView("b");

		fixture.Register(source, TestHost.Snapshot("doc"));
		fixture.Register(peer, TestHost.Snapshot("doc"));

		Assert.AreSequenceEqual([peer], fixture.Tracker.GetPeers("doc", source));
		Assert.HasCount(2, fixture.Tracker.GetPeers("doc", sourceView: null));
	}

	[TestMethod]
	public void UpdateIdentity_MovesTheViewToTheNewDocumentAndClearsItsUnsynchronizedState()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a");
		var failure = new WorkspaceOperationFailure("code", "message");

		fixture.Register(view, TestHost.Snapshot("old"));
		fixture.Tracker.RecordFailure(view, failure);

		WorkspaceDocumentSnapshot renamed = TestHost.Snapshot("new", version: 5);

		fixture.Tracker.UpdateIdentity(view, renamed);

		Assert.IsEmpty(fixture.Tracker.GetPeers("old", sourceView: null));
		Assert.AreSequenceEqual([view], fixture.Tracker.GetPeers("new", sourceView: null));
		Assert.AreSequenceEqual([view], fixture.Tracker.GetPeersByDocumentKey(renamed.DocumentKey));
		Assert.AreEqual("new", fixture.Tracker.GetDocumentId(view));
		Assert.IsFalse(fixture.Tracker.IsMarkedUnsynchronized(view));
		Assert.IsFalse(fixture.Tracker.IsBehind(view, TestHost.Snapshot("doc", version: 5)));
	}

	[TestMethod]
	public void IsBehind_AtTheCurrentLogicalVersion_ComparesThePersistedVersion_ButNeverCountsAnOlderLogicalVersion()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a");

		fixture.Register(view, TestHost.Snapshot("doc", version: 5) with { PersistedVersion = 4, IsDirty = true });

		Assert.IsTrue(fixture.Tracker.IsBehind(view, TestHost.Snapshot("doc", version: 5)));
		Assert.IsFalse(fixture.Tracker.IsBehind(view, TestHost.Snapshot("doc", version: 5) with { PersistedVersion = 4, IsDirty = true }));
		Assert.IsFalse(fixture.Tracker.IsBehind(view, TestHost.Snapshot("doc", version: 3)));

		fixture.Tracker.RecordResult(view, synchronized: true, failure: null, snapshot: TestHost.Snapshot("doc", version: 5));

		Assert.IsFalse(fixture.Tracker.IsBehind(view, TestHost.Snapshot("doc", version: 5)));
	}

	[TestMethod]
	public void RecordResult_KeepsTheRecordedVersionFromMovingBackwards()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a");

		fixture.Register(view, TestHost.Snapshot("doc", version: 1));

		fixture.Tracker.RecordResult(view, synchronized: true, failure: null, snapshot: TestHost.Snapshot("doc", version: 5));
		fixture.Tracker.RecordResult(view, synchronized: true, failure: null, snapshot: TestHost.Snapshot("doc", version: 3));

		Assert.IsFalse(fixture.Tracker.IsBehind(view, TestHost.Snapshot("doc", version: 5)));
		Assert.IsTrue(fixture.Tracker.IsBehind(view, TestHost.Snapshot("doc", version: 6)));
	}

	[TestMethod]
	public void RecordResult_WithoutAFailure_KeepsTheLastKnownReason()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a");
		var failure = new WorkspaceOperationFailure("code", "message");

		fixture.Register(view, TestHost.Snapshot("doc"));

		fixture.Tracker.RecordResult(view, synchronized: false, failure, snapshot: null);
		fixture.Tracker.RecordResult(view, synchronized: false, failure: null, snapshot: null);

		Assert.IsTrue(fixture.Tracker.TryGetUnsynchronized(view, out WorkspaceOperationFailure? recorded));
		Assert.AreSame(failure, recorded);
	}

	[TestMethod]
	public void RecordResult_WhenSynchronized_ClearsTheUnsynchronizedStateAndTheFailure()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a");

		fixture.Register(view, TestHost.Snapshot("doc"));
		fixture.Tracker.RecordFailure(view, new WorkspaceOperationFailure("code", "message"));
		fixture.Tracker.RecordResult(view, synchronized: true, failure: null, snapshot: null);

		Assert.IsFalse(fixture.Tracker.IsMarkedUnsynchronized(view));
		Assert.IsFalse(fixture.Tracker.TryGetUnsynchronized(view, out _));
	}

	[TestMethod]
	public void RecordFailure_ForADetachedView_RecordsNothing()
	{
		var fixture = new TestHost();
		var view = new FakeWorkspaceView("a");

		fixture.Tracker.RecordFailure(view, new WorkspaceOperationFailure("code", "message"));

		Assert.IsFalse(fixture.Tracker.IsMarkedUnsynchronized(view));
		Assert.IsFalse(fixture.Tracker.Contains(view));
	}

	[TestMethod]
	public void Register_TwoViewsOfOneDocument_QueueOneOpen()
	{
		var fixture = new TestHost();
		WorkspaceDocumentSnapshot snapshot = TestHost.Snapshot("doc");

		fixture.Register(new FakeWorkspaceView("a"), snapshot);
		fixture.Register(new FakeWorkspaceView("b"), snapshot);

		Assert.AreSequenceEqual(["Opened doc"], TakeChanges(fixture.Tracker));
		Assert.AreSequenceEqual(["doc"], AttachedIds(fixture.Tracker));
	}

	[TestMethod]
	public void Remove_OnlyTheLastViewOfADocument_QueuesItsClose()
	{
		var fixture = new TestHost();

		WorkspaceDocumentSnapshot snapshot = TestHost.Snapshot("doc");

		var first = new FakeWorkspaceView("a");
		var second = new FakeWorkspaceView("b");

		fixture.Register(first, snapshot);
		fixture.Register(second, snapshot);

		_ = TakeChanges(fixture.Tracker);

		fixture.Tracker.Remove(first);
		Assert.IsEmpty(TakeChanges(fixture.Tracker));

		fixture.Tracker.Remove(second);

		Assert.AreSequenceEqual(["Closed doc"], TakeChanges(fixture.Tracker));
		Assert.IsEmpty(AttachedIds(fixture.Tracker));
	}

	[TestMethod]
	public void ObserveSnapshot_QueuesAContentChangeForNewTextAndAMoveForANewId()
	{
		var fixture = new TestHost();
		WorkspaceDocumentSnapshot snapshot = TestSnapshots.Create("doc", "one", version: 1);

		fixture.Register(new FakeWorkspaceView("a"), snapshot);

		fixture.Tracker.ObserveSnapshot(TestSnapshots.Create(documentId: "doc", content: "two", version: 2, documentKey: snapshot.DocumentKey));
		fixture.Tracker.ObserveSnapshot(TestSnapshots.Create(documentId: "doc", content: "two", version: 3, documentKey: snapshot.DocumentKey));
		fixture.Tracker.ObserveSnapshot(TestSnapshots.Create(documentId: "moved", content: "two", version: 4, documentKey: snapshot.DocumentKey));

		Assert.AreSequenceEqual(["Opened doc", "ContentChanged doc", "Renamed moved from doc"], TakeChanges(fixture.Tracker));
		Assert.AreSequenceEqual(["moved"], AttachedIds(fixture.Tracker));
	}

	[TestMethod]
	public void ObserveSnapshot_AVersionNoNewerThanTheTrackedOne_QueuesNothing()
	{
		var fixture = new TestHost();
		WorkspaceDocumentSnapshot snapshot = TestSnapshots.Create("doc", "one", version: 2);

		fixture.Register(new FakeWorkspaceView("a"), snapshot);
		_ = TakeChanges(fixture.Tracker);

		// An operation that finished late carries an older snapshot, which must not move the document back to its older text or id.
		fixture.Tracker.ObserveSnapshot(TestSnapshots.Create(documentId: "old", content: "zero", version: 1, documentKey: snapshot.DocumentKey));
		fixture.Tracker.ObserveSnapshot(TestSnapshots.Create(documentId: "doc", content: "other", version: 2, documentKey: snapshot.DocumentKey));

		Assert.IsEmpty(TakeChanges(fixture.Tracker));
		Assert.AreSequenceEqual(["doc"], AttachedIds(fixture.Tracker));
	}

	[TestMethod]
	public void ObserveSnapshot_ADocumentWithoutAView_QueuesNothing()
	{
		var fixture = new TestHost();

		fixture.Tracker.ObserveSnapshot(TestSnapshots.Create("doc", "one", version: 1));

		Assert.IsFalse(fixture.Tracker.HasPendingChanges);
	}

	[TestMethod]
	public void Drain_QueuesTheCloseOfEveryAttachedDocument()
	{
		var fixture = new TestHost();

		fixture.Register(new FakeWorkspaceView("a"), TestHost.Snapshot("first"));
		fixture.Register(new FakeWorkspaceView("b"), TestHost.Snapshot("second"));

		_ = TakeChanges(fixture.Tracker);

		fixture.Tracker.Drain();

		Assert.AreSequenceEqual(["Closed first", "Closed second"], TakeChanges(fixture.Tracker).Order(StringComparer.Ordinal));
		Assert.IsEmpty(AttachedIds(fixture.Tracker));
	}

	private static string[] AttachedIds(ViewTracker tracker) => [.. tracker.GetAttachedDocuments().Select(snapshot => snapshot.DocumentId)];

	private static List<string> TakeChanges(ViewTracker tracker)
	{
		List<string> changes = [];

		while (tracker.TryTakeChange(out AttachedDocumentChangedEventArgs? change))
		{
			changes.Add(
				change.OldDocumentId is null
					? $"{change.Kind} {change.Snapshot.DocumentId}"
					: $"{change.Kind} {change.Snapshot.DocumentId} from {change.OldDocumentId}"
			);
		}

		return changes;
	}
}
