namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class LatestRequestGateTests
{
	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task RunAsync_ThrowingCallbackOnSupersededRun_DoesNotFaultTheNewRequest()
	{
		var gate = new LatestRequestGate();
		var firstCompute = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		var callbackRegistered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<RequestOutcome> first = gate.RunAsync(
			state: 1,

			computeAsync: (state, token) =>
			{
				token.Register(static () => throw new InvalidOperationException("Cancellation callback failure."));
				callbackRegistered.SetResult();
				return firstCompute.Task;
			},

			canApply: static (state, result) => true,
			apply: static _ => { }
		);

		await callbackRegistered.Task;

		// Superseding cancels the first run, which runs its throwing callback. The aggregate must not
		// fault this new request or leak its cancellation source.
		RequestOutcome secondOutcome = await gate.RunAsync(
			state: 2,
			computeAsync: static (state, token) => Task.FromResult(state * 10),
			canApply: static (state, result) => true,
			apply: static _ => { }
		);

		Assert.AreEqual(RequestOutcome.Completed, secondOutcome);

		firstCompute.SetResult(1);
		Assert.AreEqual(RequestOutcome.Superseded, await first);
	}

	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task CancelPendingRequest_ThrowingCallbackOnTheRun_DoesNotEscapeTheCancelCall()
	{
		var gate = new LatestRequestGate();
		var computeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<RequestOutcome> request = gate.RunAsync(
			state: start,

			computeAsync: (state, token) =>
			{
				token.Register(static () => throw new InvalidOperationException("Cancellation callback failure."));
				computeStarted.SetResult();
				return state.Task;
			},

			canApply: static (state, result) => true,
			apply: static _ => { }
		);

		await computeStarted.Task;

		// Canceling runs the run token's throwing callback. The fault belongs to the run, so it must
		// be absorbed exactly like the supersede path absorbs it, not escape this call.
		gate.CancelPendingRequest();

		start.SetResult(42);

		Assert.AreEqual(RequestOutcome.Canceled, await request);
	}

	[TestMethod]
	public async Task RunAsync_PropagatesComputeException()
	{
		var gate = new LatestRequestGate();

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
			gate.RunAsync<int, int>(
				state: 0,
				computeAsync: static (state, token) => throw new InvalidOperationException("boom"),
				canApply: static (state, result) => true,
				apply: _ => { }
			)
		);
	}

	[TestMethod]
	public async Task RunAsync_ComputeException_OnCurrentRun_RunsFailureCallbackBeforePropagating()
	{
		var gate = new LatestRequestGate();

		Exception? reported = null;
		int failureCount = 0;

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
			gate.RunAsync<int, int>(
				state: 0,
				computeAsync: static (state, token) => throw new InvalidOperationException("boom"),
				canApply: static (state, result) => true,
				apply: static _ => { },

				onFailure: exception =>
				{
					failureCount++;
					reported = exception;
				}
			)
		);

		Assert.AreEqual(1, failureCount);
		Assert.IsNotNull(reported);
		Assert.AreEqual("boom", reported!.Message);
	}

	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task RunAsync_ComputeException_OnSupersededRun_DoesNotRunFailureCallback()
	{
		var gate = new LatestRequestGate();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		int failureCount = 0;

		Task<RequestOutcome> first = gate.RunAsync<int, int>(
			state: 0,

			computeAsync: async (state, token) =>
			{
				await release.Task.ConfigureAwait(true);
				throw new InvalidOperationException("stale failure");
			},

			canApply: static (state, result) => true,
			apply: static _ => { },
			onFailure: _ => failureCount++
		);

		Assert.AreEqual(
			RequestOutcome.Completed,

			await gate.RunAsync(
				state: 1,
				computeAsync: static (state, token) => Task.FromResult(state),
				canApply: static (state, result) => true,
				apply: static _ => { }
			)
		);

		release.SetResult();

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => first);
		Assert.AreEqual(0, failureCount);
	}

	[TestMethod]
	[Timeout(30_000, CooperativeCancellation = true)]
	public async Task RunAsync_ComputeException_OnInvalidatedRun_DoesNotRunFailureCallback()
	{
		var gate = new LatestRequestGate();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		int failureCount = 0;

		Task<RequestOutcome> request = gate.RunAsync<int, int>(
			state: 0,

			computeAsync: async (state, token) =>
			{
				await release.Task.ConfigureAwait(true);
				throw new InvalidOperationException("invalidated failure");
			},

			canApply: static (state, result) => true,
			apply: static _ => { },
			onFailure: _ => failureCount++
		);

		gate.Invalidate();
		release.SetResult();

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => request);
		Assert.AreEqual(0, failureCount);
	}

	[TestMethod]
	public async Task RunAsync_CanApplyException_RunsFailureCallbackBeforePropagating()
	{
		var gate = new LatestRequestGate();
		int failureCount = 0;

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
			gate.RunAsync<int, int>(
				state: 0,
				computeAsync: static (state, token) => Task.FromResult(42),
				canApply: static (state, result) => throw new InvalidOperationException("canApply failed"),
				apply: static _ => { },
				onFailure: _ => failureCount++
			)
		);

		Assert.AreEqual(1, failureCount);
	}

	[TestMethod]
	public async Task RunAsync_ApplyException_RunsFailureCallbackBeforePropagating()
	{
		var gate = new LatestRequestGate();
		int failureCount = 0;

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
			gate.RunAsync<int, int>(
				state: 0,
				computeAsync: static (state, token) => Task.FromResult(42),
				canApply: static (state, result) => true,
				apply: static _ => throw new InvalidOperationException("apply failed"),
				onFailure: _ => failureCount++
			)
		);

		Assert.AreEqual(1, failureCount);
	}

	[TestMethod]
	public async Task RunAsync_ApplyThrowsOperationCanceled_DoesNotRunFailureCallbackButPropagates()
	{
		var gate = new LatestRequestGate();
		int failureCount = 0;

		// A cancellation thrown by the host's apply delegate is a real failure that propagates, but it is
		// not a reportable run failure, so the callback stays silent.
		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
			gate.RunAsync<int, int>(
				state: 0,
				computeAsync: static (state, token) => Task.FromResult(42),
				canApply: static (state, result) => true,
				apply: static _ => throw new OperationCanceledException(),
				onFailure: _ => failureCount++
			)
		);

		Assert.AreEqual(0, failureCount);
	}

	[TestMethod]
	public async Task RunAsync_NullDelegates_Throw()
	{
		var gate = new LatestRequestGate();

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
			gate.RunAsync<int, int>(state: 0, computeAsync: null!, canApply: static (_, _) => true, apply: static _ => { })
		);

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
			gate.RunAsync(state: 0, computeAsync: static (_, _) => Task.FromResult(0), canApply: null!, apply: static _ => { })
		);

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
			gate.RunAsync<int, int>(state: 0, computeAsync: static (_, _) => Task.FromResult(0), canApply: static (_, _) => true, apply: null!)
		);
	}
}
