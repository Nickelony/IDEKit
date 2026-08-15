using Nickelony.IDEKit.Core.Requests;

namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Tracks the request lifetime a completion pipeline uses, shared by a controller's standard
/// request entry point and by hosts that admit requests themselves.
/// </summary>
/// <remarks>
/// <para>
/// A completion controller creates the session and exposes it to its host; the session ends when its
/// owner disposes it. Its operations keep reporting their documented safe defaults afterwards (for
/// example <see cref="BeginRequest"/> reports <see cref="RequestHandle.None"/>). Admission,
/// cancellation, and invalidation are all decided inside the coordinator's critical section against
/// the session's disposal state, so a request admitted concurrently with disposal is either canceled
/// by it or rejected by it, never left pending, and a late call can never reach a request admitted
/// through the coordinator after disposal; see <see cref="BeginRequest"/> for the admission rules.
/// </para>
/// <para>
/// The session and the controller's standard request pipeline share one request lifetime through the
/// core <see cref="LatestRequestCoordinator"/>: beginning a request here supersedes an in-flight
/// standard request and vice versa, so only the newest request of either kind stays current.
/// </para>
/// <para>
/// This session is the request-lifetime mechanism: it reports whether the request that produced a
/// result is still the newest one. Items that outlive the pipeline - for example an
/// open completion session whose entries are resolved asynchronously - carry their own staleness
/// stamps instead (<see cref="TextCompletionItem.RequestDocumentVersion"/>,
/// <see cref="TextCompletionItem.RequestGeneration"/>,
/// <see cref="TextCompletionItem.WithRequestContext"/>) and are rebased by the host when the
/// document changes under the open window.
/// </para>
/// <para>
/// The session is the package's single host-state type and the deliberate exception to its
/// otherwise declarative payloads: it owns live request coordination and a disposal lifetime, which
/// is state the host must own rather than data the package can model as a value.
/// </para>
/// </remarks>
public sealed class TextCompletionRequestSession : IDisposable
{
	private readonly LatestRequestCoordinator _requests = new();
	private readonly object _sync = new();
	private volatile bool _isDisposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextCompletionRequestSession"/> class.
	/// </summary>
	/// <remarks>
	/// A completion controller normally creates the session and exposes it to its host; a host that
	/// owns the complete request lifecycle may create one directly and dispose it when done.
	/// </remarks>
	public TextCompletionRequestSession()
	{
	}

	/// <summary>
	/// Gets the request coordinator behind the session, so a controller's standard request pipeline can
	/// admit its runs as requests of the same session.
	/// </summary>
	/// <remarks>
	/// The coordinator stays usable after the session is disposed, but requests admitted through it
	/// directly bypass the session's disposal gate (admission is not rejected with
	/// <see cref="RequestHandle.None"/>) and the session reports them as non-current while it is
	/// disposed; the handle they return keeps their token observable. Use <see cref="BeginRequest"/> so
	/// a disposed session rejects admission with <see cref="RequestHandle.None"/>.
	/// </remarks>
	public LatestRequestCoordinator Coordinator => _requests;

	/// <summary>
	/// Begins tracking a new completion request and returns its handle. The previous request's token is
	/// canceled first, so only the newest request can still publish.
	/// </summary>
	/// <remarks>
	/// The handle pairs the new request's identifier with its cancellation token, so the caller always
	/// observes the token of the request it admitted. Its identifier stays current until a newer request
	/// begins or <see cref="InvalidateRequests"/> is called. Admission is evaluated against the disposal
	/// state inside the coordinator's critical section, so a request admitted concurrently with disposal
	/// is either canceled by the disposal or rejected, never left pending, and a rejected call leaves
	/// every other request untouched.
	/// </remarks>
	/// <returns>The handle of the new request, or <see cref="RequestHandle.None"/> when the session is disposed.</returns>
	public RequestHandle BeginRequest()
	{
		RequestHandle request = _requests.BeginRequestIf(IsActive);

		if (request == RequestHandle.None)
			return RequestHandle.None;

		// Disposal may have run between the admission predicate and this re-check; the rejected call
		// cancels only its own admission, so it cannot disturb a request admitted through the
		// coordinator by someone else.
		lock (_sync)
		{
			if (!_isDisposed)
				return request;
		}

		_requests.CancelPendingRequest(request);
		return RequestHandle.None;
	}

	/// <summary>
	/// The shared lifecycle predicate that every gated operation evaluates inside the coordinator's
	/// critical section; it reads the volatile disposal flag only, so it cannot deadlock against the
	/// session lock.
	/// </summary>
	private bool IsActive() => !_isDisposed;

	/// <summary>
	/// Determines whether a request handle is still current: not invalidated and not disposed.
	/// </summary>
	/// <param name="request">The request handle to check.</param>
	/// <returns><see langword="true"/> when the handle is current; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// Canceling the request does not make its handle non-current: a host that drives the pipeline
	/// manually also treats a canceled <see cref="RequestHandle.CancellationToken"/> as a rejection, and
	/// <see cref="InvalidateRequests"/> is the way to reject an outstanding result without canceling it.
	/// Use <see cref="CanPublish"/> for one check that covers both staleness and cancellation.
	/// </remarks>
	public bool IsCurrent(RequestHandle request)
	{
		lock (_sync)
		{
			return !_isDisposed && _requests.IsCurrent(request);
		}
	}

	/// <summary>
	/// Determines whether a request handle is still current and its request has not been canceled,
	/// so a result produced for it may be published.
	/// </summary>
	/// <param name="request">The request handle to check.</param>
	/// <returns>
	/// <see langword="true"/> when the handle is current and its request's token is not
	/// canceled; otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// This combines the two rejection rules a standard request pipeline applies: the handle must
	/// still be the newest one and the request must not have been canceled by
	/// <see cref="CancelInFlightRequest"/> or a newer admission. The check is atomic - the identifier
	/// and the token are evaluated together under the coordinator lock - so a concurrent admission can
	/// never make a superseded handle report publishable. A host that checks only
	/// <see cref="IsCurrent"/> can publish a canceled result and must apply the cancellation check
	/// itself.
	/// </remarks>
	public bool CanPublish(RequestHandle request)
	{
		lock (_sync)
		{
			return !_isDisposed && _requests.CanPublish(request);
		}
	}

	/// <summary>
	/// Cancels the pending completion request's token, if any, so a cooperative provider can stop early.
	/// </summary>
	/// <remarks>
	/// Canceling only stops the provider call; the request stays current, and a host that drives the
	/// pipeline manually treats a canceled <see cref="RequestHandle.CancellationToken"/> as a rejection
	/// the same way a standard request pipeline does. Use <see cref="CanPublish"/> for the combined
	/// staleness and cancellation check. Starting a newer request cancels the pending request's token as
	/// well, and the canceled token stays observable through the handle of its request. The cancellation
	/// decision is evaluated inside the coordinator's critical section against the session's disposal
	/// state, so it is atomic with the cancel: a call that races <see cref="Dispose"/> either cancels the
	/// request that was pending at that moment or is rejected, and it can never reach a request admitted
	/// through <see cref="Coordinator"/> after disposal.
	/// </remarks>
	public void CancelInFlightRequest()
		=> _requests.CancelPendingRequestIf(IsActive);

	/// <summary>
	/// Marks outstanding completion requests as stale so completed results are ignored. The pending
	/// request's token is not canceled; use <see cref="CancelInFlightRequest"/> for that. The
	/// invalidation decision is evaluated inside the coordinator's critical section against the
	/// session's disposal state, exactly like the cancellation decision, so it can never invalidate
	/// a request admitted through <see cref="Coordinator"/> after disposal.
	/// </summary>
	public void InvalidateRequests()
		=> _requests.InvalidateIf(IsActive);

	/// <summary>
	/// Ends the session: invalidates outstanding requests and cancels the pending request's token. The
	/// method is idempotent, and the canceled token stays observable through the handle of its request.
	/// Disposal is serialized with admission, cancellation, and invalidation, so a concurrent request is
	/// either canceled by it or rejected with <see cref="RequestHandle.None"/>.
	/// </summary>
	public void Dispose()
	{
		lock (_sync)
		{
			if (_isDisposed)
				return;

			_isDisposed = true;
		}

		_requests.Invalidate();
		_requests.CancelPendingRequest();
	}
}
