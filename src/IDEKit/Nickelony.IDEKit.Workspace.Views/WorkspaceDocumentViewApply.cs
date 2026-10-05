namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>Contains the outcome of a view acknowledging an applied document mutation.</summary>
/// <param name="Outcome">The acknowledgment outcome.</param>
/// <param name="Failure">Explains a failed acknowledgment, when one occurred.</param>
/// <remarks>
/// Any outcome other than <see cref="WorkspaceDocumentViewApplyOutcome.Applied"/> leaves the view unsynchronized, so the coordinator records
/// the failure and keeps that state until a later refresh or acknowledgment succeeds.
/// </remarks>
public sealed record WorkspaceDocumentViewApplyResult(WorkspaceDocumentViewApplyOutcome Outcome, WorkspaceOperationFailure? Failure = null);

/// <summary>Describes the outcome of a view acknowledging an applied document mutation.</summary>
public enum WorkspaceDocumentViewApplyOutcome
{
	/// <summary>The view adopted the applied document state and is synchronized.</summary>
	Applied,

	/// <summary>The view could not adopt the applied state and is unsynchronized.</summary>
	Failed,
}
