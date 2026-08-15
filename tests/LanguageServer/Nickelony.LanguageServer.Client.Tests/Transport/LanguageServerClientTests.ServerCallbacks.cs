using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

public partial class LanguageServerClientTests
{
	[TestMethod]
	public void CapabilityRegistrationParams_Deserialize_ReadsRegistrationsProperty()
	{
		CapabilityRegistrationParams parameters = JsonSerializer.Deserialize<CapabilityRegistrationParams>(
			"""
			{
			  "registrations": [
			    { "id": "1", "method": "textDocument/rename" }
			  ]
			}
			""");

		Assert.IsNotNull(parameters.Registrations);
		Assert.AreEqual(1, parameters.Registrations.Length);
		Assert.AreEqual("1", parameters.Registrations[0].Id);
		Assert.AreEqual("textDocument/rename", parameters.Registrations[0].Method);
	}

	[TestMethod]
	public void CapabilityUnregistrationParams_Deserialize_ReadsHistoricalProperty()
	{
		CapabilityUnregistrationParams parameters = JsonSerializer.Deserialize<CapabilityUnregistrationParams>(
			"""
			{
			  "unregisterations": [
			    { "id": "1", "method": "textDocument/rename" }
			  ]
			}
			""");

		Assert.IsNotNull(parameters.Unregistrations);
		Assert.AreEqual(1, parameters.Unregistrations.Length);
		Assert.AreEqual("1", parameters.Unregistrations[0].Id);
		Assert.AreEqual("textDocument/rename", parameters.Unregistrations[0].Method);
	}

	[TestMethod]
	public void CapabilityUnregistrationParams_Deserialize_ReadsCorrectedPropertyName()
	{
		CapabilityUnregistrationParams parameters = JsonSerializer.Deserialize<CapabilityUnregistrationParams>(
			"""
			{
			  "unregistrations": [
			    { "id": "1", "method": "textDocument/rename" }
			  ]
			}
			""");

		Assert.IsNotNull(parameters.Unregistrations);
		Assert.AreEqual(1, parameters.Unregistrations.Length);
		Assert.AreEqual("1", parameters.Unregistrations[0].Id);
		Assert.AreEqual("textDocument/rename", parameters.Unregistrations[0].Method);
	}

	[TestMethod]
	public void CapabilityUnregistrationParams_Serialize_WritesHistoricalPropertyName()
	{
		string json = JsonSerializer.Serialize(
			new CapabilityUnregistrationParams(
			[
				new CapabilityUnregistrationPayload("1", "textDocument/rename")
			]));

		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;

		Assert.IsTrue(root.TryGetProperty("unregisterations", out JsonElement unregistrations));
		Assert.IsFalse(root.TryGetProperty("unregistrations", out _));
		Assert.AreEqual(JsonValueKind.Array, unregistrations.ValueKind);
		Assert.AreEqual(1, unregistrations.GetArrayLength());
	}

	[TestMethod]
	public void RegisterCapability_IgnoresDynamicRegistrationAndLogsWarning()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		TransportSession session = CreateTransportSession(client, 1, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetTransportGeneration(session));
		object? result = rpcTarget.RegisterCapability(
			new CapabilityRegistrationParams(
			[
				new CapabilityRegistrationPayload("1", "textDocument/rename")
			]));

		Assert.IsNull(result);

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("client/registerCapability", StringComparison.Ordinal)
			&& log.Contains("generation 1", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("dynamicRegistration = false", StringComparison.Ordinal)
			&& log.Contains("textDocument/rename", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void UnregisterCapability_IgnoresDynamicUnregistrationAndLogsWarning()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		TransportSession session = CreateTransportSession(client, 2, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetTransportGeneration(session));
		object? result = rpcTarget.UnregisterCapability(
			new CapabilityUnregistrationParams(
			[
				new CapabilityUnregistrationPayload("1", "textDocument/rename")
			]));

		Assert.IsNull(result);

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("client/unregisterCapability", StringComparison.Ordinal)
			&& log.Contains("generation 2", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("dynamicRegistration = false", StringComparison.Ordinal)
			&& log.Contains("textDocument/rename", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void RegisterCapability_StaleTransportGeneration_LogsDebugWithoutWarning()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		TransportSession oldSession = CreateTransportSession(client, 3, process: null, Stream.Null, Stream.Null);
		TransportSession newSession = CreateTransportSession(client, 4, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, newSession);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetTransportGeneration(oldSession));
		object? result = rpcTarget.RegisterCapability(
			new CapabilityRegistrationParams(
			[
				new CapabilityRegistrationPayload("1", "textDocument/rename")
			]));

		Assert.IsNull(result);

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Debug|", StringComparison.Ordinal)
			&& log.Contains("client/registerCapability", StringComparison.Ordinal)
			&& log.Contains("generation 3", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("stale", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));

		Assert.IsFalse(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("client/registerCapability", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void CreateWorkDoneProgress_LogsIgnoredCallbackAtDebugLevel()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		TransportSession session = CreateTransportSession(client, 6, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetTransportGeneration(session));
		object? result = rpcTarget.CreateWorkDoneProgress(JsonSerializer.SerializeToElement(new { token = "task-1" }));

		Assert.IsNull(result);
		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Debug|", StringComparison.Ordinal)
			&& log.Contains("window/workDoneProgress/create", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void TelemetryEvent_LogsIgnoredCallbackAtDebugLevel()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		TransportSession session = CreateTransportSession(client, 7, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetTransportGeneration(session));

		rpcTarget.TelemetryEvent(JsonSerializer.SerializeToElement(new { eventName = "startup" }));

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Debug|", StringComparison.Ordinal)
			&& log.Contains("telemetry/event", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void Progress_LogsIgnoredCallbackAtDebugLevel()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		TransportSession session = CreateTransportSession(client, 8, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetTransportGeneration(session));

		rpcTarget.Progress(JsonSerializer.SerializeToElement(new { token = "task-1", value = new { kind = "report", message = "working" } }));

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Debug|", StringComparison.Ordinal)
			&& log.Contains("$/progress", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void Hello_LogsIgnoredUnsupportedCallbackAtDebugLevel()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		TransportSession session = CreateTransportSession(client, 20, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetTransportGeneration(session));

		rpcTarget.Hello(JsonSerializer.SerializeToElement(new { text = "hello" }));

		// The nonstandard startup notification is ignored, so the host must be able to see why in the log
		// without the callback ever surfacing a result.
		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Debug|", StringComparison.Ordinal)
			&& log.Contains("$/hello", StringComparison.Ordinal)
			&& log.Contains("nonstandard startup notifications", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));

		Assert.IsFalse(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("$/hello", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void LogMessage_WritesTheServerMessageAtItsSeverityLevel()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		TransportSession session = CreateTransportSession(client, 9, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetTransportGeneration(session));

		rpcTarget.LogMessage(new WindowMessageParams(MessageType.Error, "server exploded"));

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Error|", StringComparison.Ordinal)
			&& log.Contains("[LS window/logMessage]", StringComparison.Ordinal)
			&& log.Contains("server exploded", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void ShowMessage_WithMissingSeverity_LogsAtDebugLevel()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		TransportSession session = CreateTransportSession(client, 10, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetTransportGeneration(session));

		rpcTarget.ShowMessage(new WindowMessageParams(Type: null, Message: "hello from the server"));

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Debug|", StringComparison.Ordinal)
			&& log.Contains("[LS window/showMessage]", StringComparison.Ordinal)
			&& log.Contains("hello from the server", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void RegisterCapability_WithNoRegistrations_ReturnsNullWithoutLogging()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		TransportSession session = CreateTransportSession(client, 11, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetTransportGeneration(session));

		Assert.IsNull(rpcTarget.RegisterCapability(new CapabilityRegistrationParams([])));
		Assert.AreEqual(0, logScope.Logs.Count, string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void UnregisterCapability_StaleTransportGeneration_LogsDebugWithoutWarning()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		TransportSession oldSession = CreateTransportSession(client, 12, process: null, Stream.Null, Stream.Null);
		TransportSession newSession = CreateTransportSession(client, 13, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, newSession);

		ClientRpcTarget rpcTarget = CreateRpcTarget(client, GetTransportGeneration(oldSession));
		object? result = rpcTarget.UnregisterCapability(
			new CapabilityUnregistrationParams(
			[
				new CapabilityUnregistrationPayload("1", "textDocument/rename")
			]));

		Assert.IsNull(result);
		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Debug|", StringComparison.Ordinal)
			&& log.Contains("client/unregisterCapability", StringComparison.Ordinal)
			&& log.Contains("stale", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));

		Assert.IsFalse(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("client/unregisterCapability", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}
}
