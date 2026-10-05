namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Serializes per-document operations, coalesces pending latest-only updates, runs exclusive operations that temporarily take ownership
/// of one or two document paths, and keeps the order of the calls that wait for the connection before they join a lane.
/// </summary>
/// <remarks>
/// The scheduler queues each operation on the lanes of an <see cref="OperationLaneTracker"/>, keeps the registrations of the latest-only
/// updates in a <see cref="LatestUpdateTracker"/>, keeps the places of the waiting calls in a <see cref="CallOrderTracker"/>, and rejects flow-local
/// re-entrancy through an <see cref="OperationReentrancyGate"/>, so a queued delegate cannot deadlock against itself. The three trackers share
/// the scheduler's lock.
/// </remarks>
internal sealed class DocumentOperationScheduler
{
	// The lanes, the latest-update registrations and the call reservations are guarded by _syncRoot, which the three trackers share.
	private readonly object _syncRoot = new();

	private readonly LocalPathComparisonPolicy _pathComparison;
	private readonly OperationLaneTracker _lanes;
	private readonly LatestUpdateTracker _latestUpdates;
	private readonly CallOrderTracker _callOrder;
	private readonly OperationReentrancyGate _reentrancy = new();

	/// <param name="options">
	/// The provider options whose path comparison decides which paths share a lane, or <see langword="null"/> for the defaults.
	/// </param>
	internal DocumentOperationScheduler(LanguageServerFeatureProviderOptions? options = null)
	{
		_pathComparison = LanguageServerFeatureProviderOptions.PathComparisonOf(options);
		_lanes = new OperationLaneTracker(_syncRoot, _pathComparison);
		_latestUpdates = new LatestUpdateTracker(_syncRoot, _pathComparison);
		_callOrder = new CallOrderTracker(_syncRoot, _pathComparison);
	}

	/// <summary>The number of document paths that hold a reservation not yet complete.</summary>
	/// <remarks>
	/// A test reads it to see that a finished call leaves nothing behind; production code never branches on it.
	/// </remarks>
	internal int ReservedCallPathCount => _callOrder.ReservedPathCount;

	/// <summary>Reserves the next place among the calls on a document path, in the order the host made them.</summary>
	/// <param name="filePath">The document path the call affects.</param>
	/// <returns>The reservation, which the call completes on every exit path.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.
	/// </exception>
	/// <remarks>
	/// The call reserves before it waits for the connection and joins a lane only after the reservations before it are complete, so a call
	/// that reaches its lane late never lands behind a call the host made after it.
	/// </remarks>
	internal CallOrderTracker.CallReservation ReserveCall(string filePath)
	{
		string normalizedFilePath = LocalPath.Normalize(filePath);

		lock (_syncRoot)
			return _callOrder.Reserve([normalizedFilePath]);
	}

	/// <summary>Reserves the next place among the calls on two document paths, in the order the host made them.</summary>
	/// <param name="firstFilePath">The first document path the call affects.</param>
	/// <param name="secondFilePath">The second document path the call affects.</param>
	/// <returns>The reservation, which waits behind the calls on both paths and holds both until it completes.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="firstFilePath"/> or <paramref name="secondFilePath"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="firstFilePath"/> or <paramref name="secondFilePath"/> is empty or whitespace-only, or a path is invalid
	/// on the current platform.
	/// </exception>
	internal CallOrderTracker.CallReservation ReserveCall(string firstFilePath, string secondFilePath)
	{
		string[] normalizedFilePaths = OrderAffectedPaths(LocalPath.Normalize(firstFilePath), LocalPath.Normalize(secondFilePath));

		lock (_syncRoot)
			return _callOrder.Reserve(normalizedFilePaths);
	}

	/// <summary>Registers one operation node on the lanes the operation joins and returns the caller-facing task.</summary>
	/// <typeparam name="TResult">The operation result type.</typeparam>
	/// <param name="normalizedFilePaths">The distinct normalized document paths whose lanes the node joins, in a deterministic order.</param>
	/// <param name="joinsExclusiveLane">Whether the node also joins the lane that every exclusive operation shares.</param>
	/// <param name="operation">The operation to run.</param>
	/// <param name="cancellationToken">A token that can cancel the queued operation.</param>
	/// <returns>A task that completes with the queued operation result.</returns>
	/// <remarks>
	/// The node waits for the tails the lanes held at registration time and then becomes their new tail, so later work on those lanes observes
	/// it as its predecessor. The node only starts once this call released <see cref="_syncRoot"/>.
	/// </remarks>
	private Task<TResult> EnqueueOperationAsync<TResult>(
		string[] normalizedFilePaths,
		bool joinsExclusiveLane,
		Func<CancellationToken, Task<TResult>> operation,
		CancellationToken cancellationToken
	)
	{
		var completionSource = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		var node = new OperationLaneTracker.LaneNode();

		try
		{
			lock (_syncRoot)
			{
				OperationLaneTracker.OperationLane[] lanes = _lanes.Capture(normalizedFilePaths, joinsExclusiveLane);
				var predecessors = new Task[lanes.Length];

				for (int i = 0; i < lanes.Length; i++)
					predecessors[i] = lanes[i].TailTask;

				node._completion = RunOperationNodeAsync(
					node,
					predecessors,
					activeContext: new OperationReentrancyGate.ActiveDocumentContext(normalizedFilePaths),
					operation,
					completionSource,
					cancellationToken,
					onCompleted: () => _lanes.Release(lanes, node)
				);

				for (int i = 0; i < lanes.Length; i++)
					lanes[i]._tail = node;
			}
		}
		finally
		{
			node.Release();
		}

		return completionSource.Task;
	}

	/// <summary>Awaits a queued operation while suppressing its failure so later operations can continue.</summary>
	/// <param name="queuedOperation">The operation scheduled ahead of this one.</param>
	/// <returns>A task that completes after the queued operation settles.</returns>
	private static async Task WaitForOperationAsync(Task queuedOperation)
	{
		try
		{
			await queuedOperation.ConfigureAwait(false);
		}
		catch (Exception)
		{
			// A predecessor is a lane node task, and a node reports its operation's failure to its own caller through its completion
			// source, so the only fault the task can still carry comes from its own bookkeeping, which this node cannot act on. The
			// scheduler has no logger, and the nodes behind the predecessor must still run.
		}
	}

	/// <summary>Waits until the enqueuing call released the scheduler lock and the predecessor operations settled.</summary>
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
	/// Runs one action with the flow-local active-operation context detached, so work the action starts is not treated as nested scheduler work.
	/// </summary>
	/// <param name="action">The action that synchronously starts the detached work.</param>
	/// <remarks>
	/// Only the synchronous start of <paramref name="action"/> runs inside the detached scope (that is where an async state machine or a queued
	/// <see cref="Task.Run(Action)"/> captures its context), so work the action starts observes no active-operation scope. The originating delegate
	/// must not await the detached work.
	/// </remarks>
	internal void RunDetachedFromActiveContext(Action action) => _reentrancy.RunDetached(action);

	/// <summary>Runs a queued operation after the predecessor operations settled and reports the result to the caller's completion source.</summary>
	/// <typeparam name="TResult">The operation result type.</typeparam>
	/// <param name="node">The lane node whose start gate the enqueuing call releases.</param>
	/// <param name="predecessors">The operations that must settle before the delegate may run.</param>
	/// <param name="activeContext">The active-operation context published while the delegate executes.</param>
	/// <param name="operation">The queued operation to execute.</param>
	/// <param name="completionSource">The completion source exposed to the caller.</param>
	/// <param name="cancellationToken">A token that can cancel the queued operation.</param>
	/// <param name="onCompleted">An optional action that runs after the queued operation settled.</param>
	/// <returns>A task representing the scheduled lane node.</returns>
	private async Task RunOperationNodeAsync<TResult>(
		OperationLaneTracker.LaneNode node,
		Task[] predecessors,
		OperationReentrancyGate.ActiveDocumentContext activeContext,
		Func<CancellationToken, Task<TResult>> operation,
		TaskCompletionSource<TResult> completionSource,
		CancellationToken cancellationToken,
		Action? onCompleted = null
	)
	{
		try
		{
			await WaitForPredecessorsAsync(node._startGate.Task, predecessors).ConfigureAwait(false);

			cancellationToken.ThrowIfCancellationRequested();

			TResult result = await _reentrancy.RunAsync(activeContext, () => operation(cancellationToken)).ConfigureAwait(false);
			completionSource.TrySetResult(result);
		}
		// Cancellation is reported as canceled when it targets the caller's token or when the caller's token
		// observed cancellation, even if a delegate linked a token of its own; other faults stay faults.
		catch (OperationCanceledException exception)
			when (exception.CancellationToken == cancellationToken || cancellationToken.IsCancellationRequested)
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

	/// <summary>Enqueues an operation behind the current lane for the specified document path.</summary>
	/// <typeparam name="TResult">The operation result type.</typeparam>
	/// <param name="filePath">The document path whose lane should receive the operation.</param>
	/// <param name="operation">The operation to enqueue.</param>
	/// <param name="cancellationToken">A token that can cancel the queued operation.</param>
	/// <returns>A task that completes with the queued operation result.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The caller executes inside a queued scheduler operation and must queue the operation after awaiting it instead.
	/// </exception>
	internal Task<TResult> EnqueuePerDocumentAsync<TResult>(
		string filePath,
		Func<CancellationToken, Task<TResult>> operation,
		CancellationToken cancellationToken
	)
	{
		string normalizedFilePath = LocalPath.Normalize(filePath);
		_reentrancy.EnsureNotInside(nameof(EnqueuePerDocumentAsync));

		return EnqueueOperationAsync(normalizedFilePaths: [normalizedFilePath], joinsExclusiveLane: false, operation, cancellationToken);
	}

	/// <summary>
	/// Enqueues an exclusive operation for one or two document paths so later work on those paths cannot run until the exclusive operation
	/// has finished.
	/// </summary>
	/// <typeparam name="TResult">The operation result type.</typeparam>
	/// <param name="firstFilePath">The first affected document path.</param>
	/// <param name="secondFilePath">The second affected document path.</param>
	/// <param name="operation">The exclusive operation to enqueue.</param>
	/// <param name="cancellationToken">A token that can cancel the queued operation.</param>
	/// <returns>A task that completes with the queued operation result.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="firstFilePath"/> or <paramref name="secondFilePath"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="firstFilePath"/> or <paramref name="secondFilePath"/> is empty or whitespace-only, or a path is invalid
	/// on the current platform.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The caller executes inside a queued scheduler operation and must queue the exclusive operation after awaiting it instead.
	/// </exception>
	/// <remarks>
	/// The exclusive operation joins the lane that every exclusive operation shares, so exclusive operations run one at a time
	/// in the order they were queued while per-document work on unaffected paths stays ungated. Repeated spellings of the same document collapse
	/// into one affected path, and the affected paths are claimed in a deterministic order.
	/// </remarks>
	internal Task<TResult> EnqueueExclusivePerDocumentAsync<TResult>(
		string firstFilePath,
		string secondFilePath,
		Func<CancellationToken, Task<TResult>> operation,
		CancellationToken cancellationToken
	)
	{
		string normalizedFirstFilePath = LocalPath.Normalize(firstFilePath);
		string normalizedSecondFilePath = LocalPath.Normalize(secondFilePath);

		_reentrancy.EnsureNotInside(nameof(EnqueueExclusivePerDocumentAsync));

		return EnqueueOperationAsync(
			normalizedFilePaths: OrderAffectedPaths(normalizedFirstFilePath, normalizedSecondFilePath),
			joinsExclusiveLane: true,
			operation,
			cancellationToken
		);
	}

	/// <summary>Drops a repeated affected path and orders the remaining normalized paths deterministically.</summary>
	/// <param name="normalizedFirstFilePath">The first normalized affected document path.</param>
	/// <param name="normalizedSecondFilePath">The second normalized affected document path.</param>
	/// <returns>The distinct normalized paths in a deterministic order.</returns>
	/// <remarks>
	/// Two spellings of the same document (a canonical path and an aliased one, for example) collapse into one path, so an operation
	/// that names the same document twice claims - and waits for - a single lane. Ordering the paths makes an operation claim overlapping lanes
	/// in the same sequence on every platform, whatever order the caller passed them in.
	/// </remarks>
	private string[] OrderAffectedPaths(string normalizedFirstFilePath, string normalizedSecondFilePath)
	{
		if (_pathComparison.Comparer.Equals(normalizedFirstFilePath, normalizedSecondFilePath))
			return [normalizedFirstFilePath];

		string[] affectedPaths = [normalizedFirstFilePath, normalizedSecondFilePath];
		Array.Sort(affectedPaths, _pathComparison.Comparer);
		return affectedPaths;
	}

	/// <summary>Replaces any pending latest-only update for the specified document path so only the newest one remains active.</summary>
	/// <param name="filePath">The document path whose latest update should be replaced.</param>
	/// <param name="operation">The latest-only update delegate to execute.</param>
	/// <param name="reservation">
	/// The reservation of the call that makes the update, or <see langword="null"/> for an update with no place among the waiting calls.
	/// The update joins the lane unless a cancellation superseded the reservation after it was made.
	/// </param>
	/// <returns>
	/// A task that represents the latest update. A superseded update completes successfully without running its delegate;
	/// delegate faults and cancellation propagate to the caller.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The caller executes inside a queued scheduler operation and must queue the update after awaiting it instead.
	/// </exception>
	/// <remarks>
	/// The update is queued behind the operations already queued for the document, including a running update,
	/// and is skipped when it was superseded before it started. Queued delegates must not call back into the scheduler; that pattern is rejected
	/// with an <see cref="InvalidOperationException"/> instead of risking a deadlock.
	/// </remarks>
	internal Task EnqueueLatestUpdateAsync(
		string filePath,
		Func<CancellationToken, Task> operation,
		CallOrderTracker.CallReservation? reservation = null
	)
	{
		string normalizedFilePath = LocalPath.Normalize(filePath);
		_reentrancy.EnsureNotInside(nameof(EnqueueLatestUpdateAsync));

		var completionSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var node = new OperationLaneTracker.LaneNode();

		try
		{
			lock (_syncRoot)
			{
				// The answer and the join are one critical section: a cancellation either ran before it, and the update never joins, or runs
				// after it, and finds the registration.
				if (reservation is not null && _callOrder.IsSuperseded(reservation))
					return Task.CompletedTask;

				OperationLaneTracker.OperationLane lane = _lanes.GetOrCreate(normalizedFilePath);
				LatestUpdateTracker.UpdateHandle registration = _latestUpdates.Register(normalizedFilePath);

				node._completion = RunLatestUpdateNodeAsync(
					normalizedFilePath,
					lane,
					node,
					predecessor: lane.TailTask,
					registration,
					operation,
					completionSource
				);

				lane._tail = node;
			}
		}
		finally
		{
			node.Release();
		}

		return completionSource.Task;
	}

	/// <summary>Cancels the current latest-only update for the specified document path, including one that is already running.</summary>
	/// <param name="filePath">The document path whose latest update should be canceled.</param>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="filePath"/> is empty or whitespace-only, or the path is invalid on the current platform.
	/// </exception>
	/// <remarks>
	/// A pending update is skipped and completes without running its delegate. The running update is canceled even when a newer
	/// update superseded it, so an enqueue cannot hide it from this call. An update that reserved its place and still waits for the connection
	/// is skipped as well, because the cancellation supersedes its reservation before it cancels the registrations.
	/// </remarks>
	internal void CancelLatestUpdate(string filePath)
	{
		string normalizedFilePath = LocalPath.Normalize(filePath);

		lock (_syncRoot)
			_callOrder.Supersede(normalizedFilePath);

		_latestUpdates.Cancel(normalizedFilePath);
	}

	/// <summary>Cancels every active latest-only update, whether queued, running, or waiting for the connection.</summary>
	internal void CancelAllLatestUpdates()
	{
		lock (_syncRoot)
			_callOrder.SupersedeAll();

		_latestUpdates.CancelAll();
	}

	/// <summary>Executes a latest-only update once its predecessor settled and it is still the latest update for its document path.</summary>
	/// <param name="normalizedFilePath">The normalized document path associated with the update.</param>
	/// <param name="lane">The lane that owns the document path.</param>
	/// <param name="node">The lane node whose start gate the enqueuing call releases.</param>
	/// <param name="predecessor">The operation that must settle before the update may run.</param>
	/// <param name="registration">The active latest-update registration.</param>
	/// <param name="operation">The update delegate to execute.</param>
	/// <param name="completionSource">
	/// The source of the task exposed to the caller, which completes, cancels, or faults with the update while the node task itself never faults.
	/// </param>
	/// <returns>A task that completes when the update node settled.</returns>
	private async Task RunLatestUpdateNodeAsync(
		string normalizedFilePath,
		OperationLaneTracker.OperationLane lane,
		OperationLaneTracker.LaneNode node,
		Task predecessor,
		LatestUpdateTracker.UpdateHandle registration,
		Func<CancellationToken, Task> operation,
		TaskCompletionSource<object?> completionSource
	)
	{
		try
		{
			await WaitForPredecessorsAsync(node._startGate.Task, [predecessor]).ConfigureAwait(false);

			CancellationTokenSource? source;

			lock (_syncRoot)
				source = _latestUpdates.Start(normalizedFilePath, registration);

			if (source is null)
			{
				completionSource.TrySetResult(null);
				return;
			}

			await _reentrancy
				.RunAsync(
					new OperationReentrancyGate.ActiveDocumentContext([normalizedFilePath]),

					async () =>
					{
						await operation(source.Token).ConfigureAwait(false);
						return true;
					}
				)
				.ConfigureAwait(false);

			completionSource.TrySetResult(null);
		}
		catch (OperationCanceledException exception)
		{
			completionSource.TrySetCanceled(exception.CancellationToken);
		}
		catch (Exception exception)
		{
			// The node task doubles as the document lane tail; keep it fault-suppressed and surface the failure
			// through the caller's task instead so an ignored lane tail cannot fault unobserved.
			completionSource.TrySetException(exception);
		}
		finally
		{
			CompleteLatestUpdateNode(normalizedFilePath, lane, node, registration);
		}
	}

	/// <summary>
	/// Clears the update registrations and the lane tail once the settled node still owns them, removes the lane once it drained,
	/// and releases the registration's cancellation source.
	/// </summary>
	/// <param name="normalizedFilePath">The normalized document path associated with the update.</param>
	/// <param name="lane">The lane that owns the document path.</param>
	/// <param name="node">The node that settled.</param>
	/// <param name="registration">The registration of the completed update.</param>
	private void CompleteLatestUpdateNode(
		string normalizedFilePath,
		OperationLaneTracker.OperationLane lane,
		OperationLaneTracker.LaneNode node,
		LatestUpdateTracker.UpdateHandle registration
	)
	{
		lock (_syncRoot)
		{
			_latestUpdates.Complete(normalizedFilePath, registration);
			_lanes.Complete(lane, node);
		}

		registration.Dispose();
	}
}
