namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Executes language-server requests with timeout enforcement, connection-generation fencing, a single retry after a connection boundary,
/// and a timeout-driven connection restart threshold; a request that can change state on the server is sent once, without a timeout or a retry.
/// </summary>
/// <remarks>
/// A collaborator internal to the provider framework:
/// the feature providers and sources send their requests through it by way of <see cref="ProviderContext"/>. The full policy and its rationale live
/// in the <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/LanguageServerInternals.md">language-server internals guide</see>.
/// </remarks>
internal sealed class RequestRunner
{
	private readonly ILanguageServerClient? _client;
	private readonly string _workspaceRootsDisplayText;
	private readonly ILogger _logger;
	private readonly ProviderLifetime _disposal;
	private readonly Func<CancellationToken, Task<bool>> _ensureConnectionStartedAsync;

	private readonly RequestAttemptRunner _attempts;

	/// <param name="context">The collaborators and values the runner is built from.</param>
	internal RequestRunner(RequestRunnerContext context)
	{
		_workspaceRootsDisplayText = context.WorkspaceRootsDisplayText;
		_client = context.Client;
		_disposal = context.Disposal;
		_ensureConnectionStartedAsync = context.EnsureConnectionStartedAsync;

		_attempts = new RequestAttemptRunner(
			workspaceRootsDisplayText: context.WorkspaceRootsDisplayText,
			requestTimeout: context.Options.RequestTimeout,
			disposal: context.Disposal,
			timeouts: context.Timeouts,
			logger: context.Logger
		);

		_logger = context.Logger;
	}

	/// <summary>
	/// Sends a language-server request with timeout tracking and a single retry when the active connection changes or becomes unavailable.
	/// </summary>
	/// <typeparam name="TResponse">The expected response type.</typeparam>
	/// <param name="method">The LSP method name.</param>
	/// <param name="parameters">The request parameters.</param>
	/// <param name="fallbackValue">
	/// The value returned when no client is configured, the request times out, the provider is disposed (including a disposal that races the send),
	/// the connection is still unavailable after the single retry, the server rejects the request, or the response payload cannot be processed.
	/// </param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The response returned by the language server, or <paramref name="fallbackValue"/>.</returns>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
	internal async Task<TResponse> SendAsync<TResponse>(
		string method,
		object parameters,
		TResponse fallbackValue,
		CancellationToken cancellationToken
	)
	{
		(bool succeeded, TResponse? response) = await SendOutcomeAsync<TResponse>(method, parameters, cancellationToken).ConfigureAwait(false);
		return succeeded ? response! : fallbackValue;
	}

	/// <summary>
	/// Sends a language-server request and reports whether a response was received, so the framework can tell a real response apart
	/// from the documented fallback value without inspecting the response type.
	/// </summary>
	/// <typeparam name="TResponse">The expected response type.</typeparam>
	/// <param name="method">The LSP method name.</param>
	/// <param name="parameters">The request parameters.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>
	/// <see langword="true"/> with the response when the server answered; otherwise, <see langword="false"/> with the default
	/// value when no response was received. This distinction is required for response types that are non-nullable structs,
	/// for which a <see langword="null"/> check cannot detect a fallback.
	/// </returns>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
	/// <remarks>
	/// <see cref="SendAsync{TResponse}(string, object, TResponse, CancellationToken)"/> collapses each outcome into the supplied fallback value,
	/// and this outcome path lets the provider framework itself tell a real response apart from that fallback. The failure classification
	/// is identical.
	/// </remarks>
	internal Task<(bool Succeeded, TResponse? Response)> SendOutcomeAsync<TResponse>(
		string method,
		object parameters,
		CancellationToken cancellationToken
	) => SendCoreAsync<TResponse>(method, parameters, policy: RequestSendPolicy.Repeatable, cancellationToken);

	/// <summary>
	/// Sends a request that can change state on the server, once: the request has no timeout, so only the caller's
	/// token and the disposal of the provider cancel it, and it never counts toward the restart threshold. A connection boundary ends the request
	/// instead of sending it again, because the server may already have acted on the first send.
	/// </summary>
	/// <typeparam name="TResponse">The expected response type.</typeparam>
	/// <param name="method">The LSP method name.</param>
	/// <param name="parameters">The request parameters.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>
	/// <see langword="true"/> with the response when the server answered; otherwise, <see langword="false"/> with the default value,
	/// also when a connection boundary ended the request.
	/// </returns>
	/// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
	internal Task<(bool Succeeded, TResponse? Response)> SendOnceAsync<TResponse>(
		string method,
		object parameters,
		CancellationToken cancellationToken
	) => SendCoreAsync<TResponse>(method, parameters, policy: RequestSendPolicy.Once, cancellationToken);

	private async Task<(bool Succeeded, TResponse? Response)> SendCoreAsync<TResponse>(
		string method,
		object parameters,
		RequestSendPolicy policy,
		CancellationToken cancellationToken
	)
	{
		ILanguageServerClient? client = _client;

		if (client is null)
			return (false, default);

		cancellationToken.ThrowIfCancellationRequested();

		long connectionGeneration = client.ConnectionGeneration;

		RequestAttemptResult<TResponse> attempt = await _attempts
			.SendAsync<TResponse>(client, method, parameters, connectionGeneration, isRetry: false, policy, cancellationToken)
			.ConfigureAwait(false);

		if (attempt.Outcome == RequestAttemptOutcome.Succeeded)
			return (true, attempt.Response);

		if (attempt.Outcome == RequestAttemptOutcome.Fallback)
			return (false, default);

		// The connection was superseded underneath the request or reported unavailability; ensure the client is healthy
		// before the single retry attempt on the refreshed connection generation. The startup helper can surface the
		// same failure modes as a request attempt, so it is classified here exactly like the attempt above.
		cancellationToken.ThrowIfCancellationRequested();

		bool connectionStarted;

		try
		{
			connectionStarted = await _ensureConnectionStartedAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (OperationCanceledException) when (_disposal.IsDisposed)
		{
			return (false, default);
		}
		catch (OperationCanceledException)
		{
			_logger.LogDebug(
				"Preparing the connection for the retry of '{Method}' for workspace '{Workspace}' was canceled by an unowned cancellation source; returning the fallback value.",
				method,
				_workspaceRootsDisplayText
			);

			return (false, default);
		}
		catch (IOException exception)
		{
			_logger.LogDebug(
				exception,
				"The language server request '{Method}' for workspace '{Workspace}' could not prepare a ready connection for the retry attempt; returning the fallback value.",
				method,
				_workspaceRootsDisplayText
			);

			return (false, default);
		}
		catch (ObjectDisposedException)
		{
			// The client was disposed while the provider is still alive; surface the documented fallback value
			// instead of leaking a disposal exception from the retry path.
			return (false, default);
		}

		if (!connectionStarted)
			return (false, default);

		connectionGeneration = client.ConnectionGeneration;

		attempt = await _attempts
			.SendAsync<TResponse>(client, method, parameters, connectionGeneration, isRetry: true, policy, cancellationToken)
			.ConfigureAwait(false);

		return attempt.Outcome == RequestAttemptOutcome.Succeeded ? (true, attempt.Response) : (false, default);
	}
}
