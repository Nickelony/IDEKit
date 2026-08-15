namespace Nickelony.LanguageServer.Provider;

internal sealed partial class DocumentOperationScheduler
{
	/// <summary>
	/// Enqueues an operation behind the current chain for the specified document path.
	/// </summary>
	/// <typeparam name="TResult">The operation result type.</typeparam>
	/// <param name="filePath">The document path whose chain should receive the operation.</param>
	/// <param name="operation">The operation to enqueue.</param>
	/// <param name="cancellationToken">A token that can cancel the queued operation.</param>
	/// <returns>A task that completes with the queued operation result.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="operation"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.</exception>
	/// <exception cref="InvalidOperationException">
	/// The caller executes inside a queued scheduler operation and must queue the operation after awaiting it instead.
	/// </exception>
	public Task<TResult> EnqueuePerDocumentAsync<TResult>(string filePath, Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
		ArgumentNullException.ThrowIfNull(operation);

		string normalizedFilePath = LanguageServerPaths.NormalizeLocalPath(filePath);
		EnsureNotInsideSchedulerOperation(nameof(EnqueuePerDocumentAsync));

		return EnqueueOperation(
			[normalizedFilePath],
			joinsGlobalChain: false,
			operation,
			cancellationToken);
	}

	/// <summary>
	/// Enqueues an exclusive operation for one or two document paths so later work on those paths cannot run until the
	/// exclusive operation has finished.
	/// </summary>
	/// <remarks>
	/// The exclusive operation joins the global chain as well, so global work queued after it cannot overtake it while
	/// per-document work on unaffected paths stays ungated. Repeated spellings of the same document collapse into one
	/// affected path, and the affected paths are claimed in a deterministic order.
	/// </remarks>
	/// <typeparam name="TResult">The operation result type.</typeparam>
	/// <param name="firstFilePath">The first affected document path.</param>
	/// <param name="secondFilePath">The second affected document path.</param>
	/// <param name="operation">The exclusive operation to enqueue.</param>
	/// <param name="cancellationToken">A token that can cancel the queued operation.</param>
	/// <returns>A task that completes with the queued operation result.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="firstFilePath"/>, <paramref name="secondFilePath"/>, or <paramref name="operation"/> is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="firstFilePath"/> or <paramref name="secondFilePath"/> is empty or whitespace-only, or a path is invalid on the current platform.</exception>
	/// <exception cref="InvalidOperationException">
	/// The caller executes inside a queued scheduler operation and must queue the exclusive operation after awaiting it instead.
	/// </exception>
	public Task<TResult> EnqueueExclusivePerDocumentAsync<TResult>(
		string firstFilePath,
		string secondFilePath,
		Func<CancellationToken, Task<TResult>> operation,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(firstFilePath);
		ArgumentException.ThrowIfNullOrWhiteSpace(secondFilePath);
		ArgumentNullException.ThrowIfNull(operation);

		string normalizedFirstFilePath = LanguageServerPaths.NormalizeLocalPath(firstFilePath);
		string normalizedSecondFilePath = LanguageServerPaths.NormalizeLocalPath(secondFilePath);

		EnsureNotInsideSchedulerOperation(nameof(EnqueueExclusivePerDocumentAsync));

		return EnqueueOperation(
			OrderAffectedPaths(normalizedFirstFilePath, normalizedSecondFilePath),
			joinsGlobalChain: true,
			operation,
			cancellationToken);
	}

	/// <summary>
	/// Drops a repeated affected path and orders the remaining normalized paths deterministically.
	/// </summary>
	/// <param name="normalizedFirstFilePath">The first normalized affected document path.</param>
	/// <param name="normalizedSecondFilePath">The second normalized affected document path.</param>
	/// <returns>The distinct normalized paths in a deterministic order.</returns>
	/// <remarks>
	/// Two spellings of the same document (a canonical path and an aliased one, for example) collapse into one path, so
	/// an operation that names the same document twice claims - and waits for - a single chain. Ordering the paths makes
	/// an operation claim overlapping chains in the same sequence on every platform, whatever order the caller passed
	/// them in.
	/// </remarks>
	private static string[] OrderAffectedPaths(string normalizedFirstFilePath, string normalizedSecondFilePath)
	{
		if (LanguageServerPaths.AreLocalPathsEqual(normalizedFirstFilePath, normalizedSecondFilePath))
			return [normalizedFirstFilePath];

		string[] affectedPaths = [normalizedFirstFilePath, normalizedSecondFilePath];
		Array.Sort(affectedPaths, LanguageServerPaths.LocalPathComparer);
		return affectedPaths;
	}

	/// <summary>
	/// Waits for the queued operations of one or two document paths that were queued at the time of the call to finish.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Intended for hosts that need a quiescence point for one or two documents (for example before closing or
	/// renaming them); the client itself does not call this method. Work queued after this call is not awaited.
	/// </para>
	/// <para>
	/// Do not call this method from inside any operation queued with this scheduler; the scheduler rejects that
	/// pattern with an <see cref="InvalidOperationException"/> because awaiting scheduler work from within a queued
	/// operation can deadlock a chain.
	/// </para>
	/// </remarks>
	/// <param name="firstFilePath">The first document path to await.</param>
	/// <param name="secondFilePath">The second document path to await.</param>
	/// <returns>A task that completes when the queued operations have finished.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="firstFilePath"/> or <paramref name="secondFilePath"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="firstFilePath"/> or <paramref name="secondFilePath"/> is empty or whitespace-only, or a path is invalid on the current platform.</exception>
	/// <exception cref="InvalidOperationException">
	/// The caller executes inside a queued scheduler operation, so waiting for scheduler work would deadlock a chain.
	/// </exception>
	public async Task WaitForPerDocumentOperationsAsync(string firstFilePath, string secondFilePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(firstFilePath);
		ArgumentException.ThrowIfNullOrWhiteSpace(secondFilePath);

		string normalizedFirstFilePath = LanguageServerPaths.NormalizeLocalPath(firstFilePath);
		string normalizedSecondFilePath = LanguageServerPaths.NormalizeLocalPath(secondFilePath);

		EnsureNotInsideSchedulerOperation(nameof(WaitForPerDocumentOperationsAsync));

		var queuedOperations = new List<Task>(2);

		lock (_syncRoot)
		{
			foreach (string normalizedFilePath in OrderAffectedPaths(normalizedFirstFilePath, normalizedSecondFilePath))
			{
				if (_documentChains.TryGetValue(normalizedFilePath, out OperationChain? chain) && chain._tail is { } tail)
					queuedOperations.Add(tail._completion);
			}
		}

		for (int i = 0; i < queuedOperations.Count; i++)
			await WaitForOperationAsync(queuedOperations[i]).ConfigureAwait(false);
	}
}
