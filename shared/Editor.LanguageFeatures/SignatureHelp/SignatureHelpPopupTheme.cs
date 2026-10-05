using Nickelony.IDEKit.Core.Validation;
#if AVALONIAEDIT
using Avalonia;
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

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SignatureHelp;

#else

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SignatureHelp;

#endif

/// <summary>Describes the frame a <see cref="SignatureHelpPresenter"/> draws around the content of its popup.</summary>
/// <remarks>
/// The theme supplies the frame only; the content the host builds keeps its own styling. Every value validates itself when it is assigned,
/// so a <see langword="null"/> brush or a negative measure is rejected at the offending object initializer. The brushes are expected
/// not to be mutated after the theme is applied.
/// </remarks>
public sealed record SignatureHelpPopupTheme
{
#if AVALONIAEDIT
	private static readonly Brush s_defaultBorderBrush = ArgbColor.FromRgb(0x80, 0x80, 0x80).ToBrush();
	private static readonly Brush s_defaultBackground = ArgbColor.FromRgb(0xFF, 0xFF, 0xFF).ToBrush();
#else
	private static readonly Brush s_defaultBorderBrush = SystemColors.ActiveBorderBrush;
	private static readonly Brush s_defaultBackground = SystemColors.WindowBrush;
#endif

	private Brush _background = s_defaultBackground;
	private Brush _borderBrush = s_defaultBorderBrush;

	private double _borderThickness = 1.0;
	private double _cornerRadius = 4.0;

	private Thickness _padding = new(left: 8.0, top: 6.0, right: 8.0, bottom: 6.0);
	private double _maxWidth = 600.0;

	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public Brush Background
	{
		get => _background;
		init => _background = value ?? throw new ArgumentNullException(nameof(Background), "The popup background brush must not be null.");
	}

	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public Brush BorderBrush
	{
		get => _borderBrush;
		init => _borderBrush = value ?? throw new ArgumentNullException(nameof(BorderBrush), "The popup border brush must not be null.");
	}

	/// <summary>The thickness of the border on every side; defaults to <c>1</c>.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double BorderThickness
	{
		get => _borderThickness;
		init => _borderThickness = NumericValidation.FiniteNonNegative(value, nameof(BorderThickness));
	}

	/// <summary>The radius of every corner of the frame; defaults to <c>4</c>.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double CornerRadius
	{
		get => _cornerRadius;
		init => _cornerRadius = NumericValidation.FiniteNonNegative(value, nameof(CornerRadius));
	}

	/// <summary>The space between the border and the content; defaults to <c>8</c> on the sides and <c>6</c> above and below.</summary>
	/// <exception cref="ArgumentOutOfRangeException">A side of the assigned value is negative or not finite.</exception>
	public Thickness Padding
	{
		get => _padding;
		init => _padding = FeaturePopupPresenter.ValidatePadding(value, nameof(Padding));
	}

	/// <summary>The widest the frame grows before the content wraps; defaults to <c>600</c>, and positive infinity lifts the bound.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is zero, negative, or NaN.</exception>
	public double MaxWidth
	{
		get => _maxWidth;
		init => _maxWidth = NumericValidation.Positive(value, nameof(MaxWidth));
	}

	/// <summary>The default theme: the binding's default window chrome with a one-unit border.</summary>
	public static SignatureHelpPopupTheme Default { get; } = new();
}
