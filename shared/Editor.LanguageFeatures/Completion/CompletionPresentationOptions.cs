using Nickelony.IDEKit.Core.Validation;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

#endif

/// <summary>Groups the themes, the window options and the settings that configure a <see cref="CompletionPresenter"/>.</summary>
/// <remarks>
/// <para>
/// Grouping them gives the presenter's constructor one presentation argument instead of one adjacent argument per theme or option record.
/// Each theme and the window options reject <see langword="null"/> and validate their own values when they are assigned, and the tooltip
/// delay rejects a negative value, so a presentation that exists is valid on its own. The presenter still rejects window options whose maximum width
/// cannot hold the minimum content width plus the chrome, because only the constructor sees those members together.
/// </para>
/// </remarks>
public sealed record CompletionPresentationOptions
{
	private readonly CompletionWindowTheme _windowTheme = CompletionWindowTheme.Default;
	private readonly CompletionWindowOptions _windowOptions = CompletionWindowOptions.Default;
#if !AVALONIAEDIT
	private readonly CompletionTooltipTheme _tooltipTheme = CompletionTooltipTheme.Default;
	private readonly TimeSpan _tooltipResolveDelay = TimeSpan.FromMilliseconds(120.0);
#endif

	/// <summary>The presentation with the default window theme, window options, and tooltip theme.</summary>
	public static CompletionPresentationOptions Default { get; } = new();

	/// <summary>The theme applied to created completion windows; defaults to <see cref="CompletionWindowTheme.Default"/>.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public CompletionWindowTheme WindowTheme
	{
		get => _windowTheme;
		init => _windowTheme = value ?? throw new ArgumentNullException(nameof(WindowTheme));
	}

	/// <summary>The completion window's sizing and behavior options; defaults to <see cref="CompletionWindowOptions.Default"/>.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public CompletionWindowOptions WindowOptions
	{
		get => _windowOptions;
		init => _windowOptions = value ?? throw new ArgumentNullException(nameof(WindowOptions));
	}

#if !AVALONIAEDIT
	/// <summary>The theme applied to the completion tooltip; defaults to <see cref="CompletionTooltipTheme.Default"/>.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	/// <remarks>
	/// The theme describes the chrome
	/// the presenter applies to the editor's completion tooltip; <see cref="CompletionPresenterHooks.ConfigureTooltip"/> runs after the theme
	/// and can override any value the theme applied. The theme is applied only when the running editor exposes a styleable tooltip
	/// (<see cref="CompletionPresenter.IsTooltipSupported"/> is <see langword="true"/>); otherwise, the engine-owned tooltip is used unchanged
	/// and this value has no effect.
	/// </remarks>
	public CompletionTooltipTheme TooltipTheme
	{
		get => _tooltipTheme;
		init => _tooltipTheme = value ?? throw new ArgumentNullException(nameof(TooltipTheme));
	}

	/// <summary>The delay before the tooltip of the selected item is updated; defaults to 120 milliseconds.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative.</exception>
	/// <remarks>
	/// The delay debounces the description resolve after each selection change, so moving through the list quickly resolves only the item
	/// it stops on. AvaloniaEdit builds no tooltip pipeline, so the binding declares no such option there.
	/// </remarks>
	public TimeSpan TooltipResolveDelay
	{
		get => _tooltipResolveDelay;
		init => _tooltipResolveDelay = NumericValidation.NonNegative(value, nameof(TooltipResolveDelay));
	}
#endif

	/// <summary>
	/// Whether a character that the selected item declares as a commit character accepts the item while the character is typed;
	/// defaults to <see langword="true"/>.
	/// </summary>
	/// <remarks>
	/// The presenter consults the selected item through <see cref="ICompletionCommitCharacters"/>:
	/// when the typed character is one of the item's declared characters, the item is committed and the character is typed in the same input step.
	/// Items that declare no characters, and hosts whose item mapping does not implement the interface, are unaffected; a host that wants the editor
	/// to never accept an item on a typed character sets this option to <see langword="false"/>.
	/// </remarks>
	public bool AcceptOnCommitCharacters { get; init; } = true;
}
