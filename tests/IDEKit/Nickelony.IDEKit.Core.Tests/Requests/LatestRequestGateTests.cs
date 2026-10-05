namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed partial class LatestRequestGateTests
{
	[TestMethod]
	public async Task RunAsync_AppliesLatestResult()
	{
		var gate = new LatestRequestGate();
		int appliedValue = 0;

		RequestOutcome outcome = await gate.RunAsync(
			state: 21,
			computeAsync: static (state, token) => Task.FromResult(state * 2),
			canApply: static (state, result) => true,
			apply: result => appliedValue = result,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(RequestOutcome.Completed, outcome);
		Assert.AreEqual(42, appliedValue);
	}

	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task RunAsync_SupersedesEarlierRequest()
	{
		var gate = new LatestRequestGate();
		var firstStart = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondStart = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

		int appliedCount = 0;

		Task<RequestOutcome> first = gate.RunAsync(
			state: firstStart,
			computeAsync: static (state, token) => state.Task,
			canApply: static (state, result) => true,
			apply: _ => appliedCount++,
			cancellationToken: TestContext.CancellationToken
		);

		Task<RequestOutcome> second = gate.RunAsync(
			state: secondStart,
			computeAsync: static (state, token) => state.Task,
			canApply: static (state, result) => true,
			apply: _ => appliedCount++,
			cancellationToken: TestContext.CancellationToken
		);

		secondStart.SetResult(42);
		Assert.AreEqual(RequestOutcome.Completed, await second);

		firstStart.SetResult(1);

		Assert.AreEqual(RequestOutcome.Superseded, await first);
		Assert.AreEqual(1, appliedCount);
	}

	[TestMethod]
	public async Task RunAsync_NewerRequestStartedInsideCanApply_DoesNotRetractAppliedResult()
	{
		var gate = new LatestRequestGate();
		var appliedValues = new List<int>();

		RequestOutcome outcome = await gate.RunAsync(
			state: 1,
			computeAsync: static (state, token) => Task.FromResult(state),

			canApply: (state, result) =>
			{
				// A newer request starts inside the documented publication window; the result that is
				// already being applied is not retracted, so the host should keep its own state check
				// in canApply when requests can start concurrently with publication.
				_ = gate.RunAsync(
					state: 2,
					computeAsync: static (state, token) => Task.FromResult(state * 10),
					canApply: static (state, result) => true,
					apply: appliedValues.Add,
					cancellationToken: TestContext.CancellationToken
				);

				return true;
			},

			apply: appliedValues.Add,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(RequestOutcome.Completed, outcome);
		Assert.AreSequenceEqual([20, 1], appliedValues);
	}

	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task RunAsync_InvalidatedRequestIsDiscarded()
	{
		var gate = new LatestRequestGate();
		var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

		int appliedCount = 0;

		Task<RequestOutcome> request = gate.RunAsync(
			state: start,
			computeAsync: static (state, token) => state.Task,
			canApply: static (state, result) => true,
			apply: _ => appliedCount++,
			cancellationToken: TestContext.CancellationToken
		);

		gate.Invalidate();
		start.SetResult(42);

		Assert.AreEqual(RequestOutcome.Superseded, await request);
		Assert.AreEqual(0, appliedCount);
	}

	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task RunAsync_CancelPendingRequest_SignalsCancellationAndDiscardsResult()
	{
		var gate = new LatestRequestGate();
		var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

		bool computeObservedCancellation = false;
		int appliedCount = 0;

		Task<RequestOutcome> request = gate.RunAsync(
			state: start,

			computeAsync: (state, token) =>
			{
				token.Register(() => computeObservedCancellation = true);
				return state.Task;
			},

			canApply: static (state, result) => true,
			apply: _ => appliedCount++,
			cancellationToken: TestContext.CancellationToken
		);

		gate.CancelPendingRequest();
		start.SetResult(42);

		Assert.AreEqual(RequestOutcome.Canceled, await request);
		Assert.IsTrue(computeObservedCancellation);
		Assert.AreEqual(0, appliedCount);
	}

	[TestMethod]
	public async Task RunAsync_CanApplyReceivesStateAndResult()
	{
		var gate = new LatestRequestGate();

		int? observedState = null;
		int? observedResult = null;

		int appliedCount = 0;

		RequestOutcome outcome = await gate.RunAsync(
			state: 7,
			computeAsync: static (state, token) => Task.FromResult(state * 3),

			canApply: (state, result) =>
			{
				observedState = state;
				observedResult = result;
				return true;
			},

			apply: _ => appliedCount++,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(RequestOutcome.Completed, outcome);
		Assert.AreEqual(7, observedState);
		Assert.AreEqual(21, observedResult);
		Assert.AreEqual(1, appliedCount);
	}

	[TestMethod]
	public async Task RunAsync_CanApplyFalse_ReportsRejectedByCurrentState()
	{
		var gate = new LatestRequestGate();
		int appliedCount = 0;

		RequestOutcome outcome = await gate.RunAsync(
			state: 0,
			computeAsync: static (state, token) => Task.FromResult(42),
			canApply: static (state, result) => false,
			apply: _ => appliedCount++,
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(RequestOutcome.RejectedByCurrentState, outcome);
		Assert.AreEqual(0, appliedCount);
	}
}
