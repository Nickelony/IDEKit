using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;
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

/// <summary>Groups the optional host hooks that restyle the menu of a <see cref="CodeActionPresenter"/>.</summary>
/// <remarks>
/// The hooks carry toolkit values (<see cref="ContextMenu"/> and <see cref="MenuItem"/>), so they belong to the binding
/// and not to the neutral <see cref="CodeActionControllerHooks"/>. Host hooks should not throw: an exception escapes to the caller of the presenter's
/// open member.
/// </remarks>
public sealed class CodeActionMenuHooks
{
	/// <summary>The optional hook that configures the actions menu before it opens.</summary>
	/// <remarks>
	/// The presenter applies the <see cref="CodeActionMenuTheme"/> chrome and creates the menu items before invoking the hook, so the hook
	/// can override any value it set - for example the menu font - and style whatever the theme does not cover. An exception it throws propagates
	/// to the <see cref="CodeActionPresenter.TryOpenMenu()"/> caller before the menu is tracked or shown.
	/// </remarks>
	public Action<ContextMenu>? ConfigureMenu { get; init; }

	/// <summary>The optional hook that configures a created menu item before the menu opens.</summary>
	/// <remarks>
	/// The presenter renders the action title as a text element, applies the preferred-action emphasis, and wires the item's
	/// click to the controller's execute hook before invoking this hook, so the hook can restyle the item - for example with an icon or a different
	/// font weight - without changing what clicking it does. An exception it throws propagates to the <see cref="CodeActionPresenter.TryOpenMenu()"/>
	/// caller before the menu is tracked.
	/// </remarks>
	public Action<MenuItem, CodeAction>? ConfigureMenuItem { get; init; }
}
