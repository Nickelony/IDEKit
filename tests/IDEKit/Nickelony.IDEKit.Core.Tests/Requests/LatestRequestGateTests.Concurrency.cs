namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class LatestRequestGateTests
{
	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task RunAsync_SupersedesEarlierRequestWithThreadPoolContinuation()
	{
		var gate = new LatestRequestGate();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		int appliedCount = 0;

		Task<RequestOutcome> first = gate.RunAsync<int, int>(
			state: 0,

			computeAsync: async (state, token) =>
			{
				await release.Task.ConfigureAwait(true);
				return state;
			},

			canApply: static (state, result) => true,
			apply: _ => appliedCount++,
			cancellationToken: TestContext.CancellationToken
		);

		Task<RequestOutcome> second = gate.RunAsync<int, int>(
			state: 1,
			computeAsync: static (state, token) => Task.Run(() => state, token),
			canApply: static (state, result) => true,
			apply: _ => appliedCount++,
			cancellationToken: TestContext.CancellationToken
		);

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
		var gate = new LatestRequestGate();
		var applied = new List<int>();

		Assert.AreEqual(
			RequestOutcome.Completed,

			await gate.RunAsync(
				state: 1,
				computeAsync: static (state, token) => Task.FromResult(state),
				canApply: static (state, result) => true,
				apply: applied.Add,
				cancellationToken: TestContext.CancellationToken
			)
		);

		Assert.AreEqual(
			RequestOutcome.Completed,

			await gate.RunAsync(
				state: 2,
				computeAsync: static (state, token) => Task.FromResult(state),
				canApply: static (state, result) => true,
				apply: applied.Add,
				cancellationToken: TestContext.CancellationToken
			)
		);

		Assert.AreEqual(
			RequestOutcome.Completed,

			await gate.RunAsync(
				state: 3,
				computeAsync: static (state, token) => Task.FromResult(state),
				canApply: static (state, result) => true,
				apply: applied.Add,
				cancellationToken: TestContext.CancellationToken
			)
		);

		Assert.AreSequenceEqual([1, 2, 3], applied);
	}

	[TestMethod]
	public void RunAsync_ConcurrentAdmissions_NeverApplyMoreThanOneWinner()
	{
		const int runCount = 16;

		var gate = new LatestRequestGate();
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
				barrier.SignalAndWait(TestContext.CancellationToken);

				outcomesByRun[runIndex] = gate.RunAsync(
						state: runIndex,
						computeAsync: static (state, token) => Task.FromResult(state),
						canApply: static (state, result) => true,

						apply: value =>
						{
							lock (applied)
								applied.Add(value);
						},

						cancellationToken: TestContext.CancellationToken
					)
					.GetAwaiter()
					.GetResult();
			})
			{
				IsBackground = true,
				Name = $"LatestRequestGate race {runIndex}",
			};

			threads[runIndex].Start();
		}

		foreach (Thread thread in threads)
			Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30.0)), "A concurrent request thread did not finish in time.");

		// The last admitted run is always eligible, so at least one run completes; every other run is
		// superseded without applying its result.
		Assert.Contains(RequestOutcome.Completed, outcomesByRun, "Expected at least one completed run.");

		for (int runIndex = 0; runIndex < runCount; runIndex++)
		{
			bool appliedByRun;

			lock (applied)
				appliedByRun = applied.Contains(runIndex);

			if (outcomesByRun[runIndex] == RequestOutcome.Completed)
			{
				Assert.IsTrue(appliedByRun, $"Run {runIndex} reported Completed without applying its result.");
			}
			else
			{
				Assert.AreEqual(RequestOutcome.Superseded, outcomesByRun[runIndex], $"Unexpected outcome for run {runIndex}.");
				Assert.IsFalse(appliedByRun, $"Run {runIndex} applied its result despite not completing.");
			}
		}
	}

	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task RunAsync_CancelPendingRequestRacingCompletion_ClassifiesConsistently()
	{
		const int iterations = 64;

		for (int iteration = 0; iteration < iterations; iteration++)
		{
			var gate = new LatestRequestGate();
			var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

			int appliedCount = 0;

			Task<RequestOutcome> request = gate.RunAsync(
				state: start,
				computeAsync: static (state, token) => state.Task,
				canApply: static (state, result) => true,
				apply: _ => Interlocked.Increment(ref appliedCount),
				cancellationToken: TestContext.CancellationToken
			);

			// The cancel races the completing compute. Whichever wins, the classification must stay
			// consistent with whether the result was applied: a run that observes the cancel before its
			// publication checks reports Canceled and never applies, while a cancel that lands too late
			// leaves the run Completed.
			var cancelTask = Task.Run(gate.CancelPendingRequest, TestContext.CancellationToken);
			start.SetResult(42);

			RequestOutcome outcome = await request;
			await cancelTask;

			if (outcome == RequestOutcome.Completed)
			{
				Assert.AreEqual(1, appliedCount, $"Iteration {iteration}: a completed run must apply exactly once.");
			}
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
		var gate = new LatestRequestGate();
		int appliedCount = 0;

		RequestOutcome outcome = await gate.RunAsync(
			state: 0,
			computeAsync: static (state, token) => Task.FromResult(42),

			canApply: (state, result) =>
			{
				// Invalidation inside the documented publication window does not retract a result that is
				// already being published; only checks before the window treat it as supersession.
				gate.Invalidate();
				return true;
			},

			apply: _ => appliedCount++,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(RequestOutcome.Completed, outcome);
		Assert.AreEqual(1, appliedCount);
	}

	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task RunAsync_InvalidateRacingCompletion_ClassifiesConsistently()
	{
		const int iterations = 64;

		for (int iteration = 0; iteration < iterations; iteration++)
		{
			var gate = new LatestRequestGate();
			var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

			int appliedCount = 0;

			Task<RequestOutcome> request = gate.RunAsync(
				state: start,
				computeAsync: static (state, token) => state.Task,
				canApply: static (state, result) => true,
				apply: _ => Interlocked.Increment(ref appliedCount),
				cancellationToken: TestContext.CancellationToken
			);

			// The invalidation races the completing compute: a run that observes it before its
			// publication checks reports supersession without applying, while a late invalidation
			// leaves the run Completed.
			var invalidateTask = Task.Run(gate.Invalidate, TestContext.CancellationToken);
			start.SetResult(42);

			RequestOutcome outcome = await request;
			await invalidateTask;

			if (outcome == RequestOutcome.Completed)
			{
				Assert.AreEqual(1, appliedCount, $"Iteration {iteration}: a completed run must apply exactly once.");
			}
			else
			{
				Assert.AreEqual(RequestOutcome.Superseded, outcome, $"Iteration {iteration}: an unapplied run must report supersession.");
				Assert.AreEqual(0, appliedCount, $"Iteration {iteration}: a superseded run must not apply its result.");
			}
		}
	}
}
