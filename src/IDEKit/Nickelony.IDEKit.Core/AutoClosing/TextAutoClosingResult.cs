namespace Nickelony.IDEKit.Core.AutoClosing;

/// <summary>
/// Describes the auto-closing action applied by an editor-side service while handling text entering.
/// </summary>
/// <remarks>
/// A default-initialized value (the value returned as <see cref="None"/>) reports that no action was
/// applied; its <see cref="Action"/> is the default <see cref="TextAutoClosingAction"/>.
/// </remarks>
/// <param name="Action">
/// The action that was applied, or the default action, whose kind is
/// <see cref="TextAutoClosingActionKind.None"/>, when nothing was applied.
/// </param>
/// <param name="DidWrapSelection">
/// Whether the applied insert wrapped a non-empty selection instead of inserting at the caret.
/// </param>
public readonly record struct TextAutoClosingResult(
	TextAutoClosingAction Action,
	bool DidWrapSelection)
{
	/// <summary>
	/// Gets the result for no applied action.
	/// </summary>
	public static TextAutoClosingResult None => default;
}
