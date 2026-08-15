namespace Nickelony.IDEKit.Workspace.Editing;

/// <summary>
/// Identifies the outcome of multi-file workspace-edit application.
/// </summary>
/// <remarks>
/// <see cref="ValidationFailed"/> is produced by the host's edit-preparation layer before any
/// mutation, together with the preparation issues; the library's
/// <see cref="WorkspaceEditApplier"/> only produces <see cref="Completed"/>,
/// <see cref="PartiallyApplied"/>, and <see cref="Canceled"/>.
/// </remarks>
public enum WorkspaceEditApplicationOutcome
{
	/// <summary>
	/// Target resolution or edit preparation failed before mutation. The host populates
	/// <see cref="WorkspaceEditApplicationResult.Issues"/> for this outcome.
	/// </summary>
	ValidationFailed,

	/// <summary>
	/// Every prepared target applied successfully or was a no-op.
	/// </summary>
	Completed,

	/// <summary>
	/// The application was canceled before any target changed a document. No content change was
	/// confirmed; a target that was already satisfied as a no-op may still be listed as applied.
	/// </summary>
	Canceled,

	/// <summary>
	/// A runtime target failure occurred after preflight, or the application was canceled after a
	/// confirmed change, so not all targets were applied or confirmed.
	/// </summary>
	PartiallyApplied
}
