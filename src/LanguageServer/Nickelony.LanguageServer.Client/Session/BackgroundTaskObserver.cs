namespace Nickelony.LanguageServer.Client;

/// <summary>Observes the background tasks of one client so that no failure goes unlogged and a lost loop marks the connection unhealthy.</summary>
/// <remarks>
/// <para>
/// An abandoned task, one that a caller stopped waiting for, logs its late fault at debug level. A background loop that ends
/// while the client is still active logs a warning once and marks the connection unhealthy, because a client without that loop cannot deliver server
/// messages or diagnostics. The record of logged loops lets disposal skip a second warning for a loop that was already reported.
/// </para>
/// </remarks>
internal sealed class BackgroundTaskObserver
{
	private readonly ILogger _logger;
	private readonly ClientLifetime _lifetime;
	private readonly Action _markConnectionUnhealthy;

	private readonly object _loopTerminationSyncRoot = new();
	private readonly HashSet<Task> _loggedLoopTerminations = [];

	/// <param name="lifetime">The lifetime of the owning client, which tells a loop that ends during disposal from one that ends early.</param>
	/// <param name="markConnectionUnhealthy">Marks the current connection generation unhealthy when a background loop ends unexpectedly.</param>
	/// <param name="logger">The logger that receives the diagnostics.</param>
	internal BackgroundTaskObserver(ClientLifetime lifetime, Action markConnectionUnhealthy, ILogger logger)
	{
		_lifetime = lifetime;
		_markConnectionUnhealthy = markConnectionUnhealthy;
		_logger = logger;
	}

	/// <summary>
	/// Observes a task that a caller stopped waiting for, so its late failure is logged instead of surfacing as an unobserved exception.
	/// </summary>
	/// <param name="task">The abandoned task.</param>
	internal void ObserveAbandoned(Task task)
	{
		task.ContinueWith(
			continuationAction: static (completedTask, state) =>
			{
				if (completedTask.IsCompletedSuccessfully)
					return;

				if (completedTask.Exception is not { } aggregateException || state is not ILogger logger)
					return;

				logger.LogDebug(FlattenForLogging(aggregateException), "An abandoned language server connection task later failed.");
			},

			state: _logger,
			cancellationToken: CancellationToken.None,
			continuationOptions: TaskContinuationOptions.ExecuteSynchronously,
			scheduler: TaskScheduler.Default
		);
	}

	/// <summary>Starts one background loop and observes it so that an unexpected end is reported while the client is still active.</summary>
	/// <param name="backgroundLoop">The loop delegate.</param>
	/// <param name="loopName">The logical loop name used for diagnostics.</param>
	/// <returns>The task of the started loop.</returns>
	internal Task StartObservedLoop(Func<Task> backgroundLoop, string loopName)
	{
		var task = Task.Run(backgroundLoop, CancellationToken.None);
		ObserveLoop(task, loopName);
		return task;
	}

	/// <summary>Observes one background loop task so unexpected termination is logged immediately while the client is still active.</summary>
	/// <param name="task">The background loop task to observe.</param>
	/// <param name="loopName">The logical loop name used for diagnostics.</param>
	internal void ObserveLoop(Task task, string loopName)
	{
		task.ContinueWith(
			continuationAction: static (completedTask, state) =>
			{
				if (state is not LoopObservation observation)
					return;

				observation.Owner.LogUnexpectedLoopTermination(completedTask, observation.LoopName);
			},

			state: new LoopObservation(this, loopName),
			cancellationToken: CancellationToken.None,
			continuationOptions: TaskContinuationOptions.ExecuteSynchronously,
			scheduler: TaskScheduler.Default
		);
	}

	/// <summary>
	/// Waits for one teardown stage within what is left of the disposal budget and reports a stage that was skipped, timed out or failed.
	/// </summary>
	/// <param name="task">The teardown stage to await.</param>
	/// <param name="remainingBudget">What is left of the disposal budget.</param>
	/// <param name="stage">The teardown stage reported in disposal diagnostics.</param>
	/// <remarks>
	/// A stage that keeps running after the wait ended is observed, so a later fault is logged instead of surfacing as an unobserved task exception.
	/// </remarks>
	internal async Task WaitForTeardownStageAsync(Task task, TimeSpan remainingBudget, string stage)
	{
		if (remainingBudget <= TimeSpan.Zero)
		{
			if (!task.IsCompleted)
			{
				_logger.LogWarning("Language server teardown stage '{Stage}' was skipped because the disposal budget was exhausted.", stage);
				ObserveAbandoned(task);
			}

			return;
		}

		try
		{
			await task.WaitAsync(remainingBudget).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			_logger.LogWarning(
				"Language server teardown stage '{Stage}' did not complete within {TimeoutMs} ms during disposal.",
				stage,
				(int)remainingBudget.TotalMilliseconds
			);

			ObserveAbandoned(task);
		}
		catch (Exception exception)
		{
			if (WasLoopTerminationLogged(task))
				return;

			_logger.LogWarning(exception, "Language server teardown stage '{Stage}' raised an exception during disposal.", stage);
		}
	}

	/// <summary>Reports whether the supplied loop task already logged its unexpected termination before disposal.</summary>
	/// <param name="task">The completed loop task.</param>
	/// <returns><see langword="true"/> when the task already logged unexpected termination; otherwise, <see langword="false"/>.</returns>
	internal bool WasLoopTerminationLogged(Task task)
	{
		lock (_loopTerminationSyncRoot)
			return _loggedLoopTerminations.Contains(task);
	}

	/// <summary>Clears the record of one completed loop task before that loop is restarted.</summary>
	/// <param name="task">The completed loop task to forget.</param>
	internal void ForgetLoop(Task task)
	{
		lock (_loopTerminationSyncRoot)
			_loggedLoopTerminations.Remove(task);
	}

	/// <summary>Drops every recorded loop termination once disposal has finished consulting them.</summary>
	/// <remarks>
	/// The records de-duplicate the disposal warning for an already-reported loop, so they must survive until the teardown stages have run;
	/// only then can they be released instead of retaining one task reference per un-restarted loop for the client's lifetime.
	/// </remarks>
	internal void ForgetAllLoops()
	{
		lock (_loopTerminationSyncRoot)
			_loggedLoopTerminations.Clear();
	}

	/// <summary>Logs cancellation for one background loop when disposal canceled it intentionally.</summary>
	/// <param name="task">The loop task to inspect.</param>
	/// <param name="loopName">The logical loop name for diagnostics.</param>
	/// <returns><see langword="true"/> when cancellation was logged; otherwise, <see langword="false"/>.</returns>
	internal bool TryLogCanceledLoop(Task? task, string loopName)
	{
		if (task?.IsCanceled != true)
			return false;

		_logger.LogDebug("Language server background loop '{LoopName}' was canceled during disposal.", loopName);
		return true;
	}

	/// <summary>Logs the failure for one background loop when it faulted during disposal.</summary>
	/// <param name="task">The loop task to inspect.</param>
	/// <param name="loopName">The logical loop name for diagnostics.</param>
	/// <returns><see langword="true"/> when a fault was logged; otherwise, <see langword="false"/>.</returns>
	internal bool TryLogFaultedLoop(Task? task, string loopName)
	{
		if (task?.IsFaulted != true || task.Exception is not { } aggregateException)
			return false;

		Exception loggedException = FlattenForLogging(aggregateException);

		_logger.LogWarning(loggedException, "Language server background loop '{LoopName}' failed during disposal.", loopName);
		return true;
	}

	/// <summary>Logs unexpected background loop termination while the client is still active.</summary>
	/// <param name="task">The completed background loop task.</param>
	/// <param name="loopName">The logical loop name used for diagnostics.</param>
	private void LogUnexpectedLoopTermination(Task task, string loopName)
	{
		if (_lifetime.IsDisposed || _lifetime.Token.IsCancellationRequested)
			return;

		if (!TryRecordLoopTermination(task))
			return;

		// A background loop that stops while the client is still active leaves it unable to deliver server messages or
		// diagnostics, so the connection is marked unhealthy to surface that through the connection-unavailable path.
		_markConnectionUnhealthy();

		if (task.IsFaulted && task.Exception is { } aggregateException)
		{
			Exception loggedException = FlattenForLogging(aggregateException);

			_logger.LogWarning(
				loggedException,
				"Language server background loop '{LoopName}' terminated unexpectedly while the client was still active.",
				loopName
			);

			return;
		}

		if (task.IsCanceled)
		{
			_logger.LogWarning("Language server background loop '{LoopName}' was canceled unexpectedly while the client was still active.", loopName);
			return;
		}

		_logger.LogWarning("Language server background loop '{LoopName}' completed unexpectedly while the client was still active.", loopName);
	}

	/// <summary>Records that one background loop termination has already been logged before disposal.</summary>
	/// <param name="task">The completed background loop task.</param>
	/// <returns><see langword="true"/> when the task was newly recorded; otherwise, <see langword="false"/>.</returns>
	private bool TryRecordLoopTermination(Task task)
	{
		lock (_loopTerminationSyncRoot)
			return _loggedLoopTerminations.Add(task);
	}

	/// <summary>Flattens the aggregate of a faulted task into the exception to log.</summary>
	/// <param name="aggregateException">The aggregate exception carried by the faulted task.</param>
	/// <returns>The single inner exception when there is exactly one; otherwise, the flattened aggregate.</returns>
	private static Exception FlattenForLogging(AggregateException aggregateException)
	{
		AggregateException flattened = aggregateException.Flatten();
		return flattened.InnerExceptions.Count == 1 ? flattened.InnerExceptions[0] : flattened;
	}

	private readonly record struct LoopObservation(BackgroundTaskObserver Owner, string LoopName);
}
