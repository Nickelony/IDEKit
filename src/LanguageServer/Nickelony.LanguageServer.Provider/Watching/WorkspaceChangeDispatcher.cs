using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Delivers each external workspace change to the watch supervisor of the root that owns its path.</summary>
/// <remarks>
/// Per-root watcher lifecycle, snapshot capture, and recovery reconciliation are delegated to the watch supervisors,
/// and a path is normally forwarded only by the supervisor that owns it (longest prefix wins when roots nest). The dispatcher hands
/// the changes to a shared change forwarder, which sends them through the change sender. Change buffering is reserved for recoverable
/// connection or startup gaps after a client exists, and an empty watch-specification list disables workspace watching entirely. The rationale lives
/// in the <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/LanguageServerInternals.md">language-server internals guide</see>.
/// </remarks>
internal sealed class WorkspaceChangeDispatcher : IDisposable, IWorkspaceWatchRestoration
{
	private readonly ILogger _logger;

	private readonly string _providerDisplayName;
	private readonly string _workspaceRootsDisplayText;

	private readonly Func<ILanguageServerClient?> _clientAccessor;

	private readonly ProviderLifetime _disposal;

	private readonly WorkspaceChangeSender _sender;

	private readonly LocalPathComparisonPolicy _pathComparison;
	private readonly FileWatchSupervisor[] _watchSupervisors;
	private readonly WorkspaceChangeForwarder _workspaceChangeForwarder;

	// One flag per supervisor, set when the forwarder collapsed buffered changes of its root; the next replay rescans each flagged root.
	private readonly int[] _rescanRequested;
	private readonly Action<Task, string> _observeBackgroundTask;

	/// <param name="context">The dispatcher context: workspace roots, watch specifications, watcher factory, change sender, and log identity.</param>
	internal WorkspaceChangeDispatcher(WorkspaceChangeDispatcherContext context)
	{
		_logger = context.Logger;
		_pathComparison = context.Options.PathComparison;

		_providerDisplayName = context.ProviderDisplayName;
		_workspaceRootsDisplayText = context.WorkspaceRootsDisplayText;
		_sender = context.Sender;
		_clientAccessor = context.Owner.ClientAccessor;
		_disposal = context.Owner.Disposal;
		_observeBackgroundTask = context.ObserveBackgroundTask;

		// An empty specification list disables workspace watching: no watch supervisor is created, no snapshot is
		// tracked, and no replay is produced. A language package that needs no watching passes an empty list
		// to the provider constructor.
		if (context.WatchSpecifications.Count == 0)
		{
			_watchSupervisors = [];
		}
		else
		{
			_watchSupervisors = new FileWatchSupervisor[context.WorkspaceRootDirectoryPaths.Count];

			for (int i = 0; i < context.WorkspaceRootDirectoryPaths.Count; i++)
			{
				_watchSupervisors[i] = new FileWatchSupervisor(
					new FileWatchSupervisorSpecification(
						WorkspaceRootDirectoryPath: context.WorkspaceRootDirectoryPaths[i],
						DisplayName: context.ProviderDisplayName,
						WatchSpecifications: context.WatchSpecifications,
						FileWatcherFactory: context.FileWatcherFactory,
						FileSnapshotTrackerFactory: context.FileSnapshotTrackerFactory
					),

					new FileWatchSupervisorHooks
					{
						IsEnabled = () => _clientAccessor() is not null,
						IsDisposed = () => _disposal.IsDisposed,

						ReportFailure = failure =>
							context.Owner.RaiseWorkspaceFileWatchFailed(
								FileWatchFailureConversion.ToWorkspaceFileWatchFailure(failure, context.ProviderDisplayName)
							),

						DispatchAsync = DispatchWorkspaceChangesAsync,
						ObserveBackgroundTask = context.ObserveBackgroundTask,
					},

					_logger
				);
			}
		}

		_workspaceChangeForwarder = new WorkspaceChangeForwarder(
			// Buffering covers recoverable connection/startup gaps only (see the type remarks).
			canForwardAccessor: () => _clientAccessor() is not null && !_disposal.IsDisposed,
			disposal: _disposal,
			ensureConnectionStartedAsync: context.Owner.EnsureConnectionStartedAsync,

			logForwardingFailure: failure =>
			{
				string firstPath = string.IsNullOrWhiteSpace(failure.FirstPath) ? "<unknown>" : failure.FirstPath;

				if (failure.WasDropped)
				{
					_logger.LogWarning(
						failure.Exception,
						"Dropped {BatchCount} {DisplayName} workspace change(s) for '{Workspace}' after an unexpected forwarding failure. First path: '{FirstPath}'.",
						failure.BatchCount,
						_providerDisplayName,
						_workspaceRootsDisplayText,
						firstPath
					);
				}
				else
				{
					_logger.LogDebug(
						failure.Exception,
						"Failed to forward {BatchCount} {DisplayName} workspace change(s) for '{Workspace}' starting at '{FirstPath}'; the batch was buffered for replay.",
						failure.BatchCount,
						_providerDisplayName,
						_workspaceRootsDisplayText,
						firstPath
					);
				}
			},

			reportOverflow: RequestRescanOfOwningRoots,
			options: context.Options
		);

		_rescanRequested = new int[_watchSupervisors.Length];
	}

	/// <inheritdoc/>
	public async Task RestoreWorkspaceWatchingAsync(CancellationToken cancellationToken)
	{
		await EnsureWorkspaceFileWatcherStartedAsync().ConfigureAwait(false);
		await ReplayBufferedWorkspaceChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Starts the external workspace watcher of every root when the language-server client is available and captures each root's recovery
	/// baseline snapshot.
	/// </summary>
	internal async Task EnsureWorkspaceFileWatcherStartedAsync()
	{
		for (int i = 0; i < _watchSupervisors.Length; i++)
			await _watchSupervisors[i].EnsureWatcherStartedAsync().ConfigureAwait(false);
	}

	/// <summary>Forwards a coalesced batch of already-normalized external workspace changes to the language server.</summary>
	/// <param name="deliveringSupervisor">The supervisor whose watcher or recovery reconciliation produced the batch.</param>
	/// <param name="batch">The coalesced workspace change batch.</param>
	/// <param name="cancellationToken">A token that can cancel the forwarding operation.</param>
	/// <remarks>
	/// <para>
	/// Batch entries are already normalized local paths: the workspace watcher and the snapshot tracker normalize every path
	/// before queuing it, so the batch is not re-normalized here.
	/// </para>
	/// <para>
	/// A path is forwarded when its owning supervisor is the delivering supervisor (longest prefix wins when roots nest), so overlapping watchers
	/// produce one notification: the outer root's watcher observes a nested root's subtree too, but the nested root's own watcher is the single
	/// forwarder for its paths.
	/// </para>
	/// <para>
	/// A non-owned path is still forwarded and tracked while the owning supervisor's workspace
	/// file watcher is inactive - not started yet, or being recovered - so a root that is temporarily unwatched stays covered by the delivering
	/// watcher instead of losing its changes. Once the owning supervisor's watcher is active again, its recovery reconciliation owns
	/// the missed-change detection. A path outside every root is forwarded as delivered.
	/// </para>
	/// </remarks>
	internal async Task DispatchWorkspaceChangesAsync(
		FileWatchSupervisor deliveringSupervisor,
		FileChangeBatch batch,
		CancellationToken cancellationToken
	)
	{
		if (_clientAccessor() is null || _disposal.IsDisposed || batch.Count == 0)
			return;

		var changes = new List<FileChange>(batch.Count);

		foreach ((string path, FileChangeKind kind) in batch.Entries)
		{
			FileWatchSupervisor? owningSupervisor = FindOwningSupervisor(path);

			if (owningSupervisor is not null && !ReferenceEquals(owningSupervisor, deliveringSupervisor) && owningSupervisor.IsWatcherActive)
				continue;

			changes.Add(new FileChange(path, kind));
		}

		if (changes.Count == 0)
			return;

		bool forwarded = await _workspaceChangeForwarder.DispatchAsync(changes, _sender.SendAsync, cancellationToken).ConfigureAwait(false);

		if (forwarded)
			ApplyTrackedChangesToOwningSupervisors(changes);
	}

	/// <summary>Replays any buffered workspace changes once the language server is ready again.</summary>
	/// <param name="cancellationToken">A token that can cancel the replay operation.</param>
	internal async Task ReplayBufferedWorkspaceChangesAsync(CancellationToken cancellationToken)
	{
		if (_clientAccessor() is null || _disposal.IsDisposed)
			return;

		StartRequestedRescans();

		IReadOnlyList<FileChange> replayedChanges = await _workspaceChangeForwarder
			.ReplayBufferedAsync(_sender.SendAsync, cancellationToken)
			.ConfigureAwait(false);

		if (replayedChanges.Count > 0)
			ApplyTrackedChangesToOwningSupervisors(replayedChanges);
	}

	/// <summary>Disposes every root's watch supervisor and any buffered forwarding state.</summary>
	public void Dispose()
	{
		// Disposal-order invariant: every watcher and the forwarding buffer are stopped before the owner
		// disposes the language-server client, so a queued dispatch never observes a half-disposed
		// connection. The dispatcher is always disposed before the provider's client.
		for (int i = 0; i < _watchSupervisors.Length; i++)
			_watchSupervisors[i].Dispose();

		_workspaceChangeForwarder.Dispose();
	}

	/// <summary>Applies forwarded changes to the tracked snapshot of the supervisor that owns each path.</summary>
	/// <param name="changes">The normalized forwarded changes.</param>
	/// <remarks>
	/// Roots may nest; the longest matching root wins, so a nested subtree's changes are applied to its own supervisor even when they arrive
	/// through the outer root's watcher.
	/// </remarks>
	private void ApplyTrackedChangesToOwningSupervisors(IReadOnlyList<FileChange> changes)
	{
		Dictionary<FileWatchSupervisor, List<FileChange>>? ownedChanges = null;

		for (int i = 0; i < changes.Count; i++)
		{
			FileChange change = changes[i];
			FileWatchSupervisor? owningSupervisor = FindOwningSupervisor(change.Path);

			if (owningSupervisor is null)
				continue;

			ownedChanges ??= new Dictionary<FileWatchSupervisor, List<FileChange>>(_watchSupervisors.Length);

			if (!ownedChanges.TryGetValue(owningSupervisor, out List<FileChange>? supervisorChanges))
			{
				supervisorChanges = [];
				ownedChanges.Add(owningSupervisor, supervisorChanges);
			}

			supervisorChanges.Add(change);
		}

		if (ownedChanges is null)
			return;

		foreach ((FileWatchSupervisor supervisor, List<FileChange> supervisorChanges) in ownedChanges)
			supervisor.ApplyTrackedChanges(supervisorChanges);
	}

	/// <summary>Flags the root of each collapsed change for a rescan before the next replay.</summary>
	/// <param name="changes">The buffered changes the forwarder collapsed past its path limit.</param>
	/// <remarks>
	/// A rescan forwards what the root's tracked snapshot lacks, so it recovers every collapsed change of the root; a change outside every root
	/// has no snapshot to recover it from and is dropped.
	/// </remarks>
	private void RequestRescanOfOwningRoots(IReadOnlyList<FileChange> changes)
	{
		int droppedCount = 0;

		for (int i = 0; i < changes.Count; i++)
		{
			int index = FindOwningSupervisor(changes[i].Path) is { } owningSupervisor ? Array.IndexOf(_watchSupervisors, owningSupervisor) : -1;

			if (index < 0)
				droppedCount++;
			else
				Volatile.Write(ref _rescanRequested[index], 1);
		}

		_logger.LogWarning(
			"Collapsed {Count} buffered {DisplayName} workspace change(s) for '{Workspace}' into a rescan of their roots; {DroppedCount} outside every root were dropped.",
			changes.Count,
			_providerDisplayName,
			_workspaceRootsDisplayText,
			droppedCount
		);
	}

	/// <summary>Starts a rescan of each root whose buffered changes were collapsed, so the server learns what the collapse dropped.</summary>
	/// <remarks>
	/// A rescan dispatches through the forwarder, which ensures the connection is started; the replay can run under the startup lock, so each rescan
	/// runs in the background instead of being awaited there.
	/// </remarks>
	private void StartRequestedRescans()
	{
		for (int i = 0; i < _watchSupervisors.Length; i++)
		{
			if (Interlocked.Exchange(ref _rescanRequested[i], 0) != 0)
				_observeBackgroundTask(_watchSupervisors[i].RescanAsync(), $"{_providerDisplayName} workspace rescan");
		}
	}

	/// <summary>Finds the supervisor that owns a normalized path: the supervisor whose root is the longest prefix of the path.</summary>
	/// <param name="normalizedPath">The normalized path to route.</param>
	/// <returns>The owning supervisor, or <see langword="null"/> when no root contains the path.</returns>
	private FileWatchSupervisor? FindOwningSupervisor(string normalizedPath)
	{
		FileWatchSupervisor? owningSupervisor = null;
		int longestRootLength = -1;

		for (int i = 0; i < _watchSupervisors.Length; i++)
		{
			FileWatchSupervisor supervisor = _watchSupervisors[i];

			if (
				supervisor.WorkspaceRootDirectoryPath.Length > longestRootLength
				&& LocalPath.IsDirectoryOrDescendant(normalizedPath, supervisor.WorkspaceRootDirectoryPath, _pathComparison)
			)
			{
				owningSupervisor = supervisor;
				longestRootLength = supervisor.WorkspaceRootDirectoryPath.Length;
			}
		}

		return owningSupervisor;
	}
}
