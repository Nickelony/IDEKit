using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class ConnectionSessionSupervisorTests
{
	public TestContext TestContext { get; set; } = null!;

	private static RecordingLanguageServerConnection ConnectionOf(ConnectionSession session) => (RecordingLanguageServerConnection)session.Connection;

	private static async Task AssertDisposedAsync(ConnectionSession session) =>
		await ConnectionOf(session).Disposed.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

	[TestMethod]
	public async Task DisposeActiveSessionAsync_DetachesAndDisposesTheActiveSession()
	{
		var host = new ClientCollaboratorTestHost();
		ConnectionSession session = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process: null);
		host.MakeReady(session);

		await host.Supervisor.DisposeActiveSessionAsync().ConfigureAwait(false);

		Assert.IsNull(host.State.ActiveSession);
		Assert.IsFalse(host.State.IsReady);
		Assert.AreEqual(1, ConnectionOf(session).DisposeCount);
	}

	[TestMethod]
	public async Task DisposeActiveSessionAsync_WithoutAnActiveSession_DoesNothing()
	{
		var host = new ClientCollaboratorTestHost();

		await host.Supervisor.DisposeActiveSessionAsync().ConfigureAwait(false);

		Assert.IsNull(host.State.ActiveSession);
	}

	[TestMethod]
	public async Task HandleJsonRpcDisconnected_ForTheReadyActiveSession_DetachesRaisesUnavailableAndDisposesInTheBackground()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);
		var host = new ClientCollaboratorTestHost(logger);

		ConnectionSession session = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process: null);
		host.MakeReady(session);

		host.Supervisor.HandleJsonRpcDisconnected(
			session,
			new JsonRpcDisconnectedEventArgs("The stream ended.", DisconnectedReason.RemotePartyTerminated)
		);

		Assert.IsNull(host.State.ActiveSession);
		Assert.AreSequenceEqual([1], [.. host.UnavailableRaises]);

		Assert.Contains(
			static log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("disconnected unexpectedly", StringComparison.Ordinal),
			logger.Logs
		);

		await AssertDisposedAsync(session).ConfigureAwait(false);
	}

	[TestMethod]
	public void HandleJsonRpcDisconnected_BeforeTheSessionBecameReady_DetachesWithoutRaisingUnavailable()
	{
		var host = new ClientCollaboratorTestHost();
		ConnectionSession session = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process: null);
		host.State.SetActiveSession(session);

		host.Supervisor.HandleJsonRpcDisconnected(
			session,
			new JsonRpcDisconnectedEventArgs("The stream ended.", DisconnectedReason.RemotePartyTerminated)
		);

		Assert.IsNull(host.State.ActiveSession);
		Assert.IsEmpty(host.UnavailableRaises);
	}

	[TestMethod]
	public void HandleJsonRpcDisconnected_ForASessionThatIsNoLongerActive_IsIgnoredWithADebugLog()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		var host = new ClientCollaboratorTestHost(logger);

		ConnectionSession stale = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process: null);
		ConnectionSession current = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 2, process: null);

		host.MakeReady(current);

		host.Supervisor.HandleJsonRpcDisconnected(
			stale,
			new JsonRpcDisconnectedEventArgs("The stream ended.", DisconnectedReason.RemotePartyTerminated)
		);

		Assert.AreSame(current, host.State.ActiveSession);
		Assert.IsEmpty(host.UnavailableRaises);

		Assert.Contains(
			static log => log.StartsWith("Debug|", StringComparison.Ordinal) && log.Contains("stale", StringComparison.Ordinal),
			logger.Logs
		);
	}

	[TestMethod]
	public void HandleJsonRpcDisconnected_DuringClientDisposal_DetachesQuietlyWithoutRaisingUnavailable()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);
		var host = new ClientCollaboratorTestHost(logger);

		ConnectionSession session = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process: null);
		host.MakeReady(session);
		_ = host.Lifetime.BeginDisposeAsync(static () => Task.CompletedTask);

		host.Supervisor.HandleJsonRpcDisconnected(
			session,
			new JsonRpcDisconnectedEventArgs("The stream ended.", DisconnectedReason.RemotePartyTerminated)
		);

		Assert.IsNull(host.State.ActiveSession);
		Assert.IsEmpty(host.UnavailableRaises);
		Assert.DoesNotContain(static log => log.StartsWith("Warn|", StringComparison.Ordinal), logger.Logs);
	}

	[TestMethod]
	public void HandleJsonRpcDisconnected_LocalDisposal_IsExpectedAndRaisesNothing()
	{
		using var logger = new RecordingLogger(LogLevel.Information);
		var host = new ClientCollaboratorTestHost(logger);

		ConnectionSession session = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process: null);
		host.MakeReady(session);

		host.Supervisor.HandleJsonRpcDisconnected(session, new JsonRpcDisconnectedEventArgs("Disposed.", DisconnectedReason.LocallyDisposed));

		Assert.IsEmpty(host.UnavailableRaises);
		Assert.DoesNotContain(static log => log.StartsWith("Warn|", StringComparison.Ordinal), logger.Logs);

		Assert.Contains(
			static log => log.StartsWith("Info|", StringComparison.Ordinal) && log.Contains("expected local shutdown", StringComparison.Ordinal),
			logger.Logs
		);
	}

	[TestMethod]
	public async Task HandleProcessExited_ForTheReadyActiveSession_DetachesRaisesUnavailableAndDisposesInTheBackground()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);
		var host = new ClientCollaboratorTestHost(logger);

		ConnectionSession session = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process: null);
		host.MakeReady(session);

		host.Supervisor.HandleProcessExited(session);

		Assert.IsNull(host.State.ActiveSession);
		Assert.AreSequenceEqual([1], [.. host.UnavailableRaises]);

		Assert.Contains(
			static log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("exited unexpectedly", StringComparison.Ordinal),
			logger.Logs
		);

		await AssertDisposedAsync(session).ConfigureAwait(false);
	}

	[TestMethod]
	public void HandleProcessExited_ForASessionThatIsNotActive_ChangesNothing()
	{
		var host = new ClientCollaboratorTestHost();

		ConnectionSession stale = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process: null);
		ConnectionSession current = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 2, process: null);

		host.MakeReady(current);

		host.Supervisor.HandleProcessExited(stale);

		Assert.AreSame(current, host.State.ActiveSession);
		Assert.IsTrue(host.State.IsReady);
		Assert.IsEmpty(host.UnavailableRaises);
	}

	[TestMethod]
	public void HandleProcessExited_DuringClientDisposal_DoesNotWarn()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);
		var host = new ClientCollaboratorTestHost(logger);

		ConnectionSession session = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process: null);
		host.MakeReady(session);
		_ = host.Lifetime.BeginDisposeAsync(static () => Task.CompletedTask);

		host.Supervisor.HandleProcessExited(session);

		Assert.IsNull(host.State.ActiveSession);
		Assert.IsEmpty(logger.Logs);
	}

	[TestMethod]
	public async Task HandleProcessExited_ForSuccessiveFailedSessions_DisposesEachOfThem()
	{
		var host = new ClientCollaboratorTestHost();

		ConnectionSession first = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process: null);
		ConnectionSession second = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 2, process: null);

		host.MakeReady(first);
		host.Supervisor.HandleProcessExited(first);

		host.MakeReady(second);
		host.Supervisor.HandleProcessExited(second);

		await host
			.FailedSessionDispatcher.QueuedDisposalTask.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(1, ConnectionOf(first).DisposeCount);
		Assert.AreEqual(1, ConnectionOf(second).DisposeCount);
	}
}
