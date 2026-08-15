using Nickelony.IDEKit.Core.Pathing;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task Rename_MoveFailures_MapToRenameOutcomesAndKeepTracking()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(
			TestPath("folder", "file.lua"),
			s_openOptions)).Snapshot!;
		string destinationPath = TestPath("folder", "renamed.lua");

		fileSystem.MoveResult = new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.DestinationExists);
		WorkspaceDocumentRenameResult exists = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.DestinationExists, exists.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		fileSystem.MoveResult = new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.MoveFailed);
		WorkspaceDocumentRenameResult failed = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.MoveFailed, failed.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		fileSystem.MoveResult = new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.MoveStateUnknown);
		WorkspaceDocumentRenameResult unknown = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.MoveStateUnknown, unknown.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		fileSystem.MoveResult = new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Canceled);
		WorkspaceDocumentRenameResult canceled = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Canceled, canceled.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task RenameDirectory_MoveFailures_MapToOutcomesAndKeepTracking()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;

		foreach ((WorkspaceFileMoveOutcome moveOutcome, WorkspaceDocumentDirectoryRenameOutcome expectedOutcome) in new[]
		{
			(WorkspaceFileMoveOutcome.DestinationExists, WorkspaceDocumentDirectoryRenameOutcome.DestinationExists),
			(WorkspaceFileMoveOutcome.MoveFailed, WorkspaceDocumentDirectoryRenameOutcome.MoveFailed),
			(WorkspaceFileMoveOutcome.MoveStateUnknown, WorkspaceDocumentDirectoryRenameOutcome.MoveStateUnknown),
			(WorkspaceFileMoveOutcome.Canceled, WorkspaceDocumentDirectoryRenameOutcome.Canceled)
		})
		{
			fileSystem.MoveDirectoryResult = new WorkspaceFileMoveResult(moveOutcome);

			WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
				new WorkspaceDocumentDirectoryRenameRequest(
					TestPath("folder"),
					TestPath("moved")));

			Assert.AreEqual(expectedOutcome, result.Outcome);
			Assert.IsTrue(store.TryGetSnapshot(first.DocumentId, out _));
			Assert.AreEqual(0, store.GetSnapshotsUnderDirectory(TestPath("moved")).Count);
		}
	}

	[TestMethod]
	public async Task RenameDirectory_TrackedDestinationCollision_ReportsDestinationInUse()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot source = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot collision = (await store.OpenAsync(
			TestPath("moved", "one.lua"),
			s_openOptions)).Snapshot!;

		// The rebased destination "moved/one.lua" is already tracked by an unrelated document, so the
		// directory rename must fail without touching that document or the source descendants.
		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				TestPath("folder"),
				TestPath("moved")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.DestinationInUse, result.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(source.DocumentId, out _));
		Assert.IsTrue(store.TryGetSnapshot(collision.DocumentId, out _));
		Assert.AreEqual(1, store.GetSnapshotsUnderDirectory(TestPath("moved")).Count);
	}

	[TestMethod]
	public async Task RenameDirectory_TrackedDocumentAtTheDestinationDirectory_ReportsDestinationInUse()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot source = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot occupant = (await store.OpenAsync(
			TestPath("moved"),
			s_openOptions)).Snapshot!;

		// The destination directory path itself is a tracked document, so the move would turn that path
		// into a directory while the tracked file identity still points at it. No rebased descendant
		// targets that exact path, so the collision is only visible when the destination is scoped as a
		// subtree; the rename is rejected without touching the file system.
		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				TestPath("folder"),
				TestPath("moved")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.DestinationInUse, result.Outcome);
		Assert.AreEqual(0, fileSystem.MoveDirectoryCount);
		Assert.IsTrue(store.TryGetSnapshot(source.DocumentId, out _));
		Assert.IsTrue(store.TryGetSnapshot(occupant.DocumentId, out _));
	}

	[TestMethod]
	public async Task RenameDirectory_TrackedDocumentBelowTheDestinationSubtree_ReportsDestinationInUse()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot source = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot deeper = (await store.OpenAsync(
			TestPath("moved", "nested", "other.lua"),
			s_openOptions)).Snapshot!;

		// A tracked document anywhere inside the destination subtree collides with the incoming subtree
		// even though the rebase never targets its exact path: the store already tracks a location inside
		// the destination, so the move cannot prove the destination empty and is rejected before any gate.
		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				TestPath("folder"),
				TestPath("moved")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.DestinationInUse, result.Outcome);
		Assert.AreEqual(0, fileSystem.MoveDirectoryCount);
		Assert.IsTrue(store.TryGetSnapshot(source.DocumentId, out _));
		Assert.IsTrue(store.TryGetSnapshot(deeper.DocumentId, out _));
	}

	[TestMethod]
	public async Task RenameDirectory_ReservedDestination_ReportsDestinationBusy()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot source = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot mover = (await store.OpenAsync(TestPath("other.lua"), s_openOptions)).Snapshot!;

		// Hold an in-flight Save As that has reserved "moved/one.lua"; the directory rename must see
		// the reservation as DestinationBusy because its rebased descendant targets the same destination.
		fileSystem.CapturedStamps.Enqueue(mover.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		Task<WorkspaceDocumentSaveAsResult> saveAs = store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(mover.DocumentKey, mover.DocumentId, mover.Version),
			mover.OnDiskStamp,
			TestPath("moved", "one.lua")));
		await fileSystem.Replacements.WhenReachedAsync(1);

		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				TestPath("folder"),
				TestPath("moved")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.DestinationBusy, result.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(source.DocumentId, out _));

		pendingReplacement.SetResult(new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementOutcome.Replaced,
			new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(1), "saved")));
		WorkspaceDocumentSaveAsResult saved = await saveAs;

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, saved.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(TestPath("moved", "one.lua"), out _));
	}

	[TestMethod]
	public async Task Rename_DestinationWithInFlightOpen_ReportsDestinationBusy()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("source.lua"), s_openOptions)).Snapshot!;
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;

		// The open holds a reservation for the destination path while it loads.
		Task<WorkspaceDocumentOpenResult> open = store.OpenAsync(TestPath("destination.lua"), s_openOptions);
		await fileSystem.Reads.WhenReachedAsync(2);

		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			TestPath("destination.lua")));

		// The destination checks include the in-flight open reservation, so the file is never moved and
		// the tracked instance is never dropped by a colliding identity update.
		Assert.AreEqual(WorkspaceDocumentRenameOutcome.DestinationBusy, result.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out WorkspaceDocumentSnapshot? retained));
		Assert.AreEqual(initial.DocumentKey, retained!.DocumentKey);

		pendingRead.SetResult(fileSystem.CreateReadResult());
		WorkspaceDocumentOpenResult opened = await open;
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, opened.Outcome);
	}

	[TestMethod]
	public async Task RenameDirectory_RebasesAllTrackedDescendantsAnd_PreservesKeys()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot second = (await store.OpenAsync(
			TestPath("folder", "two.lua"),
			s_openOptions)).Snapshot!;

		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				TestPath("folder"),
				TestPath("moved")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(2, result.Snapshots.Count);
		Assert.AreEqual(first.DocumentKey, result.Snapshots[0].DocumentKey);
		Assert.AreEqual(second.DocumentKey, result.Snapshots[1].DocumentKey);
		Assert.AreEqual(Path.GetFullPath(TestPath("moved", "one.lua")), result.Snapshots[0].DocumentId);
		Assert.AreEqual(Path.GetFullPath(TestPath("moved", "two.lua")), result.Snapshots[1].DocumentId);
		Assert.IsFalse(store.TryGetSnapshot(first.DocumentId, out _));
		Assert.IsFalse(store.TryGetSnapshot(second.DocumentId, out _));
		Assert.AreEqual(2, store.GetSnapshotsUnderDirectory(TestPath("moved")).Count);
	}

	[TestMethod]
	public async Task RenameDirectory_ExactSameSpelling_ReportsNoChange()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;

		// A destination that normalizes to the source directory is a no-op: the directory already is
		// where the caller wants it, so the result mirrors the file rename NoChange outcome instead of
		// reporting InvalidPath.
		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				TestPath("folder"),
				TestPath("folder")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.NoChange, result.Outcome);
		Assert.AreEqual(1, result.Snapshots.Count);
		Assert.AreEqual(first.DocumentKey, result.Snapshots[0].DocumentKey);
		Assert.IsTrue(store.TryGetSnapshot(first.DocumentId, out _));
		Assert.AreEqual(0, fileSystem.MoveDirectoryCount);
	}

	[TestMethod]
	public async Task RenameDirectory_EquivalentSpelling_ReportsNoChangeWithoutReachingTheFileSystem()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;

		// A trailing separator or a "." segment normalizes to the same directory identity, so the
		// request is the same no-op as an exactly repeated spelling.
		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				TestPath("folder"),
				TestPath("folder", ".")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.NoChange, result.Outcome);
		Assert.AreEqual(1, result.Snapshots.Count);
		Assert.IsTrue(store.TryGetSnapshot(first.DocumentId, out _));
		Assert.AreEqual(0, fileSystem.MoveDirectoryCount);

		WorkspaceDocumentDirectoryRenameResult trailingSeparator = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				TestPath("folder"),
				TestPath("folder") + Path.DirectorySeparatorChar));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.NoChange, trailingSeparator.Outcome);
		Assert.AreEqual(1, trailingSeparator.Snapshots.Count);
		Assert.AreEqual(0, fileSystem.MoveDirectoryCount);
	}

	[TestMethod]
	public async Task RenameDirectory_PassesNormalizedDirectoryIdsToTheFileSystem()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;

		// The caller brings a relative spelling with a redundant segment; the file system must receive
		// the normalized ids so a case-sensitive file system is not at the mercy of that spelling.
		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				TestPath("folder", "."),
				TestPath("moved")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(TestPath("folder"), fileSystem.LastMoveDirectorySource);
		Assert.AreEqual(TestPath("moved"), fileSystem.LastMoveDirectoryDestination);
	}

	[TestMethod]
	public async Task Rename_EquivalentSpelling_ReturnsNoChangeWithoutMoving()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(
			TestPath("folder", "file.lua"),
			s_openOptions)).Snapshot!;
		string destinationPath = TestPath("folder", ".", "file.lua");

		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.NoChange, result.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
		Assert.AreEqual(0, fileSystem.Moves.Count);
	}

	[TestMethod]
	public async Task Rename_ForwardsTheExpectedSourceStampToTheFileSystem()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			TestPath("renamed.lua")));

		// The caller's stamp is the move precondition; the file system must receive exactly that value
		// so a source file that changed since the snapshot is never moved silently.
		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(initial.OnDiskStamp, fileSystem.LastMoveExpectedStamp);
	}

	[TestMethod]
	public async Task Rename_ConcurrentReplacement_StaysAppliedAfterTheMove()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		var pendingMove = new TaskCompletionSource<WorkspaceFileMoveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingMove = pendingMove.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		Task<WorkspaceDocumentRenameResult> rename = store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			TestPath("renamed.lua")));
		await fileSystem.Moves.WhenReachedAsync(1);

		// A logical replacement does not take the disk gate, so it interleaves with the in-flight move
		// by design; the renamed document must keep the edit.
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"edited",
			initial.FileFormat));

		pendingMove.SetResult(new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved));
		WorkspaceDocumentRenameResult result = await rename;

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, edited.Outcome);
		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual("edited", result.Snapshot!.Content);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task RenameDirectory_WithoutTrackedDescendants_StillMovesTheDirectory()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(TestPath("empty"), TestPath("moved")));

		// A folder without tracked documents is still a file-system rename; only the identity rebase has
		// nothing to do.
		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(0, result.Snapshots.Count);
		Assert.AreEqual(1, fileSystem.MoveDirectoryCount);
	}

	[TestMethod]
	public async Task RenameDirectory_StampMismatch_ReportsExternalFileConflict()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;
		fileSystem.CapturedStamps.Enqueue(new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(1), "changed"));

		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				TestPath("folder"),
				TestPath("moved")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.ExternalFileConflict, result.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(first.DocumentId, out _));
		Assert.AreEqual(0, fileSystem.MoveDirectoryCount);
	}

	[TestMethod]
	public async Task Rename_BlankDestination_ReportsInvalidPath()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			"   "));

		// The destination is validated before the operation gate, so the document keeps its identity.
		Assert.AreEqual(WorkspaceDocumentRenameOutcome.InvalidPath, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task RenameDirectory_BlankDestination_ReportsInvalidPath()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;

		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				TestPath("folder"),
				"   "));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.InvalidPath, result.Outcome);
		Assert.AreEqual(0, fileSystem.MoveDirectoryCount);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task RenameAndSaveAs_PreserveDocumentKeyAndRetargetPath()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		string sourcePath = Path.Combine(directory, "source.lua");
		string renamedPath = Path.Combine(directory, "renamed.lua");
		string savedAsPath = Path.Combine(directory, "saved-as.lua");
		File.WriteAllText(sourcePath, "content");
		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(sourcePath, s_openOptions)).Snapshot!;

		WorkspaceDocumentRenameResult renamed = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			renamedPath));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, renamed.Outcome);
		Assert.IsFalse(store.TryGetSnapshot(sourcePath, out _));
		Assert.IsTrue(store.TryGetSnapshot(renamedPath, out WorkspaceDocumentSnapshot? renamedSnapshot));
		Assert.AreEqual(initial.DocumentKey, renamedSnapshot!.DocumentKey);

		WorkspaceDocumentSaveAsResult savedAs = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(renamedSnapshot.DocumentKey, renamedSnapshot.DocumentId, renamedSnapshot.Version),
			renamedSnapshot.OnDiskStamp,
			savedAsPath));

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, savedAs.Outcome);
		Assert.IsFalse(store.TryGetSnapshot(renamedPath, out _));
		Assert.IsTrue(store.TryGetSnapshot(savedAsPath, out WorkspaceDocumentSnapshot? savedAsSnapshot));
		Assert.AreEqual(initial.DocumentKey, savedAsSnapshot!.DocumentKey);

		// Save As follows the commit-path convention: the persisted version identifies the
		// captured content that was written while the retargeted snapshot version is incremented.
		Assert.AreEqual(renamedSnapshot.Version + 1, savedAsSnapshot.Version);
		Assert.AreEqual(renamedSnapshot.Version, savedAsSnapshot.PersistedVersion);
		Assert.IsFalse(savedAsSnapshot.IsDirty);

		WorkspaceDocumentDeleteResult deleted = await store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(savedAsSnapshot.DocumentKey, savedAsSnapshot.DocumentId, savedAsSnapshot.Version),
			savedAsSnapshot.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, deleted.Outcome);
		Assert.IsFalse(store.TryGetSnapshot(savedAsPath, out _));
		Assert.IsFalse(File.Exists(savedAsPath));
	}

	[TestMethod]
	public async Task Rename_CaseOnly_PreservesDocumentKeyAndUpdatesDisplayPath()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		string sourcePath = Path.Combine(directory, "Script.lua");
		string destinationPath = Path.Combine(directory, "script.lua");
		File.WriteAllText(sourcePath, "content");
		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(sourcePath, s_openOptions)).Snapshot!;

		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(initial.DocumentKey, result.Snapshot!.DocumentKey);
		Assert.AreEqual(destinationPath, result.Snapshot.DisplayPath);
		string[] files = Directory.GetFiles(directory);
		Assert.AreEqual(1, files.Length);
		Assert.AreEqual("script.lua", Path.GetFileName(files[0]));
	}

	[TestMethod]
	public async Task RenameDirectory_EquivalentSpellingWithFullPaths_ReportsNoChange()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		string sourcePath = Path.Combine(directory, "folder");
		Directory.CreateDirectory(sourcePath);
		string filePath = Path.Combine(sourcePath, "one.lua");
		File.WriteAllText(filePath, "content");
		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(filePath, s_openOptions)).Snapshot!;

		// A "." segment normalizes to the same directory identity, so the request is a no-op that
		// never reaches the file system.
		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				sourcePath,
				Path.Combine(sourcePath, ".")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.NoChange, result.Outcome);
		Assert.AreEqual(1, result.Snapshots.Count);
		Assert.IsTrue(Directory.Exists(sourcePath));
		Assert.IsTrue(File.Exists(filePath));
		Assert.IsTrue(store.TryGetSnapshot(filePath, out WorkspaceDocumentSnapshot? retained));
		Assert.AreEqual(initial.DocumentId, retained!.DocumentId);
	}

	[TestMethod]
	public async Task Rename_TrackedDestinationCollision_ReportsDestinationInUse()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot source = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot collision = (await store.OpenAsync(
			TestPath("folder", "two.lua"),
			s_openOptions)).Snapshot!;

		// The destination is tracked by an unrelated document instance, so the rename is rejected
		// before the file system is asked to move anything.
		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(source.DocumentKey, source.DocumentId, source.Version),
			source.OnDiskStamp,
			TestPath("folder", "two.lua")));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.DestinationInUse, result.Outcome);
		Assert.AreEqual(0, fileSystem.Moves.Count);
		Assert.IsTrue(store.TryGetSnapshot(source.DocumentId, out _));
		Assert.IsTrue(store.TryGetSnapshot(collision.DocumentId, out _));
	}

	[TestMethod]
	public async Task Rename_PreCanceledMove_ReportsCanceledAndKeepsTracking()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentRenameResult result = await store.RenameAsync(
			new WorkspaceDocumentRenameRequest(
				new(initial.DocumentKey, initial.DocumentId, initial.Version),
				initial.OnDiskStamp,
				TestPath("folder", "renamed.lua")),
			cancellation.Token);

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Canceled, result.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task Rename_ThrowingFileSystem_ReportsMoveFailedAndKeepsTracking()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat) { ThrowOnMove = true };
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			TestPath("folder", "renamed.lua")));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.MoveFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.MoveFailed, result.Failure?.Code);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task RenameDirectory_PreCanceledMove_ReportsCanceledAndKeepsTracking()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(TestPath("folder"), TestPath("moved")),
			cancellation.Token);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Canceled, result.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(first.DocumentId, out _));
		Assert.AreEqual(0, store.GetSnapshotsUnderDirectory(TestPath("moved")).Count);
	}

	[TestMethod]
	public async Task RenameDirectory_ThrowingFileSystem_ReportsMoveFailedAndKeepsTracking()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat) { ThrowOnMoveDirectory = true };
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(TestPath("folder"), TestPath("moved")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.MoveFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.MoveFailed, result.Failure?.Code);
		Assert.IsTrue(store.TryGetSnapshot(first.DocumentId, out _));
	}

	[TestMethod]
	public async Task Rename_MissingSource_RetargetsTheIdentityWithoutMoving()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.Missing));
		WorkspaceDocumentSnapshot unsaved = (await store.OpenAsync(TestPath("unsaved.lua"), s_openOptions)).Snapshot!;

		// A rename whose expected source stamp is missing has no bytes to move: the document identity
		// is retargeted, the destination keeps the missing stamp, and no file-system move runs.
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(unsaved.DocumentKey, unsaved.DocumentId, unsaved.Version),
			unsaved.OnDiskStamp,
			TestPath("renamed.lua")));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(0, fileSystem.Moves.Count);
		Assert.IsFalse(store.TryGetSnapshot(unsaved.DocumentId, out _));
		Assert.IsTrue(store.TryGetSnapshot(TestPath("renamed.lua"), out WorkspaceDocumentSnapshot? retargeted));
		Assert.AreEqual(unsaved.DocumentKey, retargeted!.DocumentKey);
		Assert.AreEqual(FileStamp.Missing, retargeted.OnDiskStamp);
		Assert.AreEqual(unsaved.Version + 1, retargeted.Version);
	}

	[TestMethod]
	public async Task Rename_TrailingSeparatorDestination_MovesToTheNormalizedPath()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("source.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp,
			TestPath("renamed.lua") + Path.DirectorySeparatorChar));

		// The destination is normalized for identity and for the file-system move, so the caller's
		// trailing separator cannot reach the move as a directory-shaped path.
		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(TestPath("renamed.lua"), fileSystem.LastMoveDestination);
	}

	[TestMethod]
	public async Task Rename_MissingSourceThatAppeared_ReportsExternalFileConflict()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.Missing));
		WorkspaceDocumentSnapshot unsaved = (await store.OpenAsync(TestPath("unsaved.lua"), s_openOptions)).Snapshot!;

		// The source is re-checked before the retarget: a file that appeared since the caller's
		// snapshot means the rename has real bytes to move.
		var appearedStamp = new FileStamp(true, 5, DateTime.UnixEpoch.AddMinutes(1), "appeared");
		fileSystem.CapturedStamps.Enqueue(appearedStamp);
		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(unsaved.DocumentKey, unsaved.DocumentId, unsaved.Version),
			unsaved.OnDiskStamp,
			TestPath("renamed.lua")));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(appearedStamp, result.ObservedOnDiskStamp);
		Assert.AreEqual(0, fileSystem.Moves.Count);
		Assert.IsTrue(store.TryGetSnapshot(unsaved.DocumentId, out _));
	}

	[TestMethod]
	public async Task Rename_MissingSourceToOccupiedDestination_ReportsDestinationExists()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.Missing));
		WorkspaceDocumentSnapshot unsaved = (await store.OpenAsync(TestPath("unsaved.lua"), s_openOptions)).Snapshot!;

		// The destination must be as free as a file-system move would require it to be: an existing
		// file is a collision rather than a retarget onto an occupied path.
		var occupiedStamp = new FileStamp(true, 2, DateTime.UnixEpoch.AddMinutes(1), "occupied");
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		fileSystem.CapturedStamps.Enqueue(occupiedStamp);
		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(unsaved.DocumentKey, unsaved.DocumentId, unsaved.Version),
			unsaved.OnDiskStamp,
			TestPath("renamed.lua")));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.DestinationExists, result.Outcome);
		Assert.AreEqual(occupiedStamp, result.ObservedOnDiskStamp);
		Assert.AreEqual(0, fileSystem.Moves.Count);
		Assert.IsTrue(store.TryGetSnapshot(unsaved.DocumentId, out _));
	}

	[TestMethod]
	public async Task Rename_MissingSourceReplacedByDirectory_ReportsExternalFileConflict()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.Missing));
		WorkspaceDocumentSnapshot unsaved = (await store.OpenAsync(TestPath("unsaved.lua"), s_openOptions)).Snapshot!;

		// A directory now occupies the source path: it is not the missing file the retarget expects, so
		// the rename reports a conflict instead of retargeting the document onto the destination.
		fileSystem.CapturedStamps.Enqueue(FileStamp.Directory);
		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(unsaved.DocumentKey, unsaved.DocumentId, unsaved.Version),
			unsaved.OnDiskStamp,
			TestPath("renamed.lua")));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(FileStamp.Directory, result.ObservedOnDiskStamp);
		Assert.AreEqual(0, fileSystem.Moves.Count);
		Assert.IsTrue(store.TryGetSnapshot(unsaved.DocumentId, out _));
		Assert.IsFalse(store.TryGetSnapshot(TestPath("renamed.lua"), out _));
	}

	[TestMethod]
	public async Task Rename_MissingSourceToDirectoryDestination_ReportsDestinationExists()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.Missing));
		WorkspaceDocumentSnapshot unsaved = (await store.OpenAsync(TestPath("unsaved.lua"), s_openOptions)).Snapshot!;

		// A directory occupying the destination is not a free path: the retarget is rejected like any
		// other occupation rather than moving the document identity onto a directory.
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Directory);
		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(unsaved.DocumentKey, unsaved.DocumentId, unsaved.Version),
			unsaved.OnDiskStamp,
			TestPath("renamed.lua")));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.DestinationExists, result.Outcome);
		Assert.AreEqual(FileStamp.Directory, result.ObservedOnDiskStamp);
		Assert.AreEqual(0, fileSystem.Moves.Count);
		Assert.IsTrue(store.TryGetSnapshot(unsaved.DocumentId, out _));
	}

	[TestMethod]
	public async Task Rename_MissingSourceRetargetedDocument_SavesAtTheNewPath()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.Missing));
		WorkspaceDocumentSnapshot unsaved = (await store.OpenAsync(TestPath("unsaved.lua"), s_openOptions)).Snapshot!;

		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		WorkspaceDocumentSnapshot retargeted = (await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(unsaved.DocumentKey, unsaved.DocumentId, unsaved.Version),
			unsaved.OnDiskStamp,
			TestPath("renamed.lua")))).Snapshot!;

		// The retargeted document composes with the save flow: a commit against the missing stamp
		// creates the file at the new path like any other not-yet-written document.
		WorkspaceDocumentCommitResult committed = await store.CommitAsync(CreateCommitRequest(retargeted));

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, committed.Outcome);
		Assert.AreEqual(TestPath("renamed.lua"), committed.Snapshot!.DocumentId);
		Assert.IsFalse(committed.Snapshot.IsDirty);
		Assert.AreEqual(1, fileSystem.ReplaceCount);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Rename_DestinationReservedByAnInFlightSaveAs_ReportsDestinationBusy()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot saved = (await store.OpenAsync(TestPath("one.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot source = (await store.OpenAsync(TestPath("two.lua"), s_openOptions)).Snapshot!;
		string destinationPath = TestPath("shared.lua");

		// The save-as holds the destination reservation while its write is in flight.
		fileSystem.CapturedStamps.Enqueue(saved.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		Task<WorkspaceDocumentSaveAsResult> held = store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(saved.DocumentKey, saved.DocumentId, saved.Version),
			saved.OnDiskStamp,
			destinationPath));
		await fileSystem.Replacements.WhenReachedAsync(1);

		// A rename onto the same destination is rejected with the destination-reservation outcome rather
		// than colliding with the file the save-as is publishing.
		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(source.DocumentKey, source.DocumentId, source.Version),
			source.OnDiskStamp,
			destinationPath));

		pendingReplacement.SetResult(new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementOutcome.Replaced,
			new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(1), "written")));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.DestinationBusy, result.Outcome);
		Assert.AreEqual(0, fileSystem.Moves.Count);
		Assert.IsTrue(store.TryGetSnapshot(source.DocumentId, out _));
		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, (await held).Outcome);
	}

	[TestMethod]
	public async Task Rename_CaseOnlyRenameWithARollbackFailure_ReportsMoveStateUnknownAndKeepsTracking()
	{
		using var temp = new TemporaryDirectory();
		string sourcePath = Path.Combine(temp.Path, "Script.lua");
		File.WriteAllText(sourcePath, "content");

		// The real local file system runs the case-only rename through its intermediate path; the
		// substituted move fails the move to the destination and the rollback, so the file is left at
		// the intermediate path and the store must report the state it cannot establish. The comparison
		// policy is supplied for both the file system and the store so the rename is case-only on every
		// platform.
		var options = new WorkspaceDocumentOptions { PathComparison = LocalPathComparisonPolicy.CaseInsensitive };
		var moveFailure = new IOException("The move to the destination failed.");
		var rollbackFailure = new IOException("The rollback failed.");
		int moveCount = 0;
		var fileSystem = new LocalWorkspaceFileSystem(options, new WorkspaceFileOperations(
			Replace: static (_, _) => throw new InvalidOperationException("A rename never replaces a file."),
			MoveFile: (source, destination) =>
			{
				if (moveCount++ == 0)
				{
					File.Move(source, destination, overwrite: false);
					return;
				}

				throw moveCount == 2 ? moveFailure : rollbackFailure;
			},
			MoveDirectory: static (_, _) => throw new InvalidOperationException("A file rename never moves a directory.")));
		await using var store = new WorkspaceDocumentStore(fileSystem, options);
		WorkspaceDocumentSnapshot opened = (await store.OpenAsync(sourcePath, s_openOptions)).Snapshot!;

		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(opened.DocumentKey, opened.DocumentId, opened.Version),
			opened.OnDiskStamp,
			Path.Combine(temp.Path, "script.lua")));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.MoveStateUnknown, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.MoveStateUnknown, result.Failure?.Code);

		// The store keeps tracking the source identity with its unchanged stamp, because the file may
		// still be the document's file.
		Assert.IsTrue(store.TryGetSnapshot(sourcePath, out WorkspaceDocumentSnapshot? tracked));
		Assert.AreEqual(opened.DocumentId, tracked!.DocumentId);
		Assert.AreEqual(opened.OnDiskStamp, tracked.OnDiskStamp);
	}

	[TestMethod]
	public async Task Rename_FileSystemThatDoesNotReportAStamp_RefreshesTheMovedStamp()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(
			TestPath("folder", "file.lua"),
			s_openOptions)).Snapshot!;
		string destinationPath = TestPath("folder", "renamed.lua");

		// The file system moves the file but reports no resulting stamp, which a cross-volume move leaves
		// with a new last-write time; the store must re-capture the destination stamp instead of installing
		// the pre-move stamp, which the next write would report as a spurious conflict.
		fileSystem.MoveResult = new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved);
		FileStamp movedStamp = initial.OnDiskStamp with
		{
			LastWriteTimeUtc = initial.OnDiskStamp.LastWriteTimeUtc!.Value.AddMinutes(5),
		};
		fileSystem.CapturedStamps.Enqueue(movedStamp);

		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(movedStamp, result.Snapshot!.OnDiskStamp, "The resulting destination stamp is installed, not the pre-move stamp.");
	}

	[TestMethod]
	public async Task RenameDirectory_FileSystemThatDoesNotReportAStamp_RefreshesDescendantStamps()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;

		// The directory move reports no resulting stamp per file, so the store re-captures each
		// descendant's destination stamp for the rebase. The pre-gate re-captures the source stamp first
		// (it must still match the tracked stamp), then the rebase captures the destination stamp.
		fileSystem.MoveDirectoryResult = new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved);
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		FileStamp movedStamp = initial.OnDiskStamp with
		{
			LastWriteTimeUtc = initial.OnDiskStamp.LastWriteTimeUtc!.Value.AddMinutes(5),
		};
		fileSystem.CapturedStamps.Enqueue(movedStamp);

		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(
				TestPath("folder"),
				TestPath("moved")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.Outcome);
		WorkspaceDocumentSnapshot rebased = result.Snapshots.Single();
		Assert.AreEqual(movedStamp, rebased.OnDiskStamp, "The descendant's resulting stamp is installed, not the pre-move stamp.");
	}
}
