using System.Diagnostics.CodeAnalysis;
using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Synchronizes attached views with the document store's state, reporting the views that stay out of sync.</summary>
/// <remarks>
/// A view that throws from a probe, a refresh or an acknowledgment is recorded as unsynchronized and reported as an issue
/// instead of faulting the operation. A member that starts a view access dispatches it through the delegate; a member that the coordinator calls
/// from inside its own dispatched action says so. None reads a view while the coordinator's lock is held.
/// </remarks>
internal sealed class ViewSynchronizer
{
	private readonly ViewTracker _views;
	private readonly Func<Action, Task> _dispatchViewAction;

	/// <param name="views">The tracker of the attached views.</param>
	/// <param name="dispatchViewAction">The delegate that runs actions where view access is legal.</param>
	internal ViewSynchronizer(ViewTracker views, Func<Action, Task> dispatchViewAction)
	{
		_views = views;
		_dispatchViewAction = dispatchViewAction;
	}

	// Runs inside a dispatched action.
	internal static WorkspaceDocumentViewApplyResult AcknowledgeApply(IWorkspaceDocumentView view, WorkspaceDocumentMutationResult result)
	{
		try
		{
			return view.AcknowledgeApply(result);
		}
		catch (Exception exception)
		{
			return new WorkspaceDocumentViewApplyResult(
				WorkspaceDocumentViewApplyOutcome.Failed,
				new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.ViewAcknowledgeFailed, exception.Message, exception)
			);
		}
	}

	private static WorkspaceDocumentViewRefreshResult Refresh(IWorkspaceDocumentView view, WorkspaceDocumentSnapshot snapshot)
	{
		try
		{
			return view.Refresh(snapshot);
		}
		catch (Exception exception)
		{
			return new WorkspaceDocumentViewRefreshResult(
				WorkspaceDocumentViewRefreshOutcome.Failed,
				new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.ViewRefreshFailed, exception.Message, exception)
			);
		}
	}

	// A view blocks a store operation when it is marked unsynchronized, still has pending edits or a conflict,
	// or throws while reporting that state. The recovery operations (commit, reload, and conflict resolution)
	// ignore the unsynchronized state, so a view whose only outstanding problem is a prior synchronization
	// failure is retried instead of blocking the operation. The check must run through the dispatcher,
	// and the returned issues are not normalized; callers normalize them when they compose a result. A recorded
	// failure and a throwing probe carry their detail; pending edits or a conflict report none.
	internal List<WorkspaceDocumentViewSynchronizationIssue> GetBlockingViewIssues(
		IEnumerable<IWorkspaceDocumentView> views,
		bool includeUnsynchronizedState
	)
	{
		List<WorkspaceDocumentViewSynchronizationIssue> blockingIssues = [];

		foreach (IWorkspaceDocumentView view in views)
		{
			WorkspaceOperationFailure? failure = null;
			bool blocks = false;

			if (includeUnsynchronizedState && _views.TryGetUnsynchronized(view, out WorkspaceOperationFailure? unsynchronizedFailure))
			{
				blocks = true;
				failure = unsynchronizedFailure;
			}

			if (ViewHasUnpublishedState(view, out WorkspaceOperationFailure? probeFailure))
			{
				blocks = true;

				// A throwing state probe is a view failure like a failed refresh or acknowledgment, so the reported
				// issue explains why the view blocked the operation instead of dropping the exception detail.
				if (probeFailure is not null)
				{
					failure = probeFailure;
					_views.RecordFailure(view, failure);
				}
			}

			if (blocks)
				blockingIssues.Add(new WorkspaceDocumentViewSynchronizationIssue(ViewIdReader.Read(view), failure));
		}

		return blockingIssues;
	}

	// Runs through the dispatcher: returns the issues of the views that block a store
	// operation, or null when none block.
	internal Task<IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>?> GetBlockingIssuesAsync(
		string documentId,
		bool includeUnsynchronizedState = true
	) => GetBlockingIssuesAsync(_views.GetPeers(documentId, sourceView: null), includeUnsynchronizedState);

	internal async Task<IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>?> GetBlockingIssuesAsync(
		IEnumerable<IWorkspaceDocumentView> views,
		bool includeUnsynchronizedState = true
	)
	{
		IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue> blockingIssues = await QueryUnsynchronizedIssuesAsync(() =>
				GetBlockingViewIssues(views, includeUnsynchronizedState)
			)
			.ConfigureAwait(false);

		return blockingIssues.Count > 0 ? blockingIssues : null;
	}

	// Runs a view query through the dispatcher and returns the captured result. The query runs
	// inside the dispatched action, so every view access and mutation stays on the dispatch thread.
	private async Task<IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>> QueryUnsynchronizedIssuesAsync(
		Func<IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>> query
	)
	{
		IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue> issues = [];
		await _dispatchViewAction(() => issues = query()).ConfigureAwait(false);
		return issues;
	}

	// Refreshes the views that fell behind the snapshot or whose synchronization failed earlier, and collects
	// the views whose refresh did not succeed when a collector is supplied. A view whose synchronization failed
	// earlier is retried even when its recorded version is current, because a successful refresh clears the
	// unsynchronized state. A view that acquired pending edits or a conflict after the pre-check is skipped
	// rather than handed the snapshot, which would overwrite its unpublished buffer, and is reported through the
	// collector so the operation does not claim it synchronized. Runs inside a dispatched action.
	internal void RefreshPeerViews(
		IEnumerable<IWorkspaceDocumentView> views,
		WorkspaceDocumentSnapshot snapshot,
		List<WorkspaceDocumentViewSynchronizationIssue>? failedViewIssues
	)
	{
		foreach (IWorkspaceDocumentView view in views)
		{
			if (!_views.IsBehind(view, snapshot) && !_views.IsMarkedUnsynchronized(view))
				continue;

			if (ViewHasUnpublishedState(view, out WorkspaceOperationFailure? probeFailure))
			{
				// A probe failure is a view failure like a failed refresh; unpublished edits or a
				// conflict carry no detail, because the host can inspect that state on the view itself.
				if (probeFailure is not null)
					_views.RecordFailure(view, probeFailure);

				failedViewIssues?.Add(new WorkspaceDocumentViewSynchronizationIssue(ViewIdReader.Read(view), probeFailure));
				continue;
			}

			WorkspaceDocumentViewRefreshResult refresh = Refresh(view, snapshot);
			bool refreshed = refresh.Outcome == WorkspaceDocumentViewRefreshOutcome.Refreshed;

			_views.RecordResult(view, synchronized: refreshed, failure: refresh.Failure, snapshot);

			if (!refreshed)
				failedViewIssues?.Add(new WorkspaceDocumentViewSynchronizationIssue(ViewIdReader.Read(view), refresh.Failure));
		}
	}

	// Reports whether a view holds state the snapshot refresh must not overwrite: unpublished edits or
	// a conflict. A view that throws while reporting that state is treated as holding it, and the
	// probe failure is returned for the caller to record. Runs inside a dispatched action.
	private static bool ViewHasUnpublishedState(IWorkspaceDocumentView view, out WorkspaceOperationFailure? failure)
	{
		try
		{
			failure = null;
			return view.HasPendingEdits || view.HasConflict;
		}
		catch (Exception exception)
		{
			failure = new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.ViewStateProbeFailed, exception.Message, exception);
			return true;
		}
	}

	// Refreshes the attached views from the snapshot through the dispatcher and returns the
	// views that are not in sync with the resulting document state, with the failure each reported;
	// an empty list means every attached view synchronized. A view that has pending edits, a conflict,
	// or a recorded synchronization failure when the report is composed counts as unsynchronized,
	// including one that acquired that state while the operation ran: the report covers view state,
	// not only failures.
	internal Task<IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>> RefreshViewsAndCollectUnsynchronizedAsync(
		string documentId,
		WorkspaceDocumentSnapshot snapshot
	)
	{
		return QueryUnsynchronizedIssuesAsync(() =>
		{
			IWorkspaceDocumentView[] peers = _views.GetPeers(documentId, sourceView: null);
			RefreshPeerViews(peers, snapshot, failedViewIssues: null);
			return GetBlockingViewIssues(peers, includeUnsynchronizedState: true);
		});
	}

	// Collects the views that are not in sync with the document's current state when an operation did not
	// reach its post-operation refresh, so a failed commit, reload, or conflict resolution still reports the
	// views it left unsynchronized instead of claiming a clean result. Runs through the dispatcher.
	internal Task<IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>> CollectUnsynchronizedIssuesAsync(string documentId) =>
		QueryUnsynchronizedIssuesAsync(() => GetBlockingViewIssues(_views.GetPeers(documentId, sourceView: null), includeUnsynchronizedState: true));

	// Asks every peer view attached to the document to acknowledge the mutation through the dispatch
	// delegate and returns the views that did not apply it, with the failure each reported. A failed
	// acknowledgment is also recorded as unsynchronized view state.
	internal async Task<List<WorkspaceDocumentViewSynchronizationIssue>> AcknowledgeDocumentViewsAsync(WorkspaceDocumentMutationResult result)
	{
		List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues = [];

		if (result.Snapshot is null)
			return failedViewIssues;

		await _dispatchViewAction(() =>
			{
				foreach (IWorkspaceDocumentView view in _views.GetPeers(result.Snapshot.DocumentId, sourceView: null))
				{
					WorkspaceDocumentViewApplyResult acknowledgment = AcknowledgeApply(view, result);
					bool applied = acknowledgment.Outcome == WorkspaceDocumentViewApplyOutcome.Applied;

					_views.RecordResult(view, synchronized: applied, failure: acknowledgment.Failure, snapshot: result.Snapshot);

					if (!applied)
						failedViewIssues.Add(new WorkspaceDocumentViewSynchronizationIssue(ViewIdReader.Read(view), acknowledgment.Failure));
				}
			})
			.ConfigureAwait(false);

		return failedViewIssues;
	}

	// Acknowledges a logical replacement: the view that published it learns the outcome, and the peers that fell behind the
	// replacement or whose synchronization failed earlier are refreshed. A no-change replacement still carries the authoritative
	// snapshot, so the peers are reconciled against it as well. Returns the views that did not follow, with the failure each reported.
	internal async Task<List<WorkspaceDocumentViewSynchronizationIssue>> AcknowledgeReplacementAsync(
		WorkspaceDocumentMutationResult result,
		IWorkspaceDocumentView? sourceView
	)
	{
		List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues = [];

		await _dispatchViewAction(() =>
			{
				if (sourceView is not null)
				{
					WorkspaceDocumentViewApplyResult acknowledgment = AcknowledgeApply(sourceView, result);
					bool applied = acknowledgment.Outcome == WorkspaceDocumentViewApplyOutcome.Applied;

					_views.RecordResult(view: sourceView, synchronized: applied, failure: acknowledgment.Failure, snapshot: result.Snapshot);

					if (!applied)
						failedViewIssues.Add(new WorkspaceDocumentViewSynchronizationIssue(ViewIdReader.Read(sourceView), acknowledgment.Failure));
				}

				if (result.Snapshot is null || !result.Applied)
					return;

				RefreshPeerViews(_views.GetPeers(result.Snapshot.DocumentId, sourceView), result.Snapshot, failedViewIssues);
			})
			.ConfigureAwait(false);

		return failedViewIssues;
	}

	// Composes the view-synchronization outcome for a completed operation from the views that failed
	// to synchronize.
	internal static WorkspaceDocumentViewSynchronizationResult ComposeSynchronization(
		IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues
	)
	{
		return failedViewIssues.Count == 0
			? WorkspaceDocumentViewSynchronizationResult.Synchronized
			: WorkspaceDocumentViewSynchronizationResult.Unsynchronized(failedViewIssues);
	}

	internal List<ViewSnapshotPair> GetSnapshotPairs(IEnumerable<WorkspaceDocumentSnapshot> snapshots)
	{
		List<ViewSnapshotPair> pairs = [];
		HashSet<IWorkspaceDocumentView> seen = new(ReferenceEqualityComparer.Instance);

		foreach (WorkspaceDocumentSnapshot snapshot in snapshots)
		{
			foreach (IWorkspaceDocumentView view in _views.GetPeers(snapshot.DocumentId, sourceView: null))
			{
				if (seen.Add(view))
					pairs.Add(new ViewSnapshotPair(view, snapshot));
			}
		}

		return pairs;
	}

	// A view that cannot report its document key is not matched to a late identity change: the
	// captured pairs remain its only coordination point. Runs inside a dispatched action.
	internal static bool TryGetViewDocumentKey(IWorkspaceDocumentView view, [NotNullWhen(true)] out WorkspaceDocumentKey? documentKey)
	{
		try
		{
			documentKey = view.DocumentKey;
			return documentKey is not null;
		}
		catch
		{
			documentKey = null;
			return false;
		}
	}

	private static WorkspaceDocumentViewIdentityResult AcknowledgeIdentity(IWorkspaceDocumentView view, WorkspaceDocumentViewIdentityChange change)
	{
		try
		{
			return view.AcknowledgeIdentity(change);
		}
		catch (Exception exception)
		{
			return new WorkspaceDocumentViewIdentityResult(
				WorkspaceDocumentViewIdentityOutcome.Failed,
				new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.ViewIdentityUpdateFailed, exception.Message, exception)
			);
		}
	}

	// Asks every peer view attached to the document to acknowledge an identity change and returns
	// the composed synchronization outcome for the operation result.
	internal async Task<WorkspaceDocumentViewSynchronizationResult> SynchronizeIdentitiesAsync(
		string documentId,
		WorkspaceDocumentKey oldDocumentKey,
		WorkspaceDocumentSnapshot snapshot
	) => ComposeSynchronization(await AcknowledgeIdentityChangesAsync(documentId, oldDocumentKey, snapshot).ConfigureAwait(false));

	// Asks every peer view attached to the document to acknowledge the identity change through the
	// dispatcher and updates the recorded registration for each view that acknowledged. Returns
	// the views that failed to acknowledge, with the failure each reported. Peers are matched by the
	// old document key rather than the document id. A rename vacates the id, so a concurrent open at
	// the vacated path registers a different document under the same id string, and an id-only match
	// would rekey that unrelated view with the renamed document's snapshot.
	private async Task<List<WorkspaceDocumentViewSynchronizationIssue>> AcknowledgeIdentityChangesAsync(
		string documentId,
		WorkspaceDocumentKey oldDocumentKey,
		WorkspaceDocumentSnapshot snapshot
	)
	{
		List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues = [];
		WorkspaceDocumentViewIdentityChange change = new(oldDocumentKey, documentId, snapshot);

		await _dispatchViewAction(() =>
			{
				foreach (IWorkspaceDocumentView view in _views.GetPeersByDocumentKey(oldDocumentKey))
					AcknowledgeIdentity(view, change, failedViewIssues);
			})
			.ConfigureAwait(false);

		return failedViewIssues;
	}

	// Acknowledges the identity change of every view of a renamed directory: the views captured before the
	// rename by the snapshot they were paired with, and the views that attached while the store call was in
	// flight by the document key they report. Runs inside a dispatched action.
	internal void AcknowledgeDirectoryRename(
		IEnumerable<ViewSnapshotPair> pairs,
		IReadOnlyList<WorkspaceDocumentSnapshot> renamedSnapshots,
		List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues
	)
	{
		// The result snapshots are indexed once; a per-pair linear search would make the
		// tail quadratic in the number of moved documents.
		Dictionary<WorkspaceDocumentKey, WorkspaceDocumentSnapshot> snapshotsByKey = [];

		foreach (WorkspaceDocumentSnapshot candidate in renamedSnapshots)
			snapshotsByKey[candidate.DocumentKey] = candidate;

		HashSet<IWorkspaceDocumentView> capturedViews = new(ReferenceEqualityComparer.Instance);

		foreach (ViewSnapshotPair pair in pairs)
		{
			capturedViews.Add(pair.View);

			if (!snapshotsByKey.TryGetValue(pair.Snapshot.DocumentKey, out WorkspaceDocumentSnapshot? snapshot))
			{
				// The result did not carry the view's snapshot, so the snapshot captured for it is stale: record it
				// as unsynchronized in addition to reporting it, matching the acknowledgment-failure
				// branch of AcknowledgeIdentity.
				_views.RecordFailure(pair.View);
				failedViewIssues.Add(new WorkspaceDocumentViewSynchronizationIssue(ViewIdReader.Read(pair.View)));
				continue;
			}

			AcknowledgeIdentity(
				pair.View,
				new WorkspaceDocumentViewIdentityChange(pair.Snapshot.DocumentKey, pair.Snapshot.DocumentId, snapshot),
				failedViewIssues
			);
		}

		// A view that attached while the store call was in flight is not in the captured
		// pairs. It is matched to its moved document by the document key it reports and
		// acknowledged the same way, so it does not stay attached to the vacated id.
		foreach (IWorkspaceDocumentView view in _views.GetRegisteredViews())
		{
			if (
				capturedViews.Contains(view)
				|| !TryGetViewDocumentKey(view, out WorkspaceDocumentKey? viewDocumentKey)
				|| !snapshotsByKey.TryGetValue(viewDocumentKey.Value, out WorkspaceDocumentSnapshot? lateSnapshot)
			)
			{
				continue;
			}

			string? lateDocumentId = _views.GetDocumentId(view);

			if (lateDocumentId is null)
				continue;

			AcknowledgeIdentity(view, new WorkspaceDocumentViewIdentityChange(viewDocumentKey.Value, lateDocumentId, lateSnapshot), failedViewIssues);
		}
	}

	// Asks one view to acknowledge an identity change and records the outcome: a successful
	// acknowledgment updates the registration and clears the unsynchronized state, and a failure is
	// recorded as unsynchronized view state and reported as an issue. Runs inside a dispatched action.
	internal void AcknowledgeIdentity(
		IWorkspaceDocumentView view,
		WorkspaceDocumentViewIdentityChange change,
		List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues
	)
	{
		WorkspaceDocumentViewIdentityResult acknowledgment = AcknowledgeIdentity(view, change);

		if (acknowledgment.Outcome == WorkspaceDocumentViewIdentityOutcome.Updated)
		{
			_views.UpdateIdentity(view, change.Snapshot);
		}
		else
		{
			_views.RecordFailure(view, acknowledgment.Failure);
			failedViewIssues.Add(new WorkspaceDocumentViewSynchronizationIssue(ViewIdReader.Read(view), acknowledgment.Failure));
		}
	}
}
