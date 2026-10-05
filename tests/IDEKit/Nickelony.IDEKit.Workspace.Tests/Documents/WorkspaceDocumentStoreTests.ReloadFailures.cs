using System.Text;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task Reload_CleanDocumentWithPreCanceledToken_ReportsCanceled()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;
		int readsBeforeReload = fileSystem.ReadCount;

		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version)),
			cancellation.Token
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Canceled, result.Outcome);
		Assert.AreEqual(readsBeforeReload, fileSystem.ReadCount);
	}

	[TestMethod]
	public async Task Reload_CleanReadFailure_ReportsReadFailedAndKeepsTracking()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;
		fileSystem.ReadResults.Enqueue(Task.FromException<WorkspaceFileReadResult>(new IOException("The file could not be read.")));

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version)),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ReadFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.ReadFailed, result.Failure!.Code);
		Assert.AreEqual(initial.Version, result.Snapshot!.Version);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task Reload_CleanDocumentWithUndecodableBytes_ReportsInvalidEncoding()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		// 0xC3 followed by 0x28 is never valid UTF-8; the clean reload path decodes raw bytes exactly
		// like the open and conflict-resolution paths and must classify the failure as an encoding
		// problem instead of a generic read failure.
		fileSystem.ReadResults.Enqueue(
			Task.FromResult(
				WorkspaceFileReadResult.FromBytes(
					new FileStamp(Exists: true, Length: 2, LastWriteTimeUtc: DateTime.UnixEpoch.AddMinutes(1), ContentHash: "bytes"),
					new ReadOnlyMemory<byte>([0xC3, 0x28])
				)
			)
		);

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version)),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ReadFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.InvalidEncoding, result.Failure!.Code);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task Reload_PermissionFailure_ReportsAccessDenied()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;
		fileSystem.ReadResults.Enqueue(Task.FromException<WorkspaceFileReadResult>(new UnauthorizedAccessException("The file could not be read.")));

		// A permission failure is a property of the path, not of the read: the clean reload reports
		// AccessDenied so a host can tell an unfixable permission problem from a transient read failure.
		WorkspaceDocumentReloadResult result = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version)),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ReadFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.AccessDenied, result.Failure!.Code);
		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
	}

	[TestMethod]
	public async Task Reload_CleanDocumentWithBytesWithoutABom_DecodesWithTheNoBomEncodingOfTheDocument()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		var utf16Options = new WorkspaceDocumentOpenOptions([TextEncoding.Utf16LittleEndian], s_defaultFormat);
		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), utf16Options, TestContext.CancellationToken)).Snapshot!;

		// "ab" as UTF-16 little endian without a byte-order mark: the document's own no-BOM encoding decodes it, where UTF-8 reads four characters.
		fileSystem.ReadResults.Enqueue(
			Task.FromResult(
				WorkspaceFileReadResult.FromBytes(
					new FileStamp(Exists: true, Length: 4, LastWriteTimeUtc: DateTime.UnixEpoch.AddMinutes(1), ContentHash: "bytes"),
					new ReadOnlyMemory<byte>([0x61, 0x00, 0x62, 0x00])
				)
			)
		);

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version)),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.Reloaded, result.Outcome);
		Assert.AreEqual("ab", result.Snapshot!.Content);
	}

	[TestMethod]
	public async Task Reload_CleanReadThatThrowsADecoderFallback_ReportsInvalidEncoding()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;
		fileSystem.ReadResults.Enqueue(Task.FromException<WorkspaceFileReadResult>(new DecoderFallbackException("The bytes did not decode.")));

		// A file system may decode inside its read: the failure reads as an encoding problem, as it does from an open.
		WorkspaceDocumentReloadResult result = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version)),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ReadFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.InvalidEncoding, result.Failure?.Code);
	}

	[TestMethod]
	public async Task Reload_DirtyDocumentStampCaptureThatThrowsADecoderFallback_ReportsInvalidEncoding()
	{
		var fileSystem = new FakeFileSystem("original", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentMutationResult edited = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), "logical", initial.FileFormat)
		);

		fileSystem.CaptureTasks.Enqueue(Task.FromException<FileStamp>(new DecoderFallbackException("The bytes did not decode.")));

		WorkspaceDocumentReloadResult result = await store.ReloadAsync(
			new WorkspaceDocumentReloadRequest(new(initial.DocumentKey, initial.DocumentId, edited.Snapshot!.Version)),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentReloadOutcome.ReadFailed, result.Outcome);
		Assert.AreEqual(WorkspaceOperationFailureCodes.InvalidEncoding, result.Failure?.Code);
		Assert.IsTrue(result.Snapshot!.IsDirty);
	}
}
