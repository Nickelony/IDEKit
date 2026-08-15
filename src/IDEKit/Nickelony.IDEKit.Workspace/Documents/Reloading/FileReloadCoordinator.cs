namespace Nickelony.IDEKit.Workspace.Documents.Reloading;

/// <summary>
/// Queues external file reloads and drives the reload, conflict, and failure flow for tracked
/// workspace documents using a host-neutral reload decision.
/// </summary>
/// <typeparam name="TDecisionResult">
/// The host-neutral value that <see cref="FileReloadHooks{TDecisionResult}.DecideReload"/> produces and
/// <see cref="FileReloadHooks{TDecisionResult}.ResolveConflict"/> receives. The coordinator passes it through
/// without interpreting it, which is why the type parameter cannot be erased behind a non-generic facade.
/// </typeparam>
/// <remarks>
/// This coordinator does not normalize paths or provide synchronization. Callers should serialize
/// access to an instance. A call to <see cref="ProcessQueuedFilesAsync"/> that finds another pass
/// already running returns without processing; the paths queued by then are reported through
/// <see cref="PendingFileCount"/> so the caller can drive the next pass instead of assuming a silent
/// no-op drained them.
/// A pass dequeues the entries that were queued when it started. A path queued while the pass runs
/// (for example from a watcher or host callback, including the path currently being processed) stays
/// queued and is processed by the next pass.
/// </remarks>
public sealed class FileReloadCoordinator<TDecisionResult>
{
	private readonly Queue<string> _pendingFileReloads = new();
	private readonly HashSet<string> _queuedPaths;

	/// <summary>
	/// Initializes a new instance of the <see cref="FileReloadCoordinator{TDecisionResult}"/> class.
	/// </summary>
	/// <param name="options">
	/// The options shared with the document store and the file system; its comparison decides which
	/// queued paths count as duplicates. Pass the same value the store that processes the reloads was
	/// created with - or read <see cref="IWorkspaceDocumentReader.PathComparison"/> from that store -
	/// because the default follows the operating system.
	/// </param>
	public FileReloadCoordinator(WorkspaceDocumentOptions? options = null)
	{
		_queuedPaths = new HashSet<string>((options ?? WorkspaceDocumentOptions.Default).PathComparison.Comparer);
	}

	/// <summary>
	/// Gets a value indicating whether a reload pass is currently running on this coordinator.
	/// </summary>
	/// <value><see langword="true"/> for the duration of a <see cref="ProcessQueuedFilesAsync"/> pass, including the queue bookkeeping before and after the hooks.</value>
	public bool IsRunning { get; private set; }

	/// <summary>
	/// Gets the number of paths waiting for a pass, including paths queued while a pass was running.
	/// </summary>
	/// <remarks>
	/// A call to <see cref="ProcessQueuedFilesAsync"/> that finds another pass already running returns
	/// without processing, so a caller that owns the driving loop checks this value after a pass and runs
	/// another pass while it is non-zero.
	/// </remarks>
	public int PendingFileCount => _pendingFileReloads.Count;

	/// <summary>
	/// Queues a file for reload. Blank paths and duplicates under the configured path comparison are ignored.
	/// </summary>
	/// <remarks>
	/// This member is not thread-safe; serialize coordinator access as described on the class, for
	/// example by queueing watcher events through a single synchronization point.
	/// </remarks>
	/// <param name="filePath">The file path to reload.</param>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	public void QueueFile(string filePath)
	{
		ArgumentNullException.ThrowIfNull(filePath);

		if (string.IsNullOrWhiteSpace(filePath) || !_queuedPaths.Add(filePath))
			return;

		_pendingFileReloads.Enqueue(filePath);
	}

	/// <summary>
	/// Processes the files queued at the start of the pass, prompting the host only for conflicts.
	/// </summary>
	/// <param name="hooks">The hook bundle for the pass.</param>
	/// <param name="cancellationToken">
	/// A token that can cancel the pass before the next file is processed; it is passed to every callback.
	/// </param>
	/// <remarks>
	/// Reloaded and unchanged results are not reported as failures. The decision callback receives the
	/// conflict result - including the current snapshot and identity - so a host prompt can present the
	/// document state without re-reading it; it is invoked only when a resolver is supplied, because a
	/// decision without a resolver cannot be applied. A conflict is considered resolved
	/// only when the resolver returns <see cref="WorkspaceDocumentConflictResolutionOutcome.ResolvedWithDisk"/>
	/// or <see cref="WorkspaceDocumentConflictResolutionOutcome.ResolvedWithLogical"/>; any other
	/// resolution outcome (including <see langword="null"/>) is reported through
	/// <see cref="FileReloadHooks{TDecisionResult}.ReportReloadFailure"/> as the original conflict
	/// result, and the resolution result is not surfaced separately. When no resolver is supplied, the
	/// conflict is reported without prompting. A result with
	/// <see cref="WorkspaceDocumentReloadOutcome.Canceled"/> - for example a store that was disposed
	/// while the reload ran - is not reported as a failure and stops the pass; the canceled entry and
	/// the entries queued after it stay pending. A callback exception propagates to the caller: the
	/// failing entry and the entries queued after it stay pending for the next pass, while entries
	/// already processed in this pass are not re-queued.
	/// </remarks>
	/// <returns>A task that completes when the pass has finished processing the queued files.</returns>
	/// <exception cref="ArgumentNullException">
	/// <see cref="FileReloadHooks{TDecisionResult}.ReloadDocument"/> is <see langword="null"/>, or
	/// <see cref="FileReloadHooks{TDecisionResult}.ResolveConflict"/> is supplied while
	/// <see cref="FileReloadHooks{TDecisionResult}.DecideReload"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="OperationCanceledException">The pass was canceled between files or by a callback.</exception>
	public async Task ProcessQueuedFilesAsync(
		FileReloadHooks<TDecisionResult> hooks,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(hooks.ReloadDocument);

		// The decision callback is only consumed by a resolver, so it is required only when one is
		// supplied.
		if (hooks.ResolveConflict is not null)
			ArgumentNullException.ThrowIfNull(hooks.DecideReload);

		if (IsRunning)
			return;

		IsRunning = true;

		try
		{
			// The pass takes the entries queued at its start and releases their duplicate markers
			// before processing, so a path queued while the pass runs - including the path currently
			// being processed, for example from a watcher callback fired by the reload itself - is
			// accepted and stays queued for the next pass instead of being dropped as a duplicate.
			string[] pendingSnapshot = _pendingFileReloads.ToArray();
			foreach (string filePath in pendingSnapshot)
				_queuedPaths.Remove(filePath);

			try
			{
				foreach (string filePath in pendingSnapshot)
				{
					cancellationToken.ThrowIfCancellationRequested();

					if (await ProcessFileAsync(filePath, hooks, cancellationToken).ConfigureAwait(false))
					{
						// A canceled reload stops the pass instead of being reported as a failure. When the caller's
						// token caused the cancellation it is renormalized into an exception; a store-disposal
						// cancellation leaves the remaining entries queued for a later pass.
						cancellationToken.ThrowIfCancellationRequested();
						break;
					}

					// The processed entry is the queue front; removing it as the pass advances keeps the
					// queue holding only work that still needs processing, including a path that was
					// re-queued while it was being processed (its duplicate marker was released for this
					// pass).
					_pendingFileReloads.Dequeue();
				}
			}
			finally
			{
				// Rebuilding the queue and the duplicate markers in one pass restores their invariant - a
				// path is marked exactly while it is queued - and keeps one entry per path: a path that
				// was re-queued while its own processing threw would otherwise appear twice and be
				// processed twice by the next pass.
				string[] remainingPaths = _pendingFileReloads.ToArray();
				_pendingFileReloads.Clear();
				_queuedPaths.Clear();
				foreach (string filePath in remainingPaths)
				{
					if (_queuedPaths.Add(filePath))
						_pendingFileReloads.Enqueue(filePath);
				}
			}
		}
		finally
		{
			IsRunning = false;
		}
	}

	// Processes one queued file. Returns true when the pass must stop without reporting a failure: a
	// result whose outcome is Canceled - for example because the store was disposed while the reload
	// ran - is not a per-file failure.
	private static async Task<bool> ProcessFileAsync(
		string filePath,
		FileReloadHooks<TDecisionResult> hooks,
		CancellationToken cancellationToken)
	{
		WorkspaceDocumentReloadResult result = await hooks
			.ReloadDocument(filePath, cancellationToken)
			.ConfigureAwait(false);
		if (result.Outcome is WorkspaceDocumentReloadOutcome.Reloaded or WorkspaceDocumentReloadOutcome.Unchanged)
			return false;

		if (result.Outcome == WorkspaceDocumentReloadOutcome.Canceled)
			return true;

		// The decision callback exists to feed a resolution decision, so it is skipped when no resolver
		// consumes the answer: asking for a decision that is then discarded would confuse the user.
		if (result.Outcome == WorkspaceDocumentReloadOutcome.ExternalFileConflict
			&& hooks.ResolveConflict is { } resolveConflict
			&& hooks.DecideReload is { } decideReload)
		{
			TDecisionResult choice = await decideReload(result, cancellationToken).ConfigureAwait(false);
			WorkspaceDocumentConflictResolutionResult? resolution = await resolveConflict(
				result,
				choice,
				cancellationToken).ConfigureAwait(false);
			if (resolution?.Outcome is WorkspaceDocumentConflictResolutionOutcome.ResolvedWithDisk
				or WorkspaceDocumentConflictResolutionOutcome.ResolvedWithLogical)
				return false;
		}

		if (hooks.ReportReloadFailure is { } reportReloadFailure)
			await reportReloadFailure(result, cancellationToken).ConfigureAwait(false);

		return false;
	}
}
