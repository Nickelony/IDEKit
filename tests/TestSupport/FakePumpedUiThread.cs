using System.Diagnostics;

namespace Nickelony.Testing;

/// <summary>
/// The synchronization context and the clock of the thread that created it, whose queued work and timers run
/// only when a test pumps it, so a controller test runs without a UI toolkit.
/// </summary>
/// <remarks>
/// <para>
/// This is the one queue-and-clock engine of the pumped test doubles. It exposes the thread as a <see cref="SynchronizationContext"/>
/// and a <see cref="System.TimeProvider"/> because the quick pick tests, which do not reference IDEKit Core, have no dispatcher interface;
/// <c>FakePumpedDispatcher</c> in the IDEKit test support wraps the same engine as an <c>IUiDispatcher</c> with priorities, so the two differ
/// only in the facade a test reaches.
/// </para>
/// <para>
/// On the owner thread, <see cref="SynchronizationContext.Send"/> runs the call inline, as the context of a UI toolkit does. From another thread
/// it queues the call and blocks until the owner pumps; a blocking call outranks normal work, like the send priority of the toolkits.
/// <see cref="SynchronizationContext.Post"/> always queues. Queued work runs by priority, then by arrival.
/// </para>
/// <para>
/// A timer created through <see cref="TimeProvider"/> waits on a virtual clock that only <see cref="Advance"/> moves, so a debounce
/// costs no wall time and a test states how much time passed, including that a timer must not have fired yet. A timer that comes due joins the queue
/// at background priority, and a timer disposed after that still runs nothing.
/// </para>
/// <para>
/// The constructor either installs the context as the thread's <see cref="SynchronizationContext"/>,
/// which is the production shape because a continuation that captured it queues here, or clears the thread's context, so a continuation
/// resumes on the thread pool whatever the test runner installed. A test that installs it pumps with <see cref="PumpUntil"/> and never awaits
/// a pending task, because the continuation of a suspended await runs only when the owner pumps. <see cref="Dispose"/> restores the previous context,
/// because the test host reuses its threads, and then cancels the calls that wait for the owner and rejects later ones the way a shutdown of a UI
/// thread does.
/// </para>
/// </remarks>
internal sealed class FakePumpedUiThread : IDisposable
{
	// A pump that runs this many items, or a clock advance that fires this many timers, is work that keeps queuing more work; failing is better
	// than hanging.
	private const int RunawayLimit = 100_000;

	// The order of the toolkits' dispatchers: blocking calls, then normal work, then render work, then background work, then context-idle work.
	internal const int SendRank = 4;
	internal const int NormalRank = 3;
	internal const int RenderRank = 2;
	internal const int BackgroundRank = 1;
	internal const int IdleRank = 0;

	private static readonly TimeSpan s_pollInterval = TimeSpan.FromMilliseconds(10.0);

	private readonly object _sync = new();
	private readonly List<QueuedWork> _queue = [];
	private readonly List<VirtualTimer> _timers = [];

	private readonly SynchronizationContext? _installedContext;
	private readonly SynchronizationContext? _previousContext;

	private TimeSpan _now;
	private long _nextSequence;
	private bool _isShutDown;

	/// <param name="installSynchronizationContext">
	/// <see langword="true"/> to make the context the synchronization context of the creating thread until it is disposed;
	/// <see langword="false"/> to clear the thread's synchronization context for the same time.
	/// </param>
	public FakePumpedUiThread(bool installSynchronizationContext)
	{
		_previousContext = SynchronizationContext.Current;
		Context = new PumpedSynchronizationContext(this);
		TimeProvider = new VirtualTimeProvider(this);
		_installedContext = installSynchronizationContext ? Context : null;
		SynchronizationContext.SetSynchronizationContext(_installedContext);
	}

	/// <summary>The context that posts to the owner thread, which is also the thread's current context when the constructor installed it.</summary>
	public SynchronizationContext Context { get; }

	/// <summary>The clock whose timers fire only when <see cref="Advance"/> reaches them.</summary>
	public TimeProvider TimeProvider { get; }

	/// <summary>The managed thread identifier of the owner thread.</summary>
	public int OwnerThreadId { get; } = Environment.CurrentManagedThreadId;

	/// <summary>The number of queued items that have not run.</summary>
	public int QueuedCount
	{
		get
		{
			lock (_sync)
				return _queue.Count;
		}
	}

	/// <summary>The number of timers that were neither canceled nor due.</summary>
	public int PendingTimerCount
	{
		get
		{
			lock (_sync)
				return _timers.Count(static timer => timer.IsPending);
		}
	}

	/// <summary>The virtual clock reading.</summary>
	public TimeSpan Now
	{
		get
		{
			lock (_sync)
				return _now;
		}
	}

	/// <summary>Determines whether the calling thread is the owner thread.</summary>
	public bool CheckAccess() => Environment.CurrentManagedThreadId == OwnerThreadId;

	/// <summary>Runs the queued work until the queue is empty, without moving the virtual clock.</summary>
	/// <exception cref="InvalidOperationException">The caller is not on the owner thread, or the work keeps queuing more work.</exception>
	/// <remarks>
	/// A failure of a posted callback propagates to the caller, so a test fails where the work ran.
	/// </remarks>
	public void PumpUntilIdle()
	{
		VerifyOwner();

		for (int count = 0; RunNext(); count++)
		{
			if (count >= RunawayLimit)
				throw new InvalidOperationException("The queue did not drain because the work keeps queuing more work.");
		}
	}

	/// <summary>
	/// Runs queued work and waits for work that other threads queue, until the condition holds; then asserts it. The virtual clock stays
	/// where it is, so a timer fires only through <see cref="Advance"/>.
	/// </summary>
	/// <param name="condition">The condition to wait for.</param>
	/// <param name="timeout">The wall-clock limit for the wait, which every pass of the wait checks; five seconds by default.</param>
	/// <exception cref="InvalidOperationException">The caller is not on the owner thread.</exception>
	public void PumpUntil(Func<bool> condition, TimeSpan? timeout = null)
	{
		ArgumentNullException.ThrowIfNull(condition);

		VerifyOwner();

		var stopwatch = Stopwatch.StartNew();
		TimeSpan limit = timeout ?? TimeSpan.FromSeconds(5.0);

		while (!condition() && stopwatch.Elapsed < limit)
		{
			if (RunNext())
				continue;

			// A call of another thread or a continuation can still arrive, so wait for it without spinning.
			lock (_sync)
			{
				if (_queue.Count == 0)
					Monitor.Wait(_sync, s_pollInterval);
			}
		}

		Assert.IsTrue(condition(), "The expected dispatcher work did not complete within the allotted time.");
	}

	/// <summary>Moves the virtual clock forward, running the queued work and each timer that comes due on the way, in due order.</summary>
	/// <param name="duration">How far to move the clock.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="duration"/> is negative.</exception>
	/// <exception cref="InvalidOperationException">
	/// The caller is not on the owner thread, or the timers keep scheduling more timers that come due.
	/// </exception>
	public void Advance(TimeSpan duration)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

		VerifyOwner();

		TimeSpan target;

		lock (_sync)
			target = _now + duration;

		int firedRounds = 0;

		do
		{
			PumpUntilIdle();

			if (++firedRounds > RunawayLimit)
				throw new InvalidOperationException("The clock did not settle because the timers keep scheduling more timers that come due.");
		} while (FireNextTimer(through: target));

		lock (_sync)
		{
			if (_now < target)
				_now = target;
		}
	}

	/// <summary>Restores the previous synchronization context, cancels the calls that wait for the owner, and rejects later ones.</summary>
	public void Dispose()
	{
		QueuedWork[] abandoned;

		lock (_sync)
		{
			if (_isShutDown)
				return;

			_isShutDown = true;
			abandoned = [.. _queue];

			_queue.Clear();
			_timers.Clear();

			Monitor.PulseAll(_sync);
		}

		foreach (QueuedWork work in abandoned)
			work.Completion?.TrySetCanceled();

		// The current context is thread-local, so a disposal from another thread leaves the owner's context alone.
		if (CheckAccess() && ReferenceEquals(SynchronizationContext.Current, _installedContext))
			SynchronizationContext.SetSynchronizationContext(_previousContext);
	}

	private void VerifyOwner()
	{
		if (!CheckAccess())
			throw new InvalidOperationException("Only the owner thread can pump the fake UI thread.");
	}

	/// <summary>
	/// Runs <paramref name="callback"/> inline on the owner thread, or queues it at send priority from another thread and blocks until it ran.
	/// </summary>
	/// <param name="callback">The call to run.</param>
	internal void Send(Action callback)
	{
		if (CheckAccess())
		{
			callback();
			return;
		}

		Enqueue(callback, SendRank, withCompletion: true).GetAwaiter().GetResult();
	}

	/// <summary>Runs <paramref name="callback"/> as <see cref="Send(Action)"/> does and returns its result.</summary>
	/// <typeparam name="TResult">The type the call returns.</typeparam>
	/// <param name="callback">The call to run.</param>
	/// <returns>The result of the call.</returns>
	internal TResult Send<TResult>(Func<TResult> callback)
	{
		if (CheckAccess())
			return callback();

		var result = new TResult[1];
		Enqueue(() => result[0] = callback(), SendRank, withCompletion: true).GetAwaiter().GetResult();
		return result[0];
	}

	/// <summary>
	/// Runs <paramref name="callback"/> inline on the owner thread, with a failure carried by the returned task, or queues it at normal priority
	/// from another thread; the task completes when the call ran.
	/// </summary>
	/// <param name="callback">The call to run.</param>
	/// <returns>The task that completes with the call.</returns>
	internal Task SendAsync(Action callback)
	{
		if (!CheckAccess())
			return Enqueue(callback, NormalRank, withCompletion: true);

		try
		{
			callback();
			return Task.CompletedTask;
		}
		catch (Exception exception)
		{
			return Task.FromException(exception);
		}
	}

	/// <summary>Queues <paramref name="callback"/> at the given rank; it runs only when the owner pumps.</summary>
	/// <param name="callback">The call to queue.</param>
	/// <param name="rank">One of the rank constants of this type.</param>
	internal void Post(Action callback, int rank = NormalRank) => _ = Enqueue(callback, rank, withCompletion: false);

	private Task Enqueue(Action callback, int rank, bool withCompletion)
	{
		lock (_sync)
		{
			if (_isShutDown)
				return Task.FromCanceled(new CancellationToken(canceled: true));

			TaskCompletionSource? completion = withCompletion ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : null;

			_queue.Add(new QueuedWork(rank, sequence: ++_nextSequence, callback, completion));
			Monitor.PulseAll(_sync);

			return completion?.Task ?? Task.CompletedTask;
		}
	}

	/// <summary>
	/// Creates a timer that queues <paramref name="callback"/> at background priority once the virtual clock has moved by <paramref name="dueTime"/>.
	/// </summary>
	/// <param name="dueTime">
	/// How far the clock moves before the timer comes due; a negative value never comes due, as a timer of <see cref="System.TimeProvider"/> does.
	/// </param>
	/// <param name="callback">The call to queue.</param>
	/// <returns>The timer, whose disposal also suppresses a tick that was already queued.</returns>
	internal ITimer Schedule(TimeSpan dueTime, Action callback)
	{
		lock (_sync)
		{
			var timer = new VirtualTimer(due: dueTime < TimeSpan.Zero ? TimeSpan.MaxValue : _now + dueTime, sequence: ++_nextSequence, callback);

			if (_isShutDown)
				timer.Dispose();
			else
				_timers.Add(timer);

			return timer;
		}
	}

	// Runs the next queued item: the highest priority first, and the earliest arrival within a priority.
	private bool RunNext()
	{
		QueuedWork work;

		lock (_sync)
		{
			int next = -1;

			for (int index = 0; index < _queue.Count; index++)
			{
				if (next < 0 || _queue[index].RunsBefore(_queue[next]))
					next = index;
			}

			if (next < 0)
				return false;

			work = _queue[next];
			_queue.RemoveAt(next);
		}

		work.Run();
		return true;
	}

	// Moves the clock to the due time of the earliest pending timer, unless that time is after the limit, and moves every timer that is due by
	// then into the queue at background priority, in due order, so timers due at the same instant all queue before any of them runs.
	private bool FireNextTimer(TimeSpan through)
	{
		lock (_sync)
		{
			_timers.RemoveAll(static timer => !timer.IsPending);

			if (_timers.Count == 0)
				return false;

			TimeSpan due = _timers.Min(static timer => timer.Due);

			if (due > through)
				return false;

			if (due > _now)
				_now = due;

			foreach (
				VirtualTimer timer in _timers
					.Where(timer => timer.Due <= _now)
					.OrderBy(static timer => timer.Due)
					.ThenBy(static timer => timer.Sequence)
					.ToArray()
			)
			{
				_timers.Remove(timer);
				timer.MarkQueued();

				_queue.Add(new QueuedWork(rank: BackgroundRank, sequence: ++_nextSequence, callback: timer.Run, completion: null));
			}

			return true;
		}
	}

	private sealed class QueuedWork(int rank, long sequence, Action callback, TaskCompletionSource? completion)
	{
		public TaskCompletionSource? Completion => completion;

		public bool RunsBefore(QueuedWork other) => rank != other.Rank ? rank > other.Rank : sequence < other.Sequence;

		private int Rank => rank;

		private long Sequence => sequence;

		public void Run()
		{
			if (completion is null)
			{
				callback();
				return;
			}

			try
			{
				callback();
				completion.TrySetResult();
			}
			catch (Exception exception)
			{
				completion.TrySetException(exception);
			}
		}
	}

	private sealed class VirtualTimer(TimeSpan due, long sequence, Action callback) : ITimer
	{
		private bool _isCanceled;
		private bool _isQueued;

		public TimeSpan Due => due;

		public long Sequence => sequence;

		public bool IsPending => !Volatile.Read(ref _isCanceled) && !Volatile.Read(ref _isQueued);

		public void MarkQueued() => Volatile.Write(ref _isQueued, true);

		// A tick that is already queued runs nothing once the timer was disposed, like the timer of a toolkit.
		public void Run()
		{
			if (!Volatile.Read(ref _isCanceled))
				callback();
		}

		public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException("The fake timer fires once and cannot be changed.");

		public void Dispose() => Volatile.Write(ref _isCanceled, true);

		public ValueTask DisposeAsync()
		{
			Dispose();
			return ValueTask.CompletedTask;
		}
	}

	private sealed class VirtualTimeProvider(FakePumpedUiThread owner) : TimeProvider
	{
		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
			owner.Schedule(dueTime, () => callback(state));
	}

	private sealed class PumpedSynchronizationContext(FakePumpedUiThread owner) : SynchronizationContext
	{
		public override void Post(SendOrPostCallback d, object? state) => owner.Post(() => d(state));

		public override void Send(SendOrPostCallback d, object? state) => owner.Send(() => d(state));

		public override SynchronizationContext CreateCopy() => this;
	}
}
