using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.Reloading;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Creates the hooks that let a <see cref="FileReloadCoordinator{TDecision}"/> reload the documents of a view coordinator.</summary>
public static class WorkspaceViewReloadHooksFactory
{
	/// <summary>Creates hooks that reload a changed file through the coordinator, so every view of its document follows the disk.</summary>
	/// <param name="coordinator">The coordinator whose documents and views the reloads keep in step.</param>
	/// <param name="dispatcher">
	/// The dispatcher of the thread that owns the views, which is the one the coordinator was constructed with. The hooks start
	/// <paramref name="decideConflict"/> and <paramref name="reportFailure"/> on it, so each may show a dialog.
	/// </param>
	/// <param name="decideConflict">
	/// Answers how to resolve a file that changed on disk while its document had unsaved changes, or <see langword="null"/> to keep the conflict,
	/// which <paramref name="reportFailure"/> then receives. It starts on the dispatcher thread.
	/// </param>
	/// <param name="reportFailure">
	/// Receives a reload that failed and a conflict that stayed, or <see langword="null"/> to report nothing. It starts on the dispatcher thread.
	/// </param>
	/// <returns>The hooks, which a host decorates to observe a reload or a resolution, for example to move a change-marker baseline.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="coordinator"/>, <paramref name="dispatcher"/> or <paramref name="decideConflict"/> is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// <para>
	/// A reload first has the views publish the edits they hold back (<see cref="IWorkspaceViewCoordinator.PublishPendingEditsAsync"/>), so the store
	/// compares the file with the text the user sees and reports a changed file under unsaved text as a conflict. A reload that views blocked
	/// reaches no store, so it is reported as <see cref="WorkspaceDocumentReloadOutcome.OperationInProgress"/> with the current snapshot.
	/// </para>
	/// <para>
	/// A resolution counts as resolved once the store applied it, even when a view stayed unsynchronized: the coordinator keeps that view marked,
	/// and the view blocks the operations that would lose its state until a later refresh succeeds.
	/// </para>
	/// <para>
	/// The reload and the resolution touch a view only through the coordinator's own dispatch, so they need no marshaling here.
	/// </para>
	/// </remarks>
	public static FileReloadHooks<WorkspaceDocumentConflictResolutionMode?> Create(
		IWorkspaceViewCoordinator coordinator,
		IUiDispatcher dispatcher,
		Func<WorkspaceDocumentReloadResult, CancellationToken, Task<WorkspaceDocumentConflictResolutionMode?>> decideConflict,
		Func<WorkspaceDocumentReloadResult, CancellationToken, Task>? reportFailure = null
	)
	{
		ArgumentNullException.ThrowIfNull(coordinator);
		ArgumentNullException.ThrowIfNull(dispatcher);
		ArgumentNullException.ThrowIfNull(decideConflict);

		return new FileReloadHooks<WorkspaceDocumentConflictResolutionMode?>
		{
			ReloadDocument = (filePath, cancellationToken) => ReloadAsync(coordinator, filePath, cancellationToken),
			DecideReload = (conflict, cancellationToken) => StartOnDispatcherAsync(dispatcher, () => decideConflict(conflict, cancellationToken)),
			ResolveConflict = (conflict, mode, cancellationToken) => ResolveAsync(coordinator, conflict, mode, cancellationToken),

			ReportReloadFailure = reportFailure is null
				? null
				: (result, cancellationToken) =>
					StartOnDispatcherAsync(
						dispatcher,

						async () =>
						{
							await reportFailure(result, cancellationToken).ConfigureAwait(false);
							return true;
						}
					),
		};
	}

	private static async Task<WorkspaceDocumentReloadResult> ReloadAsync(
		IWorkspaceViewCoordinator coordinator,
		string filePath,
		CancellationToken cancellationToken
	)
	{
		await coordinator.PublishPendingEditsAsync(filePath, cancellationToken).ConfigureAwait(false);

		// The reload coordinator skips a path the store does not track, so this answers a document closed between the two checks.
		if (!coordinator.Documents.TryGetSnapshot(filePath, out WorkspaceDocumentSnapshot? snapshot))
		{
			return new WorkspaceDocumentReloadResult(
				WorkspaceDocumentReloadOutcome.DocumentNotFound,
				new WorkspaceDocumentRequestIdentity(default, filePath, 0),
				null
			);
		}

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult> reload = await coordinator
			.ReloadAsync(new WorkspaceDocumentReloadRequest(snapshot.Identity), cancellationToken)
			.ConfigureAwait(false);

		return reload.StoreResult
			?? new WorkspaceDocumentReloadResult(WorkspaceDocumentReloadOutcome.OperationInProgress, snapshot.Identity, reload.BlockedSnapshot);
	}

	private static async Task<WorkspaceDocumentConflictResolutionResult?> ResolveAsync(
		IWorkspaceViewCoordinator coordinator,
		WorkspaceDocumentReloadResult conflict,
		WorkspaceDocumentConflictResolutionMode? mode,
		CancellationToken cancellationToken
	)
	{
		if (mode is not { } resolution || conflict.Snapshot is not { } snapshot || conflict.ObservedOnDiskStamp is not { } observed)
			return null;

		WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentConflictResolutionResult> resolved = await coordinator
			.ResolveExternalConflictAsync(new WorkspaceDocumentConflictResolutionRequest(snapshot.Identity, observed, resolution), cancellationToken)
			.ConfigureAwait(false);

		return resolved.StoreResult;
	}

	// The hook starts inside the dispatched action, so its code before the first await may touch the UI; its task is awaited off the dispatcher
	// thread, so a prompt that stays open does not hold the dispatcher.
	private static async Task<TResult> StartOnDispatcherAsync<TResult>(IUiDispatcher dispatcher, Func<Task<TResult>> hook)
	{
		Task<TResult>? started = null;

		await dispatcher.InvokeAsync(() => started = hook()).ConfigureAwait(false);

		return await (started ?? throw new InvalidOperationException("The dispatcher completed the call without running the hook.")).ConfigureAwait(
			false
		);
	}
}
