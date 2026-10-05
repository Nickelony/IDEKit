#if AVALONIAQUICKPICK
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Brush = Avalonia.Media.IBrush;
using RowTemplate = Avalonia.Controls.Templates.IDataTemplate;
#else
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using RowTemplate = System.Windows.DataTemplate;
#endif

#if AVALONIAQUICKPICK

namespace Nickelony.QuickPick.Avalonia;

#else

namespace Nickelony.QuickPick.Wpf;

#endif

/// <summary>
/// Describes the look of the window a <see cref="QuickPickPresenter"/> draws: its colors, its frame, the size of its list and the template
/// of its rows.
/// </summary>
/// <remarks>
/// <para>
/// The brushes color the frame, the text and the active row. The description, the trailing text and a separator are drawn at reduced opacity
/// of the text color, so they follow the foreground of the row and stay readable on the active background.
/// </para>
/// <para>
/// Every value validates itself when it is assigned, so a <see langword="null"/> brush or a negative measure is rejected
/// at the offending object initializer. The brushes are expected not to be mutated after the theme is applied.
/// </para>
/// </remarks>
public sealed record QuickPickTheme
{
#if AVALONIAQUICKPICK
	private static readonly Brush s_defaultBackground = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
	private static readonly Brush s_defaultForeground = new ImmutableSolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
	private static readonly Brush s_defaultBorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
	private static readonly Brush s_defaultActiveBackground = new ImmutableSolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
	private static readonly Brush s_defaultActiveForeground = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
#else
	private static readonly Brush s_defaultBackground = SystemColors.WindowBrush;
	private static readonly Brush s_defaultForeground = SystemColors.WindowTextBrush;
	private static readonly Brush s_defaultBorderBrush = SystemColors.ActiveBorderBrush;
	private static readonly Brush s_defaultActiveBackground = SystemColors.HighlightBrush;
	private static readonly Brush s_defaultActiveForeground = SystemColors.HighlightTextBrush;
#endif

	private Brush _background = s_defaultBackground;
	private Brush _foreground = s_defaultForeground;
	private Brush _borderBrush = s_defaultBorderBrush;
	private Brush _activeBackground = s_defaultActiveBackground;
	private Brush _activeForeground = s_defaultActiveForeground;

	private double _borderThickness = 1.0;
	private double _cornerRadius = 6.0;

	private Thickness _padding = new(6.0);

	private double _width = 600.0;
	private double _topOffset = 12.0;
	private double _rowHeight = 24.0;

	private int _maxVisibleRows = 10;

	/// <summary>The background of the window.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public Brush Background
	{
		get => _background;
		init => _background = value ?? throw new ArgumentNullException(nameof(Background), "The window background brush must not be null.");
	}

	/// <summary>The color of the text of the input and of the rows.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public Brush Foreground
	{
		get => _foreground;
		init => _foreground = value ?? throw new ArgumentNullException(nameof(Foreground), "The window foreground brush must not be null.");
	}

	/// <summary>The color of the border of the window.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public Brush BorderBrush
	{
		get => _borderBrush;
		init => _borderBrush = value ?? throw new ArgumentNullException(nameof(BorderBrush), "The window border brush must not be null.");
	}

	/// <summary>The background of the active row, which the busy bar also uses.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public Brush ActiveBackground
	{
		get => _activeBackground;
		init =>
			_activeBackground =
				value ?? throw new ArgumentNullException(nameof(ActiveBackground), "The active row background brush must not be null.");
	}

	/// <summary>The color of the text of the active row.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public Brush ActiveForeground
	{
		get => _activeForeground;
		init =>
			_activeForeground =
				value ?? throw new ArgumentNullException(nameof(ActiveForeground), "The active row foreground brush must not be null.");
	}

	/// <summary>The thickness of the border on every side; defaults to <c>1</c>.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double BorderThickness
	{
		get => _borderThickness;
		init => _borderThickness = FiniteNonNegative(value, nameof(BorderThickness));
	}

	/// <summary>The radius of every corner of the frame; defaults to <c>6</c>.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double CornerRadius
	{
		get => _cornerRadius;
		init => _cornerRadius = FiniteNonNegative(value, nameof(CornerRadius));
	}

	/// <summary>The space between the border and the content; defaults to <c>6</c> on every side.</summary>
	/// <exception cref="ArgumentOutOfRangeException">A side of the assigned value is negative or not finite.</exception>
	public Thickness Padding
	{
		get => _padding;
		init
		{
			bool isValid =
				IsFiniteNonNegative(value.Left)
				&& IsFiniteNonNegative(value.Top)
				&& IsFiniteNonNegative(value.Right)
				&& IsFiniteNonNegative(value.Bottom);

			_padding = isValid
				? value
				: throw new ArgumentOutOfRangeException(nameof(Padding), value, "Every side of the padding must be finite and non-negative.");
		}
	}

	/// <summary>The width of the window; defaults to <c>600</c>. A host narrower than the window gets a window that fits.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is zero, negative, or not finite.</exception>
	public double Width
	{
		get => _width;
		init => _width = FinitePositive(value, nameof(Width));
	}

	/// <summary>The distance between the top of the host and the window; defaults to <c>12</c>.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	public double TopOffset
	{
		get => _topOffset;
		init => _topOffset = FiniteNonNegative(value, nameof(TopOffset));
	}

	/// <summary>The least height of a row; defaults to <c>24</c>.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is zero, negative, or not finite.</exception>
	public double RowHeight
	{
		get => _rowHeight;
		init => _rowHeight = FinitePositive(value, nameof(RowHeight));
	}

	/// <summary>The number of rows the list shows before it scrolls, and the number a page key moves; defaults to <c>10</c>.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is not positive.</exception>
	public int MaxVisibleRows
	{
		get => _maxVisibleRows;
		init
		{
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
			_maxVisibleRows = value;
		}
	}

	/// <summary>Replaces the template of a row, or <see langword="null"/> to draw the default row.</summary>
	/// <remarks>
	/// The data context of the template is the <see cref="QuickPickRow"/> of the row: a separator, or an item with its highlight ranges.
	/// The template draws every row, so it also draws the separators, and the icon hook of the presenter is ignored because the template draws
	/// its own icons.
	/// </remarks>
	public RowTemplate? ItemTemplate { get; init; }

	/// <summary>The default theme: the default window colors of the toolkit.</summary>
	public static QuickPickTheme Default { get; } = new();

	private static bool IsFiniteNonNegative(double value) => double.IsFinite(value) && value >= 0.0;

	private static double FiniteNonNegative(double value, string name) =>
		IsFiniteNonNegative(value) ? value : throw new ArgumentOutOfRangeException(name, value, "The value must be finite and non-negative.");

	private static double FinitePositive(double value, string name)
	{
		return double.IsFinite(value) && value > 0.0
			? value
			: throw new ArgumentOutOfRangeException(name, value, "The value must be finite and greater than zero.");
	}
}
