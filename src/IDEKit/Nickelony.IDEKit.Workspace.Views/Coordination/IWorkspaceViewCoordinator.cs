using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Coordinates workspace document authority with registered views.</summary>
/// <remarks>
/// Operations that coordinate
/// views report the document store's result and the view-synchronization result separately: the store result describes the document,
/// and <see cref="WorkspaceDocumentViewSynchronizationResult"/> reports whether attached views blocked the operation or stayed unsynchronized.
/// The <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/WorkspaceViewsGuide.md">workspace views guide</see> owns the result
/// shape and the blocking rule of each operation.
/// </remarks>
public interface IWorkspaceViewCoordinator : IAsyncDisposable
{
	/// <summary>Opens a document through the workspace authority without attaching a view.</summary>
	/// <param name="filePath">
	/// The path to open; a <see langword="null"/>, blank, or unnormalizable path
	/// reports <see cref="WorkspaceViewCoordinatorOpenOutcome.InvalidPath"/>.
	/// </param>
	/// <param name="options">The encoding and new-file format defaults for the load.</param>
	/// <param name="cancellationToken">A token that can cancel the load.</param>
	/// <returns>The open outcome with a snapshot for a successful or already-open document.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The coordinator has been stopped or disposed.</exception>
	/// <remarks>
	/// No view is attached, so the view-specific outcomes and view-attachment
	/// failures never occur; the remaining outcomes mirror the store's open outcomes with the document snapshot, and the wrapped store
	/// outcome is available through <see cref="WorkspaceViewCoordinatorOpenResult.StoreResult"/>. Missing options are an argument error that throws
	/// from the returned task instead of being reported as an open outcome, matching the store.
	/// </remarks>
	Task<WorkspaceViewCoordinatorOpenResult> OpenAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		CancellationToken cancellationToken = default
	);

	/// <summary>The read-side view of the document authority this coordinator coordinates.</summary>
	/// <remarks>
	/// Read-only consumers query tracked documents through this reader, which is the coordinator's underlying store.
	/// </remarks>
	IWorkspaceDocumentReader Documents { get; }

	/// <summary>Raised when a document gains its first attached view, changes its text or its id, or loses its last attached view.</summary>
	/// <remarks>
	/// <para>
	/// The coordinator raises the changes on its dispatcher,
	/// one at a time and in the order the operations made them. Each document therefore reports one <see cref="AttachedDocumentChangeKind.Opened"/>,
	/// then its content changes and renames, then one <see cref="AttachedDocumentChangeKind.Closed"/>; a delete and a stop close the documents
	/// of the views they close. When the dispatcher serializes its actions, an operation raises its changes before it completes, unless it runs
	/// inside a handler; otherwise, the raise that is running delivers them, possibly after the operation completed.
	/// </para>
	/// <para>
	/// A change that a handler causes is raised after that handler returns. A handler fault never changes the outcome of an operation
	/// and never costs another handler a change: the coordinator posts the fault to the dispatcher, where it is rethrown for the toolkit's
	/// unhandled-exception handler.
	/// </para>
	/// </remarks>
	event EventHandler<AttachedDocumentChangedEventArgs>? AttachedDocumentChanged;

	/// <summary>The newest snapshot of each document that has at least one attached view.</summary>
	/// <remarks>
	/// A host that mirrors the documents, such as to a search index, subscribes to <see cref="AttachedDocumentChanged"/> and then reads this list
	/// to learn the documents that were already open. The snapshots include the changes the event has not raised yet, so a consumer that subscribes
	/// and then reads them can still receive the open or a change of a document it read; a change the snapshot does not hold yet carries
	/// a newer version.
	/// </remarks>
	IReadOnlyList<WorkspaceDocumentSnapshot> AttachedDocuments { get; }

	/// <summary>Opens a document and attaches a view to it.</summary>
	/// <param name="filePath">The path to open.</param>
	/// <param name="options">The encoding and new-file format defaults for the load.</param>
	/// <param name="view">The view to attach to the opened document.</param>
	/// <param name="cancellationToken">A token that can cancel the open and attachment.</param>
	/// <returns>The open outcome; a failed attachment carries the loaded snapshot and the attachment failure.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="view"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The coordinator has been stopped or disposed.</exception>
	/// <remarks>
	/// <para>
	/// A view that is already registered is reported as <see cref="WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen"/> without a second attachment,
	/// and the result carries the current snapshot of the document the view is attached to (or <see langword="null"/> when that document
	/// is untracked).
	/// </para>
	/// <para>
	/// A view whose own <see cref="IWorkspaceDocumentView.Open"/>
	/// returns <see cref="WorkspaceDocumentViewOpenOutcome.AlreadyOpen"/> is reported as <see cref="WorkspaceViewCoordinatorOpenOutcome.ViewInUse"/>
	/// and stays untouched, unless a concurrent open registered the same instance with this coordinator, in which case
	/// the open is a no-op reported as <see cref="WorkspaceViewCoordinatorOpenOutcome.AlreadyOpen"/>. A different view that duplicates a registered
	/// view id is detached again and reported as <see cref="WorkspaceViewCoordinatorOpenOutcome.ViewInUse"/>; when the duplicate is detected
	/// before the document is loaded, the result carries no snapshot.
	/// </para>
	/// <para>
	/// A view that already has a document, pending edits,
	/// or a conflict is reported as <see cref="WorkspaceViewCoordinatorOpenOutcome.ViewUnavailable"/>; when the view cannot report its id or state
	/// because a member threw, the result failure carries that exception.
	/// </para>
	/// <para>
	/// Missing options are an argument error that throws synchronously,
	/// as a missing view does, instead of being reported as <see cref="WorkspaceViewCoordinatorOpenOutcome.OpenFailed"/>. Every other unexpected
	/// exception from the open or attach phase is reported as <see cref="WorkspaceViewCoordinatorOpenOutcome.OpenFailed"/> without a snapshot.
	/// </para>
	/// </remarks>
	Task<WorkspaceViewCoordinatorOpenResult> OpenWithViewAsync(
		string? filePath,
		WorkspaceDocumentOpenOptions options,
		IWorkspaceDocumentView view,
		CancellationToken cancellationToken = default
	);

	/// <summary>Resolves an external change conflict for a tracked document and refreshes attached views when possible.</summary>
	/// <param name="request">The conflict-resolution request.</param>
	/// <param name="cancellationToken">A token that can cancel the resolution.</param>
	/// <returns>The resolution outcome with the view-synchronization result and the current snapshot.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The coordinator has been stopped or disposed.</exception>
	/// <remarks>
	/// A view with pending edits or a conflict blocks the resolution, because the resolution replaces the state the view holds.
	/// A view whose only outstanding state is a prior synchronization failure does not block; the post-resolution refresh retries it and reports
	/// <see cref="WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized"/> when it fails again.
	/// </remarks>
	Task<WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentConflictResolutionResult>> ResolveExternalConflictAsync(
		WorkspaceDocumentConflictResolutionRequest request,
		CancellationToken cancellationToken = default
	);

	/// <summary>
	/// Applies a replacement to a workspace document without writing to disk, unless a view of the document holds edits it would overwrite.
	/// </summary>
	/// <param name="request">The replacement request.</param>
	/// <param name="cancellationToken">A token that can cancel the replacement before it reaches the store.</param>
	/// <returns>
	/// The mutation outcome with the view-synchronization result and the current snapshot, or a blocked result that names the views that hold edits.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="OperationCanceledException">
	/// <paramref name="cancellationToken"/> was canceled before the replacement reached the store.
	/// </exception>
	/// <exception cref="ObjectDisposedException">
	/// The coordinator or its store has been stopped or disposed, before the store changed the document.
	/// </exception>
	/// <remarks>
	/// <para>
	/// A view of the document that holds pending edits or a conflict, or whose state probe throws, blocks the replacement: the store is not reached,
	/// and the issues name each such view with the exception of a throwing probe. A view whose only problem is an earlier failed synchronization
	/// does not block, and the replacement refreshes it.
	/// </para>
	/// <para>
	/// The check, the store replacement and the refresh of the views run
	/// in one action of the dispatcher, so when the dispatcher runs the views' own thread, typing cannot land between the check and the replacement.
	/// A refresh that fails or leaves a view stale is reported as <see cref="WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized"/>.
	/// Once the store changed the document, the call returns its result, even when the dispatcher faults afterwards. A view's own publication
	/// through <see cref="IWorkspaceDocumentView.ApplyRequested"/> does not take this path and is never blocked.
	/// </para>
	/// </remarks>
	Task<WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentMutationResult>> ReplaceAsync(
		WorkspaceDocumentReplaceRequest request,
		CancellationToken cancellationToken = default
	);

	/// <summary>Discards unsaved logical changes and asks attached views to acknowledge the restored snapshot.</summary>
	/// <param name="request">The discard request.</param>
	/// <param name="cancellationToken">A token that can cancel the discard before it reaches the store.</param>
	/// <returns>The mutation outcome with the view-synchronization result and the restored snapshot.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="OperationCanceledException">
	/// <paramref name="cancellationToken"/> was canceled before the discard reached the store.
	/// </exception>
	/// <exception cref="ObjectDisposedException">The coordinator has been stopped or disposed.</exception>
	/// <remarks>
	/// Discard deliberately overrides attached view state, including pending edits and conflicts, because the caller already decided
	/// that the changes are abandoned. Every attached view is asked to acknowledge the restored snapshot, which clears its pending state;
	/// a view that cannot acknowledge is retained as unsynchronized view state and reported on the result.
	/// </remarks>
	Task<WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentMutationResult>> DiscardAsync(
		WorkspaceDocumentDiscardRequest request,
		CancellationToken cancellationToken = default
	);

	/// <summary>Renames a tracked document and updates attached views.</summary>
	/// <param name="request">The rename request.</param>
	/// <param name="cancellationToken">A token that can cancel the move.</param>
	/// <returns>The rename outcome with the view-synchronization result and the current snapshot.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The coordinator has been stopped or disposed.</exception>
	/// <remarks>
	/// Attached view state does not block the operation: the move cannot lose view edits, and the identity change is acknowledged afterwards.
	/// A view that cannot accept the new identity is reported through <see cref="WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized"/>
	/// on the result.
	/// </remarks>
	Task<WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentRenameResult>> RenameAsync(
		WorkspaceDocumentRenameRequest request,
		CancellationToken cancellationToken = default
	);

	/// <summary>Saves a tracked document at a destination path and updates attached views.</summary>
	/// <param name="request">The save-as request.</param>
	/// <param name="cancellationToken">A token that can cancel the write.</param>
	/// <returns>The save-as outcome with the view-synchronization result and the current snapshot.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The coordinator has been stopped or disposed.</exception>
	/// <remarks>
	/// The source file is retained. Attached view state does not block the operation:
	/// the write cannot lose view edits, and the identity change is acknowledged afterwards. A view that cannot accept the new identity is reported
	/// through <see cref="WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized"/> on the result.
	/// </remarks>
	Task<WorkspaceViewCoordinatorUnblockedResult<WorkspaceDocumentSaveAsResult>> SaveAsAsync(
		WorkspaceDocumentSaveAsRequest request,
		CancellationToken cancellationToken = default
	);

	/// <summary>Deletes a tracked document and updates attached views.</summary>
	/// <param name="request">The delete request.</param>
	/// <param name="cancellationToken">A token that can cancel the delete.</param>
	/// <returns>The delete outcome with the view-synchronization result and the pre-removal snapshot.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The coordinator has been stopped or disposed.</exception>
	/// <remarks>
	/// A view with pending edits, a conflict, or an unsynchronized failure blocks the delete, because the delete destroys state the view still holds.
	/// Attached views that implement <see cref="IWorkspaceDocumentDeleteLeaseView"/> must accept its delete lease before deletion;
	/// successful deletion then closes and detaches the attached views.
	/// </remarks>
	Task<WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentDeleteResult>> DeleteAsync(
		WorkspaceDocumentDeleteRequest request,
		CancellationToken cancellationToken = default
	);

	/// <summary>Renames a directory and updates the identities of tracked documents below it.</summary>
	/// <param name="request">The directory-rename request.</param>
	/// <param name="cancellationToken">A token that can cancel the move.</param>
	/// <returns>The rename outcome with the view-synchronization result and the current snapshots.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The coordinator has been stopped or disposed.</exception>
	/// <remarks>
	/// Attached views are leased before the move and acknowledge their new identities afterward; their state does not block the move, but a view
	/// that cannot enter its delete lease does. A view that attached while the move was in flight is acknowledged as well, matched to its moved
	/// document by its document key.
	/// </remarks>
	Task<WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryRenameResult>> RenameDirectoryAsync(
		WorkspaceDocumentDirectoryRenameRequest request,
		CancellationToken cancellationToken = default
	);

	/// <summary>Deletes a directory and its tracked documents.</summary>
	/// <param name="request">The directory-delete request.</param>
	/// <param name="cancellationToken">A token that can cancel the delete.</param>
	/// <returns>The delete outcome with the view-synchronization result and the current snapshots.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The coordinator has been stopped or disposed.</exception>
	/// <remarks>
	/// A view with pending edits, a conflict, or an unsynchronized failure blocks the delete. Attached views are leased before recursive deletion
	/// and closed afterward.
	/// </remarks>
	Task<WorkspaceViewCoordinatorDirectoryResult<WorkspaceDocumentDirectoryDeleteResult>> DeleteDirectoryAsync(
		WorkspaceDocumentDirectoryDeleteRequest request,
		CancellationToken cancellationToken = default
	);

	/// <summary>Commits a tracked document to disk and refreshes attached views.</summary>
	/// <param name="request">The commit request.</param>
	/// <param name="cancellationToken">A token that can cancel the commit.</param>
	/// <returns>The commit outcome with the view-synchronization result and the current snapshot.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The coordinator has been stopped or disposed.</exception>
	/// <remarks>
	/// Views with pending edits or conflicts block the commit and report <see cref="WorkspaceDocumentViewSynchronizationOutcome.Blocked"/>.
	/// A view whose only outstanding state is a prior synchronization failure does not block the commit: the coordinator performs the commit,
	/// retries the view synchronization, and reports <see cref="WorkspaceDocumentViewSynchronizationOutcome.Unsynchronized"/> when that retry
	/// fails again.
	/// </remarks>
	Task<WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentCommitResult>> CommitAsync(
		WorkspaceDocumentCommitRequest request,
		CancellationToken cancellationToken = default
	);

	/// <summary>Reloads a tracked document from disk and refreshes attached views when content changes.</summary>
	/// <param name="request">The reload request.</param>
	/// <param name="cancellationToken">A token that can cancel the reload.</param>
	/// <returns>The reload outcome with the view-synchronization result and the current snapshot.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The coordinator has been stopped or disposed.</exception>
	/// <remarks>
	/// Blocking view state is reported before the reload: a view with pending edits or a conflict can lose that state when the document content
	/// is replaced from disk, so it blocks the operation. A view whose only outstanding state is a prior synchronization failure does not block:
	/// the reload retries the failed synchronization. Dirty documents are handled by the store as conflicts and are not overwritten.
	/// </remarks>
	Task<WorkspaceViewCoordinatorDocumentResult<WorkspaceDocumentReloadResult>> ReloadAsync(
		WorkspaceDocumentReloadRequest request,
		CancellationToken cancellationToken = default
	);

	/// <summary>Has every attached view of a document publish the edits it holds back, and waits until the store applied them.</summary>
	/// <param name="filePath">The path of the document whose views publish; any spelling of a tracked document's path reaches its views.</param>
	/// <param name="cancellationToken">A token that can cancel the wait for the publications.</param>
	/// <returns>
	/// A synchronized result with the snapshot that the final check of the views read when no view holds pending edits,
	/// a conflict or a failed synchronization afterwards; otherwise, a blocked result that names the views that do.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ObjectDisposedException">The coordinator has been stopped or disposed.</exception>
	/// <remarks>
	/// A host calls it before an operation that must see the text the user sees, because a store operation on the document leaves a view's
	/// held-back edits behind it: a save would write older text, and a later publication would overwrite an edit of a language server. Only a view
	/// that implements <see cref="IWorkspaceDocumentPublishingView"/> can publish; another view with pending edits is reported. The snapshot is read
	/// in the same dispatched action as the final check, so a publication that typing scheduled cannot land between them.
	/// </remarks>
	Task<WorkspaceViewCoordinatorPublicationResult> PublishPendingEditsAsync(string filePath, CancellationToken cancellationToken = default);

	/// <summary>Detaches a closed view.</summary>
	/// <param name="view">The view to detach; a view that is not attached is ignored.</param>
	/// <exception cref="ArgumentNullException"><paramref name="view"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// This member removes the registration and event subscription; it does not call <see cref="IWorkspaceDocumentView.Close"/>.
	/// The last view of a document closes the document for <see cref="AttachedDocumentChanged"/>. The subscription is removed on the calling thread;
	/// a throwing event accessor is ignored (the view is already detached, so later raises are ignored as well).
	/// </remarks>
	void DetachView(IWorkspaceDocumentView view);

	/// <summary>Stops new operations, waits for active operations to finish, and closes registered views.</summary>
	/// <returns>A task that completes when the coordinator has stopped; repeated calls observe the same completion.</returns>
	/// <exception cref="InvalidOperationException">
	/// The call runs inside an active operation, such as a view member, a dispatch action, the dispatcher itself, or a file-system callback
	/// the operation reaches.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Repeated calls return the same completion task. <c>DisposeAsync</c> delegates to this member.
	/// </para>
	/// <para>
	/// The completion waits for active operations to finish before the dispatcher closes the registered views.
	/// A call from inside an active operation, such as a view member or a dispatch action, throws, because the stop would wait for that operation,
	/// which cannot finish while it waits for the stop. The check follows the asynchronous flow of the operation, so work that such a member starts,
	/// a <see cref="Task.Run(Action)"/> call included, throws as well, and so does code that runs on the member's thread while the member runs,
	/// a nested message loop included. A call after the operation has finished is not affected.
	/// </para>
	/// <para>
	/// A dispatcher that faults before it runs its action (a dispatcher contract violation) faults the stop completion: every later caller observes
	/// the same fault, the registered views may stay unclosed and subscribed, and the coordinator stays stopped.
	/// </para>
	/// </remarks>
	Task StopAsync();
}
