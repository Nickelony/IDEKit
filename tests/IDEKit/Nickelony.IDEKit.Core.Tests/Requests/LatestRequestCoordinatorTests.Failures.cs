namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class LatestRequestCoordinatorTests
{
	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_ThrowingCallbackOnSupersededRun_DoesNotFaultTheNewRequest()
	{
		var coordinator = new LatestRequestCoordinator();
		var firstCompute = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		var callbackRegistered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<RequestOutcome> first = coordinator.RunAsync(
			1,
			(state, token) =>
			{
				token.Register(static () => throw new InvalidOperationException("Cancellation callback failure."));
				callbackRegistered.SetResult();
				return firstCompute.Task;
			},
			static (state, result) => true,
			static _ => { });

		await callbackRegistered.Task;

		// Superseding cancels the first run, which runs its throwing callback. The aggregate must not
		// fault this new request or leak its cancellation source.
		RequestOutcome secondOutcome = await coordinator.RunAsync(
			2,
			static (state, token) => Task.FromResult(state * 10),
			static (state, result) => true,
			static _ => { });

		Assert.AreEqual(RequestOutcome.Completed, secondOutcome);

		firstCompute.SetResult(1);
		Assert.AreEqual(RequestOutcome.Superseded, await first);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task CancelPendingRequest_ThrowingCallbackOnTheRun_DoesNotEscapeTheCancelCall()
	{
		var coordinator = new LatestRequestCoordinator();
		var computeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<RequestOutcome> request = coordinator.RunAsync(
			start,
			(state, token) =>
			{
				token.Register(static () => throw new InvalidOperationException("Cancellation callback failure."));
				computeStarted.SetResult();
				return state.Task;
			},
			static (state, result) => true,
			static _ => { });

		await computeStarted.Task;

		// Canceling runs the run token's throwing callback. The fault belongs to the run, so it must
		// be absorbed exactly like the supersede path absorbs it, not escape this call.
		coordinator.CancelPendingRequest();

		start.SetResult(42);

		Assert.AreEqual(RequestOutcome.Canceled, await request);
	}

	[TestMethod]
	public async Task RunAsync_PropagatesComputeException()
	{
		var coordinator = new LatestRequestCoordinator();

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
			coordinator.RunAsync<int, int>(
				0,
				static (state, token) => throw new InvalidOperationException("boom"),
				static (state, result) => true,
				_ => { }));
	}

	[TestMethod]
	public async Task RunAsync_ComputeException_OnCurrentRun_RunsFailureCallbackBeforePropagating()
	{
		var coordinator = new LatestRequestCoordinator();
		Exception? reported = null;
		int failureCount = 0;

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
			coordinator.RunAsync<int, int>(
				0,
				static (state, token) => throw new InvalidOperationException("boom"),
				static (state, result) => true,
				static _ => { },
				onFailure: exception =>
				{
					failureCount++;
					reported = exception;
				}));

		Assert.AreEqual(1, failureCount);
		Assert.IsNotNull(reported);
		Assert.AreEqual("boom", reported!.Message);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_ComputeException_OnSupersededRun_DoesNotRunFailureCallback()
	{
		var coordinator = new LatestRequestCoordinator();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		int failureCount = 0;

		Task<RequestOutcome> first = coordinator.RunAsync<int, int>(
			0,
			async (state, token) =>
			{
				await release.Task.ConfigureAwait(true);
				throw new InvalidOperationException("stale failure");
			},
			static (state, result) => true,
			static _ => { },
			onFailure: _ => failureCount++);

		Assert.AreEqual(RequestOutcome.Completed, await coordinator.RunAsync(
			1,
			static (state, token) => Task.FromResult(state),
			static (state, result) => true,
			static _ => { }));

		release.SetResult();

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => first);
		Assert.AreEqual(0, failureCount);
	}

	[TestMethod]
	[Timeout(30_000)]
	public async Task RunAsync_ComputeException_OnInvalidatedRun_DoesNotRunFailureCallback()
	{
		var coordinator = new LatestRequestCoordinator();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		int failureCount = 0;

		Task<RequestOutcome> request = coordinator.RunAsync<int, int>(
			0,
			async (state, token) =>
			{
				await release.Task.ConfigureAwait(true);
				throw new InvalidOperationException("invalidated failure");
			},
			static (state, result) => true,
			static _ => { },
			onFailure: _ => failureCount++);

		coordinator.Invalidate();
		release.SetResult();

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => request);
		Assert.AreEqual(0, failureCount);
	}

	[TestMethod]
	public async Task RunAsync_CanApplyException_RunsFailureCallbackBeforePropagating()
	{
		var coordinator = new LatestRequestCoordinator();
		int failureCount = 0;

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
			coordinator.RunAsync<int, int>(
				0,
				static (state, token) => Task.FromResult(42),
				static (state, result) => throw new InvalidOperationException("canApply failed"),
				static _ => { },
				onFailure: _ => failureCount++));

		Assert.AreEqual(1, failureCount);
	}

	[TestMethod]
	public async Task RunAsync_ApplyException_RunsFailureCallbackBeforePropagating()
	{
		var coordinator = new LatestRequestCoordinator();
		int failureCount = 0;

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
			coordinator.RunAsync<int, int>(
				0,
				static (state, token) => Task.FromResult(42),
				static (state, result) => true,
				static _ => throw new InvalidOperationException("apply failed"),
				onFailure: _ => failureCount++));

		Assert.AreEqual(1, failureCount);
	}

	[TestMethod]
	public async Task RunAsync_ApplyThrowsOperationCanceled_DoesNotRunFailureCallbackButPropagates()
	{
		var coordinator = new LatestRequestCoordinator();
		int failureCount = 0;

		// A cancellation thrown by the host's apply delegate is a real failure that propagates, but it is
		// not a reportable run failure, so the callback stays silent.
		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
			coordinator.RunAsync<int, int>(
				0,
				static (state, token) => Task.FromResult(42),
				static (state, result) => true,
				static _ => throw new OperationCanceledException(),
				onFailure: _ => failureCount++));

		Assert.AreEqual(0, failureCount);
	}

	[TestMethod]
	public async Task RunAsync_NullDelegates_Throw()
	{
		var coordinator = new LatestRequestCoordinator();

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => coordinator.RunAsync<int, int>(
			0, null!, static (_, _) => true, static _ => { }));
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => coordinator.RunAsync(
			0, static (_, _) => Task.FromResult(0), null!, static _ => { }));
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => coordinator.RunAsync<int, int>(
			0, static (_, _) => Task.FromResult(0), static (_, _) => true, null!));
	}
}
