using Nickelony.IDEKit.Workspace.Documents;
using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.Workspace.Views;

public sealed partial class WorkspaceDocumentManager
{
	private static WorkspaceDocumentViewApplyResult TryAcknowledgeApply(
		IWorkspaceDocumentView view,
		WorkspaceDocumentMutationResult result)
	{
		try
		{
			return view.AcknowledgeApply(result);
		}
		catch (Exception exception)
		{
			return new WorkspaceDocumentViewApplyResult(
				WorkspaceDocumentViewApplyOutcome.Failed,
				new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.ViewAcknowledgeFailed, exception.Message, exception));
		}
	}

	private static WorkspaceDocumentViewRefreshResult TryRefresh(
		IWorkspaceDocumentView view,
		WorkspaceDocumentSnapshot snapshot)
	{
		try
		{
			return view.Refresh(snapshot);
		}
		catch (Exception exception)
		{
			return new WorkspaceDocumentViewRefreshResult(
				WorkspaceDocumentViewRefreshOutcome.Failed,
				new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.ViewRefreshFailed, exception.Message, exception));
		}
	}

	// A view blocks a store operation when it is marked unsynchronized, still has pending edits or a
	// conflict, or throws while reporting that state. The recovery operations (commit, reload, and
	// conflict resolution) ignore the unsynchronized state so a view whose only outstanding problem
	// is a prior synchronization failure is retried instead of blocking the operation. The check must
	// run through the dispatch delegate. The returned issues are not normalized; the callers normalize
	// them when they compose a result. A recorded synchronization failure and a throwing state probe
	// carry their failure detail; a view blocked only by pending edits or a conflict reports none,
	// because the host can inspect that state on the view itself.
	private List<WorkspaceDocumentViewSynchronizationIssue> GetBlockingViewIssues(
		IEnumerable<IWorkspaceDocumentView> views,
		bool includeUnsynchronizedState)
	{
		List<WorkspaceDocumentViewSynchronizationIssue> blockingIssues = [];
		foreach (IWorkspaceDocumentView view in views)
		{
			WorkspaceOperationFailure? failure = null;
			bool blocks = false;
			if (includeUnsynchronizedState)
			{
				lock (_stateLock)
				{
					if (_registrations.TryGetValue(view, out ViewRegistration? registration)
						&& registration.Unsynchronized)
					{
						blocks = true;
						failure = registration.LastFailure;
					}
				}
			}

			try
			{
				blocks |= view.HasPendingEdits || view.HasConflict;
			}
			catch (Exception exception)
			{
				// A throwing state probe is a view failure like a failed refresh or acknowledgment, so the reported
				// issue explains why the view blocked the operation instead of dropping the exception detail.
				failure = new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.ViewStateProbeFailed, exception.Message, exception);
				RecordViewFailure(view, failure);
				blocks = true;
			}

			if (blocks)
				blockingIssues.Add(new WorkspaceDocumentViewSynchronizationIssue(ReadViewId(view), failure));
		}

		return blockingIssues;
	}

	// Runs through the dispatch delegate: returns the issues of the views that block a store
	// operation, or null when none block.
	private Task<IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>?> GetBlockingIssuesAsync(
		string documentId,
		bool includeUnsynchronizedState = true)
		=> GetBlockingIssuesAsync(GetPeers(documentId, sourceView: null), includeUnsynchronizedState);

	private async Task<IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>?> GetBlockingIssuesAsync(
		IEnumerable<IWorkspaceDocumentView> views,
		bool includeUnsynchronizedState = true)
	{
		IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue> blockingIssues = await QueryUnsynchronizedIssuesAsync(
			() => GetBlockingViewIssues(views, includeUnsynchronizedState)).ConfigureAwait(false);

		return blockingIssues.Count > 0 ? blockingIssues : null;
	}

	// Runs a view query through the dispatch delegate and returns the captured result. The query runs
	// inside the dispatched action, so every view access and mutation stays on the dispatch thread.
	private async Task<IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>> QueryUnsynchronizedIssuesAsync(
		Func<IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>> query)
	{
		IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue> issues = [];
		await _dispatchViewAction(() => issues = query()).ConfigureAwait(false);
		return issues;
	}

	// Refreshes the views that fell behind the snapshot or whose synchronization failed earlier, and
	// collects the views whose refresh did not succeed when a collector is supplied. A view whose
	// synchronization failed earlier is retried even when its recorded version is current: a
	// successful refresh clears the unsynchronized state. A view that acquired pending edits or a
	// conflict after the operation's pre-check is skipped instead of being handed the snapshot, which
	// would overwrite its unpublished buffer; it is reported through the collector (or by the state
	// report that follows) so the operation does not claim it synchronized. Runs inside a dispatched
	// action.
	private void RefreshPeerViews(
		IEnumerable<IWorkspaceDocumentView> views,
		WorkspaceDocumentSnapshot snapshot,
		List<WorkspaceDocumentViewSynchronizationIssue>? failedViewIssues)
	{
		foreach (IWorkspaceDocumentView view in views)
		{
			if (!IsViewBehind(view, snapshot.Version) && !IsViewMarkedUnsynchronized(view))
				continue;

			if (ViewHasUnpublishedState(view, out WorkspaceOperationFailure? probeFailure))
			{
				// A probe failure is a view failure like a failed refresh; unpublished edits or a
				// conflict carry no detail, because the host can inspect that state on the view itself.
				if (probeFailure is not null)
					RecordViewFailure(view, probeFailure);

				failedViewIssues?.Add(new WorkspaceDocumentViewSynchronizationIssue(ReadViewId(view), probeFailure));
				continue;
			}

			WorkspaceDocumentViewRefreshResult refresh = TryRefresh(view, snapshot);
			bool refreshed = refresh.Outcome == WorkspaceDocumentViewRefreshOutcome.Refreshed;
			RecordViewResult(view, refreshed, refresh.Failure, snapshot);
			if (!refreshed)
				failedViewIssues?.Add(new WorkspaceDocumentViewSynchronizationIssue(ReadViewId(view), refresh.Failure));
		}
	}

	// Reports whether a view holds state the snapshot refresh must not overwrite: unpublished edits or
	// a conflict. A view that throws while reporting that state is treated as holding it, and the
	// probe failure is returned for the caller to record. Runs inside a dispatched action.
	private static bool ViewHasUnpublishedState(
		IWorkspaceDocumentView view,
		out WorkspaceOperationFailure? failure)
	{
		try
		{
			failure = null;
			return view.HasPendingEdits || view.HasConflict;
		}
		catch (Exception exception)
		{
			failure = new WorkspaceOperationFailure(
				WorkspaceViewOperationFailureCodes.ViewStateProbeFailed,
				exception.Message,
				exception);
			return true;
		}
	}

	// Refreshes the attached views from the snapshot through the dispatch delegate and returns the
	// views that are not in sync with the resulting document state, with the failure each reported;
	// an empty list means every attached view synchronized. A view that has pending edits, a conflict,
	// or a recorded synchronization failure when the report is composed counts as unsynchronized,
	// including one that acquired that state while the operation ran: the report covers view state,
	// not only failures.
	private Task<IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>> RefreshViewsAndCollectUnsynchronizedAsync(
		string documentId,
		WorkspaceDocumentSnapshot snapshot)
		=> QueryUnsynchronizedIssuesAsync(() =>
		{
			IWorkspaceDocumentView[] peers = GetPeers(documentId, sourceView: null);
			RefreshPeerViews(peers, snapshot, failedViewIssues: null);
			return GetBlockingViewIssues(peers, includeUnsynchronizedState: true);
		});

	// Collects the views that are not in sync with the document's current state when an operation did not
	// reach its post-operation refresh, so a failed commit, reload, or conflict resolution still reports the
	// views it left unsynchronized instead of claiming a clean result. Runs through the dispatch delegate.
	private Task<IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>> CollectUnsynchronizedIssuesAsync(string documentId)
		=> QueryUnsynchronizedIssuesAsync(
			() => GetBlockingViewIssues(GetPeers(documentId, sourceView: null), includeUnsynchronizedState: true));

	// Asks every peer view attached to the document to acknowledge the mutation through the dispatch
	// delegate and returns the views that did not refresh, with the failure each reported. A failed
	// acknowledgment is also recorded as unsynchronized view state.
	private async Task<List<WorkspaceDocumentViewSynchronizationIssue>> AcknowledgeDocumentViewsAsync(WorkspaceDocumentMutationResult result)
	{
		List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues = [];
		if (result.Snapshot is null)
			return failedViewIssues;

		await _dispatchViewAction(() =>
		{
			foreach (IWorkspaceDocumentView view in GetPeers(result.Snapshot.DocumentId, sourceView: null))
			{
				WorkspaceDocumentViewApplyResult acknowledgment = TryAcknowledgeApply(view, result);
				bool applied = acknowledgment.Outcome == WorkspaceDocumentViewApplyOutcome.Applied;
				RecordViewResult(view, applied, acknowledgment.Failure, result.Snapshot);
				if (!applied)
					failedViewIssues.Add(new WorkspaceDocumentViewSynchronizationIssue(ReadViewId(view), acknowledgment.Failure));
			}
		}).ConfigureAwait(false);

		return failedViewIssues;
	}

	// Asks every peer view attached to the document to acknowledge an identity change and returns
	// the composed synchronization outcome for the operation result.
	private async Task<WorkspaceDocumentViewSynchronizationResult> SynchronizeViewIdentitiesAsync(
		string documentId,
		WorkspaceDocumentKey oldDocumentKey,
		WorkspaceDocumentSnapshot snapshot)
		=> ComposeSynchronization(
			await AcknowledgeIdentityChangesAsync(documentId, oldDocumentKey, snapshot).ConfigureAwait(false));

	// Asks every peer view attached to the document to acknowledge the identity change through the
	// dispatch delegate and updates the recorded binding for each view that acknowledged. Returns
	// the views that failed to acknowledge, with the failure each reported. Peers are matched by the
	// old document key rather than the document id: a rename vacates the id, so a concurrent open at
	// the vacated path registers a different document under the same id string, and an id-only match
	// would rekey that unrelated view with the renamed document's snapshot.
	private async Task<List<WorkspaceDocumentViewSynchronizationIssue>> AcknowledgeIdentityChangesAsync(
		string documentId,
		WorkspaceDocumentKey oldDocumentKey,
		WorkspaceDocumentSnapshot snapshot)
	{
		List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues = [];
		WorkspaceDocumentViewIdentityChange change = new(oldDocumentKey, documentId, snapshot);
		await _dispatchViewAction(() =>
		{
			foreach (IWorkspaceDocumentView view in GetPeersByDocumentKey(oldDocumentKey))
				AcknowledgeViewIdentity(view, change, failedViewIssues);
		}).ConfigureAwait(false);

		return failedViewIssues;
	}

	// Asks one view to acknowledge an identity change and records the outcome: a successful
	// acknowledgment updates the binding and clears the unsynchronized state, and a failure is
	// recorded as unsynchronized view state and reported as an issue. Runs inside a dispatched action.
	private void AcknowledgeViewIdentity(
		IWorkspaceDocumentView view,
		WorkspaceDocumentViewIdentityChange change,
		List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues)
	{
		WorkspaceDocumentViewIdentityResult acknowledgment = TryAcknowledgeIdentity(view, change);
		if (acknowledgment.Outcome == WorkspaceDocumentViewIdentityOutcome.Updated)
			UpdateViewIdentity(view, change.Snapshot);
		else
		{
			RecordViewFailure(view, acknowledgment.Failure);
			failedViewIssues.Add(new WorkspaceDocumentViewSynchronizationIssue(ReadViewId(view), acknowledgment.Failure));
		}
	}

	// Composes the view-synchronization outcome for a completed operation from the views that failed
	// to synchronize.
	private static WorkspaceDocumentViewSynchronizationResult ComposeSynchronization(IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues)
		=> failedViewIssues.Count == 0
			? WorkspaceDocumentViewSynchronizationResult.Synchronized
			: Unsynchronized(failedViewIssues);

	// Issue lists in results are normalized: de-duplicated by view id with an ordinal comparison and
	// sorted ordinally, so the reported order does not depend on registration or enumeration order.
	// A view reported several times keeps the entry that carries a failure.
	private static WorkspaceDocumentViewSynchronizationIssue[] NormalizeIssues(IEnumerable<WorkspaceDocumentViewSynchronizationIssue> issues)
		=> [.. issues
			.GroupBy(issue => issue.ViewId, StringComparer.Ordinal)
			.OrderBy(group => group.Key, StringComparer.Ordinal)
			.Select(group => group.FirstOrDefault(issue => issue.Failure is not null) ?? group.First())];

	private static WorkspaceDocumentViewSynchronizationResult Blocked(IEnumerable<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues)
		=> new(WorkspaceDocumentViewSynchronizationOutcome.Blocked, NormalizeIssues(failedViewIssues));

	private static WorkspaceDocumentViewSynchronizationResult Unsynchronized(IEnumerable<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues)
		=> new(WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized, NormalizeIssues(failedViewIssues));

	private IWorkspaceDocumentView[] GetPeers(
		string documentId,
		IWorkspaceDocumentView? sourceView)
	{
		lock (_stateLock)
		{
			if (!_viewsByDocument.TryGetValue(documentId, out List<IWorkspaceDocumentView>? documentViews))
				return [];

			if (sourceView is null)
				return documentViews.ToArray();

			IWorkspaceDocumentView[] peers = new IWorkspaceDocumentView[documentViews.Count];
			int count = 0;
			foreach (IWorkspaceDocumentView view in documentViews)
			{
				if (!ReferenceEquals(view, sourceView))
					peers[count++] = view;
			}

			if (count != peers.Length)
				Array.Resize(ref peers, count);

			return peers;
		}
	}

	// Returns the views registered to the document with the given key. The document key, not the
	// document id, identifies the document: after a rename or save-as vacates an id, a concurrent open
	// at that path registers a different document under the same id string, so peer matching by id
	// would rekey the unrelated view.
	private IWorkspaceDocumentView[] GetPeersByDocumentKey(WorkspaceDocumentKey documentKey)
	{
		lock (_stateLock)
		{
			List<IWorkspaceDocumentView>? peers = null;
			foreach ((IWorkspaceDocumentView view, ViewRegistration registration) in _registrations)
			{
				if (registration.DocumentKey != documentKey)
					continue;

				(peers ??= []).Add(view);
			}

			return peers is null ? [] : [.. peers];
		}
	}

	private List<ViewBinding> GetViewBindings(IEnumerable<WorkspaceDocumentSnapshot> snapshots)
	{
		List<ViewBinding> bindings = [];
		HashSet<IWorkspaceDocumentView> seen = new(ReferenceEqualityComparer.Instance);
		foreach (WorkspaceDocumentSnapshot snapshot in snapshots)
		{
			foreach (IWorkspaceDocumentView view in GetPeers(snapshot.DocumentId, sourceView: null))
			{
				if (seen.Add(view))
					bindings.Add(new ViewBinding(view, snapshot));
			}
		}

		return bindings;
	}

	// A view that cannot report its document key is not matched to a late identity change: the
	// captured bindings remain its only coordination point.
	private static bool TryGetViewDocumentKey(
		IWorkspaceDocumentView view,
		[NotNullWhen(true)] out WorkspaceDocumentKey? documentKey)
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
}
