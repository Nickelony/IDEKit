using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Reloading;

/// <summary>Supplies the asynchronous hooks of a <see cref="FileReloadCoordinator{TDecision}"/>.</summary>
/// <typeparam name="TDecision">
/// The host-neutral value produced by <see cref="DecideReload"/> and passed back to <see cref="ResolveConflict"/>; one hooks value serves
/// one decision type.
/// </typeparam>
public sealed class FileReloadHooks<TDecision>
{
	/// <summary>Reloads a tracked document and returns its result.</summary>
	/// <remarks>
	/// It runs on the thread that started the pass until a hook of the pass completes asynchronously, and on the thread
	/// pool after that, so it reaches a UI member only through a dispatcher.
	/// </remarks>
	public required Func<string, CancellationToken, Task<WorkspaceDocumentReloadResult>> ReloadDocument { get; init; }

	/// <summary>
	/// Asks the host for a reload decision for a conflicted document; the argument carries the conflict outcome, the current snapshot,
	/// and the identity, and the callback may answer interactively or procedurally.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Required when <see cref="ResolveConflict"/> is supplied, because the resolver receives only the decision this callback returns.
	/// </para>
	/// <para>
	/// It runs where <see cref="ReloadDocument"/> runs, so a callback that shows a dialog moves
	/// to the UI thread itself; the hooks that <c>WorkspaceViewReloadHooksFactory.Create</c> returns do so through the dispatcher.
	/// </para>
	/// </remarks>
	public Func<WorkspaceDocumentReloadResult, CancellationToken, Task<TDecision>>? DecideReload { get; init; }

	/// <summary>Resolves an external file conflict with the host decision, or <see langword="null"/> to report conflicts without prompting.</summary>
	/// <remarks>
	/// It runs where <see cref="ReloadDocument"/> runs.
	/// </remarks>
	public Func<
		WorkspaceDocumentReloadResult,
		TDecision,
		CancellationToken,
		Task<WorkspaceDocumentConflictResolutionResult?>
	>? ResolveConflict { get; init; }

	/// <summary>Reports a failed reload result, or <see langword="null"/> to report nothing.</summary>
	/// <remarks>
	/// <para>
	/// It is not called for a <see cref="WorkspaceDocumentReloadOutcome.Reloaded"/>, <see cref="WorkspaceDocumentReloadOutcome.Unchanged"/>,
	/// or <see cref="WorkspaceDocumentReloadOutcome.Canceled"/> result, nor for an external conflict that the resolver resolved. A failed resolution
	/// is reported as the original conflict result, and a conflict without a resolver is reported without prompting.
	/// </para>
	/// <para>
	/// It runs where <see cref="ReloadDocument"/> runs.
	/// </para>
	/// </remarks>
	public Func<WorkspaceDocumentReloadResult, CancellationToken, Task>? ReportReloadFailure { get; init; }
}
