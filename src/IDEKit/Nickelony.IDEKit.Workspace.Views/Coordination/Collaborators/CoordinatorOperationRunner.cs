using Nickelony.IDEKit.Core.Threading;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>
/// Runs each operation of a coordinator as an active operation, so a stop waits for it, and raises the attached-document changes it queued.
/// </summary>
/// <remarks>
/// The runner raises the changes of an operation before the operation completes, and the changes
/// of a failed operation too. It admits under the coordinator's lock, which also orders the view tracker, and the exception that rejects an operation
/// after a stop began names the coordinator.
/// </remarks>
internal sealed class CoordinatorOperationRunner
{
	private readonly object _owner;
	private readonly object _syncRoot;

	private readonly StopGate<Task> _stopGate;
	private readonly AttachedDocumentChangeForwarder _attachedChanges;

	internal CoordinatorOperationRunner(object owner, object syncRoot, StopGate<Task> stopGate, AttachedDocumentChangeForwarder attachedChanges)
	{
		_owner = owner;
		_syncRoot = syncRoot;
		_stopGate = stopGate;
		_attachedChanges = attachedChanges;
	}

	/// <summary>Runs an asynchronous operation while it is registered with the stop gate.</summary>
	/// <returns>The result of <paramref name="body"/>, after the raise of the queued changes or after its contained fault.</returns>
	/// <exception cref="ObjectDisposedException">The coordinator began stopping.</exception>
	/// <remarks>
	/// A body that changes a document and whose result must reach its caller sets <paramref name="containRaiseFault"/>, so a fault of the raise
	/// that follows it is contained and the queued changes wait for the next operation.
	/// </remarks>
	internal async Task<T> RunAsync<T>(Func<Task<T>> body, bool containRaiseFault = false)
	{
		TaskCompletionSource operation = new(TaskCreationOptions.RunContinuationsAsynchronously);

		lock (_syncRoot)
			_stopGate.Admit(operation.Task, owner: _owner);

		try
		{
			T result;

			try
			{
				result = await body().ConfigureAwait(false);
			}
			catch
			{
				await RaiseChangesOfFailedOperationAsync().ConfigureAwait(false);
				throw;
			}

			// The changes are raised while the operation is still active, so a stop waits for them and the caller sees them before the result.
			try
			{
				await _attachedChanges.RaisePendingAsync().ConfigureAwait(false);
			}
			catch (Exception) when (containRaiseFault)
			{
				// The result is returned without the raise; the queued changes wait for the next operation.
			}

			return result;
		}
		finally
		{
			lock (_syncRoot)
			{
				_stopGate.Complete(operation.Task);
				operation.TrySetResult();
			}
		}
	}

	// A failed operation can still have attached or detached a view, so its changes are raised too. Its own fault stays the outcome of the operation,
	// so a dispatcher that also fails the raise does not replace it, and the next operation raises the changes again.
	private async Task RaiseChangesOfFailedOperationAsync()
	{
		try
		{
			await _attachedChanges.RaisePendingAsync().ConfigureAwait(false);
		}
		catch
		{
			// The caller rethrows the fault of the operation.
		}
	}
}
