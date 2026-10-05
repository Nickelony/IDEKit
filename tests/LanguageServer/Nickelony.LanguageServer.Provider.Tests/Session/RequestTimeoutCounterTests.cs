namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class RequestTimeoutCounterTests
{
	[TestMethod]
	public void RecordTimeout_CountsConsecutiveTimeoutsOfOneGeneration()
	{
		var tracker = new RequestTimeoutCounter(restartThreshold: 5);

		Assert.AreEqual(1, tracker.RecordTimeout(connectionGeneration: 3).ConsecutiveTimeouts);
		Assert.AreEqual(2, tracker.RecordTimeout(connectionGeneration: 3).ConsecutiveTimeouts);
		Assert.AreEqual(3, tracker.TrackedGeneration);
		Assert.AreEqual(2, tracker.ConsecutiveTimeouts);
	}

	[TestMethod]
	public void RecordTimeout_ReachingTheThreshold_MakesARestartDueOncePerGeneration()
	{
		var tracker = new RequestTimeoutCounter(restartThreshold: 2);

		Assert.IsFalse(tracker.RecordTimeout(7).RestartDue);
		Assert.IsTrue(tracker.RecordTimeout(7).RestartDue);
		Assert.IsFalse(tracker.RecordTimeout(7).RestartDue, "The latch holds until the generation settles or changes.");
		Assert.AreEqual(3, tracker.ConsecutiveTimeouts);
	}

	[TestMethod]
	public void RecordTimeout_OnANewGeneration_StartsAFreshStreakAndLatch()
	{
		var tracker = new RequestTimeoutCounter(restartThreshold: 2);

		_ = tracker.RecordTimeout(7);
		Assert.IsTrue(tracker.RecordTimeout(7).RestartDue);

		Assert.IsFalse(tracker.RecordTimeout(8).RestartDue);
		Assert.AreEqual(1, tracker.ConsecutiveTimeouts);
		Assert.IsTrue(tracker.RecordTimeout(8).RestartDue);
	}

	[TestMethod]
	public void RecordTimeout_WithAZeroThreshold_NeverMakesARestartDue()
	{
		var tracker = new RequestTimeoutCounter(restartThreshold: 0);

		for (int i = 0; i < 10; i++)
			Assert.IsFalse(tracker.RecordTimeout(1).RestartDue);

		Assert.AreEqual(10, tracker.ConsecutiveTimeouts);
	}

	[TestMethod]
	public void Reset_OnTheTrackedGeneration_ClearsTheStreakAndTheRestartLatch()
	{
		var tracker = new RequestTimeoutCounter(restartThreshold: 2);

		_ = tracker.RecordTimeout(4);
		Assert.IsTrue(tracker.RecordTimeout(4).RestartDue);

		tracker.Reset(4);

		Assert.AreEqual(0, tracker.ConsecutiveTimeouts);
		Assert.IsFalse(tracker.RecordTimeout(4).RestartDue);
		Assert.IsTrue(tracker.RecordTimeout(4).RestartDue, "The latch re-arms after a settlement.");
	}

	[TestMethod]
	public void Reset_FromASupersededGeneration_IsIgnored()
	{
		var tracker = new RequestTimeoutCounter(restartThreshold: 3);

		_ = tracker.RecordTimeout(6);
		_ = tracker.RecordTimeout(6);

		tracker.Reset(5);

		Assert.AreEqual(6, tracker.TrackedGeneration);
		Assert.AreEqual(2, tracker.ConsecutiveTimeouts);
	}

	[TestMethod]
	public void Reset_FromANewerGeneration_AdoptsItAndClearsTheStreak()
	{
		var tracker = new RequestTimeoutCounter(restartThreshold: 3);

		_ = tracker.RecordTimeout(6);

		tracker.Reset(9);

		Assert.AreEqual(9, tracker.TrackedGeneration);
		Assert.AreEqual(0, tracker.ConsecutiveTimeouts);
	}
}
