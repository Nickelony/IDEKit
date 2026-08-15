namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Groups the test-only hooks through which the <see cref="WorkspaceDocumentStore"/> suite holds
/// points in the operation machinery that are otherwise nondeterministic.
/// </summary>
/// <remarks>
/// The hooks are reached through <see cref="WorkspaceDocumentStore.TestHooks"/>. The type is
/// <see langword="internal"/> and reachable only through <c>InternalsVisibleTo</c>; every hook
/// defaults to <see langword="null"/>, so production callers skip each hook branch. The properties
/// stay mutable because a hook closure captures the store instance under test.
/// </remarks>
internal sealed class WorkspaceDocumentStoreTestHooks
{
	private readonly object _stateLock;

	internal WorkspaceDocumentStoreTestHooks(object stateLock)
	{
		_stateLock = stateLock;
	}

	/// <summary>
	/// Gets or sets the hook invoked after a directory operation's disk gate is acquired and its
	/// post-gate setup runs, so a test can force that setup to throw and exercise the driver's
	/// gate-release path, or <see langword="null"/> in production.
	/// </summary>
	public Action? DirectoryOperationPostGateSetup { get; set; }

	/// <summary>
	/// Gets or sets the hook invoked after a directory operation publishes its reservation and before
	/// it links to the lifetime token, so a test can dispose the store in that window, or
	/// <see langword="null"/> in production.
	/// </summary>
	public Action? DirectoryOperationBeforeLifetimeLink { get; set; }

	/// <summary>
	/// Gets a value that reports whether the calling thread holds the store state lock. A host
	/// cancellation callback runs inline on the thread that cancels the lifetime token, so a test
	/// uses this to observe whether disposal canceled the token while holding the lock (which would
	/// deadlock a callback that needs the lock).
	/// </summary>
	public bool IsStateLockHeldByCurrentThread => Monitor.IsEntered(_stateLock);
}
