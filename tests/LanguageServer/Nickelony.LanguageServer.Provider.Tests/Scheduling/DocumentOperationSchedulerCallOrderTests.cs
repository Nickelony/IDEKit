namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers how the scheduler reserves the places of the calls that wait for the connection and how a cancellation reaches an update that waits.
/// </summary>
[TestClass]
public sealed class DocumentOperationSchedulerCallOrderTests
{
	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(5);

	private static readonly string s_first = TestPaths.Script("first.ext");
	private static readonly string s_second = TestPaths.Script("second.ext");

	[TestMethod]
	public void ReserveCall_ForTwoPaths_HoldsBothUntilItCompletes()
	{
		var scheduler = new DocumentOperationScheduler();

		CallOrderTracker.CallReservation reservation = scheduler.ReserveCall(s_first, s_second);

		Assert.AreEqual(2, scheduler.ReservedCallPathCount);

		reservation.Complete();

		Assert.AreEqual(0, scheduler.ReservedCallPathCount);
	}

	[TestMethod]
	public void ReserveCall_ForTwoSpellingsOfOneDocument_HoldsOnePath()
	{
		var scheduler = new DocumentOperationScheduler(TestOptions.CaseInsensitive);

		CallOrderTracker.CallReservation reservation = scheduler.ReserveCall(TestPaths.Script("case.ext"), TestPaths.Script("CASE.ext"));

		Assert.AreEqual(1, scheduler.ReservedCallPathCount);

		reservation.Complete();

		Assert.AreEqual(0, scheduler.ReservedCallPathCount);
	}

	[TestMethod]
	public async Task EnqueueLatestUpdateAsync_ForAReservationSupersededByCancelLatestUpdate_SkipsTheDelegate()
	{
		var scheduler = new DocumentOperationScheduler();
		CallOrderTracker.CallReservation reservation = scheduler.ReserveCall(s_first);

		// The update still waits for the connection when the cancellation arrives.
		scheduler.CancelLatestUpdate(s_first);

		bool ran = false;

		await scheduler
			.EnqueueLatestUpdateAsync(
				s_first,

				_ =>
				{
					ran = true;
					return Task.CompletedTask;
				},

				reservation
			)
			.WaitAsync(s_waitTimeout);

		Assert.IsFalse(ran);

		// The skipped update left no node on the lane, so the next operation of the document runs at once.
		Assert.AreEqual(
			7,
			await scheduler.EnqueuePerDocumentAsync(s_first, _ => Task.FromResult(7), CancellationToken.None).WaitAsync(s_waitTimeout)
		);

		reservation.Complete();
	}

	[TestMethod]
	public async Task EnqueueLatestUpdateAsync_ForAReservationMadeAfterTheCancellation_RunsTheDelegate()
	{
		var scheduler = new DocumentOperationScheduler();

		scheduler.CancelLatestUpdate(s_first);

		CallOrderTracker.CallReservation reservation = scheduler.ReserveCall(s_first);
		bool ran = false;

		await scheduler
			.EnqueueLatestUpdateAsync(
				s_first,

				_ =>
				{
					ran = true;
					return Task.CompletedTask;
				},

				reservation
			)
			.WaitAsync(s_waitTimeout);

		Assert.IsTrue(ran);

		reservation.Complete();
	}

	[TestMethod]
	public async Task CancelLatestUpdate_ForAnotherPath_LeavesTheReservationUnsuperseded()
	{
		var scheduler = new DocumentOperationScheduler();
		CallOrderTracker.CallReservation reservation = scheduler.ReserveCall(s_first);

		scheduler.CancelLatestUpdate(s_second);

		bool ran = false;

		await scheduler
			.EnqueueLatestUpdateAsync(
				s_first,

				_ =>
				{
					ran = true;
					return Task.CompletedTask;
				},

				reservation
			)
			.WaitAsync(s_waitTimeout);

		Assert.IsTrue(ran);

		reservation.Complete();
	}

	[TestMethod]
	public async Task CancelAllLatestUpdates_SupersedesTheReservationsOfEveryPath()
	{
		var scheduler = new DocumentOperationScheduler();

		CallOrderTracker.CallReservation onFirst = scheduler.ReserveCall(s_first);
		CallOrderTracker.CallReservation onSecond = scheduler.ReserveCall(s_second);

		scheduler.CancelAllLatestUpdates();

		int ran = 0;

		await scheduler
			.EnqueueLatestUpdateAsync(
				s_first,

				_ =>
				{
					Interlocked.Increment(ref ran);
					return Task.CompletedTask;
				},

				onFirst
			)
			.WaitAsync(s_waitTimeout);

		await scheduler
			.EnqueueLatestUpdateAsync(
				s_second,

				_ =>
				{
					Interlocked.Increment(ref ran);
					return Task.CompletedTask;
				},

				onSecond
			)
			.WaitAsync(s_waitTimeout);

		Assert.AreEqual(0, Volatile.Read(ref ran));

		onFirst.Complete();
		onSecond.Complete();
	}
}
