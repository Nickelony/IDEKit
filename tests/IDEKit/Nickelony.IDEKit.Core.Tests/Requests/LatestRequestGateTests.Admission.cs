namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class LatestRequestGateTests
{
	[TestMethod]
	public async Task RunIfAsync_PredicateAccepts_RunsLikeRunAsync()
	{
		var gate = new LatestRequestGate();
		int appliedValue = 0;

		RequestOutcome outcome = await gate.RunIfAsync(
			canAdmit: static () => true,
			state: 21,
			computeAsync: static (state, token) => Task.FromResult(state * 2),
			canApply: static (state, result) => true,
			apply: result => appliedValue = result
		);

		Assert.AreEqual(RequestOutcome.Completed, outcome);
		Assert.AreEqual(42, appliedValue);
	}

	[TestMethod]
	public async Task RunIfAsync_PredicateRejects_ReportsCanceledAndRunsNoDelegate()
	{
		var gate = new LatestRequestGate();
		int delegateCount = 0;

		RequestOutcome outcome = await gate.RunIfAsync(
			canAdmit: static () => false,
			state: 0,

			computeAsync: (state, token) =>
			{
				delegateCount++;
				return Task.FromResult(state);
			},

			canApply: (state, result) =>
			{
				delegateCount++;
				return true;
			},

			apply: _ => delegateCount++
		);

		Assert.AreEqual(RequestOutcome.Canceled, outcome);
		Assert.AreEqual(0, delegateCount);
	}

	[TestMethod]
	public async Task RunIfAsync_PredicateRejects_LeavesTheOutstandingHostRequestUntouched()
	{
		var gate = new LatestRequestGate();

		RequestHandle outstanding = gate.BeginRequest();

		RequestOutcome outcome = await gate.RunIfAsync(
			canAdmit: static () => false,
			state: 0,
			computeAsync: static (state, token) => Task.FromResult(state),
			canApply: static (state, result) => true,
			apply: static _ => { }
		);

		Assert.AreEqual(RequestOutcome.Canceled, outcome);
		Assert.IsFalse(outstanding.CancellationToken.IsCancellationRequested, "A rejected run must not cancel the outstanding request.");
		Assert.IsTrue(gate.CanPublish(outstanding), "A rejected run must not supersede the outstanding request.");
	}

	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task RunIfAsync_PredicateRejects_LeavesTheOutstandingRunUntouched()
	{
		var gate = new LatestRequestGate();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var runStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		int appliedCount = 0;

		Task<RequestOutcome> run = gate.RunAsync<int, int>(
			state: 0,

			computeAsync: async (state, token) =>
			{
				runStarted.SetResult();
				await release.Task.ConfigureAwait(true);
				return state;
			},

			canApply: static (state, result) => true,
			apply: _ => appliedCount++
		);

		await runStarted.Task;

		RequestOutcome rejected = await gate.RunIfAsync(
			canAdmit: static () => false,
			state: 0,
			computeAsync: static (state, token) => Task.FromResult(state),
			canApply: static (state, result) => true,
			apply: static _ => { }
		);

		release.SetResult();

		Assert.AreEqual(RequestOutcome.Canceled, rejected);
		Assert.AreEqual(RequestOutcome.Completed, await run, "A rejected run must not supersede the run in flight.");
		Assert.AreEqual(1, appliedCount);
	}

	[TestMethod]
	public async Task RunIfAsync_PredicateAccepts_SupersedesAnOutstandingHostRequest()
	{
		var gate = new LatestRequestGate();

		RequestHandle outstanding = gate.BeginRequest();

		RequestOutcome outcome = await gate.RunIfAsync(
			canAdmit: static () => true,
			state: 0,
			computeAsync: static (state, token) => Task.FromResult(state),
			canApply: static (state, result) => true,
			apply: static _ => { }
		);

		Assert.AreEqual(RequestOutcome.Completed, outcome);
		Assert.IsTrue(outstanding.CancellationToken.IsCancellationRequested);
		Assert.IsFalse(gate.IsCurrent(outstanding));
	}

	[TestMethod]
	public async Task RunIfAsync_PredicateRuns_UnderTheGateLock()
	{
		var gate = new LatestRequestGate();

		RequestHandle outstanding = gate.BeginRequest();
		Task<bool>? probe = null;
		bool probeBlockedDuringPredicate = false;

		await gate.RunIfAsync(
			canAdmit: () =>
			{
				// IsCurrent takes the gate lock for a real request, so the probe cannot finish while the predicate holds it.
				probe = Task.Run(() => gate.IsCurrent(outstanding));
				probeBlockedDuringPredicate = !probe.Wait(TimeSpan.FromMilliseconds(100.0));

				return false;
			},

			state: 0,
			computeAsync: static (state, token) => Task.FromResult(state),
			canApply: static (state, result) => true,
			apply: static _ => { }
		);

		Assert.IsTrue(probeBlockedDuringPredicate, "The admission predicate must run inside the gate's critical section.");
		Assert.IsNotNull(probe);
		Assert.IsTrue(await probe.WaitAsync(TimeSpan.FromSeconds(10.0)), "The probe finishes after the predicate releases the lock.");
	}

	[TestMethod]
	public void RunIfAsync_NullPredicate_ThrowsSynchronously()
	{
		var gate = new LatestRequestGate();

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			gate.RunIfAsync<int, int>(
				canAdmit: null!,
				state: 0,
				computeAsync: static (_, _) => Task.FromResult(0),
				canApply: static (_, _) => true,
				apply: static _ => { }
			)
		);
	}

	[TestMethod]
	public async Task RunIfAsync_NullDelegates_FaultTheReturnedTask()
	{
		var gate = new LatestRequestGate();

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
			gate.RunIfAsync<int, int>(
				canAdmit: static () => true,
				state: 0,
				computeAsync: null!,
				canApply: static (_, _) => true,
				apply: static _ => { }
			)
		);

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
			gate.RunIfAsync(
				canAdmit: static () => true,
				state: 0,
				computeAsync: static (_, _) => Task.FromResult(0),
				canApply: null!,
				apply: static _ => { }
			)
		);

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
			gate.RunIfAsync<int, int>(
				canAdmit: static () => true,
				state: 0,
				computeAsync: static (_, _) => Task.FromResult(0),
				canApply: static (_, _) => true,
				apply: null!
			)
		);
	}
}
