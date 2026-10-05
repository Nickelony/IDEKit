using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Dispose_WhileCommitWaitsForReplacement_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		fileSystem.PendingReplacement = pendingReplacement.Task;

		var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentSnapshot dirty = store
			.Replace(
				new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "changed", initial.FileFormat)
			)
			.Snapshot!;

		Task<WorkspaceDocumentCommitResult> commit = store.CommitAsync(
			new WorkspaceDocumentCommitRequest(new(dirty.DocumentKey, dirty.DocumentId, dirty.Version), dirty.OnDiskStamp),
			CancellationToken.None
		);

		await fileSystem.Replacements.WhenReachedAsync(1);

		// Disposal cancels the in-flight commit through the lifetime token, because the call's own token never cancels,
		// and waits for the registered operation before it disposes the per-document disk gates.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentCommitResult result = await commit.WaitAsync(TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Canceled, result.Outcome);
		Assert.IsTrue(result.Snapshot!.IsDirty);

		await disposal;
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Dispose_WhileCleanReloadReads_CompletesWithCanceledWhenTheReadIgnoresTheToken()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot clean = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		// The read ignores the cancellation token, so disposal cannot end it through the lifetime token; the
		// clean reload must still report Canceled instead of adopting the content after the store was disposed.
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReadIgnoringCancellation = pendingRead.Task;

		Task<WorkspaceDocumentReloadResult> reload = store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(clean.DocumentKey, clean.DocumentId, clean.Version)),
			TestContext.CancellationToken
		);

		await fileSystem.Reads.WhenReachedAsync(2);

		ValueTask disposal = store.DisposeAsync();

		pendingRead.SetResult(
			WorkspaceFileReadResult.FromContent(
				"external",
				s_defaultFormat,
				new FileStamp(Exists: true, Length: 8, LastWriteTimeUtc: DateTime.UnixEpoch.AddMinutes(1), ContentHash: "external")
			)
		);

		WorkspaceDocumentReloadResult result = await reload;

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Canceled, result.Outcome);
		Assert.AreEqual("content", result.Snapshot!.Content);

		await disposal;
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Dispose_WhileSaveAsWaitsForReplacement_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		fileSystem.PendingReplacement = pendingReplacement.Task;

		var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;
		string destinationPath = TestPath("folder", "destination.lua");

		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);

		Task<WorkspaceDocumentSaveAsResult> saveAs = store.SaveAsAsync(
			new WorkspaceDocumentSaveAsRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), initial.OnDiskStamp, destinationPath),
			CancellationToken.None
		);

		await fileSystem.Replacements.WhenReachedAsync(1);

		// Disposal cancels the in-flight save-as through the lifetime token, because the call's own token never cancels,
		// and waits for the registered operation before it disposes the per-document disk gates.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentSaveAsResult result = await saveAs.WaitAsync(TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.Canceled, result.Outcome);
		Assert.AreEqual(initial.DocumentId, result.Snapshot!.DocumentId);

		await disposal;
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Dispose_WhileOpenWaitsForPendingRead_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		fileSystem.PendingRead = pendingRead.Task;
		var store = new WorkspaceDocumentStore(fileSystem);

		Task<WorkspaceDocumentOpenResult> open = store.OpenAsync(TestPath("script.lua"), s_openOptions, CancellationToken.None);
		await fileSystem.Reads.WhenReachedAsync(1);

		// Disposal cancels the in-flight load through the lifetime token, because the call's own token never cancels,
		// and waits for the open reservation before it disposes the per-document gates.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentOpenResult result = await open.WaitAsync(TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Canceled, result.Outcome);
		Assert.IsNull(result.Snapshot);

		await disposal;
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Dispose_WhileAnOpenReadIgnoresTheToken_WaitsForTheOpenBeforeItCompletes()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		fileSystem.PendingReadIgnoringCancellation = pendingRead.Task;
		var store = new WorkspaceDocumentStore(fileSystem);

		Task<WorkspaceDocumentOpenResult> open = store.OpenAsync(TestPath("script.lua"), s_openOptions, CancellationToken.None);
		await fileSystem.Reads.WhenReachedAsync(1);

		Task disposal = store.DisposeAsync().AsTask();

		// Disposal captured the open's reservation in the same critical section that stopped the store, and the read never observes the
		// lifetime token, so disposal stays pending until the open resolves.
		Assert.IsFalse(disposal.IsCompleted);

		pendingRead.SetResult(fileSystem.CreateReadResult());

		WorkspaceDocumentOpenResult result = await open;

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Canceled, result.Outcome);

		await disposal;
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Dispose_WhileDeleteWaitsForPendingDelete_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		fileSystem.PendingDelete = pendingDelete.Task;
		var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		Task<WorkspaceDocumentDeleteResult> delete = store.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			CancellationToken.None
		);

		await fileSystem.Deletes.WhenReachedAsync(1);

		// Disposal cancels the in-flight delete through the lifetime token, because the call's own token never cancels,
		// and waits for the registered operation before it disposes the per-document gates.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentDeleteResult result = await delete.WaitAsync(TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Canceled, result.Outcome);
		Assert.AreEqual(snapshot.DocumentId, result.Snapshot!.DocumentId);

		await disposal;
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Dispose_CancelsTheLifetimeTokenOutsideTheStateLock()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingMove = new TaskCompletionSource<WorkspaceFileMoveResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		fileSystem.PendingMoveDirectory = pendingMove.Task;
		var store = new WorkspaceDocumentStore(fileSystem);
		await store.OpenAsync(TestPath("folder", "tracked.lua"), s_openOptions, TestContext.CancellationToken);

		bool lockHeldDuringCancellation = true;

		fileSystem.MoveDirectoryCancellationObserver = token =>
			token.Register(() => lockHeldDuringCancellation = store.TestHooks.IsStateLockHeldByCurrentThread, useSynchronizationContext: false);

		Task<WorkspaceDocumentDirectoryRenameResult> rename = store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(TestPath("folder"), TestPath("moved")),
			TestContext.CancellationToken
		);

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
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Dispose_FromAFileSystemCallbackOfARegisteredOperation_ThrowsAndTheOperationCompletes()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		await store.OpenAsync(TestPath("folder", "tracked.lua"), s_openOptions, TestContext.CancellationToken);

		InvalidOperationException? thrown = null;

		// Disposal waits for the directory rename, which waits for this callback, so the call could never complete.
		fileSystem.MoveDirectoryCancellationObserver = _ => thrown = Assert.ThrowsExactly<InvalidOperationException>(() => store.DisposeAsync());

		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(TestPath("folder"), TestPath("moved")),
			TestContext.CancellationToken
		);

		// The refused call began no disposal: the rename completed against a live store, which disposes normally afterwards.
		Assert.IsNotNull(thrown);
		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(TestPath("moved", "tracked.lua"), out _));
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Dispose_FromTheFileSystemReadOfAnOpen_ThrowsAndTheOpenCompletes()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		InvalidOperationException? thrown = null;

		// Disposal waits for the open, which waits for this read, so the call could never complete.
		fileSystem.ReadObserver = () => thrown = Assert.ThrowsExactly<InvalidOperationException>(() => store.DisposeAsync());

		WorkspaceDocumentOpenResult result = await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken);

		// The refused call began no disposal: the open completed against a live store, which disposes normally afterwards.
		Assert.IsNotNull(thrown);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, result.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(TestPath("script.lua"), out _));
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Dispose_AfterAnOpenCompleted_StillDisposes()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentOpenResult result = await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, result.Outcome);

		// The open ended with its mark, so a disposal from the caller's flow afterwards is neither refused nor left waiting.
		await store.DisposeAsync();

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
			store.OpenAsync(TestPath("other.lua"), s_openOptions, TestContext.CancellationToken)
		);
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Dispose_WithNothingInFlight_CompletesBeforeItReturns()
	{
		var store = new WorkspaceDocumentStore(new FakeFileSystem("content", s_defaultFormat));

		await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken);

		ValueTask disposal = store.DisposeAsync();

		// Disposal waits for the cancellation of the lifetime, which it performs itself, and for nothing else.
		Assert.IsTrue(disposal.IsCompletedSuccessfully);

		await disposal;
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Dispose_WithOnlyADirtyReloadInFlight_CancelsTheTokenOfItsStampCapture()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentSnapshot dirty = store
			.Replace(
				new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "changed", initial.FileFormat)
			)
			.Snapshot!;

		// The capture ends only through its token, so the reload finishes, and disposal with it, only if disposal cancels the lifetime.
		var neverCompletes = new TaskCompletionSource<FileStamp>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingCapturedStampObservingCancellation = neverCompletes.Task;

		Task<WorkspaceDocumentReloadResult> reload = store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(dirty.DocumentKey, dirty.DocumentId, dirty.Version)),
			CancellationToken.None
		);

		await fileSystem.Captures.WhenReachedAsync(1);

		await store.DisposeAsync();

		WorkspaceDocumentReloadResult result = await reload.WaitAsync(TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Canceled, result.Outcome);
		Assert.IsTrue(result.Snapshot!.IsDirty);
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Dispose_WhileCleanReloadWaitsForPendingRead_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;

		// A clean reload holds the document's disk gate and is registered as an active operation.
		Task<WorkspaceDocumentReloadResult> reload = store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version)),
			CancellationToken.None
		);

		await fileSystem.Reads.WhenReachedAsync(2);

		// Disposal cancels the in-flight read through the lifetime token, because the call's own token never cancels,
		// and waits for the registered operation before it disposes the per-document disk gates.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentReloadResult result = await reload.WaitAsync(TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Canceled, result.Outcome);
		Assert.AreEqual(snapshot.DocumentId, result.Snapshot!.DocumentId);

		await disposal;
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Dispose_WhileRenameWaitsForPendingMove_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingMove = new TaskCompletionSource<WorkspaceFileMoveResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		fileSystem.PendingMove = pendingMove.Task;
		var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		// A rename holds the document's disk gate as a registered operation and reserves its destination.
		Task<WorkspaceDocumentRenameResult> rename = store.RenameAsync(
			new WorkspaceDocumentRenameRequest(
				new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
				snapshot.OnDiskStamp,
				TestPath("renamed.lua")
			),

			CancellationToken.None
		);

		await fileSystem.Moves.WhenReachedAsync(1);

		// Disposal cancels the in-flight move through the lifetime token, because the call's own token never cancels,
		// and waits for the registered operation and its destination reservation.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentRenameResult result = await rename.WaitAsync(TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.Canceled, result.Outcome);
		Assert.AreEqual(snapshot.DocumentId, result.Snapshot!.DocumentId);

		await disposal;
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Dispose_WhileDirectoryDeleteWaitsForPendingDelete_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		fileSystem.PendingDirectoryDelete = pendingDelete.Task;
		var store = new WorkspaceDocumentStore(fileSystem);
		await store.OpenAsync(TestPath("folder", "tracked.lua"), s_openOptions, TestContext.CancellationToken);

		// A directory delete registers itself as an active operation like a document delete.
		Task<WorkspaceDocumentDirectoryDeleteResult> delete = store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")),
			CancellationToken.None
		);

		await fileSystem.DirectoryDeletes.WhenReachedAsync(1);

		// Disposal cancels the in-flight recursive delete through the lifetime token, because the call's own token never cancels,
		// and waits for the registered operation.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentDirectoryDeleteResult result = await delete.WaitAsync(TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Canceled, result.Outcome);
		await disposal;
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Dispose_WhileConflictResolutionWaitsForPendingRead_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;

		// Adopting the disk content holds the document's disk gate as a registered operation.
		Task<WorkspaceDocumentConflictResolutionResult> resolution = store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
				snapshot.OnDiskStamp,
				WorkspaceDocumentConflictResolutionMode.ResolveWithDisk
			),

			CancellationToken.None
		);

		await fileSystem.Reads.WhenReachedAsync(2);

		// Disposal cancels the in-flight read through the lifetime token, because the call's own token never cancels,
		// and waits for the registered operation.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentConflictResolutionResult result = await resolution.WaitAsync(TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.Canceled, result.Outcome);
		await disposal;
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Dispose_WhileConflictResolutionUseLogicalWaitsForReplacement_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		fileSystem.PendingReplacement = pendingReplacement.Task;

		var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentSnapshot dirty = store
			.Replace(
				new WorkspaceDocumentReplaceRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), "logical", snapshot.FileFormat)
			)
			.Snapshot!;

		// Resolving with the logical content runs the force-write through the commit path, which is
		// registered as an active operation like any other write.
		Task<WorkspaceDocumentConflictResolutionResult> resolution = store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(dirty.DocumentKey, dirty.DocumentId, dirty.Version),
				snapshot.OnDiskStamp,
				WorkspaceDocumentConflictResolutionMode.ResolveWithLogical
			),

			CancellationToken.None
		);

		await fileSystem.Replacements.WhenReachedAsync(1);

		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentConflictResolutionResult result = await resolution.WaitAsync(TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.Canceled, result.Outcome);
		Assert.IsTrue(result.Snapshot!.IsDirty);

		await disposal;
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Dispose_WhileOpenWaitsForAnInFlightDelete_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		fileSystem.PendingDelete = pendingDelete.Task;
		var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		Task<WorkspaceDocumentDeleteResult> delete = store.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			CancellationToken.None
		);

		await fileSystem.Deletes.WhenReachedAsync(1);

		// The delete reserves the document before its file-system call, so an open of the same path
		// must wait for the delete to finish rather than reload a path that is about to vanish.
		Task<WorkspaceDocumentOpenResult> open = store.OpenAsync(snapshot.DocumentId, s_openOptions, CancellationToken.None);

		// Disposal cancels the lifetime token while the open waits for the delete; the open was in flight,
		// so it reports Canceled like the delete instead of throwing. Neither call's own token ever cancels.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentOpenResult result = await open.WaitAsync(TestContext.CancellationToken);
		WorkspaceDocumentDeleteResult deleteResult = await delete.WaitAsync(TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Canceled, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Canceled, deleteResult.Outcome);

		await disposal;
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Dispose_WhileAnOpenFollowerWaitsForTheOwner_CompletesWithCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		fileSystem.PendingRead = pendingRead.Task;
		var store = new WorkspaceDocumentStore(fileSystem);
		string filePath = TestPath("script.lua");

		// The first caller owns the load; the second joins the owner's reservation and waits for its
		// result instead of loading the same file twice.
		Task<WorkspaceDocumentOpenResult> owner = store.OpenAsync(filePath, s_openOptions, CancellationToken.None);
		await fileSystem.Reads.WhenReachedAsync(1);

		Task<WorkspaceDocumentOpenResult> follower = store.OpenAsync(filePath, s_openOptions, CancellationToken.None);

		// Disposal cancels the owner's read through the lifetime token, because no call's own token ever cancels. The follower is
		// in flight but untracked, so observing the disposal when it re-evaluates the path reports Canceled like the
		// owner instead of surfacing ObjectDisposedException mid-call.
		ValueTask disposal = store.DisposeAsync();
		WorkspaceDocumentOpenResult followerResult = await follower.WaitAsync(TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Canceled, followerResult.Outcome);
		Assert.IsNull(followerResult.Snapshot);

		await disposal;
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Canceled, (await owner.WaitAsync(TestContext.CancellationToken)).Outcome);
	}
}
