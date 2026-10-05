using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Pairs a view with the document snapshot that a coordinator operation captured for it.</summary>
/// <param name="View">The attached view.</param>
/// <param name="Snapshot">The snapshot captured for the view when the operation enumerated its document.</param>
internal sealed record ViewSnapshotPair(IWorkspaceDocumentView View, WorkspaceDocumentSnapshot Snapshot);
