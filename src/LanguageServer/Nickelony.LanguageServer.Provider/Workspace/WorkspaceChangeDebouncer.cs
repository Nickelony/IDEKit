namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Debounces bursts of workspace file changes before dispatching them to the owner.
/// </summary>
internal sealed class WorkspaceChangeDebouncer : IDisposable
{
	/// <summary>
	/// Triggers dispatch of the currently accumulated workspace changes.
	/// </summary>
	private readonly Action _dispatchPendingChanges;

	/// <summary>
	/// Defines how long new changes delay the next dispatch.
	/// </summary>
	private readonly TimeSpan _debounceDelay;

	/// <summary>
	/// Bounds how long a sustained change stream may postpone a dispatch.
	/// </summary>
	private readonly TimeSpan _maxDispatchDelay;

	/// <summary>
	/// Stores the currently accumulated coalesced changes.
	/// </summary>
	private readonly WorkspaceChangeAccumulator _pendingChanges = new();

	/// <summary>
	/// Stores drained changes that must be dispatched before newer pending changes, in their original order.
	/// </summary>
	private readonly WorkspaceChangeAccumulator _replayedChanges = new();

	/// <summary>
	/// Schedules the delayed dispatch callback.
	/// </summary>
	private readonly ITimer _timer;

	/// <summary>
	/// Supplies timestamps and the debounce timer, so tests can exercise the scheduling logic against a fake clock.
	/// </summary>
	private readonly TimeProvider _timeProvider;

	/// <summary>
	/// Serializes scheduling decisions and the scheduling timestamps below.
	/// </summary>
	private readonly object _scheduleSyncRoot = new();

	/// <summary>
	/// Stores the tick count when the current pending burst started, or <see langword="null"/> when no burst is
	/// active. A nullable sentinel keeps a legitimate timestamp of <c>0</c> (a fresh fake clock) from being read
	/// as "no burst".
	/// </summary>
	private long? _firstPendingTimestamp;

	/// <summary>
	/// Tracks whether the current schedule is a retry backoff that must not be shortened.
	/// </summary>
	private bool _retryScheduled;

	/// <summary>
	/// Stores the due tick count of the installed schedule so an early or superseded tick cannot dispatch ahead of
	/// the schedule or consume the newer schedule's retry/backoff state.
	/// </summary>
	private long _scheduledDueTimestamp;

	private volatile bool _isDisposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceChangeDebouncer"/> class.
	/// </summary>
	/// <param name="debounceDelay">The delay applied after the most recent change.</param>
	/// <param name="maxDispatchDelay">The maximum time a change may wait for dispatch while new changes keep arriving.</param>
	/// <param name="dispatchPendingChanges">The callback that dispatches the accumulated changes.</param>
	/// <param name="timeProvider">
	/// The time provider used for timestamps and the debounce timer, or <see langword="null"/> for the system
	/// provider. Tests supply a fake provider to exercise the scheduling logic without wall-clock waits.
	/// </param>
	public WorkspaceChangeDebouncer(TimeSpan debounceDelay, TimeSpan maxDispatchDelay, Action dispatchPendingChanges, TimeProvider? timeProvider = null)
	{
		_debounceDelay = debounceDelay;
		_maxDispatchDelay = maxDispatchDelay;
		_dispatchPendingChanges = dispatchPendingChanges;
		_timeProvider = timeProvider ?? TimeProvider.System;

		_timer = _timeProvider.CreateTimer(OnDebounceTick, state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
	}

	/// <summary>
	/// Reads the current monotonic timestamp in milliseconds from the injected time provider.
	/// </summary>
	/// <returns>The current timestamp in milliseconds.</returns>
	/// <remarks>
	/// The conversion divides before multiplying (with an exact remainder term): multiplying the raw
	/// high-frequency timestamp by 1000 first can overflow the 64-bit product on a long-running machine
	/// (a 1 GHz provider wraps after roughly 107 days of uptime), which would jump the deadlines the
	/// debounce scheduling derives from it.
	/// </remarks>
	private long GetTimestampMilliseconds()
	{
		long timestamp = _timeProvider.GetTimestamp();
		long frequency = _timeProvider.TimestampFrequency;

		// A conforming provider reports a positive frequency; a mis-implemented one that reports zero or a
		// negative value would divide by zero, so fall back to the raw tick count. Only relative comparisons
		// consume this value, so any monotonic scale keeps the debounce scheduling correct.
		if (frequency <= 0)
			return timestamp;

		return (timestamp / frequency * 1000) + (timestamp % frequency * 1000 / frequency);
	}

	/// <summary>
	/// Gets a value indicating whether the debouncer has no pending changes.
	/// </summary>
	public bool IsEmpty => _pendingChanges.IsEmpty && _replayedChanges.IsEmpty;

	/// <summary>
	/// Queues a single file change and restarts the debounce timer without exceeding the maximum dispatch latency.
	/// A pending retry backoff is never shortened by an arriving change; the change joins the batch that the
	/// scheduled retry drains.
	/// </summary>
	/// <param name="filePath">The changed file path.</param>
	/// <param name="kind">The change kind.</param>
	public void Queue(string filePath, FileChangeKind kind)
	{
		if (_isDisposed || string.IsNullOrEmpty(filePath))
			return;

		_pendingChanges.Add(filePath, kind);

		long now = GetTimestampMilliseconds();

		lock (_scheduleSyncRoot)
		{
			if (_firstPendingTimestamp is null)
				_firstPendingTimestamp = now;

			// A pending retry already carries the coalesced change and its drain reads both buffers; keep its backoff
			// exactly as scheduled.
			if (_retryScheduled)
				return;

			long latestDueTimestamp = _firstPendingTimestamp.Value + (long)_maxDispatchDelay.TotalMilliseconds;
			long dueTimestamp = Math.Min(now + (long)_debounceDelay.TotalMilliseconds, latestDueTimestamp);

			ScheduleDispatchAt(dueTimestamp);
		}
	}

	/// <summary>
	/// Drains the currently pending changes into a forwardable batch.
	/// </summary>
	/// <remarks>
	/// Replayed changes are drained first so changes that failed to dispatch keep their original order ahead of the
	/// changes that arrived while the dispatch was in flight. Pending changes that arrived during a retry backoff
	/// follow in the same batch, so a retry can never strand a change that was queued while it was scheduled.
	/// </remarks>
	/// <returns>The drained file-change batch.</returns>
	public FileChangeBatch DrainBatch()
	{
		if (_replayedChanges.IsEmpty)
			return _pendingChanges.DrainBatch();

		FileChangeBatch replayedBatch = _replayedChanges.DrainBatch();

		if (_pendingChanges.IsEmpty)
			return replayedBatch;

		FileChangeBatch pendingBatch = _pendingChanges.DrainBatch();

		var combinedChanges = new List<WorkspaceFileChange>(replayedBatch.Count + pendingBatch.Count);
		combinedChanges.AddRange(replayedBatch.Entries);
		combinedChanges.AddRange(pendingBatch.Entries);

		return new FileChangeBatch(combinedChanges);
	}

	/// <summary>
	/// Requeues a drained batch and schedules another dispatch attempt after the retry delay.
	/// </summary>
	/// <param name="batch">The batch to requeue.</param>
	/// <param name="dispatchDelay">The delay before the next retry attempt.</param>
	public void Requeue(FileChangeBatch batch, TimeSpan? dispatchDelay = null)
	{
		if (_isDisposed || batch.Count == 0)
			return;

		long now = GetTimestampMilliseconds();

		lock (_scheduleSyncRoot)
		{
			// The replayed changes keep their drained order and stay in front of the changes that arrived while the
			// failed dispatch was in flight, so an older delete cannot cancel against a newer create.
			for (int i = 0; i < batch.Count; i++)
				_replayedChanges.Add(batch.Entries[i].Path, batch.Entries[i].Kind);

			if (_firstPendingTimestamp is null)
				_firstPendingTimestamp = now;

			_retryScheduled = true;
			ScheduleDispatchAt(now + (long)(dispatchDelay ?? _debounceDelay).TotalMilliseconds);
		}
	}

	/// <summary>
	/// Stops the debounce timer without discarding the currently buffered changes.
	/// </summary>
	public void Stop()
	{
		if (_isDisposed)
			return;

		try
		{
			lock (_scheduleSyncRoot)
			{
				// Clear the scheduling state together with the timer: a later Queue must be able to arm a fresh
				// schedule instead of observing a retry stamp that can never fire again.
				_retryScheduled = false;
				_firstPendingTimestamp = null;
				_timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
			}
		}
		catch (ObjectDisposedException)
		{ }
	}

	/// <summary>
	/// Stops the timer and releases the debouncer resources.
	/// </summary>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		_timer.Dispose();
	}

	/// <summary>
	/// Dispatches pending changes when the debounce timer fires.
	/// </summary>
	/// <param name="_">Unused timer state.</param>
	private void OnDebounceTick(object? _)
	{
		lock (_scheduleSyncRoot)
		{
			long remainingDelay = _scheduledDueTimestamp - GetTimestampMilliseconds();

			// A timer can fire slightly before its due time, and a callback that was already queued when a newer
			// schedule was installed still fires for the superseded schedule. Both ticks belong to an installed
			// schedule, so wait out the remainder instead of dispatching early, clearing the newer schedule's
			// retry/backoff state, or dropping the scheduled dispatch.
			if (remainingDelay > 0)
			{
				ScheduleDispatchAt(_scheduledDueTimestamp);
				return;
			}

			_retryScheduled = false;
			_firstPendingTimestamp = null;
		}

		if (_isDisposed || IsEmpty)
			return;

		_dispatchPendingChanges();
	}

	/// <summary>
	/// Schedules the next dispatch at the supplied absolute timestamp.
	/// The caller must hold <see cref="_scheduleSyncRoot"/>.
	/// </summary>
	/// <param name="dueTimestamp">The absolute timestamp in milliseconds from the injected time provider when the dispatch should run.</param>
	private void ScheduleDispatchAt(long dueTimestamp)
	{
		_scheduledDueTimestamp = dueTimestamp;

		try
		{
			_timer.Change(TimeSpan.FromMilliseconds(Math.Max(0, dueTimestamp - GetTimestampMilliseconds())), Timeout.InfiniteTimeSpan);
		}
		catch (ObjectDisposedException)
		{
			// Disposal won the race; the pending changes are dropped together with the debouncer.
		}
	}
}
