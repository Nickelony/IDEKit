using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Reloading;

/// <summary>
/// Reloads the tracked documents whose files changed on disk, one file and one pass at a time, prompting the host only for conflicts.
/// </summary>
/// <typeparam name="TDecision">
/// The host-neutral value that <see cref="FileReloadHooks{TDecision}.DecideReload"/>
/// produces and <see cref="FileReloadHooks{TDecision}.ResolveConflict"/> receives. The coordinator passes
/// it through without interpreting it, which is why the type parameter cannot be erased behind a non-generic facade.
/// </typeparam>
/// <remarks>
/// <para>
/// <see cref="QueueFiles"/> is safe on any thread, such as the dispatch of a file watcher, and <see cref="ProcessQueuedFilesAsync"/> starts
/// a pass on the caller's thread or joins the pass that runs. A pass drains every path queued before it ends, including a path
/// queued again while it was being reloaded, so a caller needs no loop of its own. A path the store does not track when its turn comes is dropped.
/// A hook runs on the thread that started the pass until one hook completes asynchronously, and the hooks after that run on the thread pool.
/// </para>
/// <para>
/// A watcher that awaits a pass inside its dispatch stalls while a conflict prompt is open, and counts a throwing hook as a failed dispatch,
/// so it queues the paths and starts the pass without awaiting it. A hook must not await the pass it runs in, because that pass waits for the hook.
/// </para>
/// </remarks>
public sealed class FileReloadCoordinator<TDecision>
{
	private readonly object _lock = new();
	private readonly FileReloadHooks<TDecision> _hooks;
	private readonly IWorkspaceDocumentReader _documents;
	private readonly Queue<string> _pendingFileReloads = new();
	private readonly HashSet<string> _queuedPaths;

	private TaskCompletionSource? _pass;

	/// <param name="documents">
	/// The store whose documents the hooks reload; its path comparison decides which queued paths are duplicates, and it tells which paths it tracks.
	/// </param>
	/// <param name="hooks">Reload a document, decide and resolve a conflict, and report a failed reload.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documents"/> or <paramref name="hooks"/> is <see langword="null"/>,
	/// or <see cref="FileReloadHooks{TDecision}.ResolveConflict"/> is supplied while <see cref="FileReloadHooks{TDecision}.DecideReload"/>
	/// is <see langword="null"/>.
	/// </exception>
	public FileReloadCoordinator(IWorkspaceDocumentReader documents, FileReloadHooks<TDecision> hooks)
	{
		ArgumentNullException.ThrowIfNull(documents);
		ArgumentNullException.ThrowIfNull(hooks);

		// The decision callback is only consumed by a resolver, so it is required only when one is supplied.
		if (hooks.ResolveConflict is not null)
			ArgumentNullException.ThrowIfNull(hooks.DecideReload);

		_hooks = hooks;
		_documents = documents;
		_queuedPaths = new HashSet<string>(documents.Options.PathComparison.Comparer);
	}

	/// <summary>Whether a pass runs.</summary>
	public bool IsRunning
	{
		get
		{
			lock (_lock)
				return _pass is not null;
		}
	}

	/// <summary>The number of paths waiting for a pass, including the path a running pass is reloading.</summary>
	public int PendingFileCount
	{
		get
		{
			lock (_lock)
				return _pendingFileReloads.Count;
		}
	}

	/// <summary>Queues files for a reload; blank paths and paths already queued under the store's path comparison are ignored.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="filePaths"/> or one of its paths is <see langword="null"/>.</exception>
	public void QueueFiles(IEnumerable<string> filePaths)
	{
		ArgumentNullException.ThrowIfNull(filePaths);

		lock (_lock)
		{
			foreach (string filePath in filePaths)
			{
				ArgumentNullException.ThrowIfNull(filePath, nameof(filePaths));

				if (!string.IsNullOrWhiteSpace(filePath) && _queuedPaths.Add(filePath))
					_pendingFileReloads.Enqueue(filePath);
			}
		}
	}

	/// <summary>Reloads the queued files, starting a pass or joining the one that runs, and completes when the pass has drained the queue.</summary>
	/// <param name="cancellationToken">
	/// A token that cancels the pass before its next file when this call starts it, and that stops only the wait when the call joins a pass.
	/// </param>
	/// <exception cref="OperationCanceledException">The pass or the wait was canceled; the files not reloaded yet stay queued.</exception>
	/// <remarks>
	/// Reloaded and unchanged results are not reported as failures.
	/// Only <see cref="WorkspaceDocumentConflictResolutionOutcome.ResolvedWithDisk"/>
	/// or <see cref="WorkspaceDocumentConflictResolutionOutcome.ResolvedWithLogical"/>
	/// resolves a conflict; any other outcome, including <see langword="null"/>, is reported as the original conflict result.
	/// A <see cref="WorkspaceDocumentReloadOutcome.Canceled"/> result, for example of a disposed store, stops the pass without being a failure,
	/// and a hook exception faults the pass; both leave the file and the files after it queued.
	/// </remarks>
	public Task ProcessQueuedFilesAsync(CancellationToken cancellationToken = default)
	{
		TaskCompletionSource pass;

		lock (_lock)
		{
			if (_pass is not null)
				return _pass.Task.WaitAsync(cancellationToken);

			_pass = pass = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		}

		// The pass starts on the caller's thread, outside the lock, so a host whose hooks need its UI thread calls from there.
		_ = RunPassAsync(pass, cancellationToken);

		return pass.Task;
	}

	// Ends with the pass completed, canceled or faulted, so it never throws itself.
	private async Task RunPassAsync(TaskCompletionSource pass, CancellationToken cancellationToken)
	{
		try
		{
			while (true)
			{
				string filePath;

				lock (_lock)
				{
					// The empty check and the end of the pass are one step, so a path queued after it starts the next pass.
					if (!_pendingFileReloads.TryPeek(out string? next))
					{
						_pass = null;
						pass.TrySetResult();
						return;
					}

					// The path stays at the front until it is reloaded, but its duplicate marker goes, so a change the reload itself causes is
					// queued again behind it instead of being dropped.
					filePath = next;
					_queuedPaths.Remove(filePath);
				}

				cancellationToken.ThrowIfCancellationRequested();

				if (_documents.TryGetSnapshot(filePath, out _) && await ProcessFileAsync(filePath, cancellationToken).ConfigureAwait(false))
				{
					// A canceled reload stops the pass instead of being reported as a failure; a canceled token throws.
					cancellationToken.ThrowIfCancellationRequested();
					EndInterruptedPass();
					pass.TrySetResult();
					return;
				}

				lock (_lock)
					_pendingFileReloads.Dequeue();
			}
		}
		catch (OperationCanceledException exception)
		{
			EndInterruptedPass();
			pass.TrySetCanceled(exception.CancellationToken);
		}
		catch (Exception exception)
		{
			EndInterruptedPass();
			pass.TrySetException(exception);
		}
	}

	// Restores the invariant that a path is marked exactly while it is queued, keeping one entry per path: a path queued again while its
	// own reload threw would otherwise appear twice and be reloaded twice by the next pass.
	private void EndInterruptedPass()
	{
		lock (_lock)
		{
			string[] remainingPaths = [.. _pendingFileReloads];

			_pendingFileReloads.Clear();
			_queuedPaths.Clear();

			foreach (string filePath in remainingPaths)
			{
				if (_queuedPaths.Add(filePath))
					_pendingFileReloads.Enqueue(filePath);
			}

			_pass = null;
		}
	}

	// Returns true when the pass must stop without reporting a failure: a result whose outcome is Canceled is not a per-file failure.
	private async Task<bool> ProcessFileAsync(string filePath, CancellationToken cancellationToken)
	{
		WorkspaceDocumentReloadResult result = await _hooks.ReloadDocument(filePath, cancellationToken).ConfigureAwait(false);

		if (result.Outcome is WorkspaceDocumentReloadOutcome.Reloaded or WorkspaceDocumentReloadOutcome.Unchanged)
			return false;

		if (result.Outcome == WorkspaceDocumentReloadOutcome.Canceled)
			return true;

		// The decision callback exists to feed a resolution decision, so it is skipped when no resolver
		// consumes the answer: asking for a decision that is then discarded would confuse the user.
		if (
			result.Outcome == WorkspaceDocumentReloadOutcome.ExternalFileConflict
			&& _hooks.ResolveConflict is { } resolveConflict
			&& _hooks.DecideReload is { } decideReload
		)
		{
			TDecision choice = await decideReload(result, cancellationToken).ConfigureAwait(false);
			WorkspaceDocumentConflictResolutionResult? resolution = await resolveConflict(result, choice, cancellationToken).ConfigureAwait(false);

			if (
				resolution?.Outcome
				is WorkspaceDocumentConflictResolutionOutcome.ResolvedWithDisk
					or WorkspaceDocumentConflictResolutionOutcome.ResolvedWithLogical
			)
			{
				return false;
			}
		}

		if (_hooks.ReportReloadFailure is { } reportReloadFailure)
			await reportReloadFailure(result, cancellationToken).ConfigureAwait(false);

		return false;
	}
}
