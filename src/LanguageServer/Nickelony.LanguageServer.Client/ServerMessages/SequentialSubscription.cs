namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Provides the per-subscriber background drain schedule and handler identity shared by the sequential subscriber sets: at most one drain runs
/// per subscriber at a time, and work enqueued while a drain is running causes exactly one rescheduled drain.
/// </summary>
/// <typeparam name="THandler">The subscriber delegate type this subscription delivers to.</typeparam>
internal abstract class SequentialSubscription<THandler> : IDisposable
	where THandler : Delegate
{
	private int _drainScheduled;
	private int _isDisposed;

	/// <param name="handler">The subscriber this subscription delivers to.</param>
	internal SequentialSubscription(THandler handler) => Handler = handler;

	/// <summary>The subscriber this subscription delivers to, which identifies it when it is removed from its set.</summary>
	internal THandler Handler { get; }

	/// <summary>Indicates whether this subscriber was disposed and should stop draining.</summary>
	internal bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

	/// <summary>Indicates whether work is still pending that requires another scheduled drain.</summary>
	internal abstract bool HasPendingWork { get; }

	/// <summary>
	/// Whether this subscriber has neither pending work nor a scheduled drain; tests read it through <see cref="ServerMessageRouter.IsDrained"/>.
	/// </summary>
	internal bool IsIdle => !HasPendingWork && Volatile.Read(ref _drainScheduled) == 0;

	/// <summary>Drains at most one round of pending work for this subscriber.</summary>
	/// <returns>
	/// <see langword="true"/> when a round of work was processed and the drain loop should continue; otherwise, <see langword="false"/>.
	/// </returns>
	internal abstract bool TryDrainNext();

	/// <summary>Drops this subscriber's pending work when it is disposed.</summary>
	internal abstract void OnDisposed();

	/// <summary>Marks this subscriber as disposed and drops its pending work.</summary>
	/// <remarks>
	/// A handler that already started still runs to completion; only work that was queued but had not started is dropped.
	/// </remarks>
	public void Dispose()
	{
		if (!TryMarkDisposed())
			return;

		OnDisposed();
	}

	/// <summary>Marks this subscriber as disposed.</summary>
	/// <returns><see langword="true"/> when this call performed the transition; otherwise, <see langword="false"/>.</returns>
	internal bool TryMarkDisposed() => Interlocked.Exchange(ref _isDisposed, 1) == 0;

	/// <summary>Reports one handler failure through the subscriber set's logger while isolating a throwing logger callback.</summary>
	/// <param name="logHandlerFailure">The logger callback supplied by the subscriber set.</param>
	/// <param name="exception">The handler failure to report.</param>
	internal static void LogHandlerFailure(Action<Exception> logHandlerFailure, Exception exception)
	{
		try
		{
			logHandlerFailure(exception);
		}
		catch (Exception)
		{
			// A throwing host logger must not stop the remaining drained payloads from being delivered.
		}
	}

	/// <summary>Reports a failure of the drain machinery itself through the subscriber set's logger.</summary>
	/// <param name="exception">The drain failure to report.</param>
	/// <remarks>
	/// A drain failure is contained so a faulting drain cannot terminate the process; this callback is the only place such a defect can surface,
	/// so an implementation must log rather than throw.
	/// </remarks>
	internal abstract void ReportDrainFailure(Exception exception);

	/// <summary>Schedules one background drain for this subscriber unless a drain is already scheduled.</summary>
	internal void ScheduleDrain()
	{
		if (Interlocked.CompareExchange(ref _drainScheduled, 1, 0) != 0)
			return;

		ThreadPool.QueueUserWorkItem(static state => ((SequentialSubscription<THandler>)state!).Drain(), this, preferLocal: false);
	}

	private void Drain()
	{
		try
		{
			try
			{
				while (TryDrainNext())
				{
					if (IsDisposed)
						return;
				}
			}
			catch (Exception exception)
			{
				// An exception raised by a subscriber set while draining must not escape a thread-pool work item; an
				// unhandled exception on a pool thread terminates the process on .NET Core. Handler failures are
				// already isolated per subscriber inside TryDrainNext, so this guard only protects the drain
				// machinery, whose failure is reported through the subscriber set's logger.
				ReportDrainFailure(exception);
			}
		}
		finally
		{
			Volatile.Write(ref _drainScheduled, 0);

			if (!IsDisposed && HasPendingWork)
				ScheduleDrain();
		}
	}
}
