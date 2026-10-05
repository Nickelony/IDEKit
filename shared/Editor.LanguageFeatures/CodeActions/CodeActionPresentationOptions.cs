using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;

#endif

/// <summary>Groups the menu presentation inputs of a <see cref="CodeActionPresenter"/>.</summary>
/// <remarks>
/// Grouping the theme and the options gives the presenter's constructor one presentation argument
/// instead of a growing run of adjacent ones. Each member rejects <see langword="null"/>, the theme validates its own brushes when it is assigned,
/// and the options validate themselves, so a presentation that exists is valid.
/// </remarks>
public sealed record CodeActionPresentationOptions
{
	private readonly CodeActionMenuTheme _menuTheme = CodeActionMenuTheme.Default;
	private readonly CodeActionMenuOptions _menuOptions = CodeActionMenuOptions.Default;

	/// <summary>The presentation with the default menu theme and menu options.</summary>
	public static CodeActionPresentationOptions Default { get; } = new();

	/// <summary>The theme applied to created action menus; defaults to <see cref="CodeActionMenuTheme.Default"/>.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public CodeActionMenuTheme MenuTheme
	{
		get => _menuTheme;
		init => _menuTheme = value ?? throw new ArgumentNullException(nameof(MenuTheme));
	}

	/// <summary>The menu presentation options; defaults to <see cref="CodeActionMenuOptions.Default"/>.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public CodeActionMenuOptions MenuOptions
	{
		get => _menuOptions;
		init => _menuOptions = value ?? throw new ArgumentNullException(nameof(MenuOptions));
	}
}
