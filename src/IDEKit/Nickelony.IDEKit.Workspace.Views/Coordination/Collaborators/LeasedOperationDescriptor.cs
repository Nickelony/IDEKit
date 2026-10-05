using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>The steps of one leased coordinator operation that differ from the others.</summary>
/// <typeparam name="TStoreResult">The result type of the store operation.</typeparam>
/// <typeparam name="TResult">The result type of the coordinator operation.</typeparam>
internal sealed class LeasedOperationDescriptor<TStoreResult, TResult>
{
	/// <summary>The views whose delete lease the operation enters.</summary>
	internal required IEnumerable<IWorkspaceDocumentView> Views { get; init; }

	/// <summary>
	/// Finds the views whose state blocks the operation; runs in the dispatched action that enters the leases, after they are entered.
	/// <see langword="null"/> for an operation that no view state blocks.
	/// </summary>
	internal Func<List<WorkspaceDocumentViewSynchronizationIssue>>? FindBlockingIssues { get; init; }

	/// <summary>Runs the store operation once every lease is entered.</summary>
	internal required Func<Task<TStoreResult>> ExecuteStoreAsync { get; init; }

	/// <summary>Reports whether the store operation took effect, so the views are updated, not only released.</summary>
	internal required Func<TStoreResult, bool> IsApplied { get; init; }

	/// <summary>Updates the views after an applied store operation; runs inside a dispatched action.</summary>
	/// <remarks>
	/// Receives the store result and the list that collects the views whose update failed.
	/// </remarks>
	internal required Action<TStoreResult, List<WorkspaceDocumentViewSynchronizationIssue>> UpdateViews { get; init; }

	/// <summary>Selects whether the leases are released before or after <see cref="UpdateViews"/>.</summary>
	internal required DeleteLeaseReleaseMode Release { get; init; }

	/// <summary>Builds the result for an operation that a view's rejected lease or its state blocked.</summary>
	internal required Func<IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>, TResult> CreateBlocked { get; init; }

	/// <summary>Builds the result from the store result and the views that stayed unsynchronized.</summary>
	internal required Func<TStoreResult, IReadOnlyList<WorkspaceDocumentViewSynchronizationIssue>, TResult> CreateResult { get; init; }
}

/// <summary>Selects when a leased operation releases the leases it entered after an applied store operation.</summary>
internal enum DeleteLeaseReleaseMode
{
	/// <summary>
	/// Before the views update, for views that close afterwards, so a release failure is reported without recording state on a view about to detach.
	/// </summary>
	BeforeViewUpdates,

	/// <summary>After the views update, for views that stay attached, so a release failure is recorded as unsynchronized view state.</summary>
	AfterViewUpdates,
}
