namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class LatestRequestGateTests
{
	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task RunAsync_CallerTokenCancel_IsObservedByTheComputeDelegate()
	{
		var gate = new LatestRequestGate();
		using var callerCancellation = new CancellationTokenSource();
		var computeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		bool computeObservedCancellation = false;

		Task<RequestOutcome> request = gate.RunAsync(
			state: 1,

			computeAsync: (state, token) =>
			{
				computeStarted.SetResult();
				callerCancellation.Cancel();
				computeObservedCancellation = token.IsCancellationRequested;
				return Task.FromResult(state);
			},

			canApply: static (state, result) => true,
			apply: static _ => { },
			cancellationToken: callerCancellation.Token
		);

		await computeStarted.Task;

		Assert.IsTrue(computeObservedCancellation);
		Assert.AreEqual(RequestOutcome.Canceled, await request);
	}

	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task RunAsync_CallerCancellationDiscardsResult()
	{
		var gate = new LatestRequestGate();
		var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cancellation = new CancellationTokenSource();

		int appliedCount = 0;

		Task<RequestOutcome> request = gate.RunAsync(
			state: start,
			computeAsync: static (state, token) => state.Task,
			canApply: static (state, result) => true,
			apply: _ => appliedCount++,
			cancellationToken: cancellation.Token
		);

		cancellation.Cancel();
		start.SetResult(42);

		Assert.AreEqual(RequestOutcome.Canceled, await request);
		Assert.AreEqual(0, appliedCount);
	}

	[TestMethod]
	public async Task RunAsync_PreCanceledCallerToken_SkipsComputeAndReportsCanceled()
	{
		var gate = new LatestRequestGate();
		using var cancellation = new CancellationTokenSource();

		int computeRuns = 0;

		cancellation.Cancel();

		RequestOutcome outcome = await gate.RunAsync(
			state: 0,

			computeAsync: (state, token) =>
			{
				computeRuns++;
				return Task.FromResult(42);
			},

			canApply: static (state, result) => true,
			apply: _ => { },
			cancellationToken: cancellation.Token
		);

		Assert.AreEqual(RequestOutcome.Canceled, outcome);
		Assert.AreEqual(0, computeRuns);
	}

	[TestMethod]
	public async Task RunAsync_OperationCanceledException_ReportsCanceled()
	{
		var gate = new LatestRequestGate();

		RequestOutcome outcome = await gate.RunAsync<int, int>(
			state: 0,
			computeAsync: static (state, token) => throw new OperationCanceledException(token),
			canApply: static (state, result) => true,
			apply: _ => { },
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(RequestOutcome.Canceled, outcome);
	}

	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task RunAsync_OperationCanceledException_AfterSupersedeReportsSuperseded()
	{
		var gate = new LatestRequestGate();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<RequestOutcome> first = gate.RunAsync<int, int>(
			state: 0,

			computeAsync: async (state, token) =>
			{
				await release.Task.ConfigureAwait(true);
				throw new OperationCanceledException(token);
			},

			canApply: static (state, result) => true,
			apply: _ => { },
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(
			RequestOutcome.Completed,

			await gate.RunAsync(
				state: 1,
				computeAsync: static (state, token) => Task.FromResult(state),
				canApply: static (state, result) => true,
				apply: _ => { },
				cancellationToken: TestContext.CancellationToken
			)
		);

		// The delegate cancels because the second request canceled its token, so the run reports
		// the same outcome as a superseded run that returns normally.
		release.SetResult();

		Assert.AreEqual(RequestOutcome.Superseded, await first);
	}

	[TestMethod]
	public async Task RunAsync_ApplyOperationCanceled_Propagates()
	{
		var gate = new LatestRequestGate();

		// A cancellation thrown by the host's apply delegate is a real failure, not a supersession.
		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
			gate.RunAsync(
				state: 0,
				computeAsync: static (state, token) => Task.FromResult(42),
				canApply: static (state, result) => true,
				apply: _ => throw new OperationCanceledException(),
				cancellationToken: TestContext.CancellationToken
			)
		);
	}

	[TestMethod]
	public async Task RunAsync_CanApplyOperationCanceled_Propagates()
	{
		var gate = new LatestRequestGate();

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
			gate.RunAsync(
				state: 0,
				computeAsync: static (state, token) => Task.FromResult(42),
				canApply: static (state, result) => throw new OperationCanceledException(),
				apply: _ => { },
				cancellationToken: TestContext.CancellationToken
			)
		);
	}

	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task RunAsync_SupersedingRequestCancelsEarlierToken()
	{
		var gate = new LatestRequestGate();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		bool firstTokenObservedCancellation = false;

		Task<RequestOutcome> first = gate.RunAsync<int, int>(
			state: 0,

			computeAsync: async (state, token) =>
			{
				token.Register(() => firstTokenObservedCancellation = true);
				await release.Task.ConfigureAwait(true);
				return state;
			},

			canApply: static (state, result) => true,
			apply: _ => { },
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(
			RequestOutcome.Completed,

			await gate.RunAsync(
				state: 1,
				computeAsync: static (state, token) => Task.FromResult(state),
				canApply: static (state, result) => true,
				apply: _ => { },
				cancellationToken: TestContext.CancellationToken
			)
		);

		release.SetResult();

		Assert.AreEqual(RequestOutcome.Superseded, await first);
		Assert.IsTrue(firstTokenObservedCancellation);
	}

	public TestContext? TestContext { get; set; }
}
