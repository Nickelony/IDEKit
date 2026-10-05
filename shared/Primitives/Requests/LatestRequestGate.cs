#if QUICKPICKFAMILY
namespace Nickelony.QuickPick;

#else
namespace Nickelony.IDEKit.Core.Requests;

#endif

/// <summary>Coordinates asynchronous operations so that a result is published only while its request is still the newest.</summary>
/// <remarks>
/// A superseded result is discarded even when its compute delegate ignores cancellation. A run (<see cref="RunAsync"/>) and a host-driven
/// request (<see cref="BeginRequest()"/>) share one request lifetime, so only the newest request stays current. The continuation runs on the caller's
/// captured synchronization context by default, and blocking on the returned task can deadlock.
/// </remarks>
#if QUICKPICKFAMILY
internal
#else
public
#endif
sealed class LatestRequestGate
{
	private readonly object _sync = new();

	private long _latestRequestId;
	private CancellationTokenSource? _currentRequestCancellation;

	/// <summary>Begins a host-driven request and returns its handle.</summary>
	/// <returns>A handle whose nonzero identifier identifies the new request and whose token is that request's cancellation token.</returns>
	/// <remarks>
	/// The caller runs the work, decides whether to publish with <see cref="IsCurrent(RequestHandle)"/>, and treats a canceled
	/// <see cref="RequestHandle.CancellationToken"/> as a rejection; <see cref="CanPublish"/> makes both checks as one atomic test. The new request
	/// supersedes any outstanding request, whether that request is host-driven or a run: the superseded request's token is canceled first,
	/// and its result is discarded.
	/// </remarks>
	public RequestHandle BeginRequest()
	{
		AdmitResult admission;

		lock (_sync)
			admission = CreateAdmission();

		// Cancel outside the lock: a cancellation callback is caller code and must not run while the
		// gate lock is held.
		CancelSafely(admission.SupersededCancellation);

		return admission.Handle;
	}

	/// <summary>
	/// The predicate-gated admission gate for a host-driven request or a run: it observes the caller's state and mints the request identifier
	/// and its cancellation source.
	/// </summary>
	/// <returns>
	/// <see langword="null"/> when the caller's predicate rejected the admission, in which case the superseded source is never touched.
	/// </returns>
	private AdmitResult? Admit(Func<bool>? canAdmit)
	{
		lock (_sync)
		{
			if (canAdmit is not null && !canAdmit())
				return null;

			return CreateAdmission();
		}
	}

	/// <summary>
	/// Mints the request identifier and its cancellation source, installs the source as the current one, and hands the superseded
	/// source to the caller, all inside one critical section, so the identifier rule, the supersede, and the new request's token
	/// are never observed half-applied.
	/// </summary>
	private AdmitResult CreateAdmission()
	{
		var requestCancellation = new CancellationTokenSource();

		var admission = new AdmitResult(
			new RequestHandle(NextRequestId(), requestCancellation.Token),
			requestCancellation,
			_currentRequestCancellation
		);

		_currentRequestCancellation = requestCancellation;

		return admission;
	}

	/// <summary>Advances the request identifier and skips zero, which a counter wrap produces and no live request may carry.</summary>
	private long NextRequestId()
	{
		long requestId = Interlocked.Increment(ref _latestRequestId);
		return requestId != 0 ? requestId : Interlocked.Increment(ref _latestRequestId);
	}

	/// <summary>
	/// Determines whether the supplied request handle identifies the most recent request, whether that request is host-driven or a run.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> when <paramref name="request"/> is the latest request; otherwise, <see langword="false"/>.
	/// The identifier zero - <see cref="RequestHandle.None"/> - is never current, even before any request was admitted.
	/// </returns>
	public bool IsCurrent(RequestHandle request)
	{
		if (request.RequestId == 0)
			return false;

		lock (_sync)
			return request.RequestId == _latestRequestId;
	}

	/// <summary>
	/// Determines whether a result produced for the supplied request may still be published: the request is still the latest
	/// one and its own cancellation token is not canceled.
	/// </summary>
	/// <returns>
	/// <see langword="true"/> when <paramref name="request"/> is the latest request and its token
	/// is not canceled; otherwise, <see langword="false"/>. The identifier zero - <see cref="RequestHandle.None"/> - is never publishable.
	/// </returns>
	/// <remarks>
	/// The check is atomic: the identifier and the token are evaluated together under the gate lock, so a concurrent
	/// admission cannot make a superseded request report publishable. The handle's own token is evaluated, so a canceled request is never publishable
	/// even while it is still the latest request.
	/// </remarks>
	public bool CanPublish(RequestHandle request)
	{
		if (request.RequestId == 0)
			return false;

		lock (_sync)
			return request.RequestId == _latestRequestId && !request.CancellationToken.IsCancellationRequested;
	}

	/// <summary>Invalidates all outstanding requests so their results are discarded when they complete.</summary>
	/// <remarks>
	/// This does not cancel the outstanding work; use <see cref="CancelPendingRequest()"/> to ask it to stop early,
	/// or <see cref="InvalidateIf(Func{bool})"/> to condition the invalidation on caller state.
	/// </remarks>
	public void Invalidate() => InvalidateCore(canInvalidate: null);

	/// <summary>Invalidates all outstanding requests after the supplied predicate accepted the invalidation.</summary>
	/// <param name="canInvalidate">
	/// Runs synchronously under the gate lock, so the decision is atomic with the invalidation; it must be side-effect-free and must not call
	/// back into the gate. When it returns <see langword="false"/>, nothing is invalidated and any outstanding request keeps its state;
	/// a request admitted after an accepted invalidation is not affected by it.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="canInvalidate"/> is <see langword="null"/>.</exception>
	public void InvalidateIf(Func<bool> canInvalidate)
	{
		ArgumentNullException.ThrowIfNull(canInvalidate);
		InvalidateCore(canInvalidate);
	}

	private void InvalidateCore(Func<bool>? canInvalidate)
	{
		lock (_sync)
		{
			if (canInvalidate is not null && !canInvalidate())
				return;

			_ = NextRequestId();
		}
	}

	/// <summary>Cancels the outstanding request's token, if any, so a cooperative compute delegate can stop early.</summary>
	/// <remarks>
	/// This does not invalidate the request - it is still the latest and is classified when it completes - so use <see cref="Invalidate()"/>
	/// to discard outstanding results instead. The request's <see cref="RequestHandle.CancellationToken"/> stays canceled, and a newer request begins
	/// with a fresh token.
	/// </remarks>
	public void CancelPendingRequest()
	{
		CancellationTokenSource? cancellation;

		lock (_sync)
		{
			cancellation = _currentRequestCancellation;
			_currentRequestCancellation = null;
		}

		CancelSafely(cancellation);
	}

	/// <summary>
	/// Requests cancellation and absorbs the benign races: the source was already disposed by the run that owned it, or a cancellation callback
	/// faulted so <see cref="CancellationTokenSource.Cancel()"/> threw an <see cref="AggregateException"/>.
	/// </summary>
	/// <param name="cancellation">The source to cancel, or <see langword="null"/> when no request is outstanding.</param>
	private static void CancelSafely(CancellationTokenSource? cancellation)
	{
		if (cancellation is null)
			return;

		try
		{
			cancellation.Cancel();
		}
		catch (ObjectDisposedException)
		{
			// A run disposes its own source when it completes, and a superseding or canceling call can
			// capture that source immediately before it is disposed. The run it belonged to is finished either
			// way, so the cancel has nothing left to stop and must not escape this unrelated call.
		}
		catch (AggregateException)
		{
			// Cancel runs the callbacks registered on the run's token. A faulting callback belongs
			// to that run, not to this cancel, so the aggregate is swallowed instead of escaping an
			// unrelated call.
		}
	}

	/// <summary>
	/// Runs the supplied compute delegate as the latest request, applying its result only when the request is still current and the host's
	/// <c>canApply</c> predicate accepts it.
	/// </summary>
	/// <typeparam name="TState">The type of the host-supplied request state.</typeparam>
	/// <typeparam name="TResult">The type of the computed result.</typeparam>
	/// <param name="state">The state captured for this request.</param>
	/// <param name="computeAsync">
	/// Computes the result and may observe the supplied <see cref="CancellationToken"/> to stop early.
	/// An <see cref="OperationCanceledException"/> from the delegate is treated as cancellation, so no result is applied.
	/// </param>
	/// <param name="canApply">
	/// Runs after the latest-request and cancellation checks and receives the request state and the computed result; returns whether the result
	/// may be applied.
	/// </param>
	/// <param name="apply">Applies the accepted result.</param>
	/// <param name="continueOnCapturedContext">
	/// <see langword="true"/> (the default) to resume the continuation - including <paramref name="canApply"/> and <paramref name="apply"/> -
	/// on the caller's captured synchronization context, so a UI host publishes on its own thread; <see langword="false"/> to resume
	/// on the thread pool.
	/// </param>
	/// <param name="onFailure">
	/// Reports a run failure that still belongs to the run. It runs on the continuation when the compute delegate, <paramref name="canApply"/>,
	/// or <paramref name="apply"/> throws an exception other than <see cref="OperationCanceledException"/>, and only when no newer request
	/// or invalidation replaced the run and its token is not canceled. The original exception still propagates after the callback runs.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>
	/// The outcome of the run:
	/// <list type="bullet">
	/// <item><see cref="RequestOutcome.Completed"/> when the result was applied;</item>
	/// <item><see cref="RequestOutcome.RejectedByCurrentState"/> when <paramref name="canApply"/> rejected the result;</item>
	/// <item><see cref="RequestOutcome.Canceled"/> when the caller token or <see cref="CancelPendingRequest()"/> canceled the work;</item>
	/// <item><see cref="RequestOutcome.Superseded"/> when a newer request or <see cref="Invalidate()"/> replaced it.</item>
	/// </list>
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="computeAsync"/>, <paramref name="canApply"/>, or <paramref name="apply"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// The publication window is atomic only when admission and publication run on one thread. The default captured
	/// context keeps a UI caller on one thread. A request admitted on another thread between the completion checks and <c>apply</c>
	/// does not retract the result being published, and with <paramref name="continueOnCapturedContext"/> set to <see langword="false"/>,
	/// publication leaves the captured thread, so the same applies. Exceptions from <c>canApply</c> or <c>apply</c> propagate unclassified;
	/// only the compute delegate's cancellation is intercepted. Cancellation is observed at two checkpoints: before the compute delegate runs,
	/// and after it returns or throws, which is also before publication.
	/// </remarks>
	public Task<RequestOutcome> RunAsync<TState, TResult>(
		TState state,
		Func<TState, CancellationToken, Task<TResult>> computeAsync,
		Func<TState, TResult, bool> canApply,
		Action<TResult> apply,
		bool continueOnCapturedContext = true,
		Action<Exception>? onFailure = null,
		CancellationToken cancellationToken = default
	) => RunCoreAsync(canAdmit: null, state, computeAsync, canApply, apply, continueOnCapturedContext, onFailure, cancellationToken);

	/// <summary>
	/// Runs the supplied compute delegate as <see cref="RunAsync{TState, TResult}"/> does, but only when the admission predicate accepts the run.
	/// </summary>
	/// <typeparam name="TState">The type of the host-supplied request state.</typeparam>
	/// <typeparam name="TResult">The type of the computed result.</typeparam>
	/// <param name="canAdmit">
	/// Runs synchronously under the gate lock, so the admission decision is atomic with the supersede; it must be side-effect-free and must not call
	/// back into the gate. When it returns <see langword="false"/>, no delegate runs, nothing is superseded or canceled, and any outstanding request
	/// keeps its state.
	/// </param>
	/// <param name="state">The state captured for this request.</param>
	/// <param name="computeAsync">Computes the result and may observe the supplied <see cref="CancellationToken"/> to stop early.</param>
	/// <param name="canApply">Receives the request state and the computed result; returns whether the result may be applied.</param>
	/// <param name="apply">Applies the accepted result.</param>
	/// <param name="continueOnCapturedContext">
	/// <see langword="true"/> (the default) to resume the continuation on the caller's captured synchronization context;
	/// <see langword="false"/> to resume on the thread pool.
	/// </param>
	/// <param name="onFailure">Reports a run failure that still belongs to the run, as <see cref="RunAsync{TState, TResult}"/> describes.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>
	/// <see cref="RequestOutcome.Canceled"/> when the predicate
	/// rejected the admission; otherwise, the outcome that <see cref="RunAsync{TState, TResult}"/> reports.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="canAdmit"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// A rejected admission is a cancellation because the caller's state, such as a disposed owner, ended the request before it began.
	/// A <see langword="null"/> <paramref name="computeAsync"/>, <paramref name="canApply"/>, or <paramref name="apply"/> faults the returned task,
	/// as it does for <see cref="RunAsync{TState, TResult}"/>.
	/// </remarks>
	public Task<RequestOutcome> RunIfAsync<TState, TResult>(
		Func<bool> canAdmit,
		TState state,
		Func<TState, CancellationToken, Task<TResult>> computeAsync,
		Func<TState, TResult, bool> canApply,
		Action<TResult> apply,
		bool continueOnCapturedContext = true,
		Action<Exception>? onFailure = null,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(canAdmit);
		return RunCoreAsync(canAdmit, state, computeAsync, canApply, apply, continueOnCapturedContext, onFailure, cancellationToken);
	}

	private async Task<RequestOutcome> RunCoreAsync<TState, TResult>(
		Func<bool>? canAdmit,
		TState state,
		Func<TState, CancellationToken, Task<TResult>> computeAsync,
		Func<TState, TResult, bool> canApply,
		Action<TResult> apply,
		bool continueOnCapturedContext,
		Action<Exception>? onFailure,
		CancellationToken cancellationToken
	)
	{
		ArgumentNullException.ThrowIfNull(computeAsync);
		ArgumentNullException.ThrowIfNull(canApply);
		ArgumentNullException.ThrowIfNull(apply);

		// A rejected admission runs no delegate and leaves the outstanding request alone. An accepted run keeps
		// the admitted source instead of handing it to a caller.
		if (Admit(canAdmit) is not AdmitResult admission)
			return RequestOutcome.Canceled;

		RequestHandle request = admission.Handle;
		CancellationTokenSource runCancellation = admission.Source;

		// Cancel outside the lock: a cancellation callback is caller code and must not run while the
		// gate lock is held.
		CancelSafely(admission.SupersededCancellation);

		CancellationTokenSource? linkedCancellation = cancellationToken.CanBeCanceled
			? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runCancellation.Token)
			: null;

		CancellationToken effectiveToken = linkedCancellation?.Token ?? runCancellation.Token;

		try
		{
			// A caller token that is already canceled cancels the run before the compute delegate
			// is invoked, and a run that a newer request replaced is superseded even though its
			// compute delegate never started.
			if (effectiveToken.IsCancellationRequested)
				return ClassifyCancellation(request);

			TResult result;

			// Only the compute delegate's cancellation is expected here. A cancellation thrown by
			// the host's canApply or apply delegates is a real failure and must propagate.
			try
			{
				result = await computeAsync(state, effectiveToken).ConfigureAwait(continueOnCapturedContext);
			}
			catch (OperationCanceledException)
			{
				return ClassifyCancellation(request);
			}

			if (effectiveToken.IsCancellationRequested)
				return ClassifyCancellation(request);

			if (!IsLatestRequest(request.RequestId))
				return RequestOutcome.Superseded;

			if (!canApply(state, result))
				return RequestOutcome.RejectedByCurrentState;

			apply(result);
			return RequestOutcome.Completed;
		}
		catch (Exception exception) when (ShouldReportFailure(onFailure, exception, requestId: request.RequestId, cancellationToken: effectiveToken))
		{
			// The failure belongs to this run while no newer request or invalidation replaced it and its
			// token was not canceled; a superseded failure must not overwrite the state a newer request
			// already published, so it is passed through without a report. The exception itself always
			// propagates.
			onFailure!(exception);
			throw;
		}
		finally
		{
			// The run's sources belong to the run, so both are released when the run completes: the
			// linked source registers on the caller token, and the run's own source backs the token the
			// compute delegate received. A host-driven request's source belongs to the gate and is never disposed,
			// because its handle exposes only the token. The field is cleared before the source is disposed, so a cancel
			// that arrives afterwards finds nothing to cancel; one that captured the source immediately before this cleanup
			// absorbs the disposal race.
			linkedCancellation?.Dispose();

			lock (_sync)
			{
				if (ReferenceEquals(_currentRequestCancellation, runCancellation))
					_currentRequestCancellation = null;
			}

			runCancellation.Dispose();
		}
	}

	/// <summary>
	/// The admission gate's result: the new request's handle and source, plus the source the new request superseded
	/// (or <see langword="null"/> when no request was outstanding).
	/// </summary>
	private readonly record struct AdmitResult(RequestHandle Handle, CancellationTokenSource Source, CancellationTokenSource? SupersededCancellation);

	/// <summary>
	/// Classifies a canceled run: superseded only when a newer request or <see cref="Invalidate()"/> replaced it, otherwise a plain cancellation.
	/// </summary>
	private RequestOutcome ClassifyCancellation(RequestHandle request) =>
		IsLatestRequest(request.RequestId) ? RequestOutcome.Canceled : RequestOutcome.Superseded;

	private bool IsLatestRequest(long requestId)
	{
		lock (_sync)
			return requestId == _latestRequestId;
	}

	/// <summary>
	/// Decides whether a run failure still belongs to its run: a failure callback was supplied, the failure is not a cancellation, no newer request
	/// or invalidation replaced the run, and the run's token was not canceled.
	/// </summary>
	/// <remarks>
	/// It runs as an exception filter, so a failure that is not reportable unwinds without entering a catch block.
	/// </remarks>
	private bool ShouldReportFailure(Action<Exception>? onFailure, Exception exception, long requestId, CancellationToken cancellationToken)
	{
		if (onFailure is null || exception is OperationCanceledException || cancellationToken.IsCancellationRequested)
			return false;

		return IsLatestRequest(requestId);
	}
}
