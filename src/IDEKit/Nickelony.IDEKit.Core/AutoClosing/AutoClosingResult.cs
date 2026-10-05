namespace Nickelony.IDEKit.Core.AutoClosing;

/// <summary>Describes the auto-closing action applied by an editor-side service while handling text input.</summary>
/// <param name="Action">
/// The action that was applied, or the default action (kind <see cref="AutoClosingActionKind.None"/>) when nothing was applied.
/// </param>
/// <param name="DidWrapSelection">Whether the applied insert wrapped a non-empty selection instead of inserting at the caret.</param>
public readonly record struct AutoClosingResult(AutoClosingAction Action, bool DidWrapSelection)
{
	/// <summary>The result for no applied action.</summary>
	public static AutoClosingResult None => default;
}
