using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>Covers the directory runner directly: the reservation order, the validation, the gates and the release on every exit.</summary>
[TestClass]
public sealed class DirectoryOperationRunnerTests
{
	private static readonly string s_root = Path.Combine(Path.GetTempPath(), "directory-driver-tests");

	private sealed class TestHost : StoreTestHost
	{
		internal TestHost(LocalPathComparisonPolicy? pathComparison = null)
			: base(pathComparison)
		{
			Lifetime = new StoreLifetime();
			Hooks = new WorkspaceDocumentStoreTestHooks(SyncRoot);

			Runner = new DirectoryOperationRunner(
				SyncRoot,
				Tracker,
				OperationGate,
				testHooks: Hooks,
				Options,
				FileSystem,
				io: new StampedFileWorker(FileSystem, Lifetime),
				Lifetime
			);
		}

		internal ScriptedWorkspaceFileSystem FileSystem { get; } = new();

		internal StoreLifetime Lifetime { get; }

		internal WorkspaceDocumentStoreTestHooks Hooks { get; }

		internal DirectoryOperationRunner Runner { get; }

		internal LogicalDocument Add(string firstSegment, params string[] segments) => AddDocument(PathOf([firstSegment, .. segments]));

		internal LogicalDocument AddStamped(FileStamp stamp, string firstSegment, params string[] segments) =>
			AddDocument(PathOf([firstSegment, .. segments]), stamp);

		// Asserts that an exit released everything the operation published: its gates, its delete reservations, its directory reservation
		// and its registration with the stop gate.
		internal void AssertReleased(string directory, params LogicalDocument[] documents)
		{
			foreach (LogicalDocument document in documents)
			{
				Assert.AreEqual(1, document.DiskOperationGate.CurrentCount, document.DocumentId);
				Assert.IsFalse(Locked(tracker => tracker.IsDeleteActive(document)), document.DocumentId);
			}

			Assert.IsNull(Locked(tracker => tracker.FindDirectoryOperationUnder(directory)));
			Assert.IsEmpty(Stop());
		}
	}

	public required TestContext TestContext { get; set; }

	private static string PathOf(params string[] segments) => Path.Combine([s_root, .. segments]);

	private static FileStamp StampOf(string hash) => new(Exists: true, Length: 1, LastWriteTimeUtc: DateTime.UnixEpoch, ContentHash: hash);

	private Task<WorkspaceDocumentDirectoryRenameResult> RenameAsync(TestHost fixture, string source, string destination) =>
		fixture.Runner.RenameAsync(new WorkspaceDocumentDirectoryRenameRequest(PathOf(source), PathOf(destination)), TestContext.CancellationToken);

	private Task<WorkspaceDocumentDirectoryDeleteResult> DeleteAsync(TestHost fixture, string directory) =>
		fixture.Runner.DeleteAsync(new WorkspaceDocumentDirectoryDeleteRequest(PathOf(directory)), TestContext.CancellationToken);

	[TestMethod]
	public async Task RenameAsync_MovesTheTreeAndRebasesEveryDescendant()
	{
		var fixture = new TestHost();

		LogicalDocument first = fixture.Add("src", "a.txt");
		LogicalDocument nested = fixture.Add("src", "sub", "b.txt");
		LogicalDocument sibling = fixture.Add("srcother", "c.txt");

		WorkspaceDocumentDirectoryRenameResult result = await RenameAsync(fixture, "src", "dest").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.Outcome);
		Assert.AreSequenceEqual([PathOf("dest", "a.txt"), PathOf("dest", "sub", "b.txt")], result.Snapshots.Select(snapshot => snapshot.DocumentId));
		Assert.AreEqual(PathOf("dest", "a.txt"), first.DocumentId);
		Assert.AreEqual(PathOf("dest", "sub", "b.txt"), nested.DocumentId);
		Assert.AreEqual(PathOf("srcother", "c.txt"), sibling.DocumentId);
		Assert.AreEqual(1, first.Version);
		Assert.AreEqual(0, sibling.Version);

		Assert.IsTrue(
			fixture.Locked(tracker =>
				tracker.TryGetDocument(PathOf("dest", "a.txt"), out LogicalDocument? tracked) && ReferenceEquals(tracked, first)
			)
		);

		Assert.IsFalse(fixture.Locked(tracker => tracker.TryGetDocument(PathOf("src", "a.txt"), out _)));

		fixture.AssertReleased(PathOf("src"), first, nested, sibling);
	}

	[TestMethod]
	public async Task RenameAsync_ScansTheDescendantsBeforeTheMoveAndCapturesTheMovedStampsAfterIt()
	{
		var fixture = new TestHost();

		fixture.Add("src", "a.txt");
		fixture.Add("src", "sub", "b.txt");

		await RenameAsync(fixture, "src", "dest").ConfigureAwait(false);

		string[] expected =
		[
			$"Capture:{PathOf("src", "a.txt")}",
			$"Capture:{PathOf("src", "sub", "b.txt")}",
			$"MoveDirectory:{PathOf("src")}",
			$"Capture:{PathOf("dest", "a.txt")}",
			$"Capture:{PathOf("dest", "sub", "b.txt")}",
		];

		Assert.AreSequenceEqual(expected, fixture.FileSystem.Calls);
	}

	[TestMethod]
	public async Task RenameAsync_HandsTheNormalizedDirectoryIdsToTheFileSystemAndKeepsTheCallersSpellingForTheDisplayPath()
	{
		var fixture = new TestHost();

		LogicalDocument document = fixture.Add("src", "a.txt");

		string movedSource = null!;
		string movedDestination = null!;

		fixture.FileSystem.OnMoveDirectory = (source, destination, _) =>
		{
			movedSource = source;
			movedDestination = destination;

			return Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved));
		};

		string spelledDestination = Path.Combine(PathOf("dest"), ".");

		await fixture
			.Runner.RenameAsync(
				new WorkspaceDocumentDirectoryRenameRequest(PathOf("src") + Path.DirectorySeparatorChar, spelledDestination),
				TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		Assert.AreEqual(PathOf("src"), movedSource);
		Assert.AreEqual(PathOf("dest"), movedDestination);
		Assert.AreEqual(Path.Combine(spelledDestination, "a.txt"), document.DisplayPath);
	}

	[TestMethod]
	public async Task RenameAsync_ToTheSameNormalizedDirectory_ReportsNoChangeWithTheCurrentSnapshotsAndTouchesNothing()
	{
		var fixture = new TestHost();
		LogicalDocument document = fixture.Add("src", "a.txt");

		WorkspaceDocumentDirectoryRenameResult result = await fixture
			.Runner.RenameAsync(
				new WorkspaceDocumentDirectoryRenameRequest(PathOf("src"), PathOf("src") + Path.DirectorySeparatorChar),
				TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.NoChange, result.Outcome);
		Assert.AreEqual(document.DocumentId, result.Snapshots.Single().DocumentId);
		Assert.IsEmpty(fixture.FileSystem.Calls);

		fixture.AssertReleased(PathOf("src"), document);
	}

	[TestMethod]
	public async Task RenameAsync_WithAnUnnormalizablePath_ReportsInvalidPath()
	{
		var fixture = new TestHost();

		WorkspaceDocumentDirectoryRenameResult result = await fixture
			.Runner.RenameAsync(new WorkspaceDocumentDirectoryRenameRequest("relative", PathOf("dest")), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.InvalidPath, result.Outcome);
		Assert.IsEmpty(result.Snapshots);
		Assert.IsEmpty(fixture.FileSystem.Calls);
	}

	[TestMethod]
	public async Task RenameAsync_WithAPreCanceledToken_ReportsCanceledWithTheCurrentSnapshotsBeforePublishingAReservation()
	{
		var fixture = new TestHost();
		using var cancellation = new CancellationTokenSource();

		bool reservationPublished = false;

		LogicalDocument document = fixture.Add("src", "a.txt");

		fixture.Hooks.DirectoryOperationBeforeLifetimeLink = () => reservationPublished = true;

		await cancellation.CancelAsync().ConfigureAwait(false);

		WorkspaceDocumentDirectoryRenameResult result = await fixture
			.Runner.RenameAsync(new WorkspaceDocumentDirectoryRenameRequest(PathOf("src"), PathOf("dest")), cancellation.Token)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Canceled, result.Outcome);
		Assert.AreEqual(document.DocumentId, result.Snapshots.Single().DocumentId);
		Assert.IsFalse(reservationPublished);
		Assert.IsEmpty(fixture.FileSystem.Calls);

		fixture.AssertReleased(PathOf("src"), document);
	}

	[TestMethod]
	public async Task RenameAsync_WithAPreCanceledTokenAfterTheStoreBeganStopping_Throws()
	{
		var fixture = new TestHost();
		using var cancellation = new CancellationTokenSource();

		fixture.Add("src", "a.txt");
		fixture.Stop();

		await cancellation.CancelAsync().ConfigureAwait(false);

		await Assert
			.ThrowsExactlyAsync<ObjectDisposedException>(() =>
				fixture.Runner.RenameAsync(new WorkspaceDocumentDirectoryRenameRequest(PathOf("src"), PathOf("dest")), cancellation.Token)
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task RenameAsync_WhenADocumentOutsideTheMovingSetIsTrackedUnderTheDestination_ReportsDestinationInUse()
	{
		var fixture = new TestHost();

		LogicalDocument moving = fixture.Add("src", "a.txt");
		LogicalDocument occupant = fixture.Add("dest", "other.txt");

		WorkspaceDocumentDirectoryRenameResult result = await RenameAsync(fixture, "src", "dest").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.DestinationInUse, result.Outcome);
		Assert.AreEqual(moving.DocumentId, result.Snapshots.Single().DocumentId);
		Assert.IsEmpty(fixture.FileSystem.Calls);

		fixture.AssertReleased(PathOf("src"), moving, occupant);
	}

	[TestMethod]
	public async Task RenameAsync_WhenAnotherDirectoryOperationCoversTheDestination_ReportsDestinationBusy()
	{
		var fixture = new TestHost();
		LogicalDocument moving = fixture.Add("src", "a.txt");
		var other = new DirectoryOperationReservation(PathOf("dest"));

		fixture.Locked(tracker => tracker.BeginDirectoryOperation(other, []));

		WorkspaceDocumentDirectoryRenameResult result = await RenameAsync(fixture, "src", "dest").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.DestinationBusy, result.Outcome);
		Assert.IsEmpty(fixture.FileSystem.Calls);
		Assert.AreEqual(1, moving.DiskOperationGate.CurrentCount);
		Assert.AreSame(other, fixture.Locked(tracker => tracker.FindDirectoryOperationUnder(PathOf("dest"))));
	}

	[TestMethod]
	public async Task RenameAsync_OfADirectoryWithoutTrackedDocuments_WhenAnotherDirectoryOperationCoversTheDestination_ReportsDestinationBusy()
	{
		var fixture = new TestHost();
		var other = new DirectoryOperationReservation(PathOf("dest"));

		fixture.Locked(tracker => tracker.BeginDirectoryOperation(other, []));

		WorkspaceDocumentDirectoryRenameResult result = await RenameAsync(fixture, "src", "dest").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.DestinationBusy, result.Outcome);
		Assert.IsEmpty(fixture.FileSystem.Calls);
	}

	[TestMethod]
	public async Task RenameAsync_WhenARebasedPathIsReserved_ReportsDestinationBusy()
	{
		var fixture = new TestHost();
		LogicalDocument moving = fixture.Add("src", "a.txt");

		fixture.Locked(tracker => tracker.ReserveDestination(PathOf("dest", "a.txt")));

		WorkspaceDocumentDirectoryRenameResult result = await RenameAsync(fixture, "src", "dest").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.DestinationBusy, result.Outcome);
		Assert.IsEmpty(fixture.FileSystem.Calls);
		Assert.AreEqual(1, moving.DiskOperationGate.CurrentCount);
	}

	[TestMethod]
	public async Task RenameAsync_IntoItsOwnSubtree_IgnoresItsOwnReservation()
	{
		var fixture = new TestHost();
		LogicalDocument document = fixture.Add("src", "a.txt");

		WorkspaceDocumentDirectoryRenameResult result = await RenameAsync(fixture, "src", Path.Combine("src", "inner")).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(PathOf("src", "inner", "a.txt"), document.DocumentId);

		fixture.AssertReleased(PathOf("src"), document);
	}

	[TestMethod]
	public async Task RenameAsync_WhenADescendantGateIsHeld_ReportsOperationInProgressAndReleasesTheGatesItTook()
	{
		var fixture = new TestHost();

		LogicalDocument first = fixture.Add("src", "a.txt");
		LogicalDocument second = fixture.Add("src", "b.txt");

		Assert.IsTrue(second.DiskOperationGate.Wait(0));

		WorkspaceDocumentDirectoryRenameResult result = await RenameAsync(fixture, "src", "dest").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.OperationInProgress, result.Outcome);
		Assert.AreEqual(1, first.DiskOperationGate.CurrentCount);
		Assert.AreEqual(0, second.DiskOperationGate.CurrentCount);
		Assert.IsEmpty(fixture.FileSystem.Calls);
		Assert.IsNull(fixture.Locked(tracker => tracker.FindDirectoryOperationUnder(PathOf("src"))));
		Assert.IsFalse(fixture.Locked(tracker => tracker.IsDestinationUnavailable(PathOf("dest", "a.txt"))));
	}

	[TestMethod]
	public async Task RenameAsync_WhenADescendantChangedOnDisk_ReportsExternalFileConflictNamingTheDescendant()
	{
		var fixture = new TestHost();

		LogicalDocument first = fixture.AddStamped(StampOf("tracked"), "src", "a.txt");
		LogicalDocument second = fixture.AddStamped(StampOf("tracked"), "src", "b.txt");

		fixture.FileSystem.OnCaptureStamp = (path, _) => Task.FromResult(path == second.DocumentId ? StampOf("changed") : StampOf("tracked"));

		WorkspaceDocumentDirectoryRenameResult result = await RenameAsync(fixture, "src", "dest").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ExternalFileConflict, result.Failure?.Code);
		Assert.IsNotNull(result.Failure);
		Assert.Contains(second.DocumentId, result.Failure.Message);
		Assert.DoesNotContain("MoveDirectory", string.Join(' ', fixture.FileSystem.Calls));

		fixture.AssertReleased(PathOf("src"), first, second);
	}

	[TestMethod]
	[DataRow(WorkspaceFileMoveOutcome.DestinationExists, WorkspaceDocumentDirectoryRenameOutcome.DestinationExists)]
	[DataRow(WorkspaceFileMoveOutcome.ExternalFileConflict, WorkspaceDocumentDirectoryRenameOutcome.ExternalFileConflict)]
	[DataRow(WorkspaceFileMoveOutcome.Canceled, WorkspaceDocumentDirectoryRenameOutcome.Canceled)]
	[DataRow(WorkspaceFileMoveOutcome.MoveStateUnknown, WorkspaceDocumentDirectoryRenameOutcome.MoveStateUnknown)]
	[DataRow(WorkspaceFileMoveOutcome.MoveFailed, WorkspaceDocumentDirectoryRenameOutcome.MoveFailed)]
	public async Task RenameAsync_WhenTheFileSystemDoesNotMove_MapsTheOutcomeKeepsTheDocumentsAndReleasesEverything(
		WorkspaceFileMoveOutcome moveOutcome,
		WorkspaceDocumentDirectoryRenameOutcome expected
	)
	{
		var fixture = new TestHost();
		LogicalDocument document = fixture.Add("src", "a.txt");
		var failure = new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.MoveFailed, "The move failed.");

		fixture.FileSystem.OnMoveDirectory = (_, _, _) => Task.FromResult(new WorkspaceFileMoveResult(moveOutcome, Failure: failure));

		WorkspaceDocumentDirectoryRenameResult result = await RenameAsync(fixture, "src", "dest").ConfigureAwait(false);

		Assert.AreEqual(expected, result.Outcome);
		Assert.AreSame(failure, result.Failure);
		Assert.AreEqual(PathOf("src", "a.txt"), document.DocumentId);
		Assert.AreEqual(0, document.Version);

		fixture.AssertReleased(PathOf("src"), document);
	}

	[TestMethod]
	public async Task RenameAsync_WhenTheMovedStampCannotBeCaptured_KeepsTheTrackedStamp()
	{
		var fixture = new TestHost();
		LogicalDocument document = fixture.AddStamped(StampOf("tracked"), "src", "a.txt");

		fixture.FileSystem.OnCaptureStamp = (path, _) =>
			path == document.DocumentId ? Task.FromResult(StampOf("tracked")) : throw new IOException("The destination is unreadable.");

		WorkspaceDocumentDirectoryRenameResult result = await RenameAsync(fixture, "src", "dest").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(StampOf("tracked"), document.OnDiskStamp);
	}

	[TestMethod]
	public async Task RenameAsync_RecapturesTheStampOfEveryMovedDescendant()
	{
		var fixture = new TestHost();
		LogicalDocument document = fixture.AddStamped(StampOf("tracked"), "src", "a.txt");

		fixture.FileSystem.OnCaptureStamp = (path, _) =>
			Task.FromResult(path.StartsWith(PathOf("dest"), StringComparison.Ordinal) ? StampOf("moved") : StampOf("tracked"));

		await RenameAsync(fixture, "src", "dest").ConfigureAwait(false);

		Assert.AreEqual(StampOf("moved"), document.OnDiskStamp);
	}

	[TestMethod]
	public async Task RenameAsync_WhenTheFileSystemThrows_ReportsMoveFailedAndAccessDeniedForAPermissionFailure()
	{
		var fixture = new TestHost();
		LogicalDocument document = fixture.Add("src", "a.txt");
		var exception = new IOException("The disk failed.");

		fixture.FileSystem.OnMoveDirectory = (_, _, _) => throw exception;

		WorkspaceDocumentDirectoryRenameResult failed = await RenameAsync(fixture, "src", "dest").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.MoveFailed, failed.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.MoveFailed, failed.Failure?.Code);
		Assert.AreSame(exception, failed.Failure?.Exception);

		fixture.FileSystem.OnMoveDirectory = (_, _, _) => throw new UnauthorizedAccessException("Denied.");

		WorkspaceDocumentDirectoryRenameResult denied = await RenameAsync(fixture, "src", "dest").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceOperationFailureCodes.AccessDenied, denied.Failure?.Code);
		fixture.AssertReleased(PathOf("src"), document);
	}

	[TestMethod]
	public async Task RenameAsync_WhenTheLifetimeEndsBeforeTheOperationLinksToIt_ReportsCanceledWithoutSnapshots()
	{
		var fixture = new TestHost();
		LogicalDocument document = fixture.Add("src", "a.txt");

		fixture.Hooks.DirectoryOperationBeforeLifetimeLink = fixture.Lifetime.Dispose;

		WorkspaceDocumentDirectoryRenameResult result = await RenameAsync(fixture, "src", "dest").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Canceled, result.Outcome);
		Assert.IsEmpty(result.Snapshots);
		Assert.IsEmpty(fixture.FileSystem.Calls);

		fixture.AssertReleased(PathOf("src"), document);
	}

	[TestMethod]
	public async Task RenameAsync_WhenThePostGateSetupThrows_ReleasesTheGatesAndReportsMoveFailed()
	{
		var fixture = new TestHost();

		LogicalDocument first = fixture.Add("src", "a.txt");
		LogicalDocument second = fixture.Add("src", "b.txt");

		fixture.Hooks.DirectoryOperationPostGateSetup = () => throw new InvalidOperationException("The setup failed.");

		WorkspaceDocumentDirectoryRenameResult result = await RenameAsync(fixture, "src", "dest").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.MoveFailed, result.Outcome);
		Assert.AreEqual("The setup failed.", result.Failure?.Message);
		Assert.IsFalse(fixture.Locked(tracker => tracker.IsDestinationUnavailable(PathOf("dest", "a.txt"))));

		fixture.AssertReleased(PathOf("src"), first, second);
	}

	[TestMethod]
	public async Task RenameAsync_AfterTheStoreBeganStopping_Throws()
	{
		var fixture = new TestHost();

		fixture.Add("src", "a.txt");
		fixture.Stop();

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => RenameAsync(fixture, "src", "dest")).ConfigureAwait(false);
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task RenameAsync_WaitsForAnInFlightOpenInTheSubtreeBeforeMoving()
	{
		var fixture = new TestHost();

		LogicalDocument document = fixture.Add("src", "a.txt");
		OpenReservation open = fixture.Locked(tracker => tracker.ReserveOpen(PathOf("src", "new.txt")));

		Task<WorkspaceDocumentDirectoryRenameResult> rename = RenameAsync(fixture, "src", "dest");

		Task finishedFirst = await Task.WhenAny(rename, Task.Delay(250, TestContext.CancellationToken)).ConfigureAwait(false);

		Assert.AreNotSame(rename, finishedFirst);
		Assert.DoesNotContain("MoveDirectory", string.Join(' ', fixture.FileSystem.Calls));

		fixture.Locked(tracker =>
			tracker.CompleteOpenReservation(
				PathOf("src", "new.txt"),
				open,
				new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.Canceled, null)
			)
		);

		WorkspaceDocumentDirectoryRenameResult result = await rename.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.Outcome);
		fixture.AssertReleased(PathOf("src"), document);
	}

	[TestMethod]
	public async Task RenameAsync_PublishesItsDirectoryReservationBeforeAnythingElseRuns()
	{
		var fixture = new TestHost();
		DirectoryOperationReservation? observed = null;

		fixture.Add("src", "a.txt");

		fixture.Hooks.DirectoryOperationBeforeLifetimeLink = () =>
			observed = fixture.Locked(tracker => tracker.FindDirectoryOperationUnder(PathOf("src", "a.txt")));

		await RenameAsync(fixture, "src", "dest").ConfigureAwait(false);

		Assert.IsNotNull(observed);
		Assert.AreEqual(PathOf("src"), observed.DirectoryId);
	}

	[TestMethod]
	public async Task RenameAsync_WhileTheFileSystemMoves_KeepsTheDirectoryAndDestinationReservations()
	{
		var fixture = new TestHost();

		bool sourceReserved = false;
		bool destinationReserved = false;

		fixture.Add("src", "a.txt");

		fixture.FileSystem.OnMoveDirectory = (_, _, _) =>
		{
			sourceReserved = fixture.Locked(tracker => tracker.FindDirectoryOperationUnder(PathOf("src", "a.txt")) is not null);
			destinationReserved = fixture.Locked(tracker => tracker.IsDestinationUnavailable(PathOf("dest", "a.txt")));

			return Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved));
		};

		await RenameAsync(fixture, "src", "dest").ConfigureAwait(false);

		Assert.IsTrue(sourceReserved);
		Assert.IsTrue(destinationReserved);
		Assert.IsFalse(fixture.Locked(tracker => tracker.IsDestinationUnavailable(PathOf("dest", "a.txt"))));
	}

	[TestMethod]
	public async Task RenameAsync_ThatChangesOnlyTheCaseUnderACaseInsensitiveComparison_IsHandedToTheFileSystem()
	{
		var fixture = new TestHost(LocalPathComparisonPolicy.CaseInsensitive);

		LogicalDocument document = fixture.Add("src", "a.txt");

		WorkspaceDocumentDirectoryRenameResult result = await RenameAsync(fixture, "src", "SRC").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(PathOf("SRC", "a.txt"), document.DocumentId);
		Assert.Contains($"MoveDirectory:{PathOf("src")}", fixture.FileSystem.Calls);

		fixture.AssertReleased(PathOf("src"), document);
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task RenameAsync_WhenTheCallerCancelsWhileWaitingForAnOpen_ReportsCanceledWithTheTrackedDescendants()
	{
		var fixture = new TestHost();
		using var cancellation = new CancellationTokenSource();

		LogicalDocument document = fixture.Add("src", "a.txt");

		fixture.Locked(tracker => tracker.ReserveOpen(PathOf("src", "new.txt")));

		Task<WorkspaceDocumentDirectoryRenameResult> rename = fixture.Runner.RenameAsync(
			new WorkspaceDocumentDirectoryRenameRequest(PathOf("src"), PathOf("dest")),
			cancellation.Token
		);

		await cancellation.CancelAsync().ConfigureAwait(false);

		WorkspaceDocumentDirectoryRenameResult result = await rename.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Canceled, result.Outcome);
		Assert.AreEqual(document.DocumentId, result.Snapshots.Single().DocumentId);
		Assert.IsEmpty(fixture.FileSystem.Calls);
		Assert.IsNull(fixture.Locked(tracker => tracker.FindDirectoryOperationUnder(PathOf("src"))));
	}

	[TestMethod]
	public async Task RenameAsync_WhenTheCallerCancelsBeforeTheOperationLinksToIt_ReportsCanceledWithoutMoving()
	{
		var fixture = new TestHost();
		using var cancellation = new CancellationTokenSource();

		LogicalDocument document = fixture.Add("src", "a.txt");

		fixture.Hooks.DirectoryOperationBeforeLifetimeLink = cancellation.Cancel;

		WorkspaceDocumentDirectoryRenameResult result = await fixture
			.Runner.RenameAsync(new WorkspaceDocumentDirectoryRenameRequest(PathOf("src"), PathOf("dest")), cancellation.Token)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Canceled, result.Outcome);
		Assert.AreEqual(document.DocumentId, result.Snapshots.Single().DocumentId);
		Assert.IsEmpty(fixture.FileSystem.Calls);

		fixture.AssertReleased(PathOf("src"), document);
	}

	[TestMethod]
	public async Task RenameAsync_ToTheSameDirectoryAfterTheStoreBeganStopping_Throws()
	{
		var fixture = new TestHost();

		fixture.Add("src", "a.txt");
		fixture.Stop();

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => RenameAsync(fixture, "src", "src")).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task RenameAsync_WhenTheCallerCancelsWhileTheFileSystemMoves_ReportsCanceledAndKeepsTheDocuments()
	{
		var fixture = new TestHost();
		using var cancellation = new CancellationTokenSource();

		LogicalDocument document = fixture.Add("src", "a.txt");

		fixture.FileSystem.OnMoveDirectory = (_, _, token) =>
		{
			cancellation.Cancel();
			token.ThrowIfCancellationRequested();

			return Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved));
		};

		WorkspaceDocumentDirectoryRenameResult result = await fixture
			.Runner.RenameAsync(new WorkspaceDocumentDirectoryRenameRequest(PathOf("src"), PathOf("dest")), cancellation.Token)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Canceled, result.Outcome);
		Assert.AreEqual(document.DocumentId, result.Snapshots.Single().DocumentId);
		Assert.AreEqual(PathOf("src", "a.txt"), document.DocumentId);

		fixture.AssertReleased(PathOf("src"), document);
	}

	[TestMethod]
	public async Task RenameAsync_WhenTheCallerCancelsAfterTheFileSystemMoved_StillRebasesTheDocuments()
	{
		var fixture = new TestHost();
		using var cancellation = new CancellationTokenSource();

		LogicalDocument document = fixture.AddStamped(StampOf("tracked"), "src", "a.txt");

		fixture.FileSystem.OnCaptureStamp = (_, token) =>
		{
			token.ThrowIfCancellationRequested();
			return Task.FromResult(StampOf("tracked"));
		};

		fixture.FileSystem.OnMoveDirectory = (_, _, _) =>
		{
			cancellation.Cancel();
			return Task.FromResult(new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved));
		};

		WorkspaceDocumentDirectoryRenameResult result = await fixture
			.Runner.RenameAsync(new WorkspaceDocumentDirectoryRenameRequest(PathOf("src"), PathOf("dest")), cancellation.Token)
			.ConfigureAwait(false);

		// The tree already moved, so Canceled would leave the documents at paths that are gone.
		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(PathOf("dest", "a.txt"), document.DocumentId);
		Assert.AreEqual(StampOf("tracked"), document.OnDiskStamp);

		fixture.AssertReleased(PathOf("src"), document);
	}

	[TestMethod]
	public async Task RenameAsync_PublishesNoDestinationReservationUntilEveryGateIsHeld()
	{
		var fixture = new TestHost();

		bool gatesHeld = false;
		bool destinationReserved = true;

		LogicalDocument first = fixture.Add("src", "a.txt");
		LogicalDocument second = fixture.Add("src", "b.txt");

		fixture.Hooks.DirectoryOperationPostGateSetup = () =>
		{
			gatesHeld = first.DiskOperationGate.CurrentCount == 0 && second.DiskOperationGate.CurrentCount == 0;

			destinationReserved = fixture.Locked(tracker =>
				tracker.IsDestinationUnavailable(PathOf("dest", "a.txt")) || tracker.IsDestinationUnavailable(PathOf("dest", "b.txt"))
			);
		};

		await RenameAsync(fixture, "src", "dest").ConfigureAwait(false);

		Assert.IsTrue(gatesHeld);
		Assert.IsFalse(destinationReserved);
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task RenameAsync_WhenTheStopBeginsWhileItWaitsForAnOpen_ReportsCanceledWithoutMoving()
	{
		var fixture = new TestHost();

		LogicalDocument document = fixture.Add("src", "a.txt");
		OpenReservation open = fixture.Locked(tracker => tracker.ReserveOpen(PathOf("src", "new.txt")));

		Task<WorkspaceDocumentDirectoryRenameResult> rename = RenameAsync(fixture, "src", "dest");

		fixture.Stop();

		fixture.Locked(tracker =>
			tracker.CompleteOpenReservation(
				PathOf("src", "new.txt"),
				open,
				new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.Canceled, null)
			)
		);

		WorkspaceDocumentDirectoryRenameResult result = await rename.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Canceled, result.Outcome);
		Assert.IsEmpty(result.Snapshots);
		Assert.IsEmpty(fixture.FileSystem.Calls);
		Assert.AreEqual(PathOf("src", "a.txt"), document.DocumentId);
		Assert.IsNull(fixture.Locked(tracker => tracker.FindDirectoryOperationUnder(PathOf("src"))));
	}

	[TestMethod]
	public async Task DeleteAsync_RemovesEveryDescendantFromTracking()
	{
		var fixture = new TestHost();

		LogicalDocument first = fixture.Add("src", "a.txt");
		LogicalDocument nested = fixture.Add("src", "sub", "b.txt");
		LogicalDocument sibling = fixture.Add("srcother", "c.txt");

		WorkspaceDocumentDirectoryDeleteResult result = await DeleteAsync(fixture, "src").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Deleted, result.Outcome);
		Assert.AreSequenceEqual([first.DocumentId, nested.DocumentId], result.Snapshots.Select(snapshot => snapshot.DocumentId));
		Assert.IsFalse(fixture.Locked(tracker => tracker.TryGetDocument(first.DocumentId, out _)));
		Assert.IsFalse(fixture.Locked(tracker => tracker.TryGetDocument(nested.DocumentId, out _)));
		Assert.IsTrue(fixture.Locked(tracker => tracker.TryGetDocument(sibling.DocumentId, out _)));

		string[] expectedCalls = [$"Capture:{first.DocumentId}", $"Capture:{nested.DocumentId}", $"DeleteDirectory:{PathOf("src")}"];

		Assert.AreSequenceEqual(expectedCalls, fixture.FileSystem.Calls);

		fixture.AssertReleased(PathOf("src"), first, nested, sibling);
	}

	[TestMethod]
	public async Task DeleteAsync_WithAnUnnormalizablePath_ReportsInvalidPath()
	{
		var fixture = new TestHost();

		WorkspaceDocumentDirectoryDeleteResult result = await fixture
			.Runner.DeleteAsync(new WorkspaceDocumentDirectoryDeleteRequest("relative"), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.InvalidPath, result.Outcome);
		Assert.IsEmpty(fixture.FileSystem.Calls);
	}

	[TestMethod]
	public async Task DeleteAsync_WithAPreCanceledToken_ReportsCanceledWithTheCurrentSnapshotsAndDeletesNothing()
	{
		var fixture = new TestHost();
		using var cancellation = new CancellationTokenSource();

		bool reservationPublished = false;

		LogicalDocument document = fixture.Add("src", "a.txt");

		fixture.Hooks.DirectoryOperationBeforeLifetimeLink = () => reservationPublished = true;

		await cancellation.CancelAsync().ConfigureAwait(false);

		WorkspaceDocumentDirectoryDeleteResult result = await fixture
			.Runner.DeleteAsync(new WorkspaceDocumentDirectoryDeleteRequest(PathOf("src")), cancellation.Token)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Canceled, result.Outcome);
		Assert.AreEqual(document.DocumentId, result.Snapshots.Single().DocumentId);
		Assert.IsFalse(reservationPublished);
		Assert.IsEmpty(fixture.FileSystem.Calls);
	}

	[TestMethod]
	public async Task DeleteAsync_WithAPreCanceledTokenAfterTheStoreBeganStopping_Throws()
	{
		var fixture = new TestHost();
		using var cancellation = new CancellationTokenSource();

		fixture.Add("src", "a.txt");
		fixture.Stop();

		await cancellation.CancelAsync().ConfigureAwait(false);

		await Assert
			.ThrowsExactlyAsync<ObjectDisposedException>(() =>
				fixture.Runner.DeleteAsync(new WorkspaceDocumentDirectoryDeleteRequest(PathOf("src")), cancellation.Token)
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task DeleteAsync_WhenADescendantReservationCannotBePublished_CompletesOnlyTheReservationsItMade()
	{
		var fixture = new TestHost();

		LogicalDocument first = fixture.Add("src", "a.txt");
		LogicalDocument second = fixture.Add("src", "b.txt");

		// A reservation that no gate protects makes the second reservation throw, which no caller that holds the gate can cause.
		DeleteReservation foreign = fixture.Locked(tracker => tracker.ReserveDelete(second));

		WorkspaceDocumentDirectoryDeleteResult result = await DeleteAsync(fixture, "src").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.DeleteFailed, result.Outcome);
		Assert.IsFalse(fixture.Locked(tracker => tracker.IsDeleteActive(first)));
		Assert.IsTrue(fixture.Locked(tracker => tracker.IsDeleteActive(second)));
		Assert.IsFalse(foreign.Completion.Task.IsCompleted);
		Assert.AreEqual(1, first.DiskOperationGate.CurrentCount);
		Assert.AreEqual(1, second.DiskOperationGate.CurrentCount);
	}

	[TestMethod]
	public async Task DeleteAsync_PublishesTheDeleteReservationsOnlyAfterEveryGateIsAcquired()
	{
		var fixture = new TestHost();

		LogicalDocument first = fixture.Add("src", "a.txt");
		LogicalDocument second = fixture.Add("src", "b.txt");

		Assert.IsTrue(second.DiskOperationGate.Wait(0));

		WorkspaceDocumentDirectoryDeleteResult result = await DeleteAsync(fixture, "src").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.OperationInProgress, result.Outcome);
		Assert.IsFalse(fixture.Locked(tracker => tracker.IsDeleteActive(first)));
		Assert.IsFalse(fixture.Locked(tracker => tracker.IsDeleteActive(second)));
		Assert.AreEqual(1, first.DiskOperationGate.CurrentCount);
		Assert.DoesNotContain("DeleteDirectory", string.Join(' ', fixture.FileSystem.Calls));
	}

	[TestMethod]
	public async Task DeleteAsync_ThatLosesTheGateRace_LeavesTheDeleteReservationOfTheOperationHoldingTheGate()
	{
		var fixture = new TestHost();

		LogicalDocument first = fixture.Add("src", "a.txt");
		LogicalDocument second = fixture.Add("src", "b.txt");

		// A file delete holds the second document: its gate and its reservation.
		Assert.IsTrue(second.DiskOperationGate.Wait(0));

		DeleteReservation owned = fixture.Locked(tracker => tracker.ReserveDelete(second));

		WorkspaceDocumentDirectoryDeleteResult result = await DeleteAsync(fixture, "src").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.OperationInProgress, result.Outcome);
		Assert.IsTrue(fixture.Locked(tracker => tracker.IsDeleteActive(second)));
		Assert.IsFalse(owned.Completion.Task.IsCompleted);
		Assert.AreEqual(1, first.DiskOperationGate.CurrentCount);
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task DeleteAsync_WhileTheFileSystemDeletes_KeepsEveryDescendantReserved()
	{
		var fixture = new TestHost();

		LogicalDocument first = fixture.Add("src", "a.txt");
		LogicalDocument second = fixture.Add("src", "b.txt");

		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		fixture.FileSystem.OnDeleteDirectory = (_, _) =>
		{
			started.SetResult();
			return release.Task;
		};

		Task<WorkspaceDocumentDirectoryDeleteResult> delete = DeleteAsync(fixture, "src");

		await started.Task.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsTrue(fixture.Locked(tracker => tracker.IsDeleteActive(first)));
		Assert.IsTrue(fixture.Locked(tracker => tracker.IsDeleteActive(second)));
		Assert.AreEqual(0, first.DiskOperationGate.CurrentCount);

		release.SetResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted));

		WorkspaceDocumentDirectoryDeleteResult result = await delete.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Deleted, result.Outcome);
		fixture.AssertReleased(PathOf("src"), first, second);
	}

	[TestMethod]
	public async Task DeleteAsync_WhenADescendantChangedOnDisk_ReportsExternalFileConflictAndDeletesNothing()
	{
		var fixture = new TestHost();
		LogicalDocument document = fixture.AddStamped(StampOf("tracked"), "src", "a.txt");

		fixture.FileSystem.OnCaptureStamp = (_, _) => Task.FromResult(StampOf("changed"));

		WorkspaceDocumentDirectoryDeleteResult result = await DeleteAsync(fixture, "src").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.ExternalFileConflict, result.Outcome);
		Assert.IsNotNull(result.Failure);
		Assert.Contains(document.DocumentId, result.Failure.Message);
		Assert.DoesNotContain("DeleteDirectory", string.Join(' ', fixture.FileSystem.Calls));

		fixture.AssertReleased(PathOf("src"), document);
	}

	[TestMethod]
	[DataRow(WorkspaceFileDeleteOutcome.ExternalFileConflict, WorkspaceDocumentDirectoryDeleteOutcome.ExternalFileConflict)]
	[DataRow(WorkspaceFileDeleteOutcome.Canceled, WorkspaceDocumentDirectoryDeleteOutcome.Canceled)]
	[DataRow(WorkspaceFileDeleteOutcome.DeleteFailed, WorkspaceDocumentDirectoryDeleteOutcome.DeleteFailed)]
	public async Task DeleteAsync_WhenTheFileSystemDoesNotDelete_MapsTheOutcomeKeepsTheDocumentsAndReleasesEverything(
		WorkspaceFileDeleteOutcome deleteOutcome,
		WorkspaceDocumentDirectoryDeleteOutcome expected
	)
	{
		var fixture = new TestHost();
		LogicalDocument document = fixture.Add("src", "a.txt");
		var failure = new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.DeleteFailed, "The delete failed.");

		fixture.FileSystem.OnDeleteDirectory = (_, _) => Task.FromResult(new WorkspaceFileDeleteResult(deleteOutcome, Failure: failure));

		WorkspaceDocumentDirectoryDeleteResult result = await DeleteAsync(fixture, "src").ConfigureAwait(false);

		Assert.AreEqual(expected, result.Outcome);
		Assert.AreSame(failure, result.Failure);
		Assert.IsTrue(fixture.Locked(tracker => tracker.TryGetDocument(document.DocumentId, out _)));

		fixture.AssertReleased(PathOf("src"), document);
	}

	[TestMethod]
	public async Task DeleteAsync_WhenTheFileSystemThrows_ReportsDeleteFailedAndAccessDeniedForAPermissionFailure()
	{
		var fixture = new TestHost();
		LogicalDocument document = fixture.Add("src", "a.txt");

		fixture.FileSystem.OnDeleteDirectory = (_, _) => throw new IOException("The disk failed.");

		WorkspaceDocumentDirectoryDeleteResult failed = await DeleteAsync(fixture, "src").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.DeleteFailed, failed.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.DeleteFailed, failed.Failure?.Code);

		fixture.FileSystem.OnDeleteDirectory = (_, _) => throw new UnauthorizedAccessException("Denied.");

		WorkspaceDocumentDirectoryDeleteResult denied = await DeleteAsync(fixture, "src").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceOperationFailureCodes.AccessDenied, denied.Failure?.Code);
		fixture.AssertReleased(PathOf("src"), document);
	}

	[TestMethod]
	public async Task DeleteAsync_PublishesItsDirectoryReservationBeforeAnythingElseRuns()
	{
		var fixture = new TestHost();
		DirectoryOperationReservation? observed = null;

		fixture.Add("src", "a.txt");

		fixture.Hooks.DirectoryOperationBeforeLifetimeLink = () =>
			observed = fixture.Locked(tracker => tracker.FindDirectoryOperationUnder(PathOf("src", "a.txt")));

		await DeleteAsync(fixture, "src").ConfigureAwait(false);

		Assert.IsNotNull(observed);
		Assert.AreEqual(PathOf("src"), observed.DirectoryId);
	}

	[TestMethod]
	public async Task DeleteAsync_WhenTheCallerCancelsBeforeTheOperationLinksToIt_ReportsCanceledWithoutDeleting()
	{
		var fixture = new TestHost();
		using var cancellation = new CancellationTokenSource();

		LogicalDocument document = fixture.Add("src", "a.txt");

		fixture.Hooks.DirectoryOperationBeforeLifetimeLink = cancellation.Cancel;

		WorkspaceDocumentDirectoryDeleteResult result = await fixture
			.Runner.DeleteAsync(new WorkspaceDocumentDirectoryDeleteRequest(PathOf("src")), cancellation.Token)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Canceled, result.Outcome);
		Assert.AreEqual(document.DocumentId, result.Snapshots.Single().DocumentId);
		Assert.IsEmpty(fixture.FileSystem.Calls);

		fixture.AssertReleased(PathOf("src"), document);
	}

	[TestMethod]
	public async Task DeleteAsync_AfterTheStoreBeganStopping_Throws()
	{
		var fixture = new TestHost();

		fixture.Add("src", "a.txt");
		fixture.Stop();

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => DeleteAsync(fixture, "src")).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task DeleteAsync_WhenTheCallerCancelsWhileTheFileSystemDeletes_ReportsCanceledAndKeepsTheDocuments()
	{
		var fixture = new TestHost();
		using var cancellation = new CancellationTokenSource();

		LogicalDocument document = fixture.Add("src", "a.txt");

		fixture.FileSystem.OnDeleteDirectory = (_, token) =>
		{
			cancellation.Cancel();
			token.ThrowIfCancellationRequested();

			return Task.FromResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted));
		};

		WorkspaceDocumentDirectoryDeleteResult result = await fixture
			.Runner.DeleteAsync(new WorkspaceDocumentDirectoryDeleteRequest(PathOf("src")), cancellation.Token)
			.ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Canceled, result.Outcome);
		Assert.AreEqual(document.DocumentId, result.Snapshots.Single().DocumentId);
		Assert.IsTrue(fixture.Locked(tracker => tracker.TryGetDocument(document.DocumentId, out _)));

		fixture.AssertReleased(PathOf("src"), document);
	}

	[TestMethod]
	public async Task DeleteAsync_WhenTheCallerCancelsAfterTheFileSystemDeleted_StillRemovesTheDocuments()
	{
		var fixture = new TestHost();
		using var cancellation = new CancellationTokenSource();

		LogicalDocument document = fixture.Add("src", "a.txt");

		fixture.FileSystem.OnDeleteDirectory = (_, _) =>
		{
			cancellation.Cancel();
			return Task.FromResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted));
		};

		WorkspaceDocumentDirectoryDeleteResult result = await fixture
			.Runner.DeleteAsync(new WorkspaceDocumentDirectoryDeleteRequest(PathOf("src")), cancellation.Token)
			.ConfigureAwait(false);

		// The tree is already gone, so Canceled would leave the documents tracked at paths that are gone.
		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Deleted, result.Outcome);
		Assert.IsFalse(fixture.Locked(tracker => tracker.TryGetDocument(document.DocumentId, out _)));

		fixture.AssertReleased(PathOf("src"), document);
	}

	[TestMethod]
	public async Task DeleteAsync_PublishesNoDeleteReservationUntilEveryGateIsHeld()
	{
		var fixture = new TestHost();

		bool gatesHeld = false;
		bool anyReserved = true;

		LogicalDocument first = fixture.Add("src", "a.txt");
		LogicalDocument second = fixture.Add("src", "b.txt");

		fixture.Hooks.DirectoryOperationPostGateSetup = () =>
		{
			gatesHeld = first.DiskOperationGate.CurrentCount == 0 && second.DiskOperationGate.CurrentCount == 0;
			anyReserved = fixture.Locked(tracker => tracker.IsDeleteActive(first) || tracker.IsDeleteActive(second));
		};

		await DeleteAsync(fixture, "src").ConfigureAwait(false);

		Assert.IsTrue(gatesHeld);
		Assert.IsFalse(anyReserved);
	}

	[TestMethod]
	public async Task DeleteAsync_WhenALaterGateIsHeld_ReservesNothingEvenForADescendantThatAlreadyHasAReservation()
	{
		var fixture = new TestHost();

		LogicalDocument first = fixture.Add("src", "a.txt");
		LogicalDocument second = fixture.Add("src", "b.txt");

		// No gate protects this reservation, so a driver that reserved the first descendant before it held every gate would fail on it.
		DeleteReservation foreign = fixture.Locked(tracker => tracker.ReserveDelete(first));

		Assert.IsTrue(second.DiskOperationGate.Wait(0));

		WorkspaceDocumentDirectoryDeleteResult result = await DeleteAsync(fixture, "src").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.OperationInProgress, result.Outcome);
		Assert.IsTrue(fixture.Locked(tracker => tracker.IsDeleteActive(first)));
		Assert.IsFalse(foreign.Completion.Task.IsCompleted);
		Assert.AreEqual(1, first.DiskOperationGate.CurrentCount);
	}

	[TestMethod]
	public async Task DeleteAsync_WhenTheLifetimeEndsBeforeTheOperationLinksToIt_ReportsCanceledWithoutSnapshots()
	{
		var fixture = new TestHost();

		LogicalDocument document = fixture.Add("src", "a.txt");

		fixture.Hooks.DirectoryOperationBeforeLifetimeLink = fixture.Lifetime.Dispose;

		WorkspaceDocumentDirectoryDeleteResult result = await DeleteAsync(fixture, "src").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Canceled, result.Outcome);
		Assert.IsEmpty(result.Snapshots);
		Assert.IsEmpty(fixture.FileSystem.Calls);

		fixture.AssertReleased(PathOf("src"), document);
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task DeleteAsync_WhenTheStopBeginsWhileItWaitsForAnOpen_ReportsCanceledWithoutDeleting()
	{
		var fixture = new TestHost();

		LogicalDocument document = fixture.Add("src", "a.txt");
		OpenReservation open = fixture.Locked(tracker => tracker.ReserveOpen(PathOf("src", "new.txt")));

		Task<WorkspaceDocumentDirectoryDeleteResult> delete = DeleteAsync(fixture, "src");

		fixture.Stop();

		fixture.Locked(tracker =>
			tracker.CompleteOpenReservation(
				PathOf("src", "new.txt"),
				open,
				new WorkspaceDocumentOpenResult(WorkspaceDocumentOpenOutcome.Canceled, null)
			)
		);

		WorkspaceDocumentDirectoryDeleteResult result = await delete.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Canceled, result.Outcome);
		Assert.IsEmpty(result.Snapshots);
		Assert.IsEmpty(fixture.FileSystem.Calls);
		Assert.IsTrue(fixture.Locked(tracker => tracker.TryGetDocument(document.DocumentId, out _)));
		Assert.IsNull(fixture.Locked(tracker => tracker.FindDirectoryOperationUnder(PathOf("src"))));
	}

	[TestMethod]
	public async Task DeleteAsync_OfADirectoryWithoutTrackedDescendants_StillDeletesTheDirectory()
	{
		var fixture = new TestHost();

		WorkspaceDocumentDirectoryDeleteResult result = await DeleteAsync(fixture, "empty").ConfigureAwait(false);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Deleted, result.Outcome);
		Assert.IsEmpty(result.Snapshots);
		Assert.AreSequenceEqual([$"DeleteDirectory:{PathOf("empty")}"], fixture.FileSystem.Calls);
	}
}
