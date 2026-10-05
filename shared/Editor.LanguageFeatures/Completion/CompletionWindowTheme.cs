using Nickelony.IDEKit.Core.Validation;
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

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

#endif

/// <summary>Describes the default chrome applied to a created completion window.</summary>
/// <remarks>
/// <para>
/// The theme supplies the window chrome - its border thickness and colors; item templates keep their own styling.
/// The window is never user-resizable and keeps the editor's borderless frame style, so a host that wants a different frame shape overrides
/// it in the <see cref="CompletionPresenterHooks.ConfigureWindow"/> hook. The brushes are assigned to the window when it is created and are expected
/// not to be mutated afterwards.
/// </para>
/// <para>
/// Every member is an init-only property rather than a positional parameter, like the other themes of the binding:
/// a construction site names the brushes instead of relying on the order of adjacent <see cref="Brush"/> arguments. Every brush and the thickness
/// already carry a non-<see langword="null"/> default, so a <c>new</c> initializer may set only the members it changes and <see cref="Default"/>
/// carries the binding's baseline chrome. Every value validates itself when it is assigned, so a <see langword="null"/> brush or an invalid thickness
/// is rejected at the offending object initializer instead of when the presenter is created.
/// </para>
/// </remarks>
public sealed record CompletionWindowTheme
{
#if AVALONIAEDIT
	private static readonly Brush s_defaultBorderBrush = ArgbColor.FromRgb(0x80, 0x80, 0x80).ToBrush();
	private static readonly Brush s_defaultBackground = ArgbColor.FromRgb(0xFF, 0xFF, 0xFF).ToBrush();
	private static readonly Brush s_defaultForeground = ArgbColor.FromRgb(0x1E, 0x1E, 0x1E).ToBrush();
#else
	private static readonly Brush s_defaultBorderBrush = SystemColors.ActiveBorderBrush;
	private static readonly Brush s_defaultBackground = SystemColors.WindowBrush;
	private static readonly Brush s_defaultForeground = SystemColors.WindowTextBrush;
#endif

	private Brush _borderBrush = s_defaultBorderBrush;
	private Brush _background = s_defaultBackground;
	private Brush _foreground = s_defaultForeground;

	private double _borderThickness = 1.0;

	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public Brush BorderBrush
	{
		get => _borderBrush;
		init => _borderBrush = value ?? throw new ArgumentNullException(nameof(BorderBrush), "The window border brush must not be null.");
	}

	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public Brush Background
	{
		get => _background;
		init => _background = value ?? throw new ArgumentNullException(nameof(Background), "The window background brush must not be null.");
	}

	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public Brush Foreground
	{
		get => _foreground;
		init => _foreground = value ?? throw new ArgumentNullException(nameof(Foreground), "The window foreground brush must not be null.");
	}

	/// <summary>The window border thickness; defaults to <c>1</c>.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double BorderThickness
	{
		get => _borderThickness;
		init => _borderThickness = NumericValidation.FiniteNonNegative(value, nameof(BorderThickness));
	}

	/// <summary>The default theme: the binding's default window chrome with a one-unit border.</summary>
	public static CompletionWindowTheme Default { get; } =
		new()
		{
			BorderBrush = s_defaultBorderBrush,
			Background = s_defaultBackground,
			Foreground = s_defaultForeground,
		};
}
