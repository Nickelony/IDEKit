using StreamJsonRpc;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Sends JSON-RPC notifications and requests over the active transport session and owns the cached workspace
/// settings snapshot used by configuration callbacks.
/// </summary>
/// <remarks>
/// <para>
/// Outbound sends apply the transport failure semantics of the client facade: a send that fails with a
/// transport-classified exception (<see cref="System.IO.IOException"/>, <see cref="ObjectDisposedException"/>,
/// or <see cref="StreamJsonRpc.ConnectionLostException"/>) marks the observed transport generation unhealthy
/// and is reported as <see cref="LanguageServerTransportUnavailableException"/>, while any other failure is
/// rethrown unchanged without invalidating the session. A superseded request result or rejection is discarded as
/// <see cref="LanguageServerTransportChangedException"/>.
/// </para>
/// <para>
/// The settings snapshot is captured lazily from the host and refreshed whenever a
/// <c>workspace/didChangeConfiguration</c> notification is sent successfully.
/// </para>
/// </remarks>
internal sealed class ProtocolForwarder
{
	private readonly object _settingsSnapshotSyncRoot = new();

	private readonly CapabilityStore _capabilityStore;
	private readonly ILogger _logger;
	private readonly string _workspaceRootsDisplayText;
	private readonly Func<bool> _isDisposed;
	private readonly Action<bool> _throwIfDisposed;
	private readonly Func<long, bool> _tryMarkTransportUnhealthy;
	private readonly Func<object> _settingsProvider;

	private CachedSettingsSnapshot? _cachedSettingsSnapshot;

	/// <summary>
	/// Initializes a new instance of the <see cref="ProtocolForwarder"/> class.
	/// </summary>
	/// <param name="capabilityStore">Provides session lookup and transport-generation fencing.</param>
	/// <param name="logger">The logger used for transport operations that did not invalidate the session.</param>
	/// <param name="workspaceRootsDisplayText">The comma-joined normalized workspace root paths used in failure diagnostics.</param>
	/// <param name="isDisposed">Reports whether the owning client started disposal.</param>
	/// <param name="throwIfDisposed">Guards a call against the owning client's disposal, unless the argument allows disposed access.</param>
	/// <param name="tryMarkTransportUnhealthy">Marks one observed transport generation unhealthy with client-level diagnostics.</param>
	/// <param name="settingsProvider">Produces the current settings payload for <c>workspace/didChangeConfiguration</c>.</param>
	internal ProtocolForwarder(
		CapabilityStore capabilityStore,
		ILogger logger,
		string workspaceRootsDisplayText,
		Func<bool> isDisposed,
		Action<bool> throwIfDisposed,
		Func<long, bool> tryMarkTransportUnhealthy,
		Func<object> settingsProvider)
	{
		_capabilityStore = capabilityStore;
		_logger = logger;
		_workspaceRootsDisplayText = workspaceRootsDisplayText;
		_isDisposed = isDisposed;
		_throwIfDisposed = throwIfDisposed;
		_tryMarkTransportUnhealthy = tryMarkTransportUnhealthy;
		_settingsProvider = settingsProvider;
	}

	/// <inheritdoc cref="ILanguageServerClient.SendNotificationAsync(string, object, CancellationToken)"/>
	internal async Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken)
	{
		_throwIfDisposed(false);

		TransportSession session = _capabilityStore.GetRequiredReadySession();
		object? wireParameters = NormalizeDidChangeConfigurationParameters(method, parameters);

		try
		{
			await SendNotificationCoreAsync(session, method, wireParameters, allowDisposed: false, cancellationToken).ConfigureAwait(false);
			TryRefreshCachedSettingsSnapshotFromNotification(method, wireParameters);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception) when (IsTransportOperationFailure(exception))
		{
			_tryMarkTransportUnhealthy(session.Generation);
			LogTransportOperationFailure("notification", method, session.Generation, exception);

			throw new LanguageServerTransportUnavailableException(innerException: exception, message: null);
		}
		catch (Exception exception)
		{
			LogNonTransportOperationFailure("notification", method, session.Generation, exception);
			throw;
		}
	}

	/// <inheritdoc cref="ILanguageServerClient.SendRequestAsync{TResult}(string, object, CancellationToken)"/>
	internal async Task<TResult> SendRequestAsync<TResult>(string method, object parameters, CancellationToken cancellationToken)
	{
		_throwIfDisposed(false);

		TransportSession session = _capabilityStore.GetRequiredReadySession();

		TResult result;

		try
		{
			result = await SendRequestCoreAsync<TResult>(session, method, parameters, allowDisposed: false, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception) when (IsTransportOperationFailure(exception))
		{
			_tryMarkTransportUnhealthy(session.Generation);
			LogTransportOperationFailure("request", method, session.Generation, exception);

			throw new LanguageServerTransportUnavailableException(innerException: exception, message: null);
		}
		catch (RemoteInvocationException exception)
		{
			// A rejection from a superseded transport generation is discarded like a superseded result: the
			// server that answered is no longer the active one, so its outcome must not be reported as this
			// request's failure.
			if (!_capabilityStore.CanAcceptRequestResultForSession(session))
			{
				_logger.LogDebug(
					"Discarding language server request '{Method}' rejection from transport generation {Generation} because the transport was superseded or marked unavailable before completion.",
					method,
					session.Generation);

				throw new LanguageServerTransportChangedException();
			}

			// A JSON-RPC error response is a server-side outcome: the transport stays ready and usable, and the
			// rejection is surfaced with a dedicated exception type so hosts can map it to a fallback value.
			_logger.LogDebug(exception,
				"Language server request '{Method}' for workspace '{Workspace}' was rejected with error code {ErrorCode} on transport generation {Generation}.",
				method,
				_workspaceRootsDisplayText,
				exception.ErrorCode,
				session.Generation);

			throw new LanguageServerRequestRejectedException(exception.ErrorCode, exception.Message, exception);
		}
		catch (Exception exception)
		{
			LogNonTransportOperationFailure("request", method, session.Generation, exception);
			throw;
		}

		if (!_capabilityStore.CanAcceptRequestResultForSession(session))
		{
			_logger.LogDebug(
				"Discarding language server request '{Method}' result from transport generation {Generation} because the transport was superseded or marked unavailable before completion.",
				method,
				session.Generation);

			throw new LanguageServerTransportChangedException();
		}

		return result;
	}

	/// <summary>
	/// Sends a JSON-RPC notification over a specific transport session.
	/// </summary>
	/// <param name="session">The target transport session.</param>
	/// <param name="method">The JSON-RPC method name.</param>
	/// <param name="parameters">The notification payload, or <see langword="null"/> to send the notification without parameters.</param>
	/// <param name="allowDisposed">Whether disposed-state checks should be skipped.</param>
	/// <param name="cancellationToken">A token that can cancel waiting for local JSON-RPC dispatch while the notification task is still incomplete.</param>
	/// <returns>The send task.</returns>
	internal async Task SendNotificationCoreAsync(TransportSession session, string method, object? parameters, bool allowDisposed, CancellationToken cancellationToken)
	{
		_throwIfDisposed(allowDisposed);

		cancellationToken.ThrowIfCancellationRequested();

		JsonRpc jsonRpc = session.JsonRpc
			?? throw new IOException("The language server JSON-RPC transport is not available.");

		Task notificationTask = parameters is null
			? jsonRpc.NotifyAsync(method)
			: jsonRpc.NotifyWithParameterObjectAsync(method, parameters);

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
			// so a later transport failure cannot escalate as an unobserved task exception.
			ObserveAbandonedTask(notificationTask);
			throw;
		}
	}

	/// <summary>
	/// Sends a JSON-RPC notification without parameters over a specific transport session.
	/// </summary>
	/// <param name="session">The target transport session.</param>
	/// <param name="method">The JSON-RPC method name.</param>
	/// <param name="allowDisposed">Whether disposed-state checks should be skipped.</param>
	/// <param name="cancellationToken">A token that can cancel waiting for local JSON-RPC dispatch; a notification already handed to the transport may still be sent.</param>
	/// <returns>The send task.</returns>
	internal Task SendNotificationCoreAsync(TransportSession session, string method, bool allowDisposed, CancellationToken cancellationToken)
		=> SendNotificationCoreAsync(session, method, parameters: null, allowDisposed, cancellationToken);

	/// <summary>
	/// Observes a transport or teardown task that was abandoned by a canceled or timed-out wait so a later fault is
	/// logged through the client logger instead of surfacing as an unobserved task exception.
	/// </summary>
	/// <param name="task">The still-running task.</param>
	internal void ObserveAbandonedTask(Task task)
	{
		task.ContinueWith(
			static (completedTask, state) =>
			{
				if (completedTask.IsCompletedSuccessfully)
					return;

				if (completedTask.Exception is not { } aggregateException || state is not ILogger logger)
					return;

				logger.LogDebug(aggregateException.Flatten(), "An abandoned language server transport task later failed.");
			},
			_logger,
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);
	}

	/// <summary>
	/// Sends a JSON-RPC request over a specific transport session.
	/// </summary>
	/// <remarks>
	/// StreamJsonRpc forwards a canceled request token to the server as a <c>$/cancelRequest</c> notification and
	/// then keeps waiting for the server to answer, so the invocation task alone would not honor the caller's
	/// token: a server that accepts the request and never answers would leave the caller waiting forever. The wait
	/// is therefore bounded locally by <paramref name="cancellationToken"/>; the still-running invocation is
	/// observed so its late failure cannot surface as an unobserved task exception.
	/// </remarks>
	/// <typeparam name="TResult">The expected response payload type.</typeparam>
	/// <param name="session">The target transport session.</param>
	/// <param name="method">The JSON-RPC method name.</param>
	/// <param name="parameters">The request payload.</param>
	/// <param name="allowDisposed">Whether disposed-state checks should be skipped.</param>
	/// <param name="cancellationToken">A token that can cancel the request. The transport does not impose its own default timeout.</param>
	/// <returns>The typed response task.</returns>
	internal async Task<TResult> SendRequestCoreAsync<TResult>(TransportSession session, string method, object parameters, bool allowDisposed, CancellationToken cancellationToken)
	{
		_throwIfDisposed(allowDisposed);

		JsonRpc jsonRpc = session.JsonRpc
			?? throw new IOException("The language server JSON-RPC transport is not available.");

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
			// task so a later transport failure is logged instead of escalating as an unobserved exception.
			ObserveAbandonedTask(requestTask);
			throw;
		}
	}

	/// <summary>
	/// Builds the configuration response payload requested by the language server.
	/// </summary>
	/// <param name="parameters">The requested configuration sections.</param>
	/// <returns>
	/// The configuration response objects in request order, one per requested entry; a null or malformed request
	/// entry is answered with <see langword="null"/>.
	/// </returns>
	internal object?[] BuildConfigurationResponse(WorkspaceConfigurationParams parameters)
	{
		WorkspaceConfigurationItem[] items = parameters.Items ?? [];

		if (items.Length == 0)
			return [];

		try
		{
			JsonElement settingsElement = GetCachedSettingsSnapshot().SettingsElement;
			var results = new object?[items.Length];

			for (int i = 0; i < items.Length; i++)
			{
				// A null or malformed request entry has no section to resolve, and the placeholder keeps the
				// response aligned one-for-one with the request as LSP requires.
				if (items[i].IsMalformed)
				{
					results[i] = null;
					continue;
				}

				try
				{
					results[i] = JsonConfigurationSectionReader.GetSection(settingsElement, items[i].Section);
				}
				catch (Exception exception)
				{
					_logger.LogWarning(exception,
						"Failed to extract workspace/configuration section '{Section}'; returning null for that section.",
						string.IsNullOrWhiteSpace(items[i].Section) ? "<root>" : items[i].Section);

					results[i] = null;
				}
			}

			return results;
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception,
				"Failed to build the workspace/configuration response; returning null values for {SectionCount} requested section(s).",
				items.Length);

			return new object?[items.Length];
		}
	}

	/// <summary>
	/// Logs one public request or notification failure with transport-generation context.
	/// </summary>
	/// <param name="operationKind">The operation kind, such as request or notification.</param>
	/// <param name="method">The JSON-RPC method name.</param>
	/// <param name="generation">The transport generation that owned the operation.</param>
	/// <param name="exception">The transport failure.</param>
	private void LogTransportOperationFailure(string operationKind, string method, long generation, Exception exception)
	{
		if (_isDisposed())
			return;

		if (generation != 0 && generation != _capabilityStore.TransportGeneration)
		{
			_logger.LogDebug(exception,
				"Language server {OperationKind} '{Method}' failed on stale transport generation {Generation} for workspace '{Workspace}'.",
				operationKind,
				method,
				generation,
				_workspaceRootsDisplayText);

			return;
		}

		_logger.LogWarning(exception,
			"Language server {OperationKind} '{Method}' failed on transport generation {Generation} for workspace '{Workspace}'; the host will recover by recreating the session when needed.",
			operationKind,
			method,
			generation,
			_workspaceRootsDisplayText);
	}

	/// <summary>
	/// Logs one public request or notification failure that did not invalidate the active transport.
	/// </summary>
	/// <param name="operationKind">The operation kind, such as request or notification.</param>
	/// <param name="method">The JSON-RPC method name.</param>
	/// <param name="generation">The transport generation that owned the operation.</param>
	/// <param name="exception">The failure.</param>
	private void LogNonTransportOperationFailure(string operationKind, string method, long generation, Exception exception)
	{
		if (_isDisposed())
			return;

		if (generation != 0 && generation != _capabilityStore.TransportGeneration)
		{
			_logger.LogDebug(exception,
				"Language server {OperationKind} '{Method}' failed on stale transport generation {Generation} for workspace '{Workspace}' without invalidating the active session.",
				operationKind,
				method,
				generation,
				_workspaceRootsDisplayText);

			return;
		}

		_logger.LogWarning(exception,
			"Language server {OperationKind} '{Method}' failed on transport generation {Generation} for workspace '{Workspace}' without invalidating the active session.",
			operationKind,
			method,
			generation,
			_workspaceRootsDisplayText);
	}

	private static bool IsTransportOperationFailure(Exception exception)
		=> exception is IOException or ObjectDisposedException or ConnectionLostException;

	private CachedSettingsSnapshot GetCachedSettingsSnapshot()
	{
		// The provider is invoked under the lock so two concurrent configuration callbacks cannot both run it and
		// then race to store their results, discarding the loser's snapshot after its provider call had already
		// happened. The lock is reentrant, so a provider that re-enters this type on the same thread is safe.
		lock (_settingsSnapshotSyncRoot)
		{
			return _cachedSettingsSnapshot ??= CreateCachedSettingsSnapshotFromProvider();
		}
	}

	private CachedSettingsSnapshot RefreshCachedSettingsSnapshotFromProvider()
	{
		lock (_settingsSnapshotSyncRoot)
		{
			return _cachedSettingsSnapshot = CreateCachedSettingsSnapshotFromProvider();
		}
	}

	private CachedSettingsSnapshot CreateCachedSettingsSnapshotFromProvider()
	{
		object settingsPayload = _settingsProvider();
		ArgumentNullException.ThrowIfNull(settingsPayload);

		return CreateCachedSettingsSnapshot(settingsPayload);
	}

	/// <summary>
	/// Refreshes the cached settings snapshot from the host and returns the serialized settings element used by
	/// both the <c>workspace/didChangeConfiguration</c> push and the <c>workspace/configuration</c> callback.
	/// </summary>
	/// <returns>The serialized settings element.</returns>
	internal JsonElement RefreshSettingsSnapshotFromProviderElement()
		=> RefreshCachedSettingsSnapshotFromProvider().SettingsElement;

	private CachedSettingsSnapshot CacheSettingsSnapshot(object settingsPayload)
	{
		CachedSettingsSnapshot settingsSnapshot = CreateCachedSettingsSnapshot(settingsPayload);

		lock (_settingsSnapshotSyncRoot)
		{
			_cachedSettingsSnapshot = settingsSnapshot;
			return settingsSnapshot;
		}
	}

	private static CachedSettingsSnapshot CreateCachedSettingsSnapshot(object settingsPayload)
	{
		JsonElement settingsElement = settingsPayload is JsonElement jsonElement
			? jsonElement.Clone()
			: JsonSerializer.SerializeToElement(settingsPayload, LanguageServerClient.ProtocolSerializerOptions);

		return new(settingsElement);
	}

	/// <summary>
	/// Normalizes an outgoing <c>workspace/didChangeConfiguration</c> payload to the serialized settings element the
	/// cache and the <c>workspace/configuration</c> callback share, so the pushed notification and the callback
	/// answers use the same member casing.
	/// </summary>
	/// <param name="method">The JSON-RPC method name.</param>
	/// <param name="parameters">The notification payload supplied by the host.</param>
	/// <returns>The normalized notification payload, or the original payload for other methods or when normalization fails.</returns>
	private object? NormalizeDidChangeConfigurationParameters(string method, object? parameters)
	{
		if (parameters is null || !string.Equals(method, LspMethodNames.DidChangeConfiguration, StringComparison.Ordinal))
			return parameters;

		try
		{
			if (parameters is DidChangeConfigurationParams didChangeConfigurationParameters)
				return new DidChangeConfigurationParams(CreateCachedSettingsSnapshot(didChangeConfigurationParameters.Settings).SettingsElement);

			JsonElement payload = JsonSerializer.SerializeToElement(parameters, LanguageServerClient.ProtocolSerializerOptions);

			if (!JsonElementReadHelpers.TryGetProperty(payload, "settings", out JsonElement settingsElement))
				return parameters;

			return new DidChangeConfigurationParams(settingsElement);
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Failed to normalize an outgoing didChangeConfiguration notification; sending the payload as supplied.");
			return parameters;
		}
	}

	private void TryRefreshCachedSettingsSnapshotFromNotification(string method, object? parameters)
	{
		if (parameters is null || !string.Equals(method, LspMethodNames.DidChangeConfiguration, StringComparison.Ordinal))
			return;

		try
		{
			if (parameters is DidChangeConfigurationParams didChangeConfigurationParameters)
			{
				CacheSettingsSnapshot(didChangeConfigurationParameters.Settings);
				return;
			}

			JsonElement payload = JsonSerializer.SerializeToElement(parameters, LanguageServerClient.ProtocolSerializerOptions);

			if (!JsonElementReadHelpers.TryGetProperty(payload, "settings", out JsonElement settingsElement))
				return;

			CacheSettingsSnapshot(settingsElement);
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception,
				"Failed to refresh the cached workspace settings snapshot from an outgoing didChangeConfiguration notification.");
		}
	}

	/// <summary>
	/// Stores the serialized settings element shared by the <c>workspace/didChangeConfiguration</c> push and the
	/// <c>workspace/configuration</c> callback.
	/// </summary>
	private sealed record CachedSettingsSnapshot(JsonElement SettingsElement);
}
