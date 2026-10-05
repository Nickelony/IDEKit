#if QUICKPICKFAMILY
// The family has no toolkit-neutral dispatcher contract; the debouncer needs only the scheduling member that its own dispatcher has.
using IUiDispatcher = Nickelony.QuickPick.QuickPickDispatcher;

namespace Nickelony.QuickPick;

#else
using Nickelony.IDEKit.Core.Validation;

namespace Nickelony.IDEKit.Core.Threading;

#endif

/// <summary>
/// Runs a single debounced callback on a UI dispatcher: arming the debouncer replaces any pending callback, and the last armed callback runs
/// once after the configured delay.
/// </summary>
/// <remarks>
/// Arming and cancellation are dispatcher-thread affine, matching the timer the dispatcher creates. A schedule that was replaced or canceled
/// never runs its callback.
/// </remarks>
#if QUICKPICKFAMILY
internal
#else
public
#endif
sealed class DispatcherDebouncer : IDisposable
{
	private readonly TimeSpan _delay;
	private readonly IUiDispatcher _dispatcher;

	private IDisposable? _pending;
	private bool _isDisposed;

	/// <param name="delay">The debounce delay before an armed callback runs. Must not be negative.</param>
	/// <param name="dispatcher">The dispatcher the callback runs on.</param>
	/// <exception cref="ArgumentNullException"><paramref name="dispatcher"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/> is negative.</exception>
	public DispatcherDebouncer(TimeSpan delay, IUiDispatcher dispatcher)
	{
		ArgumentNullException.ThrowIfNull(dispatcher);

		_delay = NumericValidation.NonNegative(delay, nameof(delay));
		_dispatcher = dispatcher;
	}

	/// <summary>Indicates whether a callback is armed and waiting for its debounce delay.</summary>
	public bool IsPending => _pending is not null;

	/// <summary>
	/// Arms the debouncer with the callback to run after the debounce delay, replacing any callback armed before it. Arming again restarts the delay.
	/// </summary>
	/// <param name="callback">The callback to run when the delay elapses.</param>
	/// <exception cref="ArgumentNullException"><paramref name="callback"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// A disposed debouncer ignores the call.
	/// </remarks>
	public void Arm(Action callback)
	{
		ArgumentNullException.ThrowIfNull(callback);
		ArmCore(callback, _delay);
	}

	/// <summary>
	/// Arms the debouncer with the callback to run after another delay than the configured one, replacing any callback armed before it.
	/// </summary>
	/// <param name="callback">The callback to run when the delay elapses.</param>
	/// <param name="delay">The delay for this arming. Must not be negative.</param>
	/// <exception cref="ArgumentNullException"><paramref name="callback"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/> is negative.</exception>
	/// <remarks>
	/// The delay applies to this arming only, so a later call of <see cref="Arm(Action)"/> uses the configured delay again.
	/// </remarks>
	public void Arm(Action callback, TimeSpan delay)
	{
		ArgumentNullException.ThrowIfNull(callback);
		NumericValidation.NonNegative(delay, nameof(delay));

		ArmCore(callback, delay);
	}

	private void ArmCore(Action callback, TimeSpan delay)
	{
		if (_isDisposed)
			return;

		// The previous handle is cleared before the new schedule is requested: when the dispatcher rejects
		// the schedule (for example while it shuts down), a dangling reference to the disposed handle would
		// otherwise keep IsPending true forever.
		_pending?.Dispose();
		_pending = null;

		_pending = _dispatcher.Schedule(
			delay,

			() =>
			{
				_pending = null;
				callback();
			}
		);
	}

	/// <summary>
	/// Cancels the armed callback, if any, so it never runs. Has no effect on an already running callback or on a disposed debouncer.
	/// </summary>
	public void Cancel()
	{
		if (_isDisposed)
			return;

		_pending?.Dispose();
		_pending = null;
	}

	/// <summary>Cancels any armed callback and stops the debouncer.</summary>
	public void Dispose()
	{
		if (_isDisposed)
			return;

		_isDisposed = true;
		_pending?.Dispose();
		_pending = null;
	}
}
