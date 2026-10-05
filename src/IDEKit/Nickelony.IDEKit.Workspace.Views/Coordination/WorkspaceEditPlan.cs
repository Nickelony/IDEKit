using Nickelony.IDEKit.Workspace.Replacement;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>
/// Contains the replacements a language-server edit asks for on the documents the store tracks, and the parts of the edit that cannot be applied.
/// </summary>
/// <remarks>
/// A plan with <see cref="Issues"/> names edits that cannot be applied as asked. <see cref="WorkspaceEditApplier"/> plans an edit as a whole,
/// so it applies none of the <see cref="Targets"/> when <see cref="Succeeded"/> is <see langword="false"/>.
/// </remarks>
public sealed class WorkspaceEditPlan
{
	internal WorkspaceEditPlan(IReadOnlyList<DocumentReplacementTarget> targets, IReadOnlyList<WorkspaceEditPlanIssue> issues)
	{
		Targets = targets;
		Issues = issues;
	}

	/// <summary>The prepared replacements, one per tracked document the edit changes, in the order the edit names the documents.</summary>
	public IReadOnlyList<DocumentReplacementTarget> Targets { get; }

	/// <summary>The documents whose edits cannot be applied as asked.</summary>
	public IReadOnlyList<WorkspaceEditPlanIssue> Issues { get; }

	/// <summary>Whether every document the edit names can be changed as asked.</summary>
	public bool Succeeded => Issues.Count == 0;
}

/// <summary>Names a document whose part of a language-server edit cannot be applied, and why.</summary>
/// <param name="DocumentId">The document id the edit named.</param>
/// <param name="Kind">Why the edit cannot be applied.</param>
/// <param name="OpenResult">
/// The failed open of a <see cref="WorkspaceEditPlanIssueKind.DocumentNotTracked"/> document that the applier tried to open,
/// with its outcome and failure; otherwise, <see langword="null"/>.
/// </param>
public sealed record WorkspaceEditPlanIssue(
	string DocumentId,
	WorkspaceEditPlanIssueKind Kind,
	WorkspaceViewCoordinatorOpenResult? OpenResult = null
);

/// <summary>Classifies why a document's part of a language-server edit cannot be applied.</summary>
public enum WorkspaceEditPlanIssueKind
{
	/// <summary>The store tracks no document for the id, so there is no text to change.</summary>
	DocumentNotTracked,

	/// <summary>An edit's range ends before it starts.</summary>
	InvalidRange,

	/// <summary>Two edits of the document overlap or otherwise cannot apply together.</summary>
	EditsConflict,

	/// <summary>
	/// A view of the document kept edits it could not publish, a conflict or a failed synchronization, so the edit would address text the user
	/// does not see.
	/// </summary>
	PendingEditsNotPublished,

	/// <summary>
	/// The document changed after its views published and before the plan read it, such as by a publication that typing scheduled, so the edit
	/// would address text the language server did not see.
	/// </summary>
	DocumentChanged,
}
