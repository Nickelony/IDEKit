using System.Collections.ObjectModel;

namespace Nickelony.IDEKit.FileWatching;

/// <summary>Watches a workspace root for configured file patterns and forwards coalesced changes to the owner.</summary>
/// <remarks>
/// A failed dispatch is retried with bounded exponential backoff; when the attempt limit is reached
/// the pending changes are dropped, the watchers stop, and the owner is notified once so it can dispose this watcher and create a replacement.
/// If creation or activation fails, this instance is disposed and recovery likewise needs a replacement. Disposal is idempotent,
/// can be initiated from any context (including the callbacks), and does not wait for in-flight dispatch. The full lifecycle and its rationale live
/// in the <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/FileWatchingGuide.md">file-watching guide</see>.
/// </remarks>
public sealed class FileWatcher : IFileWatcher, IAsyncDisposable
{
	private readonly ILogger _logger;

	// Debounce policy: changes within the debounce delay of each other coalesce into one dispatch, but a sustained
	// edit stream still dispatches at least once per maximum dispatch delay. A failed dispatch is retried with the debounce
	// delay as the base of its exponential backoff.
	private static readonly TimeSpan s_dispatchDebounce = TimeSpan.FromMilliseconds(250);
	private static readonly TimeSpan s_maxDispatchDelay = TimeSpan.FromSeconds(2.0);

	// Watcher registration state.
	private readonly string _workspaceRootDirectoryPath;
	private readonly ReadOnlyCollection<FileWatchSpecification> _watchSpecifications;
	private readonly FileSystemWatcherSet _nativeWatchers;
	private readonly Action<FileWatcher, Exception?>? _onWatcherFailed;

	private int _watcherFailureReported;

	// Dispatch and disposal lifecycle state. Disposal marks the watcher disposed under the notification lock and
	// then stops it without waiting: the lifetime token is canceled so an in-flight dispatch can observe disposal.
	private readonly FileChangeDebouncer _changeDebouncer;
	private readonly FileChangeDispatcher _dispatcher;
	private readonly CancellationTokenSource _lifetimeCts = new();

	// Serializes disposal against the failure notification so the owner callback cannot run after disposal returned.
	private readonly object _notificationSyncRoot = new();

	private volatile bool _isDisposed;

	// 0 = not disposing, 1 = disposing.
	private int _disposeStarted;

	/// <param name="workspaceRootDirectoryPath">The workspace root directory to watch.</param>
	/// <param name="dispatchAsync">The callback that forwards coalesced changes to the owner.</param>
	/// <param name="watchSpecifications">
	/// The explicit file patterns that should be watched under the workspace root. The list is copied on assignment.
	/// </param>
	/// <param name="onWatcherFailed">
	/// The callback that reports a watcher failure to the owner. It runs under the watcher's notification lock, so it returns promptly and leaves
	/// long work to a background task.
	/// </param>
	/// <param name="pathComparison">
	/// The comparison that decides whether file-name casing matters, or <see langword="null"/> for the current platform's.
	/// </param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="workspaceRootDirectoryPath"/>, <paramref name="dispatchAsync"/> or <paramref name="watchSpecifications"/>
	/// is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="watchSpecifications"/> contains no entries,
	/// or one of its filters is <see langword="null"/>, empty, whitespace-only, <c>.</c>, <c>..</c>, rooted, or contains a directory separator.
	/// </exception>
	public FileWatcher(
		string workspaceRootDirectoryPath,
		Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
		IReadOnlyList<FileWatchSpecification> watchSpecifications,
		Action<FileWatcher, Exception?>? onWatcherFailed = null,
		LocalPathComparisonPolicy? pathComparison = null,
		ILogger? logger = null
	)
		: this(workspaceRootDirectoryPath, dispatchAsync, watchSpecifications, createFileSystemWatcher: null, onWatcherFailed, pathComparison, logger)
	{ }

	/// <param name="workspaceRootDirectoryPath">The workspace root directory to watch.</param>
	/// <param name="dispatchAsync">The callback that forwards coalesced changes to the owner.</param>
	/// <param name="watchSpecifications">The explicit file patterns that should be watched under the workspace root.</param>
	/// <param name="createFileSystemWatcher">Creates the file-system watcher of one specification, so a test can force the creation to fail.</param>
	internal FileWatcher(
		string workspaceRootDirectoryPath,
		Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
		IReadOnlyList<FileWatchSpecification> watchSpecifications,
		Func<string, FileWatchSpecification, FileSystemWatcher> createFileSystemWatcher
	)
		: this(
			workspaceRootDirectoryPath,
			dispatchAsync,
			watchSpecifications,
			createFileSystemWatcher,
			onWatcherFailed: null,
			pathComparison: null,
			logger: null
		) { }

	private FileWatcher(
		string workspaceRootDirectoryPath,
		Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
		IReadOnlyList<FileWatchSpecification> watchSpecifications,
		Func<string, FileWatchSpecification, FileSystemWatcher>? createFileSystemWatcher,
		Action<FileWatcher, Exception?>? onWatcherFailed,
		LocalPathComparisonPolicy? pathComparison,
		ILogger? logger
	)
	{
		ArgumentNullException.ThrowIfNull(workspaceRootDirectoryPath);
		ArgumentNullException.ThrowIfNull(dispatchAsync);
		ArgumentNullException.ThrowIfNull(watchSpecifications);

		if (watchSpecifications.Count == 0)
			throw new ArgumentException("At least one watch specification is required.", nameof(watchSpecifications));

		for (int i = 0; i < watchSpecifications.Count; i++)
			FileWatchSpecification.Validate(watchSpecifications[i], nameof(watchSpecifications));

		_logger = logger ?? NullLogger.Instance;

		_workspaceRootDirectoryPath = workspaceRootDirectoryPath;
		_watchSpecifications = Array.AsReadOnly([.. watchSpecifications]);
		_onWatcherFailed = onWatcherFailed;

		_nativeWatchers = new FileSystemWatcherSet(
			workspaceRootDirectoryPath,
			pathComparison: pathComparison ?? LocalPathComparisonPolicy.ForCurrentPlatform,
			QueueChange,
			handleError: HandleWatcherFailure,
			createFileSystemWatcher,
			logger: _logger
		);

		_changeDebouncer = new FileChangeDebouncer(
			debounceDelay: s_dispatchDebounce,
			maxDispatchDelay: s_maxDispatchDelay,
			dispatchPendingChanges: () => _ = DispatchPendingChangesAsync(),
			pathComparison: pathComparison
		);

		_dispatcher = new FileChangeDispatcher(
			debouncer: _changeDebouncer,
			dispatchAsync,
			isDisposed: () => _isDisposed,
			retryBaseDelay: s_dispatchDebounce,
			onRetriesExhausted: HandleDispatchRetriesExhausted,
			workspaceRootDirectoryPath,
			logger: _logger,
			lifetimeToken: _lifetimeCts.Token
		);
	}

	/// <summary>The internal test hooks at the start path's nondeterministic seams, or <see cref="FileWatcherTestHooks.None"/>.</summary>
	/// <remarks>
	/// Internal and reachable only through <c>InternalsVisibleTo</c>; production leaves the default, so every hook branch is skipped.
	/// </remarks>
	internal FileWatcherTestHooks TestHooks { get; init; } = FileWatcherTestHooks.None;

	/// <summary>The number of active file-system watchers.</summary>
	/// <remarks>
	/// Internal diagnostic view; not part of the supported surface.
	/// </remarks>
	internal int ActiveWatcherCount => _nativeWatchers.Count;

	/// <remarks>
	/// A disposed watcher stops watching the workspace and cannot be restarted.
	/// </remarks>
	internal bool IsDisposed => _isDisposed;

	/// <summary>Starts watching the configured workspace for external file changes and reports the startup result.</summary>
	/// <returns>The watcher startup result, carrying the outcome and the startup exception when a start failed.</returns>
	/// <remarks>
	/// Only a <see cref="FileWatcherStartOutcome.StartupFailed"/> result disposes this watcher instance, so later retries should use
	/// a replacement watcher; a <see cref="FileWatcherStartOutcome.WorkspaceRootMissing"/> result leaves the instance startable once the root exists.
	/// </remarks>
	public FileWatcherStartResult Start()
	{
		if (_isDisposed)
			return new(FileWatcherStartOutcome.Disposed, null);

		try
		{
			if (!Directory.Exists(_workspaceRootDirectoryPath))
			{
				_logger.LogDebug("Workspace file watcher start skipped because '{Workspace}' does not exist.", _workspaceRootDirectoryPath);
				return new(FileWatcherStartOutcome.WorkspaceRootMissing, null);
			}
		}
		catch (Exception exception)
		{
			_logger.LogDebug(
				exception,
				"Failed to validate the workspace root for '{Workspace}' before starting the workspace watcher.",
				_workspaceRootDirectoryPath
			);

			Dispose();

			return new(FileWatcherStartOutcome.StartupFailed, exception);
		}

		try
		{
			FileSystemWatcherActivationOutcome activation = _nativeWatchers.Activate(
				specifications: _watchSpecifications,
				isDisposed: () => _isDisposed,
				onRegistered: TestHooks.WatcherActivation,
				onActivated: ResetFailureTracking
			);

			switch (activation)
			{
				case FileSystemWatcherActivationOutcome.Disposed:
					return new(FileWatcherStartOutcome.Disposed, null);

				case FileSystemWatcherActivationOutcome.AlreadyRunning:
					return new(FileWatcherStartOutcome.AlreadyRunning, null);

				case FileSystemWatcherActivationOutcome.FailedDuringActivation:
					Dispose();
					return new(FileWatcherStartOutcome.StartupFailed, null);

				default:
					return new(FileWatcherStartOutcome.Started, null);
			}
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Failed to start the workspace file watcher for '{Workspace}'.", _workspaceRootDirectoryPath);

			Dispose();

			return new(FileWatcherStartOutcome.StartupFailed, exception);
		}
	}

	/// <summary>Stops watching and releases the watcher without waiting for in-flight dispatch operations.</summary>
	/// <remarks>
	/// Disposal is idempotent and non-blocking: changes that are still buffered or scheduled for a retry are dropped, and the dispatch cancellation
	/// token is canceled so in-flight callbacks can observe disposal. The lifetime token source and the dispatch gate are deliberately not disposed:
	/// an in-flight dispatch may still be waiting on the gate or passing the token to its callback, and disposal must not fault
	/// it with an <see cref="ObjectDisposedException"/>. These objects become garbage together with the watcher once the in-flight
	/// operation releases them. See the class remarks for the shared disposal contract.
	/// </remarks>
	public void Dispose()
	{
		if (!BeginDispose())
			return;

		CancelLifetimeToken();
		_nativeWatchers.StopAll();
		_changeDebouncer.Stop();
		_changeDebouncer.Dispose();
	}

	/// <summary>Stops watching and releases the watcher without waiting for in-flight dispatch operations.</summary>
	/// <remarks>
	/// See the class remarks for the shared disposal contract.
	/// </remarks>
	public ValueTask DisposeAsync()
	{
		Dispose();
		return ValueTask.CompletedTask;
	}

	/// <summary>Queues a single file change for the next debounced dispatch.</summary>
	/// <param name="filePath">The changed file path.</param>
	/// <param name="kind">The change kind.</param>
	internal void QueueChange(string filePath, FileChangeKind kind)
	{
		if (_isDisposed)
			return;

		if (!LocalPath.TryNormalize(filePath, out string normalizedFilePath))
		{
			_logger.LogDebug("Workspace file watcher ignored a change for '{FilePath}' because the path could not be normalized.", filePath);
			return;
		}

		_changeDebouncer.Queue(normalizedFilePath, kind);
	}

	/// <summary>Dispatches any currently pending changes through the watcher's dispatcher.</summary>
	internal Task DispatchPendingChangesAsync() => _dispatcher.DispatchPendingAsync();

	/// <summary>Stops all active watchers and reports the failure to the owner once.</summary>
	/// <param name="exception">The watcher error, if one was provided.</param>
	/// <param name="watcher">
	/// The file-system watcher that raised the error, or <see langword="null"/> when the caller does not know it. An error raised by a watcher
	/// that is not registered is ignored because it belongs to a watcher set that has already been superseded or stopped.
	/// </param>
	internal void HandleWatcherFailure(Exception? exception, FileSystemWatcher? watcher = null)
	{
		if (_isDisposed)
			return;

		if (watcher is not null && !_nativeWatchers.Contains(watcher))
			return;

		_nativeWatchers.StopAll();

		if (_isDisposed)
			return;

		if (Interlocked.Exchange(ref _watcherFailureReported, 1) != 0)
			return;

		_logger.LogWarning(
			exception,
			"Workspace file watcher encountered an internal error for '{Workspace}' and stopped watching until the owner handles recovery.",
			_workspaceRootDirectoryPath
		);

		RaiseWatcherFailure(exception);
	}

	/// <summary>Clears the failure latch and the dispatch failure count once a start succeeded, under the set's lock.</summary>
	/// <remarks>
	/// A failure that the new watchers raise is handled only after the activation released that lock, so it cannot be cleared by this reset.
	/// </remarks>
	private void ResetFailureTracking()
	{
		Interlocked.Exchange(ref _watcherFailureReported, 0);
		_dispatcher.ResetFailureCount();

		_logger.LogDebug(
			"Started workspace file watcher for '{Workspace}' with {Count} watcher(s).",
			_workspaceRootDirectoryPath,
			_nativeWatchers.Count
		);
	}

	/// <summary>Stops watching and notifies the owner once, after a batch used up its dispatch attempts.</summary>
	/// <param name="exception">The last dispatch failure.</param>
	/// <param name="consecutiveDispatchFailures">The number of consecutive dispatch failures.</param>
	/// <param name="droppedChangeCount">The number of pending changes that were dropped.</param>
	private void HandleDispatchRetriesExhausted(Exception exception, int consecutiveDispatchFailures, int droppedChangeCount)
	{
		// Delivery does not wait on an owner reaction: the batch is dropped and the watchers stop; the
		// owner callback fires only once per failure sequence because a watcher error that runs concurrently
		// may already have reported the same sequence.
		_nativeWatchers.StopAll();

		if (Interlocked.Exchange(ref _watcherFailureReported, 1) == 0)
		{
			_logger.LogWarning(
				exception,
				"Workspace file watcher dispatch failed for '{Workspace}' {FailureCount} times in a row; the {Count} pending change(s) were dropped and the owner is notified so the watcher can be replaced.",
				_workspaceRootDirectoryPath,
				consecutiveDispatchFailures,
				droppedChangeCount
			);

			RaiseWatcherFailure(exception);
		}
		else
		{
			_logger.LogWarning(
				exception,
				"Workspace file watcher dispatch failed for '{Workspace}' {FailureCount} times in a row; the {Count} pending change(s) were dropped and the owner was already notified for this watcher, so no new notification is raised.",
				_workspaceRootDirectoryPath,
				consecutiveDispatchFailures,
				droppedChangeCount
			);
		}
	}

	/// <summary>Raises the watcher failure notification and dispatches any pending batch afterwards.</summary>
	/// <param name="exception">The watcher error, if one was provided.</param>
	/// <remarks>
	/// The notification runs under the notification lock, so disposal that starts afterwards waits for a running callback and no callback can start
	/// once disposal returned; the owner may dispose this watcher from inside the callback because disposal never waits for dispatch operations.
	/// The callback therefore must not block on lengthy work such as a full workspace enumeration: an owner that needs one offloads
	/// it to a background task so a concurrent disposal is not held behind the notification lock.
	/// </remarks>
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
		if (!_isDisposed && !_changeDebouncer.IsEmpty)
			_ = DispatchPendingChangesAsync();
	}

	/// <summary>Marks the watcher disposed once, serialized against the failure notification.</summary>
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

	/// <summary>Cancels the lifetime token so in-flight dispatch operations can observe disposal.</summary>
	private void CancelLifetimeToken()
	{
		try
		{
			_lifetimeCts.Cancel();
		}
		catch (ObjectDisposedException) { }
		catch (Exception exception)
		{
			// CancellationTokenSource.Cancel wraps throwing cancellation callbacks in an AggregateException;
			// cancellation failures must not keep disposal from completing.
			_logger.LogDebug(
				exception,
				"Workspace file watcher lifetime cancellation for '{Workspace}' reported one or more callback failures.",
				_workspaceRootDirectoryPath
			);
		}
	}
}
