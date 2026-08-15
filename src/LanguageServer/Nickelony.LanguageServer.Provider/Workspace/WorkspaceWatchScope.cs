namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Owns the workspace-watching lifecycle for one workspace root: the root's file watcher, the tracked snapshot
/// used for recovery reconciliation, the watcher recovery state, and the unrecoverable-failure latch.
/// </summary>
/// <remarks>
/// The coordinator creates one scope per workspace root and routes forwarded or replayed changes to the owning scope.
/// A watcher failure is contained to its own root: the scope restarts its watcher, reconciles the changes missed
/// during the outage, and reports an unrecoverable failure only for its own root. The rationale lives in
/// <c>docs/LanguageServerInternals.md</c>.
/// </remarks>
internal sealed class WorkspaceWatchScope : IDisposable
{
	private readonly ILogger _logger;

	private enum WorkspaceWatcherRecoveryOutcome
	{
		Recovered,
		Unavailable,
		Failed
	}

	private readonly Func<string> _providerDisplayNameAccessor;
	private readonly string _workspaceRootDirectoryPath;
	private readonly WorkspaceFileWatcherFactory _workspaceFileWatcherFactory;
	private readonly Func<WorkspaceWatchScope, FileChangeBatch, CancellationToken, Task> _dispatchAsync;
	private readonly Func<ILanguageServerClient?> _clientAccessor;
	private readonly Func<bool> _isDisposedAccessor;
	private readonly Action<WorkspaceWatcherFailure> _raiseWorkspaceWatcherFailed;
	private readonly WorkspaceSnapshotTracker _workspaceSnapshotTracker;

	private readonly object _watcherSyncRoot = new();

	private IWorkspaceFileWatcher? _workspaceFileWatcher;
	private int _workspaceWatcherFailureReported;

	/// <summary>
	/// Tracks whether a watcher start attempt is in progress, guarded by <see cref="_watcherSyncRoot"/>.
	/// </summary>
	/// <remarks>
	/// The watcher can report a failure synchronously from <c>Start</c> (for example while it is activating). The
	/// in-progress start frame observes the start outcome itself, so <see cref="HandleWatcherFailed"/> absorbs such a
	/// callback instead of re-entering recovery, which would start a second watcher the start frame cannot track.
	/// </remarks>
	private bool _isStartingWorkspaceWatcher;

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceWatchScope"/> class.
	/// </summary>
	/// <param name="context">The scope context describing the root, watcher factory, dispatch callback, and log identity.</param>
	/// <param name="hooks">The owner hooks used for client access, disposal probing, and failure reporting.</param>
	internal WorkspaceWatchScope(WorkspaceWatchScopeContext context, WorkspaceChangeHooks hooks)
	{
		_logger = context.Logger;
		_providerDisplayNameAccessor = context.ProviderDisplayNameAccessor;
		_workspaceRootDirectoryPath = context.WorkspaceRootDirectoryPath;
		_workspaceFileWatcherFactory = context.WorkspaceFileWatcherFactory;
		_dispatchAsync = context.DispatchAsync;
		_clientAccessor = hooks.ClientAccessor;
		_isDisposedAccessor = hooks.IsDisposedAccessor;
		_raiseWorkspaceWatcherFailed = hooks.RaiseWorkspaceWatcherFailed;
		_workspaceSnapshotTracker = new WorkspaceSnapshotTracker(context.WorkspaceRootDirectoryPath, context.WatchSpecifications, context.Logger);
	}

	/// <summary>
	/// Gets the normalized workspace root directory this scope owns.
	/// </summary>
	internal string WorkspaceRootDirectoryPath => _workspaceRootDirectoryPath;

	/// <summary>
	/// Gets a value indicating whether this scope currently has a started workspace file watcher.
	/// </summary>
	/// <remarks>
	/// The coordinator uses this to keep a nested root covered: while this scope's watcher is inactive (not
	/// started yet, or being recovered), the delivering outer watcher forwards and tracks the root's changes
	/// instead of filtering them away.
	/// </remarks>
	internal bool IsWatcherActive
	{
		get
		{
			lock (_watcherSyncRoot)
				return _workspaceFileWatcher is not null;
		}
	}

	/// <summary>
	/// Starts this root's workspace watcher when the language-server client is available and captures the recovery
	/// baseline snapshot.
	/// </summary>
	/// <remarks>
	/// The initial snapshot capture (a full workspace enumeration with file fingerprints) is awaited by the caller,
	/// so the recovery baseline is in place before the first change can be tracked. It runs on a thread-pool thread
	/// and outside the scope lock, so it does not run on the caller's own thread and disposal, failure handling, and
	/// the coordinator's watcher-activity checks stay responsive while it runs.
	/// </remarks>
	internal async Task EnsureWatcherStartedAsync()
	{
		IWorkspaceFileWatcher? watcherToDispose = null;
		Exception? startupException = null;
		bool shouldReportFailure = false;
		bool captureTrackedSnapshot = false;

		lock (_watcherSyncRoot)
		{
			if (_workspaceFileWatcher is not null || _clientAccessor() is null || string.IsNullOrEmpty(_workspaceRootDirectoryPath) || _isDisposedAccessor())
				return;

			// Absorb a failure callback the start raises synchronously: this frame observes the start outcome
			// itself, so a nested recovery would start a second watcher this frame cannot track.
			_isStartingWorkspaceWatcher = true;

			try
			{
				bool watcherStarted = TryStartWorkspaceFileWatcher(out IWorkspaceFileWatcher? watcher, out WorkspaceWatcherStartResult startResult);

				if (!watcherStarted)
				{
					watcherToDispose = watcher;
					shouldReportFailure = startResult.Outcome == WorkspaceWatcherStartOutcome.StartupFailed;
					startupException = startResult.StartupException;
				}
				else
				{
					_workspaceFileWatcher = watcher;
					captureTrackedSnapshot = true;
					Interlocked.Exchange(ref _workspaceWatcherFailureReported, 0);
				}
			}
			finally
			{
				_isStartingWorkspaceWatcher = false;
			}
		}

		// The capture runs outside the scope lock so disposal, failure handling, and the coordinator's
		// watcher-activity checks stay responsive, and on a background thread because it enumerates the whole
		// workspace and fingerprints every watched file.
		if (captureTrackedSnapshot)
			await Task.Run(_workspaceSnapshotTracker.CaptureTrackedSnapshot).ConfigureAwait(false);

		DisposeWatcher(watcherToDispose, $"Failed to dispose an unstarted {_providerDisplayNameAccessor()} workspace file watcher.");

		if (shouldReportFailure)
			ReportWorkspaceWatcherStartupFailure(startupException);
	}

	/// <summary>
	/// Handles a failure reported by this root's watcher: replaces the watcher, reconciles any missed tracked
	/// changes, and reports an unrecoverable failure for this root when recovery cannot continue.
	/// </summary>
	/// <param name="watcher">The failed watcher.</param>
	/// <param name="exception">The watcher error, if one was provided.</param>
	internal void HandleWatcherFailed(IWorkspaceFileWatcher watcher, Exception? exception)
	{
		if (_isDisposedAccessor())
			return;

		// A failure callback from a watcher that was already replaced is stale: recovering or logging a restart
		// for it would describe work that is not happening. A callback that arrives while no watcher is active
		// still runs recovery, because that state can be a failed replacement attempt.
		IWorkspaceFileWatcher? activeWatcher;

		lock (_watcherSyncRoot)
		{
			// A failure raised synchronously while a watcher is being started is accounted for by that start
			// attempt (which observes the start outcome), not by a nested recovery that would start a second
			// watcher the start frame cannot track.
			if (_isStartingWorkspaceWatcher)
			{
				_logger.LogDebug("Absorbed a {DisplayName} workspace-watcher failure for '{Workspace}' raised while a watcher start was in progress.",
					_providerDisplayNameAccessor(),
					_workspaceRootDirectoryPath);

				return;
			}

			activeWatcher = _workspaceFileWatcher;
		}

		if (activeWatcher is not null && !ReferenceEquals(activeWatcher, watcher))
		{
			_logger.LogDebug("Ignored a stale {DisplayName} workspace-watcher failure callback for '{Workspace}' because its watcher was already replaced.",
				_providerDisplayNameAccessor(),
				_workspaceRootDirectoryPath);

			return;
		}

		_logger.LogWarning(exception,
			"{DisplayName} workspace watching failed for '{Workspace}'. Attempting to restart the watcher automatically and replay any missed tracked changes.",
			_providerDisplayNameAccessor(),
			_workspaceRootDirectoryPath);

		WorkspaceWatcherRecoveryOutcome recoveryResult = RecoverWorkspaceFileWatcher(watcher);

		if (recoveryResult is WorkspaceWatcherRecoveryOutcome.Recovered or WorkspaceWatcherRecoveryOutcome.Unavailable)
			return;

		if (Interlocked.Exchange(ref _workspaceWatcherFailureReported, 1) != 0)
			return;

		_raiseWorkspaceWatcherFailed(new WorkspaceWatcherFailure(
			CreateWatcherFailureMessage($"The {_providerDisplayNameAccessor()} workspace file watcher encountered an internal error and automatic recovery failed for the workspace root '{_workspaceRootDirectoryPath}'."),
			_workspaceRootDirectoryPath));
	}

	/// <summary>
	/// Applies already forwarded file changes to this root's tracked snapshot.
	/// </summary>
	/// <param name="changes">The normalized forwarded file changes owned by this scope.</param>
	internal void ApplyTrackedChanges(IReadOnlyList<WorkspaceFileChange> changes)
		=> _workspaceSnapshotTracker.ApplyChanges(changes);

	/// <summary>
	/// Disposes this root's active watcher.
	/// </summary>
	public void Dispose()
	{
		IWorkspaceFileWatcher? watcher;

		lock (_watcherSyncRoot)
		{
			watcher = _workspaceFileWatcher;
			_workspaceFileWatcher = null;
		}

		DisposeWatcher(watcher, $"Failed to dispose the {_providerDisplayNameAccessor()} workspace file watcher.");
	}

	/// <summary>
	/// Attempts to replace a failed watcher, reconcile any missed tracked changes, and classify the recovery outcome.
	/// </summary>
	/// <param name="failedWatcher">The watcher that reported the failure.</param>
	private WorkspaceWatcherRecoveryOutcome RecoverWorkspaceFileWatcher(IWorkspaceFileWatcher failedWatcher)
	{
		bool watcherRecovered = false;
		bool replacementWatcherStarted = false;
		bool startupFailed = false;
		bool workspaceUnavailable = false;
		Exception? startupException = null;
		IWorkspaceFileWatcher? failedWatcherToDispose = failedWatcher;
		IWorkspaceFileWatcher? replacementWatcherToDispose = null;
		Dictionary<string, WorkspaceSnapshotEntry>? previousSnapshot = null;
		Dictionary<string, WorkspaceSnapshotEntry>? currentSnapshot = null;

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
				previousSnapshot = _workspaceSnapshotTracker.CloneTrackedSnapshot();
				_workspaceFileWatcher = null;
			}

			if (!watcherRecovered && !_isDisposedAccessor() && _clientAccessor() is not null && !string.IsNullOrEmpty(_workspaceRootDirectoryPath))
			{
				// Absorb a failure callback the replacement start raises synchronously: this frame classifies the
				// failed start, and a nested recovery would start a watcher chain this frame cannot track.
				_isStartingWorkspaceWatcher = true;

				try
				{
					bool replacementStarted = TryStartWorkspaceFileWatcher(out IWorkspaceFileWatcher? replacementWatcher, out WorkspaceWatcherStartResult startResult);

					if (replacementStarted)
					{
						_workspaceFileWatcher = replacementWatcher;
						watcherRecovered = true;
						replacementWatcherStarted = true;
						Interlocked.Exchange(ref _workspaceWatcherFailureReported, 0);
					}
					else
					{
						startupFailed = startResult.Outcome == WorkspaceWatcherStartOutcome.StartupFailed;
						workspaceUnavailable = startResult.Outcome == WorkspaceWatcherStartOutcome.WorkspaceRootMissing;
						startupException = startResult.StartupException;
						replacementWatcherToDispose = replacementWatcher;
					}
				}
				finally
				{
					_isStartingWorkspaceWatcher = false;
				}
			}
		}

		// The fresh capture behind the reconciliation baseline runs outside the scope lock, like the initial
		// capture in EnsureStarted; a scope that was disposed while the watcher was replaced skips it, because the
		// tracked snapshot no longer has readers then.
		if (replacementWatcherStarted && !_isDisposedAccessor())
			currentSnapshot = _workspaceSnapshotTracker.ReplaceTrackedSnapshotWithCurrent();

		// Dispose watcher instances outside the lock so recovery bookkeeping stays responsive.
		DisposeWatcher(failedWatcherToDispose, $"Failed to dispose a {_providerDisplayNameAccessor()} workspace file watcher while recovering from a watcher error.");
		DisposeWatcher(replacementWatcherToDispose, $"Failed to dispose a {_providerDisplayNameAccessor()} workspace file watcher while recovering from a watcher error.");

		if (watcherRecovered)
		{
			_logger.LogInformation("{DisplayName} workspace watching recovered successfully for '{Workspace}'.",
				_providerDisplayNameAccessor(),
				_workspaceRootDirectoryPath);

			// Reconcile any tracked changes that may have happened while the watcher was unavailable.
			if (replacementWatcherStarted && previousSnapshot is not null && currentSnapshot is not null)
				BackgroundTaskObserver.Observe(_logger, ReconcileWorkspaceSnapshotAsync(previousSnapshot, currentSnapshot), $"{_providerDisplayNameAccessor()} workspace watcher recovery reconciliation");

			return WorkspaceWatcherRecoveryOutcome.Recovered;
		}

		if (workspaceUnavailable)
		{
			_logger.LogInformation(
				"{DisplayName} workspace watching remains unavailable for '{Workspace}' because the workspace path does not exist.",
				_providerDisplayNameAccessor(),
				_workspaceRootDirectoryPath);

			return WorkspaceWatcherRecoveryOutcome.Unavailable;
		}

		// Log startup failures separately so the caller can surface the right watcher-failure message.
		if (startupFailed)
		{
			_logger.LogWarning(startupException,
				"{DisplayName} workspace watching could not be restarted for '{Workspace}' because watcher startup failed.",
				_providerDisplayNameAccessor(),
				_workspaceRootDirectoryPath);
		}

		return WorkspaceWatcherRecoveryOutcome.Failed;
	}

	/// <summary>
	/// Reports a watcher startup failure at most once per scope: logs the warning and raises the
	/// workspace-watcher-failure report when none was raised yet.
	/// </summary>
	/// <param name="exception">The startup exception reported by the watcher, if any.</param>
	private void ReportWorkspaceWatcherStartupFailure(Exception? exception)
	{
		if (Interlocked.Exchange(ref _workspaceWatcherFailureReported, 1) != 0)
			return;

		_logger.LogWarning(exception,
			"{DisplayName} workspace watching could not start for '{Workspace}'. External workspace changes will not be forwarded until the watcher can be started successfully.",
			_providerDisplayNameAccessor(),
			_workspaceRootDirectoryPath);

		_raiseWorkspaceWatcherFailed(new WorkspaceWatcherFailure(
			CreateWatcherFailureMessage($"The {_providerDisplayNameAccessor()} workspace file watcher could not be started for the workspace root '{_workspaceRootDirectoryPath}'."),
			_workspaceRootDirectoryPath));
	}

	/// <summary>
	/// Replays the tracked workspace delta detected while this root's watcher was unavailable.
	/// </summary>
	/// <param name="previousSnapshot">The tracked snapshot captured before the watcher was replaced.</param>
	/// <param name="currentSnapshot">The fresh snapshot captured after the replacement watcher started.</param>
	private async Task ReconcileWorkspaceSnapshotAsync(
		Dictionary<string, WorkspaceSnapshotEntry> previousSnapshot,
		Dictionary<string, WorkspaceSnapshotEntry> currentSnapshot)
	{
		if (_isDisposedAccessor())
			return;

		FileChangeBatch batch = WorkspaceSnapshotTracker.BuildDeltaBatch(previousSnapshot, currentSnapshot);

		if (batch.Count == 0)
			return;

		_logger.LogInformation(
			"Replaying {Count} reconciled workspace file change(s) after {DisplayName} workspace watcher recovery for '{Workspace}'.",
			batch.Count,
			_providerDisplayNameAccessor(),
			_workspaceRootDirectoryPath);

		await _dispatchAsync(this, batch, CancellationToken.None).ConfigureAwait(false);
	}

	/// <summary>
	/// Builds one user-facing watcher-failure message with the shared host-neutral consequence sentence.
	/// </summary>
	/// <param name="firstSentence">The context sentence describing the failure and its workspace root.</param>
	/// <returns>The complete failure message.</returns>
	private string CreateWatcherFailureMessage(string firstSentence)
		=> $"{firstSentence}\n\n{_providerDisplayNameAccessor()} IntelliSense will continue to work for documents synchronized through this provider, but external workspace changes may not be forwarded until the watcher is available again.";

	/// <summary>
	/// Attempts to create and start a workspace file watcher, classifying a throwing factory as a startup failure
	/// instead of letting it escape.
	/// </summary>
	/// <param name="watcher">Receives the created watcher, when any.</param>
	/// <param name="startResult">Receives the watcher start result, carrying the outcome and any startup exception.</param>
	/// <returns><see langword="true"/> when the watcher started; otherwise, <see langword="false"/>.</returns>
	private bool TryStartWorkspaceFileWatcher(out IWorkspaceFileWatcher? watcher, out WorkspaceWatcherStartResult startResult)
	{
		watcher = null;

		try
		{
			watcher = _workspaceFileWatcherFactory(
				_workspaceRootDirectoryPath,
				(batch, cancellationToken) => _dispatchAsync(this, batch, cancellationToken),
				HandleWatcherFailed);

			ArgumentNullException.ThrowIfNull(watcher);

			startResult = watcher.Start();
		}
		catch (Exception exception)
		{
			// A throwing watcher factory must not escape into request APIs; classify it as a startup failure so it
			// surfaces through the watcher-failure report instead.
			startResult = new(WorkspaceWatcherStartOutcome.StartupFailed, exception);
		}

		return startResult.Started;
	}

	private void DisposeWatcher(IWorkspaceFileWatcher? watcher, string message)
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
