using Microsoft.Extensions.Logging;
using StreamJsonRpc;
using System.Diagnostics;

namespace Nickelony.LanguageServer.Client.Tests;

public partial class LanguageServerClientTests
{
	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void Dispose_WritesGracefulShutdownMessagesAndLogsGracefulAttempt()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using Process process = StartDisposableProcess();
		using var serverOutputStream = new PendingReadStream();
		using var serverInputStream = new RecordingStream();
		using var client = new LanguageServerClient([@"C:\Workspace"], process.StartInfo.FileName, s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		TransportSession session = CreateTransportSession(client, 1, process, serverOutputStream, serverInputStream, startListening: true);

		SetActiveSession(client, session);

		client.Dispose();

		string writtenPayload = serverInputStream.GetWrittenText();

		Assert.IsTrue(writtenPayload.Contains("\"method\":\"shutdown\"", StringComparison.Ordinal), writtenPayload);
		Assert.IsTrue(writtenPayload.Contains("\"method\":\"exit\"", StringComparison.Ordinal), writtenPayload);

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Info|", StringComparison.Ordinal)
			&& log.Contains("Attempting graceful shutdown", StringComparison.Ordinal)
			&& log.Contains("generation 1", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task DisposeAsync_WritesGracefulShutdownMessages()
	{
		using Process process = StartDisposableProcess();
		using var serverOutputStream = new PendingReadStream();
		using var serverInputStream = new RecordingStream();
		await using var client = new LanguageServerClient([@"C:\Workspace"], process.StartInfo.FileName, s_defaultClientOptions);
		TransportSession session = CreateTransportSession(client, 1, process, serverOutputStream, serverInputStream, startListening: true);

		SetActiveSession(client, session);

		await client.DisposeAsync().ConfigureAwait(false);

		string writtenPayload = serverInputStream.GetWrittenText();

		Assert.IsTrue(writtenPayload.Contains("\"method\":\"shutdown\"", StringComparison.Ordinal), writtenPayload);
		Assert.IsTrue(writtenPayload.Contains("\"method\":\"exit\"", StringComparison.Ordinal), writtenPayload);
	}

	[TestMethod]
	public async Task Dispose_AndDisposeAsync_DisposeStartLockAfterCleanup()
	{
		for (int i = 0; i < 2; i++)
		{
			var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
			SemaphoreSlim startLock = client.StartLock;

			if (i == 0)
				client.Dispose();
			else
				await client.DisposeAsync().ConfigureAwait(false);

			Assert.ThrowsExactly<ObjectDisposedException>(() => startLock.Wait(0));
		}
	}

	[TestMethod]
	public async Task DisposeAsync_WhenAnotherCallerOwnsTeardown_CompletesOnlyAfterTeardownFinishes()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		var queuedCleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		client.TransportHost.QueuedFailedSessionCleanupTask = queuedCleanup.Task;

		// The first caller owns teardown and blocks on the queued cleanup until this test completes it.
		Task firstDisposeTask = client.DisposeAsync().AsTask();
		Task completedTask = await Task.WhenAny(firstDisposeTask, Task.Delay(TestPolling.AbsenceWindow)).ConfigureAwait(false);

		Assert.AreNotSame(firstDisposeTask, completedTask);

		// A later caller must observe the teardown the first caller started instead of returning while it continues.
		Task secondDisposeTask = client.DisposeAsync().AsTask();
		completedTask = await Task.WhenAny(secondDisposeTask, Task.Delay(TestPolling.AbsenceWindow)).ConfigureAwait(false);

		Assert.AreNotSame(secondDisposeTask, completedTask, "A later dispose caller must wait for the teardown that is still running.");

		queuedCleanup.TrySetResult(true);

		await firstDisposeTask.ConfigureAwait(false);
		await secondDisposeTask.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task Dispose_WhenAnotherCallerOwnsTeardown_BlocksUntilTeardownFinishes()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		var queuedCleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		client.TransportHost.QueuedFailedSessionCleanupTask = queuedCleanup.Task;

		// The asynchronous caller becomes the teardown owner before the synchronous caller is started on the pool,
		// so the synchronous caller is deterministically a later caller.
		Task firstDisposeTask = client.DisposeAsync().AsTask();
		Task secondDisposeTask = Task.Run(client.Dispose);
		Task completedTask = await Task.WhenAny(secondDisposeTask, Task.Delay(TestPolling.AbsenceWindow)).ConfigureAwait(false);

		Assert.AreNotSame(secondDisposeTask, completedTask, "A later synchronous dispose caller must wait for the teardown that is still running.");

		queuedCleanup.TrySetResult(true);

		await firstDisposeTask.ConfigureAwait(false);
		await secondDisposeTask.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task StartAsync_DisposeDuringStartupWait_ReturnsFalseWithoutObjectDisposedException()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
		SemaphoreSlim startLock = client.StartLock;

		startLock.Wait();

		try
		{
			Task<bool> startTask = client.StartAsync(CancellationToken.None);

			// StartAsync runs synchronously up to the startup-gate wait, so disposal here deterministically races a
			// blocked startup attempt without an extra delay.
			client.Dispose();

			Assert.IsFalse(await startTask.ConfigureAwait(false));
		}
		finally
		{
			startLock.Release();
		}
	}

	[TestMethod]
	public async Task StartAsync_WhenDisposalAlreadyBegan_DoesNotReportReadySuccess()
	{
		var releaseTeardown = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		await using var client = new LanguageServerClient(
			[@"C:\Workspace"],
			"example-language-server.exe",
			s_defaultClientOptions,
			logger: null,
			testHooks: new ClientTestHooks
			{
				BeforeTeardown = () => releaseTeardown.Task
			});
		TransportSession session = CreateTransportSession(client, 1, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		// Teardown is parked at its first step, so the client is disposed while the active session and the readiness
		// this test asserts on are still in place.
		Task disposeTask = client.DisposeAsync().AsTask();

		Assert.IsTrue(client.IsReady, "The regression test expects readiness to still be visible immediately after disposal begins.");

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () =>
			await client.StartAsync(CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);

		releaseTeardown.TrySetResult(true);
		await disposeTask.ConfigureAwait(false);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task StartAsync_WhenDisposalWinsBeforeTransportAttachment_TerminatesSpawnedProcess()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		var processStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowStartupToContinue = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int? startedProcessId = null;

		var options = LanguageServerClientOptions.Default with
		{
			ShutdownRequestTimeout = TimeSpan.FromMilliseconds(1500),
			DisposeWaitTimeout = TimeSpan.FromMilliseconds(2000),
			// ping keeps the child alive without reading stdin, so only forced termination can end it.
			ServerArguments = ["/c", "ping 127.0.0.1 -n 30 > nul"],
			Transport = new TestLanguageServerTransport(async (context, cancellationToken) =>
			{
				// The real stdio transport spawns the child; the hold keeps startup inside the connection attempt,
				// which is the window disposal has to win.
				ILanguageServerConnection connection = await StdioLanguageServerTransport.Default
					.ConnectAsync(context, cancellationToken)
					.ConfigureAwait(false);

				startedProcessId = connection.Process?.Id;
				processStarted.TrySetResult(true);

				await allowStartupToContinue.Task.ConfigureAwait(false);

				return connection;
			})
		};

		await using var client = new LanguageServerClient(
			[@"C:\Workspace"],
			Path.Combine(Environment.SystemDirectory, "cmd.exe"),
			options,
			logScope.CreateLogger<LanguageServerClient>());

		Task<bool> startTask = client.StartAsync(CancellationToken.None);

		await processStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		// Disposal wins the race while the transport is still connecting: the client adopts the returned connection,
		// never configures it, and must terminate the live process instead of leaving it running.
		Task disposeTask = client.DisposeAsync().AsTask();
		allowStartupToContinue.TrySetResult(true);

		Assert.IsFalse(await startTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
		Assert.IsNotNull(startedProcessId);
		Assert.IsTrue(await WaitForProcessExitAsync(startedProcessId.Value).ConfigureAwait(false));

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Info|", StringComparison.Ordinal)
			&& log.Contains("because disposal reached the session before its transport was attached", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));

		await disposeTask.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task StartAsync_WhenTheTransportCannotConnect_ReportsTheFailureWithoutActivatingASession()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		var expectedException = new InvalidOperationException("Simulated transport connection failure.");

		var options = LanguageServerClientOptions.Default with
		{
			// A transport that cannot reach the server owns its own cleanup; the client only has to report the failure.
			Transport = new TestLanguageServerTransport((_, _) => throw expectedException)
		};

		using var client = new LanguageServerClient(
			[@"C:\Workspace"],
			"example-language-server.exe",
			options,
			logScope.CreateLogger<LanguageServerClient>());

		bool started = await client.StartAsync(CancellationToken.None).ConfigureAwait(false);

		Assert.IsFalse(started);
		Assert.IsFalse(client.IsReady);
		Assert.IsNull(client.CapabilityStore.ActiveSession);
		Assert.AreSame(expectedException, client.LastStartupException);

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("Failed to start the language server", StringComparison.Ordinal)
			&& log.Contains("stage='startup'", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task StartAsync_WhenCallerCancelsWhileTheTransportConnects_RethrowsWithoutActivatingASession()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var startupCancellation = new CancellationTokenSource();
		var processStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int? startedProcessId = null;

		var options = LanguageServerClientOptions.Default with
		{
			// ping keeps the child alive without reading stdin, so only termination can end it.
			ServerArguments = ["/c", "ping 127.0.0.1 -n 30 > nul"],
			Transport = new TestLanguageServerTransport((context, cancellationToken) => HoldSpawnedConnectionAsync(
				context,
				cancellationToken,
				processStarted,
				processId => startedProcessId = processId))
		};

		using var client = new LanguageServerClient(
			[@"C:\Workspace"],
			Path.Combine(Environment.SystemDirectory, "cmd.exe"),
			options,
			logScope.CreateLogger<LanguageServerClient>());

		Task<bool> startTask = client.StartAsync(startupCancellation.Token);

		await processStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		startupCancellation.Cancel();

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
			await startTask.ConfigureAwait(false)).ConfigureAwait(false);

		Assert.IsNotNull(startedProcessId);
		Assert.IsTrue(await WaitForProcessExitAsync(startedProcessId.Value).ConfigureAwait(false));
		Assert.IsFalse(client.IsReady);
		Assert.IsNull(client.CapabilityStore.ActiveSession);

		// A canceled connection attempt must not be reported as a startup failure with a stale process warning.
		Assert.IsFalse(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("Startup cleanup", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task StartAsync_WhenClientIsDisposedWhileTheTransportConnects_ReturnsFalseWithoutActivatingASession()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		var processStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int? startedProcessId = null;

		var options = LanguageServerClientOptions.Default with
		{
			// ping keeps the child alive without reading stdin, so only termination can end it.
			ServerArguments = ["/c", "ping 127.0.0.1 -n 30 > nul"],
			Transport = new TestLanguageServerTransport((context, cancellationToken) => HoldSpawnedConnectionAsync(
				context,
				cancellationToken,
				processStarted,
				processId => startedProcessId = processId))
		};

		using var client = new LanguageServerClient(
			[@"C:\Workspace"],
			Path.Combine(Environment.SystemDirectory, "cmd.exe"),
			options,
			logScope.CreateLogger<LanguageServerClient>());

		Task<bool> startTask = client.StartAsync(CancellationToken.None);

		await processStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		client.Dispose();

		Assert.IsFalse(await startTask.ConfigureAwait(false));
		Assert.IsNotNull(startedProcessId);
		Assert.IsTrue(await WaitForProcessExitAsync(startedProcessId.Value).ConfigureAwait(false));
		Assert.IsFalse(client.IsReady);
		Assert.IsNull(client.CapabilityStore.ActiveSession);

		// A disposed client must not report the disposal cancellation as a startup failure.
		Assert.IsFalse(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("Startup cleanup", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	/// <summary>
	/// Opens a real stdio connection and holds it open until the client cancels the attempt.
	/// </summary>
	/// <remarks>
	/// A transport owns everything it created until it returns the connection, so a canceled attempt terminates the
	/// process it spawned itself instead of handing a live orphan to the client.
	/// </remarks>
	/// <param name="context">The transport context for the connection attempt.</param>
	/// <param name="cancellationToken">The token whose cancellation ends the hold.</param>
	/// <param name="processStarted">Signalled once the process was spawned.</param>
	/// <param name="recordProcessId">Records the spawned process id for the assertions.</param>
	/// <returns>The connection when the hold completed without cancellation.</returns>
	private static async Task<ILanguageServerConnection> HoldSpawnedConnectionAsync(
		LanguageServerTransportContext context,
		CancellationToken cancellationToken,
		TaskCompletionSource<bool> processStarted,
		Action<int> recordProcessId)
	{
		ILanguageServerConnection? connection = null;

		try
		{
			connection = await StdioLanguageServerTransport.Default.ConnectAsync(context, cancellationToken).ConfigureAwait(false);

			if (connection.Process is not null)
				recordProcessId(connection.Process.Id);

			processStarted.TrySetResult(true);

			await WaitForCancellationAsync(cancellationToken).ConfigureAwait(false);

			return connection;
		}
		catch
		{
			if (connection is not null)
			{
				try
				{
					connection.Process?.Kill(true);
				}
				catch
				{
					// The process may already be gone; the connection still has to be released.
				}

				await connection.DisposeAsync().ConfigureAwait(false);
			}

			throw;
		}
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task Dispose_WhenShutdownRequestTimesOut_LogsForcedTermination()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using Process process = StartDisposableProcess();
		int processId = process.Id;
		using var serverOutputStream = new PendingReadStream();
		using var serverInputStream = new RecordingStream();

		// The disposal budget must cover the shutdown-request timeout plus the exit grace period and the
		// kill, so the forced-termination warning is emitted before the budget is exhausted.
		using var client = new LanguageServerClient([@"C:\Workspace"], process.StartInfo.FileName, LanguageServerClientOptions.Default with
		{
			ShutdownRequestTimeout = TimeSpan.FromMilliseconds(1500),
			DisposeWaitTimeout = TimeSpan.FromSeconds(5)
		}, logScope.CreateLogger<LanguageServerClient>());

		TransportSession session = CreateTransportSession(client, 7, process, serverOutputStream, serverInputStream, startListening: true);

		SetActiveSession(client, session);
		RecordStandardErrorLine(session, "The example language server did not respond to shutdown.");

		client.Dispose();

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("did not acknowledge shutdown", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("generation 7", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("forcing process termination", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("generation 7", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("recent language server stderr", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("The example language server did not respond to shutdown.", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));

		// The warning must correspond to a real forced termination, not just a logged line.
		Assert.IsTrue(await WaitForProcessExitAsync(processId).ConfigureAwait(false));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task Dispose_WhenShutdownAcknowledgesWithinConfiguredBudget_DoesNotLogTimeoutOrForcedTermination()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		// Use a long-lived process and a persistent response stream so the graceful acknowledgment path runs to
		// completion; a short-lived process or an ending stream would let the assertions pass vacuously.
		using Process process = StartDisposableProcess();
		int processId = process.Id;

		using var serverOutputStream = new DeferredPersistentJsonRpcResponseStream();
		using var serverInputStream = new RecordingStream();

		using var client = new LanguageServerClient([@"C:\Workspace"], process.StartInfo.FileName, LanguageServerClientOptions.Default with
		{
			ShutdownRequestTimeout = TimeSpan.FromSeconds(5),
			DisposeWaitTimeout = TimeSpan.FromSeconds(1)
		}, logScope.CreateLogger<LanguageServerClient>());

		TransportSession session = CreateTransportSession(client, 8, process, serverOutputStream, serverInputStream, startListening: true);

		SetActiveSession(client, session);

		Task disposeTask = Task.Run(client.Dispose);

		int shutdownRequestId = await WaitForRequestIdAsync(serverInputStream).ConfigureAwait(false);
		serverOutputStream.SetPayload(JsonRpcFrameTestHelper.BuildResultResponse(shutdownRequestId, resultJson: "null"));

		await disposeTask.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

		Assert.IsTrue(await WaitForProcessExitAsync(processId).ConfigureAwait(false));
		Assert.IsTrue(serverInputStream.GetWrittenText().Contains("\"exit\"", StringComparison.Ordinal),
			"Expected the exit notification to be sent after the shutdown acknowledgment.");

		Assert.IsFalse(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("did not acknowledge shutdown", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void Dispose_WhenShutdownRequestTimesOut_UsesConfiguredTimeoutInLog()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using Process process = StartDisposableProcess();
		using var serverOutputStream = new PendingReadStream();
		using var serverInputStream = new RecordingStream();

		using var client = new LanguageServerClient([@"C:\Workspace"], process.StartInfo.FileName, LanguageServerClientOptions.Default with
		{
			ShutdownRequestTimeout = TimeSpan.FromMilliseconds(50)
		}, logScope.CreateLogger<LanguageServerClient>());

		TransportSession session = CreateTransportSession(client, 9, process, serverOutputStream, serverInputStream, startListening: true);

		SetActiveSession(client, session);

		client.Dispose();

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("did not acknowledge shutdown", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("within 50 ms", StringComparison.Ordinal)
			&& log.Contains("generation 9", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public async Task DisposeStartLockAsync_WhenStartupGateStaysBusy_LogsTimeoutWithoutDisposingGate()
	{
		using var logScope = new TestLoggerScope(LogLevel.Warning);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		SemaphoreSlim startLock = client.StartLock;
		bool reacquiredStartLock = false;

		startLock.Wait();

		try
		{
			await client.DisposeStartLockAsync(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
		}
		finally
		{
			startLock.Release();
		}

		try
		{
			reacquiredStartLock = startLock.Wait(0);
			Assert.IsTrue(reacquiredStartLock);
		}
		finally
		{
			if (reacquiredStartLock)
				startLock.Release();
		}

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("startup gate did not become available", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("within 50 ms", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task StartAsync_WhenCanceledDuringHandshake_DetachesPublishedSessionAndLeavesClientNotReady()
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

		startupCancellation.Cancel();

		await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await startTask.ConfigureAwait(false)).ConfigureAwait(false);

		Assert.IsFalse(client.IsReady);
		Assert.IsNull(client.CapabilityStore.ActiveSession);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task StartAsync_WhenInitializationTimesOut_UsesConfiguredTimeoutAndLeavesClientNotReady()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		var initializeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var client = new LanguageServerClient(
			[@"C:\Workspace"],
			Path.Combine(Environment.SystemDirectory, "cmd.exe"),
			LanguageServerClientOptions.Default with
			{
				InitializeTimeout = TimeSpan.FromMilliseconds(50)
			},
			logScope.CreateLogger<LanguageServerClient>(),
			testHooks: new ClientTestHooks
			{
				BeforeInitializeRequest = async cancellationToken =>
				{
					initializeStarted.TrySetResult(true);
					await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
				}
			});

		Task<bool> startTask = client.StartAsync(CancellationToken.None);

		await initializeStarted.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

		bool started = await startTask.ConfigureAwait(false);

		Assert.IsFalse(started);
		Assert.IsFalse(client.IsReady);

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("did not complete initialization within 50 ms", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void JsonRpc_Disconnected_UnexpectedDisconnect_LogsRecentStderrContext()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions, logScope.CreateLogger<LanguageServerClient>());
		TransportSession session = CreateTransportSession(client, 11, process: null, Stream.Null, Stream.Null);

		SetActiveSession(client, session);
		SetReadyState(client, true);
		RecordStandardErrorLine(session, "The example language server handshake failed near initialize.");

		client.TransportHost.HandleJsonRpcDisconnected(
			session,
			new JsonRpcDisconnectedEventArgs("stream closed", DisconnectedReason.StreamError));

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("disconnected unexpectedly", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("workspace", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("recreate the session", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("generation 11", StringComparison.OrdinalIgnoreCase)),
			string.Join(Environment.NewLine, logScope.Logs));

		Assert.IsTrue(logScope.Logs.Any(log => log.StartsWith("Warn|", StringComparison.Ordinal)
			&& log.Contains("recent language server stderr", StringComparison.OrdinalIgnoreCase)
			&& log.Contains("The example language server handshake failed near initialize.", StringComparison.Ordinal)),
			string.Join(Environment.NewLine, logScope.Logs));
	}

	[TestMethod]
	public void LanguageServerClientOptions_RejectsInvalidTimeoutValues()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LanguageServerClientOptions.Default with
		{
			InitializeTimeout = TimeSpan.Zero
		});

		ArgumentOutOfRangeException infiniteTimeoutException = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LanguageServerClientOptions.Default with
		{
			ShutdownRequestTimeout = Timeout.InfiniteTimeSpan
		});

		StringAssert.Contains(infiniteTimeoutException.Message, "Infinite timeouts are not supported");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LanguageServerClientOptions.Default with
		{
			DisposeWaitTimeout = TimeSpan.FromMilliseconds(-1)
		});
	}

	[TestMethod]
	public void LanguageServerClientOptions_RejectsTimeoutsAboveThePlatformTimerLimit()
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LanguageServerClientOptions.Default with
		{
			InitializeTimeout = TimeSpan.FromDays(50)
		});

		StringAssert.Contains(exception.Message, "platform limit");
	}

	[TestMethod]
	public void LanguageServerClientOptions_ServerWorkingDirectory_DefaultsToNullAndRejectsBlankValues()
	{
		Assert.IsNull(LanguageServerClientOptions.Default.ServerWorkingDirectory);

		Assert.ThrowsExactly<ArgumentException>(() => LanguageServerClientOptions.Default with
		{
			ServerWorkingDirectory = " "
		});

		Assert.AreEqual(@"C:\Workspace", (LanguageServerClientOptions.Default with
		{
			ServerWorkingDirectory = @"C:\Workspace"
		}).ServerWorkingDirectory);
	}

	[TestMethod]
	public async Task Dispose_ConcurrentSyncAndAsyncCalls_DoNotFault()
	{
		// Repeat to cover scheduling interleavings between the three dispose callers; each iteration asserts
		// deterministic outcomes and holds no timing assumption.
		for (int i = 0; i < 25; i++)
		{
			await using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", s_defaultClientOptions);
			TransportSession session = CreateTransportSession(client, i + 1, process: null, Stream.Null, Stream.Null, startListening: true);

			SetActiveSession(client, session);

			Task[] disposeTasks =
			[
				Task.Run(client.Dispose),
				Task.Run(async () => await client.DisposeAsync().ConfigureAwait(false)),
				Task.Run(client.Dispose)
			];

			await Task.WhenAll(disposeTasks).ConfigureAwait(false);

			await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
				client.SendNotificationAsync("workspace/didChangeConfiguration", new { settings = new { } }, CancellationToken.None))
				.ConfigureAwait(false);
		}
	}

	[TestMethod]
	[TestCategory(TestCategories.Performance)]
	public void Dispose_UsesOneOverallDisposeBudgetAcrossTeardownStages()
	{
		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", LanguageServerClientOptions.Default with
		{
			DisposeWaitTimeout = TimeSpan.FromMilliseconds(100)
		});

		TransportSession session = CreateTransportSession(client, 1, process: null, Stream.Null, Stream.Null);
		var pendingRpcCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var pendingStderrLoop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		SemaphoreSlim startLock = client.StartLock;

		session.RpcCompletionTask = pendingRpcCompletion.Task;
		session.StderrLoopTask = pendingStderrLoop.Task;
		SetActiveSession(client, session);

		startLock.Wait();
		var stopwatch = Stopwatch.StartNew();

		try
		{
			client.Dispose();
		}
		finally
		{
			stopwatch.Stop();

			try
			{
				startLock.Release();
			}
			catch (ObjectDisposedException)
			{ }
			catch (SemaphoreFullException)
			{ }
		}

		// Wall-clock bound: a generous multiple of the 100 ms budget, so it only fails red when teardown ignores
		// the shared budget entirely instead of measuring accurate timing.
		Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromMilliseconds(1000),
			$"Dispose should honor a single overall budget (100 ms), but took {stopwatch.Elapsed.TotalMilliseconds:F0} ms.");
	}

	[TestMethod]
	public async Task Dispose_WhenTeardownStageIsAbandonedOnBudgetTimeout_ObservesTheStageTask()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);
		var abandonedPump = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var client = new LanguageServerClient([@"C:\Workspace"], "example-language-server.exe", LanguageServerClientOptions.Default with
		{
			DisposeWaitTimeout = TimeSpan.FromMilliseconds(100)
		}, logScope.CreateLogger<LanguageServerClient>());

		// The callback dispatcher never completes, so its teardown stage is abandoned once the budget elapses.
		client.DiagnosticsRouter.CallbackPumpTask = abandonedPump.Task;

		client.Dispose();

		// A fault that arrives after the stage was abandoned must be observed and logged instead of surfacing as an
		// unobserved task exception.
		abandonedPump.TrySetException(new IOException("Simulated abandoned teardown stage failure."));

		await TestPolling.UntilAsync(
			() => logScope.Logs.Any(log => log.Contains("abandoned language server transport task", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("Simulated abandoned teardown stage failure.", StringComparison.Ordinal)),
			TimeSpan.FromSeconds(5),
			"The abandoned teardown stage failure should be logged at debug level.").ConfigureAwait(false);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task StartAsync_InterleavedWithDisposeAsync_DoesNotLeaveReadyClientReachable()
	{
		// Repeat to cover interleavings between startup and dispose; the real cmd.exe process and the 5 s waits are
		// upper bounds that only fail red when startup or teardown never completes.
		for (int i = 0; i < 10; i++)
		{
			var sessionActivated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			await using var client = new LanguageServerClient(
				[@"C:\Workspace"],
				Path.Combine(Environment.SystemDirectory, "cmd.exe"),
				s_defaultClientOptions,
				null,
				testHooks: new ClientTestHooks
				{
					SessionActivated = cancellationToken => WaitForStartupCancellationAsync(sessionActivated, cancellationToken)
				});

			Task<bool> startTask = client.StartAsync(CancellationToken.None);

			await sessionActivated.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

			await client.DisposeAsync().ConfigureAwait(false);

			Assert.IsFalse(await startTask.ConfigureAwait(false));
			Assert.IsFalse(client.IsReady);
			Assert.ThrowsExactly<ObjectDisposedException>(() => client.StartLock.Wait(0));

			await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
				client.SendNotificationAsync("workspace/didChangeConfiguration", new { settings = new { } }, CancellationToken.None))
				.ConfigureAwait(false);
		}
	}
}
