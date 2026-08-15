namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class LatestRequestCoordinatorTests
{
	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_CallerTokenCancel_IsObservedByTheComputeDelegate()
	{
		var coordinator = new LatestRequestCoordinator();
		using var callerCancellation = new CancellationTokenSource();
		var computeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		bool computeObservedCancellation = false;

		Task<RequestOutcome> request = coordinator.RunAsync(
			1,
			(state, token) =>
			{
				computeStarted.SetResult();
				callerCancellation.Cancel();
				computeObservedCancellation = token.IsCancellationRequested;
				return Task.FromResult(state);
			},
			static (state, result) => true,
			static _ => { },
			cancellationToken: callerCancellation.Token);

		await computeStarted.Task;

		Assert.IsTrue(computeObservedCancellation);
		Assert.AreEqual(RequestOutcome.Canceled, await request);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_CallerCancellationDiscardsResult()
	{
		var coordinator = new LatestRequestCoordinator();
		var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cancellation = new CancellationTokenSource();
		int appliedCount = 0;

		Task<RequestOutcome> request = coordinator.RunAsync(
			start,
			static (state, token) => state.Task,
			static (state, result) => true,
			_ => appliedCount++,
			cancellationToken: cancellation.Token);

		cancellation.Cancel();
		start.SetResult(42);

		Assert.AreEqual(RequestOutcome.Canceled, await request);
		Assert.AreEqual(0, appliedCount);
	}

	[TestMethod]
	public async Task RunAsync_PreCanceledCallerToken_SkipsComputeAndReportsCanceled()
	{
		var coordinator = new LatestRequestCoordinator();
		using var cancellation = new CancellationTokenSource();
		int computeRuns = 0;

		cancellation.Cancel();

		RequestOutcome outcome = await coordinator.RunAsync(
			0,
			(state, token) =>
			{
				computeRuns++;
				return Task.FromResult(42);
			},
			static (state, result) => true,
			_ => { },
			cancellationToken: cancellation.Token);

		Assert.AreEqual(RequestOutcome.Canceled, outcome);
		Assert.AreEqual(0, computeRuns);
	}

	[TestMethod]
	public async Task RunAsync_OperationCanceledException_ReportsCanceled()
	{
		var coordinator = new LatestRequestCoordinator();

		RequestOutcome outcome = await coordinator.RunAsync<int, int>(
			0,
			static (state, token) => throw new OperationCanceledException(token),
			static (state, result) => true,
			_ => { });

		Assert.AreEqual(RequestOutcome.Canceled, outcome);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_OperationCanceledException_AfterSupersedeReportsSuperseded()
	{
		var coordinator = new LatestRequestCoordinator();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<RequestOutcome> first = coordinator.RunAsync<int, int>(
			0,
			async (state, token) =>
			{
				await release.Task.ConfigureAwait(true);
				throw new OperationCanceledException(token);
			},
			static (state, result) => true,
			_ => { });

		Assert.AreEqual(RequestOutcome.Completed, await coordinator.RunAsync(
			1,
			static (state, token) => Task.FromResult(state),
			static (state, result) => true,
			_ => { }));

		// The delegate cancels because the second request canceled its token, so the run reports
		// the same outcome as a superseded run that returns normally.
		release.SetResult();

		Assert.AreEqual(RequestOutcome.Superseded, await first);
	}

	[TestMethod]
	public async Task RunAsync_ApplyOperationCanceled_Propagates()
	{
		var coordinator = new LatestRequestCoordinator();

		// A cancellation thrown by the host's apply delegate is a real failure, not a supersession.
		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
			coordinator.RunAsync(
				0,
				static (state, token) => Task.FromResult(42),
				static (state, result) => true,
				_ => throw new OperationCanceledException()));
	}

	[TestMethod]
	public async Task RunAsync_CanApplyOperationCanceled_Propagates()
	{
		var coordinator = new LatestRequestCoordinator();

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
			coordinator.RunAsync(
				0,
				static (state, token) => Task.FromResult(42),
				static (state, result) => throw new OperationCanceledException(),
				_ => { }));
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_SupersedingRequestCancelsEarlierToken()
	{
		var coordinator = new LatestRequestCoordinator();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		bool firstTokenObservedCancellation = false;

		Task<RequestOutcome> first = coordinator.RunAsync<int, int>(
			0,
			async (state, token) =>
			{
				token.Register(() => firstTokenObservedCancellation = true);
				await release.Task.ConfigureAwait(true);
				return state;
			},
			static (state, result) => true,
			_ => { });

		Assert.AreEqual(RequestOutcome.Completed, await coordinator.RunAsync(
			1,
			static (state, token) => Task.FromResult(state),
			static (state, result) => true,
			_ => { }));

		release.SetResult();

		Assert.AreEqual(RequestOutcome.Superseded, await first);
		Assert.IsTrue(firstTokenObservedCancellation);
	}
}
