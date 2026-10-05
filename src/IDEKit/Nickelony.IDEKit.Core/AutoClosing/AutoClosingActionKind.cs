namespace Nickelony.IDEKit.Core.AutoClosing;

/// <summary>Identifies the kind of auto-closing action to apply.</summary>
public enum AutoClosingActionKind
{
	/// <summary>No action is applied; the default value and the failure result of <see cref="AutoClosingPlanner.TryPlanAction"/>.</summary>
	None = 0,

	InsertClosingText = 1,

	SkipExistingClosingText = 2,
}
