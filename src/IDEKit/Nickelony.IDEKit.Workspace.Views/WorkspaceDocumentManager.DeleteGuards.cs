using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

public sealed partial class WorkspaceDocumentManager
{
	// Owns the delete-guard lifecycle of one guarded operation: the guards that were entered and the
	// best-effort release for exit paths that skipped the explicit release. A view left guarded would
	// block every later synchronization, so the scope releases on exit unless the operation released
	// the guards itself (MarkReleased) or the entry rollback already released them.
	private sealed class DeleteGuardScope(WorkspaceDocumentManager manager)
	{
		public List<IWorkspaceDocumentDeleteGuardView> EnteredGuards { get; } = [];

		public void MarkReleased() => EnteredGuards.Clear();

		public async Task ReleaseOnExitAsync()
		{
			if (EnteredGuards.Count == 0)
				return;

			await manager.TryReleaseDeleteGuardsViaDispatchAsync(EnteredGuards).ConfigureAwait(false);
		}
	}

	// Releases the guard of every view. A release failure is recorded as unsynchronized view state
	// when recordState is true (the view stays attached after the operation) and skipped when the
	// views are detached afterwards, where recording state for a view about to disappear is
	// pointless. In both cases the failure is reported through the issue list when one is supplied.
	private void ReleaseDeleteGuards(
		IEnumerable<IWorkspaceDocumentDeleteGuardView> views,
		List<WorkspaceDocumentViewSynchronizationIssue>? failedViewIssues = null,
		bool recordState = true)
	{
		foreach (IWorkspaceDocumentDeleteGuardView view in views)
		{
			(bool failed, WorkspaceOperationFailure? failure) = TryReleaseDeleteGuard(view);
			if (!failed)
				continue;

			if (recordState)
				RecordViewFailure(view, failure);

			failedViewIssues?.Add(new WorkspaceDocumentViewSynchronizationIssue(ReadViewId(view), failure));
		}
	}

	// A guard release that reports a failed outcome or throws is a failure; a thrown call gets the
	// delete-guard failure code with its exception detail, while a failed outcome without a detail stays
	// null because the view chose to report none.
	private static (bool Failed, WorkspaceOperationFailure? Failure) TryReleaseDeleteGuard(IWorkspaceDocumentDeleteGuardView view)
	{
		try
		{
			WorkspaceDocumentViewDeleteGuardResult guard = view.ReleaseDeleteGuard();
			return guard.Outcome == WorkspaceDocumentViewDeleteGuardOutcome.Failed
				? (true, guard.Failure)
				: (false, null);
		}
		catch (Exception exception)
		{
			return (true, new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.DeleteGuardFailed, exception.Message, exception));
		}
	}

	// Closes and unregisters a view whose document was removed. Runs inside a dispatched action; a
	// view whose close fails is still unregistered and reported in failedViewIssues. Unregistration
	// happens first so a view that raises ApplyRequested from Close cannot re-enter a replacement
	// through a handler that is about to be removed. The close is unconditional: UnregisterOpenView
	// does not close the view, so a captured view that was only unregistered is still closed here, and
	// a view the host already closed is closed again, which Close documents as safe.
	private void CloseDeletedView(IWorkspaceDocumentView view, List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues)
	{
		UnregisterOpenView(view);

		try
		{
			view.Close();
		}
		catch
		{
			failedViewIssues.Add(new WorkspaceDocumentViewSynchronizationIssue(ReadViewId(view)));
		}
	}

	// Best-effort guard release for exits that skip the explicit release path. The entered guards were
	// latched on live views, so leaving them set would block every later synchronization; a failure
	// here cannot be reported through a result the caller never receives, so it is recorded as
	// unsynchronized view state instead.
	private async Task TryReleaseDeleteGuardsViaDispatchAsync(List<IWorkspaceDocumentDeleteGuardView> views)
	{
		bool released = false;
		try
		{
			await _dispatchViewAction(() =>
			{
				ReleaseDeleteGuards(views);
				released = true;
			}).ConfigureAwait(false);
		}
		catch
		{
			// A delegate that faults before running the action leaves every guard held, so each view is
			// recorded as unsynchronized. The release itself records a per-view failure for every view it
			// could not release, so a fault after the action ran must not over-mark the views that released
			// cleanly.
			if (released)
				return;

			foreach (IWorkspaceDocumentView view in views)
				RecordViewFailure(view);
		}
	}

	// Enters the delete guard on every view through the dispatch delegate and returns the views that
	// rejected or threw, with the failure each reported. The entered guards are latched on live views
	// and the caller owns them: every exit path that skips the explicit release must release them from
	// a finally block, or the views stay guarded for every later synchronization. When any view fails,
	// the guards that were already entered are rolled back by EnterDeleteGuards itself, and rollback
	// release failures join the returned issues.
	private async Task<List<WorkspaceDocumentViewSynchronizationIssue>> EnterDeleteGuardsViaDispatchAsync(
		IEnumerable<IWorkspaceDocumentView> views,
		List<IWorkspaceDocumentDeleteGuardView> enteredGuards)
	{
		List<WorkspaceDocumentViewSynchronizationIssue> failedGuardIssues = [];
		await _dispatchViewAction(
			() => EnterDeleteGuards(views, enteredGuards, failedGuardIssues))
			.ConfigureAwait(false);
		return failedGuardIssues;
	}

	// Releases the entered guards through the dispatch delegate after a failed or abandoned
	// operation. Guard failures are recorded as unsynchronized view state by ReleaseDeleteGuards and
	// reported as issues.
	private Task ReleaseDeleteGuardsViaDispatchAsync(
		List<IWorkspaceDocumentDeleteGuardView> enteredGuards,
		List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues)
		=> _dispatchViewAction(() => ReleaseDeleteGuards(enteredGuards, failedViewIssues));

	// Closes views that attached while a store operation was in flight and are still bound to the
	// removed instance. A view bound to a document reopened at the same path carries a new document
	// key and is left alone.
	private void CloseLateAttachedViews(
		string documentId,
		WorkspaceDocumentKey deletedDocumentKey,
		List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues)
	{
		foreach (IWorkspaceDocumentView view in GetPeers(documentId, sourceView: null))
		{
			if (TargetsDeletedInstance(view, deletedDocumentKey))
				CloseDeletedView(view, failedViewIssues);
		}
	}

	// A view that cannot report its identity is treated as bound to the removed instance; a view
	// bound to a document reopened at the same path reports the new key and is left alone.
	private static bool TargetsDeletedInstance(IWorkspaceDocumentView view, WorkspaceDocumentKey deletedDocumentKey)
	{
		try
		{
			return view.DocumentKey is not { } documentKey || documentKey == deletedDocumentKey;
		}
		catch
		{
			return true;
		}
	}

	// Enters the delete guard on each view through the dispatch delegate and rolls back the guards
	// that were already entered when any view rejects the guard. Populates enteredGuards with the
	// views that entered successfully and failedGuardIssues with the views that rejected or threw,
	// each with the failure it reported. The rollback uses ReleaseDeleteGuards (whose failures join
	// the same issue list) so a throwing view records an unsynchronized failure instead of faulting
	// the whole operation.
	private void EnterDeleteGuards(
		IEnumerable<IWorkspaceDocumentView> views,
		List<IWorkspaceDocumentDeleteGuardView> enteredGuards,
		List<WorkspaceDocumentViewSynchronizationIssue> failedGuardIssues)
	{
		foreach (IWorkspaceDocumentView view in views)
		{
			// A view that does not implement the guard capability is skipped: it cannot hold state that
			// a delete or directory move would lose, so there is nothing to guard.
			if (view is not IWorkspaceDocumentDeleteGuardView guardView)
				continue;

			try
			{
				WorkspaceDocumentViewDeleteGuardResult result = guardView.ApplyDeleteGuard();
				if (result.Outcome == WorkspaceDocumentViewDeleteGuardOutcome.Succeeded)
					enteredGuards.Add(guardView);
				else
					failedGuardIssues.Add(new WorkspaceDocumentViewSynchronizationIssue(ReadViewId(view), result.Failure));
			}
			catch (Exception exception)
			{
				// A throwing guard call carries its exception detail, so the reported issue explains the failure
				// instead of being a code-less entry.
				failedGuardIssues.Add(new WorkspaceDocumentViewSynchronizationIssue(
					ReadViewId(view),
					new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.DeleteGuardFailed, exception.Message, exception)));
			}
		}

		if (failedGuardIssues.Count > 0)
		{
			ReleaseDeleteGuards(enteredGuards, failedGuardIssues);
			enteredGuards.Clear();
		}
	}
}
