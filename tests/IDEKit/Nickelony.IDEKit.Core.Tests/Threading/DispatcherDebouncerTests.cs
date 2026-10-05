namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the unit contract of the dispatcher debouncer: re-arming replaces the callback, canceling drops it, and a disposed debouncer ignores
/// further arming.
/// </summary>
[TestClass]
public sealed class DispatcherDebouncerTests
{
	private static readonly TimeSpan s_delay = TimeSpan.FromMilliseconds(10.0);

	[TestMethod]
	public void Arm_Repeatedly_RunsOnlyTheLastCallback()
	{
		var dispatcher = new FakeUiDispatcher();
		using var debouncer = new DispatcherDebouncer(s_delay, dispatcher);

		int firstCount = 0;
		int secondCount = 0;

		debouncer.Arm(() => firstCount++);
		debouncer.Arm(() => secondCount++);

		dispatcher.RunPending();

		// Arming again restarts the delay with the newest callback, so the replaced one never runs.
		Assert.AreEqual(0, firstCount);
		Assert.AreEqual(1, secondCount);
		Assert.IsFalse(debouncer.IsPending);
	}

	[TestMethod]
	public void Arm_SchedulesTheCallbackWithTheConfiguredDelay()
	{
		var dispatcher = new FakeUiDispatcher();
		using var debouncer = new DispatcherDebouncer(s_delay, dispatcher);

		debouncer.Arm(static () => { });
		debouncer.Arm(static () => { });

		Assert.AreSequenceEqual([s_delay, s_delay], dispatcher.RequestedDelays);
		Assert.AreEqual(1, dispatcher.PendingCount);
	}

	[TestMethod]
	public void Arm_WithADelay_SchedulesThatArmingWithItAndTheNextOneWithTheConfiguredDelay()
	{
		var dispatcher = new FakeUiDispatcher();
		using var debouncer = new DispatcherDebouncer(s_delay, dispatcher);
		var own = TimeSpan.FromMilliseconds(200.0);

		debouncer.Arm(static () => { }, own);
		debouncer.Arm(static () => { });

		Assert.AreSequenceEqual([own, s_delay], dispatcher.RequestedDelays);
	}

	[TestMethod]
	public void Arm_WithANegativeDelayOrANullCallback_Throws()
	{
		var dispatcher = new FakeUiDispatcher();
		using var debouncer = new DispatcherDebouncer(s_delay, dispatcher);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => debouncer.Arm(static () => { }, TimeSpan.FromMilliseconds(-1.0)));
		Assert.ThrowsExactly<ArgumentNullException>(() => debouncer.Arm(null!, s_delay));
		Assert.IsFalse(debouncer.IsPending);
	}

	[TestMethod]
	public void IsPending_IsTrueFromArmingUntilTheCallbackRuns()
	{
		var dispatcher = new FakeUiDispatcher();
		using var debouncer = new DispatcherDebouncer(s_delay, dispatcher);

		Assert.IsFalse(debouncer.IsPending);

		debouncer.Arm(static () => { });

		Assert.IsTrue(debouncer.IsPending);

		dispatcher.RunPending();

		Assert.IsFalse(debouncer.IsPending);
	}

	[TestMethod]
	public void Arm_FromInsideTheCallback_LeavesTheNewScheduleArmed()
	{
		var dispatcher = new FakeUiDispatcher();
		using var debouncer = new DispatcherDebouncer(s_delay, dispatcher);

		int secondCount = 0;

		debouncer.Arm(() => debouncer.Arm(() => secondCount++));
		dispatcher.RunPending();

		// The pending handle is cleared before the callback runs, so the callback can arm again without losing the new schedule.
		Assert.IsTrue(debouncer.IsPending);
		Assert.AreEqual(0, secondCount);

		dispatcher.RunPending();

		Assert.AreEqual(1, secondCount);
		Assert.IsFalse(debouncer.IsPending);
	}

	[TestMethod]
	public void Cancel_ArmedCallback_DoesNotRun()
	{
		var dispatcher = new FakeUiDispatcher();
		using var debouncer = new DispatcherDebouncer(s_delay, dispatcher);

		int count = 0;

		debouncer.Arm(() => count++);

		Assert.IsTrue(debouncer.IsPending);

		debouncer.Cancel();

		Assert.IsFalse(debouncer.IsPending);

		dispatcher.RunPending();

		Assert.AreEqual(0, count);
	}

	[TestMethod]
	public void Cancel_WithoutAnArmedCallback_IsANoOp()
	{
		using var debouncer = new DispatcherDebouncer(s_delay, new FakeUiDispatcher());

		debouncer.Cancel();

		Assert.IsFalse(debouncer.IsPending);
	}

	[TestMethod]
	public void Dispose_WithAnArmedCallback_CancelsIt()
	{
		var dispatcher = new FakeUiDispatcher();
		var debouncer = new DispatcherDebouncer(s_delay, dispatcher);

		int count = 0;

		debouncer.Arm(() => count++);
		debouncer.Dispose();
		dispatcher.RunPending();

		Assert.AreEqual(0, count);
		Assert.IsFalse(debouncer.IsPending);
	}

	[TestMethod]
	public void Dispose_ThenArm_IsIgnoredAndIdempotent()
	{
		var dispatcher = new FakeUiDispatcher();
		var debouncer = new DispatcherDebouncer(s_delay, dispatcher);

		int count = 0;

		debouncer.Dispose();
		debouncer.Dispose();

		// A disposed debouncer stays silent: arming is ignored and no callback can run.
		debouncer.Arm(() => count++);
		debouncer.Cancel();
		dispatcher.RunPending();

		Assert.AreEqual(0, count);
		Assert.IsEmpty(dispatcher.RequestedDelays);
		Assert.IsFalse(debouncer.IsPending);
	}

	[TestMethod]
	public void Arm_WhenScheduleThrows_DoesNotLeaveADanglingPendingHandle()
	{
		var dispatcher = new FakeUiDispatcher { ScheduleFailure = new InvalidOperationException("The dispatcher is shutting down.") };
		var debouncer = new DispatcherDebouncer(s_delay, dispatcher);

		// The dispatcher rejects the schedule (for example while it shuts down); the failed arm must not be
		// reported as pending, because its handle can never run to clear the state.
		Assert.ThrowsExactly<InvalidOperationException>(() => debouncer.Arm(static () => { }));
		Assert.IsFalse(debouncer.IsPending);

		// A later arm is still allowed and still reports the rejected schedule instead of a stale pending flag.
		Assert.ThrowsExactly<InvalidOperationException>(() => debouncer.Arm(static () => { }));
		Assert.IsFalse(debouncer.IsPending);
	}

	[TestMethod]
	public void Constructor_WithANegativeDelay_Throws()
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			new DispatcherDebouncer(TimeSpan.FromMilliseconds(-1.0), new FakeUiDispatcher())
		);

		Assert.AreEqual("delay", exception.ParamName);
	}

	[TestMethod]
	public void Constructor_WithAZeroDelay_IsAllowed()
	{
		var dispatcher = new FakeUiDispatcher();
		using var debouncer = new DispatcherDebouncer(TimeSpan.Zero, dispatcher);

		debouncer.Arm(static () => { });

		Assert.AreSequenceEqual([TimeSpan.Zero], dispatcher.RequestedDelays);
	}

	[TestMethod]
	public void Constructor_WithANullDispatcher_Throws()
	{
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() => new DispatcherDebouncer(s_delay, null!));
		Assert.AreEqual("dispatcher", exception.ParamName);
	}

	[TestMethod]
	public void Arm_WithANullCallback_Throws()
	{
		using var debouncer = new DispatcherDebouncer(s_delay, new FakeUiDispatcher());

		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() => debouncer.Arm(null!));

		Assert.AreEqual("callback", exception.ParamName);
		Assert.IsFalse(debouncer.IsPending);
	}
}
