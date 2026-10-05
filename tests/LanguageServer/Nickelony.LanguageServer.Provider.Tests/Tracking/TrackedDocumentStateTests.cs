namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class TrackedDocumentStateTests
{
	[TestMethod]
	public void CreateSnapshot_CapturesCurrentDocumentState()
	{
		var state = new TrackedDocumentState(
			new TrackedDocumentDescriptor(
				FilePath: @"C:\Workspace\Scripts\start.ext",
				Uri: "file:///C:/Workspace/Scripts/start.ext",
				Content: "return 1",
				Version: 4,
				IsOpen: true,
				OpenLeaseCount: 1,
				RequestLeaseCount: 2,
				LastAccessStamp: 3
			)
		);

		DocumentSnapshot initialSnapshot = state.CreateSnapshot();

		state.RenameDocument(@"C:\Workspace\Scripts\renamed.ext", "file:///C:/Workspace/Scripts/renamed.ext");
		string previousContent = state.ReplaceContent("return 2");
		state.MarkDocumentClosed();

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
	public async Task CreateSnapshot_DoesNotMixPathAndUriDuringConcurrentMove()
	{
		var state = new TrackedDocumentState(
			new TrackedDocumentDescriptor(
				FilePath: @"C:\Workspace\Scripts\a.ext",
				Uri: "file:///C:/Workspace/Scripts/a.ext",
				Content: "return 'a'",
				Version: 1,
				IsOpen: true,
				OpenLeaseCount: 0,
				RequestLeaseCount: 0,
				LastAccessStamp: 0
			)
		);

		var mismatchMessages = new List<string>();
		int writerFinished = 0;

		// Iteration-based stress loop: coverage does not depend on wall-clock time.
		var writerTask = Task.Run(
			() =>
			{
				for (int i = 0; i < 10_000; i++)
				{
					state.RenameDocument(@"C:\Workspace\Scripts\a.ext", "file:///C:/Workspace/Scripts/a.ext");
					state.ReplaceContent("return 'a'");
					state.RenameDocument(@"C:\Workspace\Scripts\b.ext", "file:///C:/Workspace/Scripts/b.ext");
					state.ReplaceContent("return 'b'");
				}

				Volatile.Write(ref writerFinished, 1);
			},

			TestContext.CancellationToken
		);

		var readerTask = Task.Run(
			() =>
			{
				while (Volatile.Read(ref writerFinished) == 0)
					CheckSnapshotPairing(state, mismatchMessages);

				CheckSnapshotPairing(state, mismatchMessages);
			},

			TestContext.CancellationToken
		);

		await Task.WhenAll(writerTask, readerTask).ConfigureAwait(false);

		Assert.IsEmpty(mismatchMessages, "Snapshots should keep each file path paired with its URI: " + string.Join(", ", mismatchMessages));
	}

	private static void CheckSnapshotPairing(TrackedDocumentState state, List<string> mismatchMessages)
	{
		DocumentSnapshot snapshot = state.CreateSnapshot();

		bool isA =
			string.Equals(snapshot.FilePath, @"C:\Workspace\Scripts\a.ext", StringComparison.Ordinal)
			&& string.Equals(snapshot.Uri, "file:///C:/Workspace/Scripts/a.ext", StringComparison.Ordinal);

		bool isB =
			string.Equals(snapshot.FilePath, @"C:\Workspace\Scripts\b.ext", StringComparison.Ordinal)
			&& string.Equals(snapshot.Uri, "file:///C:/Workspace/Scripts/b.ext", StringComparison.Ordinal);

		if (!isA && !isB)
		{
			lock (mismatchMessages)
				mismatchMessages.Add(snapshot.FilePath + " | " + snapshot.Uri);
		}
	}

	public TestContext? TestContext { get; set; }
}
