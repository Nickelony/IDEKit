using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Binds a view to the document snapshot a manager operation captured for it.
/// </summary>
/// <param name="View">The attached view.</param>
/// <param name="Snapshot">The snapshot captured for the view when the operation enumerated its document.</param>
internal sealed record ViewBinding(
	IWorkspaceDocumentView View,
	WorkspaceDocumentSnapshot Snapshot);
