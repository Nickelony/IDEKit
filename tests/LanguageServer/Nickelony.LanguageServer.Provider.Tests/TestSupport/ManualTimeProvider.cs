namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Provides a manually advanced clock and one-shot timers so scheduling tests can run without wall-clock waits.
/// </summary>
/// <remarks>
/// The provider is intended for single-threaded test usage: timers fire on the thread that calls
/// <see cref="Advance"/>, exactly like a fake clock in a test loop. The clock origin is configurable so a test can
/// start it at <c>0</c> and prove a legitimate zero timestamp is not confused with a scheduling sentinel.
/// </remarks>
internal sealed class ManualTimeProvider : TimeProvider
{
	private readonly List<ManualTimer> _timers = [];

	private readonly long _timestampFrequency;
	private long _timestamp;

	/// <summary>
	/// Initializes a new instance of the <see cref="ManualTimeProvider"/> class with the default clock origin and
	/// frequency.
	/// </summary>
	public ManualTimeProvider()
		: this(TimeSpan.TicksPerHour, TimeSpan.TicksPerSecond)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="ManualTimeProvider"/> class.
	/// </summary>
	/// <param name="startTimestamp">The initial timestamp, expressed in provider ticks.</param>
	/// <param name="timestampFrequency">The number of ticks per second the provider reports.</param>
	public ManualTimeProvider(long startTimestamp, long timestampFrequency)
	{
		_timestamp = startTimestamp;
		_timestampFrequency = timestampFrequency;
	}

	/// <inheritdoc/>
	public override long TimestampFrequency => _timestampFrequency;

	/// <summary>
	/// Gets the current fake time in milliseconds.
	/// </summary>
	public long ElapsedMilliseconds
		=> (GetTimestamp() / TimestampFrequency * 1000) + (GetTimestamp() % TimestampFrequency * 1000 / TimestampFrequency);

	/// <inheritdoc/>
	public override long GetTimestamp() => _timestamp;

	/// <summary>
	/// Advances the clock by the supplied delta and fires every timer that is due at the new time.
	/// </summary>
	/// <param name="delta">The amount of time to advance.</param>
	public void Advance(TimeSpan delta)
	{
		List<ManualTimer> dueTimers;

		_timestamp += delta.Ticks;

		if (_timers.Count == 0)
			return;

		dueTimers = [.. _timers.Where(timer => timer.DueTimestamp <= _timestamp)];

		// Callbacks run outside the provider so a callback that schedules another timer cannot deadlock the fake.
		foreach (ManualTimer timer in dueTimers)
			timer.Fire();
	}

	/// <inheritdoc/>
	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		ArgumentNullException.ThrowIfNull(callback);

		var timer = new ManualTimer(this, callback, state);
		timer.Change(dueTime, period);
		_timers.Add(timer);
		return timer;
	}

	/// <summary>
	/// A one-shot timer driven by <see cref="Advance"/>.
	/// </summary>
	private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
	{
		private bool _disposed;
		private long _periodTicks;

		/// <summary>
		/// Gets the timestamp when the timer is due, or <see cref="long.MaxValue"/> while it is unscheduled.
		/// </summary>
		public long DueTimestamp { get; private set; } = long.MaxValue;

		/// <summary>
		/// Invokes the timer callback and reschedules the timer for a repeating period.
		/// </summary>
		public void Fire()
		{
			if (_disposed)
				return;

			DueTimestamp = _periodTicks > 0 ? DueTimestamp + _periodTicks : long.MaxValue;
			callback(state);
		}

		/// <inheritdoc/>
		public bool Change(TimeSpan dueTime, TimeSpan period)
		{
			if (_disposed)
				return false;

			DueTimestamp = dueTime == Timeout.InfiniteTimeSpan || dueTime < TimeSpan.Zero
				? long.MaxValue
				: owner.GetTimestamp() + dueTime.Ticks;

			_periodTicks = period > TimeSpan.Zero ? period.Ticks : 0;
			return true;
		}

		/// <inheritdoc/>
		public void Dispose() => _disposed = true;

		/// <inheritdoc/>
		public ValueTask DisposeAsync()
		{
			Dispose();
			return ValueTask.CompletedTask;
		}
	}
}
