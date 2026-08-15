namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class WorkspaceChangeAccumulatorTests
{
	[TestMethod]
	public void Add_DeleteThenCreateForSamePath_PreservesDeleteThenCreatePair()
	{
		var accumulator = new WorkspaceChangeAccumulator();
		accumulator.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Deleted);
		accumulator.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Created);

		IReadOnlyList<WorkspaceFileChange> drainedChanges = accumulator.DrainBatch().Entries;

		Assert.AreEqual(2, drainedChanges.Count);
		Assert.AreEqual(@"C:\Workspace\Scripts\test.ext", drainedChanges[0].Path);
		Assert.AreEqual(FileChangeKind.Deleted, drainedChanges[0].Kind);
		Assert.AreEqual(@"C:\Workspace\Scripts\test.ext", drainedChanges[1].Path);
		Assert.AreEqual(FileChangeKind.Created, drainedChanges[1].Kind);
		Assert.IsTrue(accumulator.IsEmpty);
	}

	[TestMethod]
	public void Add_CreatedThenChangedForSamePath_PreservesCreated()
	{
		var accumulator = new WorkspaceChangeAccumulator();
		accumulator.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Created);
		accumulator.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed);

		IReadOnlyList<WorkspaceFileChange> drainedChanges = accumulator.DrainBatch().Entries;

		Assert.AreEqual(1, drainedChanges.Count);
		Assert.AreEqual(FileChangeKind.Created, drainedChanges[0].Kind);
	}

	[TestMethod]
	public void Add_CreatedThenDeletedForSamePath_RemovesBufferedEntry()
	{
		var accumulator = new WorkspaceChangeAccumulator();
		accumulator.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Created);
		accumulator.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Deleted);

		IReadOnlyList<WorkspaceFileChange> drainedChanges = accumulator.DrainBatch().Entries;

		Assert.AreEqual(0, drainedChanges.Count);
		Assert.IsTrue(accumulator.IsEmpty);
	}

	[TestMethod]
	public void Add_CreatedChangedThenDeletedForSamePath_RemovesBufferedEntry()
	{
		var accumulator = new WorkspaceChangeAccumulator();
		accumulator.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Created);
		accumulator.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed);
		accumulator.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Deleted);

		IReadOnlyList<WorkspaceFileChange> drainedChanges = accumulator.DrainBatch().Entries;

		Assert.AreEqual(0, drainedChanges.Count);
		Assert.IsTrue(accumulator.IsEmpty);
	}

	[TestMethod]
	public void Add_CasingVariants_PreservesBothChanges()
	{
		var accumulator = new WorkspaceChangeAccumulator();
		accumulator.Add(@"C:\Workspace\Scripts\Test.ext", FileChangeKind.Deleted);
		accumulator.Add(@"c:\workspace\scripts\test.ext", FileChangeKind.Created);

		IReadOnlyList<WorkspaceFileChange> drainedChanges = accumulator.DrainBatch().Entries;

		Assert.AreEqual(2, drainedChanges.Count);
		Assert.AreEqual(@"C:\Workspace\Scripts\Test.ext", drainedChanges[0].Path);
		Assert.AreEqual(FileChangeKind.Deleted, drainedChanges[0].Kind);
		Assert.AreEqual(@"c:\workspace\scripts\test.ext", drainedChanges[1].Path);
		Assert.AreEqual(FileChangeKind.Created, drainedChanges[1].Kind);
	}

	[TestMethod]
	public void Add_CreatedThenDeletedForDifferentCasing_PreservesBothOccurrences()
	{
		// A case-only replacement must stay observable: the earlier create and the later delete belong to different
		// casings and must not cancel each other out on case-insensitive hosts.
		var accumulator = new WorkspaceChangeAccumulator();
		accumulator.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Created);
		accumulator.Add(@"C:\Workspace\Scripts\Test.ext", FileChangeKind.Deleted);

		IReadOnlyList<WorkspaceFileChange> drainedChanges = accumulator.DrainBatch().Entries;

		Assert.AreEqual(2, drainedChanges.Count);
		Assert.AreEqual(@"C:\Workspace\Scripts\test.ext", drainedChanges[0].Path);
		Assert.AreEqual(FileChangeKind.Created, drainedChanges[0].Kind);
		Assert.AreEqual(@"C:\Workspace\Scripts\Test.ext", drainedChanges[1].Path);
		Assert.AreEqual(FileChangeKind.Deleted, drainedChanges[1].Kind);
	}

	[TestMethod]
	public void DrainBatch_ClearsBufferedEntries()
	{
		var accumulator = new WorkspaceChangeAccumulator();
		accumulator.Add(@"C:\Workspace\Scripts\first.ext", FileChangeKind.Changed);
		accumulator.Add(@"C:\Workspace\Scripts\second.ext", FileChangeKind.Deleted);

		FileChangeBatch drainedBatch = accumulator.DrainBatch();
		FileChangeBatch drainedAgain = accumulator.DrainBatch();

		Assert.AreEqual(2, drainedBatch.Count);
		Assert.AreEqual(0, drainedAgain.Count);
		Assert.IsTrue(accumulator.IsEmpty);
	}

	[TestMethod]
	public void DrainBatch_EntriesAreExposedAsReadOnlyView()
	{
		var accumulator = new WorkspaceChangeAccumulator();
		accumulator.Add(@"C:\Workspace\Scripts\first.ext", FileChangeKind.Changed);

		FileChangeBatch drainedBatch = accumulator.DrainBatch();

		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<WorkspaceFileChange>)drainedBatch.Entries)[0] =
			new WorkspaceFileChange(@"C:\Workspace\Scripts\second.ext", FileChangeKind.Deleted));
	}

	[TestMethod]
	public void DrainBatch_MultiplePaths_PreservesFirstPendingOccurrenceOrder()
	{
		var accumulator = new WorkspaceChangeAccumulator();
		accumulator.Add(@"C:\Workspace\Scripts\first.ext", FileChangeKind.Created);
		accumulator.Add(@"C:\Workspace\Scripts\second.ext", FileChangeKind.Changed);
		accumulator.Add(@"C:\Workspace\Scripts\first.ext", FileChangeKind.Changed);
		accumulator.Add(@"C:\Workspace\Scripts\third.ext", FileChangeKind.Deleted);

		IReadOnlyList<WorkspaceFileChange> drainedChanges = accumulator.DrainBatch().Entries;

		CollectionAssert.AreEqual(
			new[]
			{
				@"C:\Workspace\Scripts\first.ext",
				@"C:\Workspace\Scripts\second.ext",
				@"C:\Workspace\Scripts\third.ext"
			},
			drainedChanges.Select(change => change.Path).ToArray());

		CollectionAssert.AreEqual(
			new[]
			{
				FileChangeKind.Created,
				FileChangeKind.Changed,
				FileChangeKind.Deleted
			},
			drainedChanges.Select(change => change.Kind).ToArray());
	}

	[TestMethod]
	public void DrainBatch_PathRemovedAndReadded_GetsNewPendingOrder()
	{
		var accumulator = new WorkspaceChangeAccumulator();
		accumulator.Add(@"C:\Workspace\Scripts\first.ext", FileChangeKind.Created);
		accumulator.Add(@"C:\Workspace\Scripts\first.ext", FileChangeKind.Deleted);
		accumulator.Add(@"C:\Workspace\Scripts\second.ext", FileChangeKind.Changed);
		accumulator.Add(@"C:\Workspace\Scripts\first.ext", FileChangeKind.Created);

		IReadOnlyList<WorkspaceFileChange> drainedChanges = accumulator.DrainBatch().Entries;

		CollectionAssert.AreEqual(
			new[]
			{
				@"C:\Workspace\Scripts\second.ext",
				@"C:\Workspace\Scripts\first.ext"
			},
			drainedChanges.Select(change => change.Path).ToArray());
	}
}
