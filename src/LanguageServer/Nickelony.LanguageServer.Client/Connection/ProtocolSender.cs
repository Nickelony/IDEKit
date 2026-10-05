using System.Text.Json;
using Nickelony.LanguageServer.Client.Protocol;
using StreamJsonRpc;

namespace Nickelony.LanguageServer.Client;

/// <summary>Sends JSON-RPC notifications and requests over the active connection session.</summary>
/// <remarks>
/// <para>
/// Outbound sends apply the connection failure semantics of the client facade: a send that fails with a connection-classified
/// exception (<see cref="System.IO.IOException"/>, <see cref="ObjectDisposedException"/>, or <see cref="StreamJsonRpc.ConnectionLostException"/>)
/// marks the observed connection generation unhealthy and is reported as <see cref="LanguageServerConnectionUnavailableException"/>.
/// A JSON-RPC error response is reported as <see cref="LanguageServerRequestRejectedException"/>
/// and any other failure is rethrown unchanged, and neither invalidates the session. A superseded request result or rejection is discarded
/// as <see cref="LanguageServerConnectionSupersededException"/>.
/// </para>
/// <para>
/// A settings push refreshes the settings snapshot cache from the host before it sends, and any other successful
/// <c>workspace/didChangeConfiguration</c> notification refreshes the cache from its payload, so the pushed settings and the answers to the server's
/// configuration requests agree.
/// </para>
/// </remarks>
internal sealed class ProtocolSender
{
	private readonly ConnectionState _connectionState;
	private readonly SettingsSnapshotCache _settingsCache;
	private readonly BackgroundTaskObserver _backgroundTaskObserver;
	private readonly ILogger _logger;
	private readonly string _workspaceRootsDisplayText;
	private readonly ClientLifetime _lifetime;
	private readonly Action<bool> _throwIfDisposed;
	private readonly Func<long, bool> _tryMarkConnectionUnhealthy;

	/// <param name="context">The shared connection-collaborator context (connection state, logger, owner callbacks, and lifetime).</param>
	/// <param name="throwIfDisposed">Guards a call against the owning client's disposal, unless the argument allows disposed access.</param>
	/// <param name="settingsCache">
	/// Normalizes the outgoing <c>workspace/didChangeConfiguration</c> payload and learns the settings it carries.
	/// </param>
	/// <param name="backgroundTaskObserver">Observes the sends whose caller stopped waiting.</param>
	internal ProtocolSender(
		ConnectionCollaboratorContext context,
		Action<bool> throwIfDisposed,
		SettingsSnapshotCache settingsCache,
		BackgroundTaskObserver backgroundTaskObserver
	)
	{
		_connectionState = context.State;
		_settingsCache = settingsCache;
		_backgroundTaskObserver = backgroundTaskObserver;
		_logger = context.Logger;
		_workspaceRootsDisplayText = context.WorkspaceRootsDisplayText;
		_lifetime = context.Lifetime;
		_throwIfDisposed = throwIfDisposed;
		_tryMarkConnectionUnhealthy = context.TryMarkConnectionUnhealthy;
	}

	/// <inheritdoc cref="ILanguageServerClient.SendNotificationAsync(string, object, CancellationToken)"/>
	internal async Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken)
	{
		_throwIfDisposed(false);

		ConnectionSession session = _connectionState.GetRequiredReadySession();
		object? wireParameters = _settingsCache.NormalizeDidChangeConfigurationParameters(method, parameters);

		await SendHostNotificationAsync(session, method, parameters: wireParameters, cancellationToken).ConfigureAwait(false);

		_settingsCache.RefreshFromNotification(method, wireParameters);
	}

	/// <inheritdoc cref="ILanguageServerClient.PushSettingsAsync(CancellationToken)"/>
	internal async Task PushSettingsAsync(CancellationToken cancellationToken)
	{
		_throwIfDisposed(false);

		// The readiness check precedes the refresh, so a client that cannot send never runs the host's settings factory.
		ConnectionSession session = _connectionState.GetRequiredReadySession();

		if (!_settingsCache.TryRefreshFromProvider(out JsonElement settings))
			return;

		await SendHostNotificationAsync(
				session,
				method: LspMethodNames.DidChangeConfiguration,
				parameters: new DidChangeConfigurationParams(settings),
				cancellationToken
			)
			.ConfigureAwait(false);
	}

	/// <inheritdoc cref="ILanguageServerClient.SendRequestAsync{TResult}(string, object, CancellationToken)"/>
	internal async Task<TResult> SendRequestAsync<TResult>(string method, object parameters, CancellationToken cancellationToken)
	{
		_throwIfDisposed(false);

		ConnectionSession session = _connectionState.GetRequiredReadySession();

		TResult result;

		try
		{
			result = await SendRequestCoreAsync<TResult>(session, method, parameters, disposedAccess: DisposedAccessPolicy.Enforce, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception) when (IsConnectionOperationFailure(exception))
		{
			_tryMarkConnectionUnhealthy(session.ConnectionGeneration);
			LogConnectionOperationFailure(operationKind: "request", method, connectionGeneration: session.ConnectionGeneration, exception);

			throw new LanguageServerConnectionUnavailableException(innerException: exception, message: null);
		}
		catch (RemoteInvocationException exception)
		{
			// A rejection from a superseded connection generation is discarded like a superseded result: the
			// server that answered is not the active one, so its outcome must not be reported as this
			// request's failure.
			if (!_connectionState.CanAcceptRequestResultForSession(session))
			{
				_logger.LogDebug(
					"Discarding language server request '{Method}' rejection from connection generation {Generation} because the connection was superseded or marked unavailable before completion.",
					method,
					session.ConnectionGeneration
				);

				throw new LanguageServerConnectionSupersededException();
			}

			// A JSON-RPC error response is a server-side outcome: the connection stays ready and usable, and the
			// rejection is surfaced with a dedicated exception type so hosts can map it to a fallback value.
			_logger.LogDebug(
				exception,
				"Language server request '{Method}' for workspace '{Workspace}' was rejected with error code {ErrorCode} on connection generation {Generation}.",
				method,
				_workspaceRootsDisplayText,
				exception.ErrorCode,
				session.ConnectionGeneration
			);

			throw new LanguageServerRequestRejectedException(exception.ErrorCode, exception.Message, exception);
		}
		catch (Exception exception)
		{
			LogNonInvalidatingOperationFailure(operationKind: "request", method, connectionGeneration: session.ConnectionGeneration, exception);
			throw;
		}

		if (!_connectionState.CanAcceptRequestResultForSession(session))
		{
			_logger.LogDebug(
				"Discarding language server request '{Method}' result from connection generation {Generation} because the connection was superseded or marked unavailable before completion.",
				method,
				session.ConnectionGeneration
			);

			throw new LanguageServerConnectionSupersededException();
		}

		return result;
	}

	/// <summary>Sends a JSON-RPC notification over a specific connection session.</summary>
	/// <param name="session">The target connection session.</param>
	/// <param name="method">The JSON-RPC method name.</param>
	/// <param name="parameters">The notification payload, or <see langword="null"/> to send the notification without parameters.</param>
	/// <param name="disposedAccess">Whether the disposed-state guard is enforced or allowed for teardown.</param>
	/// <param name="cancellationToken">
	/// A token that can cancel waiting for local JSON-RPC dispatch while the notification task is still incomplete.
	/// </param>
	internal async Task SendNotificationCoreAsync(
		ConnectionSession session,
		string method,
		object? parameters,
		DisposedAccessPolicy disposedAccess,
		CancellationToken cancellationToken
	)
	{
		_throwIfDisposed(disposedAccess == DisposedAccessPolicy.Allow);

		cancellationToken.ThrowIfCancellationRequested();

		JsonRpc jsonRpc = session.JsonRpc ?? throw new IOException("The language server JSON-RPC instance is not available.");

		Task notificationTask = parameters is null ? jsonRpc.NotifyAsync(method) : jsonRpc.NotifyWithParameterObjectAsync(method, parameters);

		if (!cancellationToken.CanBeCanceled)
		{
			await notificationTask.ConfigureAwait(false);
			return;
		}

		// StreamJsonRpc may complete the notification task once local dispatch is handed off,
		// before the peer has necessarily flushed or processed the bytes.
		try
		{
			await notificationTask.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// The caller abandoned the wait while the JSON-RPC notification task is still running. Observe that task
			// so a later connection failure cannot escalate as an unobserved task exception.
			_backgroundTaskObserver.ObserveAbandoned(notificationTask);
			throw;
		}
	}

	/// <summary>Sends a JSON-RPC notification without parameters over a specific connection session.</summary>
	/// <param name="session">The target connection session.</param>
	/// <param name="method">The JSON-RPC method name.</param>
	/// <param name="disposedAccess">Whether the disposed-state guard is enforced or allowed for teardown.</param>
	/// <param name="cancellationToken">
	/// A token that can cancel waiting for local JSON-RPC dispatch; a notification already handed to the connection may still be sent.
	/// </param>
	internal Task SendNotificationCoreAsync(
		ConnectionSession session,
		string method,
		DisposedAccessPolicy disposedAccess,
		CancellationToken cancellationToken
	) => SendNotificationCoreAsync(session, method, parameters: null, disposedAccess, cancellationToken);

	/// <summary>Sends a JSON-RPC request over a specific connection session.</summary>
	/// <typeparam name="TResult">The expected response payload type.</typeparam>
	/// <param name="session">The target connection session.</param>
	/// <param name="method">The JSON-RPC method name.</param>
	/// <param name="parameters">The request payload.</param>
	/// <param name="disposedAccess">Whether the disposed-state guard is enforced or allowed for teardown.</param>
	/// <param name="cancellationToken">A token that can cancel the request. The client imposes no default timeout of its own.</param>
	/// <remarks>
	/// StreamJsonRpc forwards a canceled request token to the server as a <c>$/cancelRequest</c> notification and then keeps waiting
	/// for the server to answer. The invocation task alone would not honor the caller's token, so a server that accepts the request and never answers
	/// would leave the caller waiting forever. The wait is therefore bounded locally by <paramref name="cancellationToken"/>, and the still-running
	/// invocation is observed so its late failure cannot surface as an unobserved task exception.
	/// </remarks>
	internal async Task<TResult> SendRequestCoreAsync<TResult>(
		ConnectionSession session,
		string method,
		object parameters,
		DisposedAccessPolicy disposedAccess,
		CancellationToken cancellationToken
	)
	{
		_throwIfDisposed(disposedAccess == DisposedAccessPolicy.Allow);

		JsonRpc jsonRpc = session.JsonRpc ?? throw new IOException("The language server JSON-RPC instance is not available.");

		Task<TResult> requestTask = jsonRpc.InvokeWithParameterObjectAsync<TResult>(method, parameters, cancellationToken);

		if (!cancellationToken.CanBeCanceled)
			return await requestTask.ConfigureAwait(false);

		try
		{
			return await requestTask.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// The caller stopped waiting while the JSON-RPC request is still in flight. Observe the abandoned
			// task so a later connection failure is logged instead of escalating as an unobserved exception.
			_backgroundTaskObserver.ObserveAbandoned(requestTask);
			throw;
		}
	}

	/// <summary>
	/// Sends a host-initiated notification over the observed session and applies the connection failure semantics of the client facade.
	/// </summary>
	/// <param name="session">The ready session observed before the send.</param>
	/// <param name="method">The JSON-RPC method name.</param>
	/// <param name="parameters">The notification payload, or <see langword="null"/> to send the notification without parameters.</param>
	/// <param name="cancellationToken">A token that can cancel waiting for local JSON-RPC dispatch.</param>
	private async Task SendHostNotificationAsync(ConnectionSession session, string method, object? parameters, CancellationToken cancellationToken)
	{
		try
		{
			await SendNotificationCoreAsync(session, method, parameters, disposedAccess: DisposedAccessPolicy.Enforce, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception) when (IsConnectionOperationFailure(exception))
		{
			_tryMarkConnectionUnhealthy(session.ConnectionGeneration);
			LogConnectionOperationFailure(operationKind: "notification", method, connectionGeneration: session.ConnectionGeneration, exception);

			throw new LanguageServerConnectionUnavailableException(innerException: exception, message: null);
		}
		catch (Exception exception)
		{
			LogNonInvalidatingOperationFailure(operationKind: "notification", method, connectionGeneration: session.ConnectionGeneration, exception);
			throw;
		}
	}

	/// <summary>Logs one public request or notification failure with connection-generation context.</summary>
	/// <param name="operationKind">The operation kind, such as request or notification.</param>
	/// <param name="method">The JSON-RPC method name.</param>
	/// <param name="connectionGeneration">The connection generation that owned the operation.</param>
	/// <param name="exception">The connection failure.</param>
	private void LogConnectionOperationFailure(string operationKind, string method, long connectionGeneration, Exception exception)
	{
		if (_lifetime.IsDisposed)
			return;

		if (connectionGeneration != 0 && connectionGeneration != _connectionState.ConnectionGeneration)
		{
			_logger.LogDebug(
				exception,
				"Language server {OperationKind} '{Method}' failed on stale connection generation {Generation} for workspace '{Workspace}'.",
				operationKind,
				method,
				connectionGeneration,
				_workspaceRootsDisplayText
			);

			return;
		}

		_logger.LogWarning(
			exception,
			"Language server {OperationKind} '{Method}' failed on connection generation {Generation} for workspace '{Workspace}'; the host will recover by recreating the session when needed.",
			operationKind,
			method,
			connectionGeneration,
			_workspaceRootsDisplayText
		);
	}

	/// <summary>Logs one public request or notification failure that did not invalidate the active connection.</summary>
	/// <param name="operationKind">The operation kind, such as request or notification.</param>
	/// <param name="method">The JSON-RPC method name.</param>
	/// <param name="connectionGeneration">The connection generation that owned the operation.</param>
	/// <param name="exception">The failure.</param>
	private void LogNonInvalidatingOperationFailure(string operationKind, string method, long connectionGeneration, Exception exception)
	{
		if (_lifetime.IsDisposed)
			return;

		if (connectionGeneration != 0 && connectionGeneration != _connectionState.ConnectionGeneration)
		{
			_logger.LogDebug(
				exception,
				"Language server {OperationKind} '{Method}' failed on stale connection generation {Generation} for workspace '{Workspace}' without invalidating the active session.",
				operationKind,
				method,
				connectionGeneration,
				_workspaceRootsDisplayText
			);

			return;
		}

		_logger.LogWarning(
			exception,
			"Language server {OperationKind} '{Method}' failed on connection generation {Generation} for workspace '{Workspace}' without invalidating the active session.",
			operationKind,
			method,
			connectionGeneration,
			_workspaceRootsDisplayText
		);
	}

	private static bool IsConnectionOperationFailure(Exception exception) =>
		exception is IOException or ObjectDisposedException or ConnectionLostException;
}
