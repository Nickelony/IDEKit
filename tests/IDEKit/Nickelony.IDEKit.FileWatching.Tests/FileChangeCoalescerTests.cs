namespace Nickelony.IDEKit.FileWatching.Tests;

[TestClass]
public sealed class FileChangeCoalescerTests
{
	[TestMethod]
	public void Add_DeleteThenCreateForSamePath_PreservesDeleteThenCreatePair()
	{
		var coalescer = new FileChangeCoalescer();

		coalescer.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Deleted);
		coalescer.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Created);

		IReadOnlyList<FileChange> drainedChanges = coalescer.DrainBatch().Entries;

		Assert.HasCount(2, drainedChanges);
		Assert.AreEqual(@"C:\Workspace\Scripts\test.ext", drainedChanges[0].Path);
		Assert.AreEqual(FileChangeKind.Deleted, drainedChanges[0].Kind);
		Assert.AreEqual(@"C:\Workspace\Scripts\test.ext", drainedChanges[1].Path);
		Assert.AreEqual(FileChangeKind.Created, drainedChanges[1].Kind);
		Assert.IsTrue(coalescer.IsEmpty);
	}

	[TestMethod]
	public void Add_CreatedThenChangedForSamePath_PreservesCreated()
	{
		var coalescer = new FileChangeCoalescer();

		coalescer.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Created);
		coalescer.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed);

		IReadOnlyList<FileChange> drainedChanges = coalescer.DrainBatch().Entries;

		Assert.HasCount(1, drainedChanges);
		Assert.AreEqual(FileChangeKind.Created, drainedChanges[0].Kind);
	}

	[TestMethod]
	public void Add_CreatedThenDeletedForSamePath_RemovesBufferedEntry()
	{
		var coalescer = new FileChangeCoalescer();

		coalescer.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Created);
		coalescer.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Deleted);

		IReadOnlyList<FileChange> drainedChanges = coalescer.DrainBatch().Entries;

		Assert.IsEmpty(drainedChanges);
		Assert.IsTrue(coalescer.IsEmpty);
	}

	[TestMethod]
	public void Add_CreatedChangedThenDeletedForSamePath_RemovesBufferedEntry()
	{
		var coalescer = new FileChangeCoalescer();

		coalescer.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Created);
		coalescer.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Changed);
		coalescer.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Deleted);

		IReadOnlyList<FileChange> drainedChanges = coalescer.DrainBatch().Entries;

		Assert.IsEmpty(drainedChanges);
		Assert.IsTrue(coalescer.IsEmpty);
	}

	[TestMethod]
	public void Add_CasingVariants_PreservesBothChanges()
	{
		var coalescer = new FileChangeCoalescer();

		coalescer.Add(@"C:\Workspace\Scripts\Test.ext", FileChangeKind.Deleted);
		coalescer.Add(@"c:\workspace\scripts\test.ext", FileChangeKind.Created);

		IReadOnlyList<FileChange> drainedChanges = coalescer.DrainBatch().Entries;

		Assert.HasCount(2, drainedChanges);
		Assert.AreEqual(@"C:\Workspace\Scripts\Test.ext", drainedChanges[0].Path);
		Assert.AreEqual(FileChangeKind.Deleted, drainedChanges[0].Kind);
		Assert.AreEqual(@"c:\workspace\scripts\test.ext", drainedChanges[1].Path);
		Assert.AreEqual(FileChangeKind.Created, drainedChanges[1].Kind);
	}

	[TestMethod]
	public void Add_UnderACaseInsensitiveComparison_CoalescesCaseVariantsIntoOneChange()
	{
		var coalescer = new FileChangeCoalescer(LocalPathComparisonPolicy.CaseInsensitive);

		coalescer.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Created);
		coalescer.Add(@"C:\Workspace\Scripts\Test.ext", FileChangeKind.Changed);

		IReadOnlyList<FileChange> drainedChanges = coalescer.DrainBatch().Entries;

		Assert.HasCount(1, drainedChanges);
		Assert.AreEqual(@"C:\Workspace\Scripts\Test.ext", drainedChanges[0].Path);
		Assert.AreEqual(FileChangeKind.Created, drainedChanges[0].Kind);
	}

	[TestMethod]
	public void Add_UnderACaseSensitiveComparison_KeepsCaseVariantsAsSeparateChanges()
	{
		var coalescer = new FileChangeCoalescer(LocalPathComparisonPolicy.CaseSensitive);

		coalescer.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Created);
		coalescer.Add(@"C:\Workspace\Scripts\Test.ext", FileChangeKind.Changed);

		IReadOnlyList<FileChange> drainedChanges = coalescer.DrainBatch().Entries;

		Assert.HasCount(2, drainedChanges);
		Assert.AreEqual(@"C:\Workspace\Scripts\test.ext", drainedChanges[0].Path);
		Assert.AreEqual(FileChangeKind.Created, drainedChanges[0].Kind);
		Assert.AreEqual(@"C:\Workspace\Scripts\Test.ext", drainedChanges[1].Path);
		Assert.AreEqual(FileChangeKind.Changed, drainedChanges[1].Kind);
	}

	[TestMethod]
	public void Add_CreatedThenDeletedForDifferentCasing_PreservesBothOccurrences()
	{
		// A case-only replacement must stay observable: the earlier create and the later delete belong to different
		// casings and must not cancel each other out on case-insensitive hosts.
		var coalescer = new FileChangeCoalescer();

		coalescer.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Created);
		coalescer.Add(@"C:\Workspace\Scripts\Test.ext", FileChangeKind.Deleted);

		IReadOnlyList<FileChange> drainedChanges = coalescer.DrainBatch().Entries;

		Assert.HasCount(2, drainedChanges);
		Assert.AreEqual(@"C:\Workspace\Scripts\test.ext", drainedChanges[0].Path);
		Assert.AreEqual(FileChangeKind.Created, drainedChanges[0].Kind);
		Assert.AreEqual(@"C:\Workspace\Scripts\Test.ext", drainedChanges[1].Path);
		Assert.AreEqual(FileChangeKind.Deleted, drainedChanges[1].Kind);
	}

	[TestMethod]
	public void Add_CreatedThenDeletedForDifferentCasingThenCreated_DropsSupersededDelete()
	{
		// The third event recreates the path, so forwarding the pending delete of the previous casing would report a
		// removal the server must not apply.
		var coalescer = new FileChangeCoalescer();

		coalescer.Add(@"C:\Workspace\Scripts\test.ext", FileChangeKind.Created);
		coalescer.Add(@"C:\Workspace\Scripts\Test.ext", FileChangeKind.Deleted);
		coalescer.Add(@"C:\Workspace\Scripts\Test.ext", FileChangeKind.Created);

		IReadOnlyList<FileChange> drainedChanges = coalescer.DrainBatch().Entries;

		Assert.HasCount(1, drainedChanges);
		Assert.AreEqual(@"C:\Workspace\Scripts\Test.ext", drainedChanges[0].Path);
		Assert.AreEqual(FileChangeKind.Created, drainedChanges[0].Kind);
	}

	[TestMethod]
	public void DrainBatch_ClearsBufferedEntries()
	{
		var coalescer = new FileChangeCoalescer();

		coalescer.Add(@"C:\Workspace\Scripts\first.ext", FileChangeKind.Changed);
		coalescer.Add(@"C:\Workspace\Scripts\second.ext", FileChangeKind.Deleted);

		FileChangeBatch drainedBatch = coalescer.DrainBatch();
		FileChangeBatch drainedAgain = coalescer.DrainBatch();

		Assert.AreEqual(2, drainedBatch.Count);
		Assert.AreEqual(0, drainedAgain.Count);
		Assert.IsTrue(coalescer.IsEmpty);
	}

	[TestMethod]
	public void DrainBatch_EntriesAreExposedAsReadOnlyView()
	{
		var coalescer = new FileChangeCoalescer();
		coalescer.Add(@"C:\Workspace\Scripts\first.ext", FileChangeKind.Changed);

		FileChangeBatch drainedBatch = coalescer.DrainBatch();

		Assert.ThrowsExactly<NotSupportedException>(() =>
			((IList<FileChange>)drainedBatch.Entries)[0] = new FileChange(@"C:\Workspace\Scripts\second.ext", FileChangeKind.Deleted)
		);
	}

	[TestMethod]
	public void DrainBatch_MultiplePaths_PreservesFirstPendingOccurrenceOrder()
	{
		var coalescer = new FileChangeCoalescer();

		coalescer.Add(@"C:\Workspace\Scripts\first.ext", FileChangeKind.Created);
		coalescer.Add(@"C:\Workspace\Scripts\second.ext", FileChangeKind.Changed);
		coalescer.Add(@"C:\Workspace\Scripts\first.ext", FileChangeKind.Changed);
		coalescer.Add(@"C:\Workspace\Scripts\third.ext", FileChangeKind.Deleted);

		IReadOnlyList<FileChange> drainedChanges = coalescer.DrainBatch().Entries;

		Assert.AreSequenceEqual(
			[@"C:\Workspace\Scripts\first.ext", @"C:\Workspace\Scripts\second.ext", @"C:\Workspace\Scripts\third.ext"],
			[.. drainedChanges.Select(change => change.Path)]
		);

		Assert.AreSequenceEqual(
			[FileChangeKind.Created, FileChangeKind.Changed, FileChangeKind.Deleted],
			[.. drainedChanges.Select(change => change.Kind)]
		);
	}

	[TestMethod]
	public void DrainBatch_PathRemovedAndReadded_GetsNewPendingOrder()
	{
		var coalescer = new FileChangeCoalescer();

		coalescer.Add(@"C:\Workspace\Scripts\first.ext", FileChangeKind.Created);
		coalescer.Add(@"C:\Workspace\Scripts\first.ext", FileChangeKind.Deleted);
		coalescer.Add(@"C:\Workspace\Scripts\second.ext", FileChangeKind.Changed);
		coalescer.Add(@"C:\Workspace\Scripts\first.ext", FileChangeKind.Created);

		IReadOnlyList<FileChange> drainedChanges = coalescer.DrainBatch().Entries;

		Assert.AreSequenceEqual(
			[@"C:\Workspace\Scripts\second.ext", @"C:\Workspace\Scripts\first.ext"],
			[.. drainedChanges.Select(change => change.Path)]
		);
	}
}
