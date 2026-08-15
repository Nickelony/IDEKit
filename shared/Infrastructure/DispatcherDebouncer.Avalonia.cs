using Avalonia.Threading;
using System.Diagnostics;

namespace Nickelony.IDEKit.Infrastructure;

/// <summary>
/// Runs a single debounced callback on a dispatcher: arming the debouncer replaces any pending
/// callback, and the last armed callback runs once after the configured delay.
/// </summary>
/// <remarks>
/// <para>
/// The debouncer runs its callback on the dispatcher supplied at construction, or on the UI thread's
/// dispatcher when none is supplied, because <see cref="DispatcherTimer"/> fires on its associated
/// dispatcher. <see cref="Arm"/> captures the pending callback, so callers can hand over
/// per-invocation state through the callback closure instead of tracking it in fields of their own.
/// </para>
/// <para>
/// The helper is deliberately per-toolkit rather than a neutral abstraction: it is compiled into the one
/// Avalonia binding that needs it, and another toolkit binding ports the shape (a restartable timer on its own
/// dispatcher) instead of sharing this type. A neutral seam in Core would have no consumer - no neutral
/// package debounces - and the timer's thread affinity and priority are the toolkit's to define. See
/// <c>docs/EditorBindingGuide.md</c>, which lists this pair under the intentional per-binding pieces.
/// </para>
/// </remarks>
internal sealed class DispatcherDebouncer : IDisposable
{
	private readonly DispatcherTimer _timer;
	private Action? _callback;
	private bool _isDisposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="DispatcherDebouncer"/> class.
	/// </summary>
	/// <param name="delay">The debounce delay before an armed callback runs. Must not be negative.</param>
	/// <param name="dispatcher">
	/// The dispatcher the callback runs on, or <see langword="null"/> for the UI thread's dispatcher.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/> is negative.</exception>
	public DispatcherDebouncer(TimeSpan delay, Dispatcher? dispatcher = null)
	{
		if (delay < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(delay), delay, "The debounce delay must not be negative.");

		_timer = new DispatcherTimer(delay, DispatcherPriority.Background, dispatcher ?? Dispatcher.UIThread);

		_timer.Tick += HandleTick;
	}

	/// <summary>
	/// Gets a value indicating whether a callback is armed and waiting for its debounce delay.
	/// </summary>
	public bool IsPending => _timer.IsEnabled;

	/// <summary>
	/// Arms the debouncer with the callback to run after the debounce delay, replacing any callback
	/// armed before it. Arming again restarts the delay.
	/// </summary>
	/// <param name="callback">The callback to run when the delay elapses; must not be <see langword="null"/>.</param>
	public void Arm(Action callback)
	{
		// A null callback would silently disarm the debouncer, so it fails in debug builds; the member is
		// internal, and in-assembly callers are trusted in release builds.
		Debug.Assert(callback is not null, "The armed callback must not be null.");

		if (_isDisposed)
			return;

		_callback = callback;
		_timer.Stop();
		_timer.Start();
	}

	/// <summary>
	/// Cancels the armed callback, if any, so it never runs. Has no effect on an already running callback
	/// or on a disposed debouncer.
	/// </summary>
	public void Cancel()
	{
		if (_isDisposed)
			return;

		_timer.Stop();
		_callback = null;
	}

	/// <summary>
	/// Cancels any armed callback and stops the debouncer. Further <see cref="Arm"/> calls are ignored.
	/// </summary>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		_timer.Stop();
		_timer.Tick -= HandleTick;
		_callback = null;
	}

	private void HandleTick(object? sender, EventArgs e)
	{
		_timer.Stop();

		Action? callback = _callback;
		_callback = null;
		callback?.Invoke();
	}
}
