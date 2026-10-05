namespace Nickelony.IDEKit.FileWatching.Tests;

[TestClass]
public sealed class FileChangeBufferTests
{
	private static FileChange Change(string fileName, FileChangeKind kind) => new(TestPaths.Script(fileName), kind);

	[TestMethod]
	public void IsEmpty_HoldsUntilAnyBufferReceivesAChange()
	{
		var pendingOnly = new FileChangeBuffer();
		var replayedOnly = new FileChangeBuffer();

		Assert.IsTrue(pendingOnly.IsEmpty);

		pendingOnly.AddPending(TestPaths.Script("a.test"), FileChangeKind.Changed);
		replayedOnly.AddReplayedRange([Change("a.test", FileChangeKind.Changed)]);

		Assert.IsFalse(pendingOnly.IsEmpty);
		Assert.IsFalse(replayedOnly.IsEmpty);
	}

	[TestMethod]
	public void Count_CountsEachBufferedPathOncePerBuffer()
	{
		var buffer = new FileChangeBuffer();

		buffer.AddPendingRange([Change("a.test", FileChangeKind.Changed), Change("b.test", FileChangeKind.Created)]);
		buffer.AddPending(TestPaths.Script("a.test"), FileChangeKind.Changed);
		buffer.AddReplayedRange([Change("a.test", FileChangeKind.Deleted)]);

		Assert.AreEqual(3, buffer.Count);

		buffer.DrainBatch();

		Assert.AreEqual(0, buffer.Count);
	}

	[TestMethod]
	public void DrainBatch_WithOnlyPendingChanges_ReturnsThemInFirstPendingOrderAndEmptiesTheBuffer()
	{
		var buffer = new FileChangeBuffer();

		buffer.AddPendingRange([Change("b.test", FileChangeKind.Created), Change("a.test", FileChangeKind.Changed)]);
		buffer.AddPending(TestPaths.Script("b.test"), FileChangeKind.Changed);

		FileChangeBatch batch = buffer.DrainBatch();

		Assert.AreSequenceEqual([Change("b.test", FileChangeKind.Created), Change("a.test", FileChangeKind.Changed)], [.. batch.Entries]);
		Assert.IsTrue(buffer.IsEmpty);
		Assert.AreEqual(0, buffer.DrainBatch().Count);
	}

	[TestMethod]
	public void DrainBatch_WithOnlyReplayedChanges_KeepsTheDrainedOrder()
	{
		var buffer = new FileChangeBuffer();
		var drained = new FileChangeBatch([Change("z.test", FileChangeKind.Created), Change("a.test", FileChangeKind.Deleted)]);

		buffer.AddReplayed(drained);

		Assert.AreSequenceEqual([.. drained.Entries], [.. buffer.DrainBatch().Entries]);
		Assert.IsTrue(buffer.IsEmpty);
	}

	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public void DrainBatch_PutsReplayedChangesAheadOfNewerPendingChanges(bool replayAsBatch)
	{
		var buffer = new FileChangeBuffer();
		FileChange replayed = Change("replayed.test", FileChangeKind.Created);

		buffer.AddPending(TestPaths.Script("pending.test"), FileChangeKind.Changed);

		if (replayAsBatch)
			buffer.AddReplayed(new FileChangeBatch([replayed]));
		else
			buffer.AddReplayedRange([replayed]);

		FileChangeBatch batch = buffer.DrainBatch();

		Assert.AreSequenceEqual(
			[Change("replayed.test", FileChangeKind.Created), Change("pending.test", FileChangeKind.Changed)],
			[.. batch.Entries]
		);

		Assert.IsTrue(buffer.IsEmpty);
	}

	[TestMethod]
	public void DrainBatch_AReplayedDeleteAndANewerPendingCreateOfOnePath_AreForwardedInOrderInsteadOfCancelingOut()
	{
		var buffer = new FileChangeBuffer();

		buffer.AddReplayed(new FileChangeBatch([Change("a.test", FileChangeKind.Deleted)]));
		buffer.AddPending(TestPaths.Script("a.test"), FileChangeKind.Created);

		Assert.AreSequenceEqual(
			[Change("a.test", FileChangeKind.Deleted), Change("a.test", FileChangeKind.Created)],
			[.. buffer.DrainBatch().Entries]
		);
	}

	[TestMethod]
	public void AddPending_ForPathsThatDifferOnlyInCase_FollowsTheConfiguredPathComparison()
	{
		var caseSensitive = new FileChangeBuffer(LocalPathComparisonPolicy.CaseSensitive);

		caseSensitive.AddPending(TestPaths.Script("a.test"), FileChangeKind.Changed);
		caseSensitive.AddPending(TestPaths.Script("A.test"), FileChangeKind.Changed);

		Assert.AreEqual(2, caseSensitive.DrainBatch().Count);

		var caseInsensitive = new FileChangeBuffer(LocalPathComparisonPolicy.CaseInsensitive);

		caseInsensitive.AddPending(TestPaths.Script("a.test"), FileChangeKind.Changed);
		caseInsensitive.AddPending(TestPaths.Script("A.test"), FileChangeKind.Changed);

		Assert.AreEqual(1, caseInsensitive.DrainBatch().Count);
	}
}
