using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task ResolveExternalConflict_UseDisk_InstallsCurrentDiskContent()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		FileStamp externalStamp = new(true, 3, DateTime.UnixEpoch.AddMinutes(1), "disk");
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.FromContent(
			"disk",
			s_defaultFormat,
			externalStamp)));

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				externalStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ResolvedWithDisk, result.Outcome);
		Assert.AreEqual("disk", result.Snapshot!.Content);
		Assert.AreEqual(2, result.Snapshot.Version);
		Assert.AreEqual(2, result.Snapshot.PersistedVersion);
		Assert.IsFalse(result.Snapshot.IsDirty);
		Assert.AreEqual(externalStamp, result.Snapshot.OnDiskStamp);
		Assert.AreEqual(2, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseLogical_OverwritesCurrentDiskAndCommitsBaseline()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		FileStamp externalStamp = new(true, 6, DateTime.UnixEpoch.AddMinutes(1), "external");
		FileStamp committedStamp = new(true, 7, DateTime.UnixEpoch.AddMinutes(2), "committed");
		fileSystem.ReplacementStamp = committedStamp;

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				externalStamp,
				WorkspaceDocumentConflictResolutionChoice.UseLogical));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ResolvedWithLogical, result.Outcome);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.AreEqual(1, result.Snapshot.Version);
		Assert.AreEqual(1, result.Snapshot.PersistedVersion);
		Assert.IsFalse(result.Snapshot.IsDirty);
		Assert.AreEqual(committedStamp, result.Snapshot.OnDiskStamp);
		Assert.AreEqual(externalStamp, fileSystem.LastReplacementExpectedStamp);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseDisk_AdoptsAMissingFileAsEmptyContentKeepingTheFormat()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.Missing));

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				FileStamp.Missing,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ResolvedWithDisk, result.Outcome);
		Assert.AreEqual(string.Empty, result.Snapshot!.Content);
		Assert.AreEqual(FileStamp.Missing, result.Snapshot.OnDiskStamp);
		Assert.IsFalse(result.Snapshot.IsDirty);

		// A missing file has no on-disk format to adopt, so the document keeps the format it had.
		Assert.AreEqual(initial.FileFormat, result.Snapshot.FileFormat);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseLogical_CreatesAMissingFileFromLogicalContent()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		FileStamp committedStamp = new(true, 7, DateTime.UnixEpoch.AddMinutes(2), "committed");
		fileSystem.ReplacementStamp = committedStamp;

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				FileStamp.Missing,
				WorkspaceDocumentConflictResolutionChoice.UseLogical));

		// The missing file is created from the logical content with a missing-file expectation.
		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ResolvedWithLogical, result.Outcome);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.AreEqual(FileStamp.Missing, fileSystem.LastReplacementExpectedStamp);
		Assert.AreEqual(committedStamp, result.Snapshot.OnDiskStamp);
		Assert.IsFalse(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseDiskOnADirectory_ReportsReadFailed()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.Directory));

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				FileStamp.Missing,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		// A directory is not disk content to adopt: the resolution fails with the directory-specific
		// code and keeps the logical content.
		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ReadFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.IsDirectory, result.Failure?.Code);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseDisk_AdoptsTheReadStateWithASingleRead()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		FileStamp diskStamp = new(true, 3, DateTime.UnixEpoch.AddMinutes(1), "disk");
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.FromContent(
			"disk",
			s_defaultFormat,
			diskStamp)));

		// The read carries the stamp of the bytes it returned, so the resolution must not re-capture the
		// destination. The enqueued capture is never consumed when that holds.
		fileSystem.CapturedStamps.Enqueue(new FileStamp(true, 4, DateTime.UnixEpoch.AddMinutes(2), "later"));

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				diskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ResolvedWithDisk, result.Outcome);
		Assert.AreEqual("disk", result.Snapshot!.Content);
		Assert.AreEqual(diskStamp, result.Snapshot.OnDiskStamp);
		Assert.IsFalse(result.Snapshot.IsDirty);
		Assert.AreEqual(1, fileSystem.CapturedStamps.Count);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseDisk_RejectsAReadThatNoLongerMatchesTheObservation()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		FileStamp observedStamp = new(true, 3, DateTime.UnixEpoch.AddMinutes(1), "observed");
		FileStamp readStamp = new(true, 4, DateTime.UnixEpoch.AddMinutes(2), "read");
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.FromContent(
			"disk",
			s_defaultFormat,
			readStamp)));

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				observedStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		// The disk changed again after the conflict was observed, so the logical content is retained
		// (still dirty) and the fresh stamp is reported for the next attempt.
		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.AreEqual(readStamp, result.ObservedOnDiskStamp);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseLogicalWhileDeleteInFlight_ReportsOperationInProgress()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingDelete = pendingDelete.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));

		Task<WorkspaceDocumentDeleteResult> delete = store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
			initial.OnDiskStamp));
		await fileSystem.Deletes.WhenReachedAsync(1);

		// Resolving with the logical content writes the document, so it is rejected while the delete
		// that is about to remove it is in flight: the force-write must not re-create the file the
		// delete is removing.
		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot.Version),
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseLogical));

		pendingDelete.SetResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted));
		WorkspaceDocumentDeleteResult deleted = await delete;

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.OperationInProgress, result.Outcome);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.AreEqual(0, fileSystem.ReplaceCount);
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, deleted.Outcome);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UndefinedChoice_IsAnArgumentError()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		// An undefined choice must not silently take the UseDisk branch; it is rejected as an
		// argument error instead of discarding unpublishable logical content.
		await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, initial.Version),
				initial.OnDiskStamp,
				(WorkspaceDocumentConflictResolutionChoice)42)));
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseDisk_ReturnsStaleAfterLogicalEditDuringRead()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;
		Task<WorkspaceDocumentConflictResolutionResult> resolutionTask = store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));
		await fileSystem.Reads.WhenReachedAsync(2);

		WorkspaceDocumentMutationResult laterEdit = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot.Version),
			"later logical",
			initial.FileFormat));
		pendingRead.SetResult(WorkspaceFileReadResult.FromContent("disk", s_defaultFormat, initial.OnDiskStamp));

		WorkspaceDocumentConflictResolutionResult result = await resolutionTask;

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.StaleDocument, result.Outcome);
		Assert.AreEqual(laterEdit.Snapshot!.Version, result.Snapshot!.Version);
		Assert.AreEqual("later logical", result.Snapshot.Content);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseLogical_KeepsLaterLogicalEditDirty()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		FileStamp observedStamp = new(true, 8, DateTime.UnixEpoch.AddMinutes(1), "external");

		// The conditional replacement validates the observed stamp itself, so no capture is enqueued.
		Task<WorkspaceDocumentConflictResolutionResult> resolutionTask = store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				observedStamp,
				WorkspaceDocumentConflictResolutionChoice.UseLogical));
		await fileSystem.Replacements.WhenReachedAsync(1);

		WorkspaceDocumentMutationResult laterEdit = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot.Version),
			"later logical",
			initial.FileFormat));
		pendingReplacement.SetResult(new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementOutcome.Replaced,
			new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(1), "committed")));

		WorkspaceDocumentConflictResolutionResult result = await resolutionTask;

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ResolvedWithLogical, result.Outcome);
		Assert.AreEqual(laterEdit.Snapshot!.Version, result.Snapshot!.Version);
		Assert.AreEqual(edited.Snapshot.Version, result.Snapshot.PersistedVersion);
		Assert.AreEqual("later logical", result.Snapshot.Content);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseLogical_RejectsSecondDiskChange()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		FileStamp observedStamp = new(true, 6, DateTime.UnixEpoch.AddMinutes(1), "observed");
		FileStamp secondStamp = new(true, 7, DateTime.UnixEpoch.AddMinutes(2), "second");

		// The destination changed again after the conflict was observed; the conditional replacement
		// detects it and reports the conflict with the newly observed stamp instead of overwriting.
		fileSystem.ReplacementOutcome = WorkspaceFileReplacementOutcome.ExternalFileConflict;
		fileSystem.ReplacementStamp = secondStamp;

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				observedStamp,
				WorkspaceDocumentConflictResolutionChoice.UseLogical));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(secondStamp, result.ObservedOnDiskStamp);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.IsTrue(result.Snapshot.IsDirty);

		// The replacement is the validation point, so it was attempted before the conflict surfaced.
		Assert.AreEqual(1, fileSystem.ReplaceCount);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UnknownDocument_ReportsNotFound()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, TestPath("other.lua"), initial.Version),
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.DocumentNotFound, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(1, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_StaleDocumentKey_ReportsStaleInstance()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(new WorkspaceDocumentKey(Guid.NewGuid()), initial.DocumentId, initial.Version),
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.StaleDocumentInstance, result.Outcome);
		Assert.IsNotNull(result.Snapshot);
		Assert.AreEqual(initial.Content, result.Snapshot.Content);
		Assert.AreEqual(initial.Version, result.Snapshot.Version);
		Assert.AreEqual(1, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_WhileDeleteInFlight_ReportsOperationInProgress()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingDelete = pendingDelete.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		Task<WorkspaceDocumentDeleteResult> delete = store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp));
		await fileSystem.Deletes.WhenReachedAsync(1);

		// The delete holds the document's disk-operation gate, so the conflict resolution is rejected
		// instead of racing the removal.
		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, initial.Version),
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		pendingDelete.SetResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted));
		WorkspaceDocumentDeleteResult deleted = await delete;

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.OperationInProgress, result.Outcome);
		Assert.IsNotNull(result.Snapshot);
		Assert.AreEqual(initial.Content, result.Snapshot.Content);
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, deleted.Outcome);
		Assert.AreEqual(1, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_ReadFailure_ReportsReadFailed()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		FileStamp observedStamp = new(true, 8, DateTime.UnixEpoch.AddMinutes(1), "external");
		fileSystem.ReadResults.Enqueue(Task.FromException<WorkspaceFileReadResult>(new IOException("read failed")));

		// The read itself faulting is a read failure with the logical content retained, not a conflict;
		// the caller can retry once the file system recovers.
		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				observedStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ReadFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ReadFailed, result.Failure!.Code);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.IsTrue(result.Snapshot.IsDirty);
		Assert.AreEqual(2, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseLogicalUnencodableContent_ReportsWriteFailed()
	{
		var fileSystem = new FakeFileSystem("disk", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		TextFileFormat windows1252Format = new(TextEncodingKind.Windows1252, false, TextNewlineStyle.None);
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"\u2603",
			windows1252Format));
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, edited.Outcome);

		// The force-write cannot encode the logical content in the document's format, so the resolution
		// reports a write failure classified as an encoding problem and keeps the content dirty.
		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseLogical));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.WriteFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.InvalidEncoding, result.Failure!.Code);
		Assert.AreEqual(0, fileSystem.ReplaceCount);
		Assert.IsTrue(result.Snapshot!.IsDirty);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseLogicalStampRecaptureFailure_ReportsStateUnknown()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));

		// The replacement completes but reports no resulting stamp, and the re-capture fails: whether the
		// write took effect is unknown, so the outcome is state-unknown rather than a failure or a commit.
		// The state-unknown failure carries the capture exception for post-mortems.
		var captureFailure = new IOException("capture failed");
		fileSystem.OmitReplacementStamp = true;
		fileSystem.CaptureTasks.Enqueue(Task.FromException<FileStamp>(captureFailure));

		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseLogical));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ReplacementStateUnknown, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ReplacementStateUnknown, result.Failure!.Code);
		Assert.AreSame(captureFailure, result.Failure.Exception);
		StringAssert.Contains(result.Failure.Message, "capture failed");
		Assert.AreEqual(1, fileSystem.ReplaceCount);
		Assert.IsTrue(result.Snapshot!.IsDirty);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_CancellationDuringRead_ReportsCanceled()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;

		using var cancellation = new CancellationTokenSource();
		Task<WorkspaceDocumentConflictResolutionResult> resolutionTask = store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk),
			cancellation.Token);
		await fileSystem.Reads.WhenReachedAsync(2);
		cancellation.Cancel();

		WorkspaceDocumentConflictResolutionResult result = await resolutionTask;

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.Canceled, result.Outcome);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_StaleVersion_IsRejectedBeforeTouchingDisk()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));

		// The request carries the pre-edit version; the stale expectation is rejected by the
		// operation preamble before any conflict work starts.
		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, initial.Version),
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.StaleDocument, result.Outcome);
		Assert.AreEqual(edited.Snapshot!.Version, result.Snapshot!.Version);
		Assert.AreEqual(1, fileSystem.ReadCount);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task ResolveExternalConflict_CancellationDuringWrite_ReportsCanceled()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));

		using var cancellation = new CancellationTokenSource();
		Task<WorkspaceDocumentConflictResolutionResult> resolutionTask = store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseLogical),
			cancellation.Token);
		await fileSystem.Replacements.WhenReachedAsync(1);
		cancellation.Cancel();

		WorkspaceDocumentConflictResolutionResult result = await resolutionTask;

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.Canceled, result.Outcome);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task ResolveExternalConflict_UseDiskPermissionFailure_ReportsAccessDenied()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		fileSystem.ReadResults.Enqueue(Task.FromException<WorkspaceFileReadResult>(
			new UnauthorizedAccessException("The file could not be read.")));

		// The conflict-resolution read maps a permission failure to AccessDenied like the reload read
		// path, so the host sees one permission vocabulary across read and write operations.
		WorkspaceDocumentConflictResolutionResult result = await store.ResolveExternalConflictAsync(
			new WorkspaceDocumentConflictResolutionRequest(
				new(initial.DocumentKey, initial.DocumentId, initial.Version),
				initial.OnDiskStamp,
				WorkspaceDocumentConflictResolutionChoice.UseDisk));

		Assert.AreEqual(WorkspaceDocumentConflictResolutionOutcome.ReadFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.AccessDenied, result.Failure!.Code);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}
}
