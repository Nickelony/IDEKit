using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class ConnectionSessionFactoryTests
{
	public TestContext TestContext { get; set; } = null!;

	private static ConnectionSessionFactory CreateFactory(ClientCollaboratorTestHost host, RecordingFailureHandlers failures)
	{
		return new(
			context: host.Context,
			serverMessageRouter: host.Router,
			settingsSnapshotCache: host.SettingsCache,
			standardErrorReader: host.StandardErrorReader,
			handleProcessExited: failures.HandleProcessExited,
			handleJsonRpcDisconnected: failures.HandleJsonRpcDisconnected
		);
	}

	private static async Task AssertDisposedAsync(ConnectionSession session) =>
		await ((RecordingLanguageServerConnection)session.Connection).Disposed.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

	[TestMethod]
	public void Create_TakesTheNextGenerationAndOwnsTheConnection()
	{
		var host = new ClientCollaboratorTestHost();
		var connection = new RecordingLanguageServerConnection(new MemoryStream(), new MemoryStream());

		ConnectionSession first = host.SessionFactory.Create(connection);
		ConnectionSession second = host.SessionFactory.Create(new RecordingLanguageServerConnection(Stream.Null, Stream.Null));

		Assert.AreEqual(1, first.ConnectionGeneration);
		Assert.AreEqual(2, second.ConnectionGeneration);
		Assert.AreSame(connection, first.Connection);
		Assert.AreSame(connection.ReadStream, first.ReadStream);
		Assert.AreSame(connection.WriteStream, first.WriteStream);
		Assert.IsNull(host.State.ActiveSession, "Creating a session does not activate it.");
	}

	[TestMethod]
	public void Configure_WiresTheHandlerTheRpcTargetAndTheJsonRpcInstance()
	{
		var host = new ClientCollaboratorTestHost();
		ConnectionSession session = host.SessionFactory.Create(new RecordingLanguageServerConnection(Stream.Null, Stream.Null));

		host.SessionFactory.Configure(session);

		Assert.IsInstanceOfType<TolerantHeaderDelimitedMessageHandler>(session.MessageHandler);
		Assert.IsNotNull(session.RpcTarget);
		Assert.IsNotNull(session.JsonRpc);
		Assert.AreSame(session.JsonRpc.Completion, session.RpcCompletionTask);
		Assert.IsNotNull(session.ProcessExitedHandler);

		session.JsonRpc.Dispose();
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task Configure_ForASessionWithAProcess_PassesItsExitToTheProcessExitHandler()
	{
		using Process process = ProcessTestHelpers.StartProcessThatExitsAfterOneInputLine();

		var failures = new RecordingFailureHandlers();

		ConnectionSessionFactory factory = CreateFactory(new ClientCollaboratorTestHost(), failures);
		ConnectionSession session = factory.Create(new RecordingLanguageServerConnection(Stream.Null, Stream.Null, process));

		factory.Configure(session);

		try
		{
			await process.StandardInput.WriteLineAsync("exit".AsMemory(), TestContext.CancellationToken).ConfigureAwait(false);

			ConnectionSession exitedSession = await failures
				.ProcessExited.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken)
				.ConfigureAwait(false);

			Assert.AreSame(session, exitedSession);
		}
		finally
		{
			session.JsonRpc?.Dispose();

			if (!process.HasExited)
				process.Kill(entireProcessTree: true);
		}
	}

	[TestMethod]
	public async Task Configure_PassesAJsonRpcDisconnectToTheDisconnectHandlerWithTheSession()
	{
		var failures = new RecordingFailureHandlers();

		ConnectionSessionFactory factory = CreateFactory(new ClientCollaboratorTestHost(), failures);
		ConnectionSession session = factory.Create(new RecordingLanguageServerConnection(Stream.Null, Stream.Null));

		factory.Configure(session);
		session.JsonRpc!.Dispose();

		(ConnectionSession disconnectedSession, JsonRpcDisconnectedEventArgs eventArgs) = await failures
			.Disconnected.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreSame(session, disconnectedSession);
		Assert.AreEqual(DisconnectedReason.LocallyDisposed, eventArgs.Reason);
	}

	[TestMethod]
	public async Task Start_StartsTheJsonRpcListener()
	{
		var failures = new RecordingFailureHandlers();

		ConnectionSessionFactory factory = CreateFactory(new ClientCollaboratorTestHost(), failures);
		ConnectionSession session = factory.Create(new RecordingLanguageServerConnection(new MemoryStream(), Stream.Null));

		factory.Configure(session);
		factory.Start(session);

		// An empty server stream ends the conversation as soon as the listener reads it, which an unstarted listener never does.
		(ConnectionSession disconnectedSession, JsonRpcDisconnectedEventArgs eventArgs) = await failures
			.Disconnected.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreSame(session, disconnectedSession);
		Assert.AreNotEqual(DisconnectedReason.LocallyDisposed, eventArgs.Reason);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task Start_KeepsTheStandardErrorLoopOfTheProcessOnTheSession()
	{
		using Process process = ProcessTestHelpers.StartProcessThatWritesToStandardError("server diagnostics");

		var host = new ClientCollaboratorTestHost();
		ConnectionSession session = host.SessionFactory.Create(new RecordingLanguageServerConnection(new MemoryStream(), Stream.Null, process));

		host.SessionFactory.Configure(session);
		host.SessionFactory.Start(session);

		Assert.IsNotNull(session.StandardErrorLoopTask);

		await session.StandardErrorLoopTask.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual("server diagnostics", session.GetRecentStandardErrorSummary());

		session.JsonRpc?.Dispose();
	}

	[TestMethod]
	public void Start_WithoutAJsonRpcInstance_ThrowsInvalidOperationException()
	{
		var host = new ClientCollaboratorTestHost();
		ConnectionSession session = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process: null);

		Assert.ThrowsExactly<InvalidOperationException>(() => host.SessionFactory.Start(session));
	}

	[TestMethod]
	public void CreateJsonRpc_WithoutAMessageHandlerOrRpcTarget_ThrowsInvalidOperationException()
	{
		var host = new ClientCollaboratorTestHost();
		ConnectionSession session = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process: null);

		Assert.ThrowsExactly<InvalidOperationException>(() => host.SessionFactory.CreateJsonRpc(session));

		session.MessageHandler = host.SessionFactory.CreateMessageHandler(Stream.Null, Stream.Null, session.ConnectionGeneration);

		Assert.ThrowsExactly<InvalidOperationException>(() => host.SessionFactory.CreateJsonRpc(session));
	}

	[TestMethod]
	public void CreateMessageHandler_ReadsWithTheProtocolOptionsAndTheClientLogger()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);
		var host = new ClientCollaboratorTestHost(logger);

		using HeaderDelimitedMessageHandler handler = host.SessionFactory.CreateMessageHandler(Stream.Null, Stream.Null, connectionGeneration: 1);
		var formatter = (SystemTextJsonFormatter)handler.Formatter;

		_ = JsonSerializer.Deserialize<HoverResponse>("[ 1 ]", formatter.JsonSerializerOptions);

		Assert.IsTrue(logger.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logger.Logs));
	}

	[TestMethod]
	public void CreateMessageHandler_CalledTwice_ReusesTheMessageConvertersOfTheFirstCall()
	{
		var host = new ClientCollaboratorTestHost();

		using HeaderDelimitedMessageHandler first = host.SessionFactory.CreateMessageHandler(Stream.Null, Stream.Null, connectionGeneration: 1);
		using HeaderDelimitedMessageHandler second = host.SessionFactory.CreateMessageHandler(Stream.Null, Stream.Null, connectionGeneration: 2);

		HoverResponseJsonConverter firstConverter = ((SystemTextJsonFormatter)first.Formatter)
			.JsonSerializerOptions.Converters.OfType<HoverResponseJsonConverter>()
			.Single();

		HoverResponseJsonConverter secondConverter = ((SystemTextJsonFormatter)second.Formatter)
			.JsonSerializerOptions.Converters.OfType<HoverResponseJsonConverter>()
			.Single();

		Assert.AreSame(firstConverter, secondConverter);
	}

	[TestMethod]
	public void CreateRpcTarget_BindsTheTargetToItsGeneration()
	{
		var host = new ClientCollaboratorTestHost(canAcceptServerMessagesForGeneration: static generation => generation == 5);

		Assert.HasCount(1, host.SessionFactory.CreateRpcTarget(5).WorkspaceFolders());
		Assert.IsEmpty(host.SessionFactory.CreateRpcTarget(4).WorkspaceFolders());
	}

	[TestMethod]
	public async Task Start_WhenTheServerStreamEndsUnderTheComposition_DetachesRaisesUnavailableAndDisposes()
	{
		var host = new ClientCollaboratorTestHost();
		ConnectionSession session = host.SessionFactory.Create(new RecordingLanguageServerConnection(new MemoryStream(), Stream.Null));

		host.SessionFactory.Configure(session);
		host.MakeReady(session);
		host.SessionFactory.Start(session);

		await TestPolling
			.UntilAsync(() => host.UnavailableRaises.Count == 1, TestPolling.DefaultTimeout, "The supervisor never saw the disconnect.")
			.ConfigureAwait(false);

		Assert.AreSequenceEqual([1], [.. host.UnavailableRaises]);
		Assert.IsNull(host.State.ActiveSession);

		await AssertDisposedAsync(session).ConfigureAwait(false);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public async Task Configure_WhenTheProcessExitsUnderTheComposition_DetachesRaisesUnavailableAndDisposes()
	{
		using Process process = ProcessTestHelpers.StartProcessThatExitsAfterOneInputLine();

		var host = new ClientCollaboratorTestHost();
		ConnectionSession session = host.SessionFactory.Create(new RecordingLanguageServerConnection(Stream.Null, Stream.Null, process));

		host.SessionFactory.Configure(session);
		host.MakeReady(session);

		try
		{
			await process.StandardInput.WriteLineAsync("exit".AsMemory(), TestContext.CancellationToken).ConfigureAwait(false);

			await TestPolling
				.UntilAsync(() => host.UnavailableRaises.Count == 1, TestPolling.DefaultTimeout, "The supervisor never saw the process exit.")
				.ConfigureAwait(false);

			Assert.AreSequenceEqual([1], [.. host.UnavailableRaises]);
			Assert.IsNull(host.State.ActiveSession);

			await AssertDisposedAsync(session).ConfigureAwait(false);
		}
		finally
		{
			if (!process.HasExited)
				process.Kill(entireProcessTree: true);
		}
	}

	private sealed class RecordingFailureHandlers
	{
		private readonly TaskCompletionSource<ConnectionSession> _processExited = new(TaskCreationOptions.RunContinuationsAsynchronously);

		private readonly TaskCompletionSource<(ConnectionSession Session, JsonRpcDisconnectedEventArgs EventArgs)> _disconnected = new(
			TaskCreationOptions.RunContinuationsAsynchronously
		);

		public Task<ConnectionSession> ProcessExited => _processExited.Task;

		public Task<(ConnectionSession Session, JsonRpcDisconnectedEventArgs EventArgs)> Disconnected => _disconnected.Task;

		public void HandleProcessExited(ConnectionSession session) => _processExited.TrySetResult(session);

		public void HandleJsonRpcDisconnected(ConnectionSession session, JsonRpcDisconnectedEventArgs eventArgs) =>
			_disconnected.TrySetResult((session, eventArgs));
	}
}
