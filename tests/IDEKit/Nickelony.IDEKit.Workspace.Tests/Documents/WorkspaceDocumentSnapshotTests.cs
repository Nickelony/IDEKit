using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
public sealed class WorkspaceDocumentSnapshotTests
{
	[TestMethod]
	public void Content_ReturnsTheCompleteSnapshotText()
	{
		WorkspaceDocumentSnapshot snapshot = TestSnapshots.Create("script.lua", "one\ntwo");
		Assert.AreEqual("one\ntwo", snapshot.Content);
	}

	[TestMethod]
	public void Identity_CarriesTheKeyTheIdAndTheVersion()
	{
		WorkspaceDocumentSnapshot snapshot = TestSnapshots.Create("script.lua", "one");
		Assert.AreEqual(new WorkspaceDocumentRequestIdentity(snapshot.DocumentKey, snapshot.DocumentId, snapshot.Version), snapshot.Identity);
	}

	[TestMethod]
	public async Task DefaultOpenOptions_OpenAMissingFileAsUtf8WithoutBomWithLf()
	{
		using var temp = new TemporaryDirectory();
		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentOpenResult opened = await store.OpenAsync(
			Path.Combine(temp.Path, "new.lua"),
			WorkspaceDocumentOpenOptions.Default,
			CancellationToken.None
		);

		Assert.AreEqual(new TextFileFormat(TextEncoding.Utf8, HasBom: false, LineTerminatorStyle.Lf), opened.Snapshot?.FileFormat);
	}

	[TestMethod]
	public async Task Content_StoreCreatedSnapshot_ReturnsTheCapturedStringInstance()
	{
		using var temp = new TemporaryDirectory();
		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());

		WorkspaceDocumentOpenResult opened = await store.OpenAsync(
			Path.Combine(temp.Path, "script.lua"),
			WorkspaceDocumentOpenOptions.Default,
			CancellationToken.None
		);

		WorkspaceDocumentSnapshot initial = opened.Snapshot!;

		// A concatenation at run time is a distinct instance, so the reference check fails when the
		// snapshot copies its content instead of handing out the captured string.
		string content = string.Concat("one", "\n", "two");

		WorkspaceDocumentMutationResult replaced = store.Replace(
			new WorkspaceDocumentReplaceRequest(new(initial.DocumentKey, initial.DocumentId, initial.Version), content, initial.FileFormat)
		);

		Assert.AreSame(content, replaced.Snapshot!.Content);
	}
}
