namespace Nickelony.IDEKit.FileWatching;

/// <summary>Owns the file-watching lifecycle for one workspace root.</summary>
/// <remarks>
/// The owner creates one supervisor per workspace root and routes
/// forwarded or replayed changes to the owning supervisor. A watcher failure is contained to its own root: the supervisor restarts its watcher,
/// reconciles the changes missed during the outage, and reports an unrecoverable failure only for its own root. The rationale lives
/// in the <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/FileWatchingGuide.md">file-watching guide</see>.
/// </remarks>
public sealed class FileWatchSupervisor : IDisposable
{
	private readonly ILogger _logger;

	private enum WorkspaceFileWatcherRecoveryOutcome
	{
		Recovered,
		Unavailable,
		Failed,
	}

	private readonly string _displayName;
	private readonly FileWatcherFactory _workspaceFileWatcherFactory;
	private readonly Func<FileWatchSupervisor, FileChangeBatch, CancellationToken, Task> _dispatchAsync;
	private readonly Action<Task, string> _observeBackgroundTask;

	private readonly Func<bool> _isEnabled;
	private readonly Func<bool> _isDisposed;

	private readonly Action<FileWatchFailure> _reportFailure;

	// The tracker is created on first use, not in the constructor: the owner may construct the supervisor
	// from its own constructor, and the tracker factory can be a supplied delegate, so nothing reads the tracker
	// during construction and no supplied code runs before the owner is fully constructed.
	private readonly Lazy<IFileSnapshotTracker> _workspaceSnapshotTracker;

	private readonly object _watcherSyncRoot = new();

	private IFileWatcher? _workspaceFileWatcher;
	private int _workspaceFileWatchFailureReported;

	/// <summary>Tracks whether a watcher start attempt is in progress, guarded by <see cref="_watcherSyncRoot"/>.</summary>
	/// <remarks>
	/// The watcher can report a failure synchronously from <c>Start</c> (for example while it is activating). The in-progress start frame observes
	/// the start outcome itself, so <see cref="HandleWatcherFailure"/> absorbs such a callback instead of re-entering recovery, which would start
	/// a second watcher the start frame cannot track.
	/// </remarks>
	private bool _isStartingWorkspaceFileWatcher;

	/// <param name="specification">The root, the display name, the watch patterns and the watcher and tracker factories.</param>
	/// <param name="hooks">
	/// The owner callbacks: the enabled and disposed probes, the failure report, the dispatch and the background-task observer.
	/// </param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="specification"/> or <paramref name="hooks"/>, a member of the specification or a delegate of the hooks
	/// is <see langword="null"/>.
	/// </exception>
	public FileWatchSupervisor(FileWatchSupervisorSpecification specification, FileWatchSupervisorHooks hooks, ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(specification);
		ArgumentNullException.ThrowIfNull(hooks);
		ArgumentNullException.ThrowIfNull(specification.WorkspaceRootDirectoryPath);
		ArgumentNullException.ThrowIfNull(specification.DisplayName);
		ArgumentNullException.ThrowIfNull(specification.WatchSpecifications);
		ArgumentNullException.ThrowIfNull(specification.FileWatcherFactory);
		ArgumentNullException.ThrowIfNull(specification.FileSnapshotTrackerFactory);
		ArgumentNullException.ThrowIfNull(hooks.IsEnabled);
		ArgumentNullException.ThrowIfNull(hooks.IsDisposed);
		ArgumentNullException.ThrowIfNull(hooks.ReportFailure);
		ArgumentNullException.ThrowIfNull(hooks.DispatchAsync);
		ArgumentNullException.ThrowIfNull(hooks.ObserveBackgroundTask);

		_logger = logger ?? NullLogger.Instance;
		_displayName = specification.DisplayName;
		WorkspaceRootDirectoryPath = specification.WorkspaceRootDirectoryPath;
		_workspaceFileWatcherFactory = specification.FileWatcherFactory;
		_isEnabled = hooks.IsEnabled;
		_isDisposed = hooks.IsDisposed;
		_reportFailure = hooks.ReportFailure;
		_dispatchAsync = hooks.DispatchAsync;
		_observeBackgroundTask = hooks.ObserveBackgroundTask;

		_workspaceSnapshotTracker = new Lazy<IFileSnapshotTracker>(() =>
			specification.FileSnapshotTrackerFactory(specification.WorkspaceRootDirectoryPath, specification.WatchSpecifications)
		);
	}

	/// <summary>The normalized workspace root directory this supervisor owns.</summary>
	public string WorkspaceRootDirectoryPath { get; }

	/// <remarks>
	/// The owner uses this to keep a nested root covered: while this supervisor's watcher is inactive (not started yet, or being recovered),
	/// the delivering outer watcher forwards and tracks the root's changes instead of filtering them away.
	/// </remarks>
	public bool IsWatcherActive
	{
		get
		{
			lock (_watcherSyncRoot)
				return _workspaceFileWatcher is not null;
		}
	}

	/// <summary>Starts this root's file watcher when the owner enables watching and captures the recovery baseline snapshot.</summary>
	/// <remarks>
	/// The initial snapshot capture (a full workspace enumeration with file fingerprints)
	/// is awaited by the caller, so the recovery baseline is in place before the first change can be tracked. It runs on a thread-pool thread
	/// and outside the supervisor lock, so disposal, failure handling, and the owner's watcher-activity checks stay responsive while it runs.
	/// </remarks>
	public async Task EnsureWatcherStartedAsync()
	{
		IFileWatcher? watcherToDispose = null;
		Exception? startupException = null;

		bool shouldReportFailure = false;
		bool captureTrackedSnapshot = false;

		lock (_watcherSyncRoot)
		{
			if (_workspaceFileWatcher is not null || !_isEnabled() || string.IsNullOrEmpty(WorkspaceRootDirectoryPath) || _isDisposed())
				return;

			// Absorb a failure callback the start raises synchronously: this frame observes the start outcome
			// itself, so a nested recovery would start a second watcher this frame cannot track.
			_isStartingWorkspaceFileWatcher = true;

			try
			{
				bool watcherStarted = TryStartFileWatcher(out IFileWatcher? watcher, out FileWatcherStartResult startResult);

				if (!watcherStarted)
				{
					watcherToDispose = watcher;
					shouldReportFailure = startResult.Outcome == FileWatcherStartOutcome.StartupFailed;
					startupException = startResult.StartupException;
				}
				else
				{
					_workspaceFileWatcher = watcher;
					captureTrackedSnapshot = true;
					Interlocked.Exchange(ref _workspaceFileWatchFailureReported, 0);
				}
			}
			finally
			{
				_isStartingWorkspaceFileWatcher = false;
			}
		}

		// The capture runs outside the supervisor lock so disposal, failure handling, and the owner's
		// watcher-activity checks stay responsive, and on a background thread because it enumerates the whole
		// workspace and fingerprints every watched file.
		if (captureTrackedSnapshot)
			await Task.Run(_workspaceSnapshotTracker.Value.CaptureTrackedSnapshot).ConfigureAwait(false);

		DisposeWatcher(watcherToDispose, $"Failed to dispose an unstarted {_displayName} workspace file watcher.");

		if (shouldReportFailure)
			ReportFileWatchStartFailure(startupException);
	}

	/// <summary>
	/// Handles a failure reported by this root's watcher: replaces the watcher, reconciles any missed tracked changes, and reports an unrecoverable
	/// failure for this root when recovery cannot continue.
	/// </summary>
	/// <param name="watcher">The failed watcher.</param>
	/// <param name="exception">The watcher error, if one was provided.</param>
	public void HandleWatcherFailure(IFileWatcher watcher, Exception? exception)
	{
		ArgumentNullException.ThrowIfNull(watcher);

		if (_isDisposed())
			return;

		// A failure callback from a watcher that was already replaced is stale: recovering or logging a restart
		// for it would describe work that is not happening. A callback that arrives while no watcher is active
		// still runs recovery, because that state can be a failed replacement attempt.
		IFileWatcher? activeWatcher;

		lock (_watcherSyncRoot)
		{
			// A failure raised synchronously while a watcher is being started is accounted for by that start
			// attempt (which observes the start outcome), not by a nested recovery that would start a second
			// watcher the start frame cannot track.
			if (_isStartingWorkspaceFileWatcher)
			{
				_logger.LogDebug(
					"Absorbed a {DisplayName} workspace-watcher failure for '{Workspace}' raised while a watcher start was in progress.",
					_displayName,
					WorkspaceRootDirectoryPath
				);

				return;
			}

			activeWatcher = _workspaceFileWatcher;
		}

		if (activeWatcher is not null && !ReferenceEquals(activeWatcher, watcher))
		{
			_logger.LogDebug(
				"Ignored a stale {DisplayName} workspace-watcher failure callback for '{Workspace}' because its watcher was already replaced.",
				_displayName,
				WorkspaceRootDirectoryPath
			);

			return;
		}

		_logger.LogWarning(
			exception,
			"{DisplayName} workspace watching failed for '{Workspace}'. Attempting to restart the watcher automatically and replay any missed tracked changes.",
			_displayName,
			WorkspaceRootDirectoryPath
		);

		WorkspaceFileWatcherRecoveryOutcome recoveryOutcome = RecoverWorkspaceFileWatcher(watcher);

		if (recoveryOutcome is WorkspaceFileWatcherRecoveryOutcome.Recovered or WorkspaceFileWatcherRecoveryOutcome.Unavailable)
			return;

		if (Interlocked.Exchange(ref _workspaceFileWatchFailureReported, 1) != 0)
			return;

		_reportFailure(new FileWatchFailure(FileWatchFailureCodes.RecoveryFailed, WorkspaceRootDirectoryPath, exception));
	}

	/// <summary>Applies already forwarded file changes to this root's tracked snapshot.</summary>
	/// <param name="changes">The normalized forwarded file changes owned by this supervisor.</param>
	public void ApplyTrackedChanges(IReadOnlyList<FileChange> changes)
	{
		ArgumentNullException.ThrowIfNull(changes);
		_workspaceSnapshotTracker.Value.ApplyChanges(changes);
	}

	/// <summary>Captures this root again and dispatches the difference between the tracked snapshot and the files on disk.</summary>
	/// <returns>A task that completes when the difference was dispatched.</returns>
	/// <remarks>
	/// The tracked snapshot holds the changes the owner applied after forwarding them, so the difference is what the receiver missed,
	/// as after a watcher recovery. An owner calls it when it had to drop buffered changes of this root. A supervisor whose watcher
	/// is not running does nothing, because its recovery or its next start captures the root itself.
	/// </remarks>
	public Task RescanAsync()
	{
		Dictionary<string, FileSnapshotEntry> previousSnapshot;

		lock (_watcherSyncRoot)
		{
			if (_workspaceFileWatcher is null || _isDisposed())
				return Task.CompletedTask;

			previousSnapshot = _workspaceSnapshotTracker.Value.CloneTrackedSnapshot();
		}

		return RecoverTrackedSnapshotAndReconcileAsync(previousSnapshot);
	}

	/// <summary>Disposes this root's active watcher.</summary>
	public void Dispose()
	{
		IFileWatcher? watcher;

		lock (_watcherSyncRoot)
		{
			watcher = _workspaceFileWatcher;
			_workspaceFileWatcher = null;
		}

		DisposeWatcher(watcher, $"Failed to dispose the {_displayName} workspace file watcher.");
	}

	/// <summary>Attempts to replace a failed watcher, reconcile any missed tracked changes, and classify the recovery outcome.</summary>
	/// <param name="failedWatcher">The watcher that reported the failure.</param>
	private WorkspaceFileWatcherRecoveryOutcome RecoverWorkspaceFileWatcher(IFileWatcher failedWatcher)
	{
		bool watcherRecovered = false;
		bool replacementWatcherStarted = false;
		bool startupFailed = false;
		bool workspaceUnavailable = false;

		Exception? startupException = null;

		IFileWatcher? failedWatcherToDispose = failedWatcher;
		IFileWatcher? replacementWatcherToDispose = null;

		Dictionary<string, FileSnapshotEntry>? previousSnapshot = null;

		// Decide under the lock whether recovery is needed, and capture the baseline snapshot required for
		// reconciliation; the fresh capture that follows runs outside the lock (see below).
		lock (_watcherSyncRoot)
		{
			if (!ReferenceEquals(_workspaceFileWatcher, failedWatcher))
			{
				watcherRecovered = _workspaceFileWatcher is not null;
			}
			else
			{
				previousSnapshot = _workspaceSnapshotTracker.Value.CloneTrackedSnapshot();
				_workspaceFileWatcher = null;
			}

			if (!watcherRecovered && !_isDisposed() && _isEnabled() && !string.IsNullOrEmpty(WorkspaceRootDirectoryPath))
			{
				// Absorb a failure callback the replacement start raises synchronously: this frame classifies the
				// failed start, and a nested recovery would start a watcher chain this frame cannot track.
				_isStartingWorkspaceFileWatcher = true;

				try
				{
					bool replacementStarted = TryStartFileWatcher(out IFileWatcher? replacementWatcher, out FileWatcherStartResult startResult);

					if (replacementStarted)
					{
						_workspaceFileWatcher = replacementWatcher;
						watcherRecovered = true;
						replacementWatcherStarted = true;
						Interlocked.Exchange(ref _workspaceFileWatchFailureReported, 0);
					}
					else
					{
						startupFailed = startResult.Outcome == FileWatcherStartOutcome.StartupFailed;
						workspaceUnavailable = startResult.Outcome == FileWatcherStartOutcome.WorkspaceRootMissing;
						startupException = startResult.StartupException;
						replacementWatcherToDispose = replacementWatcher;
					}
				}
				finally
				{
					_isStartingWorkspaceFileWatcher = false;
				}
			}
		}

		// Dispose watcher instances outside the lock so recovery bookkeeping stays responsive.
		DisposeWatcher(failedWatcherToDispose, $"Failed to dispose a {_displayName} workspace file watcher while recovering from a watcher error.");

		DisposeWatcher(
			replacementWatcherToDispose,
			$"Failed to dispose a {_displayName} workspace file watcher while recovering from a watcher error."
		);

		if (watcherRecovered)
		{
			_logger.LogInformation(
				"{DisplayName} workspace watching recovered successfully for '{Workspace}'.",
				_displayName,
				WorkspaceRootDirectoryPath
			);

			// Reconcile any tracked changes that may have happened while the watcher was unavailable. The fresh
			// capture and the replay run on a background thread (see RecoverTrackedSnapshotAndReconcileAsync), so
			// the watcher-failure callback returns promptly instead of walking the whole workspace on the
			// notification thread that also serializes disposal.
			if (replacementWatcherStarted && previousSnapshot is not null)
			{
				_observeBackgroundTask(
					RecoverTrackedSnapshotAndReconcileAsync(previousSnapshot),
					$"{_displayName} workspace watcher recovery reconciliation"
				);
			}

			return WorkspaceFileWatcherRecoveryOutcome.Recovered;
		}

		if (workspaceUnavailable)
		{
			_logger.LogInformation(
				"{DisplayName} workspace watching remains unavailable for '{Workspace}' because the workspace path does not exist.",
				_displayName,
				WorkspaceRootDirectoryPath
			);

			return WorkspaceFileWatcherRecoveryOutcome.Unavailable;
		}

		// Log startup failures separately so the caller can surface the right watcher-failure message.
		if (startupFailed)
		{
			_logger.LogWarning(
				startupException,
				"{DisplayName} workspace watching could not be restarted for '{Workspace}' because watcher startup failed.",
				_displayName,
				WorkspaceRootDirectoryPath
			);
		}

		return WorkspaceFileWatcherRecoveryOutcome.Failed;
	}

	/// <summary>
	/// Reports a watcher startup failure once until a watcher starts again: logs the warning and raises the workspace-watcher-failure report
	/// when none was raised yet.
	/// </summary>
	/// <param name="exception">The startup exception reported by the watcher, if any.</param>
	private void ReportFileWatchStartFailure(Exception? exception)
	{
		if (Interlocked.Exchange(ref _workspaceFileWatchFailureReported, 1) != 0)
			return;

		_logger.LogWarning(
			exception,
			"{DisplayName} workspace watching could not start for '{Workspace}'. External workspace changes will not be forwarded until the watcher can be started successfully.",
			_displayName,
			WorkspaceRootDirectoryPath
		);

		_reportFailure(new FileWatchFailure(FileWatchFailureCodes.StartFailed, WorkspaceRootDirectoryPath, exception));
	}

	/// <summary>
	/// Captures the current workspace state on a background thread and replays its delta against an earlier tracked snapshot, after a watcher
	/// recovery or a rescan.
	/// </summary>
	/// <param name="previousSnapshot">The tracked snapshot captured before the watcher was replaced or the rescan began.</param>
	/// <remarks>
	/// The capture enumerates the whole workspace and fingerprints every watched file, so it must not run
	/// on the watcher's failure-notification thread, which also serializes disposal. It runs on a thread-pool thread, mirroring the initial
	/// capture in <see cref="EnsureWatcherStartedAsync"/>. An owner that was disposed while the recovery was scheduled skips the capture,
	/// because the tracked snapshot has no readers then.
	/// </remarks>
	private async Task RecoverTrackedSnapshotAndReconcileAsync(Dictionary<string, FileSnapshotEntry> previousSnapshot)
	{
		if (_isDisposed())
			return;

		Dictionary<string, FileSnapshotEntry> currentSnapshot = await Task.Run(_workspaceSnapshotTracker.Value.ReplaceTrackedSnapshotWithCurrent)
			.ConfigureAwait(false);

		await ReconcileWorkspaceSnapshotAsync(previousSnapshot, currentSnapshot).ConfigureAwait(false);
	}

	/// <summary>Replays the tracked workspace delta between an earlier tracked snapshot and a fresh capture.</summary>
	/// <param name="previousSnapshot">The tracked snapshot captured before the watcher was replaced or the rescan began.</param>
	/// <param name="currentSnapshot">The fresh snapshot captured afterwards.</param>
	private async Task ReconcileWorkspaceSnapshotAsync(
		Dictionary<string, FileSnapshotEntry> previousSnapshot,
		Dictionary<string, FileSnapshotEntry> currentSnapshot
	)
	{
		if (_isDisposed())
			return;

		FileChangeBatch batch = _workspaceSnapshotTracker.Value.BuildDeltaBatch(previousSnapshot, currentSnapshot);

		if (batch.Count == 0)
			return;

		_logger.LogInformation(
			"Replaying {Count} reconciled {DisplayName} workspace change(s) for '{Workspace}'.",
			batch.Count,
			_displayName,
			WorkspaceRootDirectoryPath
		);

		await _dispatchAsync(this, batch, CancellationToken.None).ConfigureAwait(false);
	}

	/// <summary>
	/// Attempts to create and start a workspace file watcher, classifying a throwing factory or start call as a startup failure instead of letting
	/// it escape.
	/// </summary>
	/// <param name="watcher">Receives the created watcher, when any.</param>
	/// <param name="startResult">Receives the watcher start result, carrying the outcome and any startup exception.</param>
	/// <returns><see langword="true"/> when the watcher started; otherwise, <see langword="false"/>.</returns>
	private bool TryStartFileWatcher(out IFileWatcher? watcher, out FileWatcherStartResult startResult)
	{
		watcher = null;

		try
		{
			watcher = _workspaceFileWatcherFactory(
				WorkspaceRootDirectoryPath,
				(batch, cancellationToken) => _dispatchAsync(this, batch, cancellationToken),
				HandleWatcherFailure
			);

			ArgumentNullException.ThrowIfNull(watcher);

			startResult = watcher.Start();
		}
		catch (Exception exception)
		{
			// A throwing watcher factory must not escape into request APIs; classify it as a startup failure so it
			// surfaces through the watcher-failure report instead.
			startResult = new(FileWatcherStartOutcome.StartupFailed, exception);
		}

		return startResult.Succeeded;
	}

	private void DisposeWatcher(IFileWatcher? watcher, string message)
	{
		if (watcher is null)
			return;

		try
		{
			watcher.Dispose();
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "{Message}", message);
		}
	}
}
