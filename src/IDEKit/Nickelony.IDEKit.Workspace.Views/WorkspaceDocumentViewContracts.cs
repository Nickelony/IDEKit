using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Represents a document view of a workspace document, such as a text view, a preview, or another
/// document model.
/// </summary>
/// <remarks>
/// <para>
/// The manager invokes members of this interface through the dispatch delegate supplied to its
/// constructor, so view access is always dispatched. Only the <see cref="ApplyRequested"/> subscription
/// add runs on the resuming thread, and event accessors must be usable from any thread; when the delegate
/// runs actions inline, the apply pipeline can run synchronously inside a raise, so members must tolerate
/// reentrant calls from <see cref="ApplyRequested"/>.
/// </para>
/// <para>
/// Lifecycle: the manager attaches the view with <see cref="Open"/> and reports a rejected attach, and a
/// view may receive <see cref="Close"/> after a rejected attach, so closing must be safe for a view that
/// never attached. While attached, the view receives <see cref="Refresh"/> for unseen content or version
/// updates, <see cref="AcknowledgeIdentity"/> before an identity change, and
/// <see cref="AcknowledgeApply"/> when a mutation published by the view or a manager-initiated discard
/// becomes the document state. Delete operations and directory moves ask views that implement
/// <see cref="IWorkspaceDocumentDeleteGuardView"/> to enter and always release a delete guard.
/// </para>
/// <para>
/// A detached view has a <see langword="null"/> <see cref="DocumentKey"/>. A view with
/// <see cref="HasPendingEdits"/> or <see cref="HasConflict"/> blocks the operations that could lose that
/// state (delete, delete-directory, commit, reload, and conflict resolution); operations that only change
/// the document identity (rename, save-as, and directory rename) do not block on view state, and discard
/// deliberately overrides view state. The view adds only the interactive buffer and its relationship to
/// the document; the workspace document already owns the logical content, the persisted baseline, the
/// dirty flag, and the version pair.
/// </para>
/// </remarks>
public interface IWorkspaceDocumentView
{
	/// <summary>
	/// Raised when the view requests that edited content be published to the workspace document.
	/// </summary>
	/// <remarks>
	/// Raise the event with the view instance as the sender; the manager identifies the publishing
	/// view from it. The event request must identify the view's current document key, id, and
	/// version. The manager applies the replacement and acknowledges the outcome through
	/// <see cref="AcknowledgeApply"/>; a failure thrown by the apply path itself is recorded as
	/// unsynchronized view state instead.
	/// </remarks>
	event EventHandler<WorkspaceDocumentViewApplyRequestedEventArgs> ApplyRequested;

	/// <summary>
	/// Gets the stable identifier used to report and distinguish this view; a blank or whitespace id
	/// cannot be attached, and the value must not change while the view is registered.
	/// </summary>
	string ViewId { get; }

	/// <summary>Gets the current logical document identity, or <see langword="null"/> when detached.</summary>
	WorkspaceDocumentKey? DocumentKey { get; }

	/// <summary>Gets a value indicating whether the view has edits not published to the workspace document.</summary>
	bool HasPendingEdits { get; }

	/// <summary>Gets a value indicating whether the view cannot currently be synchronized without conflict resolution.</summary>
	bool HasConflict { get; }

	/// <summary>Attaches the view to a document snapshot and makes that snapshot its current content.</summary>
	/// <param name="snapshot">The workspace document snapshot to attach.</param>
	/// <returns>An open outcome; return <see cref="WorkspaceDocumentViewOpenOutcome.AlreadyOpen"/> when the view is already attached.</returns>
	WorkspaceDocumentViewOpenResult Open(WorkspaceDocumentSnapshot snapshot);

	/// <summary>Refreshes the view from a document snapshot.</summary>
	/// <remarks>The view may return <see cref="WorkspaceDocumentViewRefreshOutcome.MarkedStale"/> when it cannot apply the snapshot immediately.</remarks>
	/// <param name="snapshot">The workspace document snapshot to refresh from.</param>
	/// <returns>The refresh outcome.</returns>
	WorkspaceDocumentViewRefreshResult Refresh(WorkspaceDocumentSnapshot snapshot);

	/// <summary>Applies a document identity change, such as a rename or directory move, to the view.</summary>
	/// <remarks>
	/// The view should update its document id and content from
	/// <see cref="WorkspaceDocumentViewIdentityChange.Snapshot"/> before reporting success. A
	/// successful result also clears the manager's unsynchronized state for the view.
	/// </remarks>
	/// <param name="change">The identity change to apply.</param>
	/// <returns>The identity update outcome.</returns>
	WorkspaceDocumentViewIdentityResult AcknowledgeIdentity(WorkspaceDocumentViewIdentityChange change);

	/// <summary>Acknowledges the result of applying a logical document mutation to the view.</summary>
	/// <remarks>Used when a mutation published by the view, or a manager-initiated discard, becomes the document state. A non-applied acknowledgment leaves the view unsynchronized.</remarks>
	/// <param name="result">The mutation result the view should adopt.</param>
	/// <returns>The acknowledgment outcome.</returns>
	WorkspaceDocumentViewApplyResult AcknowledgeApply(WorkspaceDocumentMutationResult result);

	/// <summary>Closes the view and detaches it from its current document.</summary>
	/// <remarks>
	/// <para>Closing a view does not unregister it from the manager; the owner must call <see cref="IWorkspaceDocumentManager.UnregisterOpenView"/>.</para>
	/// <para>Closing must be idempotent: a delete or directory operation closes the views it captured
	/// even when the owner already unregistered them, and the owner may have closed a view itself, so an
	/// implementation must tolerate a repeat close without faulting or double-releasing resources.</para>
	/// </remarks>
	void Close();
}

/// <summary>
/// Represents a workspace view that can hold a delete guard for an in-flight delete or directory move.
/// </summary>
/// <remarks>
/// The manager enters the guard on every attached view that implements this interface before a file
/// delete, a directory move, or a directory delete, and releases it afterwards. A view that
/// implements only <see cref="IWorkspaceDocumentView"/> is skipped: it cannot hold state that one of
/// those operations would lose, so it has nothing to guard.
/// </remarks>
public interface IWorkspaceDocumentDeleteGuardView : IWorkspaceDocumentView
{
	/// <summary>Applies the view's delete guard for an in-flight file delete, directory delete, or directory move.</summary>
	/// <returns>The guard outcome; a view that cannot enter the guard reports <see cref="WorkspaceDocumentViewDeleteGuardOutcome.Failed"/>.</returns>
	WorkspaceDocumentViewDeleteGuardResult ApplyDeleteGuard();

	/// <summary>Releases the view's delete guard after the delete or directory operation completed or was abandoned.</summary>
	/// <remarks>Releasing a guard that is not applied is a no-op.</remarks>
	/// <returns>The guard outcome.</returns>
	WorkspaceDocumentViewDeleteGuardResult ReleaseDeleteGuard();
}

/// <summary>
/// Describes a workspace document identity change acknowledged by a view.
/// </summary>
/// <remarks>
/// <see cref="OldDocumentId"/> identifies the binding before a rename or directory move;
/// <see cref="Snapshot"/> carries the new path, content, version, and preserved document key.
/// </remarks>
/// <param name="OldDocumentKey">The document instance the view was bound to before the change.</param>
/// <param name="OldDocumentId">The normalized document id the view was bound to before the change.</param>
/// <param name="Snapshot">The snapshot after the change; it carries the new path, content, and version.</param>
public sealed record WorkspaceDocumentViewIdentityChange(
	WorkspaceDocumentKey OldDocumentKey,
	string OldDocumentId,
	WorkspaceDocumentSnapshot Snapshot);

/// <summary>
/// Provides the replacement requested by a view.
/// </summary>
/// <remarks>The manager applies <see cref="Request"/> as a logical mutation; committing it to disk is a separate operation.</remarks>
public sealed class WorkspaceDocumentViewApplyRequestedEventArgs : EventArgs
{
	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceDocumentViewApplyRequestedEventArgs"/> class.
	/// </summary>
	/// <param name="request">The replacement to apply.</param>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	public WorkspaceDocumentViewApplyRequestedEventArgs(WorkspaceDocumentReplaceRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		Request = request;
	}

	/// <summary>
	/// Gets the replacement requested by the view.
	/// </summary>
	public WorkspaceDocumentReplaceRequest Request { get; }
}

/// <summary>
/// Contains the outcome of applying or releasing a view delete guard.
/// </summary>
/// <remarks><see cref="Failure"/> is populated when the view could not change its guard state.</remarks>
/// <param name="Outcome">The guard outcome.</param>
/// <param name="Failure">Explains a failed guard operation, when one occurred.</param>
public sealed record WorkspaceDocumentViewDeleteGuardResult(
	WorkspaceDocumentViewDeleteGuardOutcome Outcome,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Describes the outcome of a view delete-guard operation.
/// </summary>
public enum WorkspaceDocumentViewDeleteGuardOutcome
{
	/// <summary>The guard operation succeeded: the guard is applied, or a previously applied guard was released.</summary>
	Succeeded,

	/// <summary>The view could not change its guard state.</summary>
	Failed
}

/// <summary>
/// Contains the outcome of updating a view's document identity.
/// </summary>
/// <remarks>
/// A failed result causes the manager to retain the view's prior binding and mark it unsynchronized;
/// a successful result updates the binding and clears that state.
/// </remarks>
/// <param name="Outcome">The identity update outcome.</param>
/// <param name="Failure">Explains a failed identity update, when one occurred.</param>
public sealed record WorkspaceDocumentViewIdentityResult(
	WorkspaceDocumentViewIdentityOutcome Outcome,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Describes the outcome of updating a view's document identity.
/// </summary>
public enum WorkspaceDocumentViewIdentityOutcome
{
	/// <summary>The identity was updated.</summary>
	Updated,

	/// <summary>The view rejected the identity change or could not apply it.</summary>
	Failed
}

/// <summary>
/// Contains the outcome of attaching a view to a workspace document.
/// </summary>
/// <remarks><see cref="Failure"/> explains an unavailable or failed attach when the view can provide that detail.</remarks>
/// <param name="Outcome">The attach outcome.</param>
/// <param name="Failure">Explains an unavailable or failed attach, when the view can provide that detail.</param>
public sealed record WorkspaceDocumentViewOpenResult(
	WorkspaceDocumentViewOpenOutcome Outcome,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Describes the outcome of attaching a view.
/// </summary>
public enum WorkspaceDocumentViewOpenOutcome
{
	/// <summary>The view was attached.</summary>
	Opened,

	/// <summary>The view was already attached.</summary>
	AlreadyOpen,

	/// <summary>The view could not be attached; the manager closes the view afterwards.</summary>
	Unavailable
}

/// <summary>
/// Contains the outcome of refreshing a view from workspace content.
/// </summary>
/// <remarks>
/// Any outcome other than <see cref="WorkspaceDocumentViewRefreshOutcome.Refreshed"/> requires the
/// manager to treat the view as unsynchronized until it is refreshed successfully.
/// </remarks>
/// <param name="Outcome">The refresh outcome.</param>
/// <param name="Failure">Explains a failed refresh, when one occurred.</param>
public sealed record WorkspaceDocumentViewRefreshResult(
	WorkspaceDocumentViewRefreshOutcome Outcome,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Describes the outcome of refreshing a view.
/// </summary>
public enum WorkspaceDocumentViewRefreshOutcome
{
	/// <summary>The view was refreshed.</summary>
	Refreshed,

	/// <summary>
	/// The view could not apply the snapshot immediately: it keeps its current content and is marked
	/// stale. The manager keeps the view unsynchronized until a later refresh succeeds.
	/// </summary>
	MarkedStale,

	/// <summary>The view could not apply the workspace content.</summary>
	Failed
}

/// <summary>
/// Contains the outcome of a view acknowledging an applied document mutation.
/// </summary>
/// <remarks>
/// Used when a mutation published by the view, or a manager-initiated discard, becomes the document
/// state. Any outcome other than <see cref="WorkspaceDocumentViewApplyOutcome.Applied"/> leaves the
/// view unsynchronized, so the manager records the failure and keeps the view unsynchronized until a
/// later refresh or acknowledgment succeeds.
/// </remarks>
/// <param name="Outcome">The acknowledgment outcome.</param>
/// <param name="Failure">Explains a failed acknowledgment, when one occurred.</param>
public sealed record WorkspaceDocumentViewApplyResult(
	WorkspaceDocumentViewApplyOutcome Outcome,
	WorkspaceOperationFailure? Failure = null);

/// <summary>
/// Describes the outcome of a view acknowledging an applied document mutation.
/// </summary>
public enum WorkspaceDocumentViewApplyOutcome
{
	/// <summary>The view adopted the applied document state and is synchronized.</summary>
	Applied,

	/// <summary>The view could not adopt the applied state and is unsynchronized.</summary>
	Failed
}
