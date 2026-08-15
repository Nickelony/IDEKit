using System.Diagnostics;
using System.Windows.Threading;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>
/// Provides dispatcher pumping helpers for tests that wait for timer or posted dispatcher work.
/// </summary>
internal static class DispatcherTestUtils
{
	/// <summary>
	/// Pumps dispatcher frames until the condition holds or the deadline elapses, then asserts the condition.
	/// </summary>
	/// <param name="condition">The condition to wait for.</param>
	/// <param name="timeout">The optional timeout; defaults to five seconds.</param>
	/// <param name="priority">
	/// The priority of the frame-exit callback. The exit callback runs before every item of a lower priority,
	/// so pass <see cref="DispatcherPriority.ApplicationIdle"/> or lower when posted
	/// <see cref="DispatcherPriority.ContextIdle"/> work must run before the frame exits; the default
	/// <see cref="DispatcherPriority.Background"/> never reaches context-idle work.
	/// </param>
	internal static void PumpUntil(
		Func<bool> condition,
		TimeSpan? timeout = null,
		DispatcherPriority priority = DispatcherPriority.Background)
	{
		var stopwatch = Stopwatch.StartNew();
		TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5.0);

		while (!condition() && stopwatch.Elapsed < effectiveTimeout)
		{
			var frame = new DispatcherFrame();
			Dispatcher.CurrentDispatcher.BeginInvoke(priority, new Action(() => frame.Continue = false));
			Dispatcher.PushFrame(frame);
		}

		Assert.IsTrue(condition(), "The expected dispatcher work did not complete within the allotted time.");
	}

	/// <summary>
	/// Runs the dispatcher's queued work up to the given priority and returns once a sentinel posted at that
	/// priority has run, so every item queued ahead of it - including awaits that resumed on the dispatcher -
	/// has completed.
	/// </summary>
	/// <param name="priority">
	/// The priority of the sentinel. Work posted at the same or a higher priority that was already queued runs
	/// before the sentinel; pass <see cref="DispatcherPriority.ApplicationIdle"/> to also drain context-idle
	/// work. This never advances the clock, so it does not run a timer whose delay has not elapsed.
	/// </param>
	/// <remarks>
	/// Use this to make a negative assertion observable when the guarded action is a posted or resumed
	/// continuation: the drain is a deterministic barrier, so the assertion no longer depends on a wall-clock
	/// window. Use <see cref="PumpFrames"/> only for the timer-driven case, where there is no completion signal
	/// to synchronize on.
	/// </remarks>
	internal static void PumpUntilIdle(DispatcherPriority priority = DispatcherPriority.Background)
		=> Dispatcher.CurrentDispatcher.Invoke(priority, new Action(static () => { }));

	/// <summary>
	/// Pumps dispatcher frames for the given duration.
	/// </summary>
	/// <param name="duration">How long to keep pumping.</param>
	/// <param name="priority">
	/// The priority of the frame-exit callback. The exit callback runs before every item of a lower priority,
	/// so pass <see cref="DispatcherPriority.ApplicationIdle"/> or lower when posted
	/// <see cref="DispatcherPriority.ContextIdle"/> work must run during the pump; the default
	/// <see cref="DispatcherPriority.Background"/> never reaches context-idle work.
	/// </param>
	/// <remarks>
	/// This is the last resort for a timer-driven negative assertion ("the debounce must not fire", "the
	/// dropped request must not run"): a timer that has not elapsed has no completion signal to synchronize
	/// on, so a bounded pump is the only mechanism, and the assertion must check an explicit counter the
	/// guarded action would increment. Prefer <see cref="PumpUntil"/> with a completion condition or a stable
	/// counter, and <see cref="PumpUntilIdle"/> for posted or resumed continuations; a fixed-duration pump only
	/// ever false-passes under load, never false-fails.
	/// </remarks>
	internal static void PumpFrames(TimeSpan duration, DispatcherPriority priority = DispatcherPriority.Background)
	{
		var stopwatch = Stopwatch.StartNew();

		while (stopwatch.Elapsed < duration)
		{
			var frame = new DispatcherFrame();
			Dispatcher.CurrentDispatcher.BeginInvoke(priority, new Action(() => frame.Continue = false));
			Dispatcher.PushFrame(frame);
		}
	}
}
