using Nickelony.IDEKit.Workspace.Replacement;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Contains the outcome of applying a language-server edit through a <see cref="WorkspaceEditApplier"/>.</summary>
/// <param name="Plan">The plan of the edit; when it did not succeed, no document changed.</param>
/// <param name="Replacement">The per-document result of the replacements, or <see langword="null"/> when the plan did not succeed.</param>
/// <param name="ViewSynchronization">
/// The view synchronization of each document the coordinator replaced or its views blocked, keyed by document id: a blocked document names the views
/// that held edits, and an unsynchronized one names the views that did not follow the replacement.
/// </param>
public sealed record WorkspaceEditApplyResult(
	WorkspaceEditPlan Plan,
	DocumentReplacementResult? Replacement,
	IReadOnlyDictionary<string, WorkspaceDocumentViewSynchronizationResult> ViewSynchronization
);
