namespace Nickelony.QuickPick;

/// <summary>Gives the controller the thread it belongs to: it checks the caller and schedules a callback on that thread.</summary>
/// <remarks>
/// <para>
/// The thread is the one that creates the dispatcher, and its <see cref="SynchronizationContext.Current"/> is the one source of the thread:
/// it carries the work to the thread, and it is the context that an <see langword="await"/> on that thread resumes on. A callback scheduled for later
/// waits on a timer of the time provider, which fires on a pool thread, and then posts to the context, so the callback runs on the owner thread.
/// A scheduled callback whose handle was disposed before it ran runs nothing, even when its timer already fired.
/// </para>
/// <para>
/// The base <see cref="SynchronizationContext"/> type posts to the thread pool, so a thread that has only that type has no thread to return
/// to and the dispatcher is refused.
/// </para>
/// </remarks>
internal sealed class QuickPickDispatcher
{
	private readonly SynchronizationContext _context;
	private readonly TimeProvider _timeProvider;
	private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

	/// <param name="timeProvider">The source of the timers that delay a scheduled callback.</param>
	/// <exception cref="InvalidOperationException">
	/// The creating thread has no <see cref="SynchronizationContext.Current"/>, or its context is the base <see cref="SynchronizationContext"/> type.
	/// </exception>
	internal QuickPickDispatcher(TimeProvider timeProvider)
	{
		_context = SynchronizationContext.Current ?? throw new InvalidOperationException(NoContextMessage);

		if (_context.GetType() == typeof(SynchronizationContext))
			throw new InvalidOperationException(NoContextMessage);

		_timeProvider = timeProvider;
	}

	private static string NoContextMessage =>
		"A quick pick controller belongs to the thread that creates it, and that thread needs a single-threaded synchronization context: its timers and "
		+ "the answers of its providers return through it. Install the context of the UI toolkit or a pump of your own before creating the controller; the "
		+ "base SynchronizationContext type posts to the thread pool and does not qualify.";

	/// <summary>Determines whether the calling thread is the owner thread.</summary>
	internal bool CheckAccess() => Environment.CurrentManagedThreadId == _ownerThreadId;

	/// <summary>Verifies that the calling thread is the owner thread.</summary>
	/// <exception cref="InvalidOperationException">The calling thread is not the owner thread.</exception>
	internal void VerifyAccess()
	{
		if (!CheckAccess())
			throw new InvalidOperationException("The calling thread cannot use the quick pick controller because a different thread owns it.");
	}

	/// <summary>Posts a callback to the owner thread, where it runs when that thread pumps its context.</summary>
	/// <param name="callback">The callback to run.</param>
	/// <returns>
	/// <see langword="true"/> when the context accepted the post; <see langword="false"/> when it rejected the post because the owner thread
	/// is shutting down, in which case the callback never runs.
	/// </returns>
	internal bool TryPost(Action callback) => TryPost(_context, static state => ((Action)state!)(), callback);

	// A fault must not escape the thread that posts: the callers are a timer thread and the thread that disposes the controller.
	private static bool TryPost(SynchronizationContext context, SendOrPostCallback callback, object? state)
	{
		try
		{
			context.Post(callback, state);
			return true;
		}
		catch (Exception)
		{
			// The owner thread is shutting down, so the callback has no thread to run on.
			return false;
		}
	}

	/// <summary>Runs a callback once on the owner thread after a delay.</summary>
	/// <param name="delay">The delay before the callback runs.</param>
	/// <param name="callback">The callback to run.</param>
	/// <returns>A handle that cancels the callback when it is disposed before the callback ran.</returns>
	internal IDisposable Schedule(TimeSpan delay, Action callback) =>
		new ScheduledCallback(timeProvider: _timeProvider, context: _context, delay, callback);

	private sealed class ScheduledCallback : IDisposable
	{
		private readonly SynchronizationContext _context;
		private readonly Action _callback;
		private readonly ITimer _timer;

		private int _isCanceled;

		internal ScheduledCallback(TimeProvider timeProvider, SynchronizationContext context, TimeSpan delay, Action callback)
		{
			_context = context;
			_callback = callback;

			_timer = timeProvider.CreateTimer(
				callback: static state => ((ScheduledCallback)state!).Post(),
				state: this,
				dueTime: delay,
				period: Timeout.InfiniteTimeSpan
			);
		}

		public void Dispose()
		{
			Volatile.Write(ref _isCanceled, 1);
			_timer.Dispose();
		}

		private void Post() => _ = TryPost(_context, static state => ((ScheduledCallback)state!).Run(), this);

		private void Run()
		{
			if (Volatile.Read(ref _isCanceled) == 0)
				_callback();
		}
	}
}
