using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

public sealed partial class WorkspaceDocumentStoreTests
{
	[TestMethod]
	public async Task Open_DirectoryPath_ReportsIsDirectoryWithoutTrackingIt()
	{
		using var temp = new TemporaryDirectory();
		string directoryPath = Path.Combine(temp.Path, "folder");
		Directory.CreateDirectory(directoryPath);
		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		// A directory is not a document: the open is rejected instead of being tracked as a new
		// empty document whose writes and deletes could never succeed.
		WorkspaceDocumentOpenResult result = await store.OpenAsync(directoryPath, s_openOptions);

		Assert.AreEqual(WorkspaceDocumentOpenOutcome.IsDirectory, result.Outcome);
		Assert.IsNull(result.Snapshot);
		Assert.IsFalse(store.TryGetSnapshot(directoryPath, out _));
	}
}
