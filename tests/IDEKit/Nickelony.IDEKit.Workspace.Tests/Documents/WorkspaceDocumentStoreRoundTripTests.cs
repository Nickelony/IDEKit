using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Documents.FileSystem;

namespace Nickelony.IDEKit.Workspace.Tests;

[TestClass]
public sealed class WorkspaceDocumentStoreRoundTripTests
{
	[TestMethod]
	public async Task Commit_PreservesTheByteOrderMarkOfTheOpenedFile()
	{
		using var temp = new TemporaryDirectory();
		string filePath = Path.Combine(temp.Path, "script.lua");
		TextFileFormat format = new(TextEncodingKind.Utf8, true, TextNewlineStyle.Lf);
		File.WriteAllBytes(filePath, WorkspaceTextCodec.Encode("one", format));

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());
		WorkspaceDocumentOpenOptions options = new(TextEncodingKind.Utf8, format);
		WorkspaceDocumentSnapshot opened = (await store.OpenAsync(filePath, options)).Snapshot!;

		// The byte-order mark is detected from the bytes even though the options describe a bomless
		// default, and it must survive the rewrite.
		Assert.IsTrue(opened.FileFormat.HasBom);

		WorkspaceDocumentMutationResult edited = store.Replace(new WorkspaceDocumentReplaceRequest(
			new(opened.DocumentKey, opened.DocumentId, opened.Version),
			"two",
			opened.FileFormat));
		WorkspaceDocumentCommitResult committed = await store.CommitAsync(new WorkspaceDocumentCommitRequest(
			new(edited.Snapshot!.DocumentKey, edited.Snapshot.DocumentId, edited.Snapshot.Version),
			edited.Snapshot.OnDiskStamp));

		Assert.AreEqual(WorkspaceDocumentCommitOutcome.Committed, committed.Outcome);

		byte[] bytes = File.ReadAllBytes(filePath);
		Assert.IsTrue(bytes.Length > 3);
		Assert.AreEqual((byte)0xEF, bytes[0]);
		Assert.AreEqual((byte)0xBB, bytes[1]);
		Assert.AreEqual((byte)0xBF, bytes[2]);
		Assert.AreEqual("two", WorkspaceTextCodec.Decode(bytes, TextEncodingKind.Utf8, out _));
	}

	[TestMethod]
	public async Task RenameDirectory_MovesFilesAndRebasesTrackedDocuments()
	{
		using var temp = new TemporaryDirectory();
		string sourceDirectory = Path.Combine(temp.Path, "folder");
		Directory.CreateDirectory(sourceDirectory);
		string firstPath = Path.Combine(sourceDirectory, "one.lua");
		string secondPath = Path.Combine(sourceDirectory, "two.lua");
		File.WriteAllText(firstPath, "one");
		File.WriteAllText(secondPath, "two");
		string destinationDirectory = Path.Combine(temp.Path, "moved");

		await using var store = new WorkspaceDocumentStore(new LocalWorkspaceFileSystem());
		WorkspaceDocumentOpenOptions options = new(TextEncodingKind.Utf8, TestSnapshots.FileFormat);
		WorkspaceDocumentSnapshot first = (await store.OpenAsync(firstPath, options)).Snapshot!;
		WorkspaceDocumentSnapshot second = (await store.OpenAsync(secondPath, options)).Snapshot!;

		WorkspaceDocumentDirectoryRenameResult result = await store.RenameDirectoryAsync(
			new WorkspaceDocumentDirectoryRenameRequest(sourceDirectory, destinationDirectory));

		// The files really moved and every tracked descendant was rebased onto the destination.
		Assert.AreEqual(WorkspaceDocumentDirectoryRenameOutcome.Renamed, result.Outcome);
		Assert.IsFalse(File.Exists(firstPath));
		Assert.AreEqual("one", File.ReadAllText(Path.Combine(destinationDirectory, "one.lua")));
		Assert.AreEqual("two", File.ReadAllText(Path.Combine(destinationDirectory, "two.lua")));
		Assert.AreEqual(2, result.Snapshots.Count);

		string movedFirstId = Path.Combine(destinationDirectory, "one.lua");
		Assert.IsFalse(store.TryGetSnapshot(firstPath, out _));
		Assert.IsTrue(store.TryGetSnapshot(movedFirstId, out WorkspaceDocumentSnapshot? movedFirst));
		Assert.AreEqual(first.DocumentKey, movedFirst!.DocumentKey);
		Assert.AreEqual(first.Content, movedFirst.Content);

		string movedSecondId = Path.Combine(destinationDirectory, "two.lua");
		Assert.IsTrue(store.TryGetSnapshot(movedSecondId, out WorkspaceDocumentSnapshot? movedSecond));
		Assert.AreEqual(second.DocumentKey, movedSecond!.DocumentKey);
	}
}
