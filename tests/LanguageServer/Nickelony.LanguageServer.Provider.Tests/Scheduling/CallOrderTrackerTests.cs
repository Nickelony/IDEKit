namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the order table of the scheduler: which reservation waits behind which, and when a path leaves the table.</summary>
[TestClass]
public sealed class CallOrderTrackerTests
{
	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(5);

	private static readonly string s_first = TestPaths.Script("first.test");
	private static readonly string s_second = TestPaths.Script("second.test");

	private sealed class TestHost
	{
		internal TestHost(LocalPathComparisonPolicy? pathComparison = null) =>
			Tracker = new CallOrderTracker(SyncRoot, pathComparison ?? LocalPathComparisonPolicy.CaseSensitive);

		internal object SyncRoot { get; } = new();

		internal CallOrderTracker Tracker { get; }

		internal CallOrderTracker.CallReservation Reserve(params string[] paths)
		{
			lock (SyncRoot)
				return Tracker.Reserve(paths);
		}

		internal void Supersede(string path)
		{
			lock (SyncRoot)
				Tracker.Supersede(path);
		}

		internal void SupersedeAll()
		{
			lock (SyncRoot)
				Tracker.SupersedeAll();
		}

		internal bool IsSuperseded(CallOrderTracker.CallReservation reservation)
		{
			lock (SyncRoot)
				return Tracker.IsSuperseded(reservation);
		}
	}

	private static bool IsReady(CallOrderTracker.CallReservation reservation) => reservation.WaitForTurnAsync(CancellationToken.None).IsCompleted;

	private static Task WaitUntilReadyAsync(CallOrderTracker.CallReservation reservation) =>
		reservation.WaitForTurnAsync(CancellationToken.None).WaitAsync(s_waitTimeout);

	[TestMethod]
	public void Reserve_WithNoEarlierReservation_IsReadyAtOnce()
	{
		var fixture = new TestHost();
		Assert.IsTrue(IsReady(fixture.Reserve(s_first)));
	}

	[TestMethod]
	public async Task Reserve_BehindAnIncompleteReservation_WaitsUntilItCompletes()
	{
		var fixture = new TestHost();

		CallOrderTracker.CallReservation first = fixture.Reserve(s_first);
		CallOrderTracker.CallReservation second = fixture.Reserve(s_first);

		Assert.IsFalse(IsReady(second));

		first.Complete();

		await WaitUntilReadyAsync(second);
	}

	[TestMethod]
	public async Task Reserve_BehindAReservationThatEndedEarly_StillWaitsForTheOneBeforeIt()
	{
		var fixture = new TestHost();

		CallOrderTracker.CallReservation first = fixture.Reserve(s_first);
		CallOrderTracker.CallReservation second = fixture.Reserve(s_first);
		CallOrderTracker.CallReservation third = fixture.Reserve(s_first);

		// The second call ends before its turn, as a call that fails while it waits for the connection does.
		second.Complete();

		Assert.IsFalse(IsReady(third));

		first.Complete();

		await WaitUntilReadyAsync(third);
	}

	[TestMethod]
	public void Complete_CalledTwice_LeavesTheTableConsistent()
	{
		var fixture = new TestHost();

		CallOrderTracker.CallReservation first = fixture.Reserve(s_first);
		CallOrderTracker.CallReservation second = fixture.Reserve(s_first);

		first.Complete();
		first.Complete();

		// The repeat did not release the second reservation's place: the path stays in the table until the second one completes too.
		Assert.AreEqual(1, fixture.Tracker.ReservedPathCount);

		second.Complete();

		Assert.AreEqual(0, fixture.Tracker.ReservedPathCount);
		Assert.IsTrue(IsReady(fixture.Reserve(s_first)));
	}

	[TestMethod]
	public void Complete_OfEveryReservation_RemovesTheirPathsFromTheTable()
	{
		var fixture = new TestHost();

		CallOrderTracker.CallReservation single = fixture.Reserve(s_first);
		CallOrderTracker.CallReservation both = fixture.Reserve(s_first, s_second);

		Assert.AreEqual(2, fixture.Tracker.ReservedPathCount);

		single.Complete();
		Assert.AreEqual(2, fixture.Tracker.ReservedPathCount);

		both.Complete();
		Assert.AreEqual(0, fixture.Tracker.ReservedPathCount);
	}

	[TestMethod]
	public void Reserve_ForAnotherPath_DoesNotWait()
	{
		var fixture = new TestHost();

		_ = fixture.Reserve(s_first);

		Assert.IsTrue(IsReady(fixture.Reserve(s_second)));
	}

	[TestMethod]
	public void Reserve_ForPathsThatDifferOnlyInCase_SharesOneOrderUnderACaseInsensitiveComparison()
	{
		var insensitive = new TestHost(LocalPathComparisonPolicy.CaseInsensitive);
		var sensitive = new TestHost(LocalPathComparisonPolicy.CaseSensitive);

		string lower = TestPaths.Script("case.test");
		string upper = TestPaths.Script("CASE.test");

		_ = insensitive.Reserve(lower);
		_ = sensitive.Reserve(lower);

		Assert.IsFalse(IsReady(insensitive.Reserve(upper)));
		Assert.IsTrue(IsReady(sensitive.Reserve(upper)));
	}

	[TestMethod]
	public async Task Reserve_ForTwoPaths_WaitsBehindBothAndHoldsBoth()
	{
		var fixture = new TestHost();

		CallOrderTracker.CallReservation onFirst = fixture.Reserve(s_first);
		CallOrderTracker.CallReservation onSecond = fixture.Reserve(s_second);
		CallOrderTracker.CallReservation onBoth = fixture.Reserve(s_first, s_second);
		CallOrderTracker.CallReservation afterOnSecond = fixture.Reserve(s_second);
		CallOrderTracker.CallReservation afterOnFirst = fixture.Reserve(s_first);

		onFirst.Complete();
		Assert.IsFalse(IsReady(onBoth));

		onSecond.Complete();
		await WaitUntilReadyAsync(onBoth);

		// The call on both paths holds each of them, so the calls behind it on either path wait for it.
		Assert.IsFalse(IsReady(afterOnSecond));
		Assert.IsFalse(IsReady(afterOnFirst));

		onBoth.Complete();

		await WaitUntilReadyAsync(afterOnSecond);
		await WaitUntilReadyAsync(afterOnFirst);
	}

	[TestMethod]
	public async Task WaitForTurnAsync_WhenCanceled_ThrowsAndLeavesTheReservationUsable()
	{
		var fixture = new TestHost();

		CallOrderTracker.CallReservation first = fixture.Reserve(s_first);
		CallOrderTracker.CallReservation second = fixture.Reserve(s_first);
		CallOrderTracker.CallReservation third = fixture.Reserve(s_first);

		using var cancellation = new CancellationTokenSource();
		Task wait = second.WaitForTurnAsync(cancellation.Token);

		await cancellation.CancelAsync();

		await Assert.ThrowsAsync<OperationCanceledException>(() => wait);

		// A call that leaves its wait still completes its reservation, and the call behind it waits for the call before it as before.
		second.Complete();
		Assert.IsFalse(IsReady(third));

		first.Complete();

		await WaitUntilReadyAsync(third);
		Assert.AreEqual(1, fixture.Tracker.ReservedPathCount);

		third.Complete();
		Assert.AreEqual(0, fixture.Tracker.ReservedPathCount);
	}

	[TestMethod]
	public void IsSuperseded_AfterSupersede_IsTrueOnlyForTheReservationsMadeBefore()
	{
		var fixture = new TestHost();

		CallOrderTracker.CallReservation before = fixture.Reserve(s_first);
		CallOrderTracker.CallReservation otherPath = fixture.Reserve(s_second);

		Assert.IsFalse(fixture.IsSuperseded(before));

		fixture.Supersede(s_first);

		CallOrderTracker.CallReservation after = fixture.Reserve(s_first);

		Assert.IsTrue(fixture.IsSuperseded(before));
		Assert.IsFalse(fixture.IsSuperseded(after));
		Assert.IsFalse(fixture.IsSuperseded(otherPath));
	}

	[TestMethod]
	public void Supersede_ForAPathWithoutReservations_LeavesNothingBehind()
	{
		var fixture = new TestHost();

		fixture.Supersede(s_first);

		Assert.AreEqual(0, fixture.Tracker.ReservedPathCount);
		Assert.IsFalse(fixture.IsSuperseded(fixture.Reserve(s_first)));
	}

	[TestMethod]
	public void SupersedeAll_SupersedesTheReservationsOfEveryPath()
	{
		var fixture = new TestHost();

		CallOrderTracker.CallReservation onFirst = fixture.Reserve(s_first);
		CallOrderTracker.CallReservation onSecond = fixture.Reserve(s_second);

		fixture.SupersedeAll();

		CallOrderTracker.CallReservation later = fixture.Reserve(s_first);

		Assert.IsTrue(fixture.IsSuperseded(onFirst));
		Assert.IsTrue(fixture.IsSuperseded(onSecond));
		Assert.IsFalse(fixture.IsSuperseded(later));
	}
}
