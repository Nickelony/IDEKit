namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed partial class LatestRequestCoordinatorTests
{
	[TestMethod]
	public async Task RunAsync_AppliesLatestResult()
	{
		var coordinator = new LatestRequestCoordinator();
		int appliedValue = 0;

		RequestOutcome outcome = await coordinator.RunAsync(
			21,
			static (state, token) => Task.FromResult(state * 2),
			static (state, result) => true,
			result => appliedValue = result);

		Assert.AreEqual(RequestOutcome.Completed, outcome);
		Assert.AreEqual(42, appliedValue);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_SupersedesEarlierRequest()
	{
		var coordinator = new LatestRequestCoordinator();
		var firstStart = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondStart = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		int appliedCount = 0;

		Task<RequestOutcome> first = coordinator.RunAsync(
			firstStart,
			static (state, token) => state.Task,
			static (state, result) => true,
			_ => appliedCount++);

		Task<RequestOutcome> second = coordinator.RunAsync(
			secondStart,
			static (state, token) => state.Task,
			static (state, result) => true,
			_ => appliedCount++);

		secondStart.SetResult(42);
		Assert.AreEqual(RequestOutcome.Completed, await second);

		firstStart.SetResult(1);
		Assert.AreEqual(RequestOutcome.Superseded, await first);
		Assert.AreEqual(1, appliedCount);
	}

	[TestMethod]
	public async Task RunAsync_NewerRequestStartedInsideCanApply_DoesNotRetractAppliedResult()
	{
		var coordinator = new LatestRequestCoordinator();
		var appliedValues = new List<int>();

		RequestOutcome outcome = await coordinator.RunAsync(
			1,
			static (state, token) => Task.FromResult(state),
			(state, result) =>
			{
				// A newer request starts inside the documented publication window; the result that is
				// already being applied is not retracted, so the host should keep its own state check
				// in canApply when requests can start concurrently with publication.
				_ = coordinator.RunAsync(
					2,
					static (state, token) => Task.FromResult(state * 10),
					static (state, result) => true,
					appliedValues.Add);

				return true;
			},
			appliedValues.Add);

		Assert.AreEqual(RequestOutcome.Completed, outcome);
		CollectionAssert.AreEqual(new[] { 20, 1 }, appliedValues);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_InvalidatedRequestIsDiscarded()
	{
		var coordinator = new LatestRequestCoordinator();
		var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		int appliedCount = 0;

		Task<RequestOutcome> request = coordinator.RunAsync(
			start,
			static (state, token) => state.Task,
			static (state, result) => true,
			_ => appliedCount++);

		coordinator.Invalidate();
		start.SetResult(42);

		Assert.AreEqual(RequestOutcome.Superseded, await request);
		Assert.AreEqual(0, appliedCount);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_CancelPendingRequest_SignalsCancellationAndDiscardsResult()
	{
		var coordinator = new LatestRequestCoordinator();
		var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		bool computeObservedCancellation = false;
		int appliedCount = 0;

		Task<RequestOutcome> request = coordinator.RunAsync(
			start,
			(state, token) =>
			{
				token.Register(() => computeObservedCancellation = true);
				return state.Task;
			},
			static (state, result) => true,
			_ => appliedCount++);

		coordinator.CancelPendingRequest();
		start.SetResult(42);

		Assert.AreEqual(RequestOutcome.Canceled, await request);
		Assert.IsTrue(computeObservedCancellation);
		Assert.AreEqual(0, appliedCount);
	}

	[TestMethod]
	public async Task RunAsync_CanApplyReceivesStateAndResult()
	{
		var coordinator = new LatestRequestCoordinator();
		int? observedState = null;
		int? observedResult = null;
		int appliedCount = 0;

		RequestOutcome outcome = await coordinator.RunAsync(
			7,
			static (state, token) => Task.FromResult(state * 3),
			(state, result) =>
			{
				observedState = state;
				observedResult = result;
				return true;
			},
			_ => appliedCount++);

		Assert.AreEqual(RequestOutcome.Completed, outcome);
		Assert.AreEqual(7, observedState);
		Assert.AreEqual(21, observedResult);
		Assert.AreEqual(1, appliedCount);
	}

	[TestMethod]
	public async Task RunAsync_CanApplyFalse_ReportsRejectedByCurrentState()
	{
		var coordinator = new LatestRequestCoordinator();
		int appliedCount = 0;

		RequestOutcome outcome = await coordinator.RunAsync(
			0,
			static (state, token) => Task.FromResult(42),
			static (state, result) => false,
			_ => appliedCount++);

		Assert.AreEqual(RequestOutcome.RejectedByCurrentState, outcome);
		Assert.AreEqual(0, appliedCount);
	}
}
