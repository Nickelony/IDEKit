using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>Represents a document view of a workspace document, such as a text view, a preview, or another document model.</summary>
/// <remarks>
/// <para>
/// The coordinator adds the <see cref="ApplyRequested"/> subscription on the thread that resumes the attach, and event accessors must still be usable
/// from any thread. Members must tolerate reentrant calls from <see cref="ApplyRequested"/>, because an inline dispatcher runs the apply pipeline
/// synchronously inside a raise.
/// </para>
/// <para>
/// Lifecycle: the coordinator attaches the view with <see cref="Open"/>, and a view may receive <see cref="Close"/> after a rejected attach,
/// so closing must be safe for a view that never attached. While attached, the view receives <see cref="Refresh"/> for unseen content
/// or version updates, <see cref="AcknowledgeIdentity"/> after an identity change, and <see cref="AcknowledgeApply"/> when a mutation published
/// by the view or a coordinator-initiated discard becomes the document state. Delete operations and directory moves ask views that implement
/// <see cref="IWorkspaceDocumentDeleteLeaseView"/> to enter and always release a delete lease.
/// </para>
/// <para>
/// A detached view has a <see langword="null"/> <see cref="DocumentKey"/>. A view with <see cref="HasPendingEdits"/> or <see cref="HasConflict"/>
/// blocks the operations that could lose that state, and the operations that only change the document identity do not block on view state.
/// The <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/WorkspaceViewsGuide.md#blocking-policy">blocking policy</see>
/// of the workspace views guide lists each operation. The view adds only the interactive buffer and its relationship to the document;
/// the workspace document already owns the logical content, the persisted baseline, the dirty flag, and the version pair.
/// </para>
/// </remarks>
public interface IWorkspaceDocumentView
{
	/// <summary>Raised when the view requests that edited content be published to the workspace document.</summary>
	/// <remarks>
	/// Raise the event with the view instance as the sender; the coordinator identifies the publishing view from it. The event request
	/// must identify the view's current document key, id, and version. The raise has no synchronous completion channel:
	/// the coordinator applies the replacement and acknowledges the outcome through <see cref="AcknowledgeApply"/>, with the current
	/// state when the store rejects the replacement. A failure thrown by the apply path itself is recorded as unsynchronized view state,
	/// reported with <c>WorkspaceViewOperationFailureCodes.ViewApplyFailed</c> and the exception detail, and blocks the operations that destroy
	/// the document until a successful refresh clears it. A raise from a view that DetachView has removed is ignored.
	/// </remarks>
	event EventHandler<ApplyRequestedEventArgs> ApplyRequested;

	/// <summary>
	/// The stable identifier for reporting and distinguishing this view; a blank or whitespace id cannot be attached, and the value must not change
	/// while the view is registered.
	/// </summary>
	string ViewId { get; }

	/// <summary>The current logical document identity, or <see langword="null"/> when detached.</summary>
	WorkspaceDocumentKey? DocumentKey { get; }

	/// <summary>Indicates whether the view has edits not published to the workspace document.</summary>
	bool HasPendingEdits { get; }

	/// <summary>Indicates whether the view cannot currently be synchronized without conflict resolution.</summary>
	bool HasConflict { get; }

	/// <summary>Attaches the view to a document snapshot and makes that snapshot its current content.</summary>
	/// <param name="snapshot">The workspace document snapshot to attach.</param>
	/// <returns>An open outcome; return <see cref="WorkspaceDocumentViewOpenOutcome.AlreadyOpen"/> when the view is already attached.</returns>
	WorkspaceDocumentViewOpenResult Open(WorkspaceDocumentSnapshot snapshot);

	/// <summary>Refreshes the view from a document snapshot.</summary>
	/// <param name="snapshot">The workspace document snapshot to refresh from.</param>
	/// <remarks>
	/// The view may return <see cref="WorkspaceDocumentViewRefreshOutcome.MarkedStale"/> when it cannot apply the snapshot immediately.
	/// </remarks>
	WorkspaceDocumentViewRefreshResult Refresh(WorkspaceDocumentSnapshot snapshot);

	/// <summary>Applies a document identity change, such as a rename or directory move, to the view.</summary>
	/// <param name="change">The identity change to apply.</param>
	/// <remarks>
	/// The view should update its document id and content from <see cref="WorkspaceDocumentViewIdentityChange.Snapshot"/> before reporting success.
	/// A successful result also clears the coordinator's unsynchronized state for the view.
	/// </remarks>
	WorkspaceDocumentViewIdentityResult AcknowledgeIdentity(WorkspaceDocumentViewIdentityChange change);

	/// <summary>Acknowledges the result of applying a logical document mutation to the view.</summary>
	/// <param name="result">The mutation result the view should adopt.</param>
	/// <remarks>
	/// Used when a mutation published by the view, or a coordinator-initiated discard, becomes the document state. A non-applied acknowledgment
	/// leaves the view unsynchronized.
	/// </remarks>
	WorkspaceDocumentViewApplyResult AcknowledgeApply(WorkspaceDocumentMutationResult result);

	/// <summary>Closes the view and detaches it from its current document.</summary>
	/// <remarks>
	/// <para>
	/// Closing a view does not detach it from the coordinator; the owner must call <c>IWorkspaceViewCoordinator.DetachView</c>.
	/// </para>
	/// <para>
	/// Closing must be idempotent: a file or directory delete closes the views it captured even when the owner already detached them, and the owner
	/// may have closed a view itself, so an implementation must tolerate a repeat close without faulting or double-releasing resources.
	/// </para>
	/// </remarks>
	void Close();
}
