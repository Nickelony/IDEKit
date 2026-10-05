using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task Replacement_RetainsLogicalContentWhenOpenedAgain()
	{
		var fileSystem = new FakeFileSystem("disk", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentOpenResult opened = await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken);
		WorkspaceDocumentSnapshot initial = opened.Snapshot!;

		WorkspaceDocumentMutationResult noOp = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), initial.Content, initial.FileFormat)
		);

		WorkspaceDocumentMutationResult replaced = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "logical", initial.FileFormat)
		);

		WorkspaceDocumentOpenResult reopened = await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken);

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

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentMutationResult edited = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "edited", initial.FileFormat)
		);

		WorkspaceDocumentMutationResult undone = store.Replace(
			new WorkspaceDocumentReplaceRequest(
				new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
				initial.Content,
				initial.FileFormat
			)
		);

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

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentMutationResult changed = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "changed", initial.FileFormat)
		);

		WorkspaceDocumentMutationResult staleVersion = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "stale", initial.FileFormat)
		);

		WorkspaceDocumentMutationResult staleKey = store.Replace(
			new WorkspaceDocumentReplaceRequest(
				new(new WorkspaceDocumentKey(Guid.NewGuid()), initial.DocumentId, changed.Snapshot!.Version),
				"stale key",
				initial.FileFormat
			)
		);

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

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;
		TextFileFormat changedFormat = new(TextEncoding.Utf16LittleEndian, true, LineTerminatorStyle.CrLf);

		WorkspaceDocumentMutationResult changed = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "changed", changedFormat)
		);

		WorkspaceDocumentMutationResult discarded = store.Discard(
			new WorkspaceDocumentDiscardRequest(new(initial.DocumentKey, initial.DocumentId, changed.Snapshot!.Version))
		);

		WorkspaceDocumentMutationResult noOp = store.Discard(
			new WorkspaceDocumentDiscardRequest(new(initial.DocumentKey, initial.DocumentId, discarded.Snapshot!.Version))
		);

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

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;
		FileStamp reloadedStamp = new(Exists: true, Length: 7, LastWriteTimeUtc: DateTime.UnixEpoch.AddMinutes(1), ContentHash: "reloaded");

		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.FromContent("reloaded", s_defaultFormat, reloadedStamp)));

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version)),
			TestContext.CancellationToken
		);

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

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentMutationResult edited = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "logical", initial.FileFormat)
		);

		FileStamp externalStamp = new(Exists: true, Length: 8, LastWriteTimeUtc: DateTime.UnixEpoch.AddMinutes(1), ContentHash: "external");

		fileSystem.CapturedStamps.Enqueue(externalStamp);

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version)),
			TestContext.CancellationToken
		);

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

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;

		Task<WorkspaceDocumentReloadResult> reloadTask = store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version)),
			TestContext.CancellationToken
		);

		await fileSystem.Reads.WhenReachedAsync(2);

		WorkspaceDocumentMutationResult edited = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "logical", initial.FileFormat)
		);

		pendingRead.SetResult(
			WorkspaceFileReadResult.FromContent(
				"disk",
				s_defaultFormat,
				new FileStamp(Exists: true, Length: 4, LastWriteTimeUtc: DateTime.UnixEpoch.AddMinutes(1), ContentHash: "disk")
			)
		);

		WorkspaceDocumentReloadResult result = await reloadTask;

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.StaleDocument, result.Outcome);
		Assert.AreEqual(edited.Snapshot!.Version, result.Snapshot!.Version);
		Assert.AreEqual("logical", result.Snapshot.Content);
	}

	[TestMethod]
	public async Task SnapshotText_ExposesLineMetadataAndDocumentId()
	{
		var fileSystem = new FakeFileSystem("one\r\ntwo\nthree", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		// Snapshot text defers line-table construction but must expose identical line metadata.
		Assert.AreEqual(3, snapshot.Text.LineCount);
		Assert.AreEqual(1, snapshot.Text.GetLineByNumber(1).LineNumber);
		Assert.AreEqual('t', snapshot.Text.GetCharAt(snapshot.Text.GetLineByNumber(2).Offset));
		Assert.AreEqual(3, snapshot.Text.GetLineByOffset(snapshot.Text.TextLength).LineNumber);
		Assert.AreEqual(snapshot.DocumentId, snapshot.Text.DocumentId);
		Assert.AreEqual("one\r\ntwo\nthree", snapshot.Content);
	}

	[TestMethod]
	public async Task SnapshotText_CarriesTheNormalizedDocumentIdInsteadOfTheDisplayPath()
	{
		var fileSystem = new FakeFileSystem("text", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		string spelled = TestPath("sub") + Path.DirectorySeparatorChar + ".." + Path.DirectorySeparatorChar + "script.lua";

		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(spelled, s_openOptions, TestContext.CancellationToken)).Snapshot!;

		Assert.AreEqual(spelled, snapshot.DisplayPath);
		Assert.AreNotEqual(snapshot.DisplayPath, snapshot.DocumentId);
		Assert.AreEqual(snapshot.DocumentId, snapshot.Text.DocumentId);
	}

	[TestMethod]
	public async Task Replace_NullContent_IsAnArgumentError()
	{
		var fileSystem = new FakeFileSystem("disk", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			store.Replace(
				new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), null!, initial.FileFormat)
			)
		);

		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out WorkspaceDocumentSnapshot? retained));
		Assert.AreEqual("disk", retained!.Content);
	}

	[TestMethod]
	public async Task Replace_Windows1252WithByteOrderMark_IsAnArgumentError()
	{
		var fileSystem = new FakeFileSystem("disk", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		// Windows-1252 cannot carry a byte-order mark, so the combination is rejected before the
		// replacement is applied.
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
			store.Replace(
				new WorkspaceDocumentReplaceRequest(
					new(initial.DocumentKey, initial.DocumentId, initial.Version),
					"content",
					new TextFileFormat(TextEncoding.Windows1252, true, LineTerminatorStyle.None)
				)
			)
		);

		Assert.AreEqual("request.FileFormat", exception.ParamName);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out WorkspaceDocumentSnapshot? retained));
		Assert.AreEqual("disk", retained!.Content);
	}

	[TestMethod]
	public async Task Reload_CleanDocumentWithMissingFileReloadsAsEmptyContent()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.Missing));

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version)),
			TestContext.CancellationToken
		);

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

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.Directory));

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version)),
			TestContext.CancellationToken
		);

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

		WorkspaceDocumentMutationResult result = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(new WorkspaceDocumentKey(Guid.NewGuid()), TestPath("missing.lua"), 0), "content", s_defaultFormat)
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.DocumentNotFound, result.Outcome);
		Assert.IsNull(result.Snapshot);
	}

	[TestMethod]
	public async Task Replace_WithIdenticalContentAndDifferentFormat_ReportsChangedAndDirty()
	{
		var fileSystem = new FakeFileSystem("baseline", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;
		TextFileFormat bomFormat = new(TextEncoding.Utf8, true, LineTerminatorStyle.Lf);

		WorkspaceDocumentMutationResult formatOnly = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), initial.Content, bomFormat)
		);

		// The format is part of the logical state: identical content with a different format is a real
		// change, so the document becomes dirty even though the content matches the baseline.
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, formatOnly.Outcome);
		Assert.IsTrue(formatOnly.Snapshot!.IsDirty);
		Assert.AreEqual(bomFormat, formatOnly.Snapshot.FileFormat);

		WorkspaceDocumentMutationResult discarded = store.Discard(
			new WorkspaceDocumentDiscardRequest(new(initial.DocumentKey, initial.DocumentId, formatOnly.Snapshot.Version))
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, discarded.Outcome);
		Assert.AreEqual(initial.FileFormat, discarded.Snapshot!.FileFormat);
		Assert.IsFalse(discarded.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task Reload_CleanDocument_AdoptsTheDetectedOnDiskFormat()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;
		TextFileFormat diskFormat = new(TextEncoding.Utf16LittleEndian, true, LineTerminatorStyle.CrLf);
		FileStamp diskStamp = new(Exists: true, Length: 9, LastWriteTimeUtc: DateTime.UnixEpoch.AddMinutes(2), ContentHash: "disk");

		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.FromContent("disk content", diskFormat, diskStamp)));

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version)),
			TestContext.CancellationToken
		);

		// The adopted format is both the current and the persisted format: a stale format would leave
		// the next commit writing the old encoding and line terminator style.
		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, result.Outcome);
		Assert.AreEqual(diskFormat, result.Snapshot!.FileFormat);
		Assert.IsFalse(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task Reload_DirtyDocumentWithMissingFile_ReportsExternalFileConflict()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentMutationResult edited = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "logical", initial.FileFormat)
		);

		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version)),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(FileStamp.Missing, result.ObservedOnDiskStamp);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Reload_DirtyDocumentWhileACommitIsInFlight_ReportsOperationInProgressWithoutCapturingAStamp()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		fileSystem.PendingReplacement = pendingReplacement.Task;

		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentMutationResult edited = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "logical", initial.FileFormat)
		);

		WorkspaceDocumentSnapshot dirty = edited.Snapshot!;

		// Hold the document's disk gate with an in-flight commit.
		Task<WorkspaceDocumentCommitResult> commit = store.CommitAsync(
			new WorkspaceDocumentCommitRequest(new(dirty.DocumentKey, dirty.DocumentId, dirty.Version), dirty.OnDiskStamp),
			TestContext.CancellationToken
		);

		await fileSystem.Replacements.WhenReachedAsync(1);

		// The commit holds the document's disk gate, so the reload does not capture a stamp of a file that
		// the commit is about to replace: it reports the contention and leaves the logical content alone.
		WorkspaceDocumentReloadResult reload = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(dirty.DocumentKey, dirty.DocumentId, dirty.Version)),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.OperationInProgress, reload.Outcome);
		Assert.AreEqual("logical", reload.Snapshot!.Content);
		Assert.IsTrue(reload.Snapshot.IsDirty);
		Assert.AreEqual(1, fileSystem.ReadCount);
		Assert.AreEqual(0, fileSystem.Captures.Count);

		pendingReplacement.SetResult(
			new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementOutcome.Replaced,
				new FileStamp(Exists: true, Length: 7, LastWriteTimeUtc: DateTime.UnixEpoch.AddMinutes(1), ContentHash: "committed")
			)
		);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, (await commit).Outcome);
	}

	[TestMethod]
	public async Task Reload_StaleVersion_IsRejectedBeforeTouchingDisk()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentMutationResult edited = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "logical", initial.FileFormat)
		);

		// The request still carries the pre-edit version; the stale expectation is rejected by the
		// operation preamble before any stamp capture.
		WorkspaceDocumentReloadResult result = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version)),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.StaleDocument, result.Outcome);
		Assert.AreEqual(edited.Snapshot!.Version, result.Snapshot!.Version);
		Assert.AreEqual(1, fileSystem.ReadCount);
		Assert.AreEqual(0, fileSystem.Captures.Count);
	}

	[TestMethod]
	public async Task Reload_CleanDocumentWithMatchingStamp_ReportsUnchanged()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.FromContent("original", s_defaultFormat, initial.OnDiskStamp)));

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version)),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Unchanged, result.Outcome);
		Assert.AreEqual(initial.Content, result.Snapshot!.Content);
		Assert.AreEqual(initial.Version, result.Snapshot.Version);
	}

	[TestMethod]
	public async Task Reload_CleanDocumentWithCallerStampNewerThanTracked_AdoptsTheDiskState()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;
		var newerStamp = new FileStamp(Exists: true, Length: 11, LastWriteTimeUtc: DateTime.UnixEpoch.AddMinutes(5), ContentHash: "external");
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.FromContent("external", s_defaultFormat, newerStamp)));

		// The disk read returns a stamp newer than the stamp the store tracked. The
		// unchanged outcome is defined by the stored observation, so the reload adopts the disk state
		// instead of reporting Unchanged against an expectation the store never confirmed.
		WorkspaceDocumentReloadResult result = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version)),
			TestContext.CancellationToken
		);

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

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentMutationResult edited = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "logical", initial.FileFormat)
		);

		fileSystem.CaptureTasks.Enqueue(Task.FromException<FileStamp>(new IOException("The stamp capture failed.")));

		// A capture that faults maps to ReadFailed; the dirty content and the baseline stay untouched.
		WorkspaceDocumentReloadResult result = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version)),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ReadFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ReadFailed, result.Failure?.Code);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task Reload_DirtyDocumentStampCapturePermissionFailure_ReportsAccessDenied()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentMutationResult edited = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "logical", initial.FileFormat)
		);

		fileSystem.CaptureTasks.Enqueue(Task.FromException<FileStamp>(new UnauthorizedAccessException("The stamp capture was denied.")));

		// The dirty branch classifies a faulted capture like the clean reads: a permission failure is a property of the path,
		// so a host can tell it from a transient failure.
		WorkspaceDocumentReloadResult result = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version)),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ReadFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.AccessDenied, result.Failure?.Code);
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

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);

		Task<WorkspaceDocumentSaveAsResult> saveAs = store.SaveAsAsync(
			new WorkspaceDocumentSaveAsRequest(
				new(initial.DocumentKey, initial.DocumentId, initial.Version),
				initial.OnDiskStamp,
				TestPath("copy.lua")
			),

			TestContext.CancellationToken
		);

		await fileSystem.Replacements.WhenReachedAsync(1);

		// Save As holds the document's disk gate for the whole operation, so a reload of the clean
		// document reports the contention instead of reading concurrently.
		WorkspaceDocumentReloadResult reload = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version)),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.OperationInProgress, reload.Outcome);

		pendingReplacement.SetResult(
			new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementOutcome.Replaced,
				new FileStamp(Exists: true, Length: 8, LastWriteTimeUtc: DateTime.UnixEpoch.AddMinutes(2), ContentHash: "saved")
			)
		);

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, (await saveAs).Outcome);
	}

	[TestMethod]
	public async Task Reload_DirtyDocumentDuringInFlightDelete_ReportsOperationInProgress()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);

		fileSystem.PendingDelete = pendingDelete.Task;

		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentMutationResult edited = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "logical", initial.FileFormat)
		);

		Task<WorkspaceDocumentDeleteResult> delete = store.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version), initial.OnDiskStamp),
			TestContext.CancellationToken
		);

		await fileSystem.Deletes.WhenReachedAsync(1);

		// The delete holds the gate and reserves the document, which rejects replacements; the dirty
		// reload takes the same gate, so it is rejected before it captures a stamp or compares one instead
		// of reporting Unchanged or an external conflict for a document the delete is about to remove.
		WorkspaceDocumentReloadResult reload = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, edited.Snapshot.Version)),
			TestContext.CancellationToken
		);

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
	public async Task Delete_DuringADirtyReloadsStampCapture_ReportsOperationInProgressAndKeepsTheDocument()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentMutationResult edited = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "logical", initial.FileFormat)
		);

		var pendingStamp = new TaskCompletionSource<FileStamp>(TaskCreationOptions.RunContinuationsAsynchronously);

		fileSystem.PendingCapturedStampObservingCancellation = pendingStamp.Task;

		Task<WorkspaceDocumentReloadResult> reload = store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version)),
			TestContext.CancellationToken
		);

		await fileSystem.Captures.WhenReachedAsync(1);

		// The reload holds the document's disk gate while it waits on its stamp capture, so the delete is
		// refused instead of removing the document under the comparison.

		WorkspaceDocumentDeleteResult delete = await store.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(initial.DocumentKey, initial.DocumentId, edited.Snapshot.Version), initial.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.OperationInProgress, delete.Outcome);
		Assert.AreEqual(0, fileSystem.Deletes.Count);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		pendingStamp.SetResult(new FileStamp(Exists: true, Length: 9, LastWriteTimeUtc: DateTime.UnixEpoch.AddMinutes(3), ContentHash: "external"));

		WorkspaceDocumentReloadResult result = await reload;

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual("logical", result.Snapshot!.Content);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task Commit_DuringADirtyReloadsStampCapture_ReportsOperationInProgressAndWritesNothing()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentSnapshot dirty = store
			.Replace(
				new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "logical", initial.FileFormat)
			)
			.Snapshot!;

		var pendingStamp = new TaskCompletionSource<FileStamp>(TaskCreationOptions.RunContinuationsAsynchronously);

		fileSystem.PendingCapturedStampObservingCancellation = pendingStamp.Task;

		Task<WorkspaceDocumentReloadResult> reload = store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(dirty.DocumentKey, dirty.DocumentId, dirty.Version)),
			TestContext.CancellationToken
		);

		await fileSystem.Captures.WhenReachedAsync(1);

		// A commit would replace the file the reload is reading, so the reload's gate refuses it.
		WorkspaceDocumentCommitResult commit = await store.CommitAsync(
			new WorkspaceDocumentCommitRequest(new(dirty.DocumentKey, dirty.DocumentId, dirty.Version), dirty.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.OperationInProgress, commit.Outcome);
		Assert.AreEqual(0, fileSystem.ReplaceCount);

		pendingStamp.SetResult(dirty.OnDiskStamp);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Unchanged, (await reload).Outcome);
	}

	[TestMethod]
	public async Task Reload_DirtyDocumentThatComparesItsStamp_ReleasesTheDiskGate()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot dirty = await OpenDirtyAsync(store);

		WorkspaceDocumentReloadResult reload = await store.ReloadAsync(ReloadRequestFor(dirty), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Unchanged, reload.Outcome);
		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, (await CommitAsync(store, dirty)).Outcome);
	}

	[TestMethod]
	public async Task Reload_DirtyDocumentWhoseCaptureFails_ReleasesTheDiskGate()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot dirty = await OpenDirtyAsync(store);

		fileSystem.CaptureTasks.Enqueue(Task.FromException<FileStamp>(new IOException("The stamp capture failed.")));

		WorkspaceDocumentReloadResult reload = await store.ReloadAsync(ReloadRequestFor(dirty), TestContext.CancellationToken);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ReadFailed, reload.Outcome);
		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, (await CommitAsync(store, dirty)).Outcome);
	}

	[TestMethod]
	[Timeout(15000, CooperativeCancellation = true)]
	public async Task Reload_DirtyDocumentCanceledDuringTheCapture_ReleasesTheDiskGate()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot dirty = await OpenDirtyAsync(store);

		var neverCompletes = new TaskCompletionSource<FileStamp>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingCapturedStampObservingCancellation = neverCompletes.Task;

		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
		Task<WorkspaceDocumentReloadResult> reload = store.ReloadAsync(ReloadRequestFor(dirty), cancellation.Token);

		await fileSystem.Captures.WhenReachedAsync(1);
		await cancellation.CancelAsync();

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Canceled, (await reload).Outcome);

		fileSystem.PendingCapturedStampObservingCancellation = null;

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, (await CommitAsync(store, dirty)).Outcome);
	}

	[TestMethod]
	public async Task Reload_DirtyDocumentEditedDuringTheCapture_ReportsStaleDocumentAndReleasesTheDiskGate()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot dirty = await OpenDirtyAsync(store);

		var pendingStamp = new TaskCompletionSource<FileStamp>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingCapturedStampObservingCancellation = pendingStamp.Task;

		Task<WorkspaceDocumentReloadResult> reload = store.ReloadAsync(ReloadRequestFor(dirty), TestContext.CancellationToken);

		await fileSystem.Captures.WhenReachedAsync(1);

		// An edit takes no disk gate, so it can land while the stamp is read; the comparison then describes a version the document left.
		WorkspaceDocumentSnapshot edited = store
			.Replace(new WorkspaceDocumentReplaceRequest(new(dirty.DocumentKey, dirty.DocumentId, dirty.Version), "edited again", dirty.FileFormat))
			.Snapshot!;

		pendingStamp.SetResult(new FileStamp(Exists: true, Length: 9, LastWriteTimeUtc: DateTime.UnixEpoch.AddMinutes(3), ContentHash: "external"));

		WorkspaceDocumentReloadResult result = await reload;

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.StaleDocument, result.Outcome);
		Assert.AreEqual(edited.Version, result.Snapshot!.Version);
		Assert.AreEqual("edited again", result.Snapshot.Content);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, (await CommitAsync(store, edited)).Outcome);
	}

	private async Task<WorkspaceDocumentSnapshot> OpenDirtyAsync(WorkspaceDocumentStore store)
	{
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		return store
			.Replace(
				new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "logical", initial.FileFormat)
			)
			.Snapshot!;
	}

	private static WorkspaceDocumentReloadRequest ReloadRequestFor(WorkspaceDocumentSnapshot snapshot) =>
		new(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version));

	private Task<WorkspaceDocumentCommitResult> CommitAsync(WorkspaceDocumentStore store, WorkspaceDocumentSnapshot snapshot)
	{
		return store.CommitAsync(
			new WorkspaceDocumentCommitRequest(new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.OnDiskStamp),
			TestContext.CancellationToken
		);
	}

	[TestMethod]
	public async Task Discard_UnknownStaleInstanceAndStaleVersionRequests_ReportTheirOutcomes()
	{
		var fileSystem = new FakeFileSystem("disk", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentMutationResult notFound = store.Discard(
			new WorkspaceDocumentDiscardRequest(new(new WorkspaceDocumentKey(Guid.NewGuid()), TestPath("other.lua"), initial.Version))
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.DocumentNotFound, notFound.Outcome);

		WorkspaceDocumentMutationResult instanceStale = store.Discard(
			new WorkspaceDocumentDiscardRequest(new(new WorkspaceDocumentKey(Guid.NewGuid()), initial.DocumentId, initial.Version))
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.StaleDocumentInstance, instanceStale.Outcome);

		WorkspaceDocumentMutationResult edited = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "edited", initial.FileFormat)
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, edited.Outcome);

		WorkspaceDocumentMutationResult stale = store.Discard(
			new WorkspaceDocumentDiscardRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version))
		);

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.StaleDocument, stale.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out WorkspaceDocumentSnapshot? retained));
		Assert.IsTrue(retained!.IsDirty);
	}
}
