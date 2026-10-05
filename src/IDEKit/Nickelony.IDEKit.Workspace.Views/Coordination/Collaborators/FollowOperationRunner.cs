using Nickelony.IDEKit.Workspace.Documents;
using static Nickelony.IDEKit.Workspace.Views.Coordination.ViewSynchronizer;
using static Nickelony.IDEKit.Workspace.Views.Coordination.WorkspaceDocumentViewSynchronizationResult;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>
/// Runs the operations that no view state blocks, so the views follow the store: a discard, a rename, a save-as and a directory rename.
/// </summary>
/// <remarks>
/// A discard abandons the logical changes, and a rename, a save-as and a directory rename cannot lose view edits, so each acknowledges its result
/// to the views afterwards and reports a view that did not follow.
/// </remarks>
internal sealed class FollowOperationRunner
{
	private readonly IWorkspaceDocumentStore _store;
	private readonly ViewTracker _views;
	private readonly ViewSynchronizer _synchronizer;
	private readonly DeleteLeaseCoordinator _deleteLeases;
	private readonly CoordinatorOperationRunner _operations;

	internal FollowOperationRunner(
		IWorkspaceDocumentStore store,
		ViewTracker views,
		ViewSynchronizer synchronizer,
		DeleteLeaseCoordinator deleteLeases,
		CoordinatorOperationRunner operations
	)
	{
		_store = store;
		_views = views;
		_synchronizer = synchronizer;
		_deleteLeases = deleteLeases;
		_operations = operations;
	}

	internal Task<WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentMutationResult>> DiscardAsync(
		WorkspaceDocumentDiscardRequest request,
		CancellationToken cancellationToken
	)
	{
		// Discard deliberately overrides attached view state: the caller already decided that the
		// logical changes are abandoned, so attached view state does not block it. Every attached view
		// is asked to acknowledge the restored snapshot, which clears its pending state; an
		// acknowledgment failure is retained as unsynchronized view state and reported on the result.
		return _operations.RunAsync(async () =>
		{
			cancellationToken.ThrowIfCancellationRequested();

			WorkspaceDocumentMutationResult result = _store.Discard(request);
			_views.ObserveSnapshot(result.Snapshot);

			if (result.Snapshot is null || !result.Applied)
			{
				return WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentMutationResult>.FromStore(
					result,
					WorkspaceDocumentViewSynchronizationResult.Synchronized
				);
			}

			List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues = await _synchronizer
				.AcknowledgeDocumentViewsAsync(result)
				.ConfigureAwait(false);

			return WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentMutationResult>.FromStore(
				result,
				ComposeSynchronization(failedViewIssues)
			);
		});
	}

	internal Task<WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentRenameResult>> RenameAsync(
		WorkspaceDocumentRenameRequest request,
		CancellationToken cancellationToken
	)
	{
		// Attached view state does not block a rename: the move cannot lose view edits, and the
		// identity change is acknowledged afterwards. Only an acknowledgment failure is reported.
		return _operations.RunAsync(async () =>
		{
			WorkspaceDocumentRenameResult result = await _store.RenameAsync(request, cancellationToken).ConfigureAwait(false);
			_views.ObserveSnapshot(result.Snapshot);

			if (result.Outcome != WorkspaceDocumentRenameOutcome.Renamed || result.Snapshot is null)
			{
				return WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentRenameResult>.FromStore(
					result,
					WorkspaceDocumentViewSynchronizationResult.Synchronized
				);
			}

			return WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentRenameResult>.FromStore(
				result,

				await _synchronizer
					.SynchronizeIdentitiesAsync(request.Identity.DocumentId, request.Identity.DocumentKey, result.Snapshot)
					.ConfigureAwait(false)
			);
		});
	}

	internal Task<WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentSaveAsResult>> SaveAsAsync(
		WorkspaceDocumentSaveAsRequest request,
		CancellationToken cancellationToken
	)
	{
		// Attached view state does not block a save-as: the write cannot lose view edits, and the
		// identity change is acknowledged afterwards. Only an acknowledgment failure is reported.
		return _operations.RunAsync(async () =>
		{
			WorkspaceDocumentSaveAsResult result = await _store.SaveAsAsync(request, cancellationToken).ConfigureAwait(false);
			_views.ObserveSnapshot(result.Snapshot);

			if (result.Outcome != WorkspaceDocumentSaveAsOutcome.SavedAs || result.Snapshot is null)
			{
				return WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentSaveAsResult>.FromStore(
					result,
					WorkspaceDocumentViewSynchronizationResult.Synchronized
				);
			}

			return WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentSaveAsResult>.FromStore(
				result,

				await _synchronizer
					.SynchronizeIdentitiesAsync(request.Identity.DocumentId, request.Identity.DocumentKey, result.Snapshot)
					.ConfigureAwait(false)
			);
		});
	}

	internal Task<WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult>> RenameDirectoryAsync(
		WorkspaceDocumentDirectoryRenameRequest request,
		CancellationToken cancellationToken
	)
	{
		// Attached view state does not block a directory rename for the same reason it does not block
		// a file rename: the move cannot lose view edits, and the identity changes are acknowledged
		// afterwards.
		return _operations.RunAsync(async () =>
		{
			WorkspaceDocumentSnapshot[] sourceSnapshots = [.. _store.GetSnapshotsUnderDirectory(request.SourceDirectoryPath)];
			List<ViewSnapshotPair> pairs = _synchronizer.GetSnapshotPairs(sourceSnapshots);

			return await _deleteLeases
				.RunAsync(
					new LeasedOperationDescriptor<
						WorkspaceDocumentDirectoryRenameResult,
						WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult>
					>
					{
						Views = pairs.Select(pair => pair.View),
						ExecuteStoreAsync = () => _store.RenameDirectoryAsync(request, cancellationToken),
						IsApplied = result => result.Outcome == WorkspaceDocumentDirectoryRenameOutcome.Renamed,

						UpdateViews = (result, failedViewIssues) =>
						{
							foreach (WorkspaceDocumentSnapshot movedSnapshot in result.Snapshots)
								_views.ObserveSnapshot(movedSnapshot);

							_synchronizer.AcknowledgeDirectoryRename(pairs, result.Snapshots, failedViewIssues);
						},

						Release = DeleteLeaseReleaseMode.AfterViewUpdates,

						CreateBlocked = issues =>
							WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult>.Blocked(sourceSnapshots, Blocked(issues)),

						CreateResult = (result, issues) =>
							WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult>.FromStore(
								result,
								ComposeSynchronization(issues)
							),
					}
				)
				.ConfigureAwait(false);
		});
	}
}
