using Microsoft.Extensions.Logging;
using StreamJsonRpc;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

public partial class LanguageServerClientTests
{
	[TestMethod]
	public async Task SendNotificationAsync_CompletesAfterLocalDispatchEvenWhenTransportWriteRemainsBlocked()
	{
		using var blockingWriteStream = new BlockingWriteStream();
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 1, process: null, Stream.Null, blockingWriteStream);
		using var cancellationSource = new CancellationTokenSource();

		SetActiveSession(client, session);
		SetReadyState(client, true);

		Task notificationTask = client.SendNotificationAsync(
			"workspace/didChangeConfiguration",
			new { settings = new { } },
			cancellationSource.Token);

		try
		{
			// The local dispatch must complete without waiting for the blocked transport write; the upper bound
			// only fails red when the notification never completes.
			await notificationTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

			// The dispatch completed even though the transport write is still parked on the blocked stream: the
			// write was attempted, and the notification task settled before the write could complete.
			Task writeStarted = await Task.WhenAny(blockingWriteStream.WriteStarted, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
			Assert.AreSame(blockingWriteStream.WriteStarted, writeStarted, "The transport write must have been attempted.");
			Assert.IsTrue(notificationTask.IsCompleted, "The local dispatch must complete before the blocked transport write.");

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
	public async Task ObserveAbandonedTask_WhenTaskLaterFaults_LogsDebugDiagnostic()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		var notificationSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		client.ProtocolForwarder.ObserveAbandonedTask(notificationSource.Task);

		notificationSource.TrySetException(new IOException("Simulated abandoned notification failure."));

		await TestPolling.UntilAsync(
			() => HasAbandonedNotificationLog(logScope),
			TimeSpan.FromSeconds(5),
			"The abandoned notification failure should be logged at debug level.").ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendNotificationAsync_WhenClientIsNotReady_ThrowsTransportUnavailableException()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 1, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);

		Assert.IsFalse(client.IsReady);

		await Assert.ThrowsExactlyAsync<LanguageServerTransportUnavailableException>(async () =>
			await client.SendNotificationAsync(
				"workspace/didChangeConfiguration",
				new { settings = new { } },
				CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendNotificationAsync_WhenPayloadSerializationFails_DoesNotInvalidateTransport()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 10, process: null, Stream.Null, Stream.Null);

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
		Assert.IsFalse(observedException is LanguageServerTransportUnavailableException);
		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(10L, client.TransportGeneration);
	}

	[TestMethod]
	public async Task SendRequestAsync_WhenClientIsNotReady_ThrowsTransportUnavailableException()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 1, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);

		Assert.IsFalse(client.IsReady);

		await Assert.ThrowsExactlyAsync<LanguageServerTransportUnavailableException>(async () =>
			await client.SendRequestAsync<JsonElement>(
				"workspace/configuration",
				new WorkspaceConfigurationParams([]),
				CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendRequestAsync_WhenActiveTransportFails_LogsRequestFailureWithGeneration()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var serverOutputStream = new PendingReadStream();
		await using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		TransportSession session = CreateTransportSession(client, 7, process: null, serverOutputStream, Stream.Null, startListening: true);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		Task<JsonElement> requestTask = client.SendRequestAsync<JsonElement>(
			"workspace/configuration",
			new WorkspaceConfigurationParams([]),
			CancellationToken.None);

		Task completedTask = await Task.WhenAny(requestTask, Task.Delay(TestPolling.AbsenceWindow)).ConfigureAwait(false);
		Assert.AreNotSame(requestTask, completedTask);

		session.JsonRpc!.Dispose();

		await AssertFaultedOrCanceledAsync(requestTask).ConfigureAwait(false);
		Assert.IsFalse(client.IsReady);

		Assert.IsTrue(logScope.Logs.Any(log => log.Contains("request", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("workspace/configuration", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("failed", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("generation 7", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public async Task SendRequestAsync_WhenActiveTransportFails_ThrowsUnavailableAndRaisesTransportUnavailable()
	{
		using var serverOutputStream = new PendingReadStream();
		await using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 7, process: null, serverOutputStream, Stream.Null, startListening: true);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		long raisedGeneration = 0;
		client.TransportUnavailable += (_, eventArgs) => Interlocked.Exchange(ref raisedGeneration, eventArgs.Generation);

		Task<JsonElement> requestTask = client.SendRequestAsync<JsonElement>(
			"workspace/configuration",
			new WorkspaceConfigurationParams([]),
			CancellationToken.None);

		Task completedTask = await Task.WhenAny(requestTask, Task.Delay(TestPolling.AbsenceWindow)).ConfigureAwait(false);
		Assert.AreNotSame(requestTask, completedTask);

		// A locally disposed transport is treated as an expected shutdown and does not raise the event,
		// so simulate a genuine transport failure the way the transport host reports one. The failed-session
		// teardown runs in the background and may clear the session's transport first, so the instance is
		// captured up front and disposed explicitly (JSON-RPC disposal is idempotent).
		JsonRpc jsonRpc = session.JsonRpc!;

		client.TransportHost.HandleJsonRpcDisconnected(
			session,
			new JsonRpcDisconnectedEventArgs("active transport failed", DisconnectedReason.StreamError));

		jsonRpc.Dispose();

		await Assert.ThrowsExactlyAsync<LanguageServerTransportUnavailableException>(async () =>
			await requestTask.ConfigureAwait(false)).ConfigureAwait(false);

		Assert.AreEqual(7L, Interlocked.Read(ref raisedGeneration));
		Assert.IsFalse(client.IsReady);
	}

	[TestMethod]
	public async Task SendRequestAsync_WhenServerReturnsJsonRpcError_ThrowsRejectedExceptionAndKeepsTransportReady()
	{
		using var deferredServerOutputStream = new DeferredPersistentJsonRpcResponseStream();
		using var serverInputStream = new RecordingStream();
		await using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 9, process: null, deferredServerOutputStream, serverInputStream, startListening: true);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		Task<JsonElement> requestTask = client.SendRequestAsync<JsonElement>(
			"workspace/configuration",
			new WorkspaceConfigurationParams([]),
			CancellationToken.None);

		int requestId = await WaitForRequestIdAsync(serverInputStream).ConfigureAwait(false);
		deferredServerOutputStream.SetPayload(JsonRpcFrameTestHelper.BuildErrorResponse(requestId, -32000, "Simulated request failure."));

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
		Assert.AreEqual(9L, client.TransportGeneration);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task SendNotificationAsync_WhenStartupHandshakeIsInProgress_ThrowsTransportUnavailableException()
	{
		var sessionActivated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var startupCancellation = new CancellationTokenSource();

		await using var client = new LanguageServerClient(
			[@"C:\Workspace"],
			Path.Combine(Environment.SystemDirectory, "cmd.exe"),
			s_defaultClientOptions,
			null,
			testHooks: new ClientTestHooks
			{
				SessionActivated = cancellationToken => WaitForStartupCancellationAsync(sessionActivated, cancellationToken)
			});

		Task<bool> startTask = client.StartAsync(startupCancellation.Token);

		await sessionActivated.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		await Assert.ThrowsExactlyAsync<LanguageServerTransportUnavailableException>(async () =>
			await client.SendNotificationAsync(
				"workspace/didChangeConfiguration",
				new { settings = new { } },
				CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);

		startupCancellation.Cancel();

		await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await startTask.ConfigureAwait(false)).ConfigureAwait(false);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task SendRequestAsync_WhenStartupHandshakeIsInProgress_ThrowsTransportUnavailableException()
	{
		var sessionActivated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var startupCancellation = new CancellationTokenSource();

		await using var client = new LanguageServerClient(
			[@"C:\Workspace"],
			Path.Combine(Environment.SystemDirectory, "cmd.exe"),
			s_defaultClientOptions,
			null,
			testHooks: new ClientTestHooks
			{
				SessionActivated = cancellationToken => WaitForStartupCancellationAsync(sessionActivated, cancellationToken)
			});

		Task<bool> startTask = client.StartAsync(startupCancellation.Token);

		await sessionActivated.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		await Assert.ThrowsExactlyAsync<LanguageServerTransportUnavailableException>(async () =>
			await client.SendRequestAsync<JsonElement>(
				"workspace/configuration",
				new WorkspaceConfigurationParams([]),
				CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);

		startupCancellation.Cancel();

		await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await startTask.ConfigureAwait(false)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendRequestAsync_WhenTransportGenerationIsReplacedBeforeSuccessfulResponse_FailsWithTransportChangedException()
	{
		using var deferredServerOutputStream = new DeferredJsonRpcResponseStream();
		using var serverInputStream = new RecordingStream();
		await using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession originalSession = CreateTransportSession(client, 1, process: null, deferredServerOutputStream, serverInputStream, startListening: true);
		TransportSession replacementSession = CreateTransportSession(client, 2, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, originalSession);
		SetReadyState(client, true);

		Task<JsonElement> requestTask = client.SendRequestAsync<JsonElement>(
			"workspace/configuration",
			new WorkspaceConfigurationParams([]),
			CancellationToken.None);

		int requestId = await WaitForRequestIdAsync(serverInputStream).ConfigureAwait(false);

		SetActiveSession(client, replacementSession);
		SetReadyState(client, true);

		deferredServerOutputStream.SetPayload(JsonRpcFrameTestHelper.BuildResultResponse(requestId, "{\"value\":1}"));

		await Assert.ThrowsExactlyAsync<LanguageServerTransportChangedException>(async () => await requestTask.ConfigureAwait(false)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendRequestAsync_WhenTransportGenerationIsReplacedBeforeErrorResponse_FailsWithTransportChangedException()
	{
		using var deferredServerOutputStream = new DeferredJsonRpcResponseStream();
		using var serverInputStream = new RecordingStream();
		await using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession originalSession = CreateTransportSession(client, 1, process: null, deferredServerOutputStream, serverInputStream, startListening: true);
		TransportSession replacementSession = CreateTransportSession(client, 2, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, originalSession);
		SetReadyState(client, true);

		Task<JsonElement> requestTask = client.SendRequestAsync<JsonElement>(
			"workspace/configuration",
			new WorkspaceConfigurationParams([]),
			CancellationToken.None);

		int requestId = await WaitForRequestIdAsync(serverInputStream).ConfigureAwait(false);

		SetActiveSession(client, replacementSession);
		SetReadyState(client, true);

		deferredServerOutputStream.SetPayload(JsonRpcFrameTestHelper.BuildErrorResponse(requestId, -32000, "Simulated superseded rejection."));

		// A rejection from a superseded transport generation is discarded like a superseded result, so a host never
		// surfaces a stale server outcome for a request that belongs to a replaced transport.
		await Assert.ThrowsExactlyAsync<LanguageServerTransportChangedException>(async () => await requestTask.ConfigureAwait(false)).ConfigureAwait(false);
	}

	[TestMethod]
	public void TryMarkTransportUnhealthy_WithNonPositiveGeneration_ReturnsFalseAndKeepsReadiness()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 4, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		// Generation 0 marks the pre-start state, so there is nothing to invalidate for it or for negative values.
		Assert.IsFalse(client.TryMarkTransportUnhealthy(0));
		Assert.IsFalse(client.TryMarkTransportUnhealthy(-1));
		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(4L, client.TransportGeneration);
	}

	[TestMethod]
	public async Task SendRequestAsync_WithTypedSuccessfulResult_ReturnsTheDeserializedValue()
	{
		using var deferredServerOutputStream = new DeferredPersistentJsonRpcResponseStream();
		using var serverInputStream = new RecordingStream();
		await using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 1, process: null, deferredServerOutputStream, serverInputStream, startListening: true);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		Task<TestValuePayload> requestTask = client.SendRequestAsync<TestValuePayload>(
			"example/echo",
			new EmptyParams(),
			CancellationToken.None);

		int requestId = await WaitForRequestIdAsync(serverInputStream).ConfigureAwait(false);

		deferredServerOutputStream.SetPayload(JsonRpcFrameTestHelper.BuildResultResponse(requestId, "{\"value\":42}"));

		TestValuePayload result = await requestTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.AreEqual(42, result.Value);
		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(1L, client.TransportGeneration);
	}

	private sealed class TestValuePayload
	{
		public int Value { get; init; }
	}

	[TestMethod]
	public async Task SendMethods_WithBlankMethod_ThrowArgumentException()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);

		await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
			await client.SendNotificationAsync(" ", parameters: null, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);

		await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
			await client.SendRequestAsync<object>(" ", new object(), CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task SendRequestAsync_WhenCancellationIsRequestedForAnInFlightRequest_CancelsTheWaitAndKeepsTheSessionReady()
	{
		using var serverOutputStream = new PendingReadStream();
		using var serverInputStream = new RecordingStream();
		await using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 7, process: null, serverOutputStream, serverInputStream, startListening: true);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		using var cancellationSource = new CancellationTokenSource();

		Task<JsonElement> requestTask = client.SendRequestAsync<JsonElement>(
			"workspace/configuration",
			new WorkspaceConfigurationParams([]),
			cancellationSource.Token);

		// Wait for the request to be written before canceling, so the cancellation reaches the in-flight
		// JSON-RPC request rather than the pre-send fast path.
		await WaitForRequestIdAsync(serverInputStream).ConfigureAwait(false);
		cancellationSource.Cancel();

		// The caller's token must bound the wait even though this server never answers: the request has to
		// complete as canceled instead of waiting for a response that will not arrive. The bounded wait fails
		// red with a timeout instead of hanging the suite when the token stops bounding the wait.
		bool canceled = false;

		try
		{
			await requestTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			canceled = true;
		}

		Assert.IsTrue(canceled, "The in-flight request must complete as canceled when the caller cancels it.");

		// The cancellation is still forwarded to the server so it can stop working on the request.
		await WaitForWrittenTextAsync(serverInputStream, "$/cancelRequest").ConfigureAwait(false);

		// A host-canceled request is not a transport failure, so the ready session must survive it.
		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(7L, client.TransportGeneration);

		// End the pending read so the session's background read loop can finish before the client's teardown.
		session.JsonRpc!.Dispose();
	}

	[TestMethod]
	public async Task SendRequestAsync_WithAnAlreadyCanceledToken_FailsCanceledWithoutInvalidatingTheTransport()
	{
		using var serverOutputStream = new PendingReadStream();
		await using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 7, process: null, serverOutputStream, Stream.Null, startListening: true);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		using var cancellationSource = new CancellationTokenSource();
		cancellationSource.Cancel();

		bool canceled = false;

		try
		{
			await client.SendRequestAsync<JsonElement>(
				"workspace/configuration",
				new WorkspaceConfigurationParams([]),
				cancellationSource.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			canceled = true;
		}

		Assert.IsTrue(canceled, "Expected the request to be canceled before it was sent.");
		Assert.IsTrue(client.IsReady);
		Assert.AreEqual(7L, client.TransportGeneration);

		// End the pending read so the session's background read loop can finish before the client's teardown.
		session.JsonRpc!.Dispose();
	}
}
