using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	[Timeout(15000)]
	public async Task Dispose_WhileCommitWaitsForReplacement_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot dirty = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat)).Snapshot!;

		Task<WorkspaceDocumentCommitResult> commit = store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(dirty.DocumentKey, dirty.DocumentId, dirty.Version),
			dirty.OnDiskStamp));
		await fileSystem.Replacements.WhenReachedAsync(1);

		// Disposal cancels the in-flight commit through the lifetime token and waits for the
		// registered operation before it disposes the per-document disk gates.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentCommitResult result = await commit;

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Canceled, result.Outcome);
		Assert.IsTrue(result.Snapshot!.IsDirty);
		await disposal;
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Dispose_WhileCleanReloadReads_CompletesWithCanceledWhenTheReadIgnoresTheToken()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot clean = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		// The read ignores the cancellation token, so disposal cannot end it through the lifetime token; the
		// clean reload must still report Canceled instead of adopting the content after the store was disposed.
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReadIgnoringCancellation = pendingRead.Task;

		Task<WorkspaceDocumentReloadResult> reload = store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(clean.DocumentKey, clean.DocumentId, clean.Version)));
		await fileSystem.Reads.WhenReachedAsync(2);

		ValueTask disposal = store.DisposeAsync();
		pendingRead.SetResult(WorkspaceFileReadResult.FromContent(
			"external",
			s_defaultFormat,
			new FileStamp(true, 8, DateTime.UnixEpoch.AddMinutes(1), "external")));

		WorkspaceDocumentReloadResult result = await reload;

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Canceled, result.Outcome);
		Assert.AreEqual("content", result.Snapshot!.Content);
		await disposal;
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Dispose_WhileSaveAsWaitsForReplacement_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		string destinationPath = TestPath("folder", "destination.lua");
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);

		Task<WorkspaceDocumentSaveAsResult> saveAs = store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));
		await fileSystem.Replacements.WhenReachedAsync(1);

		// Disposal cancels the in-flight save-as through the lifetime token and waits for the
		// registered operation before it disposes the per-document disk gates.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentSaveAsResult result = await saveAs;

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.Canceled, result.Outcome);
		Assert.AreEqual(initial.DocumentId, result.Snapshot!.DocumentId);
		await disposal;
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Dispose_WhileOpenWaitsForPendingRead_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;
		var store = new WorkspaceDocumentStore(fileSystem);

		Task<WorkspaceDocumentOpenResult> open = store.OpenAsync(TestPath("script.lua"), s_openOptions);
		await fileSystem.Reads.WhenReachedAsync(1);

		// Disposal cancels the in-flight load through the lifetime token and waits for the open
		// reservation before it disposes the per-document gates.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentOpenResult result = await open;

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Canceled, result.Outcome);
		Assert.IsNull(result.Snapshot);
		await disposal;
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Dispose_WhileDeleteWaitsForPendingDelete_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingDelete = pendingDelete.Task;
		var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		Task<WorkspaceDocumentDeleteResult> delete = store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));
		await fileSystem.Deletes.WhenReachedAsync(1);

		// Disposal cancels the in-flight delete through the lifetime token and waits for the registered
		// operation before it disposes the per-document gates.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentDeleteResult result = await delete;

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Canceled, result.Outcome);
		Assert.AreEqual(snapshot.DocumentId, result.Snapshot!.DocumentId);
		await disposal;
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Dispose_CancelsTheLifetimeTokenOutsideTheStateLock()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingMove = new TaskCompletionSource<WorkspaceFileMoveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingMoveDirectory = pendingMove.Task;
		var store = new WorkspaceDocumentStore(fileSystem);
		await store.OpenAsync(TestPath("folder", "tracked.lua"), s_openOptions);

		bool lockHeldDuringCancellation = true;
		fileSystem.MoveDirectoryCancellationObserver = token => token.Register(
			() => lockHeldDuringCancellation = store.TestHooks.IsStateLockHeldByCurrentThread,
			useSynchronizationContext: false);

		Task<WorkspaceDocumentDirectoryRenameResult> rename = store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(TestPath("folder"), TestPath("moved")));
		await fileSystem.MoveDirectories.WhenReachedAsync(1);

		// Disposal cancels the lifetime token while the directory move is in flight. A host cancellation
		// callback runs inline on the canceling thread, so it must not find the state lock held: a
		// callback that needs a store operation would otherwise deadlock against the disposal thread.
		ValueTask disposal = store.DisposeAsync();

		Assert.IsFalse(lockHeldDuringCancellation);

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Canceled, (await rename).Outcome);
		await disposal;
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Dispose_WhileCleanReloadWaitsForPendingRead_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;

		// A clean reload holds the document's disk gate and is registered as an active operation.
		Task<WorkspaceDocumentReloadResult> reload = store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)));
		await fileSystem.Reads.WhenReachedAsync(2);

		// Disposal cancels the in-flight read through the lifetime token and waits for the registered
		// operation before it disposes the per-document disk gates.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentReloadResult result = await reload;

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Canceled, result.Outcome);
		Assert.AreEqual(snapshot.DocumentId, result.Snapshot!.DocumentId);
		await disposal;
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Dispose_WhileRenameWaitsForPendingMove_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingMove = new TaskCompletionSource<WorkspaceFileMoveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingMove = pendingMove.Task;
		var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		// A rename holds the document's disk gate as a registered operation and reserves its destination.
		Task<WorkspaceDocumentRenameResult> rename = store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp,
			TestPath("renamed.lua")));
		await fileSystem.Moves.WhenReachedAsync(1);

		// Disposal cancels the in-flight move through the lifetime token and waits for the registered
		// operation and its destination reservation.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentRenameResult result = await rename;

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Canceled, result.Outcome);
		Assert.AreEqual(snapshot.DocumentId, result.Snapshot!.DocumentId);
		await disposal;
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Dispose_WhileDirectoryDeleteWaitsForPendingDelete_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingDirectoryDelete = pendingDelete.Task;
		var store = new WorkspaceDocumentStore(fileSystem);
		await store.OpenAsync(TestPath("folder", "tracked.lua"), s_openOptions);

		// A directory delete registers itself as an active operation like a document delete.
		Task<WorkspaceDocumentDirectoryDeleteResult> delete = store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")));
		await fileSystem.DirectoryDeletes.WhenReachedAsync(1);

		// Disposal cancels the in-flight recursive delete through the lifetime token and waits for the
		// registered operation.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentDirectoryDeleteResult result = await delete;

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Canceled, result.Outcome);
		await disposal;
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Dispose_WhileConflictResolutionWaitsForPendingRead_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;

		// Adopting the disk content holds the document's disk gate as a registered operation.
		Task<WorkspaceDocumentConflictResolutionResult> resolution = store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
				snapshot.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));
		await fileSystem.Reads.WhenReachedAsync(2);

		// Disposal cancels the in-flight read through the lifetime token and waits for the registered
		// operation.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentConflictResolutionResult result = await resolution;

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.Canceled, result.Outcome);
		await disposal;
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Dispose_WhileConflictResolutionUseLogicalWaitsForReplacement_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot dirty = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"logical",
			snapshot.FileFormat)).Snapshot!;

		// Resolving with the logical content runs the force-write through the commit path, which is
		// registered as an active operation like any other write.
		Task<WorkspaceDocumentConflictResolutionResult> resolution = store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(dirty.DocumentKey, dirty.DocumentId, dirty.Version),
				snapshot.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseLogical));
		await fileSystem.Replacements.WhenReachedAsync(1);

		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentConflictResolutionResult result = await resolution;

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.Canceled, result.Outcome);
		Assert.IsTrue(result.Snapshot!.IsDirty);
		await disposal;
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Dispose_WhileOpenWaitsForAnInFlightDelete_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingDelete = pendingDelete.Task;
		var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		Task<WorkspaceDocumentDeleteResult> delete = store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));
		await fileSystem.Deletes.WhenReachedAsync(1);

		// The delete removed the document from tracking before its file-system call, so an open of the
		// same path must wait for the delete to finish rather than reload a path that is about to vanish.
		Task<WorkspaceDocumentOpenResult> open = store.OpenAsync(snapshot.DocumentId, s_openOptions);

		// Disposal cancels the lifetime token while the open waits for the delete; the wait observed a
		// disposal of a store the caller found live, so the open reports Canceled like the delete.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentOpenResult result = await open;
		WorkspaceDocumentDeleteResult deleteResult = await delete;

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Canceled, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Canceled, deleteResult.Outcome);
		await disposal;
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Dispose_WhileAnOpenFollowerWaitsForTheOwner_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;
		var store = new WorkspaceDocumentStore(fileSystem);
		string filePath = TestPath("script.lua");

		// The first caller owns the load; the second joins the owner's reservation and waits for its
		// result instead of loading the same file twice.
		Task<WorkspaceDocumentOpenResult> owner = store.OpenAsync(filePath, s_openOptions);
		await fileSystem.Reads.WhenReachedAsync(1);
		Task<WorkspaceDocumentOpenResult> follower = store.OpenAsync(filePath, s_openOptions);

		// Disposal cancels the owner's read through the lifetime token. The follower is in flight but
		// untracked, so observing the disposal when it re-evaluates the path reports Canceled like the
		// owner instead of surfacing ObjectDisposedException mid-call.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentOpenResult followerResult = await follower;

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Canceled, followerResult.Outcome);
		Assert.IsNull(followerResult.Snapshot);
		await disposal;
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Canceled, (await owner).Outcome);
	}
}
