using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>Covers the document tracker's table, its reservations and its lock-ordered steps directly, without a store.</summary>
[TestClass]
public sealed class WorkspaceDocumentTrackerTests
{
	private static readonly string s_root = Path.Combine(Path.GetTempPath(), "workspace-document-tracker-tests");

	private sealed class TestHost
	{
		internal TestHost(LocalPathComparisonPolicy? pathComparison = null)
		{
			Tracker = new WorkspaceDocumentTracker(
				SyncRoot,
				new WorkspaceOptions { PathComparison = pathComparison ?? LocalPathComparisonPolicy.CaseSensitive }
			);
		}

		internal object SyncRoot { get; } = new();

		internal WorkspaceDocumentTracker Tracker { get; }

		/// <summary>Runs a step under the store lock, as the store does.</summary>
		internal TResult Locked<TResult>(Func<WorkspaceDocumentTracker, TResult> step)
		{
			lock (SyncRoot)
				return step(Tracker);
		}

		/// <summary>Runs a step under the store lock, as the store does.</summary>
		internal void Locked(Action<WorkspaceDocumentTracker> step)
		{
			lock (SyncRoot)
				step(Tracker);
		}
	}

	public required TestContext TestContext { get; set; }

	private static string PathOf(params string[] segments) => Path.Combine([s_root, .. segments]);

	private static LogicalDocument CreateDocument(string documentId, object? syncRoot = null)
	{
		return new LogicalDocument(
			syncRoot: syncRoot ?? new object(),
			documentKey: new WorkspaceDocumentKey(Guid.NewGuid()),
			documentId,
			displayPath: documentId,
			content: string.Empty,
			fileFormat: TestSnapshots.FileFormat,
			onDiskStamp: FileStamp.Missing,
			noBomEncodings: [TextEncoding.Utf8]
		);
	}

	private static WorkspaceDocumentOpenResult Opened() => new(WorkspaceDocumentOpenOutcome.Opened, null);

	[TestMethod]
	public void AddDocument_ThenTryGetDocument_ReturnsTheTrackedDocument()
	{
		var fixture = new TestHost();

		string documentId = PathOf("a.lua");
		LogicalDocument document = CreateDocument(documentId);

		fixture.Locked(tracker => tracker.AddDocument(documentId, document));

		Assert.AreSame(document, fixture.Locked(tracker => tracker.TryGetDocument(documentId, out LogicalDocument? tracked) ? tracked : null));
		Assert.AreSame(document, Assert.ContainsSingle(fixture.Locked(tracker => tracker.Documents.ToList())));
	}

	[TestMethod]
	public void AddDocument_ForATrackedId_Throws()
	{
		var fixture = new TestHost();
		string documentId = PathOf("a.lua");

		fixture.Locked(tracker => tracker.AddDocument(documentId, CreateDocument(documentId)));

		Assert.ThrowsExactly<ArgumentException>(() => fixture.Locked(tracker => tracker.AddDocument(documentId, CreateDocument(documentId))));
	}

	[TestMethod]
	public void RemoveDocument_StopsTrackingTheId()
	{
		var fixture = new TestHost();

		string documentId = PathOf("a.lua");

		fixture.Locked(tracker => tracker.AddDocument(documentId, CreateDocument(documentId)));
		fixture.Locked(tracker => tracker.RemoveDocument(documentId));

		Assert.IsFalse(fixture.Locked(tracker => tracker.TryGetDocument(documentId, out _)));
	}

	[TestMethod]
	public void ReplaceDocuments_ReplacesTheWholeTable()
	{
		var fixture = new TestHost();

		string oldId = PathOf("old.lua");
		string newId = PathOf("new.lua");

		LogicalDocument moved = CreateDocument(newId);

		fixture.Locked(tracker => tracker.AddDocument(oldId, CreateDocument(oldId)));
		fixture.Locked(tracker => tracker.ReplaceDocuments(new Dictionary<string, LogicalDocument> { [newId] = moved }));

		Assert.IsFalse(fixture.Locked(tracker => tracker.TryGetDocument(oldId, out _)));
		Assert.AreSame(moved, Assert.ContainsSingle(fixture.Locked(tracker => tracker.Documents.ToList())));
	}

	[TestMethod]
	public void CollectDescendants_ReturnsTheDirectoryItselfAndEveryDocumentBelowItInIdOrder()
	{
		var fixture = new TestHost();

		string nested = PathOf("dir", "sub", "z.lua");
		string direct = PathOf("dir", "b.lua");
		string same = PathOf("dir");
		string prefixOnly = PathOf("directory", "c.lua");
		string elsewhere = PathOf("other", "d.lua");

		foreach (string documentId in new[] { nested, prefixOnly, direct, elsewhere, same })
			fixture.Locked(tracker => tracker.AddDocument(documentId, CreateDocument(documentId)));

		List<string> collected = fixture.Locked(tracker => tracker.CollectDescendants(PathOf("dir"))).ConvertAll(document => document.DocumentId);

		Assert.AreSequenceEqual([same, direct, nested], collected);
	}

	[TestMethod]
	public void CollectDescendants_UsesThePathComparisonPolicy()
	{
		var fixture = new TestHost(LocalPathComparisonPolicy.CaseInsensitive);

		string documentId = PathOf("Dir", "A.lua");

		fixture.Locked(tracker => tracker.AddDocument(documentId, CreateDocument(documentId)));

		Assert.AreEqual(documentId, Assert.ContainsSingle(fixture.Locked(tracker => tracker.CollectDescendants(PathOf("dIR")))).DocumentId);
	}

	[TestMethod]
	public void CollectDescendants_OfADirectoryWithoutDocuments_ReturnsNothing()
	{
		var fixture = new TestHost();

		string documentId = PathOf("dir", "a.lua");

		fixture.Locked(tracker => tracker.AddDocument(documentId, CreateDocument(documentId)));

		Assert.IsEmpty(fixture.Locked(tracker => tracker.CollectDescendants(PathOf("empty"))));
	}

	[TestMethod]
	public void TheTables_FollowThePathComparisonPolicy()
	{
		var fixture = new TestHost(LocalPathComparisonPolicy.CaseInsensitive);

		string documentId = PathOf("Doc.lua");
		string openId = PathOf("Open.lua");
		string destinationId = PathOf("Dest.lua");

		fixture.Locked(tracker => tracker.AddDocument(documentId, CreateDocument(documentId)));
		fixture.Locked(tracker => tracker.ReserveOpen(openId));
		fixture.Locked(tracker => tracker.ReserveDestination(destinationId));

		Assert.IsTrue(fixture.Locked(tracker => tracker.TryGetDocument(documentId.ToUpperInvariant(), out _)));
		Assert.IsTrue(fixture.Locked(tracker => tracker.TryGetOpenReservation(openId.ToUpperInvariant(), out _)));
		Assert.IsTrue(fixture.Locked(tracker => tracker.TryGetDestinationReservation(destinationId.ToUpperInvariant(), out _)));
	}

	[TestMethod]
	public void ReserveOpen_RegistersThePathUntilItIsCompleted()
	{
		var fixture = new TestHost();
		string documentId = PathOf("a.lua");

		OpenReservation reservation = fixture.Locked(tracker => tracker.ReserveOpen(documentId));

		Assert.IsTrue(fixture.Locked(tracker => tracker.HasOpenOrDestinationReservation(documentId)));

		Assert.IsTrue(
			fixture.Locked(tracker => tracker.TryGetOpenReservation(documentId, out OpenReservation? found) && ReferenceEquals(found, reservation))
		);

		fixture.Locked(tracker => tracker.CompleteOpenReservation(documentId, reservation, Opened()));

		Assert.IsFalse(fixture.Locked(tracker => tracker.HasOpenOrDestinationReservation(documentId)));
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, reservation.Completion.Task.Result.Outcome);
	}

	[TestMethod]
	public void CompleteOpenReservation_WithAStaleReservation_KeepsTheNewerOne()
	{
		var fixture = new TestHost();
		string documentId = PathOf("a.lua");

		OpenReservation stale = fixture.Locked(tracker => tracker.ReserveOpen(documentId));

		fixture.Locked(tracker => tracker.CompleteOpenReservation(documentId, stale, Opened()));

		OpenReservation current = fixture.Locked(tracker => tracker.ReserveOpen(documentId));

		fixture.Locked(tracker => tracker.CompleteOpenReservation(documentId, stale, Opened()));

		Assert.IsTrue(
			fixture.Locked(tracker => tracker.TryGetOpenReservation(documentId, out OpenReservation? found) && ReferenceEquals(found, current))
		);

		Assert.IsFalse(current.Completion.Task.IsCompleted);
	}

	[TestMethod]
	public void ReserveDestination_RegistersThePathUntilItIsCompleted()
	{
		var fixture = new TestHost();
		string destinationId = PathOf("b.lua");

		DestinationReservation reservation = fixture.Locked(tracker => tracker.ReserveDestination(destinationId));

		Assert.IsTrue(
			fixture.Locked(tracker =>
				tracker.TryGetDestinationReservation(destinationId, out DestinationReservation? found) && ReferenceEquals(found, reservation)
			)
		);

		Assert.IsTrue(fixture.Locked(tracker => tracker.IsDestinationUnavailable(destinationId)));

		fixture.Locked(tracker => tracker.CompleteDestinationReservation(reservation));

		Assert.IsFalse(fixture.Locked(tracker => tracker.IsDestinationUnavailable(destinationId)));
		Assert.IsTrue(reservation.Completion.Task.IsCompletedSuccessfully);
	}

	[TestMethod]
	public void CompleteDestinationReservation_WithAStaleReservation_KeepsTheNewerOne()
	{
		var fixture = new TestHost();
		string destinationId = PathOf("b.lua");

		DestinationReservation stale = fixture.Locked(tracker => tracker.ReserveDestination(destinationId));

		fixture.Locked(tracker => tracker.CompleteDestinationReservation(stale));

		DestinationReservation current = fixture.Locked(tracker => tracker.ReserveDestination(destinationId));

		fixture.Locked(tracker => tracker.CompleteDestinationReservation(stale));

		Assert.IsTrue(fixture.Locked(tracker => tracker.IsDestinationUnavailable(destinationId)));
		Assert.IsFalse(current.Completion.Task.IsCompleted);
	}

	[TestMethod]
	public void IsDestinationUnavailable_ReadsTheReservationsAndTheDirectoryOperationsTogether()
	{
		var fixture = new TestHost();

		string freeId = PathOf("free", "a.lua");
		string openId = PathOf("open.lua");
		string insideDirectoryId = PathOf("moving", "inner", "b.lua");

		Assert.IsFalse(fixture.Locked(tracker => tracker.IsDestinationUnavailable(freeId)));

		fixture.Locked(tracker => tracker.ReserveOpen(openId));
		fixture.Locked(tracker => tracker.BeginDirectoryOperation(CreateDirectoryOperation("moving"), []));

		Assert.IsTrue(fixture.Locked(tracker => tracker.IsDestinationUnavailable(openId)));
		Assert.IsTrue(fixture.Locked(tracker => tracker.IsDestinationUnavailable(insideDirectoryId)));
		Assert.IsFalse(fixture.Locked(tracker => tracker.IsDestinationUnavailable(freeId)));
	}

	[TestMethod]
	public void FindDirectoryOperationUnder_CoversTheDirectoryAndItsDescendantsOnly()
	{
		var fixture = new TestHost();
		DirectoryOperationReservation operation = CreateDirectoryOperation("dir");

		fixture.Locked(tracker => tracker.BeginDirectoryOperation(operation, []));

		Assert.AreSame(operation, fixture.Locked(tracker => tracker.FindDirectoryOperationUnder(PathOf("dir"))));
		Assert.AreSame(operation, fixture.Locked(tracker => tracker.FindDirectoryOperationUnder(PathOf("dir", "nested", "a.lua"))));
		Assert.IsNull(fixture.Locked(tracker => tracker.FindDirectoryOperationUnder(PathOf("dir2", "a.lua"))));
		Assert.IsNull(fixture.Locked(tracker => tracker.FindDirectoryOperationUnder(PathOf("other", "dir", "a.lua"))));
	}

	[TestMethod]
	public void BeginDirectoryOperation_CollectsTheReservationsInsideTheSubtreeInOneStep()
	{
		var fixture = new TestHost();

		OpenReservation openInside = fixture.Locked(tracker => tracker.ReserveOpen(PathOf("dir", "a.lua")));
		DestinationReservation destinationInside = fixture.Locked(tracker => tracker.ReserveDestination(PathOf("dir", "deep", "b.lua")));
		OpenReservation openOutside = fixture.Locked(tracker => tracker.ReserveOpen(PathOf("dir2", "c.lua")));
		DestinationReservation destinationOutside = fixture.Locked(tracker => tracker.ReserveDestination(PathOf("elsewhere", "d.lua")));

		var pending = new List<Task>();

		fixture.Locked(tracker => tracker.BeginDirectoryOperation(CreateDirectoryOperation("dir"), pending));

		Assert.HasCount(2, pending);
		Assert.Contains(openInside.Completion.Task, pending);
		Assert.Contains(destinationInside.Completion.Task, pending);
		Assert.DoesNotContain(openOutside.Completion.Task, pending);
		Assert.DoesNotContain(destinationOutside.Completion.Task, pending);
	}

	[TestMethod]
	public void CompleteDirectoryOperation_RemovesTheOperationAndReleasesItsWaiters()
	{
		var fixture = new TestHost();

		DirectoryOperationReservation operation = CreateDirectoryOperation("dir");

		fixture.Locked(tracker => tracker.BeginDirectoryOperation(operation, []));
		fixture.Locked(tracker => tracker.CompleteDirectoryOperation(operation));

		Assert.IsNull(fixture.Locked(tracker => tracker.FindDirectoryOperationUnder(PathOf("dir", "a.lua"))));
		Assert.IsTrue(operation.Completion.Task.IsCompletedSuccessfully);
	}

	[TestMethod]
	public void GetOpenReservationTasks_SnapshotsTheOpensInFlight()
	{
		var fixture = new TestHost();

		OpenReservation first = fixture.Locked(tracker => tracker.ReserveOpen(PathOf("a.lua")));
		OpenReservation second = fixture.Locked(tracker => tracker.ReserveOpen(PathOf("b.lua")));

		fixture.Locked(tracker => tracker.ReserveDestination(PathOf("c.lua")));

		Task[] tasks = fixture.Locked(tracker => tracker.GetOpenReservationTasks());

		Assert.HasCount(2, tasks);
		Assert.Contains(first.Completion.Task, tasks);
		Assert.Contains(second.Completion.Task, tasks);
	}

	[TestMethod]
	public void ClearAfterDisposal_DisposesEveryDiskGateAndEmptiesTheTables()
	{
		var fixture = new TestHost();

		string documentId = PathOf("a.lua");
		LogicalDocument document = CreateDocument(documentId);

		fixture.Locked(tracker => tracker.AddDocument(documentId, document));
		fixture.Locked(tracker => tracker.ReserveOpen(PathOf("b.lua")));
		fixture.Locked(tracker => tracker.ReserveDestination(PathOf("c.lua")));

		fixture.Locked(tracker => tracker.ClearAfterDisposal());

		Assert.ThrowsExactly<ObjectDisposedException>(() => document.DiskOperationGate.Wait(0));
		Assert.IsFalse(fixture.Locked(tracker => tracker.TryGetDocument(documentId, out _)));
		Assert.IsEmpty(fixture.Locked(tracker => tracker.GetOpenReservationTasks()));
		Assert.IsFalse(fixture.Locked(tracker => tracker.IsDestinationUnavailable(PathOf("c.lua"))));
	}

	[TestMethod]
	public void ClearAfterDisposal_LeavesTheGateOfADocumentThatLeftTrackingUndisposed()
	{
		var fixture = new TestHost();

		string documentId = PathOf("a.lua");
		LogicalDocument removed = CreateDocument(documentId);

		fixture.Locked(tracker => tracker.AddDocument(documentId, removed));
		fixture.Locked(tracker => tracker.RemoveDocument(documentId));
		fixture.Locked(tracker => tracker.ClearAfterDisposal());

		Assert.IsTrue(removed.DiskOperationGate.Wait(0));
	}

	[TestMethod]
	public void ReserveDelete_MarksOnlyThatDocumentAsDeleteActiveAndExposesTheReservation()
	{
		var fixture = new TestHost();

		LogicalDocument document = CreateDocument(PathOf("a.lua"));
		LogicalDocument other = CreateDocument(PathOf("b.lua"));

		DeleteReservation reservation = fixture.Locked(tracker => tracker.ReserveDelete(document));

		Assert.IsTrue(fixture.Locked(tracker => tracker.IsDeleteActive(document)));
		Assert.IsFalse(fixture.Locked(tracker => tracker.IsDeleteActive(other)));

		Assert.IsTrue(
			fixture.Locked(tracker => tracker.TryGetDeleteReservation(document, out DeleteReservation? found) && ReferenceEquals(found, reservation))
		);

		Assert.IsFalse(fixture.Locked(tracker => tracker.TryGetDeleteReservation(other, out _)));
		Assert.IsFalse(reservation.Completion.Task.IsCompleted);
	}

	[TestMethod]
	public void ReserveDelete_ForADocumentThatAlreadyHasOne_Throws()
	{
		var fixture = new TestHost();
		LogicalDocument document = CreateDocument(PathOf("a.lua"));

		fixture.Locked(tracker => tracker.ReserveDelete(document));

		Assert.ThrowsExactly<ArgumentException>(() => fixture.Locked(tracker => tracker.ReserveDelete(document)));
	}

	[TestMethod]
	public void CompleteDelete_RemovesTheReservationAndCompletesTheWaiter()
	{
		var fixture = new TestHost();

		LogicalDocument document = CreateDocument(PathOf("a.lua"));
		DeleteReservation reservation = fixture.Locked(tracker => tracker.ReserveDelete(document));

		fixture.Locked(tracker => tracker.CompleteDelete(document));

		Assert.IsFalse(fixture.Locked(tracker => tracker.IsDeleteActive(document)));
		Assert.IsFalse(fixture.Locked(tracker => tracker.TryGetDeleteReservation(document, out _)));
		Assert.IsTrue(reservation.Completion.Task.IsCompletedSuccessfully);
	}

	[TestMethod]
	public void CompleteDelete_CalledTwice_LeavesNoReservationAndAllowsTheNextOne()
	{
		var fixture = new TestHost();

		LogicalDocument document = CreateDocument(PathOf("a.lua"));

		fixture.Locked(tracker => tracker.ReserveDelete(document));
		fixture.Locked(tracker => tracker.CompleteDelete(document));
		fixture.Locked(tracker => tracker.CompleteDelete(document));

		DeleteReservation next = fixture.Locked(tracker => tracker.ReserveDelete(document));

		Assert.IsFalse(next.Completion.Task.IsCompleted);
		Assert.IsTrue(fixture.Locked(tracker => tracker.IsDeleteActive(document)));
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task CompletingAReservation_ResumesItsWaitersOutsideTheLock()
	{
		var fixture = new TestHost();

		LogicalDocument document = CreateDocument(PathOf("a.lua"));

		var directory = new DirectoryOperationReservation(PathOf("dir"));

		OpenReservation open = fixture.Locked(tracker => tracker.ReserveOpen(PathOf("open.lua")));
		DestinationReservation destination = fixture.Locked(tracker => tracker.ReserveDestination(PathOf("destination.lua")));
		DeleteReservation delete = fixture.Locked(tracker => tracker.ReserveDelete(document));

		fixture.Locked(tracker => tracker.BeginDirectoryOperation(directory, []));

		Task<bool>[] resumedUnderLock =
		[
			TestContinuations.RunsUnderLock(open.Completion.Task, fixture.SyncRoot),
			TestContinuations.RunsUnderLock(destination.Completion.Task, fixture.SyncRoot),
			TestContinuations.RunsUnderLock(delete.Completion.Task, fixture.SyncRoot),
			TestContinuations.RunsUnderLock(directory.Completion.Task, fixture.SyncRoot),
		];

		fixture.Locked(tracker =>
		{
			tracker.CompleteOpenReservation(PathOf("open.lua"), open, Opened());
			tracker.CompleteDestinationReservation(destination);
			tracker.CompleteDelete(document);
			tracker.CompleteDirectoryOperation(directory);
		});

		bool[] results = await Task.WhenAll(resumedUnderLock).WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);

		Assert.DoesNotContain(true, results);
	}

	[TestMethod]
	public void CompleteDelete_ForADocumentWithoutAReservation_DoesNothing()
	{
		var fixture = new TestHost();
		LogicalDocument document = CreateDocument(PathOf("a.lua"));

		fixture.Locked(tracker => tracker.CompleteDelete(document));

		Assert.IsFalse(fixture.Locked(tracker => tracker.IsDeleteActive(document)));
	}

	[TestMethod]
	public void ClearAfterDisposal_DropsTheDeleteReservations()
	{
		var fixture = new TestHost();

		LogicalDocument document = CreateDocument(PathOf("a.lua"));

		fixture.Locked(tracker => tracker.ReserveDelete(document));
		fixture.Locked(tracker => tracker.ClearAfterDisposal());

		Assert.IsFalse(fixture.Locked(tracker => tracker.IsDeleteActive(document)));
	}

	private static DirectoryOperationReservation CreateDirectoryOperation(string directoryName) => new(PathOf(directoryName));
}
