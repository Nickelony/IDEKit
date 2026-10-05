using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>Describes a workspace document identity change acknowledged by a view.</summary>
/// <param name="OldDocumentKey">The document instance the view was attached to before the change.</param>
/// <param name="OldDocumentId">The normalized document id the view was attached to before the change.</param>
/// <param name="Snapshot">The snapshot after the change; it carries the new path, content, and version.</param>
/// <remarks>
/// <see cref="OldDocumentId"/> identifies the view's document before a rename or directory move;
/// <see cref="Snapshot"/> carries the new path, content, version, and preserved document key.
/// </remarks>
public sealed record WorkspaceDocumentViewIdentityChange(
	WorkspaceDocumentKey OldDocumentKey,
	string OldDocumentId,
	WorkspaceDocumentSnapshot Snapshot
);

/// <summary>Contains the outcome of updating a view's document identity.</summary>
/// <param name="Outcome">The identity update outcome.</param>
/// <param name="Failure">Explains a failed identity update, when one occurred.</param>
/// <remarks>
/// A failed result causes the coordinator to retain the view's prior registration and mark it unsynchronized; a successful result updates
/// the registration and clears that state.
/// </remarks>
public sealed record WorkspaceDocumentViewIdentityResult(WorkspaceDocumentViewIdentityOutcome Outcome, WorkspaceOperationFailure? Failure = null);

/// <summary>Describes the outcome of updating a view's document identity.</summary>
public enum WorkspaceDocumentViewIdentityOutcome
{
	Updated,

	/// <summary>The view rejected the identity change or could not apply it.</summary>
	Failed,
}
