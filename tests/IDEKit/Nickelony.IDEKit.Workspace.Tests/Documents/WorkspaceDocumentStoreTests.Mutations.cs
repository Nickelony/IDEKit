using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task Replacement_RetainsLogicalContentWhenOpenedAgain()
	{
		var fileSystem = new FakeFileSystem("disk", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentOpenResult opened = await store.OpenAsync(TestPath("script.lua"), s_openOptions);
		WorkspaceDocumentSnapshot initial = opened.Snapshot!;
		WorkspaceDocumentMutationResult noOp = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.Content,
			initial.FileFormat));

		WorkspaceDocumentMutationResult replaced = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		WorkspaceDocumentOpenResult reopened = await store.OpenAsync(TestPath("script.lua"), s_openOptions);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.NoChange, noOp.Outcome);
		Assert.AreEqual(0, noOp.Snapshot!.Version);
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, replaced.Outcome);
		Assert.IsNotNull(replaced.Snapshot);
		Assert.IsTrue(replaced.Snapshot.IsDirty);
		Assert.AreEqual("disk", initial.Content);
		Assert.AreEqual("logical", reopened.Snapshot!.Content);
		Assert.AreEqual(replaced.Snapshot.Version, reopened.Snapshot.Version);
		Assert.AreEqual(1, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task ReplacementAndUndoToBaseline_AdvanceVersionButRestoreCleanState()
	{
		var fileSystem = new FakeFileSystem("baseline", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"edited",
			initial.FileFormat));
		WorkspaceDocumentMutationResult undone = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
			initial.Content,
			initial.FileFormat));

		Assert.AreEqual(1, edited.Snapshot.Version);
		Assert.IsTrue(edited.Snapshot.IsDirty);
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, undone.Outcome);
		Assert.AreEqual(2, undone.Snapshot!.Version);
		Assert.AreEqual(0, undone.Snapshot.PersistedVersion);
		Assert.IsFalse(undone.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task StaleVersionAndKey_ReturnCurrentSnapshotWithoutMutation()
	{
		var fileSystem = new FakeFileSystem("current", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult changed = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat));

		WorkspaceDocumentMutationResult staleVersion = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"stale",
			initial.FileFormat));
		WorkspaceDocumentMutationResult staleKey = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(new WorkspaceDocumentKey(Guid.NewGuid()), initial.DocumentId, changed.Snapshot!.Version),
			"stale key",
			initial.FileFormat));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.StaleDocument, staleVersion.Outcome);
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.StaleDocumentInstance, staleKey.Outcome);
		Assert.IsNotNull(staleVersion.Snapshot);
		Assert.IsNotNull(staleKey.Snapshot);
		Assert.AreEqual("changed", staleVersion.Snapshot.Content);
		Assert.AreEqual(changed.Snapshot.Version, staleVersion.Snapshot.Version);
		Assert.AreEqual(staleVersion.Snapshot.Content, staleKey.Snapshot.Content);
	}

	[TestMethod]
	public async Task Discard_RestoresContentAndFormatAndIsNoOpWhenClean()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		TextFileFormat changedFormat = new(TextEncodingKind.Utf16LittleEndian, true, TextNewlineStyle.CrLf);
		WorkspaceDocumentMutationResult changed = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			changedFormat));
		WorkspaceDocumentMutationResult discarded = store.Discard(new WorkspaceDocumentDiscardRequest(
			new(initial.DocumentKey, initial.DocumentId, changed.Snapshot!.Version)));
		WorkspaceDocumentMutationResult noOp = store.Discard(new WorkspaceDocumentDiscardRequest(
			new(initial.DocumentKey, initial.DocumentId, discarded.Snapshot!.Version)));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, discarded.Outcome);
		Assert.AreEqual(initial.Content, discarded.Snapshot!.Content);
		Assert.AreEqual(initial.FileFormat, discarded.Snapshot.FileFormat);
		Assert.AreEqual(2, discarded.Snapshot.Version);
		Assert.AreEqual(2, discarded.Snapshot.PersistedVersion);
		Assert.IsFalse(discarded.Snapshot.IsDirty);
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.NoChange, noOp.Outcome);
		Assert.AreEqual(discarded.Snapshot.Version, noOp.Snapshot!.Version);
	}

	[TestMethod]
	public async Task Reload_ChangedCleanDocument_InstallsNewContentAndBaseline()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		FileStamp reloadedStamp = new(true, 7, DateTime.UnixEpoch.AddMinutes(1), "reloaded");
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.FromContent(
			"reloaded",
			s_defaultFormat,
			reloadedStamp)));

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version)));

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, result.Outcome);
		Assert.IsNotNull(result.Snapshot);
		Assert.AreEqual("reloaded", result.Snapshot.Content);
		Assert.AreEqual(1, result.Snapshot.Version);
		Assert.AreEqual(1, result.Snapshot.PersistedVersion);
		Assert.IsFalse(result.Snapshot.IsDirty);
		Assert.AreEqual(reloadedStamp, result.Snapshot.OnDiskStamp);
	}

	[TestMethod]
	public async Task Reload_DirtyDocument_ReturnsConflictWithoutReadingAgain()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		FileStamp externalStamp = new(true, 8, DateTime.UnixEpoch.AddMinutes(1), "external");
		fileSystem.CapturedStamps.Enqueue(externalStamp);

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version)));

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.AreEqual(externalStamp, result.ObservedOnDiskStamp);

		// The document keeps its prior stamp: the dirty branch holds no gate, so installing the
		// observed stamp here could replace a newer record written by a concurrent commit.
		Assert.AreEqual(initial.OnDiskStamp, result.Snapshot.OnDiskStamp);
		Assert.AreEqual(1, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task Reload_LogicalEditDuringRead_ReturnsStaleDocument()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;
		Task<WorkspaceDocumentReloadResult> reloadTask = store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version)));
		await fileSystem.Reads.WhenReachedAsync(2);

		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		pendingRead.SetResult(WorkspaceFileReadResult.FromContent("disk", s_defaultFormat, new FileStamp(true, 4, DateTime.UnixEpoch.AddMinutes(1), "disk")));

		WorkspaceDocumentReloadResult result = await reloadTask;

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.StaleDocument, result.Outcome);
		Assert.AreEqual(edited.Snapshot!.Version, result.Snapshot!.Version);
		Assert.AreEqual("logical", result.Snapshot.Content);
	}

	[TestMethod]
	public async Task SnapshotText_ExposesLineMetadataAndFileName()
	{
		var fileSystem = new FakeFileSystem("one\r\ntwo\nthree", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		// Snapshot text defers line-table construction but must expose identical line metadata.
		Assert.AreEqual(3, snapshot.Text.LineCount);
		Assert.AreEqual(1, snapshot.Text.GetLineByNumber(1).LineNumber);
		Assert.AreEqual('t', snapshot.Text.GetCharAt(snapshot.Text.GetLineByNumber(2).Offset));
		Assert.AreEqual(3, snapshot.Text.GetLineByOffset(snapshot.Text.TextLength).LineNumber);
		Assert.AreEqual(TestPath("script.lua"), snapshot.Text.FileName);
		Assert.AreEqual("one\r\ntwo\nthree", snapshot.Content);
	}

	[TestMethod]
	public async Task Replace_NullContent_IsAnArgumentError()
	{
		var fileSystem = new FakeFileSystem("disk", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		Assert.ThrowsExactly<ArgumentNullException>(() => store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			null!,
			initial.FileFormat)));

		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out WorkspaceDocumentSnapshot? retained));
		Assert.AreEqual("disk", retained!.Content);
	}

	[TestMethod]
	public async Task Replace_Windows1252WithByteOrderMark_IsAnArgumentError()
	{
		var fileSystem = new FakeFileSystem("disk", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		// Windows-1252 cannot carry a byte-order mark, so the combination is rejected before the
		// replacement is applied.
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => store.Replace(
			new WorkspaceDocumentReplaceRequest(
				new(initial.DocumentKey, initial.DocumentId, initial.Version),
				"content",
				new TextFileFormat(TextEncodingKind.Windows1252, true, TextNewlineStyle.None))));

		Assert.AreEqual("request.FileFormat", exception.ParamName);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out WorkspaceDocumentSnapshot? retained));
		Assert.AreEqual("disk", retained!.Content);
	}

	[TestMethod]
	public async Task Reload_CleanDocumentWithMissingFileReloadsAsEmptyContent()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.Missing));

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version)));

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, result.Outcome);
		Assert.AreEqual(string.Empty, result.Snapshot!.Content);
		Assert.AreEqual(FileStamp.Missing, result.Snapshot.OnDiskStamp);
		Assert.IsFalse(result.Snapshot.ExistsOnDisk);
		Assert.IsFalse(result.Snapshot.IsDirty);

		// A missing file has no on-disk format to adopt, so the document keeps the format it had.
		Assert.AreEqual(initial.FileFormat, result.Snapshot.FileFormat);
	}

	[TestMethod]
	public async Task Reload_CleanDocumentWhosePathBecameDirectory_ReportsReadFailed()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.Directory));

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version)));

		// A directory is not reloadable content: the reload fails with the directory-specific code
		// instead of adopting the path as a missing file.
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ReadFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.IsDirectory, result.Failure?.Code);
		Assert.AreEqual("original", result.Snapshot!.Content);
	}

	[TestMethod]
	public async Task Replace_UnknownDocument_ReportsDocumentNotFound()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentMutationResult result = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(new WorkspaceDocumentKey(Guid.NewGuid()), TestPath("missing.lua"), 0),
			"content",
			s_defaultFormat));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.DocumentNotFound, result.Outcome);
		Assert.IsNull(result.Snapshot);
	}

	[TestMethod]
	public async Task Replace_WithIdenticalContentAndDifferentFormat_ReportsChangedAndDirty()
	{
		var fileSystem = new FakeFileSystem("baseline", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		TextFileFormat bomFormat = new(TextEncodingKind.Utf8, true, TextNewlineStyle.Lf);

		WorkspaceDocumentMutationResult formatOnly = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.Content,
			bomFormat));

		// The format is part of the logical state: identical content with a different format is a real
		// change, so the document becomes dirty even though the content matches the baseline.
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, formatOnly.Outcome);
		Assert.IsTrue(formatOnly.Snapshot!.IsDirty);
		Assert.AreEqual(bomFormat, formatOnly.Snapshot.FileFormat);

		WorkspaceDocumentMutationResult discarded = store.Discard(new WorkspaceDocumentDiscardRequest(
			new(initial.DocumentKey, initial.DocumentId, formatOnly.Snapshot.Version)));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, discarded.Outcome);
		Assert.AreEqual(initial.FileFormat, discarded.Snapshot!.FileFormat);
		Assert.IsFalse(discarded.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task Reload_CleanDocument_AdoptsTheDetectedOnDiskFormat()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		TextFileFormat diskFormat = new(TextEncodingKind.Utf16LittleEndian, true, TextNewlineStyle.CrLf);
		FileStamp diskStamp = new(true, 9, DateTime.UnixEpoch.AddMinutes(2), "disk");
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.FromContent(
			"disk content",
			diskFormat,
			diskStamp)));

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version)));

		// The adopted format is both the current and the persisted format: a stale format would leave
		// the next commit writing the old encoding and newline style.
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, result.Outcome);
		Assert.AreEqual(diskFormat, result.Snapshot!.FileFormat);
		Assert.IsFalse(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task Reload_DirtyDocumentWithMissingFile_ReportsExternalFileConflict()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version)));

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(FileStamp.Missing, result.ObservedOnDiskStamp);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Reload_DirtyDocument_DoesNotWaitBehindAnInFlightCommit()
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
		WorkspaceDocumentSnapshot dirty = edited.Snapshot!;

		// Hold the document's disk gate with an in-flight commit.
		Task<WorkspaceDocumentCommitResult> commit = store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(dirty.DocumentKey, dirty.DocumentId, dirty.Version),
			dirty.OnDiskStamp));
		await fileSystem.Replacements.WhenReachedAsync(1);

		// A dirty reload resolves from stamps alone, so it does not wait behind the commit that holds
		// the gate: the captured stamp matches the tracked stamp, nothing changed externally, and the
		// reload reports Unchanged without touching the logical content.
		WorkspaceDocumentReloadResult reload = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(dirty.DocumentKey, dirty.DocumentId, dirty.Version)));

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Unchanged, reload.Outcome);
		Assert.AreEqual("logical", reload.Snapshot!.Content);
		Assert.IsTrue(reload.Snapshot.IsDirty);
		Assert.AreEqual(1, fileSystem.ReadCount);

		pendingReplacement.SetResult(new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementOutcome.Replaced,
			new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(1), "committed")));
		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, (await commit).Outcome);
	}

	[TestMethod]
	public async Task Reload_StaleVersion_IsRejectedBeforeTouchingDisk()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));

		// The request still carries the pre-edit version; the stale expectation is rejected by the
		// operation preamble before any stamp capture.
		WorkspaceDocumentReloadResult result = await store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version)));

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.StaleDocument, result.Outcome);
		Assert.AreEqual(edited.Snapshot!.Version, result.Snapshot!.Version);
		Assert.AreEqual(1, fileSystem.ReadCount);
		Assert.AreEqual(0, fileSystem.Captures.Count);
	}

	[TestMethod]
	public async Task Reload_DirtyDocumentDeletedDuringStampCapture_ReportsNotFound()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		var pendingStamp = new TaskCompletionSource<FileStamp>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingCapturedStamp = pendingStamp.Task;
		Task<WorkspaceDocumentReloadResult> reload = store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version)));
		await fileSystem.Captures.WhenReachedAsync(1);

		// The dirty reload holds no gate, so the document can be deleted while the stamp capture is
		// in flight; the captured instance is then no longer tracked.
		fileSystem.PendingCapturedStamp = null;
		WorkspaceDocumentDeleteResult deleted = await store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot.Version),
			initial.OnDiskStamp));
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, deleted.Outcome);

		pendingStamp.SetResult(new FileStamp(true, 9, DateTime.UnixEpoch.AddMinutes(3), "external"));
		WorkspaceDocumentReloadResult result = await reload;

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.DocumentNotFound, result.Outcome);
		Assert.IsNull(result.Snapshot);
	}

	[TestMethod]
	public async Task Reload_DirtyDocumentReplacedDuringStampCapture_ReportsStaleInstance()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		var pendingStamp = new TaskCompletionSource<FileStamp>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingCapturedStamp = pendingStamp.Task;
		Task<WorkspaceDocumentReloadResult> reload = store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version)));
		await fileSystem.Captures.WhenReachedAsync(1);

		fileSystem.PendingCapturedStamp = null;
		WorkspaceDocumentDeleteResult deleted = await store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot.Version),
			initial.OnDiskStamp));
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, deleted.Outcome);
		WorkspaceDocumentOpenResult reopened = await store.OpenAsync(TestPath("script.lua"), s_openOptions);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, reopened.Outcome);
		Assert.AreNotEqual(initial.DocumentKey, reopened.Snapshot!.DocumentKey);

		pendingStamp.SetResult(new FileStamp(true, 9, DateTime.UnixEpoch.AddMinutes(3), "external"));
		WorkspaceDocumentReloadResult result = await reload;

		// The id now maps to a different instance, so the captured one is not authoritative.
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.StaleDocumentInstance, result.Outcome);
		Assert.IsNotNull(result.Snapshot);
		Assert.AreEqual(reopened.Snapshot.DocumentKey, result.Snapshot.DocumentKey);
	}

	[TestMethod]
	public async Task Reload_CleanDocumentWithMatchingStamp_ReportsUnchanged()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.FromContent(
			"original",
			s_defaultFormat,
			initial.OnDiskStamp)));

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version)));

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Unchanged, result.Outcome);
		Assert.AreEqual(initial.Content, result.Snapshot!.Content);
		Assert.AreEqual(initial.Version, result.Snapshot.Version);
	}

	[TestMethod]
	public async Task Reload_CleanDocumentWithCallerStampNewerThanTracked_AdoptsTheDiskState()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		var newerStamp = new FileStamp(true, 11, DateTime.UnixEpoch.AddMinutes(5), "external");
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.FromContent("external", s_defaultFormat, newerStamp)));

		// The caller passes the stamp it observed, which is newer than the stamp the store tracked. The
		// unchanged outcome is defined by the stored observation, so the reload adopts the disk state
		// instead of reporting Unchanged against an expectation the store never confirmed.
		WorkspaceDocumentReloadResult result = await store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version)));

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, result.Outcome);
		Assert.AreEqual("external", result.Snapshot!.Content);
		Assert.AreEqual(newerStamp, result.Snapshot.OnDiskStamp);
		Assert.AreEqual(initial.Version + 1, result.Snapshot.Version);
		Assert.IsFalse(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task Reload_DirtyDocumentStampCaptureFailure_ReportsReadFailed()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		fileSystem.CaptureTasks.Enqueue(Task.FromException<FileStamp>(new IOException("The stamp capture failed.")));

		// A capture that faults maps to ReadFailed; the dirty content and the baseline stay untouched.
		WorkspaceDocumentReloadResult result = await store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version)));

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ReadFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ReadFailed, result.Failure?.Code);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task Reload_DocumentWithDiskGateHeldBySaveAs_ReportsOperationInProgress()
	{
		var fileSystem = new FakeFileSystem("disk", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		Task<WorkspaceDocumentSaveAsResult> saveAs = store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			TestPath("copy.lua")));
		await fileSystem.Replacements.WhenReachedAsync(1);

		// Save As holds the document's disk gate for the whole operation, so a reload of the clean
		// document reports the contention instead of reading concurrently.
		WorkspaceDocumentReloadResult reload = await store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version)));

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.OperationInProgress, reload.Outcome);

		pendingReplacement.SetResult(new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementOutcome.Replaced,
			new FileStamp(true, 8, DateTime.UnixEpoch.AddMinutes(2), "saved")));
		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, (await saveAs).Outcome);
	}

	[TestMethod]
	public async Task Reload_DirtyDocumentDuringInFlightDelete_ReportsOperationInProgress()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
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

		// The delete holds the gate and marks the document as rejecting replacements; the dirty reload
		// skips that gate, so it is rejected before it captures a stamp or compares one instead of
		// reporting Unchanged or an external conflict for a document the delete is about to remove.
		WorkspaceDocumentReloadResult reload = await store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot.Version)));

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.OperationInProgress, reload.Outcome);
		Assert.AreEqual("logical", reload.Snapshot!.Content);
		Assert.IsTrue(reload.Snapshot.IsDirty);
		Assert.AreEqual(0, fileSystem.Captures.Count);

		pendingDelete.SetResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted));
		WorkspaceDocumentDeleteResult deleted = await delete;

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, deleted.Outcome);
		Assert.IsFalse(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task Reload_DirtyDocumentDuringStampCapture_RejectsADeleteStartedMidFlight()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		var pendingStamp = new TaskCompletionSource<FileStamp>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingCapturedStamp = pendingStamp.Task;
		Task<WorkspaceDocumentReloadResult> reload = store.ReloadAsync(new WorkspaceDocumentReloadRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version)));
		await fileSystem.Captures.WhenReachedAsync(1);

		// The delete starts while the reload waits on its stamp capture without holding the gate; the
		// preamble guard did not see it, so only the post-capture check can reject it.
		fileSystem.PendingCapturedStamp = null;
		fileSystem.PendingDelete = pendingDelete.Task;
		Task<WorkspaceDocumentDeleteResult> delete = store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot.Version),
			initial.OnDiskStamp));
		await fileSystem.Deletes.WhenReachedAsync(1);

		// The capture resolves with a stamp that differs from the tracked one; without the delete check
		// the reload would report an external conflict for a document the delete is about to remove.
		pendingStamp.SetResult(new FileStamp(true, 9, DateTime.UnixEpoch.AddMinutes(3), "external"));
		WorkspaceDocumentReloadResult result = await reload;

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.OperationInProgress, result.Outcome);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.IsTrue(result.Snapshot.IsDirty);

		pendingDelete.SetResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted));
		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, (await delete).Outcome);
	}

	[TestMethod]
	public async Task Discard_UnknownStaleInstanceAndStaleVersionRequests_ReportTheirOutcomes()
	{
		var fileSystem = new FakeFileSystem("disk", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentMutationResult notFound = store.Discard(new WorkspaceDocumentDiscardRequest(
			new(new WorkspaceDocumentKey(Guid.NewGuid()), TestPath("other.lua"), initial.Version)));
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.DocumentNotFound, notFound.Outcome);

		WorkspaceDocumentMutationResult instanceStale = store.Discard(new WorkspaceDocumentDiscardRequest(
			new(new WorkspaceDocumentKey(Guid.NewGuid()), initial.DocumentId, initial.Version)));
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.StaleDocumentInstance, instanceStale.Outcome);

		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"edited",
			initial.FileFormat));
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, edited.Outcome);

		WorkspaceDocumentMutationResult stale = store.Discard(new WorkspaceDocumentDiscardRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version)));

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.StaleDocument, stale.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out WorkspaceDocumentSnapshot? retained));
		Assert.IsTrue(retained!.IsDirty);
	}
}
