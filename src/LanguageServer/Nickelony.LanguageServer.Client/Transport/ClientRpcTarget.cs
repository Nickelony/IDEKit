using StreamJsonRpc;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Exposes the host callbacks required by the language server over JSON-RPC.
/// </summary>
internal sealed class ClientRpcTarget
{
	private readonly CapabilityStore _capabilityStore;
	private readonly DiagnosticsRouter _diagnosticsRouter;
	private readonly ProtocolForwarder _protocolForwarder;
	private readonly ILogger _logger;
	private readonly IReadOnlyList<WorkspaceFolder> _workspaceFolders;
	private readonly long _transportGeneration;

	/// <summary>
	/// Initializes a new instance of the <see cref="ClientRpcTarget"/> class.
	/// </summary>
	/// <param name="capabilityStore">Provides session lookup and transport-generation fencing.</param>
	/// <param name="diagnosticsRouter">Queues semantic-token and diagnostic refresh callbacks and diagnostics payloads.</param>
	/// <param name="protocolForwarder">Builds configuration responses from the cached settings snapshot.</param>
	/// <param name="transportGeneration">The transport generation associated with the callback target.</param>
	/// <param name="workspaceFolders">The workspace folder descriptors advertised to the language server.</param>
	/// <param name="logger">The logger instance for server callback diagnostics.</param>
	internal ClientRpcTarget(CapabilityStore capabilityStore,
		DiagnosticsRouter diagnosticsRouter, ProtocolForwarder protocolForwarder,
		long transportGeneration, IReadOnlyList<WorkspaceFolder> workspaceFolders, ILogger logger)
	{
		_capabilityStore = capabilityStore;
		_diagnosticsRouter = diagnosticsRouter;
		_protocolForwarder = protocolForwarder;
		_logger = logger;
		_workspaceFolders = workspaceFolders;
		_transportGeneration = transportGeneration;
	}

	/// <summary>
	/// Supplies configuration sections requested by the language server.
	/// </summary>
	/// <remarks>
	/// The response is positional: the <c>workspace/configuration</c> result array must hold one entry per requested
	/// item, in the request's order. When the callback target's transport generation no longer accepts server
	/// callbacks, the method still returns an array of <see langword="null"/> entries sized to the request rather than
	/// an empty array, so the server's positional read stays aligned; the cached settings snapshot is not consulted
	/// for a stale generation.
	/// </remarks>
	/// <param name="parameters">The requested configuration sections.</param>
	/// <returns>The requested configuration objects.</returns>
	[JsonRpcMethod(LspMethodNames.Configuration, UseSingleObjectParameterDeserialization = true)]
	public object?[] WorkspaceConfiguration(WorkspaceConfigurationParams parameters)
	{
		if (!_capabilityStore.CanAcceptServerCallbacksForGeneration(_transportGeneration))
			return new object?[(parameters.Items ?? []).Length];

		return _protocolForwarder.BuildConfigurationResponse(parameters);
	}

	/// <summary>
	/// Returns the workspace folders advertised to the language server.
	/// </summary>
	/// <returns>The current workspace folder array.</returns>
	[JsonRpcMethod(LspMethodNames.WorkspaceFolders)]
	public WorkspaceFolder[] WorkspaceFolders()
	{
		if (!_capabilityStore.CanAcceptServerCallbacksForGeneration(_transportGeneration))
			return [];

		return [.. _workspaceFolders];
	}

	/// <summary>
	/// Acknowledges a semantic tokens refresh request and notifies the owner.
	/// </summary>
	/// <returns>A completed task that resolves to <see langword="null"/>.</returns>
	[JsonRpcMethod(LspMethodNames.SemanticTokensRefresh)]
	public Task<object?> RefreshSemanticTokensAsync()
	{
		if (!_capabilityStore.CanAcceptServerCallbacksForGeneration(_transportGeneration))
			return Task.FromResult<object?>(null);

		_diagnosticsRouter.QueueSemanticTokensRefreshRequested();

		return Task.FromResult<object?>(null);
	}

	/// <summary>
	/// Acknowledges a pull-diagnostics refresh request and notifies the owner.
	/// </summary>
	/// <returns>A completed task that resolves to <see langword="null"/>.</returns>
	[JsonRpcMethod(LspMethodNames.DiagnosticRefresh)]
	public Task<object?> RefreshDiagnosticsAsync()
	{
		if (!_capabilityStore.CanAcceptServerCallbacksForGeneration(_transportGeneration))
			return Task.FromResult<object?>(null);

		_diagnosticsRouter.QueueDiagnosticRefreshRequested();

		return Task.FromResult<object?>(null);
	}

	/// <summary>
	/// Ignores dynamic capability registration because the client advertises it as unsupported.
	/// </summary>
	/// <param name="parameters">The capability registration payload.</param>
	/// <returns><see langword="null"/>.</returns>
	[JsonRpcMethod(LspMethodNames.RegisterCapability, UseSingleObjectParameterDeserialization = true)]
	public object? RegisterCapability(CapabilityRegistrationParams parameters)
	{
		if (parameters.Registrations is null || parameters.Registrations.Length == 0)
			return null;

		return IgnoreUnsupportedDynamicCapability(
			LspMethodNames.RegisterCapability,
			parameters.Registrations,
			DescribeCapabilityRegistrations);
	}

	/// <summary>
	/// Ignores dynamic capability unregistration because the client advertises it as unsupported.
	/// </summary>
	/// <param name="parameters">The capability unregistration payload.</param>
	/// <returns><see langword="null"/>.</returns>
	[JsonRpcMethod(LspMethodNames.UnregisterCapability, UseSingleObjectParameterDeserialization = true)]
	public object? UnregisterCapability(CapabilityUnregistrationParams parameters)
	{
		if (parameters.Unregistrations is null || parameters.Unregistrations.Length == 0)
			return null;

		return IgnoreUnsupportedDynamicCapability(
			LspMethodNames.UnregisterCapability,
			parameters.Unregistrations,
			DescribeCapabilityUnregistrations);
	}

	/// <summary>
	/// Logs and ignores a dynamic capability request that this client deliberately does not support.
	/// </summary>
	/// <remarks>
	/// The requested capability names are formatted only when the matching message is enabled, so the diagnostic
	/// string is not built for a host that does not log it.
	/// </remarks>
	/// <typeparam name="TPayload">The capability payload type.</typeparam>
	/// <param name="method">The JSON-RPC method name.</param>
	/// <param name="payloads">The requested capability payloads.</param>
	/// <param name="describeCapabilities">Formats the requested capability names on demand.</param>
	/// <returns><see langword="null"/>.</returns>
	private object? IgnoreUnsupportedDynamicCapability<TPayload>(
		string method,
		TPayload[] payloads,
		Func<TPayload[], string> describeCapabilities)
	{
		if (!_capabilityStore.IsCurrentTransportGeneration(_transportGeneration))
		{
			if (_logger.IsEnabled(LogLevel.Debug))
			{
				_logger.LogDebug(
					"Ignoring unsupported dynamic capability request '{Method}' from stale language server transport generation {Generation}. Requested capabilities: {Capabilities}",
					method,
					_transportGeneration,
					describeCapabilities(payloads));
			}

			return null;
		}

		if (_logger.IsEnabled(LogLevel.Warning))
		{
			_logger.LogWarning(
				"Ignoring unsupported dynamic capability request '{Method}' on transport generation {Generation} because the client advertises dynamicRegistration = false. Requested capabilities: {Capabilities}",
				method,
				_transportGeneration,
				describeCapabilities(payloads));
		}

		return null;
	}

	/// <summary>
	/// Formats one capability-registration payload array for diagnostic logging.
	/// </summary>
	/// <param name="registrations">The capability registrations to describe.</param>
	/// <returns>The comma-separated method list.</returns>
	private static string DescribeCapabilityRegistrations(CapabilityRegistrationPayload[] registrations)
	{
		return string.Join(", ",
			Array.ConvertAll(registrations, static registration =>
				string.IsNullOrWhiteSpace(registration.Method) ? "<unknown>" : registration.Method));
	}

	/// <summary>
	/// Formats one capability-unregistration payload array for diagnostic logging.
	/// </summary>
	/// <param name="unregistrations">The capability unregistrations to describe.</param>
	/// <returns>The comma-separated method list.</returns>
	private static string DescribeCapabilityUnregistrations(CapabilityUnregistrationPayload[] unregistrations)
	{
		return string.Join(", ",
			Array.ConvertAll(unregistrations, static unregistration =>
				string.IsNullOrWhiteSpace(unregistration.Method) ? "<unknown>" : unregistration.Method));
	}

	/// <summary>
	/// Acknowledges work-done progress creation requests without creating a client-side progress sink.
	/// </summary>
	/// <param name="parameters">The protocol payload ignored by the host.</param>
	/// <returns><see langword="null"/>.</returns>
	[JsonRpcMethod(LspMethodNames.WorkDoneProgressCreate, UseSingleObjectParameterDeserialization = true)]
	public object? CreateWorkDoneProgress(JsonElement parameters)
	{
		LogIgnoredUnsupportedCallback(LspMethodNames.WorkDoneProgressCreate, "this client does not expose a client-side progress sink");
		return null;
	}

	/// <summary>
	/// Queues diagnostics published by the language server.
	/// </summary>
	/// <remarks>
	/// A null payload (a server frame with <c>params: null</c>, which the single-object deserialization passes
	/// through as <see langword="null"/>) and a degraded payload (see
	/// <see cref="PublishDiagnosticsParams.IsDegraded"/>) are dropped with a log instead of being queued, because
	/// consumers treat an empty diagnostics list as "clear this document" and a malformed payload must not erase
	/// previously published diagnostics.
	/// </remarks>
	/// <param name="parameters">The diagnostics notification payload.</param>
	[JsonRpcMethod(LspMethodNames.PublishDiagnostics, UseSingleObjectParameterDeserialization = true)]
	public void PublishDiagnostics(PublishDiagnosticsParams parameters)
	{
		if (parameters is null)
		{
			_logger.LogDebug(
				"Dropping a textDocument/publishDiagnostics notification with a null payload on transport generation {Generation}; stored diagnostics are left unchanged.",
				_transportGeneration);

			return;
		}

		if (!_capabilityStore.CanAcceptServerCallbacksForGeneration(_transportGeneration))
			return;

		if (parameters.IsDegraded)
		{
			_logger.LogWarning(
				"Dropping malformed textDocument/publishDiagnostics notification for URI '{Uri}' on transport generation {Generation}; stored diagnostics are left unchanged.",
				parameters.Uri ?? "<none>",
				_transportGeneration);

			return;
		}

		_diagnosticsRouter.QueueDiagnosticsPublished(_transportGeneration, parameters);
	}

	/// <summary>
	/// Logs a non-modal server message through the host logger.
	/// </summary>
	/// <param name="parameters">The window message payload.</param>
	[JsonRpcMethod(LspMethodNames.LogMessage, UseSingleObjectParameterDeserialization = true)]
	public void LogMessage(WindowMessageParams parameters)
		=> LogServerMessage(LspMethodNames.LogMessage, parameters);

	/// <summary>
	/// Logs a server message through the host logger.
	/// </summary>
	/// <param name="parameters">The window message payload.</param>
	[JsonRpcMethod(LspMethodNames.ShowMessage, UseSingleObjectParameterDeserialization = true)]
	public void ShowMessage(WindowMessageParams parameters)
		=> LogServerMessage(LspMethodNames.ShowMessage, parameters);

	/// <summary>
	/// Ignores telemetry events that the host does not surface.
	/// </summary>
	/// <param name="parameters">The protocol payload ignored by the host.</param>
	[JsonRpcMethod(LspMethodNames.TelemetryEvent, UseSingleObjectParameterDeserialization = true)]
	public void TelemetryEvent(JsonElement parameters)
		=> LogIgnoredUnsupportedCallback(LspMethodNames.TelemetryEvent, "this client does not surface server telemetry events");

	/// <summary>
	/// Ignores generic progress notifications that the host does not surface.
	/// </summary>
	/// <param name="parameters">The protocol payload ignored by the host.</param>
	[JsonRpcMethod(LspMethodNames.Progress, UseSingleObjectParameterDeserialization = true)]
	public void Progress(JsonElement parameters)
		=> LogIgnoredUnsupportedCallback(LspMethodNames.Progress, "this client does not surface generic progress notifications");

	/// <summary>
	/// Ignores nonstandard hello notifications that some language servers emit during startup.
	/// </summary>
	/// <param name="parameters">The protocol payload ignored by the host.</param>
	[JsonRpcMethod(LspMethodNames.Hello, UseSingleObjectParameterDeserialization = true)]
	public void Hello(JsonElement parameters)
		=> LogIgnoredUnsupportedCallback(LspMethodNames.Hello, "this client does not surface nonstandard startup notifications");

	/// <summary>
	/// Logs one unsupported server callback without surfacing it to the host.
	/// </summary>
	/// <param name="method">The callback method name.</param>
	/// <param name="reason">Why the callback is ignored.</param>
	private void LogIgnoredUnsupportedCallback(string method, string reason)
	{
		if (!_capabilityStore.IsCurrentTransportGeneration(_transportGeneration))
		{
			_logger.LogDebug(
				"Ignoring unsupported server callback '{Method}' from stale language server transport generation {Generation}. Reason: {Reason}",
				method,
				_transportGeneration,
				reason);

			return;
		}

		_logger.LogDebug(
			"Ignoring unsupported server callback '{Method}' on transport generation {Generation}. Reason: {Reason}",
			method,
			_transportGeneration,
			reason);
	}

	/// <summary>
	/// Logs a window message or log message notification from the language server.
	/// </summary>
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
