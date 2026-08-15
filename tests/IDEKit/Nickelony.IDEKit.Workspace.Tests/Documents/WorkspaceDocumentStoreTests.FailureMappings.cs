using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task Rename_SourceStampConflict_ReportsExternalFileConflictAndKeepsTracking()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		FileStamp observedStamp = new(true, 9, DateTime.UnixEpoch.AddMinutes(1), "changed");
		fileSystem.MoveResult = new WorkspaceFileMoveResult(WorkspaceFileMoveOutcome.ExternalFileConflict, observedStamp);

		WorkspaceDocumentRenameResult result = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			TestPath("renamed.lua")));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(observedStamp, result.ObservedOnDiskStamp);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		// The failed move must release its destination reservation so the path can be opened later.
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, (await store.OpenAsync(TestPath("renamed.lua"), s_openOptions)).Outcome);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task RenameDirectory_GateContention_ReportsOperationInProgressAndReleasesAcquiredGates()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(TestPath("folder", "a.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot second = (await store.OpenAsync(TestPath("folder", "b.lua"), s_openOptions)).Snapshot!;

		// Hold the later document's gate with an in-flight commit so the directory rename acquires the
		// earlier document's gate and then fails on the later one.
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(second.DocumentKey, second.DocumentId, second.Version),
			"edited",
			second.FileFormat));
		Task<WorkspaceDocumentCommitResult> commit = store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp));
		await fileSystem.Replacements.WhenReachedAsync(1);

		WorkspaceDocumentDirectoryRenameResult rename = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(TestPath("folder"), TestPath("moved")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.OperationInProgress, rename.Outcome);

		pendingReplacement.SetResult(new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementOutcome.Replaced,
			new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(1), "committed")));
		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, (await commit).Outcome);

		// The failed rename must release the gates it acquired; otherwise, the earlier document would
		// keep reporting OperationInProgress for later disk operations.
		WorkspaceDocumentMutationResult replaced = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(first.DocumentKey, first.DocumentId, first.Version),
			"after failed rename",
			first.FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.Outcome);
	}

	[TestMethod]
	public async Task DeleteDirectory_StampMismatch_ReportsExternalFileConflictAndKeepsTracking()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions)).Snapshot!;
		FileStamp changedStamp = new(true, 42, DateTime.UnixEpoch.AddMinutes(1), "changed");
		fileSystem.CapturedStamps.Enqueue(changedStamp);

		WorkspaceDocumentDirectoryDeleteResult result = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")));

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ExternalFileConflict, result.Failure!.Code);
		Assert.AreEqual(0, fileSystem.DirectoryDeletes.Count);
		Assert.IsTrue(store.TryGetSnapshot(first.DocumentId, out _));
	}

	[TestMethod]
	public async Task DeleteDirectory_PreCanceledToken_ReportsCanceledWithoutDeleting()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		WorkspaceDocumentDirectoryDeleteResult result = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")),
			cancellation.Token);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Canceled, result.Outcome);
		Assert.AreEqual(0, fileSystem.DirectoryDeletes.Count);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Rename_GateContention_ReportsOperationInProgressWithoutMoving()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		// Hold the document's disk gate with an in-flight commit so the file rename cannot begin.
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"edited",
			initial.FileFormat));
		Task<WorkspaceDocumentCommitResult> commit = store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp));
		await fileSystem.Replacements.WhenReachedAsync(1);

		WorkspaceDocumentRenameResult rename = await store.RenameAsync(new WorkspaceDocumentRenameRequest(
			new(edited.Snapshot.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp,
			TestPath("renamed.lua")));

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.OperationInProgress, rename.Outcome);
		Assert.AreEqual(0, fileSystem.Moves.Count);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		pendingReplacement.SetResult(new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementOutcome.Replaced,
			new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(1), "committed")));
		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, (await commit).Outcome);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task SaveAs_GateContention_ReportsOperationInProgressWithoutWriting()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		string destinationPath = TestPath("folder", "destination.lua");

		// Hold the document's disk gate with an in-flight commit so the save-as cannot begin.
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"edited",
			initial.FileFormat));
		Task<WorkspaceDocumentCommitResult> commit = store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp));
		await fileSystem.Replacements.WhenReachedAsync(1);

		WorkspaceDocumentSaveAsResult saveAs = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(edited.Snapshot.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.OperationInProgress, saveAs.Outcome);

		// Only the in-flight commit wrote; the save-as did not start.
		Assert.AreEqual(1, fileSystem.ReplaceCount);
		Assert.IsFalse(store.TryGetSnapshot(destinationPath, out _));

		pendingReplacement.SetResult(new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementOutcome.Replaced,
			new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(1), "committed")));
		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, (await commit).Outcome);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Delete_GateContention_ReportsOperationInProgressWithoutDeleting()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		// Hold the document's disk gate with an in-flight commit so the delete cannot begin.
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"edited",
			initial.FileFormat));
		Task<WorkspaceDocumentCommitResult> commit = store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp));
		await fileSystem.Replacements.WhenReachedAsync(1);

		WorkspaceDocumentDeleteResult delete = await store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(edited.Snapshot.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.OperationInProgress, delete.Outcome);
		Assert.AreEqual(0, fileSystem.Deletes.Count);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		pendingReplacement.SetResult(new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementOutcome.Replaced,
			new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(1), "committed")));
		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, (await commit).Outcome);
	}

	[TestMethod]
	public async Task RenameDirectory_PermissionFailureWhileCapturingDescendantStamps_ReportsAccessDenied()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions)).Snapshot!;
		fileSystem.CaptureTasks.Enqueue(Task.FromException<FileStamp>(
			new UnauthorizedAccessException("The file could not be read.")));

		// The pre-move descendant capture is a read the store performs itself: a permission failure
		// there reports AccessDenied like the other read paths instead of the generic move failure.
		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(TestPath("folder"), TestPath("moved")));

		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.MoveFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.AccessDenied, result.Failure!.Code);
		Assert.AreEqual(0, fileSystem.MoveDirectoryCount);
		Assert.IsTrue(store.TryGetSnapshot(first.DocumentId, out _));
	}

	[TestMethod]
	public async Task DeleteDirectory_PermissionFailureWhileCapturingDescendantStamps_ReportsAccessDenied()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions)).Snapshot!;
		fileSystem.CaptureTasks.Enqueue(Task.FromException<FileStamp>(
			new UnauthorizedAccessException("The file could not be read.")));

		WorkspaceDocumentDirectoryDeleteResult result = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")));

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.DeleteFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.AccessDenied, result.Failure!.Code);
		Assert.AreEqual(0, fileSystem.DirectoryDeletes.Count);
		Assert.IsTrue(store.TryGetSnapshot(first.DocumentId, out _));
	}
}
