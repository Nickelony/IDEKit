namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Captures the workspace file watcher the framework creates so framework tests can drive the coordinator's
/// dispatch path directly.
/// </summary>
internal sealed class TestWorkspaceWatcherCapture
{
	private Action<IWorkspaceFileWatcher, Exception?>? _onWatcherFailed;

	/// <summary>
	/// Gets the dispatch delegate the framework registered with the created watcher.
	/// </summary>
	public Func<FileChangeBatch, CancellationToken, Task>? Dispatch { get; private set; }

	/// <summary>
	/// Gets the created watcher.
	/// </summary>
	public IWorkspaceFileWatcher? Watcher { get; private set; }

	/// <summary>
	/// Gets the number of watchers the factory created.
	/// </summary>
	public int CreatedCount { get; private set; }

	/// <summary>
	/// Gets or sets a value indicating whether the factory throws instead of creating a watcher, simulating a
	/// watcher that cannot be recreated during recovery.
	/// </summary>
	public bool ThrowOnCreate { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the factory creates a passive watcher that never observes the file
	/// system, so a test can prove the recovery reconciliation of a change the watcher itself never delivered.
	/// </summary>
	public bool UsePassiveWatcher { get; set; }

	/// <summary>
	/// Reports a watcher failure through the callback the framework registered, so tests can drive the
	/// runtime-failure recovery path.
	/// </summary>
	/// <param name="exception">The failure to report, or <see langword="null"/> for none.</param>
	public void Fail(Exception? exception = null)
		=> Fail(Watcher!, exception);

	/// <summary>
	/// Reports a failure for one specific watcher instance, so tests can deliver a delayed failure callback for a
	/// watcher that was already replaced.
	/// </summary>
	/// <param name="watcher">The watcher instance whose failure is reported.</param>
	/// <param name="exception">The failure to report, or <see langword="null"/> for none.</param>
	public void Fail(IWorkspaceFileWatcher watcher, Exception? exception)
		=> _onWatcherFailed?.Invoke(watcher, exception);

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
		CreatedCount++;
		_onWatcherFailed = onWatcherFailed;

		if (ThrowOnCreate)
			throw new InvalidOperationException("Simulated watcher factory failure during recovery.");

		Watcher = UsePassiveWatcher
			? new TestPassiveWorkspaceFileWatcher()
			: new DefaultWorkspaceFileWatcherAdapter(
				workspaceRootDirectoryPath,
				dispatchAsync,
				[new WorkspaceWatchSpecification("*.test", IncludeSubdirectories: true)],
				onWatcherFailed);

		Dispatch = dispatchAsync;
		return Watcher;
	}
}
