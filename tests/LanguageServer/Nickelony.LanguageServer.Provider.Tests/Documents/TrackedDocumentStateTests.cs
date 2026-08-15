namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class TrackedDocumentStateTests
{
	[TestMethod]
	public void CreateSnapshot_CapturesCurrentDocumentState()
	{
		var state = new TestTrackedDocumentState(new TrackedDocumentInitialState(
			@"C:\Workspace\Scripts\start.ext",
			"file:///C:/Workspace/Scripts/start.ext",
			"return 1",
			Version: 4,
			IsOpen: true,
			OpenReferenceCount: 1,
			RequestReferenceCount: 2,
			LastAccessStamp: 3));

		DocumentSnapshot initialSnapshot = state.CreateSnapshot();

		state.Rename(@"C:\Workspace\Scripts\renamed.ext", "file:///C:/Workspace/Scripts/renamed.ext");
		string previousContent = state.Update("return 2");
		state.Close();

		DocumentSnapshot updatedSnapshot = state.CreateSnapshot();

		Assert.AreEqual(@"C:\Workspace\Scripts\start.ext", initialSnapshot.FilePath);
		Assert.AreEqual("file:///C:/Workspace/Scripts/start.ext", initialSnapshot.Uri);
		Assert.AreEqual("return 1", initialSnapshot.Content);
		Assert.AreEqual(4, initialSnapshot.Version);
		Assert.AreEqual("return 1", previousContent);
		Assert.AreEqual(@"C:\Workspace\Scripts\renamed.ext", updatedSnapshot.FilePath);
		Assert.AreEqual("file:///C:/Workspace/Scripts/renamed.ext", updatedSnapshot.Uri);
		Assert.AreEqual("return 2", updatedSnapshot.Content);
		Assert.AreEqual(5, updatedSnapshot.Version);
		Assert.IsFalse(state.IsOpen);
	}

	[TestMethod]
	public async Task CreateSnapshot_DoesNotMixPathAndUriDuringConcurrentRename()
	{
		var state = new TestTrackedDocumentState(new TrackedDocumentInitialState(
			@"C:\Workspace\Scripts\a.ext",
			"file:///C:/Workspace/Scripts/a.ext",
			"return 'a'",
			Version: 1,
			IsOpen: true,
			OpenReferenceCount: 0,
			RequestReferenceCount: 0,
			LastAccessStamp: 0));

		var mismatchMessages = new List<string>();
		int writerFinished = 0;

		// Iteration-based stress loop instead of a wall-clock window so slow CI machines do not reduce coverage.
		Task writerTask = Task.Run(() =>
		{
			for (int i = 0; i < 10_000; i++)
			{
				state.Rename(@"C:\Workspace\Scripts\a.ext", "file:///C:/Workspace/Scripts/a.ext");
				state.Update("return 'a'");
				state.Rename(@"C:\Workspace\Scripts\b.ext", "file:///C:/Workspace/Scripts/b.ext");
				state.Update("return 'b'");
			}

			Volatile.Write(ref writerFinished, 1);
		});

		Task readerTask = Task.Run(() =>
		{
			while (Volatile.Read(ref writerFinished) == 0)
			{
				CheckSnapshotPairing(state, mismatchMessages);
			}

			CheckSnapshotPairing(state, mismatchMessages);
		});

		await Task.WhenAll(writerTask, readerTask).ConfigureAwait(false);

		Assert.AreEqual(0, mismatchMessages.Count,
			"Snapshots should keep each file path paired with its URI: " + string.Join(", ", mismatchMessages));
	}

	private static void CheckSnapshotPairing(TestTrackedDocumentState state, List<string> mismatchMessages)
	{
		DocumentSnapshot snapshot = state.CreateSnapshot();

		bool isA = string.Equals(snapshot.FilePath, @"C:\Workspace\Scripts\a.ext", StringComparison.Ordinal)
			&& string.Equals(snapshot.Uri, "file:///C:/Workspace/Scripts/a.ext", StringComparison.Ordinal);

		bool isB = string.Equals(snapshot.FilePath, @"C:\Workspace\Scripts\b.ext", StringComparison.Ordinal)
			&& string.Equals(snapshot.Uri, "file:///C:/Workspace/Scripts/b.ext", StringComparison.Ordinal);

		if (!isA && !isB)
		{
			lock (mismatchMessages)
				mismatchMessages.Add(snapshot.FilePath + " | " + snapshot.Uri);
		}
	}
}
