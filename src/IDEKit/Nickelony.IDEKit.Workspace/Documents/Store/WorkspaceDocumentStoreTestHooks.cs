namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>
/// Groups the test-only hooks through which the <see cref="WorkspaceDocumentStore"/> suite holds points in the operation machinery
/// that are otherwise nondeterministic.
/// </summary>
/// <remarks>
/// The hooks are reached through <see cref="WorkspaceDocumentStore.TestHooks"/>,
/// or built directly to test a collaborator on its own. The type is <see langword="internal"/> and reachable only through <c>InternalsVisibleTo</c>;
/// every hook defaults to <see langword="null"/>, so production callers skip each hook branch. The properties stay mutable because a hook closure
/// captures the store instance under test.
/// </remarks>
internal sealed class WorkspaceDocumentStoreTestHooks
{
	private readonly object _stateLock;

	internal WorkspaceDocumentStoreTestHooks(object stateLock) => _stateLock = stateLock;

	/// <summary>
	/// The hook invoked after a directory operation acquires its disk gates and before its post-gate setup runs;
	/// a test sets it to throw so the runner's gate-release path runs, or to observe the gates held before the setup publishes any reservation.
	/// <see langword="null"/> in production.
	/// </summary>
	internal Action? DirectoryOperationPostGateSetup { get; set; }

	/// <summary>
	/// The hook invoked after a directory operation publishes its reservation and before it links to the lifetime token; a test sets
	/// it to dispose the store, cancel the call or observe the published reservation in that window. <see langword="null"/> in production.
	/// </summary>
	internal Action? DirectoryOperationBeforeLifetimeLink { get; set; }

	/// <summary>
	/// The hook invoked when an open that waited for another open's load resumes with that load's document, before it re-checks the document
	/// under the state lock; a test sets it to start a delete in that window. <see langword="null"/> in production.
	/// </summary>
	internal Action? OpenWaiterBeforeRecheck { get; set; }

	/// <summary>
	/// Whether the calling thread holds the store state lock, used by a test to observe whether disposal canceled the lifetime token
	/// while holding the lock, which would deadlock a host cancellation callback that runs inline and needs the lock.
	/// </summary>
	internal bool IsStateLockHeldByCurrentThread => Monitor.IsEntered(_stateLock);
}
