using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task Rename_IdentityRejections_ReportTheirOutcomes()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;
		string destinationPath = TestPath("renamed.lua");

		WorkspaceDocumentRenameResult notFound = await store.RenameAsync(
			new WorkspaceDocumentRenameRequest(
				new(new WorkspaceDocumentKey(Guid.NewGuid()), TestPath("absent.lua"), 0),
				initial.OnDiskStamp,
				destinationPath
			),

			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.DocumentNotFound, notFound.Outcome);

		WorkspaceDocumentRenameResult staleInstance = await store.RenameAsync(
			new WorkspaceDocumentRenameRequest(
				new(new WorkspaceDocumentKey(Guid.NewGuid()), initial.DocumentId, initial.Version),
				initial.OnDiskStamp,
				destinationPath
			),

			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.StaleDocumentInstance, staleInstance.Outcome);

		WorkspaceDocumentRenameResult staleVersion = await store.RenameAsync(
			new WorkspaceDocumentRenameRequest(
				new(initial.DocumentKey, initial.DocumentId, initial.Version + 1),
				initial.OnDiskStamp,
				destinationPath
			),

			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentRenameOutcome.StaleDocument, staleVersion.Outcome);

		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));

		Assert.AreEqual(
			WorkspaceDocumentOpenOutcome.Opened,
			(await store.OpenAsync(destinationPath, s_openOptions, TestContext.CancellationToken)).Outcome
		);
	}

	[TestMethod]
	public async Task SaveAs_IdentityRejections_ReportTheirOutcomes()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;
		string destinationPath = TestPath("copy.lua");

		WorkspaceDocumentSaveAsResult notFound = await store.SaveAsAsync(
			new WorkspaceDocumentSaveAsRequest(
				new(new WorkspaceDocumentKey(Guid.NewGuid()), TestPath("absent.lua"), 0),
				initial.OnDiskStamp,
				destinationPath
			),

			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.DocumentNotFound, notFound.Outcome);

		WorkspaceDocumentSaveAsResult staleInstance = await store.SaveAsAsync(
			new WorkspaceDocumentSaveAsRequest(
				new(new WorkspaceDocumentKey(Guid.NewGuid()), initial.DocumentId, initial.Version),
				initial.OnDiskStamp,
				destinationPath
			),

			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.StaleDocumentInstance, staleInstance.Outcome);

		WorkspaceDocumentSaveAsResult staleVersion = await store.SaveAsAsync(
			new WorkspaceDocumentSaveAsRequest(
				new(initial.DocumentKey, initial.DocumentId, initial.Version + 1),
				initial.OnDiskStamp,
				destinationPath
			),

			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentSaveAsOutcome.StaleDocument, staleVersion.Outcome);

		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
		Assert.AreEqual(0, fileSystem.ReplaceCount);
	}

	[TestMethod]
	public async Task Delete_IdentityRejections_ReportTheirOutcomes()
	{
		var fileSystem = new FakeFileSystem("content", s_defaultFormat);
		await using var store = new WorkspaceDocumentStore(fileSystem);

		WorkspaceDocumentSnapshot initial = (await store.OpenAsync(TestPath("script.lua"), s_openOptions, TestContext.CancellationToken)).Snapshot!;

		WorkspaceDocumentDeleteResult notFound = await store.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(new WorkspaceDocumentKey(Guid.NewGuid()), TestPath("absent.lua"), 0), initial.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.DocumentNotFound, notFound.Outcome);

		WorkspaceDocumentDeleteResult staleInstance = await store.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(
				new(new WorkspaceDocumentKey(Guid.NewGuid()), initial.DocumentId, initial.Version),
				initial.OnDiskStamp
			),

			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.StaleDocumentInstance, staleInstance.Outcome);

		WorkspaceDocumentDeleteResult staleVersion = await store.DeleteAsync(
			new WorkspaceDocumentDeleteRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version + 1), initial.OnDiskStamp),
			TestContext.CancellationToken
		);

		Assert.AreEqual(WorkspaceDocumentDeleteOutcome.StaleDocument, staleVersion.Outcome);

		Assert.IsTrue(store.TryGetSnapshot(initial.DocumentId, out _));
		Assert.AreEqual(0, fileSystem.Deletes.Count);
	}
}
