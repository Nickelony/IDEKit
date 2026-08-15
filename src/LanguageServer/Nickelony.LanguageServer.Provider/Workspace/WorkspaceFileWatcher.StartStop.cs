namespace Nickelony.LanguageServer.Provider;

public sealed partial class WorkspaceFileWatcher
{
	/// <summary>
	/// Starts watching the configured workspace for external file changes and reports the startup result.
	/// </summary>
	/// <remarks>
	/// Only a <see cref="WorkspaceWatcherStartOutcome.StartupFailed"/> result disposes this watcher instance, so later
	/// retries should use a replacement watcher; a <see cref="WorkspaceWatcherStartOutcome.WorkspaceRootMissing"/> result
	/// leaves the instance startable once the root exists.
	/// </remarks>
	/// <returns>The watcher startup result, carrying the outcome and the startup exception when a start failed.</returns>
	public WorkspaceWatcherStartResult Start()
	{
		if (_isDisposed)
			return new(WorkspaceWatcherStartOutcome.Disposed, null);

		try
		{
			if (!Directory.Exists(_workspaceRootDirectoryPath))
			{
				_logger.LogDebug("Workspace file watcher start skipped because '{Workspace}' does not exist.", _workspaceRootDirectoryPath);
				return new(WorkspaceWatcherStartOutcome.WorkspaceRootMissing, null);
			}
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Failed to validate the workspace root for '{Workspace}' before starting the workspace watcher.", _workspaceRootDirectoryPath);

			Dispose();

			return new(WorkspaceWatcherStartOutcome.StartupFailed, exception);
		}

		try
		{
			lock (_watchersSyncRoot)
			{
				if (_isDisposed)
					return new(WorkspaceWatcherStartOutcome.Disposed, null);

				if (_watchers.Count > 0)
					return new(WorkspaceWatcherStartOutcome.AlreadyRunning, null);

				// Each watcher is registered in the owned set before it starts raising events: the error handler
				// ignores errors from watchers that are not registered, so activating a not-yet-registered watcher
				// could drop its failure and leave a dead registration behind.
				for (int i = 0; i < _watchSpecifications.Count; i++)
				{
					FileSystemWatcher watcher = CreateWatcher(_watchSpecifications[i]);

					_watchers.Add(watcher);
					TestHooks.WatcherActivation?.Invoke(watcher);
					watcher.EnableRaisingEvents = true;

					// A watcher error raised while it was starting already stopped and cleared the set and notified
					// the owner; stop starting specifications instead of resurrecting a set the owner was told failed.
					if (_watchers.Count == 0)
					{
						Dispose();

						return new(WorkspaceWatcherStartOutcome.StartupFailed, null);
					}
				}

				// Disposal marks the watcher disposed under the notification lock and then waits for this lock, so it can
				// win the race after the entry check; report the disposal instead of claiming a start that the disposal
				// tears down as soon as this lock is released.
				if (_isDisposed)
					return new(WorkspaceWatcherStartOutcome.Disposed, null);

				Interlocked.Exchange(ref _watcherFailureReported, 0);
				Interlocked.Exchange(ref _consecutiveDispatchFailures, 0);

				_logger.LogDebug("Started workspace file watcher for '{Workspace}' with {Count} watcher(s).",
					_workspaceRootDirectoryPath,
					_watchers.Count);

				return new(WorkspaceWatcherStartOutcome.Started, null);
			}
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Failed to start the workspace file watcher for '{Workspace}'.", _workspaceRootDirectoryPath);

			Dispose();

			return new(WorkspaceWatcherStartOutcome.StartupFailed, exception);
		}
	}

	/// <summary>
	/// Creates and configures a file-system watcher for one specification without starting it.
	/// </summary>
	/// <param name="specification">The watch specification to apply.</param>
	/// <returns>The configured, not yet activated file-system watcher.</returns>
	/// <remarks>
	/// The <see cref="NotifyFilters"/> value, the internal buffer size, and the file-name filter are applied on every
	/// platform, but only Windows interprets them with full fidelity. There the settings map directly onto the NTFS
	/// change-notification API, and a burst that overflows the 64 KiB buffer is reported through
	/// <see cref="FileSystemWatcher.Error"/> instead of silently dropping changes. On Linux, macOS, and other
	/// platforms the runtime backs <see cref="FileSystemWatcher"/> with the operating system's native facility
	/// (inotify, FSEvents, or kqueue), where the notify filter and buffer size are best-effort hints: some filters
	/// are ignored, events may be coalesced or delivered with different timing, and a native overflow or watch
	/// failure surfaces through the same Error event. Correctness never depends on the buffer size, because a
	/// reported failure is handled as an unrecoverable watcher failure (which resynchronizes the workspace) rather
	/// than as a lost change. Each specification owns its own file-system watcher, so the per-watcher cost scales
	/// with the number of specifications. The caller registers the watcher before activating it so a failure raised
	/// while it starts is never treated as an unknown (superseded) watcher.
	/// </remarks>
	private FileSystemWatcher CreateWatcher(WorkspaceWatchSpecification specification)
	{
		FileSystemWatcher watcher = (TestHooks.FileSystemWatcherFactory ?? CreateFileSystemWatcher)(_workspaceRootDirectoryPath, specification);
		ArgumentNullException.ThrowIfNull(watcher);

		try
		{
			watcher.IncludeSubdirectories = specification.IncludeSubdirectories;
			watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.DirectoryName | NotifyFilters.Size;
			// 64 KB gives the internal change buffer headroom for bursty writes on large workspaces; the watcher reports
			// a buffer overflow through its Error event, which is handled as a watcher failure.
			watcher.InternalBufferSize = 64 * 1024;

			watcher.Created += (_, e) => QueueChange(e.FullPath, FileChangeKind.Created);
			watcher.Changed += (_, e) => QueueChange(e.FullPath, FileChangeKind.Changed);
			watcher.Deleted += (_, e) => QueueChange(e.FullPath, FileChangeKind.Deleted);
			// The raising instance is captured so an error callback that was already queued when this watcher was
			// superseded cannot stop a replacement watcher set created by a later Start call.
			watcher.Error += (_, e) => HandleWatcherError(e.GetException(), watcher);
			watcher.Renamed += (_, e) =>
			{
				// A rename is raised when either endpoint matches the filter; forward only the endpoints that still
				// match so a rename out of the watched set cannot forward an unmatched create.
				if (specification.MatchesFileName(e.OldFullPath))
					QueueChange(e.OldFullPath, FileChangeKind.Deleted);

				if (specification.MatchesFileName(e.FullPath))
					QueueChange(e.FullPath, FileChangeKind.Created);
			};

			return watcher;
		}
		catch
		{
			// A watcher that failed configuration is not registered yet; dispose it here so a partially
			// constructed instance cannot leak its 64 KiB internal buffer.
			TryDispose(watcher, nameof(FileSystemWatcher));
			throw;
		}
	}

	private static FileSystemWatcher CreateFileSystemWatcher(string workspaceRootDirectoryPath, WorkspaceWatchSpecification specification)
		=> new(workspaceRootDirectoryPath, specification.Filter);

	/// <summary>
	/// Stops all active watchers and reports the failure to the owner once.
	/// </summary>
	/// <param name="exception">The watcher error, if one was provided.</param>
	/// <param name="watcher">
	/// The file-system watcher that raised the error, or <see langword="null"/> when the caller does not know it.
	/// An error raised by a watcher that is no longer registered is ignored because it belongs to a watcher set
	/// that has already been superseded or stopped.
	/// </param>
	internal void HandleWatcherError(Exception? exception, FileSystemWatcher? watcher = null)
	{
		if (_isDisposed)
			return;

		if (watcher is not null)
		{
			lock (_watchersSyncRoot)
			{
				if (!_watchers.Contains(watcher))
					return;
			}
		}

		// Disposing the watchers synchronously from this callback is safe on .NET 8: FileSystemWatcher raises Error on
		// a thread-pool callback that Dispose does not join. Keep the synchronous stop so the owner's failure callback
		// always observes an already-stopped watcher and can recreate it immediately.
		StopWatching();

		if (_isDisposed)
			return;

		if (Interlocked.Exchange(ref _watcherFailureReported, 1) != 0)
			return;

		_logger.LogWarning(exception,
			"Workspace file watcher encountered an internal error for '{Workspace}' and stopped watching until the owner handles recovery.",
			_workspaceRootDirectoryPath);

		RaiseWatcherFailure(exception);
	}

	/// <summary>
	/// Raises the watcher failure notification and dispatches any pending batch afterwards.
	/// </summary>
	/// <remarks>
	/// The notification runs under the notification lock, so disposal that starts afterwards waits for a running
	/// callback and no callback can start once disposal returned; the owner may dispose this watcher from inside the
	/// callback because disposal never waits for dispatch operations.
	/// </remarks>
	/// <param name="exception">The watcher error, if one was provided.</param>
	private void RaiseWatcherFailure(Exception? exception)
	{
		lock (_notificationSyncRoot)
		{
			if (_isDisposed)
				return;

			try
			{
				_onWatcherFailed?.Invoke(this, exception);
			}
			catch (Exception callbackException)
			{
				_logger.LogWarning(callbackException, "Workspace watcher failure handler threw.");
			}
		}

		// Give changes that were still pending when the failure occurred one delivery attempt after the owner
		// reacted; a disposed or replaced watcher drops them in the dispatch admission check.
		if (!_isDisposed && !_pendingChanges.IsEmpty)
			_ = DispatchPendingChangesAsync();
	}

	/// <summary>
	/// Stops and disposes all active file-system watchers.
	/// </summary>
	private void StopWatching()
	{
		List<FileSystemWatcher> watchersToDispose;

		lock (_watchersSyncRoot)
		{
			if (_watchers.Count == 0)
				return;

			watchersToDispose = [.. _watchers];
			_watchers.Clear();
		}

		for (int i = watchersToDispose.Count - 1; i >= 0; i--)
		{
			FileSystemWatcher watcher = watchersToDispose[i];
			TryDispose(watcher, nameof(FileSystemWatcher));
		}
	}
}
