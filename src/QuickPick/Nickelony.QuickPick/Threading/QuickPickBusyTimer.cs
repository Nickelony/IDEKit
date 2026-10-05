namespace Nickelony.QuickPick;

/// <summary>Runs a callback once when a query has been running for the busy delay, unless the query ends first.</summary>
/// <remarks>
/// Starting the timer replaces the one in flight. A timer that was stopped after it came due still runs nothing, because the dispatcher suppresses
/// the tick of a disposed handle.
/// </remarks>
internal sealed class QuickPickBusyTimer
{
	private readonly QuickPickDispatcher _dispatcher;
	private readonly TimeSpan _delay;

	private IDisposable? _pending;

	/// <param name="dispatcher">The dispatcher the callback runs on.</param>
	/// <param name="delay">How long a query runs before it counts as slow.</param>
	internal QuickPickBusyTimer(QuickPickDispatcher dispatcher, TimeSpan delay)
	{
		_dispatcher = dispatcher;
		_delay = delay;
	}

	/// <summary>Starts the delay, replacing the one in flight.</summary>
	/// <param name="onElapsed">Runs on the dispatcher when the delay passed.</param>
	internal void Start(Action onElapsed)
	{
		Stop();
		_pending = _dispatcher.Schedule(_delay, onElapsed);
	}

	/// <summary>Cancels the delay in flight, if any.</summary>
	internal void Stop()
	{
		_pending?.Dispose();
		_pending = null;
	}
}
