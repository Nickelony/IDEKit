namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>Contains the outcome of attaching a view to a workspace document.</summary>
/// <param name="Outcome">The attach outcome.</param>
/// <param name="Failure">Explains an unavailable or failed attach, when the view can provide that detail.</param>
public sealed record WorkspaceDocumentViewOpenResult(WorkspaceDocumentViewOpenOutcome Outcome, WorkspaceOperationFailure? Failure = null);

/// <summary>Describes the outcome of attaching a view.</summary>
public enum WorkspaceDocumentViewOpenOutcome
{
	/// <summary>The view was attached.</summary>
	Opened,

	/// <summary>The view was already attached.</summary>
	AlreadyOpen,

	/// <summary>The view could not be attached; the coordinator closes the view afterwards.</summary>
	Unavailable,
}
