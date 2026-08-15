#if AVALONIAEDIT
using Avalonia.Media;
using Brush = Avalonia.Media.IBrush;
#else
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
#endif
using Nickelony.IDEKit.Infrastructure;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
#endif

/// <summary>
/// Describes the default chrome applied to a created completion window.
/// </summary>
/// <remarks>
/// <para>
/// The skin supplies the window chrome - its border thickness and colors; item templates keep their own
/// styling. The window is never user-resizable and keeps the editor's borderless frame style, so a host that
/// wants a different frame shape overrides it in the
/// <see cref="TextCompletionControllerHooks.ConfigureWindow"/> hook. The brushes are assigned to the window
/// when it is created and are expected not to be mutated afterwards.
/// </para>
/// <para>
/// Every member is an init-only property rather than a positional parameter, matching
/// <see cref="CompletionTooltipSkin"/>: a construction site names the brushes instead of relying on the order
/// of three adjacent <see cref="Brush"/> arguments. Use <see cref="Default"/> as a baseline and override only
/// the values that differ. Every value validates itself when it is assigned, so a null brush or an invalid
/// thickness is rejected at the offending object initializer instead of when the controller is created.
/// </para>
/// </remarks>
public sealed record CompletionWindowSkin
{
#if AVALONIAEDIT
	private static readonly Brush s_defaultBorderBrush = BrushHelpers.CreateFrozenBrush("#FF808080");
	private static readonly Brush s_defaultBackground = BrushHelpers.CreateFrozenBrush("#FFFFFFFF");
	private static readonly Brush s_defaultForeground = BrushHelpers.CreateFrozenBrush("#FF1E1E1E");
#else
	private static readonly Brush s_defaultBorderBrush = SystemColors.ActiveBorderBrush;
	private static readonly Brush s_defaultBackground = SystemColors.WindowBrush;
	private static readonly Brush s_defaultForeground = SystemColors.WindowTextBrush;
#endif

	private Brush _borderBrush = s_defaultBorderBrush;
	private Brush _background = s_defaultBackground;
	private Brush _foreground = s_defaultForeground;
	private double _borderThickness = 1.0;

	/// <summary>Gets or initializes the window border brush.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public required Brush BorderBrush
	{
		get => _borderBrush;
		init => _borderBrush = value ?? throw new ArgumentNullException(nameof(BorderBrush), "The window border brush must not be null.");
	}

	/// <summary>Gets or initializes the window background brush.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public required Brush Background
	{
		get => _background;
		init => _background = value ?? throw new ArgumentNullException(nameof(Background), "The window background brush must not be null.");
	}

	/// <summary>Gets or initializes the window foreground brush.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public required Brush Foreground
	{
		get => _foreground;
		init => _foreground = value ?? throw new ArgumentNullException(nameof(Foreground), "The window foreground brush must not be null.");
	}

	/// <summary>Gets or initializes the window border thickness. Defaults to <c>1</c>.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double BorderThickness
	{
		get => _borderThickness;
		init => _borderThickness = NumericValidation.FiniteNonNegative(value, nameof(BorderThickness));
	}

	/// <summary>
	/// Gets the default skin: the binding's default window chrome with a one-unit border.
	/// </summary>
	public static CompletionWindowSkin Default { get; } = new()
	{
		BorderBrush = s_defaultBorderBrush,
		Background = s_defaultBackground,
		Foreground = s_defaultForeground
	};
}
