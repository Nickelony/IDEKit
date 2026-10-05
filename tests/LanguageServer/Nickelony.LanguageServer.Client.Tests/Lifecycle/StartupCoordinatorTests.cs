using System.Diagnostics;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class StartupCoordinatorTests
{
	public TestContext TestContext { get; set; } = null!;

	private static ClientCollaboratorTestHost CreateHost(
		RecordingLanguageServerTransport transport,
		LanguageServerClientOptions? options = null,
		LanguageServerClientTestHooks? testHooks = null
	) => new(options: options, testHooks: testHooks, transport: transport);

	private static RecordingLanguageServerTransport CreateTransportWithoutConnection() =>
		new(static (_, _) => Task.FromResult<ILanguageServerConnection>(null!));

	/// <summary>
	/// A coordinator whose gate another attempt holds, whose last attempt failed, and whose state has an active session that is not ready,
	/// so a caller that waits at the gate can show what it leaves alone.
	/// </summary>
	private sealed class WaitingCallerScenario
	{
		public required ClientCollaboratorTestHost Host { get; init; }
		public required RecordingLanguageServerTransport Transport { get; init; }
		public required Exception FailureBeforeWait { get; init; }
		public required ConnectionSession ActiveSession { get; init; }

		public void AssertStartStateUntouched()
		{
			Assert.AreSame(FailureBeforeWait, Host.Startup.LastStartupException, "The failure of the attempt the caller waited behind is kept.");
			Assert.AreSame(ActiveSession, Host.State.ActiveSession, "The active session is kept.");
			Assert.AreEqual(0, ((RecordingLanguageServerConnection)ActiveSession.Connection).DisposeCount, "The active session is not disposed.");
			Assert.AreEqual(1, Transport.ConnectCount, "A caller that waited at the gate opens no connection.");
		}
	}

	private async Task<WaitingCallerScenario> CreateWaitingCallerScenarioAsync()
	{
		RecordingLanguageServerTransport transport = CreateTransportWithoutConnection();
		ClientCollaboratorTestHost host = CreateHost(transport);

		Assert.IsFalse(await host.Startup.EnsureStartedAsync(TestContext.CancellationToken).ConfigureAwait(false));

		Exception failure = host.Startup.LastStartupException ?? throw new AssertFailedException("The first attempt must fail.");
		ConnectionSession activeSession = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process: null);

		host.State.SetActiveSession(activeSession);

		await host.StartLock.WaitAsync(TestContext.CancellationToken).ConfigureAwait(false);

		return new WaitingCallerScenario
		{
			Host = host,
			Transport = transport,
			FailureBeforeWait = failure,
			ActiveSession = activeSession,
		};
	}

	[TestMethod]
	public async Task EnsureStartedAsync_WhenTheClientIsReady_ReturnsTrueWithoutConnecting()
	{
		RecordingLanguageServerTransport transport = CreateTransportWithoutConnection();
		ClientCollaboratorTestHost host = CreateHost(transport);

		host.MakeReady(ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process: null));

		Assert.IsTrue(await host.Startup.EnsureStartedAsync(TestContext.CancellationToken).ConfigureAwait(false));
		Assert.AreEqual(0, transport.ConnectCount);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_AfterTheLifetimeIsDisposed_ThrowsObjectDisposedException()
	{
		RecordingLanguageServerTransport transport = CreateTransportWithoutConnection();
		ClientCollaboratorTestHost host = CreateHost(transport);

		_ = host.Lifetime.BeginDisposeAsync(static () => Task.CompletedTask);

		await Assert
			.ThrowsExactlyAsync<ObjectDisposedException>(() => host.Startup.EnsureStartedAsync(TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreEqual(0, transport.ConnectCount);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_WhenTheTransportReturnsNoConnection_ReturnsFalseAndKeepsTheCause()
	{
		ClientCollaboratorTestHost host = CreateHost(CreateTransportWithoutConnection());

		Assert.IsFalse(await host.Startup.EnsureStartedAsync(TestContext.CancellationToken).ConfigureAwait(false));

		Assert.IsFalse(host.State.IsReady);
		Assert.IsNull(host.State.ActiveSession);

		InvalidOperationException cause = Assert.IsInstanceOfType<InvalidOperationException>(host.Startup.LastStartupException);

		Assert.Contains("returned no connection", cause.Message);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_WhenTheCallerCancelsWhileConnecting_ThrowsAndKeepsTheCancellation()
	{
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
		var connectReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		var transport = new RecordingLanguageServerTransport(
			async (_, cancellationToken) =>
			{
				connectReached.SetResult();
				await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
				return null!;
			}
		);

		ClientCollaboratorTestHost host = CreateHost(transport);
		Task<bool> startTask = host.Startup.EnsureStartedAsync(cancellation.Token);

		await connectReached.Task.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);
		await cancellation.CancelAsync().ConfigureAwait(false);

		await Assert.ThrowsAsync<OperationCanceledException>(() => startTask).ConfigureAwait(false);

		Assert.IsInstanceOfType<OperationCanceledException>(host.Startup.LastStartupException);
		Assert.IsFalse(host.State.IsReady);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_WhenTheCallerCancelsWhileWaitingForTheGate_ThrowsAndLeavesTheStartStateAlone()
	{
		WaitingCallerScenario scenario = await CreateWaitingCallerScenarioAsync().ConfigureAwait(false);

		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
		Task<bool> waitingTask = scenario.Host.Startup.EnsureStartedAsync(cancellation.Token);

		Assert.IsFalse(waitingTask.IsCompleted, "The caller must be waiting at the gate.");

		await cancellation.CancelAsync().ConfigureAwait(false);

		await Assert.ThrowsAsync<OperationCanceledException>(() => waitingTask).ConfigureAwait(false);

		scenario.AssertStartStateUntouched();
	}

	[TestMethod]
	public async Task EnsureStartedAsync_WhenTheLifetimeIsCanceledWhileWaitingForTheGate_ReturnsFalseAndLeavesTheStartStateAlone()
	{
		WaitingCallerScenario scenario = await CreateWaitingCallerScenarioAsync().ConfigureAwait(false);

		Task<bool> waitingTask = scenario.Host.Startup.EnsureStartedAsync(TestContext.CancellationToken);

		Assert.IsFalse(waitingTask.IsCompleted, "The caller must be waiting at the gate.");

		scenario.Host.Lifetime.Cancel();

		Assert.IsFalse(await waitingTask.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false));

		scenario.AssertStartStateUntouched();
	}

	[TestMethod]
	public async Task EnsureStartedAsync_WhenTheGateIsDisposedAfterTheLifetimeIsCanceled_ReturnsFalseAndLeavesTheStartStateAlone()
	{
		WaitingCallerScenario scenario = await CreateWaitingCallerScenarioAsync().ConfigureAwait(false);

		scenario.Host.Lifetime.Cancel();
		scenario.Host.StartLock.Dispose();

		Assert.IsFalse(
			await scenario
				.Host.Startup.EnsureStartedAsync(TestContext.CancellationToken)
				.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken)
				.ConfigureAwait(false)
		);

		scenario.AssertStartStateUntouched();
	}

	[TestMethod]
	public async Task EnsureStartedAsync_ConnectRequest_CarriesTheLoggerOfTheClient()
	{
		RecordingLanguageServerTransport transport = CreateTransportWithoutConnection();
		ClientCollaboratorTestHost host = CreateHost(transport);

		_ = await host.Startup.EnsureStartedAsync(TestContext.CancellationToken).ConfigureAwait(false);

		LanguageServerConnectRequest? request = transport.LastRequest;

		Assert.IsNotNull(request);
		Assert.AreSame(host.Logger, request.Logger);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_StartsTheServerMessageDispatcherWithTheFirstAttempt()
	{
		ClientCollaboratorTestHost host = CreateHost(CreateTransportWithoutConnection());

		Assert.AreSame(Task.CompletedTask, host.Router.ServerMessageDispatcherTask, "Constructing the collaborators starts no loop.");

		_ = await host.Startup.EnsureStartedAsync(TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreNotSame(Task.CompletedTask, host.Router.ServerMessageDispatcherTask);

		await host.Teardown.RunAsync().ConfigureAwait(false);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_WithAnActiveSessionThatIsNotReady_DetachesAndDisposesItBeforeConnecting()
	{
		ClientCollaboratorTestHost host = CreateHost(CreateTransportWithoutConnection());
		ConnectionSession previous = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process: null);

		host.State.SetActiveSession(previous);

		_ = await host.Startup.EnsureStartedAsync(TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsNull(host.State.ActiveSession);

		await ((RecordingLanguageServerConnection)previous.Connection)
			.Disposed.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken)
			.ConfigureAwait(false);
	}

	[TestMethod]
	public async Task EnsureStartedAsync_WhenTheLifetimeIsCanceledWhileConnecting_DisposesTheConnectionWithoutActivatingIt()
	{
		bool activated = false;
		ClientCollaboratorTestHost? host = null;
		RecordingLanguageServerConnection? connection = null;

		var transport = new RecordingLanguageServerTransport(
			(_, _) =>
			{
				host?.Lifetime.Cancel();
				connection = new RecordingLanguageServerConnection(Stream.Null, Stream.Null);
				return Task.FromResult<ILanguageServerConnection>(connection);
			}
		);

		host = CreateHost(
			transport,

			testHooks: new LanguageServerClientTestHooks
			{
				SessionActivated = _ =>
				{
					activated = true;
					return Task.CompletedTask;
				},
			}
		);

		Assert.IsFalse(await host.Startup.EnsureStartedAsync(TestContext.CancellationToken).ConfigureAwait(false));

		Assert.IsFalse(activated, "A session created after the lifetime was canceled is never activated.");
		Assert.IsNull(host.State.ActiveSession);
		Assert.IsNotNull(connection);

		await connection.Disposed.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task EnsureStartedAsync_WhenTheServerProcessAlreadyExited_ReturnsFalseBeforeTheHandshake()
	{
		using Process process = ProcessTestHelpers.StartProcessThatWritesToStandardError("exited");

		Assert.IsTrue(await ProcessTestHelpers.WaitForProcessExitAsync(process.Id).ConfigureAwait(false));

		bool handshakeStarted = false;
		var connection = new RecordingLanguageServerConnection(Stream.Null, Stream.Null, process);

		ClientCollaboratorTestHost host = CreateHost(
			new RecordingLanguageServerTransport((_, _) => Task.FromResult<ILanguageServerConnection>(connection)),

			testHooks: new LanguageServerClientTestHooks
			{
				SessionActivated = _ =>
				{
					handshakeStarted = true;
					return Task.CompletedTask;
				},
			}
		);

		Assert.IsFalse(await host.Startup.EnsureStartedAsync(TestContext.CancellationToken).ConfigureAwait(false));

		Assert.IsFalse(handshakeStarted, "A server that exited during startup is reported before the handshake begins.");
		Assert.IsNull(host.State.ActiveSession);

		await connection.Disposed.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);
	}
}
