namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>
/// Describes whether a view may attach to a workspace document.
/// </summary>
internal enum ViewAttachmentOutcome
{
	/// <summary>The view may be attached; the document may be loaded for it.</summary>
	Proceed,

	/// <summary>The view is already registered with this manager.</summary>
	AlreadyRegistered,

	/// <summary>Another registered view reports the same view id.</summary>
	DuplicateViewId,

	/// <summary>The view has a document, pending edits, a conflict, or could not report its id or state.</summary>
	Unavailable
}
