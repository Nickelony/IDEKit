using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Contains the result of opening a workspace document and attaching a view.</summary>
/// <param name="Outcome">The open-and-attach outcome.</param>
/// <param name="Snapshot">
/// The loaded snapshot, or the current snapshot of the document the view is attached to; otherwise, <see langword="null"/>.
/// </param>
/// <param name="Failure">Explains a load or attachment failure, when one occurred.</param>
/// <param name="StoreResult">The document-authority outcome the open path reached; <see langword="null"/> when no store call happened.</param>
/// <remarks>
/// <para>
/// <see cref="Snapshot"/> is present when a document was loaded and when an already-registered view made the open a no-op
/// (the snapshot of the document the view is attached to, or <see langword="null"/> when that document is not tracked). It is <see langword="null"/>
/// in every other case:
/// </para>
/// <list type="bullet">
/// <item><description>The path is invalid, missing, or a directory, or the load failed or was canceled.</description></item>
/// <item><description>The view was unavailable for attachment, or its id was already claimed before the load.</description></item>
/// <item>
/// <description>
/// An unexpected exception interrupted the open or attach (reported as <see cref="WorkspaceViewCoordinatorOpenOutcome.OpenFailed"/>).
/// </description>
/// </item>
/// </list>
/// <para>
/// <see cref="StoreResult"/> carries the full document-authority outcome when the open path reached the store, so a host
/// can distinguish a fresh load from a document that was already open and merely received the view; it is <see langword="null"/> when the view
/// validation answered first. <see cref="Outcome"/> uses the coordinator vocabulary and can differ from the store outcome for the same operation:
/// a view attached to an already-open document reports <see cref="WorkspaceViewCoordinatorOpenOutcome.Opened"/> while the store
/// reported <see cref="WorkspaceDocumentOpenOutcome.AlreadyOpen"/>.
/// </para>
/// </remarks>
public sealed record WorkspaceViewCoordinatorOpenResult(
	WorkspaceViewCoordinatorOpenOutcome Outcome,
	WorkspaceDocumentSnapshot? Snapshot,
	WorkspaceOperationFailure? Failure = null,
	WorkspaceDocumentOpenResult? StoreResult = null
);

/// <summary>Describes the outcome of a coordinator open operation, with or without view attachment.</summary>
public enum WorkspaceViewCoordinatorOpenOutcome
{
	/// <summary>The document was opened, or it was already open and the supplied view was attached.</summary>
	Opened,

	/// <summary>The document was already open and the call was a no-op.</summary>
	/// <remarks>
	/// Produced when no view was supplied and the store reports the document already open, when the supplied view was already registered,
	/// or when a concurrent <see cref="IWorkspaceViewCoordinator.DetachView"/> removed the view mid-attach. A registered view result carries
	/// the current snapshot, or <see langword="null"/> when that document is not tracked.
	/// </remarks>
	AlreadyOpen,

	/// <summary>
	/// The supplied view has a document, pending edits, a conflict, or could not report its id or state, so it cannot be attached
	/// to another document; <see cref="WorkspaceViewCoordinatorOpenResult.Failure"/> carries the detail when a view member threw.
	/// </summary>
	ViewUnavailable,

	/// <summary>
	/// The supplied view could not be attached because it is already in use: a different registered view reports the same view id, or the view
	/// itself reported that it is attached elsewhere.
	/// </summary>
	/// <remarks>
	/// The loaded snapshot is carried when the conflict was detected after the document was loaded; the supplied view is detached
	/// again for a duplicate id and stays untouched when its own attach reported the conflict.
	/// </remarks>
	ViewInUse,

	/// <summary>
	/// The document was loaded but the view did not accept the attachment;
	/// <see cref="WorkspaceViewCoordinatorOpenResult.Failure"/> carries the detail.
	/// </summary>
	ViewRejected,

	InvalidPath,

	/// <summary>
	/// The path does not exist and the open options did not allow creating a new document
	/// (<see cref="WorkspaceDocumentOpenOptions.CreateIfMissing"/> is <see langword="false"/>).
	/// </summary>
	NotFound,

	/// <summary>The path exists as a directory, not a file.</summary>
	/// <remarks>
	/// A directory cannot be tracked as a document, so the open is rejected: nothing is loaded
	/// and <see cref="WorkspaceViewCoordinatorOpenResult.Snapshot"/> is <see langword="null"/>.
	/// </remarks>
	IsDirectory,

	LoadFailed,

	/// <summary>
	/// Opening the document and attaching the view threw an unexpected exception;
	/// <see cref="WorkspaceViewCoordinatorOpenResult.Failure"/> carries the detail.
	/// </summary>
	/// <remarks>
	/// Only <see cref="IWorkspaceViewCoordinator.OpenWithViewAsync"/> produces this outcome:
	/// <see cref="IWorkspaceViewCoordinator.OpenAsync"/> lets an unexpected store exception fault its task, and missing options are argument errors
	/// that throw instead.
	/// </remarks>
	OpenFailed,

	Canceled,
}
