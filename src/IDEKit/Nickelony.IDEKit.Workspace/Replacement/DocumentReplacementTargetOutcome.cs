namespace Nickelony.IDEKit.Workspace.Replacement;

/// <summary>Identifies one target's runtime state after workspace-edit application.</summary>
public enum DocumentReplacementTargetOutcome
{
	/// <summary>
	/// The replacement was not applied: the target was either not attempted (an earlier failure stopped the application,
	/// or the application was canceled) or rejected without the document being mutated.
	/// </summary>
	NotApplied,

	/// <summary>The target applied successfully or was a confirmed no-op.</summary>
	Applied,

	/// <summary>The target's final state could not be read or determined.</summary>
	Unknown,
}
