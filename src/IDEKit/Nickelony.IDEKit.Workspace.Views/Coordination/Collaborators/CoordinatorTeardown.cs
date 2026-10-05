using Nickelony.IDEKit.Core.Threading;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>
/// Runs the ordered shutdown of a coordinator: it rejects new operations, waits for the active ones, closes the registered views and raises the close
/// of their documents.
/// </summary>
/// <remarks>
/// The first call marks the coordinator stopping and captures the active operations under the coordinator's lock, and the stop task it records
/// is the one every later call observes. A view is released through the dispatcher, so an empty registration set never invokes it.
/// </remarks>
internal sealed class CoordinatorTeardown
{
	private readonly object _syncRoot;
	private readonly StopGate<Task> _stopGate;
	private readonly ViewTracker _views;
	private readonly Func<Action, Task> _dispatchViewAction;
	private readonly AttachedDocumentChangeForwarder _attachedChanges;

	/// <param name="syncRoot">The coordinator's lock, which orders the stop gate and the view tracker.</param>
	/// <param name="stopGate">The coordinator's stop gate, which tracks the active operations.</param>
	/// <param name="views">The tracker of the registered views, which the teardown drains.</param>
	/// <param name="dispatchViewAction">The delegate that runs actions where view access is legal.</param>
	/// <param name="attachedChanges">The forwarder that raises the close of each attached document after its views closed.</param>
	internal CoordinatorTeardown(
		object syncRoot,
		StopGate<Task> stopGate,
		ViewTracker views,
		Func<Action, Task> dispatchViewAction,
		AttachedDocumentChangeForwarder attachedChanges
	)
	{
		_syncRoot = syncRoot;
		_stopGate = stopGate;
		_views = views;
		_dispatchViewAction = dispatchViewAction;
		_attachedChanges = attachedChanges;
	}

	/// <summary>Begins the shutdown, or joins the one that is running.</summary>
	/// <returns>A task that completes when the shutdown has finished.</returns>
	/// <exception cref="InvalidOperationException">The call runs inside an active operation.</exception>
	internal Task StopAsync()
	{
		TaskCompletionSource completion;
		IReadOnlyList<Task> activeOperations;

		lock (_syncRoot)
		{
			if (!_stopGate.TryBeginStop(out Task? stopTask, out activeOperations))
				return stopTask;

			completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			_stopGate.RecordStopTask(completion.Task);
		}

		_ = StopCoreAsync(activeOperations, completion);
		return completion.Task;
	}

	private async Task StopCoreAsync(IReadOnlyList<Task> activeOperations, TaskCompletionSource completion)
	{
		try
		{
			if (activeOperations.Count > 0)
				await Task.WhenAll(activeOperations).ConfigureAwait(false);

			KeyValuePair<IWorkspaceDocumentView, ViewEntry>[] registeredViews;

			lock (_syncRoot)
				registeredViews = _views.Drain();

			// Views are released through the dispatcher; an empty registration set never
			// invokes it, so teardown does not depend on a dispatcher with nothing to run.
			if (registeredViews.Length > 0)
			{
				await _dispatchViewAction(() =>
					{
						foreach ((IWorkspaceDocumentView view, ViewEntry registration) in registeredViews)
						{
							try
							{
								view.ApplyRequested -= registration.Forwarder.Handler;
							}
							catch (Exception)
							{
								// A throwing event accessor must not fault the memoized stop task for later callers, and the
								// teardown has no logger or result to report it through; the remaining views are still released.
							}

							try
							{
								view.Close();
							}
							catch (Exception)
							{
								// A view whose close throws is contained for the same reason.
							}
						}
					})
					.ConfigureAwait(false);
			}

			await _attachedChanges.RaisePendingAsync().ConfigureAwait(false);
			completion.TrySetResult();
		}
		catch (Exception exception)
		{
			completion.TrySetException(exception);
		}
	}
}
