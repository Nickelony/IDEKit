using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	[Timeout(15000)]
	public async Task RenameDirectory_OpenDuringMove_WaitsForCompletionAndReadsTheVacatedSourcePath()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingMove = new TaskCompletionSource<WorkspaceFileMoveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingMoveDirectory = pendingMove.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot tracked = (await store.OpenAsync(TestPath("folder", "tracked.lua"), s_openOptions)).Snapshot!;

		Task<WorkspaceDocumentDirectoryRenameResult> rename = store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(TestPath("folder"), TestPath("moved")));
		await fileSystem.MoveDirectories.WhenReachedAsync(1);

		// An open for a path inside the moving directory publishes no load while the rename holds its
		// reservation: the open runs synchronously up to the wait, so the read count is unchanged.
		Task<WorkspaceDocumentOpenResult> lateOpen = store.OpenAsync(TestPath("folder", "late.lua"), s_openOptions);
		Assert.AreEqual(1, fileSystem.ReadCount);

		// The directory moved away with the file, so the resumed open reads the vacated source path as
		// missing and creates a new document there, while the tracked document is rebased.
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.Missing));
		pendingMove.SetResult(new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved));

		WorkspaceDocumentDirectoryRenameResult result = await rename;
		WorkspaceDocumentOpenResult newcomer = await lateOpen;

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(1, result.Snapshots.Count);
		Assert.IsTrue(store.TryGetSnapshot(TestPath("moved", "tracked.lua"), out WorkspaceDocumentSnapshot? rebased));
		Assert.AreEqual(tracked.DocumentKey, rebased!.DocumentKey);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, newcomer.Outcome);
		Assert.AreEqual(TestPath("folder", "late.lua"), newcomer.Snapshot!.DocumentId);
		Assert.AreEqual(FileStamp.Missing, newcomer.Snapshot.OnDiskStamp);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task RenameDirectory_CanceledWhileDraining_ReportsCanceledWithCurrentSnapshots()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot tracked = (await store.OpenAsync(TestPath("folder", "tracked.lua"), s_openOptions)).Snapshot!;

		// Hold a subtree load open so the rename parks in its drain phase.
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;
		Task<WorkspaceDocumentOpenResult> lateOpen = store.OpenAsync(TestPath("folder", "late.lua"), s_openOptions);
		await fileSystem.Reads.WhenReachedAsync(2);

		using var cancellation = new CancellationTokenSource();
		Task<WorkspaceDocumentDirectoryRenameResult> rename = store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(TestPath("folder"), TestPath("moved")),
			cancellation.Token);
		cancellation.Cancel();

		WorkspaceDocumentDirectoryRenameResult result = await rename;

		// Cancellation before the capture phase reports the currently tracked descendants, and the
		// directory reservation is released so the pending load can complete.
		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Canceled, result.Outcome);
		Assert.AreEqual(1, result.Snapshots.Count);
		Assert.AreEqual(tracked.DocumentKey, result.Snapshots[0].DocumentKey);
		Assert.AreEqual(0, fileSystem.MoveDirectoryCount);

		pendingRead.SetResult(fileSystem.CreateReadResult());
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, (await lateOpen).Outcome);
		Assert.IsTrue(store.TryGetSnapshot(tracked.DocumentId, out _));
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task RenameDirectory_InFlightSubtreeOpen_DrainsBeforeTheMove()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		await store.OpenAsync(TestPath("folder", "tracked.lua"), s_openOptions);

		// The second load is held open so the rename starts while the subtree open is in flight.
		var lateStamp = new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(2), "late");
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;
		Task<WorkspaceDocumentOpenResult> lateOpen = store.OpenAsync(TestPath("folder", "late.lua"), s_openOptions);
		await fileSystem.Reads.WhenReachedAsync(2);

		// The in-flight load is drained before the physical move, so the directory move has not started
		// while the read is pending.
		Task<WorkspaceDocumentDirectoryRenameResult> rename = store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(TestPath("folder"), TestPath("moved")));
		Assert.AreEqual(0, fileSystem.MoveDirectoryCount);

		// The drained document lands under the source path first and is then rebased with its siblings;
		// its stamp is queued so the pre-move validation observes the state the open captured.
		fileSystem.CapturedStamps.Enqueue(lateStamp);
		pendingRead.SetResult(WorkspaceFileReadResult.FromContent("late-content", s_defaultFormat, lateStamp));

		WorkspaceDocumentDirectoryRenameResult result = await rename;
		WorkspaceDocumentOpenResult newcomer = await lateOpen;

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(1, fileSystem.MoveDirectoryCount);
		Assert.AreEqual(2, result.Snapshots.Count);
		Assert.IsTrue(store.TryGetSnapshot(TestPath("moved", "tracked.lua"), out _));
		Assert.IsTrue(store.TryGetSnapshot(TestPath("moved", "late.lua"), out WorkspaceDocumentSnapshot? rebased));
		Assert.AreEqual(newcomer.Snapshot!.DocumentKey, rebased!.DocumentKey);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task RenameDirectory_FileRenameIntoTheMovingDirectory_ReportsDestinationBusy()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingMove = new TaskCompletionSource<WorkspaceFileMoveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingMoveDirectory = pendingMove.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot inside = (await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot outside = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		Task<WorkspaceDocumentDirectoryRenameResult> renameDirectory = store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(TestPath("folder"), TestPath("moved")));
		await fileSystem.MoveDirectories.WhenReachedAsync(1);

		// A destination inside the moving subtree is busy for the whole directory operation: the file
		// rename must not land a file in a directory that is about to be vacated.
		WorkspaceDocumentRenameResult rename = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(outside.DocumentKey, outside.DocumentId, outside.Version),
			outside.OnDiskStamp,
			TestPath("folder", "script.lua")));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.DestinationBusy, rename.Outcome);
		Assert.AreEqual(0, fileSystem.Moves.Count);
		Assert.IsTrue(store.TryGetSnapshot(outside.DocumentId, out _));
		Assert.IsTrue(store.TryGetSnapshot(inside.DocumentId, out _));

		pendingMove.SetResult(new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved));
		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, (await renameDirectory).Outcome);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task RenameDirectory_SaveAsIntoTheMovingDirectory_ReportsDestinationBusy()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingMove = new TaskCompletionSource<WorkspaceFileMoveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingMoveDirectory = pendingMove.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions);
		WorkspaceDocumentSnapshot outside = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		Task<WorkspaceDocumentDirectoryRenameResult> renameDirectory = store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(TestPath("folder"), TestPath("moved")));
		await fileSystem.MoveDirectories.WhenReachedAsync(1);

		WorkspaceDocumentSaveAsResult saveAs = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(outside.DocumentKey, outside.DocumentId, outside.Version),
			outside.OnDiskStamp,
			TestPath("folder", "copy.lua")));

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.DestinationBusy, saveAs.Outcome);
		Assert.AreEqual(0, fileSystem.ReplaceCount);
		Assert.IsTrue(store.TryGetSnapshot(outside.DocumentId, out _));

		pendingMove.SetResult(new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved));
		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, (await renameDirectory).Outcome);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task DeleteDirectory_OpenDuringDelete_WaitsForCompletionAndReadsTheDeletedPath()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingDirectoryDelete = pendingDelete.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions);

		Task<WorkspaceDocumentDirectoryDeleteResult> delete = store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")));
		await fileSystem.DirectoryDeletes.WhenReachedAsync(1);

		// An open for a path inside the directory publishes no load while the delete holds its
		// reservation: the open runs synchronously up to the wait, so the read count is unchanged.
		Task<WorkspaceDocumentOpenResult> lateOpen = store.OpenAsync(TestPath("folder", "two.lua"), s_openOptions);
		Assert.AreEqual(1, fileSystem.ReadCount);

		// The deleted path no longer contains a file, so the resumed open creates a new document for it.
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.Missing));
		pendingDelete.SetResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted));

		WorkspaceDocumentDirectoryDeleteResult result = await delete;
		WorkspaceDocumentOpenResult newcomer = await lateOpen;

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Deleted, result.Outcome);
		Assert.AreEqual(1, result.Snapshots.Count);
		Assert.IsFalse(store.TryGetSnapshot(TestPath("folder", "one.lua"), out _));
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, newcomer.Outcome);
		Assert.AreEqual(FileStamp.Missing, newcomer.Snapshot!.OnDiskStamp);
		Assert.IsTrue(store.TryGetSnapshot(TestPath("folder", "two.lua"), out WorkspaceDocumentSnapshot? created));
		Assert.AreEqual(newcomer.Snapshot.DocumentKey, created!.DocumentKey);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task DeleteDirectory_InFlightSubtreeOpen_DrainsBeforeTheDelete()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions);

		// The second load is held open so the delete starts while the subtree open is in flight.
		var lateStamp = new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(2), "late");
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;
		Task<WorkspaceDocumentOpenResult> lateOpen = store.OpenAsync(TestPath("folder", "two.lua"), s_openOptions);
		await fileSystem.Reads.WhenReachedAsync(2);

		// The in-flight load is drained before the physical delete, so the directory delete has not
		// started while the read is pending.
		Task<WorkspaceDocumentDirectoryDeleteResult> delete = store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")));
		Assert.AreEqual(0, fileSystem.DirectoryDeletes.Count);

		// The drained document lands under the directory first and is then removed from tracking with
		// its siblings; the stamps are queued in capture order (one.lua, then two.lua) so the
		// pre-delete validation observes the captured state.
		fileSystem.CapturedStamps.Enqueue(fileSystem.CreateReadResult().OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(lateStamp);
		pendingRead.SetResult(WorkspaceFileReadResult.FromContent("late-content", s_defaultFormat, lateStamp));

		WorkspaceDocumentDirectoryDeleteResult result = await delete;
		WorkspaceDocumentOpenResult newcomer = await lateOpen;

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Deleted, result.Outcome);
		Assert.AreEqual(2, result.Snapshots.Count);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, newcomer.Outcome);
		Assert.AreEqual(0, store.GetSnapshotsUnderDirectory(TestPath("folder")).Count);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task DeleteDirectory_ThrowingPostGateSetup_ReportsFailureWithoutLeakingGatesOrFlags()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot tracked = (await store.OpenAsync(TestPath("folder", "tracked.lua"), s_openOptions)).Snapshot!;

		// The post-gate setup publishes the descendant delete flags after every gate is acquired; make it
		// throw so the driver's gate-release path runs with ownership still held by the operation.
		store.TestHooks.DirectoryOperationPostGateSetup = () => throw new InvalidOperationException("setup failed");

		WorkspaceDocumentDirectoryDeleteResult result = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")));

		// The failure maps to the operation's fault outcome, and neither the acquired gates nor the
		// published delete flags may stay behind: a gated delete on the tracked descendant must complete
		// instead of reporting OperationInProgress for the life of the store.
		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.DeleteFailed, result.Outcome);

		store.TestHooks.DirectoryOperationPostGateSetup = null;
		WorkspaceDocumentDeleteResult delete = await store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(tracked.DocumentKey, tracked.DocumentId, tracked.Version),
			tracked.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, delete.Outcome);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task DeleteDirectory_DisposedBeforeTheLifetimeLink_ReportsCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var store = new WorkspaceDocumentStore(fileSystem);
		try
		{
			await store.OpenAsync(TestPath("folder", "tracked.lua"), s_openOptions);

			// Disposal completes synchronously - nothing is in flight - in the window between the
			// directory operation publishing its reservation and linking to the lifetime token, so the
			// source the link would read is already released.
			store.TestHooks.DirectoryOperationBeforeLifetimeLink = () => store.DisposeAsync().AsTask().GetAwaiter().GetResult();

			WorkspaceDocumentDirectoryDeleteResult result = await store.DeleteDirectoryAsync(
				new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")));

			// The released lifetime source maps to the documented cancellation outcome instead of the
			// fault an ObjectDisposedException from the link would produce.
			Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Canceled, result.Outcome);
		}
		finally
		{
			await store.DisposeAsync();
		}
	}
}
