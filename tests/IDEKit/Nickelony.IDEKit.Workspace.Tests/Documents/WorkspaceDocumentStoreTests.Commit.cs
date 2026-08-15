using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;
using System.Text;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task Commit_DocumentNotFoundAndStaleInstance_ReportTheirOutcomes()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentCommitResult missing = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(initial.DocumentKey, TestPath("missing.lua"), 0),
			initial.OnDiskStamp));
		WorkspaceDocumentCommitResult staleInstance = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(new WorkspaceDocumentKey(Guid.NewGuid()), initial.DocumentId, initial.Version),
			initial.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.DocumentNotFound, missing.Outcome);
		Assert.IsNull(missing.Snapshot);
		Assert.AreEqual(WorkspaceDocumentCommitOutcome.StaleDocumentInstance, staleInstance.Outcome);
		Assert.IsNotNull(staleInstance.Snapshot);
		Assert.AreEqual(initial.Version, staleInstance.Snapshot.Version);
		Assert.IsFalse(staleInstance.Snapshot.IsDirty);
	}

	[TestMethod]
	[DataRow(WorkspaceFileReplacementOutcome.Canceled, WorkspaceDocumentCommitOutcome.Canceled, DisplayName = "Canceled")]
	[DataRow(WorkspaceFileReplacementOutcome.DestinationExists, WorkspaceDocumentCommitOutcome.WriteFailed, DisplayName = "DestinationExists")]
	public async Task Commit_ReplacementOutcomes_MapToCommitOutcomes(
		WorkspaceFileReplacementOutcome replacementOutcome,
		WorkspaceDocumentCommitOutcome expectedOutcome)
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat)
		{
			ReplacementOutcome = replacementOutcome
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));

		WorkspaceDocumentCommitResult result = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
			initial.OnDiskStamp));

		Assert.AreEqual(expectedOutcome, result.Outcome);

		// No failure outcome advances the document version or the persisted baseline.
		Assert.AreEqual(edited.Snapshot.Version, result.Snapshot!.Version);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task Commit_EditDuringWrite_KeepsLaterLogicalEditDirtyWithoutRetry()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"first",
			initial.FileFormat));
		Task<WorkspaceDocumentCommitResult> commitTask = store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
			initial.OnDiskStamp));
		await fileSystem.Replacements.WhenReachedAsync(1);

		WorkspaceDocumentMutationResult laterEdit = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot.Version),
			"second",
			initial.FileFormat));
		pendingReplacement.SetResult(new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementOutcome.Replaced,
			new FileStamp(true, 5, DateTime.UnixEpoch.AddMinutes(1), "committed")));

		WorkspaceDocumentCommitResult result = await commitTask;

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, result.Outcome);
		Assert.AreEqual("second", result.Snapshot!.Content);
		Assert.AreEqual(laterEdit.Snapshot!.Version, result.Snapshot.Version);
		Assert.AreEqual(edited.Snapshot.Version, result.Snapshot.PersistedVersion);
		Assert.IsTrue(result.Snapshot.IsDirty);
		Assert.AreEqual(1, fileSystem.ReplaceCount);
	}

	[TestMethod]
	public async Task Commit_ReplacementStateUnknown_CanBeRecoveredByNextExplicitSave()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat)
		{
			ReplacementOutcome = WorkspaceFileReplacementOutcome.ReplacementStateUnknown,
			ReplacementStamp = new FileStamp(true, 6, DateTime.UnixEpoch.AddMinutes(1), "unknown")
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"logical",
			initial.FileFormat));
		WorkspaceDocumentCommitResult unknown = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
			initial.OnDiskStamp));

		fileSystem.ReplacementOutcome = WorkspaceFileReplacementOutcome.Replaced;
		fileSystem.CapturedStamps.Enqueue(unknown.Snapshot!.OnDiskStamp);
		WorkspaceDocumentCommitResult recovered = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(unknown.Snapshot.DocumentKey, unknown.Snapshot.DocumentId, unknown.Snapshot.Version),
			unknown.Snapshot.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.ReplacementStateUnknown, unknown.Outcome);
		Assert.AreEqual(fileSystem.ReplacementStamp, unknown.ObservedOnDiskStamp);
		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, recovered.Outcome);
		Assert.IsFalse(recovered.Snapshot!.IsDirty);
		Assert.AreEqual(2, fileSystem.ReplaceCount);
	}

	[TestMethod]
	public async Task Commit_Cancellation_LeavesLogicalDocumentDirty()
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
		Task<WorkspaceDocumentCommitResult> commitTask = store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version),
			initial.OnDiskStamp), cancellation.Token);
		await fileSystem.Replacements.WhenReachedAsync(1);
		cancellation.Cancel();

		WorkspaceDocumentCommitResult result = await commitTask;

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Canceled, result.Outcome);
		Assert.IsTrue(result.Snapshot!.IsDirty);
	}

	[TestMethod]
	public async Task Commit_PreCanceledCleanDocument_ReportsCanceledWithoutWriting()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		// The token is observed before the clean no-op short-circuit, so a canceled commit reports
		// Canceled and performs no write even though the document has nothing to persist.
		WorkspaceDocumentCommitResult result = await store.CommitAsync(
			new WorkspaceDocumentCommitRequest(
				new(initial.DocumentKey, initial.DocumentId, initial.Version),
				initial.OnDiskStamp),
			cancellation.Token);

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Canceled, result.Outcome);
		Assert.AreEqual(0, fileSystem.ReplaceCount);
	}

	[TestMethod]
	public async Task Commit_ReplacementWithoutObservedStamp_RecapturesTheStamp()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot dirty = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat)).Snapshot!;

		// The replacement reports success without the resulting stamp, so the store re-captures it
		// exactly once; the commit itself no longer pre-captures the destination, which the
		// conditional replacement validates instead.
		FileStamp writtenStamp = new(true, 11, DateTime.UnixEpoch.AddMinutes(5), "written");
		fileSystem.OmitReplacementStamp = true;
		fileSystem.CapturedStamps.Enqueue(writtenStamp);

		WorkspaceDocumentCommitResult result = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(dirty.DocumentKey, dirty.DocumentId, dirty.Version),
			dirty.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, result.Outcome);
		Assert.AreEqual(writtenStamp, result.Snapshot!.OnDiskStamp);
		Assert.IsFalse(result.Snapshot.IsDirty);
		Assert.AreEqual(1, fileSystem.Captures.Count);
	}

	[TestMethod]
	public async Task Commit_ReplacementWithoutObservedStampAndFailedRecapture_ReportsStateUnknown()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot dirty = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat)).Snapshot!;

		// The replacement completed without reporting its stamp and the re-capture fails, so the final
		// state is indeterminate; the resulting failure carries the capture exception for post-mortems.
		var captureFailure = new IOException("capture failed");
		fileSystem.OmitReplacementStamp = true;
		fileSystem.CaptureTasks.Enqueue(Task.FromException<FileStamp>(captureFailure));

		WorkspaceDocumentCommitResult result = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(dirty.DocumentKey, dirty.DocumentId, dirty.Version),
			dirty.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.ReplacementStateUnknown, result.Outcome);
		Assert.IsNotNull(result.Failure);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ReplacementStateUnknown, result.Failure.Code);
		Assert.AreSame(captureFailure, result.Failure.Exception);
		StringAssert.Contains(result.Failure.Message, "capture failed");
		Assert.IsTrue(result.Snapshot!.IsDirty);
	}

	[TestMethod]
	public async Task Commit_UnencodableContent_ReportsInvalidEncoding()
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

		WorkspaceDocumentCommitResult result = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp));

		// The encoding failure is a property of the content and format, so it is classified as an
		// invalid-encoding write failure instead of a generic write problem.
		Assert.AreEqual(WorkspaceDocumentCommitOutcome.WriteFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.InvalidEncoding, result.Failure!.Code);
		Assert.AreEqual(0, fileSystem.ReplaceCount);
		Assert.IsTrue(result.Snapshot!.IsDirty);
	}

	[TestMethod]
	public async Task Commit_RootDocument_WritesInTheRootDirectory()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		string rootPath = Path.GetPathRoot(Path.GetFullPath(Environment.CurrentDirectory))!;
		WorkspaceDocumentSnapshot opened = (await store.OpenAsync(rootPath, s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(opened.DocumentKey, opened.DocumentId, opened.Version),
			"edited",
			opened.FileFormat));

		WorkspaceDocumentCommitResult committed = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp));

		// A file id that is itself a volume root has no parent directory; the root is the same-volume
		// directory, never the process current directory.
		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, committed.Outcome);
		Assert.AreEqual(rootPath, fileSystem.LastWriteDestination);
	}

	[TestMethod]
	public async Task CleanExistingCommit_IsNoOpWithoutCapturingOrWriting()
	{
		var fileSystem = new ControllableFileSystem("disk", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentCommitResult result = await store.CommitAsync(CreateCommitRequest(snapshot));

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, result.Outcome);

		// A clean existing document has nothing to write, so the whole-file stamp capture is skipped
		// as well; only a dirty or force-write commit reads and hashes the file.
		Assert.AreEqual(0, fileSystem.CaptureCount);
		Assert.AreEqual(0, fileSystem.ReplaceCount);
		Assert.AreEqual(snapshot.Version, result.Snapshot!.PersistedVersion);
	}

	[TestMethod]
	public async Task CleanExistingCommit_WithMismatchedExpectedStamp_ReportsExternalFileConflict()
	{
		var fileSystem = new ControllableFileSystem("disk", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		var staleExpectedStamp = new FileStamp(true, 4, DateTime.UnixEpoch.AddDays(-1), "stale");

		// The no-op path cannot verify the file without reading it, so the caller's expectation is
		// validated against the tracked stamp; an expectation that disagrees with the state the store
		// last observed reports a conflict instead of claiming a match.
		WorkspaceDocumentCommitResult result = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			staleExpectedStamp));

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(snapshot.OnDiskStamp, result.ObservedOnDiskStamp);
		Assert.AreEqual(0, fileSystem.CaptureCount);
		Assert.AreEqual(0, fileSystem.ReplaceCount);
	}

	[TestMethod]
	public async Task StaleCommit_IsRejectedWithoutFilesystemWork()
	{
		var fileSystem = new ControllableFileSystem("disk", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult changed = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat));

		WorkspaceDocumentCommitResult result = await store.CommitAsync(CreateCommitRequest(initial));

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.StaleDocument, result.Outcome);
		Assert.AreEqual(changed.Snapshot!.Content, result.Snapshot!.Content);
		Assert.AreEqual(0, fileSystem.CaptureCount);
		Assert.AreEqual(0, fileSystem.ReplaceCount);
	}

	[TestMethod]
	public async Task ObservedExternalConflict_LeavesBaselineUnchanged()
	{
		var fileSystem = new ControllableFileSystem("disk", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		fileSystem.CurrentStamp = new FileStamp(true, 8, DateTime.UnixEpoch.AddDays(1), "external");
		WorkspaceDocumentMutationResult changed = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			"changed",
			snapshot.FileFormat));
		WorkspaceDocumentSnapshot changedSnapshot = RequireSnapshot(changed);

		// The conditional replacement validates the expected stamp, so the conflict surfaces from
		// the write result.
		fileSystem.ReplacementResult = new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementOutcome.ExternalFileConflict,
			fileSystem.CurrentStamp);
		WorkspaceDocumentCommitResult result = await store.CommitAsync(CreateCommitRequest(changedSnapshot));

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(fileSystem.CurrentStamp, result.ObservedOnDiskStamp);
		Assert.AreEqual(1, fileSystem.ReplaceCount);
		Assert.IsTrue(result.Snapshot!.IsDirty);
		Assert.AreEqual(changedSnapshot.PersistedVersion, result.Snapshot.PersistedVersion);
	}

	[TestMethod]
	public async Task CancellationBeforeReplacement_PreservesBaseline()
	{
		var fileSystem = new ControllableFileSystem("disk", s_defaultFormat)
		{
			PendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously)
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult changed = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat));
		WorkspaceDocumentSnapshot changedSnapshot = RequireSnapshot(changed);
		using var cancellation = new CancellationTokenSource();
		Task<WorkspaceDocumentCommitResult> commit = store.CommitAsync(CreateCommitRequest(changedSnapshot), cancellation.Token);
		await fileSystem.Replacements.WhenReachedAsync(1);

		cancellation.Cancel();
		WorkspaceDocumentCommitResult result = await commit;

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Canceled, result.Outcome);
		Assert.IsFalse(fileSystem.ReplacementBegan);
		Assert.AreEqual(changedSnapshot.PersistedVersion, result.Snapshot!.PersistedVersion);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task ReplacementFailure_DoesNotAdvanceBaseline()
	{
		var fileSystem = new ControllableFileSystem("disk", s_defaultFormat)
		{
			ReplacementResult = new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementOutcome.Failed,
				Failure: new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.WriteFailed, "write failed"))
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult changed = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat));

		WorkspaceDocumentCommitResult result = await store.CommitAsync(CreateCommitRequest(changed.Snapshot!));

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.WriteFailed, result.Outcome);
		Assert.IsTrue(result.Snapshot!.IsDirty);
		Assert.AreEqual(initial.PersistedVersion, result.Snapshot.PersistedVersion);
		Assert.IsNotNull(result.Failure);
	}

	[TestMethod]
	public async Task ThrownWriteFailure_ReturnsWriteFailedAndDoesNotAdvanceBaseline()
	{
		var fileSystem = new ControllableFileSystem("disk", s_defaultFormat)
		{
			WriteException = new IOException("flush failed")
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult changed = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat));
		WorkspaceDocumentSnapshot changedSnapshot = RequireSnapshot(changed);

		WorkspaceDocumentCommitResult result = await store.CommitAsync(CreateCommitRequest(changedSnapshot));

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.WriteFailed, result.Outcome);
		Assert.AreEqual(changedSnapshot.PersistedVersion, result.Snapshot!.PersistedVersion);
		Assert.IsTrue(result.Snapshot.IsDirty);
		Assert.IsNotNull(result.Failure);
	}

	[TestMethod]
	public async Task ReplacementStateUnknown_ReturnsObservedStampAndDoesNotAdvanceBaseline()
	{
		var fileSystem = new ControllableFileSystem("disk", s_defaultFormat)
		{
			ReplacementResult = new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementOutcome.ReplacementStateUnknown,
				new FileStamp(true, 9, DateTime.UnixEpoch.AddDays(2), "unknown"),
				new WorkspaceOperationFailure(WorkspaceOperationFailureCodes.ReplacementStateUnknown, "unknown"))
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult changed = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat));

		WorkspaceDocumentCommitResult result = await store.CommitAsync(CreateCommitRequest(changed.Snapshot!));

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.ReplacementStateUnknown, result.Outcome);
		Assert.AreEqual(fileSystem.ReplacementResult.ObservedOnDiskStamp, result.ObservedOnDiskStamp);
		Assert.AreEqual(fileSystem.ReplacementResult.ObservedOnDiskStamp, result.Snapshot!.OnDiskStamp);
		Assert.AreEqual(initial.PersistedVersion, result.Snapshot.PersistedVersion);
		Assert.IsTrue(result.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task EditDuringCommit_InstallsCapturedBaselineAndLeavesLaterEditDirty()
	{
		var fileSystem = new ControllableFileSystem("disk", s_defaultFormat)
		{
			PendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously)
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult captured = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"captured",
			initial.FileFormat));
		WorkspaceDocumentSnapshot capturedSnapshot = RequireSnapshot(captured);
		Task<WorkspaceDocumentCommitResult> commit = store.CommitAsync(CreateCommitRequest(capturedSnapshot));
		await fileSystem.Replacements.WhenReachedAsync(1);
		WorkspaceDocumentMutationResult later = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, capturedSnapshot.Version),
			"later",
			initial.FileFormat));
		fileSystem.PendingReplacement!.SetResult(fileSystem.ReplacementResult);
		WorkspaceDocumentSnapshot laterSnapshot = RequireSnapshot(later);

		WorkspaceDocumentCommitResult result = await commit;

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, result.Outcome);
		Assert.AreEqual("later", result.Snapshot!.Content);
		Assert.AreEqual(capturedSnapshot.Version, result.Snapshot.PersistedVersion);
		Assert.IsTrue(result.Snapshot.IsDirty);
		Assert.AreEqual("captured", fileSystem.WrittenContent);
		Assert.AreEqual(laterSnapshot.Version, result.Snapshot.Version);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task DiscardAndSecondCommit_ReturnOperationInProgressWithoutSecondIo()
	{
		var fileSystem = new ControllableFileSystem("disk", s_defaultFormat)
		{
			PendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously)
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentMutationResult changed = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat));
		WorkspaceDocumentSnapshot changedSnapshot = RequireSnapshot(changed);
		WorkspaceDocumentCommitRequest request = CreateCommitRequest(changedSnapshot);
		Task<WorkspaceDocumentCommitResult> firstCommit = store.CommitAsync(request);
		await fileSystem.Replacements.WhenReachedAsync(1);

		WorkspaceDocumentMutationResult discard = store.Discard(new WorkspaceDocumentDiscardRequest(
			new(changedSnapshot.DocumentKey, changedSnapshot.DocumentId, changedSnapshot.Version)));
		WorkspaceDocumentCommitResult secondCommit = await store.CommitAsync(request);
		fileSystem.PendingReplacement!.SetResult(fileSystem.ReplacementResult);
		WorkspaceDocumentCommitResult completed = await firstCommit;

		Assert.AreEqual(WorkspaceDocumentMutationOutcome.OperationInProgress, discard.Outcome);
		Assert.AreEqual(WorkspaceDocumentCommitOutcome.OperationInProgress, secondCommit.Outcome);

		// A commit no longer pre-captures the destination; the replacement reports its resulting
		// stamp, so neither commit captures one.
		Assert.AreEqual(0, fileSystem.CaptureCount);
		Assert.AreEqual(1, fileSystem.ReplaceCount);
		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, completed.Outcome);
	}

	[TestMethod]
	public async Task QuickStart_OpenNewDocumentAndCommit_CreatesTheFile()
	{
		// Mirrors the package README quick start: the destination directory is created first because
		// the commit writes a temporary file next to the target before replacing it.
		using var temp = new TemporaryDirectory();
		string documentDirectory = Path.Combine(temp.Path, "workspace", "documents");
		string documentPath = Path.Combine(documentDirectory, "notes.txt");
		Directory.CreateDirectory(documentDirectory);

		IWorkspaceFileSystem fileSystem = new LocalWorkspaceFileSystem();
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentOpenResult opened = await store.OpenAsync(
			documentPath,
			new WorkspaceDocumentOpenOptions(
				TextEncodingKind.Utf8,
				new TextFileFormat(TextEncodingKind.Utf8, false, TextNewlineStyle.Lf)));
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, opened.Outcome);

		WorkspaceDocumentSnapshot snapshot = opened.Snapshot!;
		WorkspaceDocumentCommitResult committed = await store.CommitAsync(
			new WorkspaceDocumentCommitRequest(
				new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
				snapshot.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, committed.Outcome);
		Assert.IsTrue(File.Exists(documentPath));
	}

	// File-system-only test double that holds the replacement/read rendezvous open while counting
	// invocations and capturing written content; the store tests use FakeFileSystem instead, which
	// scripts results through queues and knobs. Keep the two dialects separate.
	private sealed class ControllableFileSystem : StoreFileSystemDouble
	{
		public ControllableFileSystem(string content, TextFileFormat fileFormat)
			: base(content, fileFormat)
		{
			CurrentStamp = new FileStamp(true, content.Length, DateTime.UnixEpoch, "disk");
			ReplacementResult = new WorkspaceFileReplacementResult(
				WorkspaceFileReplacementOutcome.Replaced,
				CurrentStamp);
		}

		public FileStamp CurrentStamp { get; set; }

		public WorkspaceFileReplacementResult ReplacementResult { get; set; }

		public TaskCompletionSource<WorkspaceFileReplacementResult>? PendingReplacement { get; set; }

		/// <summary>
		/// Signals that a replacement started; waiters name the one-based ordinal.
		/// </summary>
		public TestSignal Replacements { get; } = new();

		public bool ReplacementBegan { get; private set; }

		public int CaptureCount { get; private set; }

		public int ReplaceCount => Replacements.Count;

		public string? WrittenContent { get; private set; }

		public Exception? WriteException { get; set; }

		public override Task<WorkspaceFileReadResult> ReadAsync(string path, CancellationToken cancellationToken)
		{
			return Task.FromResult(CreateReadResult(CurrentStamp));
		}

		public override Task<FileStamp> CaptureStampAsync(string path, CancellationToken cancellationToken)
		{
			CaptureCount++;
			return Task.FromResult(CurrentStamp);
		}

		public override async Task<WorkspaceFileReplacementResult> WriteFileAsync(
			string destinationPath,
			ReadOnlyMemory<byte> content,
			FileStamp expectedStamp,
			CancellationToken cancellationToken)
		{
			Replacements.Advance();
			if (WriteException is not null)
				throw WriteException;

			WrittenContent = Encoding.UTF8.GetString(content.Span);

			if (PendingReplacement is not null)
				await PendingReplacement.Task.WaitAsync(cancellationToken);

			ReplacementBegan = true;
			return ReplacementResult;
		}
	}
}
