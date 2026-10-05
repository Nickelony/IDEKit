using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Tracks one document that has an attached view: how many views it has and the newest snapshot the coordinator saw.</summary>
/// <remarks>
/// Instances are only read and written under the tracker lock.
/// </remarks>
internal sealed class AttachedDocumentEntry(WorkspaceDocumentSnapshot snapshot)
{
	internal int ViewCount { get; set; } = 1;
	internal WorkspaceDocumentSnapshot Snapshot { get; set; } = snapshot;
}
