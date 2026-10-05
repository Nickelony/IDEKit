using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Contains the outcome of having the views of a document publish their pending edits.</summary>
/// <param name="ViewSynchronization">Whether a view still holds pending edits, a conflict or a failed synchronization afterwards.</param>
/// <param name="Snapshot">
/// The snapshot the final check of the views read, so an operation that follows addresses the text they published; <see langword="null"/> when a view
/// blocks or the store does not track the document.
/// </param>
public sealed record WorkspaceViewCoordinatorPublicationResult(
	WorkspaceDocumentViewSynchronizationResult ViewSynchronization,
	WorkspaceDocumentSnapshot? Snapshot = null
)
{
	/// <summary>Whether a view still holds pending edits, a conflict or a failed synchronization afterwards; never <see langword="null"/>.</summary>
	public WorkspaceDocumentViewSynchronizationResult ViewSynchronization { get; init; } =
		ViewSynchronization ?? throw new ArgumentNullException(nameof(ViewSynchronization));
}
