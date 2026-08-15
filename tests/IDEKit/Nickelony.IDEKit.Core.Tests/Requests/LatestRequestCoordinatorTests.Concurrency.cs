namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class LatestRequestCoordinatorTests
{
	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_SupersedesEarlierRequestWithThreadPoolContinuation()
	{
		var coordinator = new LatestRequestCoordinator();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		int appliedCount = 0;

		Task<RequestOutcome> first = coordinator.RunAsync<int, int>(
			0,
			async (state, token) =>
			{
				await release.Task.ConfigureAwait(true);
				return state;
			},
			static (state, result) => true,
			_ => appliedCount++);

		Task<RequestOutcome> second = coordinator.RunAsync<int, int>(
			1,
			static (state, token) => Task.Run(() => state, token),
			static (state, result) => true,
			_ => appliedCount++);

		Assert.AreEqual(RequestOutcome.Completed, await second);

		// The second request superseded the first and canceled its token, so the first completion
		// is discarded as soon as it observes the cancellation.
		release.SetResult();

		Assert.AreEqual(RequestOutcome.Superseded, await first);
		Assert.AreEqual(1, appliedCount);
	}

	[TestMethod]
	public async Task RunAsync_ConsecutiveRequestsApplyInOrder()
	{
		var coordinator = new LatestRequestCoordinator();
		var applied = new List<int>();

		Assert.AreEqual(RequestOutcome.Completed, await coordinator.RunAsync(
			1,
			static (state, token) => Task.FromResult(state),
			static (state, result) => true,
			applied.Add));

		Assert.AreEqual(RequestOutcome.Completed, await coordinator.RunAsync(
			2,
			static (state, token) => Task.FromResult(state),
			static (state, result) => true,
			applied.Add));

		Assert.AreEqual(RequestOutcome.Completed, await coordinator.RunAsync(
			3,
			static (state, token) => Task.FromResult(state),
			static (state, result) => true,
			applied.Add));

		CollectionAssert.AreEqual(new[] { 1, 2, 3 }, applied);
	}

	[TestMethod]
	public void RunAsync_ConcurrentAdmissions_NeverApplyMoreThanOneWinner()
	{
		const int runCount = 16;

		var coordinator = new LatestRequestCoordinator();
		var applied = new List<int>();
		var outcomesByRun = new RequestOutcome[runCount];
		var barrier = new Barrier(runCount);
		var threads = new Thread[runCount];

		for (int i = 0; i < runCount; i++)
		{
			int runIndex = i;

			threads[runIndex] = new Thread(() =>
			{
				// Every run is admitted concurrently, so the latest-request check races across threads.
				barrier.SignalAndWait();

				outcomesByRun[runIndex] = coordinator.RunAsync(
					runIndex,
					static (state, token) => Task.FromResult(state),
					static (state, result) => true,
					value =>
					{
						lock (applied)
							applied.Add(value);
					}).GetAwaiter().GetResult();
			})
			{
				IsBackground = true,
				Name = $"LatestRequestCoordinator race {runIndex}"
			};

			threads[runIndex].Start();
		}

		foreach (Thread thread in threads)
			Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30.0)), "A concurrent request thread did not finish in time.");

		// The last admitted run is always eligible, so at least one run completes; every other run is
		// superseded without applying its result.
		Assert.IsTrue(outcomesByRun.Contains(RequestOutcome.Completed), "Expected at least one completed run.");

		for (int runIndex = 0; runIndex < runCount; runIndex++)
		{
			bool appliedByRun;

			lock (applied)
				appliedByRun = applied.Contains(runIndex);

			if (outcomesByRun[runIndex] == RequestOutcome.Completed)
				Assert.IsTrue(appliedByRun, $"Run {runIndex} reported Completed without applying its result.");
			else
			{
				Assert.AreEqual(RequestOutcome.Superseded, outcomesByRun[runIndex], $"Unexpected outcome for run {runIndex}.");
				Assert.IsFalse(appliedByRun, $"Run {runIndex} applied its result despite not completing.");
			}
		}
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_CancelPendingRequestRacingCompletion_ClassifiesConsistently()
	{
		const int iterations = 64;

		for (int iteration = 0; iteration < iterations; iteration++)
		{
			var coordinator = new LatestRequestCoordinator();
			var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
			int appliedCount = 0;

			Task<RequestOutcome> request = coordinator.RunAsync(
				start,
				static (state, token) => state.Task,
				static (state, result) => true,
				_ => Interlocked.Increment(ref appliedCount));

			// The cancel races the completing compute. Whichever wins, the classification must stay
			// consistent with whether the result was applied: a run that observes the cancel before its
			// publication checks reports Canceled and never applies, while a cancel that lands too late
			// leaves the run Completed.
			Task cancelTask = Task.Run(coordinator.CancelPendingRequest);
			start.SetResult(42);

			RequestOutcome outcome = await request;
			await cancelTask;

			if (outcome == RequestOutcome.Completed)
				Assert.AreEqual(1, appliedCount, $"Iteration {iteration}: a completed run must apply exactly once.");
			else
			{
				Assert.AreEqual(RequestOutcome.Canceled, outcome, $"Iteration {iteration}: an unapplied run must report cancellation.");
				Assert.AreEqual(0, appliedCount, $"Iteration {iteration}: a canceled run must not apply its result.");
			}
		}
	}

	[TestMethod]
	public async Task RunAsync_InvalidateInsideCanApply_DoesNotRetractAppliedResult()
	{
		var coordinator = new LatestRequestCoordinator();
		int appliedCount = 0;

		RequestOutcome outcome = await coordinator.RunAsync(
			0,
			static (state, token) => Task.FromResult(42),
			(state, result) =>
			{
				// Invalidation inside the documented publication window does not retract a result that is
				// already being published; only checks before the window treat it as supersession.
				coordinator.Invalidate();
				return true;
			},
			_ => appliedCount++);

		Assert.AreEqual(RequestOutcome.Completed, outcome);
		Assert.AreEqual(1, appliedCount);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_InvalidateRacingCompletion_ClassifiesConsistently()
	{
		const int iterations = 64;

		for (int iteration = 0; iteration < iterations; iteration++)
		{
			var coordinator = new LatestRequestCoordinator();
			var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
			int appliedCount = 0;

			Task<RequestOutcome> request = coordinator.RunAsync(
				start,
				static (state, token) => state.Task,
				static (state, result) => true,
				_ => Interlocked.Increment(ref appliedCount));

			// The invalidation races the completing compute: a run that observes it before its
			// publication checks reports supersession without applying, while a late invalidation
			// leaves the run Completed.
			Task invalidateTask = Task.Run(coordinator.Invalidate);
			start.SetResult(42);

			RequestOutcome outcome = await request;
			await invalidateTask;

			if (outcome == RequestOutcome.Completed)
				Assert.AreEqual(1, appliedCount, $"Iteration {iteration}: a completed run must apply exactly once.");
			else
			{
				Assert.AreEqual(RequestOutcome.Superseded, outcome, $"Iteration {iteration}: an unapplied run must report supersession.");
				Assert.AreEqual(0, appliedCount, $"Iteration {iteration}: a superseded run must not apply its result.");
			}
		}
	}
}
