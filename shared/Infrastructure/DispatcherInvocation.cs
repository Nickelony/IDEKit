using System.Windows.Threading;

namespace Nickelony.IDEKit.Infrastructure;

/// <summary>
/// Runs callbacks on a specific dispatcher so the controllers keep their documented thread affinity
/// without relying on the ambient synchronization context of the thread that started an operation.
/// </summary>
/// <remarks>
/// <para>
/// Controllers promise that provider continuations resume on the thread that owns the state they touch,
/// but <c>ConfigureAwait(true)</c> can only keep that promise when the creating thread captured a
/// synchronization context. Hosts that pump a dispatcher manually, tooling, tests, and pre-application
/// setup have no context, so an asynchronous provider resumes on a thread-pool thread there. These
/// helpers marshal explicitly in that case; on the target dispatcher thread the callback runs directly,
/// so the common case adds no dispatch.
/// </para>
/// <para>
/// The helper is deliberately per-toolkit rather than a neutral abstraction: it is compiled into the one
/// WPF binding that needs it, and another toolkit binding ports the shape (check access, then invoke or
/// await an invoke on its own dispatcher) instead of sharing this type. A neutral seam in Core would have
/// no consumer - no neutral package marshals to a host thread - and the affinity rules are the toolkit's
/// to define. See <c>docs/EditorBindingGuide.md</c>, which lists this pair under the intentional
/// per-binding pieces.
/// </para>
/// </remarks>
internal static class DispatcherInvocation
{
	/// <summary>
	/// Runs the callback on the dispatcher and returns its result, hopping through
	/// <see cref="Dispatcher.Invoke{TResult}(Func{TResult})"/> when the current thread is not the dispatcher
	/// thread.
	/// </summary>
	/// <typeparam name="TResult">The callback's result type.</typeparam>
	/// <param name="dispatcher">The dispatcher the callback must run on.</param>
	/// <param name="callback">The callback to run.</param>
	/// <returns>The callback's result.</returns>
	internal static TResult Run<TResult>(Dispatcher dispatcher, Func<TResult> callback)
		=> dispatcher.CheckAccess() ? callback() : dispatcher.Invoke(callback);

	/// <summary>
	/// Runs the callback on the dispatcher, hopping through <see cref="Dispatcher.Invoke(Action)"/>
	/// when the current thread is not the dispatcher thread.
	/// </summary>
	/// <param name="dispatcher">The dispatcher the callback must run on.</param>
	/// <param name="callback">The callback to run.</param>
	internal static void Run(Dispatcher dispatcher, Action callback)
	{
		if (dispatcher.CheckAccess())
			callback();
		else
			dispatcher.Invoke(callback);
	}

	/// <summary>
	/// Runs the callback on the dispatcher without blocking the calling thread while the dispatcher
	/// processes the hop, when the current thread is not the dispatcher thread.
	/// </summary>
	/// <param name="dispatcher">The dispatcher the callback must run on.</param>
	/// <param name="callback">The callback to run.</param>
	/// <returns>A task that completes after the callback has run on the dispatcher.</returns>
	internal static async Task RunAsync(Dispatcher dispatcher, Action callback)
	{
		if (dispatcher.CheckAccess())
			callback();
		else
			await dispatcher.InvokeAsync(callback).Task;
	}
}
