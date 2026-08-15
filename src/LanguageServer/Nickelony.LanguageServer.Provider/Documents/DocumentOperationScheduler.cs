namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Serializes per-document operations, coalesces pending latest-only updates, and runs exclusive operations that
/// temporarily take ownership of one or two document paths.
/// </summary>
/// <remarks>
/// The scheduler owns one ordered operation chain per normalized document path plus one global chain, plus a
/// latest-only coalescing slot, and rejects flow-local re-entrancy so a queued delegate cannot deadlock against
/// itself. The model, the re-entrancy rule, and the detached-context escape hatch are documented in
/// <c>docs/LanguageServerInternals.md</c>.
/// </remarks>
internal sealed partial class DocumentOperationScheduler
{
	// Scheduler state: the global chain plus one chain per normalized document path.
	// All state is guarded by _syncRoot.
	private readonly object _syncRoot = new();

	private readonly AsyncLocal<ActiveDocumentContext?> _activeDocumentContext = new();

	private readonly Dictionary<string, OperationChain> _documentChains = new(LanguageServerPaths.LocalPathComparer);
	private readonly OperationChain _globalChain = new(normalizedFilePath: null);

	/// <summary>
	/// Registers one operation node on the chains the operation joins and returns the caller-facing task.
	/// </summary>
	/// <remarks>
	/// The node waits for the tails the chains held at registration time and then becomes their new tail, so later work
	/// on those chains observes it as its predecessor. The node only starts once this call released
	/// <see cref="_syncRoot"/>.
	/// </remarks>
	/// <typeparam name="TResult">The operation result type.</typeparam>
	/// <param name="normalizedFilePaths">The distinct normalized document paths whose chains the node joins, in a deterministic order.</param>
	/// <param name="joinsGlobalChain">Whether the node also joins the global chain.</param>
	/// <param name="operation">The operation to run.</param>
	/// <param name="cancellationToken">A token that can cancel the queued operation.</param>
	/// <returns>A task that completes with the queued operation result.</returns>
	private Task<TResult> EnqueueOperation<TResult>(
		string[] normalizedFilePaths,
		bool joinsGlobalChain,
		Func<CancellationToken, Task<TResult>> operation,
		CancellationToken cancellationToken)
	{
		var completionSource = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		var node = new ChainNode();

		try
		{
			lock (_syncRoot)
			{
				OperationChain[] chains = CaptureChainsUnderLock(normalizedFilePaths, joinsGlobalChain);
				var predecessors = new Task[chains.Length];

				for (int i = 0; i < chains.Length; i++)
					predecessors[i] = chains[i].TailTask;

				node._completion = RunOperationNodeAsync(
					node,
					predecessors,
					new ActiveDocumentContext(normalizedFilePaths),
					operation,
					completionSource,
					cancellationToken,
					onCompleted: () => ReleaseChainNodes(chains, node));

				for (int i = 0; i < chains.Length; i++)
					chains[i]._tail = node;
			}
		}
		finally
		{
			node.Release();
		}

		return completionSource.Task;
	}

	/// <summary>
	/// Awaits a queued operation while suppressing its failure so later operations can continue.
	/// </summary>
	/// <param name="queuedOperation">The previously scheduled operation.</param>
	/// <returns>A task that completes after the queued operation settles.</returns>
	private static async Task WaitForOperationAsync(Task queuedOperation)
	{
		try
		{
			await queuedOperation.ConfigureAwait(false);
		}
		catch
		{ }
	}

	/// <summary>
	/// Waits until the enqueuing call released the scheduler lock and the predecessor operations settled.
	/// </summary>
	/// <param name="startGate">Completes when the enqueuing call released the scheduler lock; the delegate starts only afterwards.</param>
	/// <param name="predecessors">The operations that must settle first; their failures are suppressed so later operations can continue.</param>
	/// <returns>A task that completes when the node may run.</returns>
	private static async Task WaitForPredecessorsAsync(Task startGate, Task[] predecessors)
	{
		// The enqueuing call completes the gate only after it released the scheduler lock, so the delegate never
		// starts while that lock is held.
		await startGate.ConfigureAwait(false);

		for (int i = 0; i < predecessors.Length; i++)
			await WaitForOperationAsync(predecessors[i]).ConfigureAwait(false);
	}

	/// <summary>
	/// Tracks the document paths whose chain is currently executing an operation so that re-entrant scheduler calls
	/// can be rejected instead of deadlocking.
	/// </summary>
	/// <param name="normalizedFilePaths">The distinct normalized document paths whose operation is executing; empty for global work.</param>
	private sealed class ActiveDocumentContext(string[] normalizedFilePaths)
	{
		private int _isRunning = 1;

		/// <summary>
		/// Gets or sets a value indicating whether the operation is still executing. The operation's continuation
		/// clears the flag on the executing thread while another thread may probe the context, so the flag is
		/// accessed with <see cref="Volatile"/> semantics.
		/// </summary>
		public bool IsRunning
		{
			get => Volatile.Read(ref _isRunning) != 0;
			set => Volatile.Write(ref _isRunning, value ? 1 : 0);
		}

		/// <summary>
		/// Describes the document paths whose operation is executing for error messages.
		/// </summary>
		/// <returns>A suffix naming the affected paths, or <see cref="string.Empty"/> for a global operation.</returns>
		public string DescribePaths() => normalizedFilePaths.Length switch
		{
			0 => string.Empty,
			1 => $" for '{normalizedFilePaths[0]}'",
			_ => $" for '{string.Join("' and '", normalizedFilePaths)}'"
		};
	}

	/// <summary>
	/// Runs an operation while publishing the active-operation context so nested scheduler calls are rejected.
	/// </summary>
	/// <typeparam name="TResult">The operation result type.</typeparam>
	/// <param name="context">The active-operation context to publish.</param>
	/// <param name="operation">The operation to run.</param>
	/// <returns>A task that completes with the operation result.</returns>
	private async Task<TResult> RunWithActiveContextAsync<TResult>(ActiveDocumentContext context, Func<Task<TResult>> operation)
	{
		ActiveDocumentContext? previousContext = _activeDocumentContext.Value;
		_activeDocumentContext.Value = context;

		try
		{
			return await operation().ConfigureAwait(false);
		}
		finally
		{
			context.IsRunning = false;
			_activeDocumentContext.Value = previousContext;
		}
	}

	/// <summary>
	/// Runs one action with the flow-local active-operation context detached, so work the action starts is not
	/// treated as nested scheduler work.
	/// </summary>
	/// <remarks>
	/// Only the synchronous start of the detached work runs inside <paramref name="action"/>; that is where an async
	/// state machine or a queued <see cref="Task.Run(Action)"/> captures its execution context, so the detached work
	/// observes no active-operation scope. Use this for provider-owned work started from inside a queued delegate that
	/// may legitimately call back into the scheduler (for example a semantic-token refresh that can drive the
	/// transport-start path, which queues per-document operations). The originating delegate must not await the
	/// detached work, so there is no circular wait; a detached enqueue still waits for the originating operation's
	/// chain, which settles independently.
	/// </remarks>
	/// <param name="action">The action that synchronously starts the detached work.</param>
	/// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
	internal void RunDetachedFromActiveContext(Action action)
	{
		ArgumentNullException.ThrowIfNull(action);

		ActiveDocumentContext? previousContext = _activeDocumentContext.Value;

		// A call from outside a queued operation has no context to detach.
		if (previousContext is null)
		{
			action();
			return;
		}

		_activeDocumentContext.Value = null;

		try
		{
			action();
		}
		finally
		{
			_activeDocumentContext.Value = previousContext;
		}
	}

	/// <summary>
	/// Rejects an enqueue or wait issued from inside a queued scheduler operation.
	/// </summary>
	/// <param name="memberName">The calling member name, used in the exception message.</param>
	/// <exception cref="InvalidOperationException">
	/// The caller executes inside a queued scheduler operation, so awaiting new scheduler work could deadlock a chain.
	/// </exception>
	private void EnsureNotInsideSchedulerOperation(string memberName)
	{
		ActiveDocumentContext? context = _activeDocumentContext.Value;

		if (context is null || !context.IsRunning)
			return;

		throw new InvalidOperationException(
			$"'{memberName}' was called from inside a queued scheduler operation{context.DescribePaths()}. "
			+ "Queued delegates must not enqueue or wait for scheduler work; await the operation from the caller and queue follow-up work afterwards, "
			+ "or inline the nested work into the operation delegate instead.");
	}

	/// <summary>
	/// Runs a queued operation after the predecessor operations settled and reports the result to the caller's
	/// completion source.
	/// </summary>
	/// <typeparam name="TResult">The operation result type.</typeparam>
	/// <param name="node">The chain node whose start gate the enqueuing call releases.</param>
	/// <param name="predecessors">The operations that must settle before the delegate may run.</param>
	/// <param name="activeContext">The active-operation context published while the delegate executes.</param>
	/// <param name="operation">The queued operation to execute.</param>
	/// <param name="completionSource">The completion source exposed to the caller.</param>
	/// <param name="cancellationToken">A token that can cancel the queued operation.</param>
	/// <param name="onCompleted">An optional action that runs after the queued operation settled.</param>
	/// <returns>A task representing the scheduled chain node.</returns>
	private async Task RunOperationNodeAsync<TResult>(
		ChainNode node,
		Task[] predecessors,
		ActiveDocumentContext activeContext,
		Func<CancellationToken, Task<TResult>> operation,
		TaskCompletionSource<TResult> completionSource,
		CancellationToken cancellationToken,
		Action? onCompleted = null)
	{
		try
		{
			await WaitForPredecessorsAsync(node._startGate.Task, predecessors).ConfigureAwait(false);

			cancellationToken.ThrowIfCancellationRequested();

			TResult result = await RunWithActiveContextAsync(activeContext, () => operation(cancellationToken)).ConfigureAwait(false);
			completionSource.TrySetResult(result);
		}
		// Cancellation is reported as canceled when it targets the caller's token or when the caller's token
		// observed cancellation, even if a delegate linked a token of its own; other faults stay faults.
		catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken || cancellationToken.IsCancellationRequested)
		{
			completionSource.TrySetCanceled(cancellationToken);
		}
		catch (Exception exception)
		{
			completionSource.TrySetException(exception);
		}
		finally
		{
			onCompleted?.Invoke();
		}
	}

	/// <summary>
	/// Gets the operation chain for the supplied normalized document path, creating it on first use.
	/// The caller must hold <see cref="_syncRoot"/>.
	/// </summary>
	/// <param name="normalizedFilePath">The normalized document path.</param>
	/// <returns>The chain that owns the document path.</returns>
	private OperationChain GetOrCreateDocumentChainUnderLock(string normalizedFilePath)
	{
		if (!_documentChains.TryGetValue(normalizedFilePath, out OperationChain? chain))
		{
			chain = new OperationChain(normalizedFilePath);
			_documentChains[normalizedFilePath] = chain;
		}

		return chain;
	}

	/// <summary>
	/// Removes a document chain once it has fully drained.
	/// The caller must hold <see cref="_syncRoot"/>.
	/// </summary>
	/// <param name="chain">The chain to remove once it is idle.</param>
	private void PruneDocumentChainUnderLock(OperationChain chain)
	{
		if (chain.IsIdle
			&& chain._normalizedFilePath is { } normalizedFilePath
			&& _documentChains.TryGetValue(normalizedFilePath, out OperationChain? currentChain)
			&& ReferenceEquals(currentChain, chain))
		{
			_documentChains.Remove(normalizedFilePath);
		}
	}

	/// <summary>
	/// Captures the chains one operation node joins.
	/// The caller must hold <see cref="_syncRoot"/>.
	/// </summary>
	/// <param name="normalizedFilePaths">The distinct normalized document paths whose chains the node joins.</param>
	/// <param name="joinsGlobalChain">Whether the node also joins the global chain.</param>
	/// <returns>The chains the node joins, with the global chain first when it is joined.</returns>
	private OperationChain[] CaptureChainsUnderLock(string[] normalizedFilePaths, bool joinsGlobalChain)
	{
		var chains = new OperationChain[(joinsGlobalChain ? 1 : 0) + normalizedFilePaths.Length];
		int index = 0;

		if (joinsGlobalChain)
			chains[index++] = _globalChain;

		for (int i = 0; i < normalizedFilePaths.Length; i++)
			chains[index++] = GetOrCreateDocumentChainUnderLock(normalizedFilePaths[i]);

		return chains;
	}

	/// <summary>
	/// Clears the tails of the chains a settled operation node was queued on and removes the chains that drained.
	/// </summary>
	/// <param name="chains">The chains the node joined.</param>
	/// <param name="node">The node that settled.</param>
	private void ReleaseChainNodes(OperationChain[] chains, ChainNode node)
	{
		lock (_syncRoot)
		{
			for (int i = 0; i < chains.Length; i++)
			{
				if (ReferenceEquals(chains[i]._tail, node))
					chains[i]._tail = null;

				PruneDocumentChainUnderLock(chains[i]);
			}
		}
	}

	/// <summary>
	/// One queued node of an operation chain: the start gate the enqueuing call releases and the task its successors
	/// await as their predecessor.
	/// </summary>
	private sealed class ChainNode
	{
		/// <summary>The gate that keeps the delegate from running while the enqueuing call still holds the scheduler lock.</summary>
		internal readonly TaskCompletionSource<bool> _startGate =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		/// <summary>The task that completes when the node settled.</summary>
		internal Task _completion = Task.CompletedTask;

		/// <summary>
		/// Releases the start gate so the queued delegate may run.
		/// </summary>
		public void Release() => _startGate.TrySetResult(true);
	}

	/// <summary>
	/// Tracks the serialized operation chain of one key.
	/// </summary>
	/// <remarks>
	/// A document chain additionally tracks the latest-only update slots; the global chain never uses them.
	/// </remarks>
	/// <param name="normalizedFilePath">The normalized document path this chain serializes, or <see langword="null"/> for the global chain.</param>
	private sealed class OperationChain(string? normalizedFilePath)
	{
		/// <summary>The normalized document path this chain serializes, or <see langword="null"/> for the global chain.</summary>
		internal readonly string? _normalizedFilePath = normalizedFilePath;

		/// <summary>The tail node of the serialized chain, or <see langword="null"/> when the chain is idle.</summary>
		internal ChainNode? _tail;

		/// <summary>The registration of the newest latest-only update, if any.</summary>
		internal QueuedUpdateRegistration? _updateSlot;

		/// <summary>The registration of the latest-only update that is currently running, if any.</summary>
		internal QueuedUpdateRegistration? _runningRegistration;

		/// <summary>Gets the task a node queued on this chain awaits as its predecessor.</summary>
		public Task TailTask => _tail?._completion ?? Task.CompletedTask;

		/// <summary>
		/// Gets a value indicating whether the chain holds no queued, running, or registered work.
		/// </summary>
		public bool IsIdle => _tail is null && _updateSlot is null && _runningRegistration is null;
	}
}
