namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// A workspace file watcher test double whose <see cref="Start"/> reports a failure through the callback the
/// framework registered and returns <see cref="WorkspaceWatcherStartOutcome.StartupFailed"/>, so a framework test
/// can drive a failure that is raised synchronously while the watcher is being started.
/// </summary>
internal sealed class TestFailingStartWorkspaceFileWatcher : IWorkspaceFileWatcher
{
	private readonly Action<IWorkspaceFileWatcher, Exception?> _onWatcherFailed;

	/// <summary>
	/// Initializes a new instance of the <see cref="TestFailingStartWorkspaceFileWatcher"/> class.
	/// </summary>
	/// <param name="onWatcherFailed">The watcher failure callback the framework registered.</param>
	public TestFailingStartWorkspaceFileWatcher(Action<IWorkspaceFileWatcher, Exception?> onWatcherFailed)
		=> _onWatcherFailed = onWatcherFailed;

	/// <summary>
	/// Gets a value indicating whether this watcher was disposed.
	/// </summary>
	public bool IsDisposed { get; private set; }

	/// <inheritdoc/>
	public WorkspaceWatcherStartResult Start()
	{
		Exception startupException = new IOException("Simulated synchronous startup failure.");
		_onWatcherFailed(this, startupException);
		return new(WorkspaceWatcherStartOutcome.StartupFailed, startupException);
	}

	/// <inheritdoc/>
	public void Dispose() => IsDisposed = true;
}
