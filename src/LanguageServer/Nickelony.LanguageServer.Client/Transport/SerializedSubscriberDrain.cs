namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Provides the per-subscriber background drain schedule and handler identity shared by the serialized subscriber
/// sets: at most one drain runs per subscriber at a time, and work enqueued while a drain is running causes exactly
/// one rescheduled drain.
/// </summary>
/// <typeparam name="THandler">The subscriber delegate type this drain delivers to.</typeparam>
internal abstract class SerializedSubscriberDrain<THandler> : IDisposable
	where THandler : Delegate
{
	private readonly Action? _onProgress;
	private int _drainScheduled;
	private int _isDisposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="SerializedSubscriberDrain{THandler}"/> class.
	/// </summary>
	/// <param name="handler">The subscriber this drain delivers to.</param>
	/// <param name="onProgress">
	/// Invoked whenever this drain advances toward (or reaches) its idle state, so a quiescence waiter can be
	/// released by a completion signal instead of polling. <see langword="null"/> when no waiter is registered.
	/// </param>
	protected SerializedSubscriberDrain(THandler handler, Action? onProgress = null)
	{
		Handler = handler;
		_onProgress = onProgress;
	}

	/// <summary>
	/// Gets the subscriber this drain delivers to, used to identify it when it is removed from its set.
	/// </summary>
	public THandler Handler { get; }

	/// <summary>
	/// Gets a value indicating whether this subscriber was disposed and should stop draining.
	/// </summary>
	protected bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

	/// <summary>
	/// Gets a value indicating whether work is still pending that requires another scheduled drain.
	/// </summary>
	protected abstract bool HasPendingWork { get; }

	/// <summary>
	/// Gets a value indicating whether this subscriber has neither pending work nor a scheduled drain, so a caller can
	/// observe a quiescence point instead of waiting out a time window.
	/// </summary>
	internal bool IsIdle => !HasPendingWork && Volatile.Read(ref _drainScheduled) == 0;

	/// <summary>
	/// Drains at most one round of pending work for this subscriber.
	/// </summary>
	/// <returns><see langword="true"/> when a round of work was processed and the drain loop should continue; otherwise, <see langword="false"/>.</returns>
	protected abstract bool TryDrainNext();

	/// <summary>
	/// Drops this subscriber's pending work when it is disposed.
	/// </summary>
	protected abstract void OnDisposed();

	/// <summary>
	/// Marks this subscriber as disposed and drops its pending work.
	/// </summary>
	/// <remarks>
	/// A handler that already started still runs to completion; only work that was queued but had not started is
	/// dropped, so no subscriber callback begins after disposal finishes.
	/// </remarks>
	public void Dispose()
	{
		if (!TryMarkDisposed())
			return;

		OnDisposed();
		_onProgress?.Invoke();
	}

	/// <summary>
	/// Marks this subscriber as disposed.
	/// </summary>
	/// <returns><see langword="true"/> when this call performed the transition; otherwise, <see langword="false"/>.</returns>
	protected bool TryMarkDisposed() => Interlocked.Exchange(ref _isDisposed, 1) == 0;

	/// <summary>
	/// Reports one handler failure through the subscriber set's logger while isolating a throwing logger callback.
	/// </summary>
	/// <param name="logHandlerFailure">The logger callback supplied by the subscriber set.</param>
	/// <param name="exception">The handler failure to report.</param>
	protected static void TryLogHandlerFailure(Action<Exception> logHandlerFailure, Exception exception)
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

	/// <summary>
	/// Reports a failure of the drain machinery itself through the subscriber set's logger.
	/// </summary>
	/// <remarks>
	/// A drain failure is contained so a faulting drain cannot terminate the process; this callback is the only
	/// place such a defect can surface, so an implementation must log rather than throw.
	/// </remarks>
	/// <param name="exception">The drain failure to report.</param>
	protected abstract void ReportDrainFailure(Exception exception);

	/// <summary>
	/// Schedules one background drain for this subscriber unless a drain is already scheduled.
	/// </summary>
	protected void TryScheduleDrain()
	{
		if (Interlocked.CompareExchange(ref _drainScheduled, 1, 0) != 0)
			return;

		ThreadPool.QueueUserWorkItem(static state => ((SerializedSubscriberDrain<THandler>)state!).Drain(), this, preferLocal: false);
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
				TryScheduleDrain();

			// The drain transition may have made this subscriber idle; release any quiescence waiter.
			_onProgress?.Invoke();
		}
	}
}
