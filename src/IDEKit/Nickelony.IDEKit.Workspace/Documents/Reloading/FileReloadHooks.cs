namespace Nickelony.IDEKit.Workspace.Documents.Reloading;

/// <summary>
/// Supplies the asynchronous hooks for a
/// <see cref="FileReloadCoordinator{TDecisionResult}.ProcessQueuedFilesAsync"/> pass.
/// </summary>
/// <typeparam name="TDecisionResult">
/// The host-neutral value produced by <see cref="DecideReload"/> and passed back to
/// <see cref="ResolveConflict"/>; one hooks value serves one decision type.
/// </typeparam>
/// <param name="DecideReload">
/// Asks the host for a reload decision for a conflicted document; the argument carries the conflict
/// outcome, the current snapshot, and the identity, and the callback may answer interactively or
/// procedurally. Required when <paramref name="ResolveConflict"/> is supplied, because a decision
/// without a resolver is discarded; otherwise, it may be <see langword="null"/>.
/// </param>
/// <param name="ReloadDocument">Reloads a tracked document and returns its result.</param>
/// <param name="ReportReloadFailure">
/// Reports a result that is neither <see cref="WorkspaceDocumentReloadOutcome.Reloaded"/> nor
/// <see cref="WorkspaceDocumentReloadOutcome.Unchanged"/> unless an external conflict was resolved
/// successfully or the result was <see cref="WorkspaceDocumentReloadOutcome.Canceled"/>; a failed
/// resolution is reported as the original conflict result, and a conflict without a resolver is
/// reported without prompting. May be <see langword="null"/>.
/// </param>
/// <param name="ResolveConflict">
/// Optionally resolves an external file conflict using the host decision result; when it is
/// <see langword="null"/>, conflicts are reported without prompting. May be
/// <see langword="null"/>.
/// </param>
public readonly record struct FileReloadHooks<TDecisionResult>(
	Func<WorkspaceDocumentReloadResult, CancellationToken, Task<TDecisionResult>>? DecideReload,
	Func<string, CancellationToken, Task<WorkspaceDocumentReloadResult>> ReloadDocument,
	Func<WorkspaceDocumentReloadResult, CancellationToken, Task>? ReportReloadFailure = null,
	Func<WorkspaceDocumentReloadResult, TDecisionResult, CancellationToken, Task<WorkspaceDocumentConflictResolutionResult?>>? ResolveConflict = null);
