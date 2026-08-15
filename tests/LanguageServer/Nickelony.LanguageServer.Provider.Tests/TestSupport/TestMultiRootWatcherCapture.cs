namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Captures the workspace file watchers the framework creates for several workspace roots so framework tests can
/// drive each root's dispatch path directly (for example to cover nested roots).
/// </summary>
internal sealed class TestMultiRootWatcherCapture
{
	private readonly Dictionary<string, Func<FileChangeBatch, CancellationToken, Task>> _dispatches = new(LanguageServerPaths.LocalPathComparer);

	/// <summary>
	/// Gets the dispatch delegates the framework registered, keyed by workspace root directory.
	/// </summary>
	public IReadOnlyDictionary<string, Func<FileChangeBatch, CancellationToken, Task>> Dispatches => _dispatches;

	/// <summary>
	/// Creates and captures a workspace file watcher for the framework's watcher factory seam.
	/// </summary>
	/// <param name="workspaceRootDirectoryPath">The workspace root directory the watcher watches.</param>
	/// <param name="dispatchAsync">The dispatch delegate for coalesced file changes.</param>
	/// <param name="onWatcherFailed">The watcher failure callback.</param>
	/// <returns>The created watcher.</returns>
	public IWorkspaceFileWatcher Create(
		string workspaceRootDirectoryPath,
		Func<FileChangeBatch, CancellationToken, Task> dispatchAsync,
		Action<IWorkspaceFileWatcher, Exception?> onWatcherFailed)
	{
		_dispatches[workspaceRootDirectoryPath] = dispatchAsync;

		return new DefaultWorkspaceFileWatcherAdapter(
			workspaceRootDirectoryPath,
			dispatchAsync,
			[new WorkspaceWatchSpecification("*.test", IncludeSubdirectories: true)],
			onWatcherFailed);
	}
}
