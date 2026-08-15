namespace Nickelony.LanguageServer.Provider;

internal sealed partial class DocumentOperationScheduler
{
	/// <summary>
	/// Enqueues an operation behind the current global operation chain.
	/// </summary>
	/// <remarks>
	/// Intended for hosts that need whole-client operations (such as a configuration push) serialized behind
	/// other global work; the client itself does not call this method. Global operations are ordered only with
	/// respect to other global and exclusive operations; per-document work on unaffected paths is not gated. Queued
	/// delegates must not call back into the scheduler; every enqueue and wait method rejects that pattern with an
	/// <see cref="InvalidOperationException"/> instead of risking a deadlock.
	/// </remarks>
	/// <typeparam name="TResult">The operation result type.</typeparam>
	/// <param name="operation">The operation to enqueue.</param>
	/// <param name="cancellationToken">A token that can cancel the queued operation.</param>
	/// <returns>A task that completes with the queued operation result.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">
	/// The caller executes inside a queued scheduler operation and must queue the global operation after awaiting it instead.
	/// </exception>
	public Task<TResult> EnqueueGlobalAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(operation);

		EnsureNotInsideSchedulerOperation(nameof(EnqueueGlobalAsync));

		return EnqueueOperation([], joinsGlobalChain: true, operation, cancellationToken);
	}
}
