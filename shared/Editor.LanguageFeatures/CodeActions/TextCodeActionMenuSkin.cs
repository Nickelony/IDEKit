#if AVALONIAEDIT
using Avalonia.Media;
using Nickelony.IDEKit.Infrastructure;
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

/// <summary>
/// Describes the default colors applied to a created code-action menu.
/// </summary>
/// <remarks>
/// <para>
/// The skin only supplies the menu chrome colors; the standard menu items keep their own styling, so the
/// highlight of the selected item follows the platform menu highlight, matching how the completion window's
/// item templates keep their own styling. The brushes are assigned to the menu when it is created and are
/// expected not to be mutated afterwards.
/// </para>
/// <para>
/// Every member is an init-only property rather than a positional parameter, matching the completion
/// window and tooltip skins: a construction site names the brushes instead of relying on the order of
/// three adjacent <see cref="Brush"/> arguments. Use <see cref="Default"/> as a baseline and override
/// only the values that differ. Every value validates itself when it is assigned, so a null brush is
/// rejected at the offending object initializer instead of when the controller is created.
/// </para>
/// </remarks>
public sealed record TextCodeActionMenuSkin
{
#if AVALONIAEDIT
	private static readonly Brush s_defaultBorderBrush = BrushHelpers.CreateFrozenBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
	private static readonly Brush s_defaultBackground = BrushHelpers.CreateFrozenBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
	private static readonly Brush s_defaultForeground = BrushHelpers.CreateFrozenBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
#else
	private static readonly Brush s_defaultBorderBrush = SystemColors.ActiveBorderBrush;
	private static readonly Brush s_defaultBackground = SystemColors.MenuBrush;
	private static readonly Brush s_defaultForeground = SystemColors.MenuTextBrush;
#endif

	private Brush _borderBrush = s_defaultBorderBrush;
	private Brush _background = s_defaultBackground;
	private Brush _foreground = s_defaultForeground;

	/// <summary>Gets or initializes the menu border brush.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public required Brush BorderBrush
	{
		get => _borderBrush;
		init => _borderBrush = value ?? throw new ArgumentNullException(nameof(BorderBrush), "The menu border brush must not be null.");
	}

	/// <summary>Gets or initializes the menu background brush.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public required Brush Background
	{
		get => _background;
		init => _background = value ?? throw new ArgumentNullException(nameof(Background), "The menu background brush must not be null.");
	}

	/// <summary>Gets or initializes the menu foreground brush.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public required Brush Foreground
	{
		get => _foreground;
		init => _foreground = value ?? throw new ArgumentNullException(nameof(Foreground), "The menu foreground brush must not be null.");
	}

	/// <summary>
	/// Gets the default skin: the binding's default menu background, foreground, and border brushes.
	/// </summary>
	public static TextCodeActionMenuSkin Default { get; } = new()
	{
		BorderBrush = s_defaultBorderBrush,
		Background = s_defaultBackground,
		Foreground = s_defaultForeground
	};
}
