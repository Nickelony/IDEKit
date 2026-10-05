#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Hover;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Hover;

#endif

/// <summary>Groups the presentation inputs of a <see cref="HoverPresenter"/>.</summary>
/// <remarks>
/// The member rejects <see langword="null"/>, and the theme validates its own values when it is assigned, so a presentation that exists is valid.
/// </remarks>
public sealed record HoverPresentationOptions
{
	private readonly HoverPopupTheme _theme = HoverPopupTheme.Default;

	/// <summary>The presentation with the default popup theme.</summary>
	public static HoverPresentationOptions Default { get; } = new();

	/// <summary>The frame of the popup; defaults to <see cref="HoverPopupTheme.Default"/>.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public HoverPopupTheme Theme
	{
		get => _theme;
		init => _theme = value ?? throw new ArgumentNullException(nameof(Theme));
	}
}
