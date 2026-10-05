namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Represents a workspace view that holds edits back, for example until typing pauses, and publishes them when the coordinator asks.
/// </summary>
/// <remarks>
/// <see cref="Coordination.IWorkspaceViewCoordinator.PublishPendingEditsAsync"/> asks every attached view of a document that implements
/// this interface before an operation that must see the text the user sees, such as a save, a reload, or an edit of a language server.
/// A view that implements only <see cref="IWorkspaceDocumentView"/> keeps its pending edits, and the coordinator reports it as a view that blocks.
/// </remarks>
public interface IWorkspaceDocumentPublishingView : IWorkspaceDocumentView
{
	/// <summary>
	/// Raises <see cref="IWorkspaceDocumentView.ApplyRequested"/> now with the pending edits, or does nothing when the view holds none.
	/// </summary>
	/// <remarks>
	/// The coordinator calls it on the view's thread, and the raise must happen before the call returns, or inside the acknowledgment
	/// of a publication still in flight, which the coordinator waits for.
	/// </remarks>
	void PublishPendingEdits();
}
