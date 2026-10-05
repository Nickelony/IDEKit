namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>Contains the outcome of entering or releasing a view delete lease.</summary>
/// <param name="Outcome">The lease outcome.</param>
/// <param name="Failure">Explains a failed lease operation, when one occurred.</param>
/// <remarks>
/// <see cref="Failure"/> is populated when the view could not change its lease state.
/// </remarks>
public sealed record WorkspaceDocumentViewDeleteLeaseResult(
	WorkspaceDocumentViewDeleteLeaseOutcome Outcome,
	WorkspaceOperationFailure? Failure = null
);

/// <summary>Describes the outcome of a view delete-lease operation.</summary>
public enum WorkspaceDocumentViewDeleteLeaseOutcome
{
	/// <summary>The view entered the lease.</summary>
	Entered,

	/// <summary>The view released the lease, or held none to release.</summary>
	Released,

	/// <summary>The view could not change its lease state.</summary>
	Failed,
}
