namespace Nickelony.LanguageServer.Provider;

public sealed partial class WorkspaceFileWatcher
{
	/// <summary>
	/// Stops watching and releases the watcher without waiting for in-flight dispatch operations.
	/// </summary>
	/// <remarks>
	/// Disposal is idempotent and non-blocking: changes that are still buffered or scheduled for a retry are
	/// dropped, and the dispatch cancellation token is canceled so in-flight callbacks can observe disposal.
	/// The lifetime token source and the dispatch gate are deliberately not disposed: an in-flight dispatch may
	/// still be waiting on the gate or passing the token to its callback, and disposal must not fault it with an
	/// <see cref="ObjectDisposedException"/>. Both objects become garbage together with the watcher once the
	/// in-flight operation releases them.
	/// See the class remarks for the shared disposal contract.
	/// </remarks>
	public void Dispose()
	{
		if (!BeginDispose())
			return;

		CancelLifetimeToken();
		StopWatching();
		_pendingChanges.Stop();
		_pendingChanges.Dispose();
	}

	/// <summary>
	/// Stops watching and releases the watcher without waiting for in-flight dispatch operations.
	/// </summary>
	/// <remarks>
	/// This method performs the same non-blocking teardown as <see cref="Dispose"/>.
	/// See the class remarks for the shared disposal contract.
	/// </remarks>
	public ValueTask DisposeAsync()
	{
		Dispose();
		return ValueTask.CompletedTask;
	}

	/// <summary>
	/// Marks the watcher disposed once, serialized against the failure notification.
	/// </summary>
	/// <returns><see langword="true"/> when this call started disposal; otherwise, <see langword="false"/>.</returns>
	private bool BeginDispose()
	{
		// Setting the disposed flag under the notification lock guarantees that once disposal returned, no owner
		// failure callback is still running or can start afterwards.
		lock (_notificationSyncRoot)
		{
			if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
				return false;

			_isDisposed = true;
			return true;
		}
	}

	/// <summary>
	/// Cancels the lifetime token so in-flight dispatch operations can observe disposal.
	/// </summary>
	private void CancelLifetimeToken()
	{
		try
		{
			_lifetimeCts.Cancel();
		}
		catch (ObjectDisposedException)
		{ }
		catch (Exception exception)
		{
			// CancellationTokenSource.Cancel wraps throwing cancellation callbacks in an AggregateException;
			// cancellation failures must not keep disposal from completing.
			_logger.LogDebug(exception, "Workspace file watcher lifetime cancellation for '{Workspace}' reported one or more callback failures.", _workspaceRootDirectoryPath);
		}
	}

	/// <summary>
	/// Disposes one watcher-owned file-system watcher while logging failures.
	/// </summary>
	/// <param name="watcher">The watcher to dispose.</param>
	/// <param name="resourceName">The resource name used for diagnostics.</param>
	private void TryDispose(FileSystemWatcher watcher, string resourceName)
	{
		try
		{
			watcher.Dispose();
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Failed to dispose workspace watcher resource '{ResourceName}'.", resourceName);
		}
	}
}
