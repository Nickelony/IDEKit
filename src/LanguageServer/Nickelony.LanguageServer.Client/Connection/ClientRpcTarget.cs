using System.Text.Json;
using Nickelony.LanguageServer.Client.Protocol;
using StreamJsonRpc;

namespace Nickelony.LanguageServer.Client;

/// <summary>Answers the requests and notifications that the language server sends over JSON-RPC.</summary>
internal sealed class ClientRpcTarget
{
	private readonly Func<long, bool> _canAcceptServerMessagesForGeneration;
	private readonly ServerMessageRouter _serverMessageRouter;
	private readonly SettingsSnapshotCache _settingsSnapshotCache;
	private readonly ILogger _logger;
	private readonly IReadOnlyList<WorkspaceFolder> _workspaceFolders;
	private readonly LanguageServerClientHooks _hooks;
	private readonly CancellationToken _lifetimeToken;
	private readonly long _connectionGeneration;

	/// <param name="context">
	/// The shared connection-collaborator context (the generation predicate, logger, workspace folders, and host hooks).
	/// </param>
	/// <param name="serverMessageRouter">Queues semantic-token and diagnostic refresh requests and diagnostics payloads.</param>
	/// <param name="settingsSnapshotCache">The cached settings snapshot that answers the configuration requests.</param>
	/// <param name="connectionGeneration">The connection generation associated with the RPC target.</param>
	internal ClientRpcTarget(
		ConnectionCollaboratorContext context,
		ServerMessageRouter serverMessageRouter,
		SettingsSnapshotCache settingsSnapshotCache,
		long connectionGeneration
	)
	{
		_canAcceptServerMessagesForGeneration = context.CanAcceptServerMessagesForGeneration;
		_serverMessageRouter = serverMessageRouter;
		_settingsSnapshotCache = settingsSnapshotCache;
		_logger = context.Logger;
		_workspaceFolders = context.WorkspaceFolders;
		_hooks = context.Hooks;
		_lifetimeToken = context.Lifetime.Token;
		_connectionGeneration = connectionGeneration;
	}

	/// <summary>Supplies configuration sections requested by the language server.</summary>
	/// <param name="parameters">The requested configuration sections.</param>
	/// <returns>The requested configuration elements.</returns>
	/// <remarks>
	/// The response is positional: the <c>workspace/configuration</c> result array must hold one entry per requested item, in the request's order.
	/// When the RPC target's connection generation does not accept server messages, the method still returns an array of <see langword="null"/>
	/// entries sized to the request rather than an empty array, so the server's positional read stays aligned. The cached settings snapshot
	/// is not consulted for a stale generation.
	/// </remarks>
	[JsonRpcMethod(LspMethodNames.Configuration, UseSingleObjectParameterDeserialization = true)]
	public JsonElement?[] WorkspaceConfiguration(WorkspaceConfigurationParams parameters)
	{
		if (!_canAcceptServerMessagesForGeneration(_connectionGeneration))
			return new JsonElement?[(parameters.Items ?? []).Length];

		return ConfigurationResponseBuilder.Build(parameters, _settingsSnapshotCache, _logger);
	}

	/// <summary>Returns the workspace folders advertised to the language server.</summary>
	/// <returns>The current workspace folder array.</returns>
	[JsonRpcMethod(LspMethodNames.WorkspaceFolders)]
	public WorkspaceFolder[] WorkspaceFolders()
	{
		if (!_canAcceptServerMessagesForGeneration(_connectionGeneration))
			return [];

		return [.. _workspaceFolders];
	}

	/// <summary>Acknowledges a semantic tokens refresh request and notifies the owner.</summary>
	/// <returns>A completed task that resolves to <see langword="null"/>.</returns>
	[JsonRpcMethod(LspMethodNames.SemanticTokensRefresh)]
	public Task<object?> RefreshSemanticTokensAsync()
	{
		if (!_canAcceptServerMessagesForGeneration(_connectionGeneration))
			return Task.FromResult<object?>(null);

		_serverMessageRouter.QueueSemanticTokensRefreshRequested();

		return Task.FromResult<object?>(null);
	}

	/// <summary>Acknowledges a pull-diagnostics refresh request and notifies the owner.</summary>
	/// <returns>A completed task that resolves to <see langword="null"/>.</returns>
	[JsonRpcMethod(LspMethodNames.DiagnosticRefresh)]
	public Task<object?> RefreshDiagnosticsAsync()
	{
		if (!_canAcceptServerMessagesForGeneration(_connectionGeneration))
			return Task.FromResult<object?>(null);

		_serverMessageRouter.QueueDiagnosticRefreshRequested();

		return Task.FromResult<object?>(null);
	}

	/// <summary>Ignores dynamic capability registration because the client advertises it as unsupported.</summary>
	/// <param name="parameters">The capability registration payload.</param>
	/// <returns><see langword="null"/>.</returns>
	[JsonRpcMethod(LspMethodNames.RegisterCapability, UseSingleObjectParameterDeserialization = true)]
	public object? RegisterCapability(CapabilityRegistrationParams parameters)
	{
		if (parameters.Registrations is null || parameters.Registrations.Length == 0)
			return null;

		return LogIgnoredUnsupportedDynamicCapability(LspMethodNames.RegisterCapability, parameters.Registrations, DescribeCapabilityRegistrations);
	}

	/// <summary>Ignores dynamic capability unregistration because the client advertises it as unsupported.</summary>
	/// <param name="parameters">The capability unregistration payload.</param>
	/// <returns><see langword="null"/>.</returns>
	[JsonRpcMethod(LspMethodNames.UnregisterCapability, UseSingleObjectParameterDeserialization = true)]
	public object? UnregisterCapability(CapabilityUnregistrationParams parameters)
	{
		if (parameters.Unregistrations is null || parameters.Unregistrations.Length == 0)
			return null;

		return LogIgnoredUnsupportedDynamicCapability(
			LspMethodNames.UnregisterCapability,
			parameters.Unregistrations,
			DescribeCapabilityUnregistrations
		);
	}

	/// <summary>Logs and ignores a dynamic capability request that this client deliberately does not support.</summary>
	/// <typeparam name="TPayload">The capability payload type.</typeparam>
	/// <param name="method">The JSON-RPC method name.</param>
	/// <param name="payloads">The requested capability payloads.</param>
	/// <param name="describeCapabilities">Formats the requested capability names on demand.</param>
	/// <returns><see langword="null"/>.</returns>
	/// <remarks>
	/// The requested capability names are formatted only when the matching message is enabled, so the diagnostic string is not built for a host
	/// that does not log it.
	/// </remarks>
	private object? LogIgnoredUnsupportedDynamicCapability<TPayload>(
		string method,
		TPayload[] payloads,
		Func<TPayload[], string> describeCapabilities
	)
	{
		if (!_canAcceptServerMessagesForGeneration(_connectionGeneration))
		{
			if (_logger.IsEnabled(LogLevel.Debug))
			{
				_logger.LogDebug(
					"Ignoring unsupported dynamic capability request '{Method}' from stale language server connection generation {Generation}. Requested capabilities: {Capabilities}",
					method,
					_connectionGeneration,
					describeCapabilities(payloads)
				);
			}

			return null;
		}

		if (_logger.IsEnabled(LogLevel.Warning))
		{
			_logger.LogWarning(
				"Ignoring unsupported dynamic capability request '{Method}' on connection generation {Generation} because the client advertises dynamicRegistration = false. Requested capabilities: {Capabilities}",
				method,
				_connectionGeneration,
				describeCapabilities(payloads)
			);
		}

		return null;
	}

	/// <summary>Formats one capability-registration payload array for diagnostic logging.</summary>
	/// <param name="registrations">The capability registrations to describe.</param>
	/// <returns>The comma-separated method list.</returns>
	private static string DescribeCapabilityRegistrations(CapabilityRegistrationPayload[] registrations)
	{
		return string.Join(
			", ",
			Array.ConvertAll(registrations, static registration => string.IsNullOrWhiteSpace(registration.Method) ? "<unknown>" : registration.Method)
		);
	}

	/// <summary>Formats one capability-unregistration payload array for diagnostic logging.</summary>
	/// <param name="unregistrations">The capability unregistrations to describe.</param>
	/// <returns>The comma-separated method list.</returns>
	private static string DescribeCapabilityUnregistrations(CapabilityUnregistrationPayload[] unregistrations)
	{
		return string.Join(
			", ",

			Array.ConvertAll(
				unregistrations,
				static unregistration => string.IsNullOrWhiteSpace(unregistration.Method) ? "<unknown>" : unregistration.Method
			)
		);
	}

	/// <summary>Acknowledges a work-done progress creation request.</summary>
	/// <param name="parameters">The protocol payload, whose token the later <c>$/progress</c> notifications repeat.</param>
	/// <returns><see langword="null"/>.</returns>
	/// <remarks>
	/// The reports reach the progress hook as notifications, so the acknowledgment keeps no state. A client without the hook logs the request
	/// as ignored.
	/// </remarks>
	[JsonRpcMethod(LspMethodNames.WorkDoneProgressCreate, UseSingleObjectParameterDeserialization = true)]
	public object? CreateWorkDoneProgress(JsonElement parameters)
	{
		if (_hooks.Progress is null)
			LogIgnoredUnsupportedServerMessage(LspMethodNames.WorkDoneProgressCreate, "this client has no progress hook");

		return null;
	}

	/// <summary>Queues diagnostics published by the language server.</summary>
	/// <param name="parameters">The diagnostics notification payload.</param>
	/// <remarks>
	/// A <see langword="null"/> payload is a defensive guard, because the tolerant message
	/// handler drops a frame whose <c>params</c> is neither an array nor an object before dispatch. A <see langword="null"/> payload and a degraded
	/// payload (see <see cref="PublishDiagnosticsParams.IsDegraded"/>) are dropped with a log instead of being queued, because consumers treat
	/// an empty diagnostics list as "clear this document" and a malformed payload must not clear the diagnostics already stored for the document.
	/// </remarks>
	[JsonRpcMethod(LspMethodNames.PublishDiagnostics, UseSingleObjectParameterDeserialization = true)]
	public void PublishDiagnostics(PublishDiagnosticsParams parameters)
	{
		if (parameters is null)
		{
			_logger.LogDebug(
				"Dropping a textDocument/publishDiagnostics notification with a null payload on connection generation {Generation}; stored diagnostics are left unchanged.",
				_connectionGeneration
			);

			return;
		}

		if (!_canAcceptServerMessagesForGeneration(_connectionGeneration))
			return;

		if (parameters.IsDegraded)
		{
			_logger.LogWarning(
				"Dropping malformed textDocument/publishDiagnostics notification for URI '{Uri}' on connection generation {Generation}; stored diagnostics are left unchanged.",
				parameters.Uri ?? "<none>",
				_connectionGeneration
			);

			return;
		}

		_serverMessageRouter.QueueDiagnosticsPublished(_connectionGeneration, parameters);
	}

	/// <summary>Logs a non-modal server message through the host logger.</summary>
	/// <param name="parameters">The window message payload.</param>
	[JsonRpcMethod(LspMethodNames.LogMessage, UseSingleObjectParameterDeserialization = true)]
	public void LogMessage(WindowMessageParams parameters) => LogServerMessage(LspMethodNames.LogMessage, parameters);

	/// <summary>Hands a server message to the show-message hook, or logs it through the host logger when the host set none.</summary>
	/// <param name="parameters">The window message payload.</param>
	/// <remarks>
	/// A message without text is not surfaced, and a message from a connection generation that does not accept server messages is dropped.
	/// A hook that throws is logged, and the message is not logged in its place.
	/// </remarks>
	[JsonRpcMethod(LspMethodNames.ShowMessage, UseSingleObjectParameterDeserialization = true)]
	public void ShowMessage(WindowMessageParams parameters)
	{
		if (_hooks.ShowMessage is not { } hook)
		{
			LogServerMessage(LspMethodNames.ShowMessage, parameters);
			return;
		}

		if (string.IsNullOrWhiteSpace(parameters.Message) || !CanAcceptMessage(LspMethodNames.ShowMessage))
			return;

		try
		{
			hook(parameters);
		}
		catch (Exception exception)
		{
			LogHookFailure(LspMethodNames.ShowMessage, exception);
		}
	}

	/// <summary>
	/// Asks the host to choose one of the actions of a server message, or logs the message and answers <see langword="null"/> when the host set
	/// no hook.
	/// </summary>
	/// <param name="parameters">The message and its actions.</param>
	/// <param name="cancellationToken">A token that the server's cancellation of the request cancels.</param>
	/// <returns>
	/// The action the hook chose, or <see langword="null"/> when the user chose none, the hook chose an action the server did not offer,
	/// the hook is unset or failed, or the request was canceled.
	/// </returns>
	[JsonRpcMethod(LspMethodNames.ShowMessageRequest, UseSingleObjectParameterDeserialization = true)]
	public async Task<MessageActionItemPayload?> ShowMessageRequestAsync(ShowMessageRequestParams parameters, CancellationToken cancellationToken)
	{
		if (_hooks.ShowMessageRequest is not { } hook)
		{
			LogServerMessage(LspMethodNames.ShowMessageRequest, new WindowMessageParams(parameters.Type, parameters.Message));
			return null;
		}

		if (!CanAcceptMessage(LspMethodNames.ShowMessageRequest))
			return null;

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);

		try
		{
			MessageActionItemPayload? chosen = await hook(parameters, linked.Token).ConfigureAwait(false);

			if (chosen is not { } answer)
				return null;

			// The server accepts only one of the actions it offered, so an answer that names none of them is no choice.
			if (parameters.Actions?.Any(action => string.Equals(action.Title, answer.Title, StringComparison.Ordinal)) == true)
				return answer;

			_logger.LogWarning(
				"The host hook for '{Method}' chose '{Title}', which the server did not offer; answering that the user chose none.",
				LspMethodNames.ShowMessageRequest,
				answer.Title
			);

			return null;
		}
		catch (OperationCanceledException) when (linked.IsCancellationRequested)
		{
			return null;
		}
		catch (Exception exception)
		{
			LogHookFailure(LspMethodNames.ShowMessageRequest, exception);
			return null;
		}
	}

	/// <summary>Asks the host to apply an edit that the server sent, or answers that the client did not apply it when the host set no hook.</summary>
	/// <param name="parameters">The edit and its label.</param>
	/// <param name="cancellationToken">A token that the server's cancellation of the request cancels.</param>
	/// <returns>
	/// The answer of the hook; an answer that did not apply the edit, with a fixed reason, when the hook is unset, failed or was canceled.
	/// </returns>
	[JsonRpcMethod(LspMethodNames.ApplyEdit, UseSingleObjectParameterDeserialization = true)]
	public async Task<ApplyWorkspaceEditResponse> ApplyWorkspaceEditAsync(ApplyWorkspaceEditParams parameters, CancellationToken cancellationToken)
	{
		if (_hooks.ApplyWorkspaceEdit is not { } hook)
			return new ApplyWorkspaceEditResponse(Applied: false, "The client applies no workspace edits.");

		if (!CanAcceptMessage(LspMethodNames.ApplyEdit))
			return new ApplyWorkspaceEditResponse(Applied: false, "The connection that sent the edit ended before the edit was applied.");

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);

		try
		{
			return await hook(parameters, linked.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (linked.IsCancellationRequested)
		{
			return new ApplyWorkspaceEditResponse(Applied: false, "The edit was canceled before it was applied.");
		}
		catch (Exception exception)
		{
			// The reason goes to the server, so it names no detail of the host's fault; the log keeps the exception.
			LogHookFailure(LspMethodNames.ApplyEdit, exception);
			return new ApplyWorkspaceEditResponse(Applied: false, "The client failed to apply the edit.");
		}
	}

	/// <summary>Ignores telemetry events that the host does not surface.</summary>
	/// <param name="parameters">The protocol payload ignored by the host.</param>
	[JsonRpcMethod(LspMethodNames.TelemetryEvent, UseSingleObjectParameterDeserialization = true)]
	public void TelemetryEvent(JsonElement parameters) =>
		LogIgnoredUnsupportedServerMessage(LspMethodNames.TelemetryEvent, "this client does not surface server telemetry events");

	/// <summary>Hands a work-done progress report to the progress hook, and ignores any other progress notification.</summary>
	/// <param name="parameters">The protocol payload.</param>
	/// <remarks>
	/// A notification whose value is not a <c>begin</c>, <c>report</c> or <c>end</c> report, such as a partial result, is logged at debug level
	/// and not surfaced. A hook that throws is logged.
	/// </remarks>
	[JsonRpcMethod(LspMethodNames.Progress, UseSingleObjectParameterDeserialization = true)]
	public void Progress(JsonElement parameters)
	{
		if (_hooks.Progress is not { } hook)
		{
			LogIgnoredUnsupportedServerMessage(LspMethodNames.Progress, "this client has no progress hook");
			return;
		}

		if (!CanAcceptMessage(LspMethodNames.Progress))
			return;

		if (!ProgressParamsParser.TryParse(parameters, out ProgressParams? progress))
		{
			LogIgnoredUnsupportedServerMessage(LspMethodNames.Progress, "the notification carries no work-done progress report");
			return;
		}

		try
		{
			hook(progress);
		}
		catch (Exception exception)
		{
			LogHookFailure(LspMethodNames.Progress, exception);
		}
	}

	/// <summary>Ignores nonstandard hello notifications that some language servers emit during startup.</summary>
	/// <param name="parameters">The protocol payload ignored by the host.</param>
	[JsonRpcMethod(LspMethodNames.Hello, UseSingleObjectParameterDeserialization = true)]
	public void Hello(JsonElement parameters) =>
		LogIgnoredUnsupportedServerMessage(LspMethodNames.Hello, "this client does not surface nonstandard startup notifications");

	/// <summary>
	/// Reports whether the connection generation of this target still accepts server messages, and logs a drop at debug level when it does not.
	/// </summary>
	/// <param name="method">The method name of the message.</param>
	private bool CanAcceptMessage(string method)
	{
		if (_canAcceptServerMessagesForGeneration(_connectionGeneration))
			return true;

		_logger.LogDebug(
			"Dropping server message '{Method}' from stale language server connection generation {Generation}.",
			method,
			_connectionGeneration
		);

		return false;
	}

	/// <summary>Logs a host hook that threw, so its fault never reaches the connection that called it.</summary>
	/// <param name="method">The method name of the message the hook handled.</param>
	/// <param name="exception">The fault of the hook.</param>
	private void LogHookFailure(string method, Exception exception) =>
		_logger.LogWarning(exception, "The host hook for '{Method}' threw on connection generation {Generation}.", method, _connectionGeneration);

	/// <summary>Logs one unsupported server message without surfacing it to the host.</summary>
	/// <param name="method">The method name of the message.</param>
	/// <param name="reason">Why the message is ignored.</param>
	private void LogIgnoredUnsupportedServerMessage(string method, string reason)
	{
		if (!_canAcceptServerMessagesForGeneration(_connectionGeneration))
		{
			_logger.LogDebug(
				"Ignoring unsupported server message '{Method}' from stale language server connection generation {Generation}. Reason: {Reason}",
				method,
				_connectionGeneration,
				reason
			);

			return;
		}

		_logger.LogDebug(
			"Ignoring unsupported server message '{Method}' on connection generation {Generation}. Reason: {Reason}",
			method,
			_connectionGeneration,
			reason
		);
	}

	/// <summary>Logs a window message or log message notification from the language server.</summary>
	/// <param name="method">The originating method name.</param>
	/// <param name="parameters">The message payload.</param>
	private void LogServerMessage(string method, WindowMessageParams parameters)
	{
		string? messageText = parameters.Message;

		if (string.IsNullOrWhiteSpace(messageText))
			return;

		// A missing severity is a log message (the least intrusive protocol level); an unknown numeric
		// value falls through to the same case.
		MessageType messageType = parameters.Type ?? MessageType.Log;

		switch (messageType)
		{
			case MessageType.Error:
				_logger.LogError("[LS {Method}] {Message}", method, messageText);
				break;

			case MessageType.Warning:
				_logger.LogWarning("[LS {Method}] {Message}", method, messageText);
				break;

			case MessageType.Information:
				_logger.LogInformation("[LS {Method}] {Message}", method, messageText);
				break;

			default:
				_logger.LogDebug("[LS {Method}] {Message}", method, messageText);
				break;
		}
	}
}
