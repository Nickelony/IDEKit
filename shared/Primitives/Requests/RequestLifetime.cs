#if QUICKPICKFAMILY
namespace Nickelony.QuickPick;

#else
namespace Nickelony.IDEKit.Core.Requests;

#endif

/// <summary>
/// Owns the request lifetime of one controller: the latest-request gate, the generation counter, the disposal flag, and the token
/// that disposal cancels.
/// </summary>
/// <remarks>
/// The gate is private, so every request passes the disposal check. A request that starts after disposal reports
/// <see cref="RequestOutcome.Canceled"/> and runs no delegate. Disposal cancels the request in flight without invalidating it, so that request
/// reports <see cref="RequestOutcome.Canceled"/> instead of <see cref="RequestOutcome.Superseded"/>. Disposal sets the flag
/// before it cancels the request, so a continuation that observes the flag stops publishing.
/// </remarks>
#if QUICKPICKFAMILY
internal
#else
public
#endif
sealed class RequestLifetime : IDisposable
{
	private readonly LatestRequestGate _gate = new();
	private readonly CancellationTokenSource _lifetimeCancellation = new();

	private int _isDisposed;
	private int _generation;

	/// <summary>Initializes a lifetime with no outstanding request.</summary>
	public RequestLifetime() => LifetimeToken = _lifetimeCancellation.Token;

	/// <summary>Whether <see cref="Dispose"/> ran, after which the lifetime admits no request.</summary>
	public bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

	/// <summary>Starts a new generation, which makes every earlier one stale.</summary>
	/// <returns>The new generation, which <see cref="IsCurrent"/> accepts until the next call.</returns>
	/// <remarks>
	/// A controller stamps its request with a generation and checks it in the publication step it dispatches, which runs after the gate's
	/// own latest-request check, so a newer request, a dismissal or an invalidation that lands in between still drops the result.
	/// </remarks>
	public int AdvanceGeneration() => Interlocked.Increment(ref _generation);

	/// <summary>Determines whether the generation is the newest and the lifetime is not disposed.</summary>
	/// <param name="generation">A value returned by <see cref="AdvanceGeneration"/>.</param>
	/// <returns>
	/// <see langword="true"/> when no later generation started and the lifetime is not disposed; otherwise, <see langword="false"/>.
	/// </returns>
	public bool IsCurrent(int generation) => !IsDisposed && generation == Volatile.Read(ref _generation);

	/// <summary>A token that is canceled when the lifetime is disposed.</summary>
	/// <remarks>
	/// A callback that runs outside the supersedable request pipeline - for example an invoked code action - has no per-request token to observe,
	/// so it receives this token instead and can stop when the controller shuts down. The token is captured once, so it stays readable
	/// after disposal.
	/// </remarks>
	public CancellationToken LifetimeToken { get; }

	/// <summary>Cancels the outstanding request's token, if any.</summary>
	/// <remarks>
	/// Disposal cancels the request in flight and admits no later one, so the call has nothing to cancel afterwards.
	/// </remarks>
	public void CancelInFlightRequest() => _gate.CancelPendingRequest();

	/// <summary>Marks outstanding requests as stale unless the lifetime is disposed.</summary>
	/// <remarks>
	/// The disposal check runs inside the gate's critical section, so an invalidation never reaches a request that disposal canceled;
	/// that request reports <see cref="RequestOutcome.Canceled"/> and not <see cref="RequestOutcome.Superseded"/>.
	/// </remarks>
	public void InvalidateRequests() => _gate.InvalidateIf(IsActive);

	/// <summary>
	/// Cancels the outstanding request and marks outstanding requests as stale, so a context change leaves no request that could still publish.
	/// The invalidation is a no-op once the lifetime is disposed.
	/// </summary>
	public void CancelAndInvalidate()
	{
		CancelInFlightRequest();
		InvalidateRequests();
	}

	/// <summary>
	/// Runs a request as the latest one: it is admitted only while the lifetime is not disposed, and its result is applied only while the lifetime
	/// is not disposed.
	/// </summary>
	/// <typeparam name="TState">The type of the state captured for the request.</typeparam>
	/// <typeparam name="TResult">The type of the computed result.</typeparam>
	/// <param name="state">The state captured for the request.</param>
	/// <param name="computeAsync">Computes the result and observes the request's token to stop early.</param>
	/// <param name="apply">Applies the accepted result.</param>
	/// <param name="onFailure">Reports a failure that still belongs to the request.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>
	/// The outcome of the run, which is <see cref="RequestOutcome.Canceled"/> when the lifetime was disposed before the request was admitted
	/// or before its result could be applied.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="computeAsync"/> or <paramref name="apply"/> is <see langword="null"/>; the returned task faults.
	/// </exception>
	public async Task<RequestOutcome> RunAsync<TState, TResult>(
		TState state,
		Func<TState, CancellationToken, Task<TResult>> computeAsync,
		Action<TResult> apply,
		Action<Exception>? onFailure = null,
		CancellationToken cancellationToken = default
	)
	{
		RequestOutcome outcome = await _gate
			.RunIfAsync(
				canAdmit: IsActive,
				state,
				computeAsync,
				canApply: (_, _) => IsActive(),
				apply,
				onFailure: onFailure,
				cancellationToken: cancellationToken
			)
			.ConfigureAwait(false);

		// The disposal check is the only check that rejects a result by current state, and a dispose between the gate's latest-request
		// check and that gate is a cancellation of the owner, so the outcome says so.
		return outcome == RequestOutcome.RejectedByCurrentState ? RequestOutcome.Canceled : outcome;
	}

	/// <summary>
	/// Ends the lifetime: marks it disposed, then cancels the outstanding request and the lifetime token. The method is idempotent.
	/// </summary>
	public void Dispose()
	{
		// The flag is claimed atomically so a concurrent second Dispose cannot double-dispose the cancellation
		// source, and it is published before the teardown runs: an admission that follows reads it under the
		// gate lock and is rejected, and one that came earlier is canceled below.
		if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
			return;

		_gate.CancelPendingRequest();
		_lifetimeCancellation.Cancel();
		_lifetimeCancellation.Dispose();
	}

	// The predicate that admission and invalidation evaluate inside the gate's critical section; it reads the
	// flag only, so it cannot deadlock against the gate.
	private bool IsActive() => !IsDisposed;
}
