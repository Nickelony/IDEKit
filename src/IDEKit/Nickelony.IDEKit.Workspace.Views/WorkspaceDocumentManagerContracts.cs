using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Coordinates workspace document authority with registered views.
/// </summary>
/// <remarks>
/// <para>
/// The manager keeps view bindings and document mutations coordinated but owns no thread or message
/// loop: store mutations run on the caller's context, while view operations are dispatched through the
/// delegate supplied to the implementation, which must run the supplied action before its returned task
/// completes (that completion may itself be asynchronous) and may be entered from arbitrary threads,
/// concurrently, and reentrantly when a view raises <see cref="IWorkspaceDocumentView.ApplyRequested"/>.
/// A delegate whose views are not thread-safe or reentrancy-safe must serialize or queue the actions
/// itself. The package README describes the dispatch contract in full.
/// </para>
/// <list type="bullet">
/// <item>Operations that coordinate views report the document-authority outcome and the
/// view-synchronization outcome separately: the wrapped store result describes the document, while
/// <see cref="WorkspaceDocumentViewSynchronizationResult"/> reports whether attached views blocked the
/// operation or stayed unsynchronized, with the affected views and any failure they reported. The store
/// contracts never contain view state. The view-driven mutations (<see cref="ReplaceAsync"/> and
/// <see cref="DiscardAsync"/>) report the same composed family with the store result always populated,
/// because those operations never block on attached views.</item>
/// <item>Blocking is decided per operation, and the package README states each operation's blocking
/// rule in full. The operations that destroy the document behind a view report
/// <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/> while a view holds state the
/// destruction would lose; the recovery operations block only on pending edits and conflicts and ignore
/// a prior synchronization failure so they can be retried; discard overrides attached view state. The
/// identity-only operations (<see cref="RenameAsync"/>, <see cref="SaveAsAsync"/>, and
/// <see cref="RenameDirectoryAsync"/>) do not block on view state, though a directory move still
/// reports <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/> when a view cannot enter
/// its delete guard.</item>
/// <item>The post-operation report covers view state, not only failures: a view that has pending edits
/// or a conflict when the report is composed - including one that acquired them while the operation
/// ran - is reported as unsynchronized.</item>
/// <item>Members that return a task validate their own arguments before any work is dispatched: a
/// <see langword="null"/> request or view throws from the call itself instead of faulting the returned
/// task. A request is otherwise forwarded unchanged, so the store's argument errors (an empty document
/// id, a non-creatable open format, an undefined conflict-resolution choice) surface as failures of the
/// returned task.</item>
/// <item>The manager never dispatches the <see cref="IWorkspaceDocumentView.ApplyRequested"/>
/// subscription: the add runs on the thread that resumes the attach after its dispatched action
/// completes, a direct <see cref="UnregisterOpenView"/> removes the handler on the calling thread, and
/// manager-initiated removals run on the dispatch thread. Event accessors must be usable from any
/// thread; every other view member invocation is dispatched through the delegate. After the add, the
/// manager re-checks the registration under the state lock and removes the handler again when a
/// concurrent <see cref="UnregisterOpenView"/> has already unregistered the view, so the subscription
/// never outlives its registration; the open then reports
/// <see cref="WorkspaceDocumentManagerOpenOutcome.AlreadyOpen"/>. Event-driven applies have no
/// synchronous completion channel: the publishing view learns the outcome through
/// <see cref="IWorkspaceDocumentView.AcknowledgeApply"/> (a rejected replacement is acknowledged with
/// the current state), while a failure thrown by the apply path is retained as unsynchronized view
/// state that blocks the document-destroying operations until a successful refresh clears it, reported
/// with <see cref="WorkspaceViewOperationFailureCodes.ViewApplyFailed"/> and the exception detail.</item>
/// </list>
/// </remarks>
public interface IWorkspaceDocumentManager : IAsyncDisposable
{
	/// <summary>
	/// Opens a document through the workspace authority without attaching a view.
	/// </summary>
	/// <remarks>
	/// No view is attached, so the view-specific outcomes and view-attachment failures never occur;
	/// the remaining outcomes mirror the store's open outcomes with the document snapshot, and the
	/// wrapped store outcome is available through
	/// <see cref="WorkspaceDocumentManagerOpenResult.StoreResult"/>. Invalid options are an argument
	/// error that throws from the returned task instead of being reported as an open outcome, matching
	/// the store.
	/// </remarks>
	/// <param name="filePath">The path to open; a <see langword="null"/>, blank, or unnormalizable path reports <see cref="WorkspaceDocumentManagerOpenOutcome.InvalidPath"/>.</param>
	/// <param name="options">The encoding and new-file format defaults for the load.</param>
	/// <param name="cancellationToken">A token that can cancel the load.</param>
	/// <returns>The open outcome with a snapshot for a successful or already-open document.</returns>
	/// <exception cref="ArgumentException"><paramref name="options"/> uses a file format that cannot be encoded.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/> uses an undefined text encoding.</exception>
	/// <exception cref="ObjectDisposedException">The manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerOpenResult> OpenAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the read-side view of the document authority this manager coordinates.
	/// </summary>
	/// <remarks>
	/// Read-only consumers use this accessor instead of a forwarding member per reader operation;
	/// the returned reader is the manager's underlying store.
	/// </remarks>
	IWorkspaceDocumentReader Documents { get; }

	/// <summary>
	/// Opens a document and attaches a view to it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The view is attached through the dispatch delegate. A view that is already registered is
	/// reported as <see cref="WorkspaceDocumentManagerOpenOutcome.AlreadyOpen"/> without a second
	/// attachment, and the result carries the current snapshot of the document the view is attached
	/// to (or <see langword="null"/> when that document is no longer tracked).
	/// </para>
	/// <para>
	/// A view whose own <see cref="IWorkspaceDocumentView.Open"/> returns
	/// <see cref="WorkspaceDocumentViewOpenOutcome.AlreadyOpen"/> is reported as
	/// <see cref="WorkspaceDocumentManagerOpenOutcome.ViewInUse"/> and stays untouched, unless a
	/// concurrent open registered the same instance with this manager, in which case the open is a
	/// no-op reported as <see cref="WorkspaceDocumentManagerOpenOutcome.AlreadyOpen"/>. A different
	/// view that duplicates a registered view id is detached again and reported as
	/// <see cref="WorkspaceDocumentManagerOpenOutcome.ViewInUse"/>; when the duplicate is detected
	/// before the document is loaded, the result carries no snapshot. A view that already has a
	/// document, pending edits, or a conflict is reported as
	/// <see cref="WorkspaceDocumentManagerOpenOutcome.ViewUnavailable"/>; when the view cannot report
	/// its id or state because a member threw, the result failure carries that exception.
	/// </para>
	/// <para>
	/// Invalid options are an argument error that throws from the returned task instead of being
	/// reported as <see cref="WorkspaceDocumentManagerOpenOutcome.OpenFailed"/>, matching
	/// <see cref="OpenAsync"/> and the store. Every other unexpected exception from the open or
	/// attach phase is reported as <see cref="WorkspaceDocumentManagerOpenOutcome.OpenFailed"/>
	/// without a snapshot.
	/// </para>
	/// </remarks>
	/// <param name="filePath">The path to open.</param>
	/// <param name="options">The encoding and new-file format defaults for the load.</param>
	/// <param name="view">The view to attach to the opened document.</param>
	/// <param name="cancellationToken">A token that can cancel the open and attachment.</param>
	/// <returns>The open outcome; a failed attachment carries the loaded snapshot and the attachment failure.</returns>
	/// <exception cref="ArgumentException"><paramref name="options"/> uses a file format that cannot be encoded.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="view"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/> uses an undefined text encoding.</exception>
	/// <exception cref="ObjectDisposedException">The manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerOpenResult> OpenWithViewAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		IWorkspaceDocumentView view,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Resolves an external change conflict for a tracked document and refreshes attached views when possible.
	/// </summary>
	/// <remarks>
	/// A view with pending edits or a conflict blocks the resolution, because the resolution replaces
	/// the state the view holds. A view whose only outstanding state is a prior synchronization
	/// failure does not block; the post-resolution refresh retries it and reports
	/// <see cref="WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized"/> when it fails again.
	/// </remarks>
	/// <param name="request">The conflict-resolution request.</param>
	/// <param name="cancellationToken">A token that can cancel the resolution.</param>
	/// <returns>The resolution outcome with the view-synchronization result and the current snapshot.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerConflictResolutionResult> ResolveExternalConflictAsync(
		WorkspaceDocumentConflictResolutionRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies a replacement to a workspace document without writing to disk.
	/// </summary>
	/// <remarks>
	/// The manager performs the mutation and asks attached views to synchronize through the dispatch
	/// delegate. No view publishes the replacement, so every attached view is treated as a peer and
	/// refreshes when its recorded state is older or its synchronization failed earlier. A view that
	/// cannot synchronize is retained as unsynchronized view state and reported on the result.
	/// </remarks>
	/// <param name="request">The replacement request.</param>
	/// <returns>The mutation outcome with the view-synchronization result and the current snapshot.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerMutationResult> ReplaceAsync(WorkspaceDocumentReplaceRequest request);

	/// <summary>
	/// Discards unsaved logical changes and asks attached views to acknowledge the restored snapshot.
	/// </summary>
	/// <remarks>
	/// Discard deliberately overrides attached view state, including pending edits and conflicts,
	/// because the caller already decided that the changes are abandoned. Every attached view is
	/// asked to acknowledge the restored snapshot, which clears its pending state; a view that cannot
	/// acknowledge is retained as unsynchronized view state and reported on the result.
	/// </remarks>
	/// <param name="request">The discard request.</param>
	/// <returns>The mutation outcome with the view-synchronization result and the restored snapshot.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerMutationResult> DiscardAsync(WorkspaceDocumentDiscardRequest request);

	/// <summary>
	/// Renames a tracked document and updates attached views.
	/// </summary>
	/// <remarks>
	/// Attached view state does not block the operation: the move cannot lose view edits, and the
	/// identity change is acknowledged afterwards. A view that cannot accept the new identity is
	/// reported through <see cref="WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized"/> on the
	/// result.
	/// </remarks>
	/// <param name="request">The rename request.</param>
	/// <param name="cancellationToken">A token that can cancel the move.</param>
	/// <returns>The rename outcome with the view-synchronization result and the current snapshot.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerRenameResult> RenameAsync(
		WorkspaceDocumentRenameRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Saves a tracked document at a destination path and updates attached views.
	/// </summary>
	/// <remarks>
	/// The source file is retained. Attached view state does not block the operation: the write cannot
	/// lose view edits, and the identity change is acknowledged afterwards. A view that cannot accept
	/// the new identity is reported through
	/// <see cref="WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized"/> on the result.
	/// </remarks>
	/// <param name="request">The save-as request.</param>
	/// <param name="cancellationToken">A token that can cancel the write.</param>
	/// <returns>The save-as outcome with the view-synchronization result and the current snapshot.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerSaveAsResult> SaveAsAsync(
		WorkspaceDocumentSaveAsRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes a tracked document and updates attached views.
	/// </summary>
	/// <remarks>Attached views must accept a delete guard before deletion; successful deletion then closes and unregisters them.</remarks>
	/// <param name="request">The delete request.</param>
	/// <param name="cancellationToken">A token that can cancel the delete.</param>
	/// <returns>The delete outcome with the view-synchronization result and the pre-removal snapshot.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerDeleteResult> DeleteAsync(
		WorkspaceDocumentDeleteRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Renames a directory and updates the identities of tracked documents below it.
	/// </summary>
	/// <remarks>
	/// Attached views are guarded before the move and acknowledge their new identities afterward; their
	/// state does not block the move, but a view that cannot enter its delete guard does. A view that
	/// attached while the move was in flight is acknowledged as well, matched to its moved document by
	/// its document key.
	/// </remarks>
	/// <param name="request">The directory-rename request.</param>
	/// <param name="cancellationToken">A token that can cancel the move.</param>
	/// <returns>The rename outcome with the view-synchronization result and the current snapshots.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerDirectoryRenameResult> RenameDirectoryAsync(
		WorkspaceDocumentDirectoryRenameRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes a directory and its tracked documents.
	/// </summary>
	/// <remarks>
	/// Attached views are guarded before recursive deletion and closed afterward.
	/// </remarks>
	/// <param name="request">The directory-delete request.</param>
	/// <param name="cancellationToken">A token that can cancel the delete.</param>
	/// <returns>The delete outcome with the view-synchronization result and the current snapshots.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerDirectoryDeleteResult> DeleteDirectoryAsync(
		WorkspaceDocumentDirectoryDeleteRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Commits a tracked document to disk and refreshes attached views.
	/// </summary>
	/// <remarks>
	/// Views with pending edits or conflicts block the commit and report
	/// <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>. A view whose only outstanding
	/// state is a prior synchronization failure does not block the commit: the manager performs the
	/// commit, retries the view synchronization, and reports
	/// <see cref="WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized"/> when that retry fails
	/// again.
	/// </remarks>
	/// <param name="request">The commit request.</param>
	/// <param name="cancellationToken">A token that can cancel the commit.</param>
	/// <returns>The commit outcome with the view-synchronization result and the current snapshot.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerCommitResult> CommitAsync(
		WorkspaceDocumentCommitRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Reloads a tracked document from disk and refreshes attached views when content changes.
	/// </summary>
	/// <remarks>
	/// Blocking view state is reported before the reload: a view with pending edits or a conflict can
	/// lose that state when the document content is replaced from disk, so it blocks the operation. A
	/// view whose only outstanding state is a prior synchronization failure does not block: the
	/// reload retries the failed synchronization. Dirty documents are handled by the store as
	/// conflicts and are not overwritten.
	/// </remarks>
	/// <param name="request">The reload request.</param>
	/// <param name="cancellationToken">A token that can cancel the reload.</param>
	/// <returns>The reload outcome with the view-synchronization result and the current snapshot.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The manager has been stopped or disposed.</exception>
	Task<WorkspaceDocumentManagerReloadResult> ReloadAsync(
		WorkspaceDocumentReloadRequest request,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Unregisters a view that is no longer open.
	/// </summary>
	/// <remarks>
	/// This member removes the binding and event subscription; it does not call
	/// <see cref="IWorkspaceDocumentView.Close"/>. The subscription is removed on the calling thread;
	/// a throwing event accessor is ignored (the view is already unregistered, so later raises are
	/// ignored as well).
	/// </remarks>
	/// <param name="view">The view to unregister; an unregistered view is ignored.</param>
	/// <exception cref="ArgumentNullException"><paramref name="view"/> is <see langword="null"/>.</exception>
	void UnregisterOpenView(IWorkspaceDocumentView view);

	/// <summary>
	/// Stops new operations, waits for active operations to finish, and closes registered views.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Repeated calls return the same completion task. <c>DisposeAsync</c> delegates to this member.
	/// </para>
	/// <para>
	/// The completion waits for active operations to finish before the dispatch delegate closes the
	/// registered views. Never call this member from inside a view member or dispatch action that
	/// belongs to an active operation: the stop waits for that operation, which cannot finish while
	/// its own thread is blocked on the stop.
	/// </para>
	/// <para>
	/// A delegate that faults before it runs its action (a delegate contract violation) faults the
	/// stop completion: every later caller observes the same fault, the registered views may stay
	/// unclosed and subscribed, and the manager stays stopped.
	/// </para>
	/// </remarks>
	/// <returns>A task that completes when the manager has stopped; repeated calls observe the same completion.</returns>
	Task StopAsync();
}
