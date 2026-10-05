using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

public sealed partial class LanguageServerClientTests
{
	[TestMethod]
	public void RegisterCapability_IgnoresDynamicRegistrationAndLogsWarning()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_defaultClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		object? result = rpcTarget.RegisterCapability(
			new CapabilityRegistrationParams([new CapabilityRegistrationPayload("1", "textDocument/rename")])
		);

		Assert.IsNull(result);

		Assert.Contains(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("client/registerCapability", StringComparison.Ordinal)
				&& log.Contains("generation 1", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("dynamicRegistration = false", StringComparison.Ordinal)
				&& log.Contains("textDocument/rename", StringComparison.Ordinal),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public void UnregisterCapability_IgnoresDynamicUnregistrationAndLogsWarning()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_defaultClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 2,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		object? result = rpcTarget.UnregisterCapability(
			new CapabilityUnregistrationParams([new CapabilityUnregistrationPayload("1", "textDocument/rename")])
		);

		Assert.IsNull(result);

		Assert.Contains(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("client/unregisterCapability", StringComparison.Ordinal)
				&& log.Contains("generation 2", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("dynamicRegistration = false", StringComparison.Ordinal)
				&& log.Contains("textDocument/rename", StringComparison.Ordinal),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public void RegisterCapability_StaleConnectionGeneration_LogsDebugWithoutWarning()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_defaultClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		ConnectionSession oldSession = CreateConnectionSession(
			client,
			generation: 3,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		ConnectionSession newSession = CreateConnectionSession(
			client,
			generation: 4,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, newSession);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(oldSession));

		object? result = rpcTarget.RegisterCapability(
			new CapabilityRegistrationParams([new CapabilityRegistrationPayload("1", "textDocument/rename")])
		);

		Assert.IsNull(result);

		Assert.Contains(
			log =>
				log.StartsWith("Debug|", StringComparison.Ordinal)
				&& log.Contains("client/registerCapability", StringComparison.Ordinal)
				&& log.Contains("generation 3", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("stale", StringComparison.OrdinalIgnoreCase),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);

		Assert.DoesNotContain(
			log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("client/registerCapability", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public void RegisterCapability_WhenConnectionGenerationIsUnhealthy_LogsDebugWithoutWarning()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_defaultClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 21,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);

		// The unhealthy transition keeps the generation number but stops accepting server messages, so the
		// generation must read as stale rather than as a live one.
		client.TryMarkConnectionUnhealthy(client.ConnectionGeneration);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		object? result = rpcTarget.RegisterCapability(
			new CapabilityRegistrationParams([new CapabilityRegistrationPayload("1", "textDocument/rename")])
		);

		Assert.IsNull(result);

		Assert.Contains(
			log =>
				log.StartsWith("Debug|", StringComparison.Ordinal)
				&& log.Contains("client/registerCapability", StringComparison.Ordinal)
				&& log.Contains("generation 21", StringComparison.OrdinalIgnoreCase),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);

		Assert.DoesNotContain(
			log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("client/registerCapability", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public void CreateWorkDoneProgress_LogsIgnoredServerMessageAtDebugLevel()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_defaultClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 6,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));
		object? result = rpcTarget.CreateWorkDoneProgress(JsonSerializer.SerializeToElement(new { token = "task-1" }));

		Assert.IsNull(result);

		Assert.Contains(
			log => log.StartsWith("Debug|", StringComparison.Ordinal) && log.Contains("window/workDoneProgress/create", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public void TelemetryEvent_LogsIgnoredServerMessageAtDebugLevel()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_defaultClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 7,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		rpcTarget.TelemetryEvent(JsonSerializer.SerializeToElement(new { eventName = "startup" }));

		Assert.Contains(
			log => log.StartsWith("Debug|", StringComparison.Ordinal) && log.Contains("telemetry/event", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public void Progress_LogsIgnoredServerMessageAtDebugLevel()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_defaultClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 8,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		rpcTarget.Progress(JsonSerializer.SerializeToElement(new { token = "task-1", value = new { kind = "report", message = "working" } }));

		Assert.Contains(
			log => log.StartsWith("Debug|", StringComparison.Ordinal) && log.Contains("$/progress", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public void Hello_LogsIgnoredUnsupportedServerMessageAtDebugLevel()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_defaultClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 20,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		rpcTarget.Hello(JsonSerializer.SerializeToElement(new { text = "hello" }));

		// The nonstandard startup notification is ignored, so the host must be able to see why in the log
		// without the message ever surfacing a result.
		Assert.Contains(
			log =>
				log.StartsWith("Debug|", StringComparison.Ordinal)
				&& log.Contains("$/hello", StringComparison.Ordinal)
				&& log.Contains("nonstandard startup notifications", StringComparison.Ordinal),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);

		Assert.DoesNotContain(
			log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("$/hello", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public void LogMessage_WritesTheServerMessageAtItsSeverityLevel()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_defaultClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 9,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		rpcTarget.LogMessage(new WindowMessageParams(MessageType.Error, "server exploded"));

		Assert.Contains(
			log =>
				log.StartsWith("Error|", StringComparison.Ordinal)
				&& log.Contains("[LS window/logMessage]", StringComparison.Ordinal)
				&& log.Contains("server exploded", StringComparison.Ordinal),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public void ShowMessage_WithMissingSeverity_LogsAtDebugLevel()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_defaultClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 10,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		rpcTarget.ShowMessage(new WindowMessageParams(Type: null, Message: "hello from the server"));

		Assert.Contains(
			log =>
				log.StartsWith("Debug|", StringComparison.Ordinal)
				&& log.Contains("[LS window/showMessage]", StringComparison.Ordinal)
				&& log.Contains("hello from the server", StringComparison.Ordinal),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public void RegisterCapability_WithNoRegistrations_ReturnsNullWithoutLogging()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_defaultClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 11,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(session));

		Assert.IsNull(rpcTarget.RegisterCapability(new CapabilityRegistrationParams([])));
		Assert.IsEmpty(logger.Logs, string.Join(Environment.NewLine, logger.Logs));
	}

	[TestMethod]
	public void UnregisterCapability_StaleConnectionGeneration_LogsDebugWithoutWarning()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_defaultClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		ConnectionSession oldSession = CreateConnectionSession(
			client,
			generation: 12,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		ConnectionSession newSession = CreateConnectionSession(
			client,
			generation: 13,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, newSession);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetConnectionGeneration(oldSession));

		object? result = rpcTarget.UnregisterCapability(
			new CapabilityUnregistrationParams([new CapabilityUnregistrationPayload("1", "textDocument/rename")])
		);

		Assert.IsNull(result);

		Assert.Contains(
			log =>
				log.StartsWith("Debug|", StringComparison.Ordinal)
				&& log.Contains("client/unregisterCapability", StringComparison.Ordinal)
				&& log.Contains("stale", StringComparison.OrdinalIgnoreCase),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);

		Assert.DoesNotContain(
			log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("client/unregisterCapability", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}
}
