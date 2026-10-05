#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SignatureHelp;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SignatureHelp;

#endif

/// <summary>Groups the presentation inputs of a <see cref="SignatureHelpPresenter"/>.</summary>
/// <remarks>
/// The member rejects <see langword="null"/>, and the theme validates its own values when it is assigned, so a presentation that exists is valid.
/// </remarks>
public sealed record SignatureHelpPresentationOptions
{
	private readonly SignatureHelpPopupTheme _theme = SignatureHelpPopupTheme.Default;

	/// <summary>The presentation with the default popup theme.</summary>
	public static SignatureHelpPresentationOptions Default { get; } = new();

	/// <summary>The frame of the popup; defaults to <see cref="SignatureHelpPopupTheme.Default"/>.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public SignatureHelpPopupTheme Theme
	{
		get => _theme;
		init => _theme = value ?? throw new ArgumentNullException(nameof(Theme));
	}
}
