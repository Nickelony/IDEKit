using System.Diagnostics;

namespace Nickelony.LanguageServer.Client;

/// <summary>Runs the ordered shutdown of the collaborators of one client within the disposal budget of its lifetime.</summary>
/// <remarks>
/// The stages run in a fixed order: the signal channels complete, the lifetime is canceled, the active session and any queued failed-session
/// cleanup are disposed, the server-message loops finish, and the startup gate is disposed last. Every stage spends from one shared budget,
/// and a stage that does not finish within it is abandoned and observed.
/// </remarks>
internal sealed class ClientTeardown
{
	private static readonly TimeSpan s_longestGateWait = TimeSpan.FromMilliseconds(int.MaxValue);

	private readonly ClientLifetime _lifetime;
	private readonly ConnectionSessionSupervisor _sessionSupervisor;
	private readonly FailedSessionDispatcher _failedSessionDispatcher;
	private readonly ServerMessageRouter _serverMessageRouter;
	private readonly BackgroundTaskObserver _backgroundTaskObserver;
	private readonly SemaphoreSlim _startLock;
	private readonly LanguageServerClientTestHooks _testHooks;
	private readonly ILogger _logger;

	/// <param name="lifetime">The lifetime that the teardown cancels, releases and measures its budget from.</param>
	/// <param name="sessionSupervisor">Disposes the active session.</param>
	/// <param name="failedSessionDispatcher">Holds the queued cleanup of failed sessions.</param>
	/// <param name="serverMessageRouter">The server-message loops that the teardown completes and awaits.</param>
	/// <param name="backgroundTaskObserver">Waits for each stage and observes the stages that are abandoned.</param>
	/// <param name="startLock">The startup gate, which the teardown disposes after the other stages.</param>
	/// <param name="testHooks">The observer of the point before the first stage.</param>
	/// <param name="logger">The logger that receives the gate diagnostics.</param>
	internal ClientTeardown(
		ClientLifetime lifetime,
		ConnectionSessionSupervisor sessionSupervisor,
		FailedSessionDispatcher failedSessionDispatcher,
		ServerMessageRouter serverMessageRouter,
		BackgroundTaskObserver backgroundTaskObserver,
		SemaphoreSlim startLock,
		LanguageServerClientTestHooks testHooks,
		ILogger logger
	)
	{
		_lifetime = lifetime;
		_sessionSupervisor = sessionSupervisor;
		_failedSessionDispatcher = failedSessionDispatcher;
		_serverMessageRouter = serverMessageRouter;
		_backgroundTaskObserver = backgroundTaskObserver;
		_startLock = startLock;
		_testHooks = testHooks;
		_logger = logger;
	}

	/// <summary>Runs the shutdown stages once.</summary>
	/// <returns>A task that completes when the stages finish.</returns>
	internal async Task RunAsync()
	{
		try
		{
			if (_testHooks.BeforeTeardown is not null)
				await _testHooks.BeforeTeardown().ConfigureAwait(false);

			var disposeStopwatch = Stopwatch.StartNew();

			_serverMessageRouter.CompleteSignalChannels();

			// Complete subscriber delivery before the loops are awaited so a payload that is still queued for a
			// subscriber is dropped instead of starting a callback; a handler that already started still runs to completion.
			_serverMessageRouter.CompleteSubscribers();

			_lifetime.Cancel();

			await WaitForStageAsync(_sessionSupervisor.DisposeActiveSessionAsync(), disposeStopwatch, "active session disposal")
				.ConfigureAwait(false);

			await WaitForQueuedFailedSessionDisposalsAsync(disposeStopwatch).ConfigureAwait(false);

			if (!ReferenceEquals(_serverMessageRouter.DiagnosticsCoalescerTask, Task.CompletedTask))
			{
				await WaitForStageAsync(_serverMessageRouter.DiagnosticsCoalescerTask, disposeStopwatch, "diagnostics coalescer")
					.ConfigureAwait(false);
			}

			await WaitForStageAsync(_serverMessageRouter.ServerMessageDispatcherTask, disposeStopwatch, "server-message dispatcher")
				.ConfigureAwait(false);

			await DisposeStartLockAsync(_lifetime.GetRemainingDisposeBudget(disposeStopwatch)).ConfigureAwait(false);
		}
		finally
		{
			// The stages have finished consulting the recorded loop terminations, so their markers can be released instead of
			// being retained for the client's lifetime, and a stage that threw still releases the cancellation source.
			_backgroundTaskObserver.ForgetAllLoops();
			_lifetime.Release();
		}
	}

	/// <summary>Waits for the startup gate to become available and then disposes it.</summary>
	/// <param name="waitTimeout">How long to wait for the gate before skipping its cleanup.</param>
	/// <returns>A task that completes when the startup gate has been disposed or when cleanup timed out.</returns>
	internal async Task DisposeStartLockAsync(TimeSpan waitTimeout)
	{
		bool startLockHeld = false;

		// A semaphore wait takes at most int.MaxValue milliseconds, and the disposal budget may be longer.
		if (waitTimeout > s_longestGateWait)
			waitTimeout = s_longestGateWait;

		if (waitTimeout <= TimeSpan.Zero)
		{
			_logger.LogWarning("Language server startup gate cleanup was skipped because the disposal budget was already exhausted.");
			return;
		}

		try
		{
			startLockHeld = await _startLock.WaitAsync(waitTimeout).ConfigureAwait(false);
		}
		catch (ObjectDisposedException)
		{
			return;
		}

		if (!startLockHeld)
		{
			_logger.LogWarning(
				"Language server startup gate did not become available within {TimeoutMs} ms during disposal.",
				(int)waitTimeout.TotalMilliseconds
			);

			return;
		}

		// Dispose the gate while still holding it. A release-then-dispose window would let a concurrent startup
		// acquire the semaphore and release it after disposal, replacing the documented EnsureStartedAsync result with an
		// ObjectDisposedException from its finally block. Concurrent waiters observe ObjectDisposedException
		// from the wait itself, which the startup path already handles as a disposal race.
		try
		{
			_startLock.Dispose();
		}
		catch (ObjectDisposedException) { }
	}

	private async Task WaitForQueuedFailedSessionDisposalsAsync(Stopwatch disposeStopwatch)
	{
		// The loop is bounded by the shared disposal budget: producers may keep replacing the queued
		// disposal task, and waiting for each replacement must not extend teardown indefinitely.
		while (_lifetime.GetRemainingDisposeBudget(disposeStopwatch) > TimeSpan.Zero)
		{
			Task pendingDisposal = _failedSessionDispatcher.QueuedDisposalTask;

			await WaitForStageAsync(pendingDisposal, disposeStopwatch, "detached session cleanup").ConfigureAwait(false);

			if (ReferenceEquals(pendingDisposal, _failedSessionDispatcher.QueuedDisposalTask))
				return;
		}

		// The budget ran out while a replacement cleanup was queued: report it as a skipped stage and observe it.
		await WaitForStageAsync(_failedSessionDispatcher.QueuedDisposalTask, disposeStopwatch, "detached session cleanup").ConfigureAwait(false);
	}

	private Task WaitForStageAsync(Task task, Stopwatch disposeStopwatch, string stage) =>
		_backgroundTaskObserver.WaitForTeardownStageAsync(task, _lifetime.GetRemainingDisposeBudget(disposeStopwatch), stage);
}
