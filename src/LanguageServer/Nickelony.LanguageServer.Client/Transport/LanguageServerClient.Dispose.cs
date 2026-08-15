using System.Diagnostics;

namespace Nickelony.LanguageServer.Client;

public sealed partial class LanguageServerClient
{
	// Completed by the teardown the first dispose caller started. Later callers wait for it instead of returning
	// while background teardown continues, so a completed dispose call means disposal finished.
	private readonly TaskCompletionSource _disposeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

	private async Task WaitForQueuedFailedSessionDisposalsAsync(Stopwatch disposeStopwatch)
	{
		// The loop is bounded by the shared disposal budget: producers may keep replacing the queued
		// cleanup task, and waiting for each replacement must not extend teardown indefinitely.
		while (GetRemainingDisposeBudget(disposeStopwatch) > TimeSpan.Zero)
		{
			Task pendingDisposal = _transportHost.QueuedFailedSessionCleanupTask;

			await WaitWithDisposeBudgetAsync(
				pendingDisposal,
				disposeStopwatch,
				"detached session cleanup").ConfigureAwait(false);

			if (ReferenceEquals(pendingDisposal, _transportHost.QueuedFailedSessionCleanupTask))
				return;
		}
	}

	/// <summary>
	/// Disposes the currently active transport session, if any.
	/// </summary>
	private async Task DisposeActiveSessionAsync()
	{
		TransportSession? session = _capabilityStore.DetachActiveSession();

		if (session is not null)
			await _transportHost.DisposeSessionAsync(session).ConfigureAwait(false);
	}

	/// <summary>
	/// Claims disposal for the first caller and returns the teardown it started; every later caller receives the
	/// completion of that teardown.
	/// </summary>
	/// <returns>A task that completes when disposal finishes.</returns>
	private Task BeginDispose()
	{
		if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
			return _disposeCompletion.Task;

		_isDisposed = true;
		return RunTeardownAsync();
	}

	/// <summary>
	/// Runs the shared teardown sequence once and completes the disposal the later callers wait on. Claiming disposal
	/// and completing it are inseparable, so no caller can mark the client disposed and leave a later caller waiting.
	/// </summary>
	/// <returns>A task that completes when disposal finishes.</returns>
	private async Task RunTeardownAsync()
	{
		try
		{
			await DisposeCoreAsync().ConfigureAwait(false);
		}
		finally
		{
			_disposeCompletion.TrySetResult();
		}
	}

	/// <summary>
	/// Cancels the client lifetime token without starting teardown, stopping background pumps that observe it.
	/// </summary>
	internal void CancelLifetime()
	{
		try
		{
			_lifetimeCts.Cancel();
		}
		catch (ObjectDisposedException)
		{ }
		catch (Exception exception)
		{
			// CancellationTokenSource.Cancel wraps throwing cancellation callbacks in an AggregateException;
			// a callback failure must not abort disposal before the process and session are torn down.
			_logger.LogDebug(exception, "Language server lifetime cancellation for workspace '{Workspace}' reported one or more callback failures.", _workspaceRootsDisplayText);
		}
	}

	/// <summary>
	/// Runs the shared teardown sequence.
	/// </summary>
	/// <returns>A task that completes when the teardown sequence finishes.</returns>
	private async Task DisposeCoreAsync()
	{
		if (_testHooks.BeforeTeardown is not null)
			await _testHooks.BeforeTeardown().ConfigureAwait(false);

		var disposeStopwatch = Stopwatch.StartNew();

		_diagnosticsRouter.CompleteSignalChannels();

		// Complete subscriber delivery before the pumps are awaited so a payload that is still queued for a
		// subscriber is dropped instead of starting a callback after teardown finishes; a handler that already
		// started still runs to completion.
		_diagnosticsRouter.CompleteSubscribers();

		CancelLifetime();

		await WaitWithDisposeBudgetAsync(
			DisposeActiveSessionAsync(),
			disposeStopwatch,
			"active session disposal").ConfigureAwait(false);

		await WaitForQueuedFailedSessionDisposalsAsync(disposeStopwatch).ConfigureAwait(false);

		if (!ReferenceEquals(_diagnosticsRouter.DiagnosticsPumpTask, Task.CompletedTask))
		{
			await WaitWithDisposeBudgetAsync(
				_diagnosticsRouter.DiagnosticsPumpTask,
				disposeStopwatch,
				"diagnostics pump").ConfigureAwait(false);
		}

		await WaitWithDisposeBudgetAsync(
			_diagnosticsRouter.CallbackPumpTask,
			disposeStopwatch,
			"callback dispatcher").ConfigureAwait(false);

		await DisposeStartLockAsync(GetRemainingDisposeBudget(disposeStopwatch)).ConfigureAwait(false);

		// The teardown stages have finished consulting the recorded pump terminations, so their markers can be
		// released instead of being retained for the client's lifetime.
		_diagnosticsRouter.ClearObservedBackgroundLoopTerminations();

		_lifetimeCts.Dispose();
	}

	/// <summary>
	/// Waits for one teardown task while spending from the caller's remaining disposal budget.
	/// </summary>
	/// <param name="task">The teardown task to await.</param>
	/// <param name="disposeStopwatch">Tracks the elapsed disposal time.</param>
	/// <param name="stage">The teardown stage reported in disposal diagnostics.</param>
	private async Task WaitWithDisposeBudgetAsync(Task task, Stopwatch disposeStopwatch, string stage)
	{
		TimeSpan remainingDisposeBudget = GetRemainingDisposeBudget(disposeStopwatch);

		if (remainingDisposeBudget <= TimeSpan.Zero)
		{
			if (!task.IsCompleted)
			{
				_logger.LogWarning(
					"Language server teardown stage '{Stage}' was skipped because the disposal budget was exhausted.",
					stage);

				// The stage keeps running after disposal returns; observe it so a later fault is logged instead of
				// surfacing as an unobserved task exception.
				_protocolForwarder.ObserveAbandonedTask(task);
			}

			return;
		}

		try
		{
			await task.WaitAsync(remainingDisposeBudget).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			_logger.LogWarning(
				"Language server teardown stage '{Stage}' did not complete within {TimeoutMs} ms during disposal.",
				stage,
				(int)remainingDisposeBudget.TotalMilliseconds);

			// Waiting with a timeout abandons the stage while it keeps running; observe it so a later fault is
			// logged instead of surfacing as an unobserved task exception.
			_protocolForwarder.ObserveAbandonedTask(task);
		}
		catch (Exception exception)
		{
			if (_diagnosticsRouter.WasObservedBackgroundLoopTermination(task))
				return;

			_logger.LogWarning(exception, "Language server teardown stage '{Stage}' raised an exception during disposal.", stage);
		}
	}

	/// <summary>
	/// Gets the remaining disposal budget shared by the teardown stages.
	/// </summary>
	/// <param name="disposeStopwatch">Tracks the elapsed disposal time.</param>
	/// <returns>The remaining shared disposal budget.</returns>
	private TimeSpan GetRemainingDisposeBudget(Stopwatch disposeStopwatch)
	{
		TimeSpan remainingDisposeBudget = _disposeWaitTimeout - disposeStopwatch.Elapsed;
		return remainingDisposeBudget > TimeSpan.Zero ? remainingDisposeBudget : TimeSpan.Zero;
	}

	/// <summary>
	/// Waits for the startup gate to become available and then disposes it.
	/// </summary>
	/// <param name="waitTimeout">How long to wait for the gate before skipping its cleanup.</param>
	/// <returns>A task that completes when the startup gate has been disposed or when cleanup timed out.</returns>
	internal async Task DisposeStartLockAsync(TimeSpan waitTimeout)
	{
		bool startLockHeld = false;

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
			_logger.LogWarning("Language server startup gate did not become available within {TimeoutMs} ms during disposal.",
				(int)waitTimeout.TotalMilliseconds);

			return;
		}

		// Dispose the gate while still holding it. A release-then-dispose window would let a concurrent startup
		// acquire the semaphore and release it after disposal, replacing the documented StartAsync result with an
		// ObjectDisposedException from its finally block. Concurrent waiters now observe ObjectDisposedException
		// from the wait itself, which the startup path already handles as a disposal race.
		try
		{
			_startLock.Dispose();
		}
		catch (ObjectDisposedException)
		{ }
	}

	/// <summary>
	/// Guards a call against a disposed client, unless <paramref name="allowDisposed"/> allows disposed access.
	/// </summary>
	/// <param name="allowDisposed">Whether disposed access should be allowed.</param>
	/// <exception cref="ObjectDisposedException">The client has been disposed and the call does not allow disposed access.</exception>
	private void ThrowIfDisposed(bool allowDisposed)
	{
		if (!allowDisposed)
			ObjectDisposedException.ThrowIf(_isDisposed, nameof(LanguageServerClient));
	}

	/// <summary>
	/// Stops the language-server process, faults or cancels pending requests, and releases transport resources.
	/// </summary>
	/// <remarks>
	/// Blocks the calling thread until teardown finishes, for the first caller and for every later caller that waits
	/// for the teardown the first caller started; this is sync-over-async, so a thread-affine caller can deadlock.
	/// Prefer <see cref="DisposeAsync"/> where blocking the calling thread is undesirable.
	/// Do not call this method, or block on <see cref="DisposeAsync"/>, from a
	/// <see cref="ILanguageServerClient.TransportUnavailable"/> handler or another client callback; see the
	/// interface remarks for the shared disposal and delivery contracts.
	/// </remarks>
	public void Dispose() => BeginDispose().GetAwaiter().GetResult();

	/// <summary>
	/// Stops the language-server process, faults or cancels pending requests, and releases transport resources asynchronously.
	/// </summary>
	/// <returns>
	/// A task that completes when disposal finishes. Only the first caller performs teardown; a later caller waits for the
	/// teardown the first caller started, so awaiting this task always means disposal completed.
	/// </returns>
	public async ValueTask DisposeAsync() => await BeginDispose().ConfigureAwait(false);
}
