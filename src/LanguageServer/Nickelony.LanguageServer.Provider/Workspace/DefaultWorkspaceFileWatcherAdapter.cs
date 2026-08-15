namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// The default watcher adapter: it wraps this package's own <see cref="WorkspaceFileWatcher"/> behind the
/// <see cref="IWorkspaceFileWatcher"/> seam.
/// </summary>
/// <remarks>
/// The default watcher-creation hook uses this adapter, so the workspace machinery depends only on the interface
/// and a host can substitute its own watcher implementation. It has no dependency on the client package.
/// </remarks>
internal sealed class DefaultWorkspaceFileWatcherAdapter : IWorkspaceFileWatcher
{
	private readonly WorkspaceFileWatcher _watcher;

	/// <summary>
	/// Initializes a new instance of the <see cref="DefaultWorkspaceFileWatcherAdapter"/> class and creates the
	/// wrapped workspace watcher.
	/// </summary>
	/// <param name="workspaceRootDirectoryPath">The workspace root directory to watch.</param>
	/// <param name="dispatchAsync">The callback that forwards coalesced file changes to the owner.</param>
	/// <param name="watchSpecifications">The file patterns to watch under the workspace root.</param>
	/// <param name="onWatcherFailed">The callback that reports a watcher failure to the owner.</param>
	/// <param name="logger">The owner's logger, or <see langword="null"/> for a no-op logger.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="dispatchAsync"/>, <paramref name="watchSpecifications"/>, or
	/// <paramref name="onWatcherFailed"/> is <see langword="null"/>.
	/// </exception>
	internal DefaultWorkspaceFileWatcherAdapter(
		string workspaceRootDirectoryPath,
		Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
		IReadOnlyList<WorkspaceWatchSpecification> watchSpecifications,
		Action<IWorkspaceFileWatcher, Exception?> onWatcherFailed,
		ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(dispatchAsync);
		ArgumentNullException.ThrowIfNull(watchSpecifications);
		ArgumentNullException.ThrowIfNull(onWatcherFailed);

		// The client watcher reports failures with its own type; the adapter forwards its own instance so the rest
		// of the provider framework stays on the interface. The logger argument is named so optional client
		// watcher parameters added over time do not shift this call.
		_watcher = new WorkspaceFileWatcher(
			workspaceRootDirectoryPath,
			dispatchAsync,
			watchSpecifications,
			(_, exception) => onWatcherFailed(this, exception),
			logger: logger);
	}

	/// <inheritdoc/>
	public WorkspaceWatcherStartResult Start()
		=> _watcher.Start();

	/// <inheritdoc/>
	public void Dispose()
		=> _watcher.Dispose();
}
