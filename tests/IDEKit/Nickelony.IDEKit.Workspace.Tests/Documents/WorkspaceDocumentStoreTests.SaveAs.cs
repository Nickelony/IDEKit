using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task SaveAs_DestinationAndWriteFailures_MapToOutcomes()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("source.lua"), s_openOptions)).Snapshot!;
		string destinationPath = Path.Combine(Path.GetDirectoryName(initial.DocumentId)!, "destination.lua");

		// Save As captures the source stamp and then the destination stamp; an existing destination
		// stamp stops the operation before any write.
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(new FileStamp(true, 3, DateTime.UnixEpoch.AddMinutes(1), "existing"));
		WorkspaceDocumentSaveAsResult exists = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.DestinationExists, exists.Outcome);
		Assert.AreEqual(0, fileSystem.ReplaceCount);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		fileSystem.ReplacementOutcome = WorkspaceFileReplacementOutcome.Failed;
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		WorkspaceDocumentSaveAsResult failed = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.WriteFailed, failed.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		// The failed write must not advance the persisted baseline or mark the document dirty.
		Assert.AreEqual(initial.PersistedVersion, failed.Snapshot!.PersistedVersion);
		Assert.IsFalse(failed.Snapshot.IsDirty);
	}

	[TestMethod]
	public async Task SaveAs_ReplacementOutcomes_MapToOutcomes()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat)
		{
			ReplacementOutcome = WorkspaceFileReplacementOutcome.DestinationExists
		};
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("source.lua"), s_openOptions)).Snapshot!;
		string destinationPath = TestPath("folder", "destination.lua");

		// The source stamp matches and the destination is missing, so the write runs and reports the
		// directory that occupies the destination path.
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		WorkspaceDocumentSaveAsResult exists = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.DestinationExists, exists.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		fileSystem.ReplacementOutcome = WorkspaceFileReplacementOutcome.Canceled;
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		WorkspaceDocumentSaveAsResult canceled = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			destinationPath));

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.Canceled, canceled.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task SaveAs_SourceStampConflict_ReportsExternalFileConflictAndKeepsTracking()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("source.lua"), s_openOptions)).Snapshot!;
		FileStamp observedStamp = new(true, 11, DateTime.UnixEpoch.AddMinutes(1), "changed");
		fileSystem.CapturedStamps.Enqueue(observedStamp);

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			TestPath("folder", "destination.lua")));

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.ExternalFileConflict, result.Outcome);
		Assert.AreEqual(observedStamp, result.ObservedOnDiskStamp);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ExternalFileConflict, result.Failure!.Code);
		Assert.AreEqual(0, fileSystem.ReplaceCount);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task SaveAs_DestinationWithInFlightOpen_ReportsDestinationBusy()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("source.lua"), s_openOptions)).Snapshot!;
		var pendingRead = new TaskCompletionSource<WorkspaceFileReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingRead = pendingRead.Task;

		// The open holds a reservation for the destination path while it loads.
		Task<WorkspaceDocumentOpenResult> open = store.OpenAsync(TestPath("destination.lua"), s_openOptions);
		await fileSystem.Reads.WhenReachedAsync(2);

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			TestPath("destination.lua")));

		// An in-flight open would add its document at the destination path, so the save must not
		// mutate anything there.
		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.DestinationBusy, result.Outcome);
		Assert.AreEqual(0, fileSystem.ReplaceCount);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		pendingRead.SetResult(fileSystem.CreateReadResult());
		WorkspaceDocumentOpenResult opened = await open;
		Assert.AreEqual(WorkspaceDocumentOpenOutcome.Opened, opened.Outcome);
	}

	[TestMethod]
	public async Task SaveAs_UnencodableContent_ReportsInvalidEncoding()
	{
		var fileSystem = new FakeFileSystem("disk", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("source.lua"), s_openOptions)).Snapshot!;
		TextFileFormat windows1252Format = new(TextEncodingKind.Windows1252, false, TextNewlineStyle.None);
		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"\u2605",
			windows1252Format));
		Assert.AreEqual(WorkspaceDocumentMutationOutcome.Changed, edited.Outcome);

		// The source and destination stamps are captured before the content is encoded, so both
		// captures must succeed for the encoding failure to be reached.
		fileSystem.CapturedStamps.Enqueue(edited.Snapshot!.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(edited.Snapshot.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp,
			TestPath("copy.lua")));

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.WriteFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.InvalidEncoding, result.Failure!.Code);
		Assert.AreEqual(0, fileSystem.ReplaceCount);
	}

	[TestMethod]
	public async Task SaveAs_ReplacementWithoutObservedStamp_RecapturesTheStamp()
	{
		var fileSystem = new FakeFileSystem("retained", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot dirty = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			"changed",
			initial.FileFormat)).Snapshot!;

		// A stamp of Missing here would claim the destination that was just written does not exist.
		FileStamp writtenStamp = new(true, 11, DateTime.UnixEpoch.AddMinutes(5), "written");
		fileSystem.OmitReplacementStamp = true;
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		fileSystem.CapturedStamps.Enqueue(writtenStamp);

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(dirty.DocumentKey, dirty.DocumentId, dirty.Version),
			dirty.OnDiskStamp,
			TestPath("folder", "copy.lua")));

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, result.Outcome);
		Assert.AreEqual(writtenStamp, result.Snapshot!.OnDiskStamp);
		Assert.IsTrue(result.Snapshot.ExistsOnDisk);
	}

	[TestMethod]
	public async Task SaveAs_SecondPathReplacementConflict_ReportsDestinationExists()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("source.lua"), s_openOptions)).Snapshot!;
		FileStamp appearedStamp = new(true, 4, DateTime.UnixEpoch.AddMinutes(1), "appeared");

		// The destination appeared between the pre-write probe and the conditional replacement; for a
		// second path the benign race is a destination collision, not a source-file conflict.
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		fileSystem.ReplacementOutcome = WorkspaceFileReplacementOutcome.ExternalFileConflict;
		fileSystem.ReplacementStamp = appearedStamp;

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			TestPath("copy.lua")));

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.DestinationExists, result.Outcome);
		Assert.AreEqual(appearedStamp, result.ObservedOnDiskStamp);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task SaveAs_RootDestination_WritesTemporaryFileInTheRootDirectory()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		string rootPath = Path.GetPathRoot(Path.GetFullPath(Environment.CurrentDirectory))!;
		WorkspaceDocumentSnapshot source = (await store.OpenAsync(TestPath("source.lua"), s_openOptions)).Snapshot!;
		fileSystem.CapturedStamps.Enqueue(source.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(source.DocumentKey, source.DocumentId, source.Version),
			source.OnDiskStamp,
			rootPath));

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, result.Outcome);
		Assert.AreEqual(rootPath, fileSystem.LastWriteDestination);
	}

	[TestMethod]
	public async Task SaveAs_BlankDestination_ReportsInvalidPath()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(initial.DocumentKey, initial.DocumentId, initial.Version),
			initial.OnDiskStamp,
			"   "));

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.InvalidPath, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.AreEqual(0, fileSystem.ReplaceCount);
	}

	[TestMethod]
	public async Task SaveAs_TrackedDestinationCollision_ReportsDestinationInUse()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot source = (await store.OpenAsync(
			TestPath("folder", "one.lua"),
			s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot collision = (await store.OpenAsync(
			TestPath("folder", "two.lua"),
			s_openOptions)).Snapshot!;

		// The destination is tracked by an unrelated document instance, so Save As is rejected before
		// the source stamp is captured and before the file system is asked to write anything.
		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(source.DocumentKey, source.DocumentId, source.Version),
			source.OnDiskStamp,
			TestPath("folder", "two.lua")));

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.DestinationInUse, result.Outcome);
		Assert.AreEqual(0, fileSystem.ReplaceCount);
		Assert.IsTrue(store.TryGetSnapshot(source.DocumentId, out _));
		Assert.IsTrue(store.TryGetSnapshot(collision.DocumentId, out _));
	}

	[TestMethod]
	public async Task SaveAs_PreCanceledWrite_ReportsCanceledAndKeepsTracking()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions)).Snapshot!;

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(
			new WorkspaceDocumentSaveAsRequest(
				new(initial.DocumentKey, initial.DocumentId, initial.Version),
				initial.OnDiskStamp,
				TestPath("copy.lua")),
			cancellation.Token);

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.Canceled, result.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	[Timeout(15000)]
	public async Task SaveAs_DestinationReservedByAnotherSaveAs_ReportsDestinationBusy()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		var pendingReplacement = new TaskCompletionSource<WorkspaceFileReplacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		fileSystem.PendingReplacement = pendingReplacement.Task;
		await using var store = new WorkspaceDocumentStore(fileSystem);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(TestPath("one.lua"), s_openOptions)).Snapshot!;
		WorkspaceDocumentSnapshot second = (await store.OpenAsync(TestPath("two.lua"), s_openOptions)).Snapshot!;
		string destinationPath = TestPath("shared.lua");

		// The first save-as holds the destination reservation for the whole write, not only for the
		// stamp checks that preceded it.
		fileSystem.CapturedStamps.Enqueue(first.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);
		Task<WorkspaceDocumentSaveAsResult> held = store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(first.DocumentKey, first.DocumentId, first.Version),
			first.OnDiskStamp,
			destinationPath));
		await fileSystem.Replacements.WhenReachedAsync(1);

		// A second save-as onto the same destination is rejected before it captures anything or writes,
		// because the first operation would publish a file the second is about to claim.
		WorkspaceDocumentSaveAsResult blocked = await store.SaveAsAsync(new WorkspaceDocumentSaveAsRequest(
			new(second.DocumentKey, second.DocumentId, second.Version),
			second.OnDiskStamp,
			destinationPath));

		pendingReplacement.SetResult(new WorkspaceFileReplacementResult(
			WorkspaceFileReplacementOutcome.Replaced,
			new FileStamp(true, 7, DateTime.UnixEpoch.AddMinutes(1), "written")));

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.DestinationBusy, blocked.Outcome);
		Assert.AreEqual(second.DocumentId, blocked.Snapshot!.DocumentId);
		Assert.AreEqual(1, fileSystem.ReplaceCount);
		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, (await held).Outcome);
		Assert.IsTrue(store.TryGetSnapshot(second.DocumentId, out _));
	}
}
