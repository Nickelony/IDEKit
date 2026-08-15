using Microsoft.Extensions.Logging;
using StreamJsonRpc;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

public partial class LanguageServerClientTests
{
	[TestMethod]
	public async Task MarkTransportUnhealthy_DuringInFlightRequest_LeavesRequestPendingAndBlocksFutureRequests()
	{
		using var serverOutputStream = new PendingReadStream();
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 1, process: null, serverOutputStream, Stream.Null, startListening: true);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		Task<JsonElement> requestTask = client.SendRequestAsync<JsonElement>(
			"workspace/configuration",
			new WorkspaceConfigurationParams([]),
			CancellationToken.None);

		Task completedTask = await Task.WhenAny(requestTask, Task.Delay(TestPolling.AbsenceWindow)).ConfigureAwait(false);
		Assert.AreNotSame(requestTask, completedTask);

		client.TryMarkTransportUnhealthy(client.TransportGeneration);

		Assert.IsFalse(client.IsReady);

		await Assert.ThrowsExactlyAsync<LanguageServerTransportUnavailableException>(async () =>
			await client.SendRequestAsync<JsonElement>(
				"workspace/configuration",
				new WorkspaceConfigurationParams([]),
				CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);

		completedTask = await Task.WhenAny(requestTask, Task.Delay(TestPolling.AbsenceWindow)).ConfigureAwait(false);
		Assert.AreNotSame(requestTask, completedTask);

		session.JsonRpc!.Dispose();

		await AssertFaultedOrCanceledAsync(requestTask).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ActiveSessionDisconnect_WhileRequestIsInFlight_FaultsOrCancelsPendingRequestAndMarksClientNotReady()
	{
		using var serverOutputStream = new PendingReadStream();
		await using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 1, process: null, serverOutputStream, Stream.Null, startListening: true);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		Task<JsonElement> requestTask = client.SendRequestAsync<JsonElement>(
			"workspace/configuration",
			new WorkspaceConfigurationParams([]),
			CancellationToken.None);

		Task completedTask = await Task.WhenAny(requestTask, Task.Delay(TestPolling.AbsenceWindow)).ConfigureAwait(false);
		Assert.AreNotSame(requestTask, completedTask);

		session.JsonRpc!.Dispose();

		// Disposal completion is the event under test; the bound only needs to accommodate a loaded machine.
		completedTask = await Task.WhenAny(requestTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
		Assert.AreSame(requestTask, completedTask);
		Assert.IsFalse(client.IsReady);

		await Assert.ThrowsExactlyAsync<LanguageServerTransportUnavailableException>(async () =>
			await client.SendRequestAsync<JsonElement>(
				"workspace/configuration",
				new WorkspaceConfigurationParams([]),
				CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);

		await AssertFaultedOrCanceledAsync(requestTask).ConfigureAwait(false);
	}

	[TestMethod]
	public void JsonRpc_Disconnected_LocallyDisposedActiveTransport_LogsExpectedShutdownAtInfoLevel()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		TransportSession session = CreateTransportSession(client, 3, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		client.TransportHost.HandleJsonRpcDisconnected(
			session,
			new JsonRpcDisconnectedEventArgs("active transport closed", DisconnectedReason.LocallyDisposed));

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Info|", StringComparison.Ordinal)
			&& log.Contains("expected local shutdown", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("generation 3", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void JsonRpc_Disconnected_UnexpectedActiveTransportPublishesUnavailableGenerationAfterReset()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 3, process: null, Stream.Null, Stream.Null);
		long unavailableGeneration = 0;
		long publishedGenerationDuringCallback = -1;

		SetActiveSession(client, session);
		SetReadyState(client, true);

		client.TransportUnavailable += (_, eventArgs) =>
		{
			unavailableGeneration = eventArgs.Generation;
			publishedGenerationDuringCallback = client.TransportGeneration;
		};

		client.TransportHost.HandleJsonRpcDisconnected(
			session,
			new JsonRpcDisconnectedEventArgs("active transport failed", DisconnectedReason.StreamError));

		Assert.IsFalse(client.IsReady);
		Assert.AreEqual(0L, client.TransportGeneration);
		Assert.AreEqual(3L, unavailableGeneration);
		Assert.AreEqual(0L, publishedGenerationDuringCallback);
		Assert.IsNull(client.CapabilityStore.ActiveSession);
	}

	[TestMethod]
	public async Task JsonRpc_Disconnected_DuringClientDisposal_LogsDebugWithoutWarning()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		var releaseTeardown = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		await using var client = new LanguageServerClient(
			[@"C:\Workspace"],
			"example-language-server.exe",
			s_defaultClientOptions,
			logScope.CreateLogger<LanguageServerClient>(),
			testHooks: new ClientTestHooks
			{
				BeforeTeardown = () => releaseTeardown.Task
			});
		TransportSession session = CreateTransportSession(client, 4, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		// Teardown is parked at its first step, so the session is still the active one when the disconnect arrives; a
		// disconnect from an already-detached session would take the stale-generation branch instead.
		Task disposeTask = client.DisposeAsync().AsTask();

		client.TransportHost.HandleJsonRpcDisconnected(
			session,
			new JsonRpcDisconnectedEventArgs("disposing transport closed", DisconnectedReason.StreamError));

		Assert.IsFalse(client.IsReady);

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Debug|", StringComparison.Ordinal)
			&& log.Contains("during client disposal", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("generation 4", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));

		Assert.IsFalse(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("disconnected unexpectedly", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));

		releaseTeardown.TrySetResult(true);
		await disposeTask.ConfigureAwait(false);
	}

	[TestMethod]
	public void JsonRpc_Disconnected_OldTransportGenerationDoesNotAffectActiveSession()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession oldSession = CreateTransportSession(client, 1, process: null, Stream.Null, Stream.Null);
		TransportSession newSession = CreateTransportSession(client, 2, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, newSession);
		SetReadyState(client, true);

		client.TransportHost.HandleJsonRpcDisconnected(
			oldSession,
			new JsonRpcDisconnectedEventArgs("old transport closed", DisconnectedReason.LocallyDisposed));

		Assert.IsTrue(client.IsReady);

		client.TransportHost.HandleJsonRpcDisconnected(
			newSession,
			new JsonRpcDisconnectedEventArgs("active transport closed", DisconnectedReason.LocallyDisposed));

		Assert.IsFalse(client.IsReady);
		Assert.IsNull(client.CapabilityStore.ActiveSession);
	}

	[TestMethod]
	public void Process_Exited_SupersededTransportGenerationDoesNotAffectActiveSessionOrWarn()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		TransportSession oldSession = CreateTransportSession(client, 1, process: null, Stream.Null, Stream.Null);
		TransportSession newSession = CreateTransportSession(client, 2, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, newSession);
		SetReadyState(client, true);

		client.TransportHost.HandleProcessExited(oldSession);

		Assert.AreEqual(2L, client.TransportGeneration);
		Assert.IsTrue(client.IsReady);

		Assert.IsFalse(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("generation 1", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("exited unexpectedly", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void Process_Exited_ActiveTransport_DetachesActiveSessionImmediately()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 2, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		long unavailableGeneration = 0;
		long publishedGenerationDuringCallback = -1;

		client.TransportUnavailable += (_, eventArgs) =>
		{
			unavailableGeneration = eventArgs.Generation;
			publishedGenerationDuringCallback = client.TransportGeneration;
		};

		client.TransportHost.HandleProcessExited(session);

		Assert.IsFalse(client.IsReady);
		Assert.AreEqual(0L, client.TransportGeneration);
		Assert.AreEqual(2L, unavailableGeneration);
		Assert.AreEqual(0L, publishedGenerationDuringCallback);
		Assert.IsNull(client.CapabilityStore.ActiveSession);
	}
}
