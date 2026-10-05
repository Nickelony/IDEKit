using Nickelony.IDEKit.Workspace.Documents;
using static Nickelony.IDEKit.Workspace.Views.Coordination.ViewSynchronizer;
using static Nickelony.IDEKit.Workspace.Views.Coordination.WorkspaceDocumentViewSynchronizationResult;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Runs the operations that destroy the document behind a view: a host replacement, a delete and a directory delete.</summary>
/// <remarks>
/// Each operation reads the state of the attached views first and reports <c>Blocked</c> when a view holds pending edits or a conflict,
/// or its state probe throws, because the operation would overwrite or remove that text. A delete also blocks on an unsynchronized failure.
/// The check runs in the dispatched action that holds the store call (a replacement) or enters the delete leases (a delete), so typing on the views'
/// own thread cannot land between the check and the step it protects.
/// </remarks>
internal sealed class DestroyOperationRunner
{
	private readonly IWorkspaceDocumentStore _store;
	private readonly ViewTracker _views;
	private readonly ViewSynchronizer _synchronizer;
	private readonly DeleteLeaseCoordinator _deleteLeases;
	private readonly ViewAttacher _attacher;
	private readonly Func<Action, Task> _dispatchViewAction;
	private readonly CoordinatorOperationRunner _operations;

	internal DestroyOperationRunner(
		IWorkspaceDocumentStore store,
		ViewTracker views,
		ViewSynchronizer synchronizer,
		DeleteLeaseCoordinator deleteLeases,
		ViewAttacher attacher,
		Func<Action, Task> dispatchViewAction,
		CoordinatorOperationRunner operations
	)
	{
		_store = store;
		_views = views;
		_synchronizer = synchronizer;
		_deleteLeases = deleteLeases;
		_attacher = attacher;
		_dispatchViewAction = dispatchViewAction;
		_operations = operations;
	}

	internal Task<WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult>> ReplaceAsync(
		WorkspaceDocumentReplaceRequest request,
		CancellationToken cancellationToken
	)
	{
		// The check of the views, the store replacement and the refresh of the views run in one dispatched action. When the dispatcher runs the
		// views' own thread, typing runs there too, so it cannot land between the check and the replacement. The changes are raised with a
		// contained fault, so a call that changed the document always returns its result.
		return _operations.RunAsync(
			async () =>
			{
				cancellationToken.ThrowIfCancellationRequested();

				WorkspaceDocumentMutationResult? result = null;
				List<WorkspaceDocumentViewSynchronizationIssue>? blockingIssues = null;
				List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues = [];
				IWorkspaceDocumentView[] peers = [];
				string[] peerIds = [];

				try
				{
					await _dispatchViewAction(() =>
						{
							peers = _views.GetPeers(request.Identity.DocumentId, sourceView: null);

							List<WorkspaceDocumentViewSynchronizationIssue> issues = _synchronizer.GetBlockingViewIssues(
								peers,
								includeUnsynchronizedState: false
							);

							if (issues.Count > 0)
							{
								blockingIssues = issues;
								return;
							}

							peerIds = [.. peers.Select(ViewIdReader.Read)];
							result = _store.Replace(request);
							_views.ObserveSnapshot(result.Snapshot);

							if (result is { Snapshot: { } snapshot, Applied: true })
								_synchronizer.RefreshPeerViews(peers, snapshot, failedViewIssues);
						})
						.ConfigureAwait(false);
				}
				catch (Exception exception) when (result is not null)
				{
					// The store replaced the document before the dispatcher or the refresh faulted, so the replacement is reported, and every view of
					// the document is recorded as unsynchronized so a later operation refreshes it.
					var failure = new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.ViewRefreshFailed, exception.Message, exception);
					failedViewIssues = [];

					for (int index = 0; index < peers.Length; index++)
					{
						_views.RecordFailure(peers[index], failure);
						failedViewIssues.Add(new WorkspaceDocumentViewSynchronizationIssue(peerIds[index], failure));
					}
				}

				if (result is not null)
				{
					return WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult>.FromStore(
						result,
						ComposeSynchronization(failedViewIssues)
					);
				}

				if (blockingIssues is null)
					throw new InvalidOperationException("The dispatcher completed the replacement without running it.");

				return WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult>.Blocked(
					GetCurrentSnapshot(request.Identity.DocumentId),
					Blocked(blockingIssues)
				);
			},

			containRaiseFault: true
		);
	}

	internal Task<WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult>> DeleteAsync(
		WorkspaceDocumentDeleteRequest request,
		CancellationToken cancellationToken
	)
	{
		return _operations.RunAsync(async () =>
		{
			IWorkspaceDocumentView[] views = _views.GetPeers(request.Identity.DocumentId, sourceView: null);

			return await _deleteLeases
				.RunAsync(
					new LeasedOperationDescriptor<
						WorkspaceDocumentDeleteResult,
						WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult>
					>
					{
						// The views are read again in the action that enters the leases, so a view detached since is not leased.
						Views = GetPeerViews(request.Identity.DocumentId),

						// A view with pending edits, a conflict or an unsynchronized failure blocks the delete: the delete destroys the
						// document whose unsaved state the view still holds. The check runs in the action that enters the leases, so typing
						// cannot land between them.
						FindBlockingIssues = () =>
							_synchronizer.GetBlockingViewIssues(GetPeerViews(request.Identity.DocumentId), includeUnsynchronizedState: true),

						ExecuteStoreAsync = () => _store.DeleteAsync(request, cancellationToken),

						// A document the store does not track is gone either way, so it closes its views like a
						// completed delete: leaving them registered would report them synchronized for a document
						// that does not exist.
						IsApplied = result =>
							result.Outcome is WorkspaceDocumentDeleteOutcome.Deleted or WorkspaceDocumentDeleteOutcome.DocumentNotFound,

						UpdateViews = (_, failedViewIssues) =>
						{
							foreach (IWorkspaceDocumentView view in views)
								_attacher.CloseDeletedView(view, failedViewIssues);

							_attacher.CloseLateAttachedViews(request.Identity.DocumentId, request.Identity.DocumentKey, failedViewIssues);
						},

						Release = DeleteLeaseReleaseMode.BeforeViewUpdates,

						CreateBlocked = issues =>
							WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult>.Blocked(
								GetCurrentSnapshot(request.Identity.DocumentId),
								Blocked(issues)
							),

						CreateResult = (result, issues) =>
							WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult>.FromStore(result, ComposeSynchronization(issues)),
					}
				)
				.ConfigureAwait(false);
		});
	}

	internal Task<WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryDeleteResult>> DeleteDirectoryAsync(
		WorkspaceDocumentDirectoryDeleteRequest request,
		CancellationToken cancellationToken
	)
	{
		return _operations.RunAsync(async () =>
		{
			WorkspaceDocumentSnapshot[] sourceSnapshots = [.. _store.GetSnapshotsUnderDirectory(request.DirectoryPath)];
			List<ViewSnapshotPair> pairs = _synchronizer.GetSnapshotPairs(sourceSnapshots);

			return await _deleteLeases
				.RunAsync(
					new LeasedOperationDescriptor<
						WorkspaceDocumentDirectoryDeleteResult,
						WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryDeleteResult>
					>
					{
						// A captured view detached since is not leased or checked.
						Views = GetStillRegisteredViews(pairs),

						// A view with pending edits, a conflict or an unsynchronized failure blocks the delete: the delete destroys the
						// documents whose unsaved state the view still holds. The check runs in the action that enters the leases.
						FindBlockingIssues = () =>
							_synchronizer.GetBlockingViewIssues(GetStillRegisteredViews(pairs), includeUnsynchronizedState: true),

						ExecuteStoreAsync = () => _store.DeleteDirectoryAsync(request, cancellationToken),
						IsApplied = result => result.Outcome == WorkspaceDocumentDirectoryDeleteOutcome.Deleted,

						UpdateViews = (result, failedViewIssues) =>
						{
							foreach (ViewSnapshotPair pair in pairs)
								_attacher.CloseDeletedView(pair.View, failedViewIssues);

							// The result snapshots cover every removed descendant, including descendants that
							// attached after this operation enumerated them; views reopened at the same paths
							// carry new keys and are left alone.
							foreach (WorkspaceDocumentSnapshot removedSnapshot in result.Snapshots)
								_attacher.CloseLateAttachedViews(removedSnapshot.DocumentId, removedSnapshot.DocumentKey, failedViewIssues);
						},

						Release = DeleteLeaseReleaseMode.BeforeViewUpdates,

						CreateBlocked = issues =>
							WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryDeleteResult>.Blocked(sourceSnapshots, Blocked(issues)),

						CreateResult = (result, issues) =>
							WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryDeleteResult>.FromStore(
								result,
								ComposeSynchronization(issues)
							),
					}
				)
				.ConfigureAwait(false);
		});
	}

	private WorkspaceDocumentSnapshot? GetCurrentSnapshot(string documentId) =>
		_store.TryGetSnapshot(documentId, out WorkspaceDocumentSnapshot? snapshot) ? snapshot : null;

	// Reads the views of the document each time it is enumerated, so a leased operation sees the views registered when its action runs.
	private IEnumerable<IWorkspaceDocumentView> GetPeerViews(string documentId)
	{
		foreach (IWorkspaceDocumentView view in _views.GetPeers(documentId, sourceView: null))
			yield return view;
	}

	// Yields the captured views that are still registered each time it is enumerated.
	private IEnumerable<IWorkspaceDocumentView> GetStillRegisteredViews(IEnumerable<ViewSnapshotPair> pairs)
	{
		foreach (ViewSnapshotPair pair in pairs)
		{
			if (_views.Contains(pair.View))
				yield return pair.View;
		}
	}
}
