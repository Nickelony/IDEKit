using System.Diagnostics;
using System.Windows.Threading;

namespace Nickelony.Testing;

/// <summary>Provides dispatcher pumping helpers for tests that wait for timer or posted dispatcher work.</summary>
/// <remarks>
/// The overloads without a priority parameter keep the call shape of the Avalonia twin, whose <c>DispatcherPriority</c> is a struct
/// and so cannot be the default of an optional parameter. Those overloads run at <see cref="DispatcherPriority.Background"/>.
/// </remarks>
internal static class DispatcherTestHelpers
{
	/// <summary>Pumps dispatcher frames until the condition holds or the deadline elapses, then asserts the condition.</summary>
	/// <param name="condition">The condition to wait for.</param>
	/// <param name="timeout">The optional timeout; defaults to five seconds.</param>
	internal static void PumpUntil(Func<bool> condition, TimeSpan? timeout = null) => PumpUntil(condition, timeout, DispatcherPriority.Background);

	/// <summary>Pumps dispatcher frames until the condition holds or the deadline elapses, then asserts the condition.</summary>
	/// <param name="condition">The condition to wait for.</param>
	/// <param name="priority">
	/// The priority of the frame-exit callback, as described on <see cref="PumpUntil(Func{bool}, TimeSpan?, DispatcherPriority)"/>.
	/// </param>
	internal static void PumpUntil(Func<bool> condition, DispatcherPriority priority) => PumpUntil(condition, null, priority);

	/// <summary>Pumps dispatcher frames until the condition holds or the deadline elapses, then asserts the condition.</summary>
	/// <param name="condition">The condition to wait for.</param>
	/// <param name="timeout">The optional timeout; defaults to five seconds.</param>
	/// <param name="priority">
	/// The priority of the frame-exit callback. The exit callback runs before every item of a lower priority,
	/// so pass <see cref="DispatcherPriority.ApplicationIdle"/> or lower when posted <see cref="DispatcherPriority.ContextIdle"/>
	/// work must run before the frame exits; <see cref="DispatcherPriority.Background"/>, which the overloads without a priority
	/// parameter run at, never reaches context-idle work.
	/// </param>
	internal static void PumpUntil(Func<bool> condition, TimeSpan? timeout, DispatcherPriority priority)
	{
		var stopwatch = Stopwatch.StartNew();
		TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5.0);

		while (!condition() && stopwatch.Elapsed < effectiveTimeout)
			PumpOneFrame(priority);

		Assert.IsTrue(condition(), "The expected dispatcher work did not complete within the allotted time.");
	}

	/// <summary>
	/// Runs the dispatcher's queued work up to the default background priority and returns once a sentinel posted at that priority has run,
	/// so every item queued ahead of it - including awaits that resumed on the dispatcher - has completed.
	/// </summary>
	internal static void PumpUntilIdle() => PumpUntilIdle(DispatcherPriority.Background);

	/// <summary>Runs the dispatcher's queued work up to the given priority and returns once a sentinel posted at that priority has run.</summary>
	/// <param name="priority">
	/// The priority of the sentinel. Work posted at the same or a higher priority that was already queued runs before the sentinel;
	/// pass <see cref="DispatcherPriority.ApplicationIdle"/> to also drain context-idle work. This never advances the clock, so it does not run
	/// a timer whose delay has not elapsed.
	/// </param>
	/// <remarks>
	/// Use this to make a negative assertion observable when the guarded action is a posted or resumed continuation:
	/// the drain is a deterministic barrier, so the assertion does not depend on a wall-clock window.
	/// </remarks>
	internal static void PumpUntilIdle(DispatcherPriority priority) => Dispatcher.CurrentDispatcher.Invoke(priority, new Action(static () => { }));

	/// <summary>Pumps dispatcher frames for the given duration at the default background priority.</summary>
	/// <param name="duration">How long to keep pumping.</param>
	internal static void PumpFrames(TimeSpan duration) => PumpFrames(duration, DispatcherPriority.Background);

	/// <summary>Pumps dispatcher frames for the given duration.</summary>
	/// <param name="duration">How long to keep pumping.</param>
	/// <param name="priority">
	/// The priority of the frame-exit callback, as described on <see cref="PumpUntil(Func{bool}, TimeSpan?, DispatcherPriority)"/>.
	/// </param>
	/// <remarks>
	/// The last resort for a timer-driven negative assertion ("must not fire", "must not run"): a timer that has not elapsed
	/// has no completion signal, so a bounded pump is the only mechanism, and the assertion must check an explicit counter the guarded
	/// action would increment. Prefer <see cref="PumpUntil(Func{bool}, TimeSpan?)"/> or <see cref="PumpUntilIdle()"/>; a fixed-duration pump
	/// only ever false-passes under load, never false-fails.
	/// </remarks>
	internal static void PumpFrames(TimeSpan duration, DispatcherPriority priority)
	{
		var stopwatch = Stopwatch.StartNew();

		while (stopwatch.Elapsed < duration)
			PumpOneFrame(priority);
	}

	/// <summary>Pumps one dispatcher frame, whose exit callback is posted at the given priority.</summary>
	private static void PumpOneFrame(DispatcherPriority priority)
	{
		var frame = new DispatcherFrame();

		Dispatcher.CurrentDispatcher.BeginInvoke(priority, new Action(() => frame.Continue = false));
		Dispatcher.PushFrame(frame);
	}
}
