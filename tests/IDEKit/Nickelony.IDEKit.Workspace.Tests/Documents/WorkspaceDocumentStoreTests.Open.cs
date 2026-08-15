using Nickelony.IDEKit.Core.Pathing;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task OpenEquivalentPaths_UsesOneDocumentAndFirstDisplayPath()
	{
		var fileSystem = new FakeFileSystem("source", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		string firstPath = TestPath("workspace", "scripts", "main.lua");
		string equivalentPath = TestPath("workspace", "scripts", ".", "main.lua");

		WorkspaceDocumentOpenResult first = await store.OpenAsync(firstPath, s_openOptions);
		WorkspaceDocumentOpenResult second = await store.OpenAsync(equivalentPath, s_openOptions);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, first.Outcome);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.AlreadyOpen, second.Outcome);
		Assert.IsNotNull(first.Snapshot);
		Assert.IsNotNull(second.Snapshot);
		Assert.AreEqual(first.Snapshot.DocumentKey, second.Snapshot.DocumentKey);
		Assert.AreEqual(first.Snapshot.DocumentId, second.Snapshot.DocumentId);
		Assert.AreEqual(Path.GetFullPath(firstPath), first.Snapshot.DocumentId);
		Assert.AreEqual(firstPath, first.Snapshot.DisplayPath);
		Assert.AreEqual(first.Snapshot.DocumentId, fileSystem.LastReadPath);
		Assert.AreEqual(1, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task OpenCaseVariantPaths_FollowConfiguredPathComparison()
	{
		var sensitiveFileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using (var sensitive = new WorkspaceDocumentStore(
			sensitiveFileSystem,
			new WorkspaceDocumentOptions { PathComparison = LocalPathComparisonPolicy.CaseSensitive }))
		{
			WorkspaceDocumentOpenResult first = await sensitive.OpenAsync(TestPath("folder", "main.lua"), s_openOptions);
			WorkspaceDocumentOpenResult second = await sensitive.OpenAsync(TestPath("folder", "MAIN.lua"), s_openOptions);

			Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, first.Outcome);
			Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, second.Outcome);
			Assert.AreNotEqual(first.Snapshot!.DocumentKey, second.Snapshot!.DocumentKey);
			Assert.AreEqual(2, sensitiveFileSystem.ReadCount);
		}

		var insensitiveFileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using (var insensitive = new WorkspaceDocumentStore(
			insensitiveFileSystem,
			new WorkspaceDocumentOptions { PathComparison = LocalPathComparisonPolicy.CaseInsensitive }))
		{
			WorkspaceDocumentOpenResult first = await insensitive.OpenAsync(TestPath("folder", "main.lua"), s_openOptions);
			WorkspaceDocumentOpenResult second = await insensitive.OpenAsync(TestPath("folder", "MAIN.lua"), s_openOptions);

			Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, first.Outcome);
			Assert.AreEqual(WorkspaceDocumentOpenOutcome.AlreadyOpen, second.Outcome);
			Assert.AreEqual(first.Snapshot!.DocumentKey, second.Snapshot!.DocumentKey);
			Assert.AreEqual(1, insensitiveFileSystem.ReadCount);
		}
	}

	[TestMethod]
	public async Task PathComparison_ExposesConfiguredValue()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var caseSensitiveStore = new WorkspaceDocumentStore(
			fileSystem,
			new WorkspaceDocumentOptions { PathComparison = LocalPathComparisonPolicy.CaseSensitive });
		await using var caseInsensitiveStore = new WorkspaceDocumentStore(
			fileSystem,
			new WorkspaceDocumentOptions { PathComparison = LocalPathComparisonPolicy.CaseInsensitive });

		// The explicitly supplied policy is exposed; the case-variant open test pins the behavioral
		// difference between the two policies.
		Assert.AreEqual(LocalPathComparisonPolicy.CaseSensitive, caseSensitiveStore.PathComparison);
		Assert.AreEqual(LocalPathComparisonPolicy.CaseInsensitive, caseInsensitiveStore.PathComparison);
	}

	[TestMethod]
	public async Task Open_MissingPathWithoutCreateIfMissing_ReportsNotFound()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		fileSystem.ReadResults.Enqueue(Task.FromResult(WorkspaceFileReadResult.FromContent(
			string.Empty,
			s_defaultFormat,
			FileStamp.Missing)));
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentOpenOptions options = new(
			TextEncodingKind.Utf8,
			s_defaultFormat,
			CreateIfMissing: false);

		WorkspaceDocumentOpenResult result = await store.OpenAsync(TestPath("missing.lua"), options);

		// The read reports a missing file and the options forbid creating one, so the open is a
		// not-found outcome instead of a new empty document.
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.NotFound, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.IsFalse(store.TryGetSnapshot(TestPath("missing.lua"), out _));
	}

	[TestMethod]
	public async Task ConcurrentFirstOpen_UsesOneReadAndFollowerCancellationIsIndependent()
	{
		var fileSystem = new FakeFileSystem("loaded", s_defaultFormat);
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);

		Task<WorkspaceDocumentOpenResult> loader = store.OpenAsync(TestPath("script.lua"), s_openOptions);
		await fileSystem.Reads.WhenReachedAsync(1);

		using var followerCancellation = new CancellationTokenSource();
		Task<WorkspaceDocumentOpenResult> follower = store.OpenAsync(
			TestPath("script.lua"),
			s_openOptions,
			followerCancellation.Token);
		followerCancellation.Cancel();

		WorkspaceDocumentOpenResult followerResult = await follower;
		pendingRead.SetResult(fileSystem.CreateReadResult());
		WorkspaceDocumentOpenResult loaderResult = await loader;

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Canceled, followerResult.Outcome);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, loaderResult.Outcome);
		Assert.AreEqual(1, fileSystem.ReadCount);
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task Open_WaitingBehindAnInFlightDelete_ReportsCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingDelete = pendingDelete.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot snapshot = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		Task<WorkspaceDocumentDeleteResult> delete = store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version),
			snapshot.OnDiskStamp));
		await fileSystem.Deletes.WhenReachedAsync(1);

		using var cancellation = new CancellationTokenSource();
		Task<WorkspaceDocumentOpenResult> waitingOpen = store.OpenAsync(
			TestPath("script.lua"),
			s_openOptions,
			cancellation.Token);
		cancellation.Cancel();

		WorkspaceDocumentOpenResult openResult = await waitingOpen;
		pendingDelete.SetResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted));
		await delete;

		// Canceling while the open waits for the delete to resolve reports Canceled without starting a
		// second read; the initial open in this test is the only read.
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Canceled, openResult.Outcome);
		Assert.AreEqual(1, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task ConcurrentFirstOpen_ServesTheFollowerFromTheCompletedFirstRead()
	{
		var fileSystem = new FakeFileSystem("loaded", s_defaultFormat);
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);

		Task<WorkspaceDocumentOpenResult> leader = store.OpenAsync(TestPath("script.lua"), s_openOptions);
		await fileSystem.Reads.WhenReachedAsync(1);
		Task<WorkspaceDocumentOpenResult> follower = store.OpenAsync(TestPath("script.lua"), s_openOptions);

		pendingRead.SetResult(fileSystem.CreateReadResult());
		WorkspaceDocumentOpenResult leaderResult = await leader;
		WorkspaceDocumentOpenResult followerResult = await follower;

		// The follower is served from the leader's completed read instead of starting a second one,
		// and it receives the same tracked instance.
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, leaderResult.Outcome);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.AlreadyOpen, followerResult.Outcome);
		Assert.AreEqual(1, fileSystem.ReadCount);
		Assert.AreEqual(leaderResult.Snapshot!.DocumentKey, followerResult.Snapshot!.DocumentKey);
		Assert.AreEqual(leaderResult.Snapshot.DocumentId, followerResult.Snapshot.DocumentId);
		Assert.AreEqual(leaderResult.Snapshot.Version, followerResult.Snapshot.Version);
		Assert.AreEqual(leaderResult.Snapshot.Content, followerResult.Snapshot.Content);
	}

	[TestMethod]
	public async Task FailedOpen_RemovesReservationAndAllowsRetry()
	{
		var fileSystem = new FakeFileSystem("retry", s_defaultFormat);
		var failedRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = failedRead.Task;
		fileSystem.ReadResults.Enqueue(Task.FromResult(fileSystem.CreateReadResult()));
		await using var store = new WorkspaceDocumentStore(fileSystem);

		Task<WorkspaceDocumentOpenResult> loader = store.OpenAsync(TestPath("script.lua"), s_openOptions);
		await fileSystem.Reads.WhenReachedAsync(1);
		Task<WorkspaceDocumentOpenResult> follower = store.OpenAsync(TestPath("script.lua"), s_openOptions);
		fileSystem.PendingRead = null;
		failedRead.SetException(new IOException("read failed"));

		WorkspaceDocumentOpenResult failed = await loader;
		WorkspaceDocumentOpenResult retried = await follower;

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.LoadFailed, failed.Outcome);
		Assert.IsNotNull(failed.Failure);
		Assert.AreEqual(WorkspaceOperationFailureCodes.LoadFailed, failed.Failure.Code);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, retried.Outcome);
		Assert.AreEqual(2, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task CanceledOpen_RemovesReservationAndAllowsRetry()
	{
		var fileSystem = new FakeFileSystem("canceled", s_defaultFormat);
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);

		using var cancellation = new CancellationTokenSource();
		Task<WorkspaceDocumentOpenResult> canceledOpen = store.OpenAsync(
			TestPath("script.lua"),
			s_openOptions,
			cancellation.Token);
		await fileSystem.Reads.WhenReachedAsync(1);
		cancellation.Cancel();

		WorkspaceDocumentOpenResult canceled = await canceledOpen;
		fileSystem.PendingRead = null;
		WorkspaceDocumentOpenResult retried = await store.OpenAsync(TestPath("script.lua"), s_openOptions);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Canceled, canceled.Outcome);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, retried.Outcome);
		Assert.AreEqual(2, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task PreCanceledOpen_DoesNotTrackTheDocumentAndAllowsRetry()
	{
		var fileSystem = new FakeFileSystem("retry", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		WorkspaceDocumentOpenResult canceled = await store.OpenAsync(
			TestPath("script.lua"),
			s_openOptions,
			cancellation.Token);

		// A token that is already canceled when the load starts must leave no trace: nobody is
		// tracked, and the released reservation lets a later open load the document normally instead
		// of waiting for the canceled attempt. The file-system call itself is the file system's
		// responsibility to observe; real implementations reject a canceled token before touching disk.
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Canceled, canceled.Outcome);
		Assert.IsNull(canceled.Snapshot);
		Assert.IsFalse(store.TryGetSnapshot(TestPath("script.lua"), out _));

		WorkspaceDocumentOpenResult retried = await store.OpenAsync(TestPath("script.lua"), s_openOptions);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, retried.Outcome);
		Assert.AreEqual("retry", retried.Snapshot!.Content);
	}

	[TestMethod]
	public async Task Open_WaitsForSaveAsDestinationReservationAndReturnsRetargetedDocument()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("source.lua"), s_openOptions)).Snapshot!;
		string destinationPath = Path.Combine(Path.GetDirectoryName(initial.DocumentId)!, "destination.lua");
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);

		Task<WorkspaceDocumentSaveAsResult> saveAs = store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));
		await fileSystem.Replacements.WhenReachedAsync(1);

		Task<WorkspaceDocumentOpenResult> open = store.OpenAsync(destinationPath, s_openOptions);
		Assert.IsFalse(open.IsCompleted);
		pendingReplacement.SetResult(new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementOutcome.Replaced,
			new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(1), "saved")));

		WorkspaceDocumentSaveAsResult saved = await saveAs;
		WorkspaceDocumentOpenResult opened = await open;

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, saved.Outcome);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.AlreadyOpen, opened.Outcome);
		Assert.AreEqual(saved.Snapshot!.DocumentKey, opened.Snapshot!.DocumentKey);
		Assert.AreEqual(saved.Snapshot.DocumentId, opened.Snapshot.DocumentId);
	}

	[TestMethod]
	public async Task Open_DuringInFlightDelete_WaitsForDeleteAndLoadsFreshDocument()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingDelete = pendingDelete.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		Task<WorkspaceDocumentDeleteResult> delete = store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp));
		await fileSystem.Deletes.WhenReachedAsync(1);

		// The delete is in flight, so the open must not return the instance that is about to be removed.
		Task<WorkspaceDocumentOpenResult> open = store.OpenAsync(TestPath("script.lua"), s_openOptions);
		Assert.IsFalse(open.IsCompleted);

		pendingDelete.SetResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.Deleted));
		WorkspaceDocumentDeleteResult deleted = await delete;
		WorkspaceDocumentOpenResult opened = await open;

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.Deleted, deleted.Outcome);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, opened.Outcome);
		Assert.AreNotEqual(initial.DocumentKey, opened.Snapshot!.DocumentKey);
	}

	[TestMethod]
	public async Task Open_DuringInFlightDeleteThatFails_ReturnsAlreadyOpen()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		var pendingDelete = new TaskCompletionSource<WorkspaceFileDeleteResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingDelete = pendingDelete.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		Task<WorkspaceDocumentDeleteResult> delete = store.DeleteAsync(new WorkspaceDocumentDeleteRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp));
		await fileSystem.Deletes.WhenReachedAsync(1);

		Task<WorkspaceDocumentOpenResult> open = store.OpenAsync(TestPath("script.lua"), s_openOptions);
		Assert.IsFalse(open.IsCompleted);

		// A failed delete leaves the document tracked, so the open resolves to the same instance.
		pendingDelete.SetResult(new WorkspaceFileDeleteResult(WorkspaceFileDeleteOutcome.ExternalFileConflict));
		WorkspaceDocumentDeleteResult deleted = await delete;
		WorkspaceDocumentOpenResult opened = await open;

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.ExternalFileConflict, deleted.Outcome);
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.AlreadyOpen, opened.Outcome);
		Assert.AreEqual(initial.DocumentKey, opened.Snapshot!.DocumentKey);
	}

	[TestMethod]
	public async Task InvalidSelectedEncoding_ReturnsInvalidEncodingFailure()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		string path = Path.Combine(directory, "invalid.lua");
		File.WriteAllBytes(path, [0xC3, 0x28]);
		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentOpenResult result = await store.OpenAsync(path, s_openOptions);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.LoadFailed, result.Outcome);
		Assert.IsNotNull(result.Failure);
		Assert.AreEqual(WorkspaceOperationFailureCodes.InvalidEncoding, result.Failure.Code);
		Assert.IsNull(result.Snapshot);
	}

	[TestMethod]
	public async Task UndefinedWindows1252Byte_ReturnsInvalidEncodingFailure()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		string path = Path.Combine(directory, "invalid.txt");
		File.WriteAllBytes(path, [0x81]);
		WorkspaceDocumentOpenOptions options = new(
			TextEncodingKind.Windows1252,
			new TextFileFormat(TextEncodingKind.Windows1252, false, TextNewlineStyle.None));
		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentOpenResult result = await store.OpenAsync(path, options);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.LoadFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.InvalidEncoding, result.Failure!.Code);
		Assert.IsNull(result.Snapshot);
	}

	[TestMethod]
	public async Task MissingFile_UsesNewFileFormatAndCommitCreatesEmptyFile()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		string path = Path.Combine(directory, "new.lua");
		TextFileFormat newFileFormat = new(TextEncodingKind.Utf16LittleEndian, true, TextNewlineStyle.CrLf);
		WorkspaceDocumentOpenOptions options = new(TextEncodingKind.Utf8, newFileFormat);
		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentSnapshot opened = (await store.OpenAsync(path, options)).Snapshot!;
		WorkspaceDocumentCommitResult committed = await store.CommitAsync(CreateCommitRequest(opened));

		Assert.IsFalse(opened.ExistsOnDisk);
		Assert.AreEqual(string.Empty, opened.Content);
		Assert.AreEqual(newFileFormat, opened.FileFormat);
		Assert.IsFalse(opened.IsDirty);
		Assert.IsNull(opened.OnDiskStamp.Length);
		Assert.IsNull(opened.OnDiskStamp.LastWriteTimeUtc);
		Assert.IsNull(opened.OnDiskStamp.ContentHash);
		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, committed.Outcome);
		Assert.IsTrue(File.Exists(path));
		Assert.AreEqual(2, new FileInfo(path).Length);
		CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFE }, File.ReadAllBytes(path));
		Assert.IsFalse(committed.Snapshot!.IsDirty);
		Assert.AreEqual(newFileFormat, committed.Snapshot.FileFormat);
	}

	[TestMethod]
	public async Task Windows1252Document_PreservesEncodingForLaterNonAsciiEdit()
	{
		using var temp = new TemporaryDirectory();
		string directory = temp.Path;
		string path = Path.Combine(directory, "strings.txt");
		File.WriteAllBytes(path, [0x6E, 0x61, 0x6D, 0x65]);
		TextFileFormat classicScriptFormat = new(
			TextEncodingKind.Windows1252,
			false,
			TextNewlineStyle.None);
		WorkspaceDocumentOpenOptions options = new(TextEncodingKind.Windows1252, classicScriptFormat);
		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());
		WorkspaceDocumentSnapshot opened = (await store.OpenAsync(path, options)).Snapshot!;
		WorkspaceDocumentMutationResult replaced = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(opened.DocumentKey, opened.DocumentId, opened.Version),
			"café",
			opened.FileFormat));

		WorkspaceDocumentCommitResult committed = await store.CommitAsync(CreateCommitRequest(replaced.Snapshot!));

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, committed.Outcome);
		CollectionAssert.AreEqual(new byte[] { 0x63, 0x61, 0x66, 0xE9 }, File.ReadAllBytes(path));
		Assert.IsFalse(committed.Snapshot!.IsDirty);
	}

	[TestMethod]
	public async Task Open_PermissionFailure_ReportsAccessDenied()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		fileSystem.ReadResults.Enqueue(Task.FromException<WorkspaceFileReadResult>(
			new UnauthorizedAccessException("The file could not be read.")));
		await using var store = new WorkspaceDocumentStore(fileSystem);

		// The open read maps a permission failure to AccessDenied like the reload and
		// conflict-resolution reads, so the host sees one permission vocabulary across operations.
		WorkspaceDocumentOpenResult result = await store.OpenAsync(TestPath("script.lua"), s_openOptions);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.LoadFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.AccessDenied, result.Failure!.Code);
		Assert.IsFalse(store.TryGetSnapshot(TestPath("script.lua"), out _));
	}
}
