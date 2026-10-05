using Nickelony.IDEKit.Core.Requests;

namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the request-lifetime gate: requests supersede, cancel, and invalidate through the lifetime, and disposal cancels the request in flight,
/// rejects later requests, and turns the later lifecycle calls into no-ops.
/// </summary>
[TestClass]
public sealed class RequestLifetimeTests
{
	[TestMethod]
	public void AdvanceGeneration_MakesEveryEarlierGenerationStale()
	{
		using var lifetime = new RequestLifetime();

		int first = lifetime.AdvanceGeneration();

		Assert.IsTrue(lifetime.IsCurrent(first));

		int second = lifetime.AdvanceGeneration();

		Assert.IsFalse(lifetime.IsCurrent(first));
		Assert.IsTrue(lifetime.IsCurrent(second));
	}

	[TestMethod]
	public void IsCurrent_AfterDisposal_IsFalseForTheNewestGeneration()
	{
		var lifetime = new RequestLifetime();
		int generation = lifetime.AdvanceGeneration();

		lifetime.Dispose();

		Assert.IsFalse(lifetime.IsCurrent(generation));
	}

	[TestMethod]
	public void RunAsync_AppliesTheComputedResult()
	{
		using var lifetime = new RequestLifetime();
		int applied = 0;

		Task<RequestOutcome> run = lifetime.RunAsync(
			state: 21,
			computeAsync: static (state, _) => Task.FromResult(state * 2),
			apply: result => applied = result
		);

		Assert.AreEqual(RequestOutcome.Completed, Complete(run));
		Assert.AreEqual(42, applied);
	}

	[TestMethod]
	public void RunAsync_CallerTokenCanceled_ReportsCanceledAndAppliesNothing()
	{
		using var lifetime = new RequestLifetime();
		int applied = 0;

		Task<RequestOutcome> run = lifetime.RunAsync(
			state: 0,
			computeAsync: static (state, _) => Task.FromResult(state),
			apply: _ => applied++,
			cancellationToken: new CancellationToken(canceled: true)
		);

		Assert.AreEqual(RequestOutcome.Canceled, Complete(run));
		Assert.AreEqual(0, applied);
	}

	[TestMethod]
	public async Task RunAsync_ProviderFails_ReportsTheFailureAndPropagatesIt()
	{
		using var lifetime = new RequestLifetime();
		Exception? reported = null;

		Task<RequestOutcome> run = lifetime.RunAsync<int, int>(
			state: 0,
			computeAsync: static (_, _) => throw new InvalidOperationException("Provider failed."),
			apply: static _ => { },
			onFailure: exception => reported = exception
		);

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => run);
		Assert.IsInstanceOfType<InvalidOperationException>(reported);
	}

	[TestMethod]
	public void RunAsync_NewerRequest_SupersedesTheRequestInFlight()
	{
		using var lifetime = new RequestLifetime();
		using var first = new GatedRun(lifetime);

		Task<RequestOutcome> second = lifetime.RunAsync(state: 0, computeAsync: static (state, _) => Task.FromResult(state), apply: static _ => { });

		Assert.AreEqual(RequestOutcome.Completed, Complete(second));
		Assert.IsTrue(first.Token.IsCancellationRequested, "A newer request cancels the previous request's token.");

		first.Release();

		Assert.AreEqual(RequestOutcome.Superseded, Complete(first.Outcome));
		Assert.AreEqual(0, first.AppliedCount);
	}

	[TestMethod]
	public void CancelInFlightRequest_CancelsTheTokenAndDiscardsTheResult()
	{
		using var lifetime = new RequestLifetime();
		using var run = new GatedRun(lifetime);

		lifetime.CancelInFlightRequest();

		Assert.IsTrue(run.Token.IsCancellationRequested);

		run.Release();

		Assert.AreEqual(RequestOutcome.Canceled, Complete(run.Outcome), "Cancellation is not invalidation.");
		Assert.AreEqual(0, run.AppliedCount);
	}

	[TestMethod]
	public void InvalidateRequests_DiscardsTheResultWithoutCancelingTheToken()
	{
		using var lifetime = new RequestLifetime();
		using var run = new GatedRun(lifetime);

		lifetime.InvalidateRequests();

		Assert.IsFalse(run.Token.IsCancellationRequested);

		run.Release();

		Assert.AreEqual(RequestOutcome.Superseded, Complete(run.Outcome));
		Assert.AreEqual(0, run.AppliedCount);
	}

	[TestMethod]
	public void CancelAndInvalidate_CancelsTheTokenAndSupersedesTheRequest()
	{
		using var lifetime = new RequestLifetime();
		using var run = new GatedRun(lifetime);

		lifetime.CancelAndInvalidate();

		Assert.IsTrue(run.Token.IsCancellationRequested);

		run.Release();

		Assert.AreEqual(RequestOutcome.Superseded, Complete(run.Outcome));
		Assert.AreEqual(0, run.AppliedCount);
	}

	[TestMethod]
	public void CancelAndInvalidate_WithoutARequest_IsANoOpAndLaterRequestsStayCurrent()
	{
		using var lifetime = new RequestLifetime();

		lifetime.CancelInFlightRequest();
		lifetime.InvalidateRequests();
		lifetime.CancelAndInvalidate();

		Task<RequestOutcome> run = lifetime.RunAsync(state: 0, computeAsync: static (state, _) => Task.FromResult(state), apply: static _ => { });

		Assert.AreEqual(RequestOutcome.Completed, Complete(run));
	}

	[TestMethod]
	public void Dispose_CancelsTheLifetimeTokenAndTheRequestInFlight()
	{
		var lifetime = new RequestLifetime();
		using var run = new GatedRun(lifetime);

		Assert.IsFalse(lifetime.IsDisposed);
		Assert.IsFalse(lifetime.LifetimeToken.IsCancellationRequested);
		Assert.IsFalse(run.Token.IsCancellationRequested);

		lifetime.Dispose();

		Assert.IsTrue(lifetime.IsDisposed);
		Assert.IsTrue(lifetime.LifetimeToken.IsCancellationRequested);
		Assert.IsTrue(run.Token.IsCancellationRequested);

		run.Release();

		Assert.AreEqual(RequestOutcome.Canceled, Complete(run.Outcome), "A request in flight at disposal is canceled, not superseded.");
		Assert.AreEqual(0, run.AppliedCount);
	}

	[TestMethod]
	public void Dispose_CalledTwice_IsIdempotent()
	{
		var lifetime = new RequestLifetime();

		lifetime.Dispose();
		lifetime.Dispose();

		Assert.IsTrue(lifetime.IsDisposed);
		Assert.IsTrue(lifetime.LifetimeToken.IsCancellationRequested);
	}

	[TestMethod]
	public void RunAsync_AfterDispose_ReportsCanceledAndRunsNoDelegate()
	{
		var lifetime = new RequestLifetime();
		int delegateCount = 0;

		lifetime.Dispose();

		Task<RequestOutcome> run = lifetime.RunAsync(
			state: 0,

			computeAsync: (state, _) =>
			{
				delegateCount++;
				return Task.FromResult(state);
			},

			apply: _ => delegateCount++
		);

		Assert.AreEqual(RequestOutcome.Canceled, Complete(run));
		Assert.AreEqual(0, delegateCount, "A disposed lifetime admits no request, so the provider is never called.");
	}

	[TestMethod]
	public void LifecycleCalls_AfterDispose_DoNotChangeTheOutcomeOfTheRequestInFlight()
	{
		var lifetime = new RequestLifetime();
		using var run = new GatedRun(lifetime);

		lifetime.Dispose();

		// The post-dispose lifecycle calls must not throw, and they must not invalidate the request, because an
		// invalidation would turn the cancellation that disposal reports into a supersession.
		lifetime.CancelInFlightRequest();
		lifetime.InvalidateRequests();
		lifetime.CancelAndInvalidate();

		run.Release();

		Assert.AreEqual(RequestOutcome.Canceled, Complete(run.Outcome));
		Assert.IsTrue(lifetime.IsDisposed);
	}

	[TestMethod]
	public void RunAsync_DisposeRacingTheCompletion_ReportsCompletedOrCanceledNeverRejected()
	{
		for (int iteration = 0; iteration < 200; iteration++)
		{
			var lifetime = new RequestLifetime();
			using var run = new GatedRun(lifetime);
			using var start = new ManualResetEventSlim();

			var releaser = Task.Run(() =>
			{
				start.Wait();
				run.Release();
			});

			var disposer = Task.Run(() =>
			{
				start.Wait();
				lifetime.Dispose();
			});

			start.Set();
			Task.WaitAll(releaser, disposer);

			RequestOutcome outcome = Complete(run.Outcome);

			// A dispose that lands between the coordinator's latest-request check and the disposal gate is still a cancellation of the owner.
			Assert.IsTrue(outcome is RequestOutcome.Completed or RequestOutcome.Canceled, $"Unexpected outcome {outcome} in iteration {iteration}.");

			Assert.AreEqual(
				outcome == RequestOutcome.Completed ? 1 : 0,
				run.AppliedCount,
				$"Applied count disagrees with {outcome} in iteration {iteration}."
			);
		}
	}

	[TestMethod]
	public void RunAsync_DisposeRacingTheAdmission_NeverSupersedesAndNeverAppliesACanceledRun()
	{
		for (int iteration = 0; iteration < 200; iteration++)
		{
			var lifetime = new RequestLifetime();
			using var start = new ManualResetEventSlim();

			int applied = 0;
			Task<RequestOutcome>? run = null;

			var requester = Task.Run(() =>
			{
				start.Wait();

				run = lifetime.RunAsync(
					state: 0,
					computeAsync: static (state, _) => Task.FromResult(state),
					apply: _ => Interlocked.Increment(ref applied)
				);
			});

			var disposer = Task.Run(() =>
			{
				start.Wait();
				lifetime.Dispose();
			});

			start.Set();
			Task.WaitAll(requester, disposer);

			Assert.IsNotNull(run);

			RequestOutcome outcome = Complete(run);

			Assert.IsTrue(outcome is RequestOutcome.Completed or RequestOutcome.Canceled, $"Unexpected outcome {outcome} in iteration {iteration}.");

			Assert.AreEqual(
				outcome == RequestOutcome.Completed ? 1 : 0,
				applied,
				$"Applied count disagrees with {outcome} in iteration {iteration}."
			);
		}
	}

	private static RequestOutcome Complete(Task<RequestOutcome> run)
	{
		Assert.IsTrue(run.Wait(TimeSpan.FromSeconds(10.0)), "The run did not complete.");
		return run.GetAwaiter().GetResult();
	}

	// A request whose provider waits on a gate, so a test can act on the lifetime while the request is in flight.
	private sealed class GatedRun : IDisposable
	{
		private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public GatedRun(RequestLifetime lifetime)
		{
			Outcome = lifetime.RunAsync(
				state: 0,

				computeAsync: async (state, token) =>
				{
					Token = token;
					await _gate.Task.ConfigureAwait(false);
					return state;
				},

				apply: _ => AppliedCount++
			);
		}

		public Task<RequestOutcome> Outcome { get; }

		public CancellationToken Token { get; private set; }

		public int AppliedCount { get; private set; }

		public void Release() => _gate.TrySetResult();

		public void Dispose() => Release();
	}
}
