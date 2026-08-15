namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Lists the stable failure codes produced by view-manager operations.
/// </summary>
/// <remarks>
/// A <see cref="Documents.WorkspaceOperationFailure.Code"/> on a manager result carries one of these
/// values for a failure the manager or an attached view produced. Document-authority failures keep
/// the codes listed by <see cref="WorkspaceOperationFailureCodes"/> and pass through the manager
/// unchanged, so each slice owns its vocabulary and a host matches a failure against the list of the
/// slice it called.
/// </remarks>
public static class WorkspaceViewOperationFailureCodes
{
	/// <summary>Opening a document with a view failed: the view did not accept the attachment, or a view member threw while the open path consulted it.</summary>
	public const string OpenFailed = "OpenFailed";

	/// <summary>A view could not refresh from workspace content.</summary>
	public const string ViewRefreshFailed = "ViewRefreshFailed";

	/// <summary>A view could not acknowledge an applied mutation.</summary>
	public const string ViewAcknowledgeFailed = "ViewAcknowledgeFailed";

	/// <summary>A replacement published through <see cref="IWorkspaceDocumentView.ApplyRequested"/> could not be applied to the workspace document.</summary>
	public const string ViewApplyFailed = "ViewApplyFailed";

	/// <summary>A view could not apply a document identity change.</summary>
	public const string ViewIdentityUpdateFailed = "ViewIdentityUpdateFailed";

	/// <summary>A view threw while its pending-edit and conflict state was probed to decide whether it blocks a store operation.</summary>
	public const string ViewStateProbeFailed = "ViewStateProbeFailed";

	/// <summary>A view could not enter or release the delete guard of a delete or directory move.</summary>
	public const string DeleteGuardFailed = "DeleteGuardFailed";
}
