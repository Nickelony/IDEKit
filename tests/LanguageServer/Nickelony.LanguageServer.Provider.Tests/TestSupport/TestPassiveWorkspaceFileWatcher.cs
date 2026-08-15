namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// A workspace file watcher test double that never observes the file system: it reports a started watcher and
/// records its disposal, while every callback is driven by the test itself.
/// </summary>
/// <remarks>
/// A framework test that proves the recovery reconciliation needs a watcher that cannot deliver a real
/// file-system change on its own. The default watcher would observe the change while it is still running and
/// track it before the replacement capture could see it, which empties the reconciliation delta and makes the
/// assertion vacuous.
/// </remarks>
internal sealed class TestPassiveWorkspaceFileWatcher : IWorkspaceFileWatcher
{
	/// <summary>
	/// Gets a value indicating whether this watcher was disposed.
	/// </summary>
	public bool IsDisposed { get; private set; }

	/// <inheritdoc/>
	public WorkspaceWatcherStartResult Start()
		=> new(WorkspaceWatcherStartOutcome.Started, null);

	/// <inheritdoc/>
	public void Dispose() => IsDisposed = true;
}
