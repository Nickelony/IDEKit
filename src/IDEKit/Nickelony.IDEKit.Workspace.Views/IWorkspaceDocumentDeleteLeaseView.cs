namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>Represents a workspace view that can hold a delete lease for an in-flight delete or directory move.</summary>
/// <remarks>
/// <para>
/// The coordinator enters the lease on every attached view that implements this interface before a file delete, a directory move,
/// or a directory delete, and releases it afterwards. A view that implements only <see cref="IWorkspaceDocumentView"/> is skipped: it cannot hold
/// state that one of those operations would lose, so it has nothing to lease.
/// </para>
/// <para>
/// The leases of concurrent operations nest: a directory delete and a file delete inside it can each enter the lease of one view, so a view counts
/// its entries and lifts its barrier only when the last one is released.
/// </para>
/// </remarks>
public interface IWorkspaceDocumentDeleteLeaseView : IWorkspaceDocumentView
{
	/// <summary>Enters the view's delete lease for an in-flight file delete, directory delete, or directory move.</summary>
	/// <returns>
	/// <see cref="WorkspaceDocumentViewDeleteLeaseOutcome.Entered"/>, or <see cref="WorkspaceDocumentViewDeleteLeaseOutcome.Failed"/> from a view
	/// that cannot enter the lease.
	/// </returns>
	WorkspaceDocumentViewDeleteLeaseResult EnterDeleteLease();

	/// <summary>Releases one entry of the view's delete lease after its operation completed or was abandoned.</summary>
	/// <returns>
	/// <see cref="WorkspaceDocumentViewDeleteLeaseOutcome.Released"/>, or <see cref="WorkspaceDocumentViewDeleteLeaseOutcome.Failed"/> from a view
	/// that cannot release the lease.
	/// </returns>
	/// <remarks>
	/// Releasing a lease that is not held is a no-op that reports <see cref="WorkspaceDocumentViewDeleteLeaseOutcome.Released"/>.
	/// </remarks>
	WorkspaceDocumentViewDeleteLeaseResult ReleaseDeleteLease();
}
