namespace Nickelony.IDEKit.Core.Threading;

/// <summary>
/// A toolkit-neutral UI dispatcher: the interface shared editor code uses to marshal work onto the UI thread without referencing a toolkit's
/// dispatcher type.
/// </summary>
/// <remarks>
/// An editor binding adapts its engine's dispatcher, so a consumer can configure a shared controller with one dispatcher value;
/// a host that runs its UI on its own scheduler can implement this interface over that scheduler. <see cref="Invoke"/> and <see cref="InvokeAsync"/>
/// run inline when already on the dispatcher thread and marshal otherwise; <see cref="Post"/> and <see cref="Schedule"/>
/// never block the caller. A dispatcher that is shutting down may reject a marshaled call with an <see cref="OperationCanceledException"/>
/// or an <see cref="InvalidOperationException"/>, which the controllers treat as the end of the work.
/// </remarks>
public interface IUiDispatcher
{
	bool CheckAccess();

	/// <summary>Verifies that the calling thread is the dispatcher thread.</summary>
	/// <exception cref="InvalidOperationException">The calling thread is not the dispatcher thread.</exception>
	void VerifyAccess();

	/// <summary>Runs <paramref name="callback"/> on the dispatcher thread, blocking the caller until it returns.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="callback"/> is <see langword="null"/>.</exception>
	void Invoke(Action callback);

	/// <summary>Runs <paramref name="callback"/> on the dispatcher thread, blocking the caller until it returns, and returns its result.</summary>
	/// <typeparam name="TResult">The callback's result type.</typeparam>
	/// <param name="callback">The callback to run.</param>
	/// <returns>The value returned by <paramref name="callback"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="callback"/> is <see langword="null"/>.</exception>
	TResult Invoke<TResult>(Func<TResult> callback);

	/// <summary>
	/// Runs <paramref name="callback"/> on the dispatcher thread without blocking the caller when the caller is not already on the dispatcher thread.
	/// </summary>
	/// <param name="callback">The callback to run.</param>
	/// <returns>A task that completes after the callback has run on the dispatcher.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="callback"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// No overload returns the callback's result, so a caller captures it in a local. A result-returning overload can come later as a default
	/// interface member written over this one, so no implementer has to change.
	/// </remarks>
	Task InvokeAsync(Action callback);

	/// <summary>Queues <paramref name="callback"/> to run on the dispatcher thread without waiting for it.</summary>
	/// <param name="callback">The callback to run.</param>
	/// <param name="priority">The priority the callback is queued at.</param>
	/// <exception cref="ArgumentNullException"><paramref name="callback"/> is <see langword="null"/>.</exception>
	void Post(Action callback, UiDispatcherPriority priority = UiDispatcherPriority.Normal);

	/// <summary>Schedules <paramref name="callback"/> to run once on the dispatcher thread after <paramref name="delay"/>.</summary>
	/// <param name="delay">The delay before the callback runs.</param>
	/// <param name="callback">The callback to run.</param>
	/// <returns>A handle that cancels the scheduled callback when disposed before it runs.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="callback"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="delay"/> is negative.</exception>
	/// <remarks>
	/// Arming a new schedule does not cancel a previous one, so a caller that wants replacement semantics disposes the previous handle first;
	/// the returned handle is a no-op once the callback has run.
	/// </remarks>
	IDisposable Schedule(TimeSpan delay, Action callback);
}
