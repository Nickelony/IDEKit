using System.Text.Json;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Executes language-server requests with timeout enforcement, transport-generation fencing, a single retry after a
/// transport boundary, and a timeout-driven transport restart threshold.
/// </summary>
/// <remarks>
/// A provider-composition helper internal to the provider framework: a derived provider reaches the same policy
/// through the protected request seam on <see cref="LanguageServerIntelliSenseProviderBase"/>. The full policy and
/// its rationale live in <c>docs/LanguageServerInternals.md</c>.
/// </remarks>
internal sealed class RequestDispatcher
{
	private const int MethodNotFoundErrorCode = -32601;
	private readonly ILanguageServerClient? _client;
	private readonly TimeSpan _requestTimeout;
	private readonly int _requestTimeoutRestartThreshold;
	private readonly string _workspaceRootsDisplayText;
	private readonly ILogger _logger;
	private readonly CancellationToken _disposeToken;
	private readonly Func<bool> _isDisposedAccessor;
	private readonly Func<CancellationToken, Task<bool>> _ensureTransportStartedAsync;

	private readonly object _requestTimeoutSyncRoot = new();

	private int _consecutiveRequestTimeouts;

	// The transport generation the timeout bookkeeping currently applies to; reset when a request settles on its
	// generation or a new generation finishes starting.
	private long _timeoutTrackingGeneration = -1;

	private long _restartRequestedGeneration = -1;

	/// <summary>
	/// Initializes a new instance of the <see cref="RequestDispatcher"/> class.
	/// </summary>
	/// <param name="workspaceRootsDisplayText">The comma-joined normalized workspace root paths used in log text.</param>
	/// <param name="client">The language-server client, or <see langword="null"/> when no server is configured.</param>
	/// <param name="options">The provider tunables that carry the request-timeout policy.</param>
	/// <param name="context">The owner services: disposal probing, startup, the disposal token, and the logger.</param>
	/// <exception cref="ArgumentException"><paramref name="workspaceRootsDisplayText"/> is empty or whitespace-only.</exception>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="workspaceRootsDisplayText"/>, <paramref name="options"/>, or <paramref name="context"/> is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <see cref="LanguageServerProviderOptions.RequestTimeout"/> is not a positive finite duration of at most
	/// <see cref="int.MaxValue"/> milliseconds, or
	/// <see cref="LanguageServerProviderOptions.RequestTimeoutRestartThreshold"/> is negative.
	/// </exception>
	public RequestDispatcher(
		string workspaceRootsDisplayText,
		ILanguageServerClient? client,
		LanguageServerProviderOptions options,
		RequestDispatcherContext context)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRootsDisplayText);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(context);

		if (options.RequestTimeout <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(options), options.RequestTimeout, "The request timeout must be a positive finite duration.");

		if (options.RequestTimeout.TotalMilliseconds > int.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(options), options.RequestTimeout, "The request timeout must be at most Int32.MaxValue milliseconds, the largest delay a request timeout can enforce.");

		ArgumentOutOfRangeException.ThrowIfNegative(options.RequestTimeoutRestartThreshold);

		_workspaceRootsDisplayText = workspaceRootsDisplayText;
		_client = client;
		_requestTimeout = options.RequestTimeout;
		_requestTimeoutRestartThreshold = options.RequestTimeoutRestartThreshold;
		_disposeToken = context.DisposeToken;
		_isDisposedAccessor = context.IsDisposedAccessor;
		_ensureTransportStartedAsync = context.EnsureTransportStartedAsync;
		_logger = context.Logger;
	}

	/// <summary>
	/// Sends a language-server request with timeout tracking and a single retry when the active transport changes or becomes unavailable.
	/// </summary>
	/// <typeparam name="TResponse">The expected response type.</typeparam>
	/// <param name="method">The LSP method name.</param>
	/// <param name="parameters">The request parameters.</param>
	/// <param name="fallbackValue">
	/// The value returned when the request times out, the provider is disposed (including a disposal that races the
	/// send), the transport is still unavailable after the single retry, the server rejects the request, or the
	/// response payload cannot be processed.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The response returned by the language server, or <paramref name="fallbackValue"/>.</returns>
	/// <exception cref="ArgumentException"><paramref name="method"/> is empty or whitespace-only.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="method"/> or <paramref name="parameters"/> is <see langword="null"/>.</exception>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
	public async Task<TResponse> SendAsync<TResponse>(
		string method,
		object parameters,
		TResponse fallbackValue,
		CancellationToken cancellationToken)
	{
		(bool succeeded, TResponse? response) = await SendOutcomeAsync<TResponse>(method, parameters, cancellationToken).ConfigureAwait(false);

		return succeeded ? response! : fallbackValue;
	}

	/// <summary>
	/// Sends a language-server request and reports whether a response was received, so the framework can tell a
	/// real response apart from the documented fallback value without inspecting the response type.
	/// </summary>
	/// <typeparam name="TResponse">The expected response type.</typeparam>
	/// <param name="method">The LSP method name.</param>
	/// <param name="parameters">The request parameters.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>
	/// <see langword="true"/> with the response when the server answered; otherwise, <see langword="false"/> with
	/// the default value when the request settled with the fallback outcome. This distinction is required for
	/// response types that are non-nullable structs, for which a null check cannot detect a fallback.
	/// </returns>
	/// <exception cref="ArgumentException"><paramref name="method"/> is empty or whitespace-only.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="method"/> or <paramref name="parameters"/> is <see langword="null"/>.</exception>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
	/// <remarks>
	/// Internal framework machinery: the public composition surface
	/// (<see cref="SendAsync{TResponse}(string, object, TResponse, CancellationToken)"/>) collapses both outcomes
	/// into the supplied fallback value, and this outcome path lets the provider framework itself tell a real
	/// response apart from that fallback. The failure classification is identical.
	/// </remarks>
	internal async Task<(bool Succeeded, TResponse? Response)> SendOutcomeAsync<TResponse>(
		string method,
		object parameters,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(method);
		ArgumentNullException.ThrowIfNull(parameters);

		ILanguageServerClient? client = _client;

		if (client is null)
			return (false, default);

		cancellationToken.ThrowIfCancellationRequested();

		long transportGeneration = client.TransportGeneration;

		RequestAttemptResult<TResponse> attempt = await TrySendRequestAsync<TResponse>(
			client, method, parameters, transportGeneration, isRetry: false, cancellationToken).ConfigureAwait(false);

		if (attempt.Outcome == RequestAttemptOutcome.Succeeded)
			return (true, attempt.Response);

		if (attempt.Outcome == RequestAttemptOutcome.Fallback)
			return (false, default);

		// The transport changed underneath the request or reported unavailability; ensure the client is healthy
		// before the single retry attempt on the refreshed transport generation. The startup helper can surface the
		// same failure modes as a request attempt, so it is classified here exactly like the attempt above.
		cancellationToken.ThrowIfCancellationRequested();

		bool transportStarted;

		try
		{
			transportStarted = await _ensureTransportStartedAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (OperationCanceledException) when (_isDisposedAccessor() || _disposeToken.IsCancellationRequested)
		{
			return (false, default);
		}
		catch (OperationCanceledException)
		{
			_logger.LogDebug("Preparing the transport for the retry of '{Method}' for workspace '{Workspace}' was canceled by an unowned cancellation source; returning the fallback value.",
				method,
				_workspaceRootsDisplayText);

			return (false, default);
		}
		catch (IOException exception)
		{
			_logger.LogDebug(exception, "The language server request '{Method}' for workspace '{Workspace}' could not prepare a ready transport for the retry attempt; returning the fallback value.",
				method,
				_workspaceRootsDisplayText);

			return (false, default);
		}
		catch (ObjectDisposedException)
		{
			// The client was disposed while the provider is still alive; surface the documented fallback value
			// instead of leaking a disposal exception from the retry path.
			return (false, default);
		}

		if (!transportStarted)
			return (false, default);

		transportGeneration = client.TransportGeneration;

		attempt = await TrySendRequestAsync<TResponse>(
			client, method, parameters, transportGeneration, isRetry: true, cancellationToken).ConfigureAwait(false);

		return attempt.Outcome == RequestAttemptOutcome.Succeeded ? (true, attempt.Response) : (false, default);
	}

	/// <summary>
	/// Describes how one request attempt settled under the dispatcher's failure policy.
	/// </summary>
	private enum RequestAttemptOutcome
	{
		/// <summary>The attempt completed and produced a response.</summary>
		Succeeded,

		/// <summary>A transport boundary was observed on the primary attempt; the dispatcher may retry once.</summary>
		RetryTransport,

		/// <summary>The attempt settled with the documented fallback value.</summary>
		Fallback
	}

	/// <summary>
	/// Carries the outcome of one request attempt and, when it succeeded, the response.
	/// </summary>
	/// <typeparam name="TResponse">The expected response type.</typeparam>
	/// <param name="Outcome">The attempt outcome.</param>
	/// <param name="Response">The response when <paramref name="Outcome"/> is <see cref="RequestAttemptOutcome.Succeeded"/>.</param>
	private readonly record struct RequestAttemptResult<TResponse>(RequestAttemptOutcome Outcome, TResponse? Response);

	/// <summary>
	/// Sends one request attempt with its own timeout and classifies the outcome under the dispatcher's shared failure policy.
	/// </summary>
	/// <typeparam name="TResponse">The expected response type.</typeparam>
	/// <param name="client">The language-server client to send through.</param>
	/// <param name="method">The LSP method name.</param>
	/// <param name="parameters">The request parameters.</param>
	/// <param name="transportGeneration">The transport generation observed for this attempt.</param>
	/// <param name="isRetry">Whether this is the single retry attempt; a transport boundary on the retry falls back instead.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The attempt outcome and, when it succeeded, the response.</returns>
	/// <remarks>
	/// Caller cancellation always propagates. Provider disposal, the provider timeout, unowned cancellation, server
	/// rejections, unprocessable response payloads, and a transport boundary on the retry attempt produce the
	/// fallback value. A transport boundary on the primary attempt asks the caller to ensure the transport and
	/// retry once.
	/// </remarks>
	private async Task<RequestAttemptResult<TResponse>> TrySendRequestAsync<TResponse>(
		ILanguageServerClient client,
		string method,
		object parameters,
		long transportGeneration,
		bool isRetry,
		CancellationToken cancellationToken)
	{
		using var attemptTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeToken);
		attemptTimeoutCts.CancelAfter(_requestTimeout);

		try
		{
			TResponse response = await client.SendRequestAsync<TResponse>(method, parameters, attemptTimeoutCts.Token).ConfigureAwait(false);
			ResetTimeoutTracking(transportGeneration);

			// A caller cancellation observed after the response completed still surfaces as cancellation.
			cancellationToken.ThrowIfCancellationRequested();

			return new(RequestAttemptOutcome.Succeeded, response);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (OperationCanceledException) when (_isDisposedAccessor() || _disposeToken.IsCancellationRequested)
		{
			// Disposal completed between the checks and the send; report the documented fallback value instead of
			// leaking a disposal exception into a caller whose contract promises a fallback value.
			return new(RequestAttemptOutcome.Fallback, default);
		}
		catch (OperationCanceledException) when (attemptTimeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
		{
			RecordTimeout(client, method, transportGeneration);

			return new(RequestAttemptOutcome.Fallback, default);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			_logger.LogDebug("The language server request '{Method}' for workspace '{Workspace}' was canceled by an unowned cancellation source (neither the caller token nor the provider timeout) on the {AttemptKind} for generation {Generation}; returning the fallback value without counting a timeout or forcing a restart.",
				method,
				_workspaceRootsDisplayText,
				isRetry ? "retry attempt" : "primary attempt",
				transportGeneration);

			return new(RequestAttemptOutcome.Fallback, default);
		}
		catch (LanguageServerRequestRejectedException exception)
		{
			// Caller cancellation wins over the rejection outcome; without this check a cancellation that races the
			// rejection would surface the rejection instead of cancellation.
			cancellationToken.ThrowIfCancellationRequested();

			// A rejection is a completed exchange with a responsive transport, so it breaks the consecutive-timeout
			// streak instead of counting toward the restart threshold.
			ResetTimeoutTracking(transportGeneration);

			LogRequestRejected(method, transportGeneration, exception);

			return new(RequestAttemptOutcome.Fallback, default);
		}
		catch (LanguageServerTransportChangedException)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (isRetry)
			{
				_logger.LogDebug("The language server request '{Method}' for workspace '{Workspace}' crossed a second transport restart boundary on generation {Generation}; returning the fallback value.",
					method,
					_workspaceRootsDisplayText,
					transportGeneration);

				return new(RequestAttemptOutcome.Fallback, default);
			}

			_logger.LogDebug("The language server request '{Method}' for workspace '{Workspace}' crossed a transport restart boundary on generation {Generation}; retrying once.",
				method,
				_workspaceRootsDisplayText,
				transportGeneration);

			return new(RequestAttemptOutcome.RetryTransport, default);
		}
		catch (ObjectDisposedException) when (_isDisposedAccessor() || _disposeToken.IsCancellationRequested)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return new(RequestAttemptOutcome.Fallback, default);
		}
		catch (LanguageServerTransportUnavailableException exception)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return TransportBoundaryOrFallback<TResponse>(exception, method, transportGeneration, isRetry);
		}
		catch (IOException exception)
		{
			// A plain IOException reports "no ready transport session" (for example when the transport was marked
			// unhealthy between the dispatcher's readiness check and the send), which is the same kind of transport
			// boundary as the dedicated transport exceptions above: retry once, then fall back.
			cancellationToken.ThrowIfCancellationRequested();

			return TransportBoundaryOrFallback<TResponse>(exception, method, transportGeneration, isRetry);
		}
		catch (JsonException exception)
		{
			// A response payload that cannot be deserialized as the expected response type is unusable by contract:
			// report the documented fallback value instead of leaking the serializer fault to the caller. The
			// transport answered, so the exchange breaks the consecutive-timeout streak like a server rejection
			// does.
			cancellationToken.ThrowIfCancellationRequested();

			ResetTimeoutTracking(transportGeneration);

			_logger.LogWarning(exception, "The language server request '{Method}' for workspace '{Workspace}' returned a response payload that could not be processed on generation {Generation}; returning the fallback value.",
				method,
				_workspaceRootsDisplayText,
				transportGeneration);

			return new(RequestAttemptOutcome.Fallback, default);
		}
	}

	/// <summary>
	/// Classifies a transport-boundary failure as a single retry (primary attempt) or the fallback value (retry attempt).
	/// </summary>
	/// <param name="exception">The transport-boundary exception.</param>
	/// <param name="method">The LSP method name used in log text.</param>
	/// <param name="transportGeneration">The transport generation the attempt observed.</param>
	/// <param name="isRetry">Whether the failed attempt was the single retry attempt.</param>
	/// <returns>The retry request on the primary attempt; otherwise, the fallback value.</returns>
	private RequestAttemptResult<TResponse> TransportBoundaryOrFallback<TResponse>(Exception exception, string method, long transportGeneration, bool isRetry)
	{
		if (isRetry)
		{
			_logger.LogDebug(exception,
				"The language server request '{Method}' for workspace '{Workspace}' failed again after transport generation {Generation} became unavailable; returning the fallback value.",
				method,
				_workspaceRootsDisplayText,
				transportGeneration);

			return new(RequestAttemptOutcome.Fallback, default);
		}

		_logger.LogDebug(exception,
			"The language server request '{Method}' for workspace '{Workspace}' failed after transport generation {Generation} became unavailable; retrying once.",
			method,
			_workspaceRootsDisplayText,
			transportGeneration);

		return new(RequestAttemptOutcome.RetryTransport, default);
	}

	/// <summary>
	/// Logs a server-side request rejection, reporting a missing method at debug level because a capability mismatch
	/// repeats for every request until the capabilities change.
	/// </summary>
	/// <param name="method">The LSP method name used in log text.</param>
	/// <param name="transportGeneration">The transport generation the attempt observed.</param>
	/// <param name="exception">The rejection reported by the client.</param>
	private void LogRequestRejected(string method, long transportGeneration, LanguageServerRequestRejectedException exception)
	{
		if (exception.ErrorCode == MethodNotFoundErrorCode)
		{
			_logger.LogDebug(exception,
				"The language server request '{Method}' for workspace '{Workspace}' is not implemented by the connected server (JSON-RPC error {ErrorCode}) on generation {Generation}; returning the fallback value.",
				method,
				_workspaceRootsDisplayText,
				exception.ErrorCode,
				transportGeneration);

			return;
		}

		_logger.LogWarning(exception,
			"The language server request '{Method}' for workspace '{Workspace}' was rejected with JSON-RPC error {ErrorCode} on generation {Generation}; returning the fallback value.",
			method,
			_workspaceRootsDisplayText,
			exception.ErrorCode,
			transportGeneration);
	}

	/// <summary>
	/// Resets the per-generation timeout bookkeeping after a request settled on its transport generation without
	/// timing out or a new transport generation finished starting successfully.
	/// </summary>
	/// <param name="transportGeneration">The transport generation that settled without timing out or finished starting.</param>
	/// <remarks>
	/// A settlement from a superseded transport generation is ignored: only a generation at least as new as the
	/// tracked one may clear the streak and the once-per-generation restart latch, so a late success from a
	/// replaced transport cannot mask consecutive timeouts on the active one.
	/// </remarks>
	public void ResetTimeoutTracking(long transportGeneration)
	{
		lock (_requestTimeoutSyncRoot)
		{
			if (transportGeneration < _timeoutTrackingGeneration)
				return;

			_timeoutTrackingGeneration = transportGeneration;
			_consecutiveRequestTimeouts = 0;
			_restartRequestedGeneration = -1;
		}
	}

	/// <summary>
	/// Records one request timeout for its transport generation: consecutive timeouts on the same generation
	/// count toward the restart threshold, and reaching it marks that generation unhealthy at most once.
	/// </summary>
	/// <param name="client">The language-server client whose generation should be marked unhealthy.</param>
	/// <param name="method">The LSP method name used in log text.</param>
	/// <param name="transportGeneration">The transport generation the timed-out attempt observed.</param>
	private void RecordTimeout(ILanguageServerClient client, string method, long transportGeneration)
	{
		int timeoutCount;
		bool shouldMarkTransportUnhealthy = false;

		lock (_requestTimeoutSyncRoot)
		{
			if (_timeoutTrackingGeneration != transportGeneration)
			{
				_timeoutTrackingGeneration = transportGeneration;
				_consecutiveRequestTimeouts = 0;
				_restartRequestedGeneration = -1;
			}

			timeoutCount = ++_consecutiveRequestTimeouts;

			// A zero threshold disables timeout-driven restarts; the timeout still returns the fallback value.
			if (_requestTimeoutRestartThreshold > 0
				&& timeoutCount >= _requestTimeoutRestartThreshold
				&& _restartRequestedGeneration != transportGeneration)
			{
				_restartRequestedGeneration = transportGeneration;
				shouldMarkTransportUnhealthy = true;
			}
		}

		if (shouldMarkTransportUnhealthy)
		{
			if (client.TryMarkTransportUnhealthy(transportGeneration))
			{
				_logger.LogWarning("The language server request '{Method}' for workspace '{Workspace}' timed out after {Timeout}s {Count} consecutive times on transport generation {Generation} (threshold {Threshold}); marking that transport unhealthy so the next request restarts it.",
					method,
					_workspaceRootsDisplayText,
					_requestTimeout.TotalSeconds,
					timeoutCount,
					transportGeneration,
					_requestTimeoutRestartThreshold);
			}
			else
			{
				_logger.LogDebug("The language server request '{Method}' for workspace '{Workspace}' timed out on superseded transport generation {Generation}; leaving the active transport unchanged.",
					method,
					_workspaceRootsDisplayText,
					transportGeneration);
			}

			return;
		}

		if (_requestTimeoutRestartThreshold > 0)
		{
			_logger.LogDebug("The language server request '{Method}' for workspace '{Workspace}' timed out after {Timeout}s (consecutive {Count}/{Threshold}, generation {Generation}).",
				method,
				_workspaceRootsDisplayText,
				_requestTimeout.TotalSeconds,
				timeoutCount,
				_requestTimeoutRestartThreshold,
				transportGeneration);
		}
		else
		{
			_logger.LogDebug("The language server request '{Method}' for workspace '{Workspace}' timed out after {Timeout}s (timeout-driven restarts disabled, generation {Generation}).",
				method,
				_workspaceRootsDisplayText,
				_requestTimeout.TotalSeconds,
				transportGeneration);
		}
	}
}
