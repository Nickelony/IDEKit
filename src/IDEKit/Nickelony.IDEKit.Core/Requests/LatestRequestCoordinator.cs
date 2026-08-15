namespace Nickelony.IDEKit.Core.Requests;

/// <summary>
/// Coordinates asynchronous operations so that a result is only published while no newer request
/// has been admitted. Starting a newer operation cancels any previously outstanding operation,
/// and each run reports a <see cref="RequestOutcome"/> that classifies how it completed.
/// </summary>
/// <remarks>
/// <para>
/// A superseded operation's result is discarded even when its compute delegate ignored the cancellation
/// token, and the guarantee covers same-thread publication only: a request admitted between this run's
/// completion checks and its callbacks does not retract a result that is already being published. Two
/// request levels share one request lifetime - a run (<see cref="RunAsync"/> awaits the work and publishes
/// through a current-state check) and a host-driven request (<see cref="BeginRequest()"/> admits a request
/// the caller completes itself, deciding admission with <see cref="IsCurrent(RequestHandle)"/> and treating
/// a canceled <see cref="RequestHandle.CancellationToken"/> as a rejection). Both levels supersede each
/// other, so only the newest request stays current.
/// </para>
/// <list type="bullet">
/// <item>Admission, cancellation, and invalidation can each be conditioned on a caller-supplied predicate
/// (<see cref="BeginRequestIf(Func{bool})"/>, <see cref="CancelPendingRequestIf(Func{bool})"/>,
/// <see cref="InvalidateIf(Func{bool})"/>) that runs inside the critical section, so a caller's own state
/// check is atomic with the operation; <see cref="CanPublish(RequestHandle)"/> reports atomically whether a
/// result may still be published.</item>
/// <item>A caller token that is already canceled when a run is admitted cancels the run before its compute
/// delegate is invoked.</item>
/// <item>Hosts supply the operation's state and a <c>canApply</c> predicate for current-state checks; the
/// predicate runs separately from <c>apply</c> so it can reject a result without side effects. Core has no
/// knowledge of editor or document state.</item>
/// <item>A run failure has no result to check, so <see cref="RunAsync{TState, TResult}"/> can report it
/// through an optional failure callback that runs only while the run still owns the request slot.</item>
/// <item>The continuation, including <c>canApply</c> and <c>apply</c>, runs on the caller's captured
/// synchronization context by default; pass <c>continueOnCapturedContext: false</c> to resume on the thread
/// pool. Await the returned task instead of blocking on it: a blocking wait on a single-threaded context can
/// deadlock.</item>
/// <item>A run's cancellation source belongs to the coordinator, which cancels it on supersede and releases
/// it (with the linked source) when the run completes; a host-driven request's source belongs to the host
/// that holds its handle, and the coordinator cancels it on supersede and on
/// <see cref="CancelPendingRequest()"/> but never releases it. Neither release is observable through a
/// token.</item>
/// </list>
/// </remarks>
public sealed class LatestRequestCoordinator
{
	private readonly object _sync = new();
	private long _latestRequestId;
	private CancellationTokenSource? _currentRequestCancellation;

	/// <summary>
	/// Begins a host-driven request and returns its handle. The caller runs the work itself, decides
	/// admission with <see cref="IsCurrent(RequestHandle)"/> and treats a canceled
	/// <see cref="RequestHandle.CancellationToken"/> as a rejection - the same rules the run path
	/// applies.
	/// </summary>
	/// <remarks>
	/// Starting the request supersedes the outstanding request of either level: its token is canceled
	/// first so a cooperative provider can stop early, and its result is discarded.
	/// </remarks>
	/// <returns>
	/// A handle whose nonzero identifier identifies the new request and whose token is that request's
	/// cancellation token.
	/// </returns>
	public RequestHandle BeginRequest()
		=> AdmitOrNone(canAdmit: null);

	/// <summary>
	/// Begins a host-driven request after the supplied admission predicate accepted it. The predicate
	/// decides admission atomically with the supersede, so a rejected call leaves every request
	/// untouched.
	/// </summary>
	/// <param name="canAdmit">
	/// Determines whether the request may be admitted. The predicate runs synchronously under the
	/// coordinator lock - that is what makes the admission decision atomic with the supersede - so it
	/// must be side-effect-free and must not call back into the coordinator. When it returns
	/// <see langword="false"/>, no request is admitted, nothing is superseded or canceled, and any
	/// outstanding request keeps its state.
	/// </param>
	/// <returns>
	/// The new request's handle, or <see cref="RequestHandle.None"/> when the predicate rejected the
	/// admission. A rejected admission has identifier zero, which is never a valid request identifier,
	/// so it cannot be mistaken for an admitted request.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="canAdmit"/> is <see langword="null"/>.</exception>
	public RequestHandle BeginRequestIf(Func<bool> canAdmit)
	{
		ArgumentNullException.ThrowIfNull(canAdmit);

		return AdmitOrNone(canAdmit);
	}

	/// <summary>
	/// Admits a host-driven request and cancels the superseded one, or reports the rejection as
	/// <see cref="RequestHandle.None"/>.
	/// </summary>
	private RequestHandle AdmitOrNone(Func<bool>? canAdmit)
	{
		if (TryAdmit(canAdmit) is not RequestAdmission admission)
			return RequestHandle.None;

		// Cancel outside the lock: a cancellation callback is caller code and must not run while the
		// coordinator lock is held.
		TryCancelSafely(admission.SupersededCancellation);

		return admission.Handle;
	}

	/// <summary>
	/// Admits a request that has no caller predicate, so the admission cannot be rejected. The run path
	/// uses this overload and therefore needs no nullable admission or null-forgiving suppression.
	/// </summary>
	private RequestAdmission Admit()
	{
		lock (_sync)
			return CreateAdmission();
	}

	/// <summary>
	/// The predicate-gated admission gate for a host-driven request: it observes the caller's state and
	/// mints the request identifier and its cancellation source. Returns <see langword="null"/> when the
	/// caller's predicate rejected the admission; every request then keeps its state and the superseded
	/// source is never touched.
	/// </summary>
	private RequestAdmission? TryAdmit(Func<bool>? canAdmit)
	{
		lock (_sync)
		{
			if (canAdmit is not null && !canAdmit())
				return null;

			return CreateAdmission();
		}
	}

	/// <summary>
	/// Mints the request identifier and its cancellation source, installs the source as the current one,
	/// and hands the superseded source to the caller, all inside one critical section, so the identifier
	/// rule, the supersede, and the new request's token cannot be observed half-applied. The caller holds
	/// or acquires the coordinator lock.
	/// </summary>
	private RequestAdmission CreateAdmission()
	{
		var requestCancellation = new CancellationTokenSource();
		var admission = new RequestAdmission(
			new RequestHandle(NextRequestId(), requestCancellation.Token),
			requestCancellation,
			_currentRequestCancellation);

		_currentRequestCancellation = requestCancellation;

		return admission;
	}

	/// <summary>
	/// Advances the request identifier through the shared nonzero-counter rule. Callers hold the
	/// coordinator lock; the helper increments atomically regardless.
	/// </summary>
	private long NextRequestId() => RequestIdSource.NextNonZero(ref _latestRequestId);

	/// <summary>
	/// Determines whether the supplied request handle identifies the most recent request begun
	/// through <see cref="BeginRequest()"/> or <see cref="RunAsync"/>.
	/// </summary>
	/// <param name="request">The request handle to inspect.</param>
	/// <returns>
	/// <see langword="true"/> when <paramref name="request"/> is the latest request; otherwise,
	/// <see langword="false"/>. The identifier zero - <see cref="RequestHandle.None"/> - is never
	/// current, even before any request was admitted.
	/// </returns>
	public bool IsCurrent(RequestHandle request)
	{
		if (request.RequestId == 0)
			return false;

		lock (_sync)
		{
			return request.RequestId == _latestRequestId;
		}
	}

	/// <summary>
	/// Determines whether a result produced for the supplied request may still be published: the
	/// request is still the latest one and its own cancellation token is not canceled.
	/// </summary>
	/// <param name="request">The request handle to inspect.</param>
	/// <returns>
	/// <see langword="true"/> when <paramref name="request"/> is the latest request and its token is
	/// not canceled; otherwise, <see langword="false"/>. The identifier zero -
	/// <see cref="RequestHandle.None"/> - is never publishable. The check is atomic: the identifier
	/// and the token are evaluated together under the coordinator lock, so a concurrent admission
	/// cannot make a superseded request report publishable. The handle's own token is evaluated, so a
	/// canceled request is never publishable even while a newer request is live.
	/// </returns>
	public bool CanPublish(RequestHandle request)
	{
		if (request.RequestId == 0)
			return false;

		lock (_sync)
		{
			return request.RequestId == _latestRequestId && !request.CancellationToken.IsCancellationRequested;
		}
	}

	/// <summary>
	/// Invalidates all outstanding requests so their results are discarded when they complete. This
	/// does not cancel the outstanding work; use <see cref="CancelPendingRequest()"/> to ask it to stop
	/// early, and <see cref="InvalidateIf(Func{bool})"/> to condition the invalidation on caller state.
	/// </summary>
	public void Invalidate()
		=> InvalidateCore(canInvalidate: null);

	/// <summary>
	/// Invalidates all outstanding requests after the supplied predicate accepted the invalidation.
	/// The predicate is evaluated inside the coordinator's critical section, so the decision is
	/// atomic with the invalidation: a rejection leaves every request untouched, and a request
	/// admitted after the accepted invalidation is not affected by it.
	/// </summary>
	/// <param name="canInvalidate">
	/// Determines whether the invalidation may proceed. The predicate runs synchronously under the
	/// coordinator lock - that is what makes the decision atomic with the invalidation - so it must
	/// be side-effect-free and must not call back into the coordinator. When it returns
	/// <see langword="false"/>, nothing is invalidated and any outstanding request keeps its state.
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

	/// <summary>
	/// Cancels the outstanding request's token, if any, so a cooperative compute delegate or provider
	/// can stop early. This does not invalidate the request: it is still the latest, and its outcome is
	/// classified when it completes. Use <see cref="Invalidate()"/> to discard outstanding results instead.
	/// </summary>
	/// <remarks>
	/// The canceled token stays observable through the request's
	/// <see cref="RequestHandle.CancellationToken"/> until a newer request begins. Canceling only stops
	/// the work, so a host-driven request keeps its current state and a host that drives the pipeline
	/// itself treats a canceled <see cref="RequestHandle.CancellationToken"/> as a rejection the same
	/// way a run is classified as <see cref="RequestOutcome.Canceled"/>. To cancel only a specific
	/// admission - so a caller can never cancel a request it did not admit - use the handle overload;
	/// to decide the cancellation against caller state atomically, use the predicate overload.
	/// </remarks>
	public void CancelPendingRequest()
		=> CancelPendingRequestCore(request: null);

	/// <summary>
	/// Cancels the outstanding request's token only when the supplied handle is still the latest
	/// request. A handle that a newer request superseded leaves the newer request's token untouched.
	/// </summary>
	/// <param name="request">
	/// The handle of the request to cancel; a handle that is not the latest request is ignored, and
	/// <see cref="RequestHandle.None"/> (never a valid identifier) does nothing.
	/// </param>
	public void CancelPendingRequest(RequestHandle request)
		=> CancelPendingRequestCore(request);

	/// <summary>
	/// Cancels the outstanding request's token after the supplied predicate accepted the
	/// cancellation. The predicate is evaluated inside the coordinator's critical section, so the
	/// decision is atomic with the cancellation: a rejection leaves every request untouched, and a
	/// request admitted after an accepted cancellation is not affected by it.
	/// </summary>
	/// <param name="canCancel">
	/// Determines whether the cancellation may proceed. The predicate runs synchronously under the
	/// coordinator lock - that is what makes the decision atomic with the cancellation - so it must
	/// be side-effect-free and must not call back into the coordinator. When it returns
	/// <see langword="false"/>, nothing is canceled and the outstanding request keeps its token.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="canCancel"/> is <see langword="null"/>.</exception>
	public void CancelPendingRequestIf(Func<bool> canCancel)
	{
		ArgumentNullException.ThrowIfNull(canCancel);

		CancelPendingRequestCore(request: null, canCancel);
	}

	private void CancelPendingRequestCore(RequestHandle? request, Func<bool>? canCancel = null)
	{
		CancellationTokenSource? cancellation;

		lock (_sync)
		{
			if (canCancel is not null && !canCancel())
				return;

			if (request is RequestHandle handle && handle.RequestId != _latestRequestId)
				return;

			cancellation = _currentRequestCancellation;
			_currentRequestCancellation = null;
		}

		TryCancelSafely(cancellation);
	}

	/// <summary>
	/// Requests cancellation and absorbs the two benign races: the source was already disposed by the
	/// run that owned it, or a cancellation callback faulted so
	/// <see cref="CancellationTokenSource.Cancel()"/> threw an <see cref="AggregateException"/>.
	/// </summary>
	/// <param name="cancellation">The source to cancel, or <see langword="null"/> when no request is outstanding.</param>
	private static void TryCancelSafely(CancellationTokenSource? cancellation)
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
			// capture that source just before it is disposed. The run it belonged to is finished either
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
	/// Runs the supplied compute delegate as the latest request, applying its result only when the
	/// request is still current and the host's <c>canApply</c> predicate accepts it.
	/// </summary>
	/// <remarks>
	/// The latest-request check and the <c>canApply</c> and <c>apply</c> callbacks run after the
	/// compute delegate completes; see the class remarks for the publication window guarantee. A
	/// host that can start requests concurrently with publication should keep its own state check in
	/// <c>canApply</c>. Exceptions thrown by <c>canApply</c> or <c>apply</c> propagate to the caller
	/// unclassified; only the compute delegate's <see cref="OperationCanceledException"/> is
	/// intercepted. A non-cancellation run failure additionally runs <c>onFailure</c> first, but
	/// only while the run still owns the request slot; see that parameter for the exact guarantee.
	/// </remarks>
	/// <typeparam name="TState">The type of the host-supplied request state.</typeparam>
	/// <typeparam name="TResult">The type of the computed result.</typeparam>
	/// <param name="state">The state captured for this request.</param>
	/// <param name="computeAsync">
	/// Computes the result. The returned task may observe the supplied
	/// <see cref="CancellationToken"/> to stop early. An <see cref="OperationCanceledException"/>
	/// thrown by the delegate is treated as cancellation, so no result is applied.
	/// </param>
	/// <param name="canApply">
	/// Determines whether the completed result may be applied. It runs after the latest-request and
	/// cancellation checks and receives both the request state and the computed result.
	/// </param>
	/// <param name="apply">Applies the accepted result.</param>
	/// <param name="continueOnCapturedContext">
	/// <see langword="true"/> (the default) to resume the continuation - including
	/// <paramref name="canApply"/> and <paramref name="apply"/> - on the caller's captured
	/// synchronization context, so a UI host publishes on its own thread; <see langword="false"/> to
	/// resume on the thread pool and marshal thread-affine work in the host.
	/// </param>
	/// <param name="onFailure">
	/// Reports a run failure while this run still owns the request slot. The callback runs when the
	/// compute delegate throws, or when <paramref name="canApply"/> or <paramref name="apply"/> throws
	/// (a cancellation from those delegates is a real failure, not a reportable run failure). It runs on
	/// the continuation - the same context as <paramref name="canApply"/> and <paramref name="apply"/> -
	/// and only when no newer request or invalidation replaced this run and its token is not canceled, so
	/// a host can recover from a failure without overwriting the state a superseding request already
	/// published. A failure that lost the slot is not reported. The original exception still propagates
	/// after the callback ran.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>
	/// The outcome of the run:
	/// <list type="bullet">
	/// <item><see cref="RequestOutcome.Completed"/> when the result was applied;</item>
	/// <item><see cref="RequestOutcome.RejectedByCurrentState"/> when <paramref name="canApply"/> rejected the result;</item>
	/// <item><see cref="RequestOutcome.Canceled"/> when the caller token or
	/// <see cref="CancelPendingRequest()"/> canceled the work;</item>
	/// <item><see cref="RequestOutcome.Superseded"/> when a newer request or
	/// <see cref="Invalidate()"/> replaced it.</item>
	/// </list>
	/// A compute failure other than cancellation, or an exception from <paramref name="canApply"/> or
	/// <paramref name="apply"/>, propagates as an exception instead of an outcome.
	/// <para>
	/// Cancellation is observed at checkpoints (before the compute delegate runs, after it returns
	/// or throws, and before publication) rather than during the <paramref name="canApply"/> and
	/// <paramref name="apply"/> callbacks, so a cancellation that arrives while they run does not
	/// preempt publication. The compute delegate receives the run's linked token; the linked source
	/// and the run's own source are released when the run completes, so a token retained by
	/// delegate-started work stops observing later cancellation at that point. The release cancels
	/// nothing and rejects no registration, so a retained token behaves exactly like a token whose
	/// request simply ended.
	/// </para>
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="computeAsync"/>, <paramref name="canApply"/>, or <paramref name="apply"/> is <see langword="null"/>.
	/// </exception>
	public async Task<RequestOutcome> RunAsync<TState, TResult>(
		TState state,
		Func<TState, CancellationToken, Task<TResult>> computeAsync,
		Func<TState, TResult, bool> canApply,
		Action<TResult> apply,
		bool continueOnCapturedContext = true,
		Action<Exception>? onFailure = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(computeAsync);
		ArgumentNullException.ThrowIfNull(canApply);
		ArgumentNullException.ThrowIfNull(apply);

		// A run has no caller-supplied admission predicate, so its admission always succeeds and it is
		// the run that keeps the admitted source instead of handing it to a caller.
		RequestAdmission admission = Admit();
		RequestHandle request = admission.Handle;
		CancellationTokenSource runCancellation = admission.Source;

		// Cancel outside the lock: a cancellation callback is caller code and must not run while the
		// coordinator lock is held.
		TryCancelSafely(admission.SupersededCancellation);

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
		catch (Exception exception) when (ShouldReportFailure(onFailure, exception, request.RequestId, effectiveToken))
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
			// The run's sources belong to the coordinator, so both are released when the run completes:
			// the linked source registers on the caller token, and the run's own source backs the token
			// the compute delegate received. A host-driven request's source is owned by the host that
			// holds its handle and is never released here.
			//
			// The field is cleared before the source is disposed so a cancel that arrives afterwards finds
			// nothing to cancel; a cancel that captured the source just before this cleanup absorbs the
			// disposal race.
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
	/// The admission gate's result: the new request's handle and source, plus the source the new
	/// request superseded (or <see langword="null"/> when no request was outstanding).
	/// </summary>
	private readonly record struct RequestAdmission(
		RequestHandle Handle,
		CancellationTokenSource Source,
		CancellationTokenSource? SupersededCancellation);

	/// <summary>
	/// Classifies a canceled run. A canceled run is only superseded when a newer request or
	/// <see cref="Invalidate()"/> replaced it; an owner-driven cancel of the latest run is a plain
	/// cancellation.
	/// </summary>
	private RequestOutcome ClassifyCancellation(RequestHandle request)
	{
		return IsLatestRequest(request.RequestId)
			? RequestOutcome.Canceled
			: RequestOutcome.Superseded;
	}

	private bool IsLatestRequest(long requestId)
	{
		lock (_sync)
		{
			return requestId == _latestRequestId;
		}
	}

	/// <summary>
	/// Decides whether a run failure still belongs to its run: the host supplied a failure callback, the
	/// failure is not a cancellation (a cancellation from <c>canApply</c> or <c>apply</c> is a real
	/// failure that must propagate unclassified), no newer request or invalidation replaced the run, and
	/// the run's token was not canceled. The predicate runs as an exception filter, so a failure that is
	/// not reportable unwinds without ever entering a catch block.
	/// </summary>
	private bool ShouldReportFailure(Action<Exception>? onFailure, Exception exception, long requestId, CancellationToken effectiveToken)
	{
		if (onFailure is null || exception is OperationCanceledException || effectiveToken.IsCancellationRequested)
			return false;

		return IsLatestRequest(requestId);
	}
}
