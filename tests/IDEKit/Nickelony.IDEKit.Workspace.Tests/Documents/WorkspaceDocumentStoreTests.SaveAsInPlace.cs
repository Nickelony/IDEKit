using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task SaveAs_SamePath_SavesInPlace()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentSnapshot dirty = store
			.Replace(
				new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "changed", initial.FileFormat)
			)
			.Snapshot!;

		FileStamp writtenStamp = new(Exists: true, Length: 7, LastWriteTimeUtc: DateTime.UnixEpoch.AddMinutes(1), ContentHash: "written");

		// The destination resolves to the tracked document's own file, so the save writes in place:
		// the validated source stamp is the replacement expectation instead of an absent destination.
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.ReplacementStamp = writtenStamp;

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(
			new WorkspaceDocumentSaveAsRequest(new(dirty.DocumentKey, dirty.DocumentId, dirty.Version), dirty.OnDiskStamp, initial.DocumentId),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, result.Outcome);
		Assert.AreEqual(initial.DocumentId, result.Snapshot!.DocumentId);
		Assert.IsFalse(result.Snapshot.IsDirty);
		Assert.AreEqual(writtenStamp, result.Snapshot.OnDiskStamp);
		Assert.AreEqual(initial.OnDiskStamp, fileSystem.LastReplacementExpectedStamp);
		Assert.AreEqual(1, fileSystem.ReplaceCount);
	}

	[TestMethod]
	public async Task SaveAs_CaseVariantSamePath_SavesInPlaceOnCaseInsensitivePolicy()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);

		await using var store = new WorkspaceDocumentStore(
			fileSystem,
			new WorkspaceOptions { PathComparison = LocalPathComparisonPolicy.CaseInsensitive }
		);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentSnapshot dirty = store
			.Replace(
				new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "changed", initial.FileFormat)
			)
			.Snapshot!;

		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);

		// A case variant resolves to the same tracked instance, so it is a save in place rather than
		// a destination collision; the caller's spelling becomes the display path.
		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(
			new WorkspaceDocumentSaveAsRequest(new(dirty.DocumentKey, dirty.DocumentId, dirty.Version), dirty.OnDiskStamp, TestPath("SCRIPT.lua")),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.SavedAs, result.Outcome);
		Assert.IsFalse(result.Snapshot!.IsDirty);
		Assert.AreEqual(TestPath("SCRIPT.lua"), result.Snapshot.DisplayPath);
		Assert.IsTrue(store.TryGetSnapshot(TestPath("script.lua"), out _));
	}

	[TestMethod]
	public async Task SaveAs_SamePathReplacementConflict_ReportsExternalFileConflict()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		// The source stamp was current when the save started, so a conflict from the in-place
		// replacement means the tracked file changed again instead of a destination collision.
		fileSystem.ReplacementOutcome = WorkspaceFileReplacementOutcome.ExternalFileConflict;
		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(
			new WorkspaceDocumentSaveAsRequest(
				new(initial.DocumentKey, initial.DocumentId, initial.Version),
				initial.OnDiskStamp,
				initial.DocumentId
			),

			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.ExternalFileConflict, result.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task SaveAs_ReplacementStateUnknown_ReportsReplacementStateUnknown()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		fileSystem.ReplacementOutcome = WorkspaceFileReplacementOutcome.ReplacementStateUnknown;

		fileSystem.CapturedStamps.Enqueue(initial.OnDiskStamp);
		fileSystem.CapturedStamps.Enqueue(FileStamp.Missing);

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(
			new WorkspaceDocumentSaveAsRequest(
				new(initial.DocumentKey, initial.DocumentId, initial.Version),
				initial.OnDiskStamp,
				TestPath("copy.lua")
			),

			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.ReplacementStateUnknown, result.Outcome);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task SaveAs_ReplacementWithoutStampAndFailedRecapture_ReportsReplacementStateUnknown()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		// The replacement completed without reporting its stamp and the re-capture fails, so the
		// final state cannot be established and no baseline may be installed. The state-unknown
		// failure carries the capture exception for post-mortems.
		var captureFailure = new IOException("The stamp could not be captured.");
		fileSystem.OmitReplacementStamp = true;

		fileSystem.CaptureTasks.Enqueue(Task.FromResult(initial.OnDiskStamp));
		fileSystem.CaptureTasks.Enqueue(Task.FromResult(FileStamp.Missing));
		fileSystem.CaptureTasks.Enqueue(Task.FromException<FileStamp>(captureFailure));

		WorkspaceDocumentSaveAsResult result = await store.SaveAsAsync(
			new WorkspaceDocumentSaveAsRequest(
				new(initial.DocumentKey, initial.DocumentId, initial.Version),
				initial.OnDiskStamp,
				TestPath("copy.lua")
			),

			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.ReplacementStateUnknown, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ReplacementStateUnknown, result.Failure!.Code);
		Assert.AreSame(captureFailure, result.Failure.Exception);
		Assert.Contains("could not be captured", result.Failure.Message);
		Assert.AreEqual(initial.OnDiskStamp, result.Snapshot!.OnDiskStamp);
	}
}
