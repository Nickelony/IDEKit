using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class ClientTeardownTests
{
	public TestContext TestContext { get; set; } = null!;

	[TestMethod]
	public async Task RunAsync_CancelsTheLifetimeAndDisposesTheActiveSession()
	{
		var host = new ClientCollaboratorTestHost();
		ConnectionSession session = ConnectionSessionTestHelpers.CreateSessionWithoutJsonRpc(generation: 1, process: null);
		host.MakeReady(session);

		await host.Teardown.RunAsync().ConfigureAwait(false);

		Assert.IsTrue(host.Lifetime.Token.IsCancellationRequested);
		Assert.IsNull(host.State.ActiveSession);
		Assert.AreEqual(1, ((RecordingLanguageServerConnection)session.Connection).DisposeCount);
	}

	[TestMethod]
	public async Task RunAsync_CompletesTheServerMessageLoopsSoNoPayloadStartsAfterwards()
	{
		var host = new ClientCollaboratorTestHost();
		int delivered = 0;

		host.Router.AddDiagnosticsSubscriber((_, _) => Interlocked.Increment(ref delivered));
		host.Router.EnsureServerMessageAndDiagnosticsLoopsRunning();

		await host.Teardown.RunAsync().ConfigureAwait(false);

		Assert.IsTrue(host.Router.ServerMessageDispatcherTask.IsCompleted);
		Assert.IsTrue(host.Router.DiagnosticsCoalescerTask.IsCompleted);

		host.Router.QueueDiagnosticsPublished(connectionGeneration: 1, ServerMessageTestHelpers.CreateDiagnostics("untitled:First", "late"));
		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(0, Volatile.Read(ref delivered));
	}

	[TestMethod]
	public async Task RunAsync_RunsTheHookBeforeCancelingTheLifetime()
	{
		var events = new ConcurrentQueue<string>();

		var hooks = new LanguageServerClientTestHooks
		{
			BeforeTeardown = () =>
			{
				events.Enqueue("hook");
				return Task.CompletedTask;
			},
		};

		var host = new ClientCollaboratorTestHost(testHooks: hooks);

		using CancellationTokenRegistration registration = host.Lifetime.Token.Register(() => events.Enqueue("cancel"));

		await host.Teardown.RunAsync().ConfigureAwait(false);

		Assert.AreSequenceEqual(["hook", "cancel"], [.. events]);
	}

	[TestMethod]
	public async Task RunAsync_DisposesTheStartupGateLast()
	{
		var host = new ClientCollaboratorTestHost();

		await host.Teardown.RunAsync().ConfigureAwait(false);

		Assert.ThrowsExactly<ObjectDisposedException>(() => host.StartLock.Wait(0, TestContext.CancellationToken));
	}

	[TestMethod]
	public async Task RunAsync_WhenTheHookThrows_StillReleasesTheLifetimeSoLaterCancellationHasNoSource()
	{
		var host = new ClientCollaboratorTestHost(
			testHooks: new LanguageServerClientTestHooks
			{
				BeforeTeardown = static () => throw new InvalidOperationException("Simulated hook failure."),
			}
		);

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(host.Teardown.RunAsync).ConfigureAwait(false);

		// The release disposed the cancellation source, so a later cancellation finds none and the token stays untouched.
		host.Lifetime.Cancel();

		Assert.IsFalse(host.Lifetime.Token.IsCancellationRequested);
	}

	[TestMethod]
	public async Task RunAsync_AfterItCompleted_LeavesNothingToCancel()
	{
		var host = new ClientCollaboratorTestHost();
		await host.Teardown.RunAsync().ConfigureAwait(false);

		// Cancel after release must not throw.
		host.Lifetime.Cancel();
	}

	[TestMethod]
	public async Task RunAsync_ASessionDisposalThatOutlastsTheBudget_ReportsTheStageAndTheSkippedQueuedCleanup()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);
		var host = new ClientCollaboratorTestHost(logger, disposeWaitTimeout: TimeSpan.FromMilliseconds(80));
		var releaseConnection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		var session = new ConnectionSession(connectionGeneration: 1, process: null, readStream: Stream.Null, writeStream: Stream.Null)
		{
			Connection = new GatedDisposeConnection(releaseConnection.Task),
		};

		host.MakeReady(session);
		FailedSessionTestHelpers.QueueBlockedDisposal(host.FailedSessionDispatcher, releaseCleanup.Task);

		await host.Teardown.RunAsync().ConfigureAwait(false);

		Assert.Contains(
			static log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("'active session disposal'", StringComparison.Ordinal),
			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);

		Assert.Contains(
			static log =>
				log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("'detached session cleanup' was skipped", StringComparison.Ordinal),

			logger.Logs,
			string.Join(Environment.NewLine, logger.Logs)
		);

		releaseConnection.SetResult();
		releaseCleanup.SetResult();
	}

	[TestMethod]
	public async Task DisposeStartLockAsync_WhenTheGateIsFree_DisposesIt()
	{
		var host = new ClientCollaboratorTestHost();

		await host.Teardown.DisposeStartLockAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		Assert.ThrowsExactly<ObjectDisposedException>(() => host.StartLock.Wait(0, TestContext.CancellationToken));
	}

	[TestMethod]
	public async Task DisposeStartLockAsync_WhenTheBudgetIsExhausted_SkipsTheCleanupWithAWarning()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);
		var host = new ClientCollaboratorTestHost(logger);

		await host.Teardown.DisposeStartLockAsync(TimeSpan.Zero).ConfigureAwait(false);

		Assert.IsTrue(host.StartLock.Wait(0, TestContext.CancellationToken), "The gate stays usable when its cleanup was skipped.");

		Assert.Contains(
			static log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("cleanup was skipped", StringComparison.Ordinal),
			logger.Logs
		);
	}

	[TestMethod]
	public async Task DisposeStartLockAsync_WhenAStartupHoldsTheGate_LogsTheTimeoutAndLeavesTheGateAlone()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);
		var host = new ClientCollaboratorTestHost(logger);

		host.StartLock.Wait(TestContext.CancellationToken);

		await host.Teardown.DisposeStartLockAsync(TimeSpan.FromMilliseconds(40)).ConfigureAwait(false);

		Assert.Contains(
			static log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("did not become available", StringComparison.Ordinal),
			logger.Logs
		);

		host.StartLock.Release();

		Assert.IsTrue(host.StartLock.Wait(0, TestContext.CancellationToken));
	}

	[TestMethod]
	public async Task DisposeStartLockAsync_WhenTheGateIsAlreadyDisposed_ReturnsQuietly()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);
		var host = new ClientCollaboratorTestHost(logger);

		host.StartLock.Dispose();

		await host.Teardown.DisposeStartLockAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

		Assert.IsEmpty(logger.Logs);
	}

	[TestMethod]
	public async Task DisposeStartLockAsync_WithABudgetLongerThanASemaphoreWaitAllows_ClampsTheWait()
	{
		var host = new ClientCollaboratorTestHost();

		// A semaphore wait takes at most int.MaxValue milliseconds; a longer disposal budget must not make the cleanup throw.
		await host.Teardown.DisposeStartLockAsync(TimeSpan.FromDays(40)).ConfigureAwait(false);

		Assert.ThrowsExactly<ObjectDisposedException>(() => host.StartLock.Wait(0, TestContext.CancellationToken));
	}

	private sealed class GatedDisposeConnection(Task release) : ILanguageServerConnection
	{
		public Stream ReadStream => Stream.Null;

		public Stream WriteStream => Stream.Null;

		public Process? Process => null;

		public async ValueTask DisposeAsync() => await release.ConfigureAwait(false);
	}
}
