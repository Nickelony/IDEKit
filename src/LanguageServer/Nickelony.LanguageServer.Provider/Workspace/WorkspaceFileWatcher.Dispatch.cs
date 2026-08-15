namespace Nickelony.LanguageServer.Provider;

public sealed partial class WorkspaceFileWatcher
{
	/// <summary>
	/// Queues a single workspace change for the next debounced dispatch.
	/// </summary>
	/// <param name="filePath">The changed file path.</param>
	/// <param name="kind">The change kind.</param>
	internal void QueueChange(string filePath, FileChangeKind kind)
	{
		if (_isDisposed)
			return;

		if (!LanguageServerPaths.TryNormalizeLocalPath(filePath, out string normalizedFilePath))
		{
			_logger.LogDebug("Workspace file watcher ignored a change for '{FilePath}' because the path could not be normalized.", filePath);
			return;
		}

		_pendingChanges.Queue(normalizedFilePath, kind);
	}

	/// <summary>
	/// Dispatches any currently pending changes.
	/// </summary>
	/// <remarks>
	/// Changes are not delivered once disposal began: the dispatch is skipped when the watcher is already disposed,
	/// and a drained batch whose dispatch fails or observes cancellation after disposal started is dropped. A
	/// dispatch callback that completes normally has already delivered its batch, even when disposal began while
	/// the dispatch was in flight.
	/// </remarks>
	internal async Task DispatchPendingChangesAsync()
	{
		if (_isDisposed)
			return;

		await DispatchPendingChangesCoreAsync().ConfigureAwait(false);
	}

	/// <summary>
	/// Drains and forwards the currently pending file changes.
	/// </summary>
	private async Task DispatchPendingChangesCoreAsync()
	{
		bool dispatchGateHeld = false;
		FileChangeBatch? batch = null;

		try
		{
			await _dispatchGate.WaitAsync(_lifetimeCts.Token).ConfigureAwait(false);
			dispatchGateHeld = true;

			// Disposal drops anything that is still buffered: the debounce buffer and scheduled retries do not
			// survive the watcher, and the replacement watcher reconciles missed changes through its snapshot.
			if (_isDisposed || _pendingChanges.IsEmpty)
				return;

			batch = _pendingChanges.DrainBatch();

			if (batch.Count == 0)
				return;

			await _dispatchAsync(batch, _lifetimeCts.Token).ConfigureAwait(false);
			Interlocked.Exchange(ref _consecutiveDispatchFailures, 0);
		}
		catch (Exception exception)
		{
			if (batch is null)
			{
				_logger.LogDebug(exception, "Workspace file watcher dispatch for '{Workspace}' was interrupted before any pending change was drained.", _workspaceRootDirectoryPath);
				return;
			}

			// Disposal cancels the lifetime token and drops a batch that was already drained; that is expected
			// teardown, not a dispatch failure.
			if (_isDisposed)
			{
				_logger.LogDebug(exception, "Workspace file watcher dropped {Count} queued change(s) for '{Workspace}' because the watcher was disposed.", batch.Count, _workspaceRootDirectoryPath);
				return;
			}

			int consecutiveDispatchFailures = Interlocked.Increment(ref _consecutiveDispatchFailures);

			if (consecutiveDispatchFailures < DispatchFailureMaxAttempts)
			{
				TimeSpan retryDelay = GetDispatchRetryDelay(consecutiveDispatchFailures);

				_logger.LogDebug(exception,
					"Workspace file watcher dispatch failed for '{Workspace}' with {Count} queued change(s) {FailureCount} times in a row; retrying in {RetryDelayMs} ms.",
					_workspaceRootDirectoryPath,
					batch.Count,
					consecutiveDispatchFailures,
					(int)retryDelay.TotalMilliseconds);

				_pendingChanges.Requeue(batch, retryDelay);
			}
			else
			{
				// Delivery no longer depends on an owner reaction: the batch is dropped and the watchers stop; the
				// owner callback fires only once per failure sequence because a watcher error that runs concurrently
				// may already have reported the same sequence.
				StopWatching();

				if (Interlocked.Exchange(ref _watcherFailureReported, 1) == 0)
				{
					_logger.LogWarning(exception,
						"Workspace file watcher dispatch failed for '{Workspace}' {FailureCount} times in a row; the {Count} pending change(s) were dropped and the owner is notified so the watcher can be replaced.",
						_workspaceRootDirectoryPath,
						consecutiveDispatchFailures,
						batch.Count);

					RaiseWatcherFailure(exception);
				}
				else
				{
					_logger.LogWarning(exception,
						"Workspace file watcher dispatch failed for '{Workspace}' {FailureCount} times in a row; the {Count} pending change(s) were dropped and the owner was already notified for this watcher, so no new notification is raised.",
						_workspaceRootDirectoryPath,
						consecutiveDispatchFailures,
						batch.Count);
				}
			}
		}
		finally
		{
			if (dispatchGateHeld)
				_dispatchGate.Release();
		}
	}

	/// <summary>
	/// Gets the bounded exponential retry delay for the supplied consecutive failure count.
	/// </summary>
	/// <param name="consecutiveDispatchFailures">The number of consecutive dispatch failures.</param>
	/// <returns>The delay before the next dispatch attempt.</returns>
	private static TimeSpan GetDispatchRetryDelay(int consecutiveDispatchFailures)
	{
		// Bounded attempts keep the shift small: the first retry waits one debounce delay, the second waits two.
		int exponentialShift = consecutiveDispatchFailures - 1;
		double retryDelayMilliseconds = s_dispatchDebounce.TotalMilliseconds * (1 << exponentialShift);

		return TimeSpan.FromMilliseconds(retryDelayMilliseconds);
	}
}
