using Nickelony.IDEKit.IntelliSense.CodeActions;
#if AVALONIAEDIT
using Avalonia.Controls;
#else
using System.Windows.Controls;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
#endif

/// <summary>
/// Groups the host hooks used by the <see cref="TextCodeActionController"/>.
/// </summary>
/// <remarks>
/// The request builder, the request hook, and the execute hook are required; the menu configuration
/// hooks are optional. Host hooks should not throw: failures inside
/// <see cref="RequestCodeActionsAsync"/> and <see cref="ExecuteActionAsync"/> are contained and
/// logged; a failure that escapes <see cref="BuildRequest"/> is contained, logged, and clears the
/// indicator for the current context.
/// </remarks>
public sealed class TextCodeActionControllerHooks
{
	/// <summary>
	/// Gets the hook that builds the provider request for an editor context, or <see langword="null"/>
	/// to veto the request for that context.
	/// </summary>
	/// <remarks>
	/// The hook runs on the text area's thread for every settled context (and for
	/// <see cref="TextCodeActionController.RefreshAsync"/>). Returning <see langword="null"/> means the
	/// context should not be requested - for example a document without a provider, or a caret position
	/// the host does not ask about - and clears the margin indicator until the next context change.
	/// </remarks>
	public required Func<TextCodeActionContext, TextCodeActionRequest?> BuildRequest { get; init; }

	/// <summary>
	/// Gets the hook that requests the code actions for a provider request.
	/// </summary>
	/// <remarks>
	/// The hook runs on the text area's thread and its completion is marshalled back to that thread; it
	/// should observe the cancellation token, because a context change cancels an in-flight request.
	/// The returned list is snapshotted for the menu; returning <see langword="null"/> or an empty list
	/// clears the indicator. A result that a newer context change superseded is discarded instead of
	/// being published.
	/// </remarks>
	public required Func<TextCodeActionRequest, CancellationToken, Task<IReadOnlyList<TextCodeActionItem>>> RequestCodeActionsAsync { get; init; }

	/// <summary>
	/// Gets the hook that applies the invoked action.
	/// </summary>
	/// <remarks>
	/// The hook runs on the text area's thread after the user invoked the action in the menu. The
	/// action is a snapshot taken when it was published, so the host should apply it through a
	/// version-validated edit pipeline, which rejects a stale action instead of corrupting the
	/// document. A failure is contained and logged.
	/// </remarks>
	public required Func<TextCodeActionItem, Task> ExecuteActionAsync { get; init; }

	/// <summary>
	/// Gets the optional hook that configures the actions menu before it opens.
	/// </summary>
	/// <remarks>
	/// The controller applies the <see cref="TextCodeActionMenuSkin"/> chrome and creates the menu
	/// items before invoking the hook, so the hook can override any value it set - for example the
	/// menu font - and style whatever the skin does not cover. An exception it throws propagates to
	/// the <see cref="TextCodeActionController.TryOpenActions()"/> caller before the menu is tracked or shown.
	/// </remarks>
	public Action<ContextMenu>? ConfigureMenu { get; init; }

	/// <summary>
	/// Gets the optional hook that configures a created menu item before the menu opens.
	/// </summary>
	/// <remarks>
	/// The presenter renders the action title as a text element, applies the preferred-action emphasis,
	/// and wires the item's click to the execute hook before invoking this hook, so the hook can restyle
	/// the item - for example with an icon or a different font weight - without changing what clicking it
	/// does. An exception it throws propagates to the
	/// <see cref="TextCodeActionController.TryOpenActions()"/> caller before the menu is tracked.
	/// </remarks>
	public Action<MenuItem, TextCodeActionItem>? ConfigureMenuItem { get; init; }
}
