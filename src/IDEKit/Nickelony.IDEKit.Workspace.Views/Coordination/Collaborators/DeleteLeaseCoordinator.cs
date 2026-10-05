using System.Runtime.CompilerServices;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Enters and releases the delete lease that protects attached views from a file delete, a directory move, or a directory delete.</summary>
/// <param name="viewTracker">The tracker that records a release failure as unsynchronized view state.</param>
/// <param name="dispatchViewAction">The delegate that runs actions where view access is legal.</param>
/// <remarks>
/// <para>
/// Every lease call reaches a view through the dispatcher. The entered leases are latched on live views and the caller owns them: every exit path
/// that skips the explicit release releases them from a <see cref="DeleteLeaseSet"/>, or the views stay leased for every later synchronization.
/// </para>
/// <para>
/// A release failure is recorded as unsynchronized view state when the view stays attached after the operation, and is reported through the issue
/// list when the caller supplies one.
/// </para>
/// </remarks>
internal sealed class DeleteLeaseCoordinator(ViewTracker viewTracker, Func<Action, Task> dispatchViewAction)
{
	/// <summary>Runs a store operation between the entry and the release of the delete lease on the views it can destroy or move.</summary>
	/// <typeparam name="TStoreResult">The result type of the store operation.</typeparam>
	/// <typeparam name="TResult">The result type of the coordinator operation.</typeparam>
	/// <param name="plan">The steps that differ between the leased operations.</param>
	/// <returns>
	/// The blocked result when a view rejected its lease or its state blocks the operation; otherwise, the result built from the store result
	/// and the views that stayed unsynchronized.
	/// </returns>
	/// <remarks>
	/// A store operation that did not take effect releases the leases at once. One that did releases them inside the same dispatched action
	/// that updates the views, and every exit releases whatever lease is still held.
	/// </remarks>
	internal async Task<TResult> RunAsync<TStoreResult, TResult>(LeasedOperationDescriptor<TStoreResult, TResult> plan)
	{
		DeleteLeaseSet leases = new(this);
		await using ConfiguredAsyncDisposable leasesDisposal = leases.ConfigureAwait(false);

		List<WorkspaceDocumentViewSynchronizationIssue> blockingIssues = await EnterAsync(plan.Views, leases.EnteredLeases, plan.FindBlockingIssues)
			.ConfigureAwait(false);

		if (blockingIssues.Count > 0)
			return plan.CreateBlocked(blockingIssues);

		TStoreResult result = await plan.ExecuteStoreAsync().ConfigureAwait(false);

		if (!plan.IsApplied(result))
		{
			// The store failed, so the leases are released here; a release failure leaves the view
			// unsynchronized and is reported on the result.
			List<WorkspaceDocumentViewSynchronizationIssue> releaseIssues = [];
			await ReleaseAsync(leases.EnteredLeases, releaseIssues).ConfigureAwait(false);
			leases.MarkReleased();
			return plan.CreateResult(result, releaseIssues);
		}

		List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues = [];

		await dispatchViewAction(() =>
			{
				if (plan.Release == DeleteLeaseReleaseMode.BeforeViewUpdates)
					Release(leases.EnteredLeases, failedViewIssues, recordState: false);

				plan.UpdateViews(result, failedViewIssues);

				if (plan.Release == DeleteLeaseReleaseMode.AfterViewUpdates)
					Release(leases.EnteredLeases, failedViewIssues);
			})
			.ConfigureAwait(false);

		leases.MarkReleased();

		return plan.CreateResult(result, failedViewIssues);
	}

	/// <summary>
	/// Enters the delete lease on every view, then checks the state of the views, and returns the views that rejected, threw or block,
	/// with the failure each reported.
	/// </summary>
	/// <param name="views">The views to lease.</param>
	/// <param name="enteredLeases">Receives the leases that were entered.</param>
	/// <param name="findBlockingIssues">
	/// Finds the views whose state blocks the operation, or <see langword="null"/> for an operation that no view state blocks.
	/// </param>
	/// <remarks>
	/// The entry and the check run in one dispatched action, so typing that the dispatcher runs between two actions
	/// cannot land between them, and a lease that makes an editor read-only already holds when the check reads it. When any view fails or blocks,
	/// the leases that were already entered are rolled back and the rollback's release failures join the returned issues.
	/// </remarks>
	internal async Task<List<WorkspaceDocumentViewSynchronizationIssue>> EnterAsync(
		IEnumerable<IWorkspaceDocumentView> views,
		List<IWorkspaceDocumentDeleteLeaseView> enteredLeases,
		Func<List<WorkspaceDocumentViewSynchronizationIssue>>? findBlockingIssues = null
	)
	{
		List<WorkspaceDocumentViewSynchronizationIssue> issues = [];

		await dispatchViewAction(() =>
			{
				Enter(views, enteredLeases, issues);

				if (findBlockingIssues?.Invoke() is not { Count: > 0 } blockingIssues)
					return;

				issues.AddRange(blockingIssues);
				Release(enteredLeases, issues);
				enteredLeases.Clear();
			})
			.ConfigureAwait(false);

		return issues;
	}

	/// <summary>Releases the entered leases through the dispatcher after a failed or abandoned operation.</summary>
	/// <param name="enteredLeases">The leases to release.</param>
	/// <param name="failedViewIssues">Receives an issue for each lease whose release failed.</param>
	/// <remarks>
	/// Lease failures are recorded as unsynchronized view state by <see cref="Release"/> and reported as issues.
	/// </remarks>
	internal Task ReleaseAsync(
		List<IWorkspaceDocumentDeleteLeaseView> enteredLeases,
		List<WorkspaceDocumentViewSynchronizationIssue> failedViewIssues
	) => dispatchViewAction(() => Release(enteredLeases, failedViewIssues));

	/// <summary>Releases the lease of every view; runs inside a dispatched action.</summary>
	/// <param name="views">The leased views.</param>
	/// <param name="failedViewIssues">Receives an issue for each lease whose release failed, or <see langword="null"/> to report none.</param>
	/// <param name="recordState">
	/// <see langword="true"/> to record a release failure as unsynchronized view state because the view stays attached after the operation;
	/// <see langword="false"/> when the views are detached afterwards, where recording state for a view about to disappear is pointless.
	/// </param>
	internal void Release(
		IEnumerable<IWorkspaceDocumentDeleteLeaseView> views,
		List<WorkspaceDocumentViewSynchronizationIssue>? failedViewIssues = null,
		bool recordState = true
	)
	{
		foreach (IWorkspaceDocumentDeleteLeaseView view in views)
		{
			(bool failed, WorkspaceOperationFailure? failure) = ReleaseLease(view);

			if (!failed)
				continue;

			if (recordState)
				viewTracker.RecordFailure(view, failure);

			failedViewIssues?.Add(new WorkspaceDocumentViewSynchronizationIssue(ViewIdReader.Read(view), failure));
		}
	}

	/// <summary>Releases the leases that an exit path skipped, as the best effort of <see cref="DeleteLeaseSet.DisposeAsync"/>.</summary>
	/// <param name="views">The leases still held.</param>
	/// <remarks>
	/// The entered leases were latched on live views, so leaving them set would block every later synchronization. A failure here cannot be reported
	/// through a result the caller never receives, so it is recorded as unsynchronized view state instead.
	/// </remarks>
	internal async Task ReleaseViaDispatchAsync(List<IWorkspaceDocumentDeleteLeaseView> views)
	{
		bool released = false;

		try
		{
			await dispatchViewAction(() =>
				{
					Release(views);
					released = true;
				})
				.ConfigureAwait(false);
		}
		catch
		{
			// A delegate that faults before running the action leaves every lease held, so each view is
			// recorded as unsynchronized. The release itself records a per-view failure for every view it
			// could not release, so a fault after the action ran must not over-mark the views that released
			// cleanly.
			if (released)
				return;

			foreach (IWorkspaceDocumentView view in views)
				viewTracker.RecordFailure(view);
		}
	}

	// A lease release that reports a failed outcome or throws is a failure; a thrown call gets the
	// delete-lease failure code with its exception detail, while a failed outcome without a detail stays
	// null because the view chose to report none.
	private static (bool Failed, WorkspaceOperationFailure? Failure) ReleaseLease(IWorkspaceDocumentDeleteLeaseView view)
	{
		try
		{
			WorkspaceDocumentViewDeleteLeaseResult lease = view.ReleaseDeleteLease();
			return lease.Outcome == WorkspaceDocumentViewDeleteLeaseOutcome.Failed ? (true, lease.Failure) : (false, null);
		}
		catch (Exception exception)
		{
			return (true, new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.DeleteLeaseFailed, exception.Message, exception));
		}
	}

	// Enters the delete lease on each view and rolls back the leases that were already entered when any view
	// rejects the lease. Populates enteredLeases with the views that entered successfully and failedLeaseIssues
	// with the views that rejected or threw, each with the failure it reported. The rollback uses Release (whose
	// failures join the same issue list) so a throwing view records an unsynchronized failure instead of
	// faulting the whole operation. Runs inside a dispatched action.
	private void Enter(
		IEnumerable<IWorkspaceDocumentView> views,
		List<IWorkspaceDocumentDeleteLeaseView> enteredLeases,
		List<WorkspaceDocumentViewSynchronizationIssue> failedLeaseIssues
	)
	{
		foreach (IWorkspaceDocumentView view in views)
		{
			// A view that does not implement the lease capability is skipped: it cannot hold state that
			// a delete or directory move would lose, so there is nothing to lease.
			if (view is not IWorkspaceDocumentDeleteLeaseView leaseView)
				continue;

			try
			{
				WorkspaceDocumentViewDeleteLeaseResult result = leaseView.EnterDeleteLease();

				if (result.Outcome == WorkspaceDocumentViewDeleteLeaseOutcome.Entered)
					enteredLeases.Add(leaseView);
				else
					failedLeaseIssues.Add(new WorkspaceDocumentViewSynchronizationIssue(ViewIdReader.Read(view), result.Failure));
			}
			catch (Exception exception)
			{
				// A throwing lease call carries its exception detail, so the reported issue explains the failure
				// instead of being a code-less entry.
				failedLeaseIssues.Add(
					new WorkspaceDocumentViewSynchronizationIssue(
						ViewIdReader.Read(view),
						new WorkspaceOperationFailure(WorkspaceViewOperationFailureCodes.DeleteLeaseFailed, exception.Message, exception)
					)
				);
			}
		}

		if (failedLeaseIssues.Count > 0)
		{
			Release(enteredLeases, failedLeaseIssues);
			enteredLeases.Clear();
		}
	}
}
