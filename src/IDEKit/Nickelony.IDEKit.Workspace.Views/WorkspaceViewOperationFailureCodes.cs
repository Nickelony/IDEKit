namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>Lists the stable failure codes produced by view-coordinator operations and by the views they reach.</summary>
/// <remarks>
/// A <see cref="WorkspaceOperationFailure.Code"/> on a coordinator result carries one of these values for a failure the coordinator
/// or an attached view produced, such as <see cref="WorkspaceDocumentTextViewBase"/>. Document-authority failures keep the codes listed
/// by <see cref="WorkspaceOperationFailureCodes"/> and pass through the coordinator unchanged, so each slice owns its vocabulary and a host matches
/// a failure against the list of the slice it called.
/// </remarks>
public static class WorkspaceViewOperationFailureCodes
{
	/// <summary>
	/// Opening a document failed: the view did not accept the attachment, a view member threw while the open path consulted it, or the open raised
	/// an unexpected exception.
	/// </summary>
	public const string OpenFailed = "OpenFailed";

	/// <summary>A view could not refresh from workspace content.</summary>
	public const string ViewRefreshFailed = "ViewRefreshFailed";

	/// <summary>A view could not acknowledge an applied mutation.</summary>
	public const string ViewAcknowledgeFailed = "ViewAcknowledgeFailed";

	/// <summary>
	/// A replacement published through <see cref="IWorkspaceDocumentView.ApplyRequested"/> could not be applied to the workspace document.
	/// </summary>
	public const string ViewApplyFailed = "ViewApplyFailed";

	/// <summary>A view threw while it was asked to publish its pending edits.</summary>
	public const string ViewPublishFailed = "ViewPublishFailed";

	/// <summary>A view threw when it was closed because its document was removed.</summary>
	public const string ViewCloseFailed = "ViewCloseFailed";

	/// <summary>A view could not apply a document identity change.</summary>
	public const string ViewIdentityUpdateFailed = "ViewIdentityUpdateFailed";

	/// <summary>A view threw while its pending-edit and conflict state was probed to decide whether it blocks a store operation.</summary>
	public const string ViewStateProbeFailed = "ViewStateProbeFailed";

	/// <summary>
	/// A view of the document held pending edits or a conflict when a language-server edit reached it, so the edit was not applied over them.
	/// </summary>
	public const string ViewHoldsPendingEdits = "ViewHoldsPendingEdits";

	/// <summary>A view could not enter or release the delete lease of a delete or directory move.</summary>
	public const string DeleteLeaseFailed = "DeleteLeaseFailed";
}
