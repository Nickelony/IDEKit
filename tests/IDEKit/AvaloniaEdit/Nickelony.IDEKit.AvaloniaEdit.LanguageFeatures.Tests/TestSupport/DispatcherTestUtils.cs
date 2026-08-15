using Avalonia.Threading;
using System.Diagnostics;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

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
	internal static void PumpUntil(Func<bool> condition, TimeSpan? timeout = null)
		=> PumpUntil(condition, timeout, DispatcherPriority.Background);

	/// <summary>
	/// Pumps dispatcher frames until the condition holds or the deadline elapses, then asserts the condition.
	/// </summary>
	/// <param name="condition">The condition to wait for.</param>
	/// <param name="priority">
	/// The priority of the frame-exit callback. The exit callback runs before every item of a lower priority,
	/// so pass <see cref="DispatcherPriority.ApplicationIdle"/> or lower when posted
	/// <see cref="DispatcherPriority.ContextIdle"/> work must run before the frame exits; the default
	/// <see cref="DispatcherPriority.Background"/> never reaches context-idle work.
	/// </param>
	/// <remarks>
	/// This overload lets a shared test name the priority without also naming the timeout. The WPF reference's
	/// optional-parameter shape accepts the same call, so a shared call site compiles unchanged in either build.
	/// </remarks>
	internal static void PumpUntil(Func<bool> condition, DispatcherPriority priority)
		=> PumpUntil(condition, null, priority);

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
	/// <remarks>
	/// Avalonia's <see cref="DispatcherPriority"/> is a struct with static readonly members, not an enum, so it
	/// cannot be an optional parameter's default value; the no-priority overload (the reference's default
	/// argument) forwards <see cref="DispatcherPriority.Background"/> instead.
	/// </remarks>
	internal static void PumpUntil(Func<bool> condition, TimeSpan? timeout, DispatcherPriority priority)
	{
		var stopwatch = Stopwatch.StartNew();
		TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(5.0);

		while (!condition() && stopwatch.Elapsed < effectiveTimeout)
		{
			var frame = new DispatcherFrame();
			Dispatcher.UIThread.Post(() => frame.Continue = false, priority);
			Dispatcher.UIThread.PushFrame(frame);
		}

		Assert.IsTrue(condition(), "The expected dispatcher work did not complete within the allotted time.");
	}

	/// <summary>
	/// Runs the dispatcher's queued work up to the default background priority and returns once a sentinel
	/// posted at that priority has run, so every item queued ahead of it - including awaits that resumed on the
	/// dispatcher - has completed.
	/// </summary>
	internal static void PumpUntilIdle()
		=> PumpUntilIdle(DispatcherPriority.Background);

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
	/// window. Use <see cref="PumpFrames(TimeSpan)"/> only for the timer-driven case, where there is no
	/// completion signal to synchronize on. Avalonia's <see cref="DispatcherPriority"/> is a struct with static
	/// readonly members, so the default background priority is supplied by a no-argument overload instead.
	/// </remarks>
	internal static void PumpUntilIdle(DispatcherPriority priority)
		=> Dispatcher.UIThread.Invoke(static () => { }, priority);

	/// <summary>
	/// Pumps dispatcher frames for the given duration at the default background priority.
	/// </summary>
	/// <param name="duration">How long to keep pumping.</param>
	internal static void PumpFrames(TimeSpan duration)
		=> PumpFrames(duration, DispatcherPriority.Background);

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
	/// guarded action would increment. Prefer <see cref="PumpUntil(Func{bool}, TimeSpan?)"/> with a completion
	/// condition or a stable counter, and <see cref="PumpUntilIdle()"/> for posted or resumed continuations; a
	/// fixed-duration pump only ever false-passes under load, never false-fails. Avalonia's
	/// <see cref="DispatcherPriority"/> is a struct with static readonly members, so the default background
	/// priority is supplied by a no-priority overload instead.
	/// </remarks>
	internal static void PumpFrames(TimeSpan duration, DispatcherPriority priority)
	{
		var stopwatch = Stopwatch.StartNew();

		while (stopwatch.Elapsed < duration)
		{
			var frame = new DispatcherFrame();
			Dispatcher.UIThread.Post(() => frame.Continue = false, priority);
			Dispatcher.UIThread.PushFrame(frame);
		}
	}
}
