using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>Covers the snapshot reader directly: the lookup by path, the subtree listing and its order.</summary>
[TestClass]
public sealed class DocumentSnapshotReaderTests
{
	private static readonly string s_root = Path.Combine(Path.GetTempPath(), "reader-tests");

	private static string PathOf(params string[] segments) => Path.Combine([s_root, .. segments]);

	[TestMethod]
	public void TryGetSnapshot_ForATrackedPath_ReturnsTheSnapshot()
	{
		var host = new StoreCollaboratorsTestHost();
		host.AddDocument(PathOf("a.txt"));

		bool found = host.Reader.TryGetSnapshot(PathOf("a.txt"), out WorkspaceDocumentSnapshot? snapshot);

		Assert.IsTrue(found);
		Assert.AreEqual(PathOf("a.txt"), snapshot?.DocumentId);
	}

	[TestMethod]
	public void TryGetSnapshot_ForAnUntrackedOrBlankPath_ReturnsFalse()
	{
		var host = new StoreCollaboratorsTestHost();

		Assert.IsFalse(host.Reader.TryGetSnapshot(PathOf("missing.txt"), out WorkspaceDocumentSnapshot? missing));
		Assert.IsNull(missing);
		Assert.IsFalse(host.Reader.TryGetSnapshot(" ", out _));
		Assert.IsFalse(host.Reader.TryGetSnapshot(null, out _));
	}

	[TestMethod]
	public void GetSnapshotsUnderDirectory_ReturnsTheDirectoryAndItsDescendantsInIdOrder()
	{
		var host = new StoreCollaboratorsTestHost();

		host.AddDocument(PathOf("src", "b.txt"));
		host.AddDocument(PathOf("src", "sub", "c.txt"));
		host.AddDocument(PathOf("src", "a.txt"));
		host.AddDocument(PathOf("srcother", "d.txt"));

		IReadOnlyList<WorkspaceDocumentSnapshot> snapshots = host.Reader.GetSnapshotsUnderDirectory(PathOf("src"));

		Assert.AreSequenceEqual(
			[PathOf("src", "a.txt"), PathOf("src", "b.txt"), PathOf("src", "sub", "c.txt")],
			snapshots.Select(snapshot => snapshot.DocumentId)
		);
	}

	[TestMethod]
	public void GetSnapshotsUnderDirectory_ForABlankPath_ReturnsNothing()
	{
		var host = new StoreCollaboratorsTestHost();
		host.AddDocument(PathOf("a.txt"));

		Assert.IsEmpty(host.Reader.GetSnapshotsUnderDirectory(null));
		Assert.IsEmpty(host.Reader.GetSnapshotsUnderDirectory(" "));
	}

	[TestMethod]
	public void Members_AfterTheStopBegan_ThrowObjectDisposedBeforeLookingAtThePath()
	{
		var host = new StoreCollaboratorsTestHost();
		host.Stop();

		Assert.ThrowsExactly<ObjectDisposedException>(() => host.Reader.TryGetSnapshot(" ", out _));
		Assert.ThrowsExactly<ObjectDisposedException>(() => host.Reader.GetSnapshotsUnderDirectory(" "));
	}
}
