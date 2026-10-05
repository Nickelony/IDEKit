using System.Text.Json;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace Nickelony.LanguageServer.Client.Tests;

public sealed partial class LanguageServerClientTests
{
	[TestMethod]
	public async Task MarkConnectionUnhealthy_DuringInFlightRequest_LeavesRequestPendingAndBlocksFutureRequests()
	{
		using var serverOutputStream = new GatedReadStream();

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream,
			serverInputStream: Stream.Null,
			startListening: true
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		Task<JsonElement> requestTask = client.SendRequestAsync<JsonElement>(
			"workspace/configuration",
			new WorkspaceConfigurationParams([]),
			CancellationToken.None
		);

		Task completedTask = await Task.WhenAny(requestTask, Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreNotSame(requestTask, completedTask);

		client.TryMarkConnectionUnhealthy(client.ConnectionGeneration);

		Assert.IsFalse(client.IsReady);

		await Assert
			.ThrowsExactlyAsync<LanguageServerConnectionUnavailableException>(async () =>
				await client
					.SendRequestAsync<JsonElement>("workspace/configuration", new WorkspaceConfigurationParams([]), CancellationToken.None)
					.ConfigureAwait(false)
			)
			.ConfigureAwait(false);

		completedTask = await Task.WhenAny(requestTask, Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken)).ConfigureAwait(false);
		Assert.AreNotSame(requestTask, completedTask);

		session.JsonRpc!.Dispose();

		await AssertFaultedOrCanceledAsync(requestTask).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ActiveSessionDisconnect_WhileRequestIsInFlight_FaultsOrCancelsPendingRequestAndMarksClientNotReady()
	{
		using var serverOutputStream = new GatedReadStream();

		await using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream,
			serverInputStream: Stream.Null,
			startListening: true
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		Task<JsonElement> requestTask = client.SendRequestAsync<JsonElement>(
			"workspace/configuration",
			new WorkspaceConfigurationParams([]),
			CancellationToken.None
		);

		Task completedTask = await Task.WhenAny(requestTask, Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreNotSame(requestTask, completedTask);

		session.JsonRpc!.Dispose();

		// Disposal completion is the event under test; the bound only needs to accommodate a loaded machine.
		completedTask = await Task.WhenAny(requestTask, Task.Delay(TimeSpan.FromSeconds(5), TestContext.CancellationToken)).ConfigureAwait(false);

		Assert.AreSame(requestTask, completedTask);
		Assert.IsFalse(client.IsReady);

		await Assert
			.ThrowsExactlyAsync<LanguageServerConnectionUnavailableException>(async () =>
				await client
					.SendRequestAsync<JsonElement>("workspace/configuration", new WorkspaceConfigurationParams([]), CancellationToken.None)
					.ConfigureAwait(false)
			)
			.ConfigureAwait(false);

		await AssertFaultedOrCanceledAsync(requestTask).ConfigureAwait(false);
	}

	[TestMethod]
	public void JsonRpc_Disconnected_LocallyDisposedActiveConnection_LogsExpectedShutdownAtInfoLevel()
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
			generation: 3,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		client.Composition.Supervisor.HandleJsonRpcDisconnected(
			session,
			new JsonRpcDisconnectedEventArgs("active connection closed", DisconnectedReason.LocallyDisposed)
		);

		Assert.Contains(
			log =>
				log.StartsWith("Info|", StringComparison.Ordinal)
				&& log.Contains("expected local shutdown", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("generation 3", StringComparison.OrdinalIgnoreCase),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public void JsonRpc_Disconnected_UnexpectedActiveConnectionPublishesUnavailableGenerationAfterReset()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 3,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		long unavailableGeneration = 0;
		long publishedGenerationDuringCallback = -1;

		SetActiveSession(client, session);
		SetReadyState(client, true);

		client.ConnectionUnavailable += (_, eventArgs) =>
		{
			unavailableGeneration = eventArgs.ConnectionGeneration;
			publishedGenerationDuringCallback = client.ConnectionGeneration;
		};

		client.Composition.Supervisor.HandleJsonRpcDisconnected(
			session,
			new JsonRpcDisconnectedEventArgs("active connection failed", DisconnectedReason.StreamError)
		);

		Assert.IsFalse(client.IsReady);
		Assert.AreEqual(0L, client.ConnectionGeneration);
		Assert.AreEqual(3L, unavailableGeneration);
		Assert.AreEqual(0L, publishedGenerationDuringCallback);
		Assert.IsNull(client.Composition.State.ActiveSession);
	}

	[TestMethod]
	public async Task JsonRpc_Disconnected_DuringClientDisposal_LogsDebugWithoutWarning()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		var releaseTeardown = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		await using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>(),
			testHooks: new LanguageServerClientTestHooks { BeforeTeardown = () => releaseTeardown.Task }
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 4,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		// Teardown is parked at its first step, so the session is still the active one when the disconnect arrives; a
		// disconnect from an already-detached session would take the stale-generation branch instead.
		Task disposeTask = client.DisposeAsync().AsTask();

		client.Composition.Supervisor.HandleJsonRpcDisconnected(
			session,
			new JsonRpcDisconnectedEventArgs("disposing connection closed", DisconnectedReason.StreamError)
		);

		Assert.IsFalse(client.IsReady);

		Assert.Contains(
			log =>
				log.StartsWith("Debug|", StringComparison.Ordinal)
				&& log.Contains("during client disposal", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("generation 4", StringComparison.OrdinalIgnoreCase),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);

		Assert.DoesNotContain(
			log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("disconnected unexpectedly", StringComparison.OrdinalIgnoreCase),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);

		releaseTeardown.TrySetResult(true);
		await disposeTask.ConfigureAwait(false);
	}

	[TestMethod]
	public void JsonRpc_Disconnected_OldConnectionGenerationDoesNotAffectActiveSession()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession oldSession = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		ConnectionSession newSession = CreateConnectionSession(
			client,
			generation: 2,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, newSession);
		SetReadyState(client, true);

		client.Composition.Supervisor.HandleJsonRpcDisconnected(
			oldSession,
			new JsonRpcDisconnectedEventArgs("old connection closed", DisconnectedReason.LocallyDisposed)
		);

		Assert.IsTrue(client.IsReady);

		client.Composition.Supervisor.HandleJsonRpcDisconnected(
			newSession,
			new JsonRpcDisconnectedEventArgs("active connection closed", DisconnectedReason.LocallyDisposed)
		);

		Assert.IsFalse(client.IsReady);
		Assert.IsNull(client.Composition.State.ActiveSession);
	}

	[TestMethod]
	public void Process_Exited_SupersededConnectionGenerationDoesNotAffectActiveSessionOrWarn()
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
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		ConnectionSession newSession = CreateConnectionSession(
			client,
			generation: 2,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, newSession);
		SetReadyState(client, true);

		client.Composition.Supervisor.HandleProcessExited(oldSession);

		Assert.AreEqual(2L, client.ConnectionGeneration);
		Assert.IsTrue(client.IsReady);

		Assert.DoesNotContain(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("generation 1", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("exited unexpectedly", StringComparison.OrdinalIgnoreCase),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public void Process_Exited_ActiveConnection_DetachesActiveSessionImmediately()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 2,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		long unavailableGeneration = 0;
		long publishedGenerationDuringCallback = -1;

		client.ConnectionUnavailable += (_, eventArgs) =>
		{
			unavailableGeneration = eventArgs.ConnectionGeneration;
			publishedGenerationDuringCallback = client.ConnectionGeneration;
		};

		client.Composition.Supervisor.HandleProcessExited(session);

		Assert.IsFalse(client.IsReady);
		Assert.AreEqual(0L, client.ConnectionGeneration);
		Assert.AreEqual(2L, unavailableGeneration);
		Assert.AreEqual(0L, publishedGenerationDuringCallback);
		Assert.IsNull(client.Composition.State.ActiveSession);
	}
}
