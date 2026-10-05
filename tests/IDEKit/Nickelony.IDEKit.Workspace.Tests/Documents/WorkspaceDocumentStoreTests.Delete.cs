using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task Delete_BlocksLogicalReplacementUntilDeletionCompletes()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		fileSystem.PendingDelete = pendingDelete.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		Task<WorkspaceDocumentDeleteResult> delete = store.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), initial.OnDiskStamp),
			TestContext.CancellationToken
		);

		await fileSystem.Deletes.WhenReachedAsync(1);

		WorkspaceDocumentMutationResult replacement = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "later", initial.FileFormat)
		);

		pendingDelete.SetResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted));

		WorkspaceDocumentDeleteResult deleted = await delete;

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.OperationInProgress, replacement.Outcome);
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, deleted.Outcome);
		Assert.IsFalse(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task Delete_ForwardsTheExpectedStampToTheFileSystem()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentDeleteResult result = await store.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		// The caller's stamp is the delete precondition; the file system must receive exactly that
		// value so a file that changed since the snapshot is never deleted silently.
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, result.Outcome);
		Assert.AreEqual(snapshot.OnDiskStamp, fileSystem.LastDeleteExpectedStamp);
	}

	[TestMethod]
	public async Task Delete_PathOccupiedByADirectory_ReportsIsDirectoryAndKeepsTracking()
	{
		using var temp = new TemporaryDirectory();
		var fileSystem = new LocalWorkspaceFileSystem();
		await using var store = new WorkspaceDocumentStore(fileSystem);

		string documentPath = Path.Combine(temp.Path, "notes.txt");
		WorkspaceDocumentSnapshot opened = (await store.OpenAsync(documentPath, s_openOptions, TestContext.CancellationToken)).Snapshot!;

		// The tracked path is a not-yet-written document and a directory now occupies it. The delete
		// must report the path-kind failure instead of untracking the document as if it were deleted.
		Directory.CreateDirectory(documentPath);

		WorkspaceDocumentDeleteResult result = await store.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(opened.DocumentKey, opened.DocumentId, opened.Version), opened.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.DeleteFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.IsDirectory, result.Failure?.Code);
		Assert.IsTrue(Directory.Exists(documentPath));
		Assert.IsTrue(store.TryGetSnapshot(documentPath, out _));
	}

	[TestMethod]
	public async Task DeleteDirectory_WithoutTrackedDescendants_StillDeletesTheDirectory()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentDirectoryDeleteResult result = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("empty")),
			TestContext.CancellationToken
		);

		// A folder without tracked documents is still a recursive file-system delete; only the
		// tracking cleanup has nothing to do.
		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Deleted, result.Outcome);
		Assert.IsEmpty(result.Snapshots);
		Assert.AreEqual(TestPath("empty"), fileSystem.LastDirectoryDeletePath);
	}

	[TestMethod]
	public async Task GetSnapshotsUnderDirectory_TrailingSeparator_ReturnsTheSameSnapshots()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions, TestContext.CancellationToken);

		IReadOnlyList<WorkspaceDocumentSnapshot> plain = store.GetSnapshotsUnderDirectory(TestPath("folder"));
		IReadOnlyList<WorkspaceDocumentSnapshot> trailing = store.GetSnapshotsUnderDirectory(TestPath("folder") + Path.DirectorySeparatorChar);

		// A trailing separator normalizes to the same directory identity, so both spellings list the
		// same tracked descendants.
		Assert.HasCount(1, plain);
		Assert.HasCount(1, trailing);
		Assert.AreEqual(plain[0].DocumentId, trailing[0].DocumentId);
	}

	[TestMethod]
	public async Task DeleteAndDirectoryDelete_Failures_MapToOutcomes()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot file = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		fileSystem.DeleteOutcome = WorkspaceFileDeleteOutcome.DeleteFailed;

		WorkspaceDocumentDeleteResult delete = await store.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(file.DocumentKey, file.DocumentId, file.Version), file.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.DeleteFailed, delete.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(file.DocumentId, out _));

		fileSystem.DeleteOutcome = WorkspaceFileDeleteOutcome.Deleted;
		fileSystem.DirectoryDeleteOutcome = WorkspaceFileDeleteOutcome.DeleteFailed;

		WorkspaceDocumentSnapshot directoryFile = (
			await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions, TestContext.CancellationToken)
		).Snapshot!;

		WorkspaceDocumentDirectoryDeleteResult directoryDelete = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.DeleteFailed, directoryDelete.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(directoryFile.DocumentId, out _));
	}

	[TestMethod]
	public async Task DeleteDirectory_PassesNormalizedDirectoryIdToTheFileSystem()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot first = (
			await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions, TestContext.CancellationToken)
		).Snapshot!;

		WorkspaceDocumentDirectoryDeleteResult result = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder", ".")),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Deleted, result.Outcome);
		Assert.AreEqual(TestPath("folder"), fileSystem.LastDirectoryDeletePath);
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task DeleteDirectory_GateContention_LeavesEarlierDocumentsReplaceable()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		fileSystem.PendingReplacement = pendingReplacement.Task;

		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot first = (
			await store.OpenAsync(TestPath("folder", "a.lua"), s_openOptions, TestContext.CancellationToken)
		).Snapshot!;

		WorkspaceDocumentSnapshot second = (
			await store.OpenAsync(TestPath("folder", "b.lua"), s_openOptions, TestContext.CancellationToken)
		).Snapshot!;

		// Hold the later document's gate with an in-flight commit so the directory delete acquires the
		// earlier document's gate and then fails on the later one.
		WorkspaceDocumentMutationResult edited = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(second.DocumentKey, second.DocumentId, second.Version), "edited", second.FileFormat)
		);

		Task<WorkspaceDocumentCommitResult> commit = store.CommitAsync(
			new WorkspaceDocumentCommitRequest(
				new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
				edited.Snapshot.OnDiskStamp
			),

			TestContext.CancellationToken
		);

		await fileSystem.Replacements.WhenReachedAsync(1);

		WorkspaceDocumentDirectoryDeleteResult delete = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.OperationInProgress, delete.Outcome);

		pendingReplacement.SetResult(
			new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementOutcome.Replaced,
				new FileStamp(Exists: true, Length: 7, LastWriteTimeUtc: DateTime.UnixEpoch.AddMinutes(1), ContentHash: "committed")
			)
		);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, (await commit).Outcome);

		// The failed delete must not leave the earlier document's delete reservation behind; otherwise, it
		// rejects every later Replace with OperationInProgress.
		WorkspaceDocumentMutationResult replaced = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(first.DocumentKey, first.DocumentId, first.Version), "after failed delete", first.FileFormat)
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.Outcome);
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task DeleteDirectory_ContentionWithAFileDelete_DoesNotCompleteTheFileDeleteReservation()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		fileSystem.PendingDelete = pendingDelete.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot descendant = (
			await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions, TestContext.CancellationToken)
		).Snapshot!;

		// A file delete holds the descendant's gate and reserves the document.
		Task<WorkspaceDocumentDeleteResult> delete = store.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(descendant.DocumentKey, descendant.DocumentId, descendant.Version), descendant.OnDiskStamp),
			TestContext.CancellationToken
		);

		await fileSystem.Deletes.WhenReachedAsync(1);

		// The directory delete collects the same descendant, fails to acquire its gate, and must not
		// complete the file delete's reservation.
		WorkspaceDocumentDirectoryDeleteResult directoryDelete = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.OperationInProgress, directoryDelete.Outcome);

		// The descendant still reports the in-flight file delete instead of accepting the edit.
		WorkspaceDocumentMutationResult replaced = store.Replace(
			new WorkspaceDocumentReplaceRequest(
				new(descendant.DocumentKey, descendant.DocumentId, descendant.Version),
				"during the file delete",
				descendant.FileFormat
			)
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.OperationInProgress, replaced.Outcome);

		pendingDelete.SetResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted));
		await delete;
	}

	[TestMethod]
	public async Task GetSnapshotsUnderDirectory_DoesNotMatchSiblingWithSharedPrefix()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		await store.OpenAsync(TestPath("a", "one.lua"), s_openOptions, TestContext.CancellationToken);
		await store.OpenAsync(TestPath("ab", "two.lua"), s_openOptions, TestContext.CancellationToken);

		IReadOnlyList<WorkspaceDocumentSnapshot> underA = store.GetSnapshotsUnderDirectory(TestPath("a"));

		Assert.HasCount(1, underA);
		Assert.AreEqual(Path.GetFullPath(TestPath("a", "one.lua")), underA[0].DocumentId);
	}

	[TestMethod]
	public async Task GetSnapshotsUnderDirectory_IncludesADocumentTrackedAtTheDirectoryId()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot atDirectory = (await store.OpenAsync(TestPath("folder"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentSnapshot descendant = (
			await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions, TestContext.CancellationToken)
		).Snapshot!;

		IReadOnlyList<WorkspaceDocumentSnapshot> snapshots = store.GetSnapshotsUnderDirectory(TestPath("folder"));

		// A document tracked at the exact directory id is included, matching the subtree operations that
		// reach it too.
		Assert.HasCount(2, snapshots);
		Assert.AreEqual(atDirectory.DocumentId, snapshots[0].DocumentId, "The directory id sorts before its descendant.");
		Assert.AreEqual(descendant.DocumentId, snapshots[1].DocumentId);
	}

	[TestMethod]
	public async Task DeleteDirectory_AllTrackedDescendants_AreRemovedFromTracking()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot first = (
			await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions, TestContext.CancellationToken)
		).Snapshot!;

		WorkspaceDocumentSnapshot second = (
			await store.OpenAsync(TestPath("folder", "two.lua"), s_openOptions, TestContext.CancellationToken)
		).Snapshot!;

		// Every tracked descendant is part of the recursive delete because it applies to the whole
		// directory on disk.
		WorkspaceDocumentDirectoryDeleteResult result = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Deleted, result.Outcome);
		Assert.HasCount(2, result.Snapshots);
		Assert.IsFalse(store.TryGetSnapshot(first.DocumentId, out _));
		Assert.IsFalse(store.TryGetSnapshot(second.DocumentId, out _));
		Assert.IsEmpty(store.GetSnapshotsUnderDirectory(TestPath("folder")));
	}

	[TestMethod]
	public async Task Delete_CanceledFileSystemDelete_ReportsCanceledAndKeepsTracking()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat) { DeleteOutcome = WorkspaceFileDeleteOutcome.Canceled };
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot snapshot = (
			await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions, TestContext.CancellationToken)
		).Snapshot!;

		WorkspaceDocumentDeleteResult result = await store.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Canceled, result.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(snapshot.DocumentId, out _));
	}

	[TestMethod]
	public async Task Delete_ThrowingFileSystem_ReportsDeleteFailedAndKeepsTracking()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat) { ThrowOnDelete = true };
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot snapshot = (
			await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions, TestContext.CancellationToken)
		).Snapshot!;

		WorkspaceDocumentDeleteResult result = await store.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.DeleteFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.DeleteFailed, result.Failure?.Code);
		Assert.IsTrue(store.TryGetSnapshot(snapshot.DocumentId, out _));
	}

	[TestMethod]
	public async Task Delete_PreCanceledDelete_ReportsCanceledAndKeepsTracking()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot snapshot = (
			await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions, TestContext.CancellationToken)
		).Snapshot!;

		WorkspaceDocumentDeleteResult result = await store.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			cancellation.Token
		);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Canceled, result.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(snapshot.DocumentId, out _));
	}

	[TestMethod]
	public async Task DeleteDirectory_CanceledFileSystemDelete_ReportsCanceledAndKeepsTracking()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat) { DirectoryDeleteOutcome = WorkspaceFileDeleteOutcome.Canceled };
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot snapshot = (
			await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions, TestContext.CancellationToken)
		).Snapshot!;

		WorkspaceDocumentDirectoryDeleteResult result = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Canceled, result.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(snapshot.DocumentId, out _));
	}

	[TestMethod]
	public async Task DeleteDirectory_ThrowingFileSystem_ReportsDeleteFailedAndKeepsTracking()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat) { ThrowOnDirectoryDelete = true };
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot snapshot = (
			await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions, TestContext.CancellationToken)
		).Snapshot!;

		WorkspaceDocumentDirectoryDeleteResult result = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.DeleteFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.DeleteFailed, result.Failure?.Code);
		Assert.IsTrue(store.TryGetSnapshot(snapshot.DocumentId, out _));
	}

	[TestMethod]
	public async Task Delete_FailedDelete_ReleasesTheDeleteReservationSoTheDocumentIsEditableAgain()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat) { ThrowOnDelete = true };
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot snapshot = (
			await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions, TestContext.CancellationToken)
		).Snapshot!;

		WorkspaceDocumentDeleteResult deleted = await store.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);

		// The failed delete completed its reservation: a replacement is accepted again instead of reporting an operation in progress.
		WorkspaceDocumentMutationResult replacement = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), "later", snapshot.FileFormat)
		);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.DeleteFailed, deleted.Outcome);
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replacement.Outcome);
	}

	[TestMethod]
	public async Task DeleteDirectory_InFlight_BlocksLogicalReplacementOfEveryDescendantUntilItCompletes()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		fileSystem.PendingDirectoryDelete = pendingDelete.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot first = (
			await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions, TestContext.CancellationToken)
		).Snapshot!;

		WorkspaceDocumentSnapshot second = (
			await store.OpenAsync(TestPath("folder", "two.lua"), s_openOptions, TestContext.CancellationToken)
		).Snapshot!;

		Task<WorkspaceDocumentDirectoryDeleteResult> delete = store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")),
			TestContext.CancellationToken
		);

		await fileSystem.DirectoryDeletes.WhenReachedAsync(1);

		WorkspaceDocumentMutationResult firstReplacement = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(first.DocumentKey, first.DocumentId, first.Version), "later", first.FileFormat)
		);

		WorkspaceDocumentMutationResult secondReplacement = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(second.DocumentKey, second.DocumentId, second.Version), "later", second.FileFormat)
		);

		pendingDelete.SetResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted));

		WorkspaceDocumentDirectoryDeleteResult deleted = await delete;

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.OperationInProgress, firstReplacement.Outcome);
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.OperationInProgress, secondReplacement.Outcome);
		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.Deleted, deleted.Outcome);
	}

	[TestMethod]
	public async Task DeleteDirectory_FailedDelete_ReleasesTheDeleteReservationsSoTheDescendantsAreEditableAgain()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat) { ThrowOnDirectoryDelete = true };
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot first = (
			await store.OpenAsync(TestPath("folder", "one.lua"), s_openOptions, TestContext.CancellationToken)
		).Snapshot!;

		WorkspaceDocumentSnapshot second = (
			await store.OpenAsync(TestPath("folder", "two.lua"), s_openOptions, TestContext.CancellationToken)
		).Snapshot!;

		WorkspaceDocumentDirectoryDeleteResult deleted = await store.DeleteDirectoryAsync(
			new WorkspaceDocumentDirectoryDeleteRequest(TestPath("folder")),
			TestContext.CancellationToken
		);

		WorkspaceDocumentMutationResult firstReplacement = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(first.DocumentKey, first.DocumentId, first.Version), "later", first.FileFormat)
		);

		WorkspaceDocumentMutationResult secondReplacement = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(second.DocumentKey, second.DocumentId, second.Version), "later", second.FileFormat)
		);

		Assert.AreEqual(WorkspaceDocumentDirectoryDeleteOutcome.DeleteFailed, deleted.Outcome);
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, firstReplacement.Outcome);
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, secondReplacement.Outcome);
	}
}
