using System.Text.Json;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Sends one language-server request attempt with its own timeout and classifies how it settled.</summary>
/// <remarks>
/// A collaborator of <see cref="RequestRunner"/>, which decides whether a connection boundary earns the single retry. The attempt applies the timeout
/// and restart policy: consecutive timeouts on one connection generation are counted by the <see cref="RequestTimeoutCounter"/>, and a rejection
/// or an unprocessable response breaks the streak.
/// </remarks>
internal sealed class RequestAttemptRunner
{
	private const int MethodNotFoundErrorCode = -32601;

	// The protocol's answers for a request that the server dropped because its content changed or the request was canceled; a caller that needs
	// the result asks again, so neither is a fault.
	private const int ContentModifiedErrorCode = -32801;
	private const int RequestCancelledErrorCode = -32800;

	private readonly TimeSpan _requestTimeout;
	private readonly string _workspaceRootsDisplayText;
	private readonly ProviderLifetime _disposal;
	private readonly RequestTimeoutCounter _timeouts;
	private readonly ILogger _logger;

	/// <param name="workspaceRootsDisplayText">The comma-joined normalized workspace root paths used in log text.</param>
	/// <param name="requestTimeout">The time one attempt may wait for the server.</param>
	/// <param name="disposal">The disposal of the owning provider, whose start and token the attempt observes.</param>
	/// <param name="timeouts">The tracker of the consecutive request timeouts.</param>
	/// <param name="logger">The logger instance.</param>
	internal RequestAttemptRunner(
		string workspaceRootsDisplayText,
		TimeSpan requestTimeout,
		ProviderLifetime disposal,
		RequestTimeoutCounter timeouts,
		ILogger logger
	)
	{
		_workspaceRootsDisplayText = workspaceRootsDisplayText;
		_requestTimeout = requestTimeout;
		_disposal = disposal;
		_timeouts = timeouts;
		_logger = logger;
	}

	/// <summary>Sends one request attempt with its own timeout and classifies the outcome under the shared failure policy.</summary>
	/// <typeparam name="TResponse">The expected response type.</typeparam>
	/// <param name="client">The language-server client to send through.</param>
	/// <param name="method">The LSP method name.</param>
	/// <param name="parameters">The request parameters.</param>
	/// <param name="connectionGeneration">The connection generation observed for this attempt.</param>
	/// <param name="isRetry">Whether this is the single retry attempt; a connection boundary on the retry falls back instead.</param>
	/// <param name="policy">Whether the request may be timed out and sent again, as <see cref="RequestSendPolicy"/> says.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The attempt outcome and, when it succeeded, the response.</returns>
	/// <remarks>
	/// Caller cancellation always propagates. Provider disposal, the provider timeout, unowned cancellation, server rejections,
	/// unprocessable response payloads, and a connection boundary on the retry attempt produce the fallback value. A connection boundary
	/// on the primary attempt asks the caller to ensure the connection and retry once. A <see cref="RequestSendPolicy.Once"/> request has no timeout,
	/// so it never records one, and a connection boundary on its only attempt produces the fallback value.
	/// </remarks>
	internal async Task<RequestAttemptResult<TResponse>> SendAsync<TResponse>(
		ILanguageServerClient client,
		string method,
		object parameters,
		long connectionGeneration,
		bool isRetry,
		RequestSendPolicy policy,
		CancellationToken cancellationToken
	)
	{
		using var attemptTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposal.Token);

		if (policy == RequestSendPolicy.Repeatable)
			attemptTimeoutCts.CancelAfter(_requestTimeout);

		try
		{
			TResponse response = await client.SendRequestAsync<TResponse>(method, parameters, attemptTimeoutCts.Token).ConfigureAwait(false);
			_timeouts.Reset(connectionGeneration);

			// A caller cancellation observed after the response completed still surfaces as cancellation.
			cancellationToken.ThrowIfCancellationRequested();

			return new(RequestAttemptOutcome.Succeeded, response);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (OperationCanceledException) when (_disposal.IsDisposed)
		{
			// Disposal completed between the checks and the send; report the documented fallback value instead of
			// leaking a disposal exception into a caller whose contract promises a fallback value.
			return new(RequestAttemptOutcome.Fallback, default);
		}
		catch (OperationCanceledException) when (attemptTimeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
		{
			RecordTimeout(client, method, connectionGeneration);
			return new(RequestAttemptOutcome.Fallback, default);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			_logger.LogDebug(
				"The language server request '{Method}' for workspace '{Workspace}' was canceled by an unowned cancellation source (neither the caller token nor the provider timeout) on the {AttemptKind} for generation {Generation}; returning the fallback value without counting a timeout or forcing a restart.",
				method,
				_workspaceRootsDisplayText,
				isRetry ? "retry attempt" : "primary attempt",
				connectionGeneration
			);

			return new(RequestAttemptOutcome.Fallback, default);
		}
		catch (LanguageServerRequestRejectedException exception)
		{
			// Caller cancellation wins over the rejection outcome; without this check a cancellation that races the
			// rejection would surface the rejection instead of cancellation.
			cancellationToken.ThrowIfCancellationRequested();

			// A rejection is a completed exchange with a responsive connection, so it breaks the consecutive-timeout
			// streak instead of counting toward the restart threshold.
			_timeouts.Reset(connectionGeneration);

			LogRequestRejected(method, connectionGeneration, exception);

			return new(RequestAttemptOutcome.Fallback, default);
		}
		catch (LanguageServerConnectionSupersededException exception)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (policy == RequestSendPolicy.Once)
				return BoundaryWithoutRetry<TResponse>(exception, method, connectionGeneration);

			if (isRetry)
			{
				_logger.LogDebug(
					"The language server request '{Method}' for workspace '{Workspace}' crossed a second connection restart boundary on generation {Generation}; returning the fallback value.",
					method,
					_workspaceRootsDisplayText,
					connectionGeneration
				);

				return new(RequestAttemptOutcome.Fallback, default);
			}

			_logger.LogDebug(
				"The language server request '{Method}' for workspace '{Workspace}' crossed a connection restart boundary on generation {Generation}; retrying once.",
				method,
				_workspaceRootsDisplayText,
				connectionGeneration
			);

			return new(RequestAttemptOutcome.RetryConnection, default);
		}
		catch (ObjectDisposedException) when (_disposal.IsDisposed)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return new(RequestAttemptOutcome.Fallback, default);
		}
		catch (LanguageServerConnectionUnavailableException exception)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return ConnectionBoundaryOrFallback<TResponse>(exception, method, connectionGeneration, isRetry, policy);
		}
		catch (IOException exception)
		{
			// A plain IOException reports that the JSON-RPC instance is not available (for example when the connection was marked
			// unhealthy between the runner's readiness check and the send), which is the same kind of connection
			// boundary as the dedicated connection exceptions above: retry once, then fall back.
			cancellationToken.ThrowIfCancellationRequested();
			return ConnectionBoundaryOrFallback<TResponse>(exception, method, connectionGeneration, isRetry, policy);
		}
		catch (JsonException exception)
		{
			// A response payload that cannot be deserialized as the expected response type is unusable by contract:
			// report the documented fallback value instead of leaking the serializer fault to the caller. The
			// connection answered, so the exchange breaks the consecutive-timeout streak like a server rejection
			// does.
			cancellationToken.ThrowIfCancellationRequested();

			_timeouts.Reset(connectionGeneration);

			_logger.LogWarning(
				exception,
				"The language server request '{Method}' for workspace '{Workspace}' returned a response payload that could not be processed on generation {Generation}; returning the fallback value.",
				method,
				_workspaceRootsDisplayText,
				connectionGeneration
			);

			return new(RequestAttemptOutcome.Fallback, default);
		}
	}

	/// <summary>
	/// Classifies a connection-boundary failure as a single retry (primary attempt of a repeatable request) or the fallback value (retry attempt,
	/// or a request that is sent once).
	/// </summary>
	/// <param name="exception">The connection-boundary exception.</param>
	/// <param name="method">The LSP method name used in log text.</param>
	/// <param name="connectionGeneration">The connection generation the attempt observed.</param>
	/// <param name="isRetry">Whether the failed attempt was the single retry attempt.</param>
	/// <param name="policy">Whether the request may be sent again.</param>
	/// <returns>A retry outcome on the primary attempt of a repeatable request; otherwise, a fallback outcome.</returns>
	private RequestAttemptResult<TResponse> ConnectionBoundaryOrFallback<TResponse>(
		Exception exception,
		string method,
		long connectionGeneration,
		bool isRetry,
		RequestSendPolicy policy
	)
	{
		if (policy == RequestSendPolicy.Once)
			return BoundaryWithoutRetry<TResponse>(exception, method, connectionGeneration);

		if (isRetry)
		{
			_logger.LogDebug(
				exception,
				"The language server request '{Method}' for workspace '{Workspace}' failed again after connection generation {Generation} became unavailable; returning the fallback value.",
				method,
				_workspaceRootsDisplayText,
				connectionGeneration
			);

			return new(RequestAttemptOutcome.Fallback, default);
		}

		_logger.LogDebug(
			exception,
			"The language server request '{Method}' for workspace '{Workspace}' failed after connection generation {Generation} became unavailable; retrying once.",
			method,
			_workspaceRootsDisplayText,
			connectionGeneration
		);

		return new(RequestAttemptOutcome.RetryConnection, default);
	}

	/// <summary>Ends a request that is sent once at a connection boundary, because the server may already have acted on it.</summary>
	/// <param name="exception">The connection-boundary exception.</param>
	/// <param name="method">The LSP method name used in log text.</param>
	/// <param name="connectionGeneration">The connection generation the attempt observed.</param>
	/// <returns>The fallback outcome.</returns>
	private RequestAttemptResult<TResponse> BoundaryWithoutRetry<TResponse>(Exception exception, string method, long connectionGeneration)
	{
		_logger.LogWarning(
			exception,
			"The language server request '{Method}' for workspace '{Workspace}' lost connection generation {Generation} before it was answered; it is not sent again because the server may already have acted on it, so it reports the fallback value.",
			method,
			_workspaceRootsDisplayText,
			connectionGeneration
		);

		return new(RequestAttemptOutcome.Fallback, default);
	}

	/// <summary>
	/// Logs a server-side request rejection, reporting a missing method at debug level because a capability mismatch repeats for every request
	/// until the capabilities change.
	/// </summary>
	/// <param name="method">The LSP method name used in log text.</param>
	/// <param name="connectionGeneration">The connection generation the attempt observed.</param>
	/// <param name="exception">The rejection reported by the client.</param>
	private void LogRequestRejected(string method, long connectionGeneration, LanguageServerRequestRejectedException exception)
	{
		if (exception.ErrorCode == MethodNotFoundErrorCode)
		{
			_logger.LogDebug(
				exception,
				"The language server request '{Method}' for workspace '{Workspace}' is not implemented by the connected server (JSON-RPC error {ErrorCode}) on generation {Generation}; returning the fallback value.",
				method,
				_workspaceRootsDisplayText,
				exception.ErrorCode,
				connectionGeneration
			);

			return;
		}

		if (exception.ErrorCode is ContentModifiedErrorCode or RequestCancelledErrorCode)
		{
			_logger.LogDebug(
				exception,
				"The language server dropped the request '{Method}' for workspace '{Workspace}' with JSON-RPC error {ErrorCode} on generation {Generation}; returning the fallback value.",
				method,
				_workspaceRootsDisplayText,
				exception.ErrorCode,
				connectionGeneration
			);

			return;
		}

		_logger.LogWarning(
			exception,
			"The language server request '{Method}' for workspace '{Workspace}' was rejected with JSON-RPC error {ErrorCode} on generation {Generation}; returning the fallback value.",
			method,
			_workspaceRootsDisplayText,
			exception.ErrorCode,
			connectionGeneration
		);
	}

	/// <summary>
	/// Records one request timeout for its connection generation: consecutive timeouts on the same generation count toward the restart threshold,
	/// and reaching it marks that generation unhealthy at most once.
	/// </summary>
	/// <param name="client">The language-server client whose generation should be marked unhealthy.</param>
	/// <param name="method">The LSP method name used in log text.</param>
	/// <param name="connectionGeneration">The connection generation the timed-out attempt observed.</param>
	private void RecordTimeout(ILanguageServerClient client, string method, long connectionGeneration)
	{
		(int timeoutCount, bool restartDue) = _timeouts.RecordTimeout(connectionGeneration);
		int restartThreshold = _timeouts.RestartThreshold;

		if (restartDue)
		{
			if (client.TryMarkConnectionUnhealthy(connectionGeneration))
			{
				_logger.LogWarning(
					"The language server request '{Method}' for workspace '{Workspace}' timed out after {Timeout}s {Count} consecutive times on connection generation {Generation} (threshold {Threshold}); marking that connection unhealthy so the next request restarts it.",
					method,
					_workspaceRootsDisplayText,
					_requestTimeout.TotalSeconds,
					timeoutCount,
					connectionGeneration,
					restartThreshold
				);
			}
			else
			{
				_logger.LogDebug(
					"The language server request '{Method}' for workspace '{Workspace}' timed out on superseded connection generation {Generation}; leaving the active connection unchanged.",
					method,
					_workspaceRootsDisplayText,
					connectionGeneration
				);
			}

			return;
		}

		if (restartThreshold > 0)
		{
			_logger.LogDebug(
				"The language server request '{Method}' for workspace '{Workspace}' timed out after {Timeout}s (consecutive {Count}/{Threshold}, generation {Generation}).",
				method,
				_workspaceRootsDisplayText,
				_requestTimeout.TotalSeconds,
				timeoutCount,
				restartThreshold,
				connectionGeneration
			);
		}
		else
		{
			_logger.LogDebug(
				"The language server request '{Method}' for workspace '{Workspace}' timed out after {Timeout}s (timeout-driven restarts disabled, generation {Generation}).",
				method,
				_workspaceRootsDisplayText,
				_requestTimeout.TotalSeconds,
				connectionGeneration
			);
		}
	}
}
