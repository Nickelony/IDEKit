using Nickelony.IDEKit.Workspace.Documents;
using static Nickelony.IDEKit.Workspace.Views.Coordination.ViewSynchronizer;
using static Nickelony.IDEKit.Workspace.Views.Coordination.WorkspaceDocumentViewSynchronizationResult;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Runs the operations that recover a document from its file: a commit, a reload and an external-conflict resolution.</summary>
/// <remarks>
/// A view with pending edits or a conflict blocks the operation, because the operation would replace that state. A view whose only outstanding state
/// is a prior synchronization failure does not block it, so the operation can be retried, and its refresh retries the failed synchronization.
/// </remarks>
internal sealed class RecoverOperationRunner
{
	private readonly IWorkspaceDocumentStore _store;
	private readonly ViewTracker _views;
	private readonly ViewSynchronizer _synchronizer;
	private readonly CoordinatorOperationRunner _operations;

	internal RecoverOperationRunner(
		IWorkspaceDocumentStore store,
		ViewTracker views,
		ViewSynchronizer synchronizer,
		CoordinatorOperationRunner operations
	)
	{
		_store = store;
		_views = views;
		_synchronizer = synchronizer;
		_operations = operations;
	}

	internal Task<WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult>> CommitAsync(
		WorkspaceDocumentCommitRequest request,
		CancellationToken cancellationToken
	)
	{
		// A view whose only outstanding state is a prior synchronization failure does not block the
		// commit: the commit proceeds (a clean document makes it a no-op) and the post-commit
		// refresh retries the failed synchronization. Pending edits, conflicts, and a state probe
		// that throws still block the commit.
		return RunRecoveryAsync(
			request.Identity.DocumentId,
			() => _store.CommitAsync(request, cancellationToken),
			result => result.Outcome == WorkspaceDocumentCommitOutcome.Committed ? result.Snapshot : null
		);
	}

	internal Task<WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult>> ReloadAsync(
		WorkspaceDocumentReloadRequest request,
		CancellationToken cancellationToken
	)
	{
		// A view with pending edits or a conflict would lose that state when the document content is
		// replaced from disk, so it blocks the reload instead of being refreshed over. A view whose
		// only outstanding state is a prior synchronization failure does not block: the reload
		// retries the failed synchronization.
		return RunRecoveryAsync(
			request.Identity.DocumentId,
			() => _store.ReloadAsync(request, cancellationToken),
			result => result.Outcome == WorkspaceDocumentReloadOutcome.Reloaded ? result.Snapshot : null
		);
	}

	internal Task<WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentConflictResolutionResult>> ResolveExternalConflictAsync(
		WorkspaceDocumentConflictResolutionRequest request,
		CancellationToken cancellationToken
	)
	{
		// Pending edits or a conflict block the resolution: the view state would be replaced by the
		// resolution. A prior synchronization failure does not block; the post-resolution refresh
		// retries it.
		return RunRecoveryAsync(
			request.Identity.DocumentId,
			() => _store.ResolveExternalConflictAsync(request, cancellationToken),

			result =>
				result.Outcome
					is WorkspaceDocumentConflictResolutionOutcome.ResolvedWithDisk
						or WorkspaceDocumentConflictResolutionOutcome.ResolvedWithLogical
					? result.Snapshot
					: null
		);
	}

	// Runs a store operation that recovers the document from a conflict with its file. The operation is blocked
	// only by pending edits and conflicts. When it took effect, the views are refreshed from its snapshot; otherwise,
	// the report is composed from the current view state instead of claiming a clean result.
	private Task<WorkspaceViewCoordinatorDocumentResult<TStoreResult>> RunRecoveryAsync<TStoreResult>(
		string documentId,
		Func<Task<TStoreResult>> executeStoreAsync,
		Func<TStoreResult, WorkspaceDocumentSnapshot?> getRefreshSnapshot
	)
		where TStoreResult : class
	{
		return _operations.RunAsync(async () =>
		{
			IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>? blockingIssues = await _synchronizer
				.GetBlockingIssuesAsync(documentId, includeUnsynchronizedState: false)
				.ConfigureAwait(false);

			if (blockingIssues is not null)
				return WorkspaceViewCoordinatorDocumentResult<TStoreResult>.Blocked(GetCurrentSnapshot(documentId), Blocked(blockingIssues));

			TStoreResult result = await executeStoreAsync().ConfigureAwait(false);
			WorkspaceDocumentSnapshot? refreshSnapshot = getRefreshSnapshot(result);

			_views.ObserveSnapshot(refreshSnapshot);

			IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue> unsynchronizedIssues = refreshSnapshot is { } snapshot
				? await _synchronizer.RefreshViewsAndCollectUnsynchronizedAsync(documentId, snapshot).ConfigureAwait(false)
				: await _synchronizer.CollectUnsynchronizedIssuesAsync(documentId).ConfigureAwait(false);

			return WorkspaceViewCoordinatorDocumentResult<TStoreResult>.FromStore(result, ComposeSynchronization(unsynchronizedIssues));
		});
	}

	private WorkspaceDocumentSnapshot? GetCurrentSnapshot(string documentId) =>
		_store.TryGetSnapshot(documentId, out WorkspaceDocumentSnapshot? snapshot) ? snapshot : null;
}
