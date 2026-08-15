using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	// A document tracked at exactly the source directory id is part of the directory subtree, so a
	// rename rebases it onto the destination directory id rather than leaving a stale entry behind at
	// the vacated path.
	[TestMethod]
	[Timeout(15000)]
	public async Task RenameDirectory_DocumentAtTheExactDirectoryPath_IsRebasedWithTheSubtree()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot atDirectory = (await store.OpenAsync(TestPath("folder"), s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot descendant = (await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(TestPath("folder"), TestPath("moved")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(2, result.Snapshots.Count);
		Assert.IsFalse(store.TryGetSnapshot(TestPath("folder"), out _));
		Assert.IsTrue(store.TryGetSnapshot(TestPath("moved"), out WorkspaceDocumentSnapshot? rebasedAtDirectory));
		Assert.AreEqual(atDirectory.DocumentKey, rebasedAtDirectory!.DocumentKey);
		Assert.IsTrue(store.TryGetSnapshot(TestPath("moved", "one.lua"), out WorkspaceDocumentSnapshot? rebasedDescendant));
		Assert.AreEqual(descendant.DocumentKey, rebasedDescendant!.DocumentKey);
	}

	// A document tracked at exactly the source directory id is removed together with its descendants
	// when the directory is deleted, instead of being left tracked against a path that no longer exists.
	[TestMethod]
	[Timeout(15000)]
	public async Task DeleteDirectory_DocumentAtTheExactDirectoryPath_IsRemovedWithTheSubtree()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		await store.OpenAsync(TestPath("folder"), s_openOptions);
		await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions);

		WorkspaceDocumentDirectoryDeleteResult result = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")));

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Deleted, result.Outcome);
		Assert.AreEqual(2, result.Snapshots.Count);
		Assert.IsFalse(store.TryGetSnapshot(TestPath("folder"), out _));
		Assert.IsFalse(store.TryGetSnapshot(TestPath("folder", "one.lua"), out _));
	}

	// An open whose path is exactly the source directory id is parked behind the in-flight rename like
	// an open of a descendant: it publishes no load while the reservation is held and, once the
	// directory has moved away, creates a new document at the vacated source path instead of racing
	// against the move.
	[TestMethod]
	[Timeout(15000)]
	public async Task RenameDirectory_OpenOfTheExactDirectoryPathDuringMove_WaitsForCompletion()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingMove = new TaskCompletionSource<WorkspaceFileMoveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingMoveDirectory = pendingMove.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		await store.OpenAsync(TestPath("folder", "tracked.lua"), s_openOptions);

		Task<WorkspaceDocumentDirectoryRenameResult> rename = store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(TestPath("folder"), TestPath("moved")));
		await fileSystem.MoveDirectories.WhenReachedAsync(1);

		// The open for the exact directory path publishes no load while the rename holds its
		// reservation: the open runs synchronously up to the wait, so the read count is unchanged.
		Task<WorkspaceDocumentOpenResult> lateOpen = store.OpenAsync(TestPath("folder"), s_openOptions);
		Assert.AreEqual(1, fileSystem.ReadCount);

		// The directory moved away from the source path, so the resumed open reads that path as missing
		// and creates a new document there.
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.Missing));
		pendingMove.SetResult(new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.Moved));

		WorkspaceDocumentDirectoryRenameResult result = await rename;
		WorkspaceDocumentOpenResult newcomer = await lateOpen;

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.Outcome);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, newcomer.Outcome);
		Assert.AreEqual(TestPath("folder"), newcomer.Snapshot!.DocumentId);
		Assert.AreEqual(FileStamp.Missing, newcomer.Snapshot.OnDiskStamp);
	}
}
