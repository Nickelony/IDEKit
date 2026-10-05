using Nickelony.IDEKit.Core.Threading;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Runs the ordered shutdown of a store: it stops admitting work, cancels what is in flight, waits for it and releases the tracked state.
/// </summary>
/// <remarks>
/// The first call marks the store stopping and captures the open reservations and the registered operations under the store lock,
/// cancels the lifetime after the lock is released, waits for the captured work, and then drops the tracked state and disposes the lifetime.
/// A later call observes the same task.
/// </remarks>
internal sealed class StoreTeardown
{
	private readonly object _syncRoot;
	private readonly WorkspaceDocumentTracker _tracker;
	private readonly StopGate<OperationLease> _stopGate;
	private readonly StoreLifetime _lifetime;

	/// <param name="syncRoot">The store's lock, which orders the stop gate and the tracked state.</param>
	/// <param name="tracker">The tracker whose open reservations the teardown waits for and whose state it clears.</param>
	/// <param name="stopGate">The store's stop gate, which tracks the registered operations.</param>
	/// <param name="lifetime">The store lifetime, which the teardown cancels and then disposes.</param>
	internal StoreTeardown(object syncRoot, WorkspaceDocumentTracker tracker, StopGate<OperationLease> stopGate, StoreLifetime lifetime)
	{
		_syncRoot = syncRoot;
		_tracker = tracker;
		_stopGate = stopGate;
		_lifetime = lifetime;
	}

	/// <summary>Begins the shutdown, or joins the one that is running.</summary>
	/// <returns>A task that completes when the shutdown has finished.</returns>
	/// <exception cref="InvalidOperationException">The call runs inside an operation, such as a file-system callback of one.</exception>
	internal ValueTask StopAsync()
	{
		Task stopTask;
		var lifetimeCanceled = new TaskCompletionSource();

		lock (_syncRoot)
		{
			if (!_stopGate.TryBeginStop(out Task? existingStopTask, out IReadOnlyList<OperationLease> activeOperations))
				return new ValueTask(existingStopTask);

			var operations = new Task[activeOperations.Count];

			for (int index = 0; index < activeOperations.Count; index++)
				operations[index] = activeOperations[index].Completion.Task;

			stopTask = StopCoreAsync(_tracker.GetOpenReservationTasks(), operations, lifetimeCanceled.Task);
			_stopGate.RecordStopTask(stopTask);
		}

		// The lifetime is canceled only after the state lock is released. Its cancellation callbacks are
		// host-visible: a linked source observes it, and a registration on that linked token can run
		// inline on the canceling thread, so a host callback that blocks on a store operation must not
		// deadlock against this lock. The stop has already marked the store and captured the in-flight
		// work while the lock was held, so no registered operation can start in between and miss the
		// cancellation. StopCoreAsync starts under the lock but waits for lifetimeCanceled before it
		// does anything else, so it disposes the lifetime only after this cancellation, also when
		// nothing was captured. The waiting continuation runs inline when the gate completes, so a
		// stop with nothing in flight still completes before this call returns.
		try
		{
			_lifetime.Cancel();
		}
		finally
		{
			lifetimeCanceled.SetResult();
		}

		return new ValueTask(stopTask);
	}

	private async Task StopCoreAsync(Task[] reservations, Task[] operations, Task lifetimeCanceled)
	{
		await lifetimeCanceled.ConfigureAwait(false);

		if (reservations.Length > 0)
			await Task.WhenAll(reservations).ConfigureAwait(false);

		if (operations.Length > 0)
			await Task.WhenAll(operations).ConfigureAwait(false);

		lock (_syncRoot)
			_tracker.ClearAfterDisposal();

		_lifetime.Dispose();
	}
}
