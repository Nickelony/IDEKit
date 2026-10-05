#if AVALONIAEDIT
using Avalonia.Media;
using Nickelony.IDEKit.AvaloniaEdit.Media;
using Nickelony.IDEKit.Core.Media;
using Brush = Avalonia.Media.IBrush;
#else
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;

#endif

/// <summary>Describes the default colors applied to a created code-action menu.</summary>
/// <remarks>
/// <para>
/// The theme only supplies the menu chrome colors; the standard menu items keep their own styling, so the highlight of the selected item follows
/// the platform menu highlight. The brushes are assigned to the menu when it is created and are expected not to be mutated afterwards.
/// </para>
/// <para>
/// Every member is an init-only, self-validating property, so a construction site names the brushes instead of relying on positional arguments.
/// Use <see cref="Default"/> as a baseline and override only the values that differ.
/// </para>
/// </remarks>
public sealed record CodeActionMenuTheme
{
#if AVALONIAEDIT
	private static readonly Brush s_defaultBorderBrush = ArgbColor.FromRgb(0x9E, 0x9E, 0x9E).ToBrush();
	private static readonly Brush s_defaultBackground = ArgbColor.FromRgb(0xFF, 0xFF, 0xFF).ToBrush();
	private static readonly Brush s_defaultForeground = ArgbColor.FromRgb(0x1F, 0x1F, 0x1F).ToBrush();
#else
	private static readonly Brush s_defaultBorderBrush = SystemColors.ActiveBorderBrush;
	private static readonly Brush s_defaultBackground = SystemColors.MenuBrush;
	private static readonly Brush s_defaultForeground = SystemColors.MenuTextBrush;
#endif

	private Brush _borderBrush = s_defaultBorderBrush;
	private Brush _background = s_defaultBackground;
	private Brush _foreground = s_defaultForeground;

	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public Brush BorderBrush
	{
		get => _borderBrush;
		init => _borderBrush = value ?? throw new ArgumentNullException(nameof(BorderBrush), "The menu border brush must not be null.");
	}

	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public Brush Background
	{
		get => _background;
		init => _background = value ?? throw new ArgumentNullException(nameof(Background), "The menu background brush must not be null.");
	}

	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public Brush Foreground
	{
		get => _foreground;
		init => _foreground = value ?? throw new ArgumentNullException(nameof(Foreground), "The menu foreground brush must not be null.");
	}

	/// <summary>The default theme, built from the binding's default menu brushes.</summary>
	public static CodeActionMenuTheme Default { get; } =
		new()
		{
			BorderBrush = s_defaultBorderBrush,
			Background = s_defaultBackground,
			Foreground = s_defaultForeground,
		};
}
