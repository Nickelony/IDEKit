namespace Nickelony.IDEKit.Core.AutoClosing;

/// <summary>
/// Identifies the kind of auto-closing action to apply.
/// </summary>
public enum TextAutoClosingActionKind
{
	/// <summary>
	/// No action is applied. This is the default value and the failure result of
	/// <see cref="TextAutoClosingResolver.TryResolveAction"/>.
	/// </summary>
	None = 0,

	/// <summary>
	/// The closing text is inserted at the caret.
	/// </summary>
	InsertClosingText = 1,

	/// <summary>
	/// Existing closing text at the caret is skipped instead of inserted.
	/// </summary>
	SkipExistingClosingText = 2
}
