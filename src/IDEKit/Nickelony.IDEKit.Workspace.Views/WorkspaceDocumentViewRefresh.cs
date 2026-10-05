namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>Contains the outcome of refreshing a view from workspace content.</summary>
/// <param name="Outcome">The refresh outcome.</param>
/// <param name="Failure">Explains a failed refresh, when one occurred.</param>
/// <remarks>
/// Any outcome other than <see cref="WorkspaceDocumentViewRefreshOutcome.Refreshed"/> requires the coordinator to treat the view as unsynchronized
/// until it is refreshed successfully.
/// </remarks>
public sealed record WorkspaceDocumentViewRefreshResult(WorkspaceDocumentViewRefreshOutcome Outcome, WorkspaceOperationFailure? Failure = null);

/// <summary>Describes the outcome of refreshing a view.</summary>
public enum WorkspaceDocumentViewRefreshOutcome
{
	Refreshed,

	/// <summary>The view could not apply the snapshot immediately: it keeps its current content and is marked stale.</summary>
	MarkedStale,

	/// <summary>The view could not apply the workspace content.</summary>
	Failed,
}
