using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>Tracks the registration state of one attached view.</summary>
/// <remarks>
/// Instances are only read and written under the tracker lock; the view id is captured at registration and never changes for a registration.
/// The last failure survives until a successful refresh or identity acknowledgment clears the unsynchronized flag, so reports can explain
/// why the view needs attention. Each registration owns its apply forwarder, so a removal matches its own add even when the same view is detached
/// and re-attached while an earlier attach is still adding its handler.
/// </remarks>
internal sealed class ViewEntry(
	string viewId,
	string documentId,
	WorkspaceDocumentKey documentKey,
	long version,
	long persistedVersion,
	ApplyRequestedForwarder forwarder
)
{
	internal string ViewId { get; } = viewId;

	internal string DocumentId { get; set; } = documentId;

	internal WorkspaceDocumentKey DocumentKey { get; set; } = documentKey;

	internal long Version { get; set; } = version;

	internal long PersistedVersion { get; set; } = persistedVersion;

	internal bool IsUnsynchronized { get; set; }

	internal WorkspaceOperationFailure? LastFailure { get; set; }

	internal ApplyRequestedForwarder Forwarder { get; } = forwarder;
}
