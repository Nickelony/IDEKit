#if AVALONIAQUICKPICK
using IconImage = Avalonia.Media.IImage;
#else
using IconImage = System.Windows.Media.ImageSource;
#endif

#if AVALONIAQUICKPICK

namespace Nickelony.QuickPick.Avalonia;

#else

namespace Nickelony.QuickPick.Wpf;

#endif

/// <summary>Groups the optional host callbacks that a <see cref="QuickPickPresenter"/> calls while it draws a row.</summary>
/// <remarks>
/// The hooks carry toolkit values, so they belong to the binding and not to the neutral controller. Host hooks should not throw: an exception escapes
/// to the code that updates the list.
/// </remarks>
public sealed class QuickPickPresenterHooks
{
	/// <summary>Maps the icon key of an item to the image drawn before its label, or <see langword="null"/> for an item without an icon.</summary>
	/// <remarks>
	/// The default row template calls it for each row that has an icon key and reserves no space for a <see langword="null"/> result.
	/// The hook is ignored when <see cref="QuickPickTheme.ItemTemplate"/> replaces the template, because that template draws its own icons.
	/// The hook runs on the UI thread of the host element, each time the list builds the visual tree of a row, so it may touch the UI and returns
	/// a cached image rather than loading one.
	/// </remarks>
	public Func<string, IconImage?>? ResolveIcon { get; init; }
}
