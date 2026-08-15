#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
#endif

/// <summary>
/// Groups the presentation inputs a <see cref="TextCodeActionController"/> and its menu presenter share:
/// the menu skin and the menu presentation options.
/// </summary>
/// <remarks>
/// <para>
/// The controller and the presenter it creates both need the same two inputs, so grouping them keeps the
/// controller's constructor from narrowing to a wall of adjacent arguments as the menu surface grows. The
/// record is a plain input bag: the skin validates its own brushes when it is assigned, and the options
/// validate themselves, so creating the presentation cannot fail.
/// </para>
/// <para>
/// Use <see cref="TextCodeActionMenuSkin.Default"/> and <see cref="TextCodeActionMenuOptions.Default"/> as
/// baselines and override only the values that differ.
/// </para>
/// </remarks>
public sealed record TextCodeActionPresentation
{
	/// <summary>
	/// Gets the skin applied to created action menus.
	/// </summary>
	public required TextCodeActionMenuSkin Skin { get; init; }

	/// <summary>
	/// Gets the menu presentation options. Defaults to <see cref="TextCodeActionMenuOptions.Default"/>.
	/// </summary>
	public TextCodeActionMenuOptions MenuOptions { get; init; } = TextCodeActionMenuOptions.Default;
}
