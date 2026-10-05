namespace Nickelony.IDEKit.Workspace.Replacement;

/// <summary>Identifies the outcome of multi-file workspace-edit application.</summary>
/// <remarks>
/// <see cref="ValidationFailed"/> comes from the host's edit-preparation layer before any mutation, together with the preparation issues;
/// the library's <see cref="DocumentReplacementApplier"/> produces only the remaining outcomes.
/// </remarks>
public enum DocumentReplacementOutcome
{
	/// <summary>
	/// Target resolution or edit preparation failed before mutation; the host populates <see cref="DocumentReplacementResult.Issues"/>
	/// for this outcome.
	/// </summary>
	ValidationFailed,

	/// <summary>Every prepared target applied successfully or was a no-op.</summary>
	Completed,

	/// <summary>
	/// The application was canceled before any target changed a document; a target already satisfied as a no-op may still be listed as applied.
	/// </summary>
	Canceled,

	/// <summary>
	/// A runtime target failure occurred after preflight, or the application was canceled after a confirmed change, so not all targets were applied
	/// or confirmed.
	/// </summary>
	PartiallyApplied,
}
