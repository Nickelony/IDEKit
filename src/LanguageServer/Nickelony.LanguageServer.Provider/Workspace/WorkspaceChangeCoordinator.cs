using System.Diagnostics.CodeAnalysis;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Owns one workspace-watch scope per workspace root and forwards external workspace changes to the
/// language server through a shared change forwarder.
/// </summary>
/// <remarks>
/// Per-root watcher lifecycle, snapshot capture, and recovery reconciliation are delegated to the watch scopes, and a
/// path is normally forwarded only by the scope that owns it (longest prefix wins when roots nest). Change buffering
/// is reserved for recoverable transport or startup gaps after a client exists, and an empty watch-specification list
/// disables workspace watching entirely. The rationale lives in <c>docs/LanguageServerInternals.md</c>.
/// </remarks>
internal sealed class WorkspaceChangeCoordinator : IDisposable
{
	private readonly ILogger _logger;

	private readonly Func<string> _providerDisplayNameAccessor;
	private readonly string _workspaceRootsDisplayText;
	private readonly Func<string, bool> _isConfigurationPath;
	private readonly Func<object> _createSettingsPayload;

	private readonly Func<ILanguageServerClient?> _clientAccessor;
	private readonly Func<bool> _isDisposedAccessor;
	private readonly Action<long> _tryMarkTransportUnhealthy;

	private readonly WorkspaceWatchScope[] _watchScopes;
	private readonly WorkspaceFileChangeForwarder _workspaceFileChangeForwarder;

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceChangeCoordinator"/> class.
	/// </summary>
	/// <param name="context">The coordinator context: workspace roots, watch specifications, watcher factory, configuration hooks, and log identity.</param>
	internal WorkspaceChangeCoordinator(WorkspaceChangeCoordinatorContext context)
	{
		_logger = context.Logger;

		_providerDisplayNameAccessor = context.ProviderDisplayNameAccessor;
		_workspaceRootsDisplayText = context.WorkspaceRootsDisplayText;
		_isConfigurationPath = context.IsConfigurationPath;
		_createSettingsPayload = context.CreateSettingsPayload;
		_clientAccessor = context.Hooks.ClientAccessor;
		_isDisposedAccessor = context.Hooks.IsDisposedAccessor;
		_tryMarkTransportUnhealthy = context.Hooks.TryMarkTransportUnhealthy;

		// An empty specification list disables workspace watching: no watch scope is created, no snapshot is
		// tracked, and no replay is produced, so a host that bridges file events itself can opt out by passing
		// an empty list to the provider constructor.
		if (context.WatchSpecifications.Count == 0)
		{
			_watchScopes = [];
		}
		else
		{
			_watchScopes = new WorkspaceWatchScope[context.WorkspaceRootDirectoryPaths.Count];

			for (int i = 0; i < context.WorkspaceRootDirectoryPaths.Count; i++)
			{
				_watchScopes[i] = new WorkspaceWatchScope(
					new WorkspaceWatchScopeContext(
						context.WorkspaceRootDirectoryPaths[i],
						context.ProviderDisplayNameAccessor,
						context.WatchSpecifications,
						context.WorkspaceFileWatcherFactory,
						DispatchWorkspaceFileChangesAsync,
						_logger),
					context.Hooks);
			}
		}

		_workspaceFileChangeForwarder = new WorkspaceFileChangeForwarder(
			// Buffering covers recoverable transport/startup gaps only (see the type remarks).
			canForwardAccessor: () => _clientAccessor() is not null && !_isDisposedAccessor(),
			isDisposedAccessor: _isDisposedAccessor,
			ensureTransportStartedAsync: context.Hooks.EnsureTransportStartedAsync,
			// The forwarder's own transport marking is intentionally disabled: SendWorkspaceFileChangesAsync
			// marks the generation it captured before the send, which fences stale notifications, so
			// transport marking is owned by that path only.
			tryMarkTransportUnhealthy: static () => { },
			logForwardingFailure: failure =>
			{
				string firstPath = string.IsNullOrWhiteSpace(failure.FirstPath) ? "<unknown>" : failure.FirstPath;

				if (failure.WasDropped)
				{
					_logger.LogWarning(failure.Exception,
						"Dropped {BatchCount} {DisplayName} workspace file change(s) for '{Workspace}' after an unexpected forwarding failure. First path: '{FirstPath}'.",
						failure.BatchCount,
						_providerDisplayNameAccessor(),
						_workspaceRootsDisplayText,
						firstPath);
				}
				else
				{
					_logger.LogDebug(failure.Exception,
						"Failed to forward {BatchCount} {DisplayName} workspace file change(s) for '{Workspace}' starting at '{FirstPath}'; the batch was buffered for replay.",
						failure.BatchCount,
						_providerDisplayNameAccessor(),
						_workspaceRootsDisplayText,
						firstPath);
				}
			},
			bufferChangesWhileForwardingDisabled: false);
	}

	/// <summary>
	/// Starts the external workspace watcher of every root when the language-server client is available and
	/// captures each root's recovery baseline snapshot.
	/// </summary>
	internal async Task EnsureWorkspaceFileWatcherStartedAsync()
	{
		for (int i = 0; i < _watchScopes.Length; i++)
			await _watchScopes[i].EnsureWatcherStartedAsync().ConfigureAwait(false);
	}

	/// <summary>
	/// Forwards a coalesced batch of already-normalized external workspace changes to the language server.
	/// </summary>
	/// <param name="deliveringScope">The scope whose watcher or recovery reconciliation produced the batch.</param>
	/// <param name="batch">The coalesced file change batch.</param>
	/// <param name="cancellationToken">A token that can cancel the forwarding operation.</param>
	/// <remarks>
	/// <para>
	/// Batch entries are already normalized local paths: the workspace watcher and the snapshot tracker normalize
	/// every path before queuing it, so the batch is not re-normalized here.
	/// </para>
	/// <para>
	/// A path is forwarded when its owning scope is the delivering scope (longest prefix wins when roots nest), so
	/// overlapping watchers produce one notification: the outer root's watcher observes a nested root's subtree
	/// too, but the nested root's own watcher is the single forwarder for its paths.
	/// </para>
	/// <para>
	/// A non-owned path is still forwarded and tracked while the owning scope's workspace file watcher is inactive -
	/// not started yet, or being recovered - so a root that is temporarily unwatched stays covered by the delivering
	/// watcher instead of losing its changes. Once the owning scope's watcher is active again, its recovery
	/// reconciliation owns the missed-change detection. A path outside every root is forwarded as delivered.
	/// </para>
	/// </remarks>
	internal async Task DispatchWorkspaceFileChangesAsync(WorkspaceWatchScope deliveringScope, FileChangeBatch batch, CancellationToken cancellationToken)
	{
		if (_clientAccessor() is null || _isDisposedAccessor() || batch.Count == 0)
			return;

		var changes = new List<WorkspaceFileChange>(batch.Count);

		foreach ((string path, FileChangeKind kind) in batch.Entries)
		{
			WorkspaceWatchScope? owningScope = FindOwningScope(path);

			if (owningScope is not null && !ReferenceEquals(owningScope, deliveringScope) && owningScope.IsWatcherActive)
				continue;

			changes.Add(new WorkspaceFileChange(path, kind));
		}

		if (changes.Count == 0)
			return;

		bool forwarded = await _workspaceFileChangeForwarder.DispatchAsync(changes, SendWorkspaceFileChangesAsync, cancellationToken).ConfigureAwait(false);

		if (forwarded)
			ApplyTrackedChangesToOwningScopes(changes);
	}

	/// <summary>
	/// Replays any buffered workspace changes once the language server is ready again.
	/// </summary>
	/// <param name="cancellationToken">A token that can cancel the replay operation.</param>
	internal async Task ReplayDeferredWorkspaceFileChangesAsync(CancellationToken cancellationToken)
	{
		if (_clientAccessor() is null || _isDisposedAccessor())
			return;

		IReadOnlyList<WorkspaceFileChange> replayedChanges = await _workspaceFileChangeForwarder.ReplayDeferredAsync(SendWorkspaceFileChangesAsync, cancellationToken).ConfigureAwait(false);

		if (replayedChanges.Count > 0)
			ApplyTrackedChangesToOwningScopes(replayedChanges);
	}

	/// <summary>
	/// Disposes every root's watch scope and any buffered forwarding state.
	/// </summary>
	public void Dispose()
	{
		// Disposal-order invariant: every watcher and the forwarding buffer are stopped before the owner
		// disposes the language-server client, so a queued dispatch never observes a half-disposed
		// transport. The coordinator is always disposed before the provider's client.
		for (int i = 0; i < _watchScopes.Length; i++)
			_watchScopes[i].Dispose();

		_workspaceFileChangeForwarder.Dispose();
	}

	/// <summary>
	/// Sends one forwarded batch: a configuration refresh (when the batch touched a configuration path and the
	/// settings hook produced a payload) precedes the watched-files notification, and a transport failure marks
	/// the generation observed before the send unhealthy.
	/// </summary>
	/// <param name="changes">The forwarded file changes.</param>
	/// <param name="cancellationToken">A token that can cancel the sends.</param>
	private async Task SendWorkspaceFileChangesAsync(IReadOnlyList<WorkspaceFileChange> changes, CancellationToken cancellationToken)
	{
		ILanguageServerClient? client = _clientAccessor();

		if (client is null || _isDisposedAccessor() || changes.Count == 0)
			return;

		long transportGeneration = client.TransportGeneration;

		bool shouldRefreshConfiguration = false;
		var payloads = new List<FileEventPayload>(changes.Count);

		for (int i = 0; i < changes.Count; i++)
		{
			WorkspaceFileChange change = changes[i];

			// The containment hook is an extension point, so it runs only until the batch has proven to
			// contain a configuration path instead of once per remaining path.
			if (!shouldRefreshConfiguration)
				shouldRefreshConfiguration = IsConfigurationPathContained(change.Path);

			payloads.Add(new FileEventPayload(LanguageServerPaths.CreateFileUri(change.Path), change.Kind));
		}

		try
		{
			if (shouldRefreshConfiguration && TryCreateSettingsPayload(out object? settingsPayload))
			{
				await client.SendNotificationAsync(LspMethodNames.DidChangeConfiguration,
					new DidChangeConfigurationParams(settingsPayload),
					cancellationToken).ConfigureAwait(false);
			}

			await client.SendNotificationAsync(LspMethodNames.DidChangeWatchedFiles,
				new DidChangeWatchedFilesParams([.. payloads]), cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			_tryMarkTransportUnhealthy(transportGeneration);
			throw;
		}
		catch (IOException)
		{
			_tryMarkTransportUnhealthy(transportGeneration);
			throw;
		}
		catch (ObjectDisposedException) when (!_isDisposedAccessor())
		{
			_tryMarkTransportUnhealthy(transportGeneration);
			throw;
		}
	}

	/// <summary>
	/// Invokes the configuration-path hook, containing failures so a provider defect cannot break file forwarding.
	/// </summary>
	/// <param name="normalizedPath">The normalized path of the changed file.</param>
	/// <returns><see langword="true"/> when the path requires a settings refresh; otherwise, <see langword="false"/>.</returns>
	private bool IsConfigurationPathContained(string normalizedPath)
		=> HookContainment.Invoke(_logger, _providerDisplayNameAccessor(), () => _isConfigurationPath(normalizedPath), fallbackValue: false, "configuration-path");

	/// <summary>
	/// Invokes the settings-payload hook, containing failures so a provider defect cannot block the watched-files
	/// notification.
	/// </summary>
	/// <param name="settingsPayload">The created settings payload when the hook succeeded.</param>
	/// <returns><see langword="true"/> when the payload was created; otherwise, <see langword="false"/>.</returns>
	private bool TryCreateSettingsPayload([NotNullWhen(true)] out object? settingsPayload)
	{
		settingsPayload = HookContainment.Invoke<object?>(_logger, _providerDisplayNameAccessor(), _createSettingsPayload, fallbackValue: null, "settings-payload");
		return settingsPayload is not null;
	}

	/// <summary>
	/// Applies forwarded changes to the tracked snapshot of the scope that owns each path.
	/// </summary>
	/// <remarks>
	/// Roots may nest; the longest matching root wins, so a nested subtree's changes are applied to its own
	/// scope even when they arrive through the outer root's watcher.
	/// </remarks>
	/// <param name="changes">The normalized forwarded changes.</param>
	private void ApplyTrackedChangesToOwningScopes(IReadOnlyList<WorkspaceFileChange> changes)
	{
		Dictionary<WorkspaceWatchScope, List<WorkspaceFileChange>>? ownedChanges = null;

		for (int i = 0; i < changes.Count; i++)
		{
			WorkspaceFileChange change = changes[i];
			WorkspaceWatchScope? owningScope = FindOwningScope(change.Path);

			if (owningScope is null)
				continue;

			ownedChanges ??= new Dictionary<WorkspaceWatchScope, List<WorkspaceFileChange>>(_watchScopes.Length);

			if (!ownedChanges.TryGetValue(owningScope, out List<WorkspaceFileChange>? scopeChanges))
			{
				scopeChanges = [];
				ownedChanges.Add(owningScope, scopeChanges);
			}

			scopeChanges.Add(change);
		}

		if (ownedChanges is null)
			return;

		foreach ((WorkspaceWatchScope scope, List<WorkspaceFileChange> scopeChanges) in ownedChanges)
			scope.ApplyTrackedChanges(scopeChanges);
	}

	/// <summary>
	/// Finds the scope that owns a normalized path: the scope whose root is the longest prefix of the path.
	/// </summary>
	/// <param name="normalizedPath">The normalized path to route.</param>
	/// <returns>The owning scope, or <see langword="null"/> when no root contains the path.</returns>
	private WorkspaceWatchScope? FindOwningScope(string normalizedPath)
	{
		WorkspaceWatchScope? owningScope = null;
		int longestRootLength = -1;

		for (int i = 0; i < _watchScopes.Length; i++)
		{
			WorkspaceWatchScope scope = _watchScopes[i];

			if (scope.WorkspaceRootDirectoryPath.Length > longestRootLength
				&& IsPathWithinRoot(normalizedPath, scope.WorkspaceRootDirectoryPath))
			{
				owningScope = scope;
				longestRootLength = scope.WorkspaceRootDirectoryPath.Length;
			}
		}

		return owningScope;
	}

	/// <summary>
	/// Reports whether a normalized path lies within a normalized root directory.
	/// </summary>
	/// <param name="normalizedPath">The normalized path to test.</param>
	/// <param name="normalizedRoot">The normalized root directory.</param>
	/// <returns><see langword="true"/> when the path is the root itself or lies beneath it; otherwise, <see langword="false"/>.</returns>
	private static bool IsPathWithinRoot(string normalizedPath, string normalizedRoot)
	{
		if (!normalizedPath.StartsWith(normalizedRoot, LanguageServerPaths.LocalPathComparison))
			return false;

		if (normalizedPath.Length == normalizedRoot.Length)
			return true;

		// A drive or share root already ends with a separator; any other root requires a separator boundary
		// so sibling directories that share a name prefix cannot match. Both separator spellings mark the
		// boundary: a forward-slash spelling of a nested root must not misroute ownership to a shorter root.
		if (Path.EndsInDirectorySeparator(normalizedRoot))
			return true;

		char boundary = normalizedPath[normalizedRoot.Length];
		return boundary == Path.DirectorySeparatorChar || boundary == Path.AltDirectorySeparatorChar;
	}
}
