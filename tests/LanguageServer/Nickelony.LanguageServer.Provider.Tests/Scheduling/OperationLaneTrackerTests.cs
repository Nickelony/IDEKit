namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the lane table of the scheduler: which lane an operation joins, when a lane is created and when it is removed.</summary>
[TestClass]
public sealed class OperationLaneTrackerTests
{
	private static readonly string s_first = TestPaths.Script("first.test");
	private static readonly string s_second = TestPaths.Script("second.test");

	private sealed class TestHost
	{
		internal TestHost(LocalPathComparisonPolicy? pathComparison = null) =>
			Tracker = new OperationLaneTracker(SyncRoot, pathComparison ?? LocalPathComparisonPolicy.CaseSensitive);

		internal object SyncRoot { get; } = new();

		internal OperationLaneTracker Tracker { get; }

		internal OperationLaneTracker.OperationLane[] Capture(string[] paths, bool joinsExclusiveLane = false)
		{
			lock (SyncRoot)
				return Tracker.Capture(paths, joinsExclusiveLane);
		}

		internal void Complete(OperationLaneTracker.OperationLane lane, OperationLaneTracker.LaneNode node)
		{
			lock (SyncRoot)
				Tracker.Complete(lane, node);
		}
	}

	[TestMethod]
	public void Capture_CreatesOneLanePerPathAndReusesItForTheNextNode()
	{
		var fixture = new TestHost();

		OperationLaneTracker.OperationLane[] firstCapture = fixture.Capture([s_first, s_second]);
		OperationLaneTracker.OperationLane[] secondCapture = fixture.Capture([s_first]);

		Assert.HasCount(2, firstCapture);
		Assert.AreNotSame(firstCapture[0], firstCapture[1]);
		Assert.AreSame(firstCapture[0], secondCapture[0]);
		Assert.AreEqual(s_first, firstCapture[0]._normalizedFilePath);
		Assert.AreEqual(s_second, firstCapture[1]._normalizedFilePath);
		Assert.AreEqual(2, fixture.Tracker.DocumentLaneCount);
	}

	[TestMethod]
	public void Capture_ForPathsThatDifferOnlyInCase_SharesOneLaneUnderACaseInsensitiveComparison()
	{
		var insensitive = new TestHost(LocalPathComparisonPolicy.CaseInsensitive);
		var sensitive = new TestHost(LocalPathComparisonPolicy.CaseSensitive);

		string lower = TestPaths.Script("case.test");
		string upper = TestPaths.Script("CASE.test");

		Assert.AreSame(insensitive.Capture([lower])[0], insensitive.Capture([upper])[0]);
		Assert.AreNotSame(sensitive.Capture([lower])[0], sensitive.Capture([upper])[0]);
	}

	[TestMethod]
	public void Capture_PutsTheExclusiveLaneFirstOnlyWhenTheNodeJoinsIt()
	{
		var fixture = new TestHost();

		OperationLaneTracker.OperationLane[] exclusive = fixture.Capture([s_first, s_second], joinsExclusiveLane: true);
		OperationLaneTracker.OperationLane[] again = fixture.Capture([s_first], joinsExclusiveLane: true);
		OperationLaneTracker.OperationLane[] plain = fixture.Capture([s_first], joinsExclusiveLane: false);

		Assert.HasCount(3, exclusive);
		Assert.IsNull(exclusive[0]._normalizedFilePath);
		Assert.AreSame(exclusive[0], again[0]);
		Assert.HasCount(1, plain);
		Assert.AreEqual(s_first, plain[0]._normalizedFilePath);
	}

	[TestMethod]
	public void TailTask_IsCompletedUntilANodeIsQueuedAndThenTheNodesCompletion()
	{
		var fixture = new TestHost();
		OperationLaneTracker.OperationLane lane = fixture.Capture([s_first])[0];

		Assert.IsTrue(lane.TailTask.IsCompleted);
		Assert.IsTrue(lane.IsIdle);

		var node = new OperationLaneTracker.LaneNode { _completion = new TaskCompletionSource().Task };

		lane._tail = node;

		Assert.AreSame(node._completion, lane.TailTask);
		Assert.IsFalse(lane.IsIdle);
	}

	[TestMethod]
	public void Complete_RemovesTheLaneOnceItsLastNodeSettled()
	{
		var fixture = new TestHost();
		OperationLaneTracker.OperationLane lane = fixture.Capture([s_first])[0];
		var node = new OperationLaneTracker.LaneNode();

		lane._tail = node;
		fixture.Complete(lane, node);

		Assert.IsTrue(lane.IsIdle);
		Assert.AreEqual(0, fixture.Tracker.DocumentLaneCount);
	}

	[TestMethod]
	public void Complete_KeepsTheLaneAndItsTailWhenANewerNodeOwnsTheTail()
	{
		var fixture = new TestHost();

		OperationLaneTracker.OperationLane lane = fixture.Capture([s_first])[0];

		var settled = new OperationLaneTracker.LaneNode();
		var newer = new OperationLaneTracker.LaneNode();

		lane._tail = newer;
		fixture.Complete(lane, settled);

		Assert.AreSame(newer, lane._tail);
		Assert.AreEqual(1, fixture.Tracker.DocumentLaneCount);
	}

	[TestMethod]
	public void Complete_NeverRemovesALaterLaneOfTheSamePath()
	{
		var fixture = new TestHost();
		OperationLaneTracker.OperationLane drained = fixture.Capture([s_first])[0];
		var settled = new OperationLaneTracker.LaneNode();

		drained._tail = settled;
		fixture.Complete(drained, settled);

		// A later node of the same path created a new lane, so a late completion for the drained one must not remove it.
		OperationLaneTracker.OperationLane replacement = fixture.Capture([s_first])[0];

		replacement._tail = new OperationLaneTracker.LaneNode();
		fixture.Complete(drained, new OperationLaneTracker.LaneNode());

		Assert.AreNotSame(drained, replacement);
		Assert.AreEqual(1, fixture.Tracker.DocumentLaneCount);
		Assert.AreSame(replacement, fixture.Capture([s_first])[0]);
	}

	[TestMethod]
	public void Release_CompletesEveryLaneTheNodeJoinedAndNeverRemovesTheExclusiveLane()
	{
		var fixture = new TestHost();
		OperationLaneTracker.OperationLane[] lanes = fixture.Capture([s_first, s_second], joinsExclusiveLane: true);
		var node = new OperationLaneTracker.LaneNode();

		foreach (OperationLaneTracker.OperationLane lane in lanes)
			lane._tail = node;

		fixture.Tracker.Release(lanes, node);

		Assert.IsTrue(lanes.All(static lane => lane.IsIdle));
		Assert.AreEqual(0, fixture.Tracker.DocumentLaneCount);
		Assert.AreSame(lanes[0], fixture.Capture([s_first], joinsExclusiveLane: true)[0]);
	}
}
