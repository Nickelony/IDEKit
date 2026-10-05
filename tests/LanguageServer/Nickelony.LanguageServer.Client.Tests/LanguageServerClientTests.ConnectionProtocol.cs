using System.Text.Json;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace Nickelony.LanguageServer.Client.Tests;

public sealed partial class LanguageServerClientTests
{
	[TestMethod]
	public async Task SendNotificationAsync_CompletesAfterLocalDispatchEvenWhenConnectionWriteRemainsBlocked()
	{
		using var blockingWriteStream = new GatedWriteStream();

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
			serverOutputStream: Stream.Null,
			serverInputStream: blockingWriteStream
		);

		using var cancellationSource = new CancellationTokenSource();

		SetActiveSession(client, session);
		SetReadyState(client, true);

		Task notificationTask = client.SendNotificationAsync(
			"workspace/didChangeConfiguration",
			new { settings = new { } },
			cancellationSource.Token
		);

		try
		{
			// The local dispatch must complete without waiting for the blocked connection write; the upper bound
			// only fails red when the notification never completes.
			await notificationTask.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);

			// The dispatch completed even though the connection write is still parked on the blocked stream: the
			// write was attempted, and the notification task settled before the write could complete.
			Task writeStarted = await Task.WhenAny(
					blockingWriteStream.WriteStarted,
					Task.Delay(TestPolling.DefaultTimeout, TestContext.CancellationToken)
				)
				.ConfigureAwait(false);

			Assert.AreSame(blockingWriteStream.WriteStarted, writeStarted, "The connection write must have been attempted.");
			Assert.IsTrue(notificationTask.IsCompleted, "The local dispatch must complete before the blocked connection write.");

			// Canceling the token after the local dispatch completed must not turn the completed send into a failure.
			cancellationSource.Cancel();
		}
		finally
		{
			// Release the blocked write so the abandoned JSON-RPC write can complete before the session is disposed.
			blockingWriteStream.Release();
		}
	}

	[TestMethod]
	public async Task SendNotificationAsync_WhenClientIsNotReady_ThrowsConnectionUnavailableException()
	{
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
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);

		Assert.IsFalse(client.IsReady);

		await Assert
			.ThrowsExactlyAsync<LanguageServerConnectionUnavailableException>(async () =>
				await client
					.SendNotificationAsync("workspace/didChangeConfiguration", new { settings = new { } }, CancellationToken.None)
					.ConfigureAwait(false)
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendNotificationAsync_WhenPayloadSerializationFails_DoesNotInvalidateConnection()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 10,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		var cyclicPayload = new Dictionary<string, object>();
		cyclicPayload["self"] = cyclicPayload;

		SetActiveSession(client, session);
		SetReadyState(client, true);

		Exception? observedException = null;

		try
		{
			await client.SendNotificationAsync("workspace/didChangeWatchedFiles", cyclicPayload, CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not UnitTestAssertException)
		{
			observedException = exception;
		}

		Assert.IsNotNull(observedException, "Expected the notification serialization to fail.");
		Assert.IsFalse(observedException is LanguageServerConnectionUnavailableException);
		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(10L, client.ConnectionGeneration);
	}

	[TestMethod]
	public async Task SendRequestAsync_WhenClientIsNotReady_ThrowsConnectionUnavailableException()
	{
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
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);

		Assert.IsFalse(client.IsReady);

		await Assert
			.ThrowsExactlyAsync<LanguageServerConnectionUnavailableException>(async () =>
				await client
					.SendRequestAsync<JsonElement>("workspace/configuration", new WorkspaceConfigurationParams([]), CancellationToken.None)
					.ConfigureAwait(false)
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendRequestAsync_WhenActiveConnectionFails_LogsRequestFailureWithGeneration()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		using var serverOutputStream = new GatedReadStream();

		await using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_defaultClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 7,
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

		await AssertFaultedOrCanceledAsync(requestTask).ConfigureAwait(false);
		Assert.IsFalse(client.IsReady);

		Assert.Contains(
			log =>
				log.Contains("request", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("workspace/configuration", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("failed", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("generation 7", StringComparison.OrdinalIgnoreCase),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public async Task SendRequestAsync_WhenActiveConnectionFails_ThrowsUnavailableAndRaisesConnectionUnavailable()
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
			generation: 7,
			process: null,
			serverOutputStream,
			serverInputStream: Stream.Null,
			startListening: true
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		long raisedGeneration = 0;
		client.ConnectionUnavailable += (_, eventArgs) => Interlocked.Exchange(ref raisedGeneration, eventArgs.ConnectionGeneration);

		Task<JsonElement> requestTask = client.SendRequestAsync<JsonElement>(
			"workspace/configuration",
			new WorkspaceConfigurationParams([]),
			CancellationToken.None
		);

		Task completedTask = await Task.WhenAny(requestTask, Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreNotSame(requestTask, completedTask);

		// A locally disposed connection is treated as an expected shutdown and does not raise the event,
		// so simulate a genuine connection failure the way the connection session supervisor reports one. The failed-session
		// teardown runs in the background and may clear the session's JSON-RPC instance first, so the instance is
		// captured up front and disposed explicitly (JSON-RPC disposal is idempotent).
		JsonRpc jsonRpc = session.JsonRpc!;

		client.Composition.Supervisor.HandleJsonRpcDisconnected(
			session,
			new JsonRpcDisconnectedEventArgs("active connection failed", DisconnectedReason.StreamError)
		);

		jsonRpc.Dispose();

		await Assert
			.ThrowsExactlyAsync<LanguageServerConnectionUnavailableException>(async () => await requestTask.ConfigureAwait(false))
			.ConfigureAwait(false);

		Assert.AreEqual(7L, Interlocked.Read(ref raisedGeneration));
		Assert.IsFalse(client.IsReady);
	}

	[TestMethod]
	public async Task SendRequestAsync_WhenServerReturnsJsonRpcError_ThrowsRejectedExceptionAndKeepsConnectionReady()
	{
		using var deferredServerOutputStream = new DeferredPersistentJsonRpcResponseStream();
		using var serverInputStream = new RecordingStream();

		await using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_briefTeardownClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 9,
			process: null,
			serverOutputStream: deferredServerOutputStream,
			serverInputStream,
			startListening: true
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		Task<JsonElement> requestTask = client.SendRequestAsync<JsonElement>(
			"workspace/configuration",
			new WorkspaceConfigurationParams([]),
			CancellationToken.None
		);

		int requestId = await WaitForRequestIdAsync(serverInputStream).ConfigureAwait(false);
		deferredServerOutputStream.SetPayload(JsonRpcFrameTestHelpers.BuildErrorResponse(requestId, -32000, "Simulated request failure."));

		LanguageServerRequestRejectedException? observedException = null;

		try
		{
			await requestTask.ConfigureAwait(false);
		}
		catch (LanguageServerRequestRejectedException exception)
		{
			observedException = exception;
		}

		Assert.IsNotNull(observedException, "Expected the JSON-RPC request to fail with a rejection.");
		Assert.AreEqual(-32000, observedException.ErrorCode);
		Assert.IsTrue(observedException.Message.Contains("Simulated request failure.", StringComparison.Ordinal), observedException.Message);
		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(9L, client.ConnectionGeneration);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task SendNotificationAsync_WhenStartupHandshakeIsInProgress_ThrowsConnectionUnavailableException()
	{
		var sessionActivated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var startupCancellation = new CancellationTokenSource();

		await using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: new StdioLanguageServerTransport(Path.Combine(Environment.SystemDirectory, "cmd.exe")),
			hooks: null,
			options: s_defaultClientOptions,
			logger: null,

			testHooks: new LanguageServerClientTestHooks
			{
				SessionActivated = cancellationToken => WaitForStartupCancellationAsync(sessionActivated, cancellationToken),
			}
		);

		Task<bool> startTask = client.EnsureStartedAsync(startupCancellation.Token);

		await sessionActivated.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		await Assert
			.ThrowsExactlyAsync<LanguageServerConnectionUnavailableException>(async () =>
				await client
					.SendNotificationAsync("workspace/didChangeConfiguration", new { settings = new { } }, CancellationToken.None)
					.ConfigureAwait(false)
			)
			.ConfigureAwait(false);

		startupCancellation.Cancel();

		await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await startTask.ConfigureAwait(false)).ConfigureAwait(false);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task SendRequestAsync_WhenStartupHandshakeIsInProgress_ThrowsConnectionUnavailableException()
	{
		var sessionActivated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var startupCancellation = new CancellationTokenSource();

		await using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: new StdioLanguageServerTransport(Path.Combine(Environment.SystemDirectory, "cmd.exe")),
			hooks: null,
			options: s_defaultClientOptions,
			logger: null,

			testHooks: new LanguageServerClientTestHooks
			{
				SessionActivated = cancellationToken => WaitForStartupCancellationAsync(sessionActivated, cancellationToken),
			}
		);

		Task<bool> startTask = client.EnsureStartedAsync(startupCancellation.Token);

		await sessionActivated.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		await Assert
			.ThrowsExactlyAsync<LanguageServerConnectionUnavailableException>(async () =>
				await client
					.SendRequestAsync<JsonElement>("workspace/configuration", new WorkspaceConfigurationParams([]), CancellationToken.None)
					.ConfigureAwait(false)
			)
			.ConfigureAwait(false);

		startupCancellation.Cancel();

		await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await startTask.ConfigureAwait(false)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendRequestAsync_WhenConnectionGenerationIsReplacedBeforeSuccessfulResponse_FailsWithConnectionSupersededException()
	{
		using var deferredServerOutputStream = new DeferredJsonRpcResponseStream();
		using var serverInputStream = new RecordingStream();

		await using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession originalSession = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: deferredServerOutputStream,
			serverInputStream,
			startListening: true
		);

		ConnectionSession replacementSession = CreateConnectionSession(
			client,
			generation: 2,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, originalSession);
		SetReadyState(client, true);

		Task<JsonElement> requestTask = client.SendRequestAsync<JsonElement>(
			"workspace/configuration",
			new WorkspaceConfigurationParams([]),
			CancellationToken.None
		);

		int requestId = await WaitForRequestIdAsync(serverInputStream).ConfigureAwait(false);

		SetActiveSession(client, replacementSession);
		SetReadyState(client, true);

		deferredServerOutputStream.SetPayload(JsonRpcFrameTestHelpers.BuildResultResponse(requestId, "{\"value\":1}"));

		await Assert
			.ThrowsExactlyAsync<LanguageServerConnectionSupersededException>(async () => await requestTask.ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendRequestAsync_WhenConnectionGenerationIsReplacedBeforeErrorResponse_FailsWithConnectionSupersededException()
	{
		using var deferredServerOutputStream = new DeferredJsonRpcResponseStream();
		using var serverInputStream = new RecordingStream();

		await using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession originalSession = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: deferredServerOutputStream,
			serverInputStream,
			startListening: true
		);

		ConnectionSession replacementSession = CreateConnectionSession(
			client,
			generation: 2,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, originalSession);
		SetReadyState(client, true);

		Task<JsonElement> requestTask = client.SendRequestAsync<JsonElement>(
			"workspace/configuration",
			new WorkspaceConfigurationParams([]),
			CancellationToken.None
		);

		int requestId = await WaitForRequestIdAsync(serverInputStream).ConfigureAwait(false);

		SetActiveSession(client, replacementSession);
		SetReadyState(client, true);

		deferredServerOutputStream.SetPayload(JsonRpcFrameTestHelpers.BuildErrorResponse(requestId, -32000, "Simulated superseded rejection."));

		// A rejection from a superseded connection generation is discarded like a superseded result, so a host never
		// surfaces a stale server outcome for a request that belongs to a replaced connection.
		await Assert
			.ThrowsExactlyAsync<LanguageServerConnectionSupersededException>(async () => await requestTask.ConfigureAwait(false))
			.ConfigureAwait(false);
	}

	[TestMethod]
	public void TryMarkConnectionUnhealthy_WithNonPositiveGeneration_ReturnsFalseAndKeepsReadiness()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
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

		// Generation 0 marks the pre-start state, so there is nothing to invalidate for it or for negative values.
		Assert.IsFalse(client.TryMarkConnectionUnhealthy(0));
		Assert.IsFalse(client.TryMarkConnectionUnhealthy(-1));
		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(4L, client.ConnectionGeneration);
	}

	[TestMethod]
	public async Task SendRequestAsync_WithTypedSuccessfulResult_ReturnsTheDeserializedValue()
	{
		using var deferredServerOutputStream = new DeferredPersistentJsonRpcResponseStream();
		using var serverInputStream = new RecordingStream();

		await using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_briefTeardownClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: deferredServerOutputStream,
			serverInputStream,
			startListening: true
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		Task<TestValuePayload> requestTask = client.SendRequestAsync<TestValuePayload>("example/echo", new EmptyParams(), CancellationToken.None);

		int requestId = await WaitForRequestIdAsync(serverInputStream).ConfigureAwait(false);

		deferredServerOutputStream.SetPayload(JsonRpcFrameTestHelpers.BuildResultResponse(requestId, "{\"value\":42}"));

		TestValuePayload result = await requestTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(42, result.Value);
		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(1L, client.ConnectionGeneration);
	}

	private sealed class TestValuePayload
	{
		public int Value { get; init; }
	}

	[TestMethod]
	public async Task SendMethods_WithBlankMethod_ThrowArgumentException()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		await Assert
			.ThrowsExactlyAsync<ArgumentException>(async () =>
				await client.SendNotificationAsync(" ", parameters: null, CancellationToken.None).ConfigureAwait(false)
			)
			.ConfigureAwait(false);

		await Assert
			.ThrowsExactlyAsync<ArgumentException>(async () =>
				await client.SendRequestAsync<object>(" ", new object(), CancellationToken.None).ConfigureAwait(false)
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendRequestAsync_WhenCancellationIsRequestedForAnInFlightRequest_CancelsTheWaitAndKeepsTheSessionReady()
	{
		using var serverOutputStream = new GatedReadStream();
		using var serverInputStream = new RecordingStream();

		await using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 7,
			process: null,
			serverOutputStream,
			serverInputStream,
			startListening: true
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		using var cancellationSource = new CancellationTokenSource();

		Task<JsonElement> requestTask = client.SendRequestAsync<JsonElement>(
			"workspace/configuration",
			new WorkspaceConfigurationParams([]),
			cancellationSource.Token
		);

		// Wait for the request to be written before canceling, so the cancellation reaches the in-flight
		// JSON-RPC request rather than the pre-send fast path.
		await WaitForRequestIdAsync(serverInputStream).ConfigureAwait(false);
		cancellationSource.Cancel();

		// The caller's token must bound the wait even though this server never answers: the request has to
		// complete as canceled instead of waiting for a response that will not arrive. The bounded wait fails
		// red with a timeout instead of blocking forever when the token stops bounding the wait.
		bool canceled = false;

		try
		{
			await requestTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			canceled = true;
		}

		Assert.IsTrue(canceled, "The in-flight request must complete as canceled when the caller cancels it.");

		// The cancellation is still forwarded to the server so it can stop working on the request.
		await WaitForWrittenTextAsync(serverInputStream, "$/cancelRequest").ConfigureAwait(false);

		// A host-canceled request is not a connection failure, so the ready session must survive it.
		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(7L, client.ConnectionGeneration);

		// End the pending read so the session's background read loop can finish before the client's teardown.
		session.JsonRpc!.Dispose();
	}

	[TestMethod]
	public async Task SendRequestAsync_WithAnAlreadyCanceledToken_FailsCanceledWithoutInvalidatingTheConnection()
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
			generation: 7,
			process: null,
			serverOutputStream,
			serverInputStream: Stream.Null,
			startListening: true
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		using var cancellationSource = new CancellationTokenSource();
		cancellationSource.Cancel();

		bool canceled = false;

		try
		{
			await client
				.SendRequestAsync<JsonElement>("workspace/configuration", new WorkspaceConfigurationParams([]), cancellationSource.Token)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			canceled = true;
		}

		Assert.IsTrue(canceled, "Expected the request to be canceled before it was sent.");
		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(7L, client.ConnectionGeneration);

		// End the pending read so the session's background read loop can finish before the client's teardown.
		session.JsonRpc!.Dispose();
	}
}
