namespace Nickelony.IDEKit.Workspace.Documents;

public sealed partial class WorkspaceDocumentStore
{
	/// <summary>
	/// Releases all document resources after tracked open reservations and registered disk operations complete.
	/// </summary>
	/// <remarks>
	/// Disposal is idempotent: the first call marks the store disposed, cancels the lifetime token that
	/// in-flight operations observe, waits for open reservations and registered operations, and disposes
	/// the per-document disk gates. Later calls observe the same disposal task. An operation that has
	/// already started observes the disposal through the lifetime token and reports its canceled outcome
	/// (<see cref="WorkspaceDocumentReloadOutcome.Canceled"/> for a reload,
	/// <see cref="WorkspaceDocumentOpenOutcome.Canceled"/> for an open); only a member invoked after
	/// disposal throws <see cref="ObjectDisposedException"/>.
	/// </remarks>
	/// <returns>A task that completes when disposal has finished.</returns>
	public ValueTask DisposeAsync()
	{
		Task disposeTask;

		lock (_stateLock)
		{
			if (_disposeTask is not null)
				return new ValueTask(_disposeTask);

			_disposed = true;

			Task[] reservations = new Task[_openReservations.Count];
			int index = 0;
			foreach (OpenReservation reservation in _openReservations.Values)
				reservations[index++] = reservation.Completion.Task;

			Task[] operations = new Task[_activeOperations.Count];
			index = 0;
			foreach (OperationRegistration operation in _activeOperations)
				operations[index++] = operation.Completion.Task;

			disposeTask = DisposeCoreAsync(reservations, operations);
			_disposeTask = disposeTask;
		}

		// The lifetime token is canceled only after the state lock is released. Its cancellation
		// callbacks are host-visible: a linked source observes it, and a registration on that linked
		// token can run inline on the canceling thread, so a host callback that blocks on a store
		// operation must not deadlock against this lock. Disposal has already marked the store and
		// captured the in-flight work while the lock was held, so no operation can start in between and
		// miss the cancellation: a linked source created afterwards is born canceled.
		CancelLifetimeCancellation();

		return new ValueTask(disposeTask);
	}

	// Cancels the store lifetime token. DisposeCoreAsync disposes the source once every captured
	// reservation and operation has completed; when nothing was in flight that happens synchronously
	// under the state lock above, and the already-disposed source needs no cancellation.
	private void CancelLifetimeCancellation()
	{
		try
		{
			_lifetimeCancellation.Cancel();
		}
		catch (ObjectDisposedException)
		{
			// Disposal already completed and released the source.
		}
		catch (AggregateException)
		{
			// A cancellation callback registered on the lifetime token threw. The token is canceled either
			// way, and a host callback failure must not fault this store's disposal.
		}
	}

	private async Task DisposeCoreAsync(Task[] reservations, Task[] operations)
	{
		if (reservations.Length > 0)
			await Task.WhenAll(reservations).ConfigureAwait(false);
		if (operations.Length > 0)
			await Task.WhenAll(operations).ConfigureAwait(false);

		lock (_stateLock)
		{
			// Every open reservation and registered operation has completed, and the paths that bypass
			// the gates (logical mutations and dirty reloads) never touch them, so no caller can still
			// acquire a disk gate: dispose them before the tracked documents are dropped. Gates of
			// documents that left tracking (deleted documents) are deliberately not disposed earlier:
			// their document is no longer reachable, so nobody can acquire them, and disposing a gate at
			// removal time could race a late release from an operation that still references it.
			foreach (LogicalDocument document in _documents.Values)
				document.DiskOperationGate.Dispose();

			_documents.Clear();
			_openReservations.Clear();
			_destinationReservations.Clear();
		}

		_lifetimeCancellation.Dispose();
	}

	private void ThrowIfDisposed()
	{
		lock (_stateLock)
			ThrowIfDisposedUnderLock();
	}

	// Creates a cancellation source linked to the caller's token and the store lifetime. The dirty
	// reload branch runs without an active-operation registration, so the lifetime source can already
	// be disposed when the branch starts; that case reports null so the caller returns the documented
	// Canceled outcome instead of observing ObjectDisposedException.
	private CancellationTokenSource? TryCreateLifetimeLinkedCancellation(CancellationToken cancellationToken)
		=> TryCreateLifetimeLinkedCancellation(_lifetimeCancellation, cancellationToken);

	internal static CancellationTokenSource? TryCreateLifetimeLinkedCancellation(
		CancellationTokenSource lifetimeCancellation,
		CancellationToken cancellationToken)
	{
		try
		{
			return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeCancellation.Token);
		}
		catch (ObjectDisposedException)
		{
			return null;
		}
	}

	private void ThrowIfDisposedUnderLock()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
