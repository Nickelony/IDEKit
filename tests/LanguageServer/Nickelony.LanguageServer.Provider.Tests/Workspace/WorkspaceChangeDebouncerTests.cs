namespace Nickelony.LanguageServer.Provider.Tests;

// Timing note: the scheduling tests drive a ManualTimeProvider, so debounce, maximum-latency, and retry windows
// are deterministic. One smoke test keeps the system time provider path covered with a generous real wait.
[TestClass]
public sealed class WorkspaceChangeDebouncerTests
{
	[TestMethod]
	public void Queue_BurstOfChanges_DispatchesOnceAfterTheDebounceDelay()
	{
		int dispatchCount = 0;
		var timeProvider = new ManualTimeProvider();
		using var debouncer = new WorkspaceChangeDebouncer(
			TimeSpan.FromMilliseconds(50),
			TimeSpan.FromSeconds(30),
			() => dispatchCount++,
			timeProvider);

		debouncer.Queue(@"C:\Workspace\a.ext", FileChangeKind.Created);
		debouncer.Queue(@"C:\Workspace\a.ext", FileChangeKind.Changed);
		debouncer.Queue(@"C:\Workspace\b.ext", FileChangeKind.Created);

		// The dispatch must not run before the debounce delay elapses.
		timeProvider.Advance(TimeSpan.FromMilliseconds(49));
		Assert.AreEqual(0, dispatchCount);

		timeProvider.Advance(TimeSpan.FromMilliseconds(2));
		Assert.AreEqual(1, dispatchCount);

		Assert.IsFalse(debouncer.IsEmpty);

		FileChangeBatch batch = debouncer.DrainBatch();

		Assert.AreEqual(2, batch.Count);
		Assert.IsTrue(debouncer.IsEmpty);
	}

	[TestMethod]
	public void Queue_TimestampCrossesTheMultiplyFirstOverflowThreshold_StillDispatchesAfterTheDebounceWindow()
	{
		int dispatchCount = 0;

		// The clock starts just below the point where converting the raw timestamp to milliseconds with a
		// multiply-first expression (timestamp * 1000 on a 10 MHz provider) overflows the 64-bit product, and
		// the change is pending while the clock crosses it. A wrapping conversion would compute a huge
		// remaining delay and fail while rescheduling; the divide-first conversion keeps the deadlines stable.
		var timeProvider = new ManualTimeProvider(9_223_372_036_854_700L, TimeSpan.TicksPerSecond);
		using var debouncer = new WorkspaceChangeDebouncer(
			TimeSpan.FromMilliseconds(50),
			TimeSpan.FromSeconds(30),
			() => dispatchCount++,
			timeProvider);

		debouncer.Queue(@"C:\Workspace\a.ext", FileChangeKind.Changed);

		timeProvider.Advance(TimeSpan.FromMilliseconds(100));

		Assert.AreEqual(1, dispatchCount);
	}

	[TestMethod]
	public void Queue_FromAZeroTimestampClock_KeepsTheMaximumLatencyAnchoredAtTheFirstChange()
	{
		int dispatchCount = 0;

		// A legitimate timestamp of 0 (a fresh fake clock) must be treated as an active burst. If it collided with
		// the "no burst" sentinel, the next change would re-anchor the burst start and widen the maximum dispatch
		// delay window beyond the first change.
		var timeProvider = new ManualTimeProvider(0, TimeSpan.TicksPerSecond);
		using var debouncer = new WorkspaceChangeDebouncer(
			TimeSpan.FromMilliseconds(50),
			TimeSpan.FromMilliseconds(45),
			() => dispatchCount++,
			timeProvider);

		debouncer.Queue(@"C:\Workspace\a.ext", FileChangeKind.Changed);

		timeProvider.Advance(TimeSpan.FromMilliseconds(40));
		debouncer.Queue(@"C:\Workspace\b.ext", FileChangeKind.Changed);

		// The maximum dispatch delay (45 ms) is measured from the first change at t=0, so the dispatch must have
		// happened by t=46; an anchor reset by the second change would postpone it past the bound.
		timeProvider.Advance(TimeSpan.FromMilliseconds(6));

		Assert.AreEqual(1, dispatchCount);
	}

	[TestMethod]
	public void Queue_ContinuousChangeStream_StillDispatchesWithinTheMaximumLatency()
	{
		int dispatchCount = 0;
		long dispatchLatencyMilliseconds = -1;
		var timeProvider = new ManualTimeProvider();
		long firstQueuedMilliseconds = timeProvider.ElapsedMilliseconds;

		using var debouncer = new WorkspaceChangeDebouncer(
			TimeSpan.FromMilliseconds(50),
			TimeSpan.FromMilliseconds(120),
			() =>
			{
				dispatchCount++;
				dispatchLatencyMilliseconds = timeProvider.ElapsedMilliseconds - firstQueuedMilliseconds;
			},
			timeProvider);

		// A sustained change stream keeps postponing the debounce window, so the maximum dispatch delay is the
		// only bound that still guarantees delivery within 120 ms of the first queued change.
		for (int i = 0; i < 8; i++)
		{
			debouncer.Queue(@"C:\Workspace\a.ext", FileChangeKind.Changed);
			timeProvider.Advance(TimeSpan.FromMilliseconds(20));
		}

		Assert.AreEqual(1, dispatchCount);
		Assert.IsTrue(dispatchLatencyMilliseconds <= 120,
			$"The dispatch latency was {dispatchLatencyMilliseconds} ms; a continuous change stream must not exceed the maximum dispatch delay.");
	}

	[TestMethod]
	public void Queue_WhileARetryIsPending_DoesNotShortenTheScheduledRetry()
	{
		int dispatchCount = 0;
		var timeProvider = new ManualTimeProvider();
		using var debouncer = new WorkspaceChangeDebouncer(
			TimeSpan.FromMilliseconds(50),
			TimeSpan.FromSeconds(30),
			() => dispatchCount++,
			timeProvider);

		debouncer.Queue(@"C:\Workspace\a.ext", FileChangeKind.Changed);
		FileChangeBatch batch = debouncer.DrainBatch();

		debouncer.Requeue(batch, TimeSpan.FromMilliseconds(2000));

		// A new change must not turn the pending retry backoff into a fresh (shorter) debounce window.
		debouncer.Queue(@"C:\Workspace\b.ext", FileChangeKind.Changed);

		timeProvider.Advance(TimeSpan.FromMilliseconds(1999));
		Assert.AreEqual(0, dispatchCount);

		timeProvider.Advance(TimeSpan.FromMilliseconds(2));
		Assert.AreEqual(1, dispatchCount);

		// The scheduled retry must deliver the replayed change and the change that arrived during the backoff.
		FileChangeBatch retriedBatch = debouncer.DrainBatch();

		Assert.AreEqual(2, retriedBatch.Count);
		Assert.AreEqual(@"C:\Workspace\a.ext", retriedBatch.Entries[0].Path);
		Assert.AreEqual(@"C:\Workspace\b.ext", retriedBatch.Entries[1].Path);
	}

	[TestMethod]
	public void Requeue_DrainedBatch_DispatchesAfterTheExplicitDelay()
	{
		int dispatchCount = 0;
		var timeProvider = new ManualTimeProvider();
		using var debouncer = new WorkspaceChangeDebouncer(
			TimeSpan.FromHours(1),
			TimeSpan.FromHours(2),
			() => dispatchCount++,
			timeProvider);

		debouncer.Queue(@"C:\Workspace\a.ext", FileChangeKind.Changed);
		FileChangeBatch batch = debouncer.DrainBatch();

		Assert.AreEqual(1, batch.Count);

		debouncer.Requeue(batch, TimeSpan.FromMilliseconds(50));

		timeProvider.Advance(TimeSpan.FromMilliseconds(49));
		Assert.AreEqual(0, dispatchCount);

		timeProvider.Advance(TimeSpan.FromMilliseconds(2));
		Assert.AreEqual(1, dispatchCount);
	}

	[TestMethod]
	public void Stop_AfterQueuedChange_PreventsTheScheduledDispatch()
	{
		int dispatchCount = 0;
		var timeProvider = new ManualTimeProvider();
		using var debouncer = new WorkspaceChangeDebouncer(
			TimeSpan.FromMilliseconds(50),
			TimeSpan.FromSeconds(30),
			() => dispatchCount++,
			timeProvider);

		debouncer.Queue(@"C:\Workspace\a.ext", FileChangeKind.Changed);
		debouncer.Stop();

		timeProvider.Advance(TimeSpan.FromSeconds(5));

		Assert.AreEqual(0, dispatchCount);
		Assert.IsFalse(debouncer.IsEmpty);
	}

	[TestMethod]
	public void Dispose_ThenQueue_IgnoresChangesAndNeverDispatches()
	{
		int dispatchCount = 0;
		var timeProvider = new ManualTimeProvider();
		var debouncer = new WorkspaceChangeDebouncer(
			TimeSpan.FromMilliseconds(50),
			TimeSpan.FromSeconds(30),
			() => dispatchCount++,
			timeProvider);

		debouncer.Dispose();
		debouncer.Queue(@"C:\Workspace\a.ext", FileChangeKind.Changed);

		timeProvider.Advance(TimeSpan.FromSeconds(5));

		Assert.AreEqual(0, dispatchCount);
		Assert.IsTrue(debouncer.IsEmpty);
	}

	[TestMethod]
	public async Task Queue_WithTheSystemTimeProvider_DispatchesAfterTheDebounceDelay()
	{
		int dispatchCount = 0;
		using var debouncer = new WorkspaceChangeDebouncer(
			TimeSpan.FromMilliseconds(50),
			TimeSpan.FromSeconds(30),
			() => Interlocked.Increment(ref dispatchCount));

		debouncer.Queue(@"C:\Workspace\a.ext", FileChangeKind.Created);

		await TestPolling.UntilAsync(() => Volatile.Read(ref dispatchCount) == 1, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
	}
}
