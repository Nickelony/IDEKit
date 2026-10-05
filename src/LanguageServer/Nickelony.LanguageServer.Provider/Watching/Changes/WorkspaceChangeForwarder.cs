using System.Diagnostics.CodeAnalysis;
using Nickelony.IDEKit.Core.Threading;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Forwards workspace changes when the owner allows it, buffers recoverable delivery failures for replay, and reports unexpected dropped batches
/// with forwarding context.
/// </summary>
/// <remarks>
/// <para>
/// This is an internal provider-composition helper rather than a host API: the workspace change dispatcher builds its forwarding on it, and a host
/// consumes the provider through the Provider.Abstractions contract instead of talking to this type directly.
/// </para>
/// <para>
/// Buffered changes are replayed only when the caller invokes <see cref="ReplayBufferedAsync"/>; <see cref="DispatchAsync"/> never drains
/// the buffered set, so a caller that wants guaranteed ordering must replay the buffered changes before forwarding new ones.
/// A batch that fails a dispatch attempt stays ahead of changes that were buffered while the attempt was in flight, so a re-buffered delete
/// cannot cancel against a newer create for the same path. Disposal is immediate and drops undelivered state: an incoming change set is discarded
/// instead of being buffered or forwarded, and buffered changes are not replayed.
/// </para>
/// <para>
/// Disposal begins the stop of the operation gate first, so no operation is admitted afterwards, and releases the forwarding gate only after the last
/// operation in flight has left, so a late operation never observes a disposed gate. Disposal does not wait for those operations, so a callback
/// that an operation invokes may dispose the forwarder: the release then follows the exit of that operation.
/// </para>
/// <para>
/// With an overflow hook, the buffer holds at most <see cref="DefaultBufferedPathLimit"/> distinct paths. Past the limit it hands every buffered
/// change to the hook and empties, so a prolonged outage costs one rescan of the roots that own the paths instead of a buffer that grows
/// with every changed path.
/// </para>
/// </remarks>
internal sealed class WorkspaceChangeForwarder : IDisposable
{
	/// <summary>The number of distinct buffered paths past which the buffer collapses into the overflow hook.</summary>
	internal const int DefaultBufferedPathLimit = 10_000;

	// Forwarding prerequisites.
	private readonly Func<bool> _canForwardAccessor;
	private readonly ProviderLifetime _disposal;

	private readonly Func<CancellationToken, Task<bool>> _ensureConnectionStartedAsync;
	private readonly Action<WorkspaceChangeForwardingFailure>? _logForwardingFailure;
	private readonly Action<IReadOnlyList<FileChange>>? _reportOverflow;
	private readonly int _bufferedPathLimit;

	// Buffered changes use the shared two-coalescer component: the replay buffer holds batches that failed a
	// dispatch attempt (they keep their original order and stay ahead of changes that arrived while the attempt was
	// in flight), and the pending buffer holds changes buffered while forwarding was not allowed. Keeping them apart
	// prevents a re-buffered delete from canceling against a newer create for the same path.
	private readonly FileChangeBuffer _bufferedChanges;
	private readonly SemaphoreSlim _forwardingGate = new(1, 1);

	// Admits forwarding operations until disposal begins; the last operation in flight to leave disposes the forwarding gate. Each operation
	// is represented by a completion source, which tells the disposal when that operation has left. The disposal does not wait for the
	// operations, so the gate allows it from inside one.
	private readonly object _operationsLock = new();
	private readonly StopGate<TaskCompletionSource> _operations;

	private int _forwardingGateDisposals;

	/// <param name="canForwardAccessor">Reports whether forwarding attempts are currently allowed.</param>
	/// <param name="disposal">The disposal of the owner, whose start stops forwarding.</param>
	/// <param name="ensureConnectionStartedAsync">Ensures the connection is running before forwarding.</param>
	/// <param name="logForwardingFailure">Logs forwarding failures together with batch context.</param>
	/// <param name="reportOverflow">
	/// Receives the buffered changes when the buffer passes its path limit, or <see langword="null"/> to buffer without a limit.
	/// </param>
	/// <param name="options">
	/// The provider options whose path comparison decides which paths coalesce into one change, or <see langword="null"/> for the defaults.
	/// </param>
	/// <param name="bufferedPathLimit">The number of distinct buffered paths past which the buffer collapses into the overflow hook.</param>
	internal WorkspaceChangeForwarder(
		Func<bool> canForwardAccessor,
		ProviderLifetime disposal,
		Func<CancellationToken, Task<bool>> ensureConnectionStartedAsync,
		Action<WorkspaceChangeForwardingFailure>? logForwardingFailure = null,
		Action<IReadOnlyList<FileChange>>? reportOverflow = null,
		LanguageServerFeatureProviderOptions? options = null,
		int bufferedPathLimit = DefaultBufferedPathLimit
	)
	{
		_canForwardAccessor = canForwardAccessor;
		_disposal = disposal;
		_ensureConnectionStartedAsync = ensureConnectionStartedAsync;
		_logForwardingFailure = logForwardingFailure;
		_reportOverflow = reportOverflow;
		_bufferedPathLimit = bufferedPathLimit;
		_bufferedChanges = new FileChangeBuffer(options?.PathComparison);
		_operations = new StopGate<TaskCompletionSource>(_operationsLock, stopWaitsForOperations: false);
	}

	/// <summary>The number of times the forwarding gate was disposed, which a test reads and production code never branches on.</summary>
	internal int ForwardingGateDisposals => Volatile.Read(ref _forwardingGateDisposals);

	/// <summary>Begins the disposal and releases the owned synchronization gate once no forwarding operations remain in flight.</summary>
	/// <remarks>
	/// The operation gate admits no new operation after the disposal began, and the gate of this forwarder is disposed only after the last operation
	/// in flight has left, so a late forwarding operation can never observe a disposed gate. The call returns without waiting for those operations.
	/// </remarks>
	public void Dispose()
	{
		Task drained;

		lock (_operationsLock)
		{
			if (!_operations.TryBeginStop(out _, out IReadOnlyList<TaskCompletionSource> inFlight))
				return;

			drained = inFlight.Count == 0 ? Task.CompletedTask : Task.WhenAll(inFlight.Select(static operation => operation.Task));
			_operations.RecordStopTask(drained);
		}

		if (drained.IsCompleted)
		{
			DisposeForwardingGate();
			return;
		}

		_ = drained.ContinueWith(
			continuationAction: static (_, owner) => ((WorkspaceChangeForwarder)owner!).DisposeForwardingGate(),
			state: this,
			cancellationToken: CancellationToken.None,
			continuationOptions: TaskContinuationOptions.ExecuteSynchronously,
			scheduler: TaskScheduler.Default
		);
	}

	private void DisposeForwardingGate()
	{
		Interlocked.Increment(ref _forwardingGateDisposals);
		_forwardingGate.Dispose();
	}

	/// <summary>Registers one forwarding operation unless the disposal began.</summary>
	/// <param name="operation">Receives the operation to pass to <see cref="ExitOperation"/> when it is admitted.</param>
	/// <returns><see langword="true"/> when the operation is admitted; otherwise, <see langword="false"/>.</returns>
	private bool TryEnterOperation([NotNullWhen(true)] out TaskCompletionSource? operation)
	{
		lock (_operationsLock)
		{
			if (_operations.IsStopping)
			{
				operation = null;
				return false;
			}

			operation = new TaskCompletionSource();

			_operations.Admit(operation, owner: this);
			return true;
		}
	}

	/// <summary>Reports whether the disposal began, so no new forwarding starts.</summary>
	private bool IsDisposeRequested
	{
		get
		{
			lock (_operationsLock)
				return _operations.IsStopping;
		}
	}

	/// <summary>Marks one forwarding operation finished, which lets a pending disposal release the synchronization gate.</summary>
	/// <param name="operation">The operation <see cref="TryEnterOperation"/> returned.</param>
	private void ExitOperation(TaskCompletionSource operation)
	{
		lock (_operationsLock)
			_operations.Complete(operation);

		operation.SetResult();
	}

	/// <summary>Attempts to forward a new change set immediately.</summary>
	/// <param name="changes">The file changes to forward.</param>
	/// <param name="forwardAsync">The connection forwarding callback.</param>
	/// <param name="cancellationToken">A token that can cancel the forwarding operation.</param>
	/// <returns><see langword="true"/> when the batch was forwarded immediately; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// <para>
	/// The change set is buffered when the startup callback reports no usable connection, when the startup callback throws, or when a recoverable
	/// live connection failure occurs. A startup throw is a connection gap: it must not escape into the watcher that dispatched the batch,
	/// and the changes must not be lost. When forwarding is not currently allowed, the change set is ignored. A change set that arrives
	/// after disposal was requested is dropped.
	/// </para>
	/// <para>
	/// A cancellation of the caller's token buffers the batch while the owner is alive and rethrows, whether it ends the connection start,
	/// the wait for the forwarding gate or the send.
	/// </para>
	/// <para>
	/// A recoverable failure re-buffers the whole batch, so a notification that was already delivered before the failure can be delivered
	/// again by a later replay. The batches this helper forwards are idempotent notification sets and the forwarding callback reports
	/// no per-notification progress, so the replay unit deliberately stays the whole batch.
	/// </para>
	/// </remarks>
	internal async Task<bool> DispatchAsync(
		IReadOnlyList<FileChange> changes,
		Func<IReadOnlyList<FileChange>, CancellationToken, Task> forwardAsync,
		CancellationToken cancellationToken
	)
	{
		if (!TryEnterOperation(out TaskCompletionSource? operation))
			return false;

		bool forwardingGateHeld = false;

		try
		{
			if (changes.Count == 0)
				return false;

			if (!_canForwardAccessor())
				return false;

			bool started;

			try
			{
				started = await _ensureConnectionStartedAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (ObjectDisposedException)
			{
				// A disposed connection cannot start; the batch is buffered unless the owner is disposed, the same
				// rule as the live-connection ObjectDisposedException path of TryForwardAsync.
				if (!_disposal.IsDisposed)
					BufferPending(changes);

				return false;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				// The caller gave up while the connection started: buffer the batch and surface the cancellation, as TryForwardAsync does.
				if (!_disposal.IsDisposed)
					BufferPending(changes);

				throw;
			}
			catch (Exception exception)
			{
				// An ensure-start failure is a connection gap, not a protocol defect: the batch is buffered for a
				// later replay unless the owner is disposed, and the failure is reported through the forwarding-failure
				// hook instead of escaping into the watcher that dispatched this batch.
				if (!_disposal.IsDisposed)
					BufferPending(changes);

				LogForwardingFailure(code: WorkspaceChangeForwardingFailureCodes.EnsureStartFailed, exception, changes, wasDropped: false);
				return false;
			}

			if (IsDisposeRequested)
				return false;

			if (!started)
			{
				BufferPending(changes);
				return false;
			}

			try
			{
				await _forwardingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
				forwardingGateHeld = true;
			}
			catch (OperationCanceledException)
			{
				// The caller gave up while another operation held the forwarding gate: nothing was forwarded, so the batch is buffered.
				if (!_disposal.IsDisposed)
					BufferPending(changes);

				throw;
			}

			if (IsDisposeRequested)
				return false;

			if (!_canForwardAccessor())
				return false;

			return await TryForwardAsync(changes, forwardAsync, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			if (forwardingGateHeld)
				_forwardingGate.Release();

			ExitOperation(operation);
		}
	}

	/// <summary>Replays the buffered changes now that forwarding is allowed again.</summary>
	/// <param name="forwardAsync">The connection forwarding callback.</param>
	/// <param name="cancellationToken">A token that can cancel the replay operation.</param>
	/// <returns>The buffered changes that were replayed successfully, or an empty list when none were forwarded.</returns>
	/// <remarks>
	/// If forwarding is still not allowed, the buffered set is preserved for a later replay attempt. A batch that failed a dispatch attempt
	/// is replayed before the changes that were buffered while the failed attempt was in flight.
	/// </remarks>
	internal async Task<IReadOnlyList<FileChange>> ReplayBufferedAsync(
		Func<IReadOnlyList<FileChange>, CancellationToken, Task> forwardAsync,
		CancellationToken cancellationToken
	)
	{
		if (!TryEnterOperation(out TaskCompletionSource? operation))
			return [];

		bool forwardingGateHeld = false;
		IReadOnlyList<FileChange>? bufferedChanges = null;

		try
		{
			// The early check runs inside the try so a throwing host accessor cannot leak the operation count.
			if (!_canForwardAccessor() || _bufferedChanges.IsEmpty)
				return [];

			await _forwardingGate.WaitAsync(cancellationToken).ConfigureAwait(false);
			forwardingGateHeld = true;

			if (IsDisposeRequested)
				return [];

			if (!_canForwardAccessor() || _bufferedChanges.IsEmpty)
				return [];

			bufferedChanges = _bufferedChanges.DrainBatch().Entries;

			if (bufferedChanges.Count == 0)
				return [];

			if (IsDisposeRequested)
				return [];

			bool forwarded = await TryForwardAsync(bufferedChanges, forwardAsync, cancellationToken).ConfigureAwait(false);

			return forwarded ? bufferedChanges : [];
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && bufferedChanges is null)
		{
			// The gate wait was canceled before any changes were drained, so there is nothing to restore and
			// nothing was forwarded. Once a batch is drained, caller cancellation propagates instead (the drained
			// batch is re-buffered by TryForwardAsync so it is not lost).
			return [];
		}
		finally
		{
			if (forwardingGateHeld)
				_forwardingGate.Release();

			ExitOperation(operation);
		}
	}

	/// <summary>Forwards a change set and converts recoverable transient live-forwarding failures into buffered replay state.</summary>
	/// <param name="changes">The file changes to forward.</param>
	/// <param name="forwardAsync">The connection forwarding callback.</param>
	/// <param name="cancellationToken">A token that can cancel the forwarding operation.</param>
	/// <returns><see langword="true"/> when the batch was forwarded successfully; otherwise, <see langword="false"/>.</returns>
	private async Task<bool> TryForwardAsync(
		IReadOnlyList<FileChange> changes,
		Func<IReadOnlyList<FileChange>, CancellationToken, Task> forwardAsync,
		CancellationToken cancellationToken
	)
	{
		try
		{
			await forwardAsync(changes, cancellationToken).ConfigureAwait(false);
			return true;
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			BufferReplayed(changes);
			return false;
		}
		catch (IOException exception)
		{
			BufferReplayed(changes);

			LogForwardingFailure(code: WorkspaceChangeForwardingFailureCodes.ConnectionFailed, exception, changes, wasDropped: false);
			return false;
		}
		catch (ObjectDisposedException)
		{
			if (!_disposal.IsDisposed)
				BufferReplayed(changes);

			return false;
		}
		catch (OperationCanceledException)
		{
			// The caller's token fired: re-buffer the batch so it is not lost, then surface the cancellation
			// instead of absorbing it, honoring the framework-wide caller-cancellation convention.
			if (!_disposal.IsDisposed)
				BufferReplayed(changes);

			throw;
		}
		catch (Exception exception)
		{
			// Intentional: unexpected failures are treated as logic/protocol defects rather than
			// transient connection gaps. Replaying here risks duplicating a partially observed batch
			// during later recovery, so the batch is logged and dropped on purpose.
			LogForwardingFailure(code: WorkspaceChangeForwardingFailureCodes.Unexpected, exception, changes, wasDropped: true);
			return false;
		}
	}

	private void BufferPending(IReadOnlyList<FileChange> changes)
	{
		_bufferedChanges.AddPendingRange(changes);
		CollapseOverLimit();
	}

	private void BufferReplayed(IReadOnlyList<FileChange> changes)
	{
		_bufferedChanges.AddReplayedRange(changes);
		CollapseOverLimit();
	}

	private void CollapseOverLimit()
	{
		if (_reportOverflow is null || _bufferedChanges.Count <= _bufferedPathLimit)
			return;

		// Two callers past the limit each drain a part, and the hook receives both parts, so no path escapes it.
		IReadOnlyList<FileChange> collapsed = _bufferedChanges.DrainBatch().Entries;

		if (collapsed.Count > 0)
			_reportOverflow(collapsed);
	}

	private void LogForwardingFailure(string code, Exception exception, IReadOnlyList<FileChange> changes, bool wasDropped)
	{
		string? firstPath = changes.Count > 0 ? changes[0].Path : null;
		_logForwardingFailure?.Invoke(new WorkspaceChangeForwardingFailure(code, exception, BatchCount: changes.Count, firstPath, wasDropped));
	}
}
