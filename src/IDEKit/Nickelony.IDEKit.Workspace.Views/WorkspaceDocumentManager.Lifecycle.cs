namespace Nickelony.IDEKit.Workspace.Views;

public sealed partial class WorkspaceDocumentManager
{
	/// <inheritdoc/>
	public Task StopAsync()
	{
		TaskCompletionSource completion;
		Task[] activeOperations;

		lock (_stateLock)
		{
			if (_stopTask is not null)
				return _stopTask;

			_stopping = true;
			completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			_stopTask = completion.Task;
			activeOperations = _activeOperations.ToArray();
		}

		_ = StopCoreAsync(activeOperations, completion);
		return completion.Task;
	}

	/// <summary>
	/// Stops the manager and releases all registered views.
	/// </summary>
	/// <remarks>
	/// Disposal delegates to <see cref="StopAsync"/>: new operations are rejected, active operations are
	/// awaited, and registered views are closed through the dispatch delegate. The store the manager was
	/// constructed with is not disposed; its lifetime belongs to the host. Calling this member more
	/// than once observes the same stop completion.
	/// </remarks>
	/// <returns>A task that completes when the manager has stopped.</returns>
	public ValueTask DisposeAsync()
		=> new(StopAsync());

	// Runs an asynchronous operation while it is registered as an active manager
	// operation so StopAsync waits for it to finish before detaching views.
	private async Task<T> RunOperationAsync<T>(Func<Task<T>> body)
	{
		TaskCompletionSource operation = EnterOperation();

		try
		{
			return await body().ConfigureAwait(false);
		}
		finally
		{
			CompleteOperation(operation);
		}
	}

	private TaskCompletionSource EnterOperation()
	{
		lock (_stateLock)
		{
			ThrowIfStoppingUnderLock();

			TaskCompletionSource operation = new(TaskCreationOptions.RunContinuationsAsynchronously);
			_activeOperations.Add(operation.Task);
			return operation;
		}
	}

	private void CompleteOperation(TaskCompletionSource operation)
	{
		lock (_stateLock)
		{
			_activeOperations.Remove(operation.Task);
			operation.TrySetResult();
		}
	}

	private async Task StopCoreAsync(
		Task[] activeOperations,
		TaskCompletionSource completion)
	{
		try
		{
			if (activeOperations.Length > 0)
				await Task.WhenAll(activeOperations).ConfigureAwait(false);

			KeyValuePair<IWorkspaceDocumentView, ViewRegistration>[] registeredViews;
			lock (_stateLock)
			{
				registeredViews = _registrations.ToArray();

				// Every collection that tracks a view is released: leaving any of them populated
				// would retain every view after the teardown and leave the stopped state inconsistent.
				_registrations.Clear();
				_viewsByDocument.Clear();
				_viewsByViewId.Clear();
			}

			// Views are released through the dispatch delegate; an empty registration set never
			// invokes the delegate, so teardown does not depend on a dispatcher with nothing to run.
			if (registeredViews.Length > 0)
			{
				await _dispatchViewAction(() =>
				{
					foreach ((IWorkspaceDocumentView view, ViewRegistration registration) in registeredViews)
					{
						try
						{
							view.ApplyRequested -= registration.Subscription.Handler;
						}
						catch
						{
							// A throwing event accessor must not fault the memoized stop task for later callers;
							// the remaining views are still released.
						}

						try
						{
							view.Close();
						}
						catch
						{ }
					}
				}).ConfigureAwait(false);
			}

			completion.TrySetResult();
		}
		catch (Exception exception)
		{
			completion.TrySetException(exception);
		}
	}

	private void ThrowIfStoppingUnderLock()
	{
		ObjectDisposedException.ThrowIf(_stopping, this);
	}
}
