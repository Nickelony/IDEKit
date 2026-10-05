using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace Nickelony.LanguageServer.Client.Tests;

public sealed partial class LanguageServerClientTests
{
	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void Dispose_WritesGracefulShutdownMessagesAndLogsGracefulAttempt()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using Process process = ProcessTestHelpers.StartDisposableProcess();

		using var serverOutputStream = new GatedReadStream();
		using var serverInputStream = new RecordingStream();

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: new StdioLanguageServerTransport(process.StartInfo.FileName),
			options: s_defaultClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process,
			serverOutputStream,
			serverInputStream,
			startListening: true
		);

		SetActiveSession(client, session);

		client.Dispose();

		string writtenPayload = serverInputStream.GetWrittenText();

		Assert.IsTrue(writtenPayload.Contains("\"method\":\"shutdown\"", StringComparison.Ordinal), writtenPayload);
		Assert.IsTrue(writtenPayload.Contains("\"method\":\"exit\"", StringComparison.Ordinal), writtenPayload);

		Assert.Contains(
			log =>
				log.StartsWith("Info|", StringComparison.Ordinal)
				&& log.Contains("Attempting graceful shutdown", StringComparison.Ordinal)
				&& log.Contains("generation 1", StringComparison.OrdinalIgnoreCase),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task DisposeAsync_WritesGracefulShutdownMessages()
	{
		using Process process = ProcessTestHelpers.StartDisposableProcess();

		using var serverOutputStream = new GatedReadStream();
		using var serverInputStream = new RecordingStream();

		await using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: new StdioLanguageServerTransport(process.StartInfo.FileName),
			hooks: null,
			options: s_defaultClientOptions
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process,
			serverOutputStream,
			serverInputStream,
			startListening: true
		);

		SetActiveSession(client, session);

		await client.DisposeAsync().ConfigureAwait(false);

		string writtenPayload = serverInputStream.GetWrittenText();

		Assert.IsTrue(writtenPayload.Contains("\"method\":\"shutdown\"", StringComparison.Ordinal), writtenPayload);
		Assert.IsTrue(writtenPayload.Contains("\"method\":\"exit\"", StringComparison.Ordinal), writtenPayload);
	}

	[TestMethod]
	public async Task Dispose_AndDisposeAsync_CancelTheLifetimeToken()
	{
		for (int i = 0; i < 2; i++)
		{
			var client = new LanguageServerClient(
				workspaceRootDirectoryPaths: [TestPaths.Root],
				transport: s_exampleTransport,
				hooks: null,
				options: s_defaultClientOptions
			);

			CancellationToken lifetimeToken = client.Composition.Lifetime.Token;

			Assert.IsFalse(lifetimeToken.IsCancellationRequested);

			if (i == 0)
				client.Dispose();
			else
				await client.DisposeAsync().ConfigureAwait(false);

			Assert.IsTrue(client.Composition.Lifetime.IsDisposed);
			Assert.IsTrue(lifetimeToken.IsCancellationRequested);
		}
	}

	[TestMethod]
	public async Task Dispose_AndDisposeAsync_DisposeStartLockAfterCleanup()
	{
		for (int i = 0; i < 2; i++)
		{
			var client = new LanguageServerClient(
				workspaceRootDirectoryPaths: [TestPaths.Root],
				transport: s_exampleTransport,
				hooks: null,
				options: s_defaultClientOptions
			);

			SemaphoreSlim startLock = client.Composition.StartLock;

			if (i == 0)
				client.Dispose();
			else
				await client.DisposeAsync().ConfigureAwait(false);

			Assert.ThrowsExactly<ObjectDisposedException>(() => startLock.Wait(0, TestContext.CancellationToken));
		}
	}

	[TestMethod]
	public async Task DisposeAsync_WhenAnotherCallerOwnsTeardown_CompletesOnlyAfterTeardownFinishes()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		var queuedCleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		FailedSessionTestHelpers.QueueBlockedDisposal(client.Composition.FailedSessionDispatcher, queuedCleanup.Task);

		// The first caller owns teardown and blocks on the queued cleanup until this test completes it.
		Task firstDisposeTask = client.DisposeAsync().AsTask();

		Task completedTask = await Task.WhenAny(firstDisposeTask, Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreNotSame(firstDisposeTask, completedTask);

		// A later caller must observe the teardown the first caller started instead of returning while it continues.
		Task secondDisposeTask = client.DisposeAsync().AsTask();

		completedTask = await Task.WhenAny(secondDisposeTask, Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreNotSame(secondDisposeTask, completedTask, "A later dispose caller must wait for the teardown that is still running.");

		queuedCleanup.TrySetResult(true);

		await firstDisposeTask.ConfigureAwait(false);
		await secondDisposeTask.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task Dispose_WhenAnotherCallerOwnsTeardown_BlocksUntilTeardownFinishes()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		var queuedCleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		FailedSessionTestHelpers.QueueBlockedDisposal(client.Composition.FailedSessionDispatcher, queuedCleanup.Task);

		// The asynchronous caller becomes the teardown owner before the synchronous caller is started on the pool,
		// so the synchronous caller is deterministically a later caller.
		Task firstDisposeTask = client.DisposeAsync().AsTask();
		var secondDisposeTask = Task.Run(client.Dispose, TestContext.CancellationToken);

		Task completedTask = await Task.WhenAny(secondDisposeTask, Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreNotSame(secondDisposeTask, completedTask, "A later synchronous dispose caller must wait for the teardown that is still running.");

		queuedCleanup.TrySetResult(true);

		await firstDisposeTask.ConfigureAwait(false);
		await secondDisposeTask.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_DisposeDuringStartupWait_ReturnsFalseWithoutObjectDisposedException()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions
		);

		SemaphoreSlim startLock = client.Composition.StartLock;

		startLock.Wait(TestContext.CancellationToken);

		try
		{
			Task<bool> startTask = client.EnsureStartedAsync(CancellationToken.None);

			// EnsureStartedAsync runs synchronously up to the startup-gate wait, so disposal here deterministically races a
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
	public async Task EnsureStartedAsync_WhenDisposalAlreadyBegan_DoesNotReportReadySuccess()
	{
		var releaseTeardown = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		await using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,
			options: s_defaultClientOptions,
			logger: null,
			testHooks: new LanguageServerClientTestHooks { BeforeTeardown = () => releaseTeardown.Task }
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		// Teardown is parked at its first step, so the client is disposed while the active session and the readiness
		// this test asserts on are still in place.
		Task disposeTask = client.DisposeAsync().AsTask();

		Assert.IsTrue(client.IsReady, "Readiness must still be visible immediately after disposal begins.");

		await Assert
			.ThrowsExactlyAsync<ObjectDisposedException>(async () => await client.EnsureStartedAsync(CancellationToken.None).ConfigureAwait(false))
			.ConfigureAwait(false);

		releaseTeardown.TrySetResult(true);
		await disposeTask.ConfigureAwait(false);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task EnsureStartedAsync_WhenDisposalWinsBeforeJsonRpcAttachment_TerminatesSpawnedProcess()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		var processStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowStartupToContinue = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int? startedProcessId = null;

		LanguageServerClientOptions options = LanguageServerClientOptions.Default with
		{
			ShutdownRequestTimeout = TimeSpan.FromMilliseconds(1500),
			DisposeWaitTimeout = TimeSpan.FromMilliseconds(2000),
		};

		StdioLanguageServerTransport stdioTransport = CreatePingTransport();

		var transport = new RecordingLanguageServerTransport(
			async (request, cancellationToken) =>
			{
				// The real stdio transport spawns the child; the hold keeps startup inside the connection attempt,
				// which is the window disposal has to win.
				ILanguageServerConnection connection = await stdioTransport.ConnectAsync(request, cancellationToken).ConfigureAwait(false);

				startedProcessId = connection.Process?.Id;
				processStarted.TrySetResult(true);

				await allowStartupToContinue.Task.ConfigureAwait(false);

				return connection;
			}
		);

		await using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport,
			hooks: null,
			options,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		Task<bool> startTask = client.EnsureStartedAsync(CancellationToken.None);

		await processStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		// Disposal wins the race while the transport is still connecting: the client adopts the returned connection,
		// never configures it, and must terminate the live process instead of leaving it running.
		Task disposeTask = client.DisposeAsync().AsTask();
		allowStartupToContinue.TrySetResult(true);

		Assert.IsFalse(await startTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));
		Assert.IsNotNull(startedProcessId);
		Assert.IsTrue(await ProcessTestHelpers.WaitForProcessExitAsync(startedProcessId.Value).ConfigureAwait(false));

		// The disposal-won-the-race path is observable only through the informational diagnostic; the exact
		// wording is not the contract, so the level is asserted through the documented helper.
		Assert.IsTrue(logger.HasEntryAtLeast(LogLevel.Information), string.Join(Environment.NewLine, logger.Logs));

		await disposeTask.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_WhenTheTransportCannotConnect_ReportsTheFailureWithoutActivatingASession()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		var expectedException = new InvalidOperationException("Simulated transport connection failure.");

		// A transport that cannot reach the server owns its own cleanup; the client only has to report the failure.
		var transport = new RecordingLanguageServerTransport((_, _) => throw expectedException);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport,
			hooks: null,
			options: LanguageServerClientOptions.Default,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		bool started = await client.EnsureStartedAsync(CancellationToken.None).ConfigureAwait(false);

		Assert.IsFalse(started);
		Assert.IsFalse(client.IsReady);
		Assert.IsNull(client.Composition.State.ActiveSession);
		Assert.AreSame(expectedException, client.LastStartupException);

		Assert.Contains(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("Failed to start the language server", StringComparison.Ordinal)
				&& log.Contains("stage='startup'", StringComparison.Ordinal),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task EnsureStartedAsync_WhenCallerCancelsWhileTheTransportConnects_RethrowsWithoutActivatingASession()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		using var startupCancellation = new CancellationTokenSource();
		var processStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int? startedProcessId = null;

		StdioLanguageServerTransport stdioTransport = CreatePingTransport();

		var transport = new RecordingLanguageServerTransport(
			(request, cancellationToken) =>
				HoldSpawnedConnectionAsync(
					stdioTransport,
					request,
					cancellationToken,
					processStarted,
					recordProcessId: processId => startedProcessId = processId
				)
		);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport,
			hooks: null,
			options: LanguageServerClientOptions.Default,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		Task<bool> startTask = client.EnsureStartedAsync(startupCancellation.Token);

		await processStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		startupCancellation.Cancel();

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await startTask.ConfigureAwait(false)).ConfigureAwait(false);

		Assert.IsNotNull(startedProcessId);
		Assert.IsTrue(await ProcessTestHelpers.WaitForProcessExitAsync(startedProcessId.Value).ConfigureAwait(false));
		Assert.IsFalse(client.IsReady);
		Assert.IsNull(client.Composition.State.ActiveSession);

		// A canceled connection attempt must not be reported as a startup failure with a stale process warning.
		Assert.DoesNotContain(
			log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("Startup cleanup", StringComparison.OrdinalIgnoreCase),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task EnsureStartedAsync_WhenClientIsDisposedWhileTheTransportConnects_ReturnsFalseWithoutActivatingASession()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		var processStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int? startedProcessId = null;

		StdioLanguageServerTransport stdioTransport = CreatePingTransport();

		var transport = new RecordingLanguageServerTransport(
			(request, cancellationToken) =>
				HoldSpawnedConnectionAsync(
					stdioTransport,
					request,
					cancellationToken,
					processStarted,
					recordProcessId: processId => startedProcessId = processId
				)
		);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport,
			hooks: null,
			options: LanguageServerClientOptions.Default,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		Task<bool> startTask = client.EnsureStartedAsync(CancellationToken.None);

		await processStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

		client.Dispose();

		Assert.IsFalse(await startTask.ConfigureAwait(false));
		Assert.IsNotNull(startedProcessId);
		Assert.IsTrue(await ProcessTestHelpers.WaitForProcessExitAsync(startedProcessId.Value).ConfigureAwait(false));
		Assert.IsFalse(client.IsReady);
		Assert.IsNull(client.Composition.State.ActiveSession);

		// A disposed client must not report the disposal cancellation as a startup failure.
		Assert.DoesNotContain(
			log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("Startup cleanup", StringComparison.OrdinalIgnoreCase),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	/// <summary>Builds a stdio transport whose child, a <c>ping</c> that never reads standard input, only termination can end.</summary>
	private static StdioLanguageServerTransport CreatePingTransport()
	{
		return new(
			Path.Combine(Environment.SystemDirectory, "cmd.exe"),
			new StdioLanguageServerTransportOptions { ServerArguments = ["/c", "ping 127.0.0.1 -n 30 > nul"] }
		);
	}

	/// <summary>Opens a real stdio connection and holds it open until the client cancels the attempt.</summary>
	/// <param name="stdioTransport">The transport that spawns the process.</param>
	/// <param name="request">The connect request for the connection attempt.</param>
	/// <param name="cancellationToken">The token whose cancellation ends the hold.</param>
	/// <param name="processStarted">Signaled once the process was spawned.</param>
	/// <param name="recordProcessId">Records the spawned process id for the assertions.</param>
	/// <returns>The connection, which the hold returns only if it ends without a cancellation; a canceled token throws instead.</returns>
	/// <remarks>
	/// A transport owns everything it created until it returns the connection, so a canceled attempt terminates the process it spawned
	/// itself instead of handing a live orphan to the client.
	/// </remarks>
	private static async Task<ILanguageServerConnection> HoldSpawnedConnectionAsync(
		StdioLanguageServerTransport stdioTransport,
		LanguageServerConnectRequest request,
		CancellationToken cancellationToken,
		TaskCompletionSource<bool> processStarted,
		Action<int> recordProcessId
	)
	{
		ILanguageServerConnection? connection = null;

		try
		{
			connection = await stdioTransport.ConnectAsync(request, cancellationToken).ConfigureAwait(false);

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
		using var logger = new RecordingLogger(LogLevel.Debug);

		using Process process = ProcessTestHelpers.StartDisposableProcess();
		int processId = process.Id;

		using var serverOutputStream = new GatedReadStream();
		using var serverInputStream = new RecordingStream();

		// The disposal budget must cover the shutdown-request timeout plus the exit grace period and the
		// kill, so the forced-termination warning is emitted before the budget is exhausted.
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: new StdioLanguageServerTransport(process.StartInfo.FileName),

			options: LanguageServerClientOptions.Default with
			{
				ShutdownRequestTimeout = TimeSpan.FromMilliseconds(1500),
				DisposeWaitTimeout = TimeSpan.FromSeconds(5),
			},

			logger: logger.CreateLogger<LanguageServerClient>()
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 7,
			process,
			serverOutputStream,
			serverInputStream,
			startListening: true
		);

		SetActiveSession(client, session);
		RecordStandardErrorLine(session, "The example language server did not respond to shutdown.");

		client.Dispose();

		Assert.Contains(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("did not acknowledge shutdown", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("generation 7", StringComparison.OrdinalIgnoreCase),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);

		Assert.Contains(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("forcing process termination", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("generation 7", StringComparison.OrdinalIgnoreCase),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);

		Assert.Contains(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("recent language server stderr", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("The example language server did not respond to shutdown.", StringComparison.Ordinal),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);

		// The warning must correspond to a real forced termination, not merely a logged line.
		Assert.IsTrue(await ProcessTestHelpers.WaitForProcessExitAsync(processId).ConfigureAwait(false));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task Dispose_WhenShutdownAcknowledgesWithinConfiguredBudget_DoesNotLogShutdownTimeout()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		// Use a long-lived process and a persistent response stream so the graceful acknowledgment path runs to
		// completion; a short-lived process or an ending stream would let the assertions pass vacuously.
		using Process process = ProcessTestHelpers.StartDisposableProcess();
		int processId = process.Id;

		using var serverOutputStream = new DeferredPersistentJsonRpcResponseStream();
		using var serverInputStream = new RecordingStream();

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: new StdioLanguageServerTransport(process.StartInfo.FileName),

			options: LanguageServerClientOptions.Default with
			{
				ShutdownRequestTimeout = TimeSpan.FromSeconds(5),
				DisposeWaitTimeout = TimeSpan.FromSeconds(1),
			},

			logger: logger.CreateLogger<LanguageServerClient>()
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 8,
			process,
			serverOutputStream,
			serverInputStream,
			startListening: true
		);

		SetActiveSession(client, session);

		var disposeTask = Task.Run(client.Dispose, TestContext.CancellationToken);

		int shutdownRequestId = await WaitForRequestIdAsync(serverInputStream).ConfigureAwait(false);
		serverOutputStream.SetPayload(JsonRpcFrameTestHelpers.BuildResultResponse(shutdownRequestId, resultJson: "null"));

		await disposeTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsTrue(await ProcessTestHelpers.WaitForProcessExitAsync(processId).ConfigureAwait(false));

		Assert.IsTrue(
			serverInputStream.GetWrittenText().Contains("\"exit\"", StringComparison.Ordinal),
			"Expected the exit notification to be sent after the shutdown acknowledgment."
		);

		Assert.DoesNotContain(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("did not acknowledge shutdown", StringComparison.OrdinalIgnoreCase),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void Dispose_WhenShutdownRequestTimesOut_UsesConfiguredTimeoutInLog()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		using Process process = ProcessTestHelpers.StartDisposableProcess();

		using var serverOutputStream = new GatedReadStream();
		using var serverInputStream = new RecordingStream();

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: new StdioLanguageServerTransport(process.StartInfo.FileName),

			options: LanguageServerClientOptions.Default with
			{
				ShutdownRequestTimeout = TimeSpan.FromMilliseconds(50),
			},

			logger: logger.CreateLogger<LanguageServerClient>()
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 9,
			process,
			serverOutputStream,
			serverInputStream,
			startListening: true
		);

		SetActiveSession(client, session);

		client.Dispose();

		Assert.Contains(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("did not acknowledge shutdown", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("within 50 ms", StringComparison.Ordinal)
				&& log.Contains("generation 9", StringComparison.OrdinalIgnoreCase),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public async Task DisposeStartLockAsync_WhenStartupGateStaysBusy_LogsTimeoutWithoutDisposingGate()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			options: s_defaultClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		SemaphoreSlim startLock = client.Composition.StartLock;
		bool reacquiredStartLock = false;

		startLock.Wait(TestContext.CancellationToken);

		try
		{
			await client.Composition.Teardown.DisposeStartLockAsync(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
		}
		finally
		{
			startLock.Release();
		}

		try
		{
			reacquiredStartLock = startLock.Wait(0, TestContext.CancellationToken);
			Assert.IsTrue(reacquiredStartLock);
		}
		finally
		{
			if (reacquiredStartLock)
				startLock.Release();
		}

		Assert.Contains(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("startup gate did not become available", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("within 50 ms", StringComparison.Ordinal),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task EnsureStartedAsync_WhenCanceledDuringHandshake_DetachesPublishedSessionAndLeavesClientNotReady()
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

		startupCancellation.Cancel();

		await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await startTask.ConfigureAwait(false)).ConfigureAwait(false);

		Assert.IsFalse(client.IsReady);
		Assert.IsNull(client.Composition.State.ActiveSession);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task EnsureStartedAsync_WhenInitializationTimesOut_UsesConfiguredTimeoutAndLeavesClientNotReady()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		var initializeStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: new StdioLanguageServerTransport(Path.Combine(Environment.SystemDirectory, "cmd.exe")),
			hooks: null,

			options: LanguageServerClientOptions.Default with
			{
				InitializeRequestTimeout = TimeSpan.FromMilliseconds(50),
			},

			logger: logger.CreateLogger<LanguageServerClient>(),

			testHooks: new LanguageServerClientTestHooks
			{
				BeforeInitializeRequest = async cancellationToken =>
				{
					initializeStarted.TrySetResult(true);
					await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
				},
			}
		);

		Task<bool> startTask = client.EnsureStartedAsync(CancellationToken.None);

		await initializeStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false);

		bool started = await startTask.ConfigureAwait(false);

		Assert.IsFalse(started);
		Assert.IsFalse(client.IsReady);

		Assert.Contains(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("did not complete initialization within 50 ms", StringComparison.OrdinalIgnoreCase),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public void JsonRpc_Disconnected_UnexpectedDisconnect_LogsRecentStandardErrorContext()
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
		RecordStandardErrorLine(session, "The example language server handshake failed near initialize.");

		client.Composition.Supervisor.HandleJsonRpcDisconnected(
			session,
			new JsonRpcDisconnectedEventArgs("stream closed", DisconnectedReason.StreamError)
		);

		Assert.Contains(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("disconnected unexpectedly", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("workspace", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("recreate the session", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("generation 11", StringComparison.OrdinalIgnoreCase),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);

		Assert.Contains(
			log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("recent language server stderr", StringComparison.OrdinalIgnoreCase)
				&& log.Contains("The example language server handshake failed near initialize.", StringComparison.Ordinal),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);
	}

	[TestMethod]
	public async Task Dispose_ConcurrentSyncAndAsyncCalls_DoNotFault()
	{
		// Repeat to cover scheduling interleavings between the three dispose callers; each iteration asserts
		// deterministic outcomes and holds no timing assumption.
		for (int i = 0; i < 25; i++)
		{
			await using var client = new LanguageServerClient(
				workspaceRootDirectoryPaths: [TestPaths.Root],
				transport: s_exampleTransport,
				hooks: null,
				options: s_defaultClientOptions
			);

			ConnectionSession session = CreateConnectionSession(
				client,
				generation: i + 1,
				process: null,
				serverOutputStream: Stream.Null,
				serverInputStream: Stream.Null,
				startListening: true
			);

			SetActiveSession(client, session);

			Task[] disposeTasks =
			[
				Task.Run(client.Dispose, TestContext.CancellationToken),
				Task.Run(async () => await client.DisposeAsync().ConfigureAwait(false), TestContext.CancellationToken),
				Task.Run(client.Dispose, TestContext.CancellationToken),
			];

			await Task.WhenAll(disposeTasks).ConfigureAwait(false);

			await Assert
				.ThrowsExactlyAsync<ObjectDisposedException>(() =>
					client.SendNotificationAsync("workspace/didChangeConfiguration", new { settings = new { } }, CancellationToken.None)
				)
				.ConfigureAwait(false);
		}
	}

	[TestMethod]
	[TestCategory(TestCategories.Performance)]
	public void Dispose_UsesOneOverallDisposeBudgetAcrossTeardownStages()
	{
		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks: null,

			options: LanguageServerClientOptions.Default with
			{
				DisposeWaitTimeout = TimeSpan.FromMilliseconds(100),
			}
		);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 1,
			process: null,
			serverOutputStream: Stream.Null,
			serverInputStream: Stream.Null
		);

		var pendingRpcCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var pendingStandardErrorLoop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		SemaphoreSlim startLock = client.Composition.StartLock;

		session.RpcCompletionTask = pendingRpcCompletion.Task;
		session.StandardErrorLoopTask = pendingStandardErrorLoop.Task;
		SetActiveSession(client, session);

		startLock.Wait(TestContext.CancellationToken);
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
			catch (ObjectDisposedException) { }
			catch (SemaphoreFullException) { }
		}

		// Wall-clock bound: a generous multiple of the 100 ms budget, so it only fails red when teardown ignores
		// the shared budget entirely instead of measuring accurate timing.
		Assert.IsLessThan(
			TimeSpan.FromMilliseconds(1000),
			stopwatch.Elapsed,
			$"Dispose should honor a single overall budget (100 ms), but took {stopwatch.Elapsed.TotalMilliseconds:F0} ms."
		);
	}

	[TestMethod]
	public async Task Dispose_WhenTeardownStageIsAbandonedOnBudgetTimeout_ObservesTheStageTask()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		var abandonedLoop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,

			options: LanguageServerClientOptions.Default with
			{
				DisposeWaitTimeout = TimeSpan.FromMilliseconds(100),
			},

			logger: logger.CreateLogger<LanguageServerClient>()
		);

		// The server-message dispatcher never completes, so its teardown stage is abandoned once the budget elapses.
		client.Composition.Router.ServerMessageDispatcherTask = abandonedLoop.Task;

		client.Dispose();

		// A fault that arrives after the stage was abandoned must be observed and logged instead of surfacing as an
		// unobserved task exception.
		abandonedLoop.TrySetException(new IOException("Simulated abandoned teardown stage failure."));

		await TestPolling
			.UntilAsync(
				() =>
					logger.Logs.Any(log =>
						log.Contains("abandoned language server connection task", StringComparison.OrdinalIgnoreCase)
						&& log.Contains("Simulated abandoned teardown stage failure.", StringComparison.Ordinal)
					),

				TimeSpan.FromSeconds(5),
				"The abandoned teardown stage failure should be logged at debug level."
			)
			.ConfigureAwait(false);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task EnsureStartedAsync_InterleavedWithDisposeAsync_DoesNotLeaveReadyClientReachable()
	{
		// Repeat to cover interleavings between startup and dispose. The connection is a stream stub with no child process, so the teardown
		// spends nothing of the disposal budget: a real process would spend most of it waiting for an exit that never comes after the
		// shutdown request, and a loaded machine would exhaust it and skip the disposal of the startup gate. The 5 s wait is an upper bound
		// that only fails red when startup never activates.
		for (int i = 0; i < 10; i++)
		{
			var sessionActivated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			await using var client = new LanguageServerClient(
				workspaceRootDirectoryPaths: [TestPaths.Root],

				transport: new RecordingLanguageServerTransport(
					(_, _) => Task.FromResult<ILanguageServerConnection>(new RecordingLanguageServerConnection(Stream.Null, Stream.Null))
				),

				hooks: null,
				options: s_defaultClientOptions,
				logger: null,

				testHooks: new LanguageServerClientTestHooks
				{
					SessionActivated = cancellationToken => WaitForStartupCancellationAsync(sessionActivated, cancellationToken),
				}
			);

			Task<bool> startTask = client.EnsureStartedAsync(CancellationToken.None);

			await sessionActivated.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

			await client.DisposeAsync().ConfigureAwait(false);

			Assert.IsFalse(await startTask.ConfigureAwait(false));
			Assert.IsFalse(client.IsReady);
			Assert.ThrowsExactly<ObjectDisposedException>(() => client.Composition.StartLock.Wait(0, TestContext.CancellationToken));

			await Assert
				.ThrowsExactlyAsync<ObjectDisposedException>(() =>
					client.SendNotificationAsync("workspace/didChangeConfiguration", new { settings = new { } }, CancellationToken.None)
				)
				.ConfigureAwait(false);
		}
	}
}
