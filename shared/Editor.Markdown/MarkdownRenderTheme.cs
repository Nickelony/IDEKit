using Nickelony.IDEKit.Infrastructure;
#if AVALONIAEDIT
using Avalonia.Media;
using MarkdownBrush = Avalonia.Media.ISolidColorBrush;
#else
using System.Windows;
using System.Windows.Media;
using MarkdownBrush = System.Windows.Media.SolidColorBrush;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;
#else
namespace Nickelony.IDEKit.AvalonEdit.Markdown;
#endif

/// <summary>
/// Defines the fonts, sizes, brushes, and layout settings used to render Markdown content.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Default"/> is a self-contained platform baseline (the toolkit's default UI font, a
/// Consolas/Courier New code font, black on white) with tooltip-sized limits, not an integration
/// with the host's own color palette or editor theme. Hosts that render dark or themed content
/// should construct a theme from their palette instead of relying on it.
/// </para>
/// <para>
/// Derived brushes (the code background and the border used by quotes, tables, thematic breaks, code
/// blocks, and inline code) are cached per theme instance; a <c>with</c> expression produces a new
/// instance with its own cache.
/// </para>
/// <para>
/// Instances are immutable. The assigned heading-font-size scales are copied on assignment, and record
/// equality still compares the copied list instance by reference, not its contents, so two themes with
/// equal scales are not equal.
/// </para>
/// </remarks>
public sealed record MarkdownRenderTheme
{
	/// <summary>
	/// The largest font size the toolkit accepts. A larger assigned font size is rejected, and a larger
	/// computed heading size is capped, so an extreme value cannot fail the rendering.
	/// </summary>
	internal const double FontSizeUpperBound = 160000.0;

#if AVALONIAEDIT
	private static readonly FontFamily s_defaultBodyFontFamily = FontFamily.Default;
#else
	private static readonly FontFamily s_defaultBodyFontFamily = SystemFonts.MessageFontFamily;
#endif

	private IReadOnlyList<double> _headingFontSizeScales = [1.30, 1.20, 1.12, 1.06, 1.03, 1.0];
	private MarkdownBrush _foreground = Brushes.Black;
	private MarkdownBrush _surfaceBackground = Brushes.White;
	private MarkdownBrush _linkForeground = BrushHelpers.CreateFrozenBrush(Color.FromRgb(0, 102, 204));
	private double _bodyFontSize = 14.0;
	private double _codeFontSize = 13.0;
	private double _maxWidth = 540.0;
	private double _maxHeight = 420.0;
	private double _codeMaxWidth = 500.0;
	private int _maxVisibleCodeBlockLines = 14;
	private double _blockSpacing = 6.0;
	private double _codeBackgroundBlendRatio = 0.32;
	private double _borderBlendRatio = 0.18;

	/// <summary>Gets the default theme: a self-contained platform baseline with tooltip-sized limits.</summary>
	public static MarkdownRenderTheme Default { get; } = new();

	/// <summary>Gets or initializes the font family used for body text. Defaults to the toolkit's default UI font.</summary>
	public FontFamily BodyFontFamily { get; init; } = s_defaultBodyFontFamily;

	/// <summary>
	/// Gets or initializes the font size used for body text. Defaults to <c>14.0</c>. Values must be
	/// positive and finite and must not exceed the largest font size the toolkit accepts.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The assigned value is zero, negative, not finite, or above <c>160000</c>.
	/// </exception>
	public double BodyFontSize
	{
		get => _bodyFontSize;
		init
		{
			_bodyFontSize = ValidateFontSize(value, nameof(BodyFontSize));
		}
	}

	/// <summary>
	/// Gets or initializes the font family used for code blocks and inline code. Defaults to a
	/// monospace fallback list that starts with a Windows monospace font and falls back to a widely
	/// available fixed-width family.
	/// </summary>
	/// <remarks>
	/// The default prefers <c>Consolas</c>, a Windows monospace font, and falls back to
	/// <c>Courier New</c> so a host with a reduced font set still renders code with a fixed-width
	/// face. It names concrete faces rather than a generic family, so assign a different
	/// <see cref="FontFamily"/> to match a host-specific theme.
	/// </remarks>
	public FontFamily CodeFontFamily { get; init; } = new("Consolas, Courier New");

	/// <summary>
	/// Gets or initializes the font size used for code blocks and inline code. Defaults to <c>13.0</c>.
	/// Values must be positive and finite and must not exceed the largest font size the toolkit accepts.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The assigned value is zero, negative, not finite, or above <c>160000</c>.
	/// </exception>
	public double CodeFontSize
	{
		get => _codeFontSize;
		init
		{
			_codeFontSize = ValidateFontSize(value, nameof(CodeFontSize));
		}
	}

	/// <summary>
	/// Gets or initializes the foreground brush used for regular rendered text and code. Defaults to black.
	/// </summary>
	/// <remarks>
	/// The assigned brush is applied as-is, so a host may change its <see cref="MarkdownBrush.Color"/>
	/// between renders to re-theme content; the derived-brush cache keys on the color it last derived from,
	/// so a change re-derives the dependent brushes.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// The assigned value is <see langword="null"/>.
	/// </exception>
	public MarkdownBrush Foreground
	{
		get => _foreground;
		init
		{
			ArgumentNullException.ThrowIfNull(value, nameof(Foreground));

			_foreground = value;
		}
	}

	/// <summary>
	/// Gets or initializes the surface background from which code backgrounds and borders are derived.
	/// Defaults to white. It is not applied as the viewer background; the rendered content is
	/// transparent so that the hosting surface supplies the visible background.
	/// </summary>
	/// <remarks>
	/// The derived brush set caches against this instance keyed by the surface color, so mutating the
	/// assigned brush's <see cref="MarkdownBrush.Color"/> between renders re-derives the dependent
	/// brushes instead of leaving the previous colors in place.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// The assigned value is <see langword="null"/>.
	/// </exception>
	public MarkdownBrush SurfaceBackground
	{
		get => _surfaceBackground;
		init
		{
			ArgumentNullException.ThrowIfNull(value, nameof(SurfaceBackground));

			_surfaceBackground = value;
		}
	}

	/// <summary>
	/// Gets or initializes the foreground brush used for hyperlinks. Defaults to a shareable medium blue.
	/// </summary>
	/// <exception cref="ArgumentNullException">
	/// The assigned value is <see langword="null"/>.
	/// </exception>
	public MarkdownBrush LinkForeground
	{
		get => _linkForeground;
		init
		{
			ArgumentNullException.ThrowIfNull(value, nameof(LinkForeground));

			_linkForeground = value;
		}
	}

	/// <summary>
	/// Gets or initializes the flow direction of the rendered content. Defaults to
	/// <see cref="FlowDirection.LeftToRight"/>.
	/// </summary>
	public FlowDirection FlowDirection { get; init; } = FlowDirection.LeftToRight;

	/// <summary>
	/// Gets or initializes the maximum width of the rendered content. Defaults to <c>540.0</c>. The
	/// value must be positive; <see cref="double.PositiveInfinity"/> means unbounded.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The assigned value is zero, negative, or NaN.
	/// </exception>
	public double MaxWidth
	{
		get => _maxWidth;
		init
		{
			_maxWidth = NumericValidation.Positive(value, nameof(MaxWidth));
		}
	}

	/// <summary>
	/// Gets or initializes the maximum height of the rendered content. Defaults to <c>420.0</c>. The
	/// value must be positive; <see cref="double.PositiveInfinity"/> means unbounded.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The assigned value is zero, negative, or NaN.
	/// </exception>
	public double MaxHeight
	{
		get => _maxHeight;
		init
		{
			_maxHeight = NumericValidation.Positive(value, nameof(MaxHeight));
		}
	}

	/// <summary>
	/// Gets or initializes the maximum width of code blocks and inline code. Defaults to <c>500.0</c>.
	/// The value must be positive; <see cref="double.PositiveInfinity"/> means unbounded.
	/// </summary>
	/// <remarks>
	/// Body text is not constrained by this value: it wraps at the width the rendered content is given,
	/// up to <see cref="MaxWidth"/>. Code is constrained because code wraps and scrolls as a block of
	/// its own, independently of the text around it.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The assigned value is zero, negative, or NaN.
	/// </exception>
	public double CodeMaxWidth
	{
		get => _codeMaxWidth;
		init
		{
			_codeMaxWidth = NumericValidation.Positive(value, nameof(CodeMaxWidth));
		}
	}

	/// <summary>
	/// Gets or initializes the blend ratio used to derive the code background from
	/// <see cref="SurfaceBackground"/>. Defaults to <c>0.32</c>. The surface color is blended toward
	/// the contrasting pole (black on light surfaces, white on dark surfaces) so the code background
	/// stays distinguishable on any surface. Values outside the range from <c>0.0</c> to <c>1.0</c> are
	/// clamped on assignment.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The assigned value is NaN.
	/// </exception>
	public double CodeBackgroundBlendRatio
	{
		get => _codeBackgroundBlendRatio;
		init
		{
			if (double.IsNaN(value))
				throw new ArgumentOutOfRangeException(nameof(CodeBackgroundBlendRatio), value, "The value must not be NaN.");

			_codeBackgroundBlendRatio = Math.Clamp(value, 0.0, 1.0);
		}
	}

	/// <summary>
	/// Gets or initializes the blend ratio used to derive code borders, quote bars, table cell borders,
	/// and thematic breaks from <see cref="SurfaceBackground"/>. Defaults to <c>0.18</c>. The surface
	/// color is blended toward black on light surfaces and toward white on dark surfaces, so the derived
	/// color contrasts with the visible surface in both cases. Values outside the range from <c>0.0</c>
	/// to <c>1.0</c> are clamped on assignment.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The assigned value is NaN.
	/// </exception>
	public double BorderBlendRatio
	{
		get => _borderBlendRatio;
		init
		{
			if (double.IsNaN(value))
				throw new ArgumentOutOfRangeException(nameof(BorderBlendRatio), value, "The value must not be NaN.");

			_borderBlendRatio = Math.Clamp(value, 0.0, 1.0);
		}
	}

	/// <summary>
	/// Gets or initializes the number of visual lines after which a code block starts scrolling.
	/// Defaults to <c>14</c>. Wrapped content can use more than one line height per source line, so the
	/// effective limit is a visual-line count rather than a source-line count. When scrolling is
	/// allowed, additional content can be scrolled into view; otherwise, the block clips the overflow.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The assigned value is less than <c>1</c>.
	/// </exception>
	public int MaxVisibleCodeBlockLines
	{
		get => _maxVisibleCodeBlockLines;
		init
		{
			ArgumentOutOfRangeException.ThrowIfLessThan(value, 1, nameof(MaxVisibleCodeBlockLines));
			_maxVisibleCodeBlockLines = value;
		}
	}

	/// <summary>
	/// Gets or initializes the vertical spacing used between block elements and below paragraphs.
	/// Defaults to <c>6.0</c>.
	/// </summary>
	/// <remarks>
	/// Headings, thematic breaks, and code blocks use the value for their top and bottom margins;
	/// paragraphs, quotes, lists, and tables use it for their bottom margin. Table cells suppress the
	/// vertical spacing of their content because the cell supplies its own padding, and the items of a
	/// tight list render without the spacing that separates the items of a loose list. Values must be
	/// non-negative and finite.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The assigned value is negative or not finite.
	/// </exception>
	public double BlockSpacing
	{
		get => _blockSpacing;
		init
		{
			_blockSpacing = NumericValidation.FiniteNonNegative(value, nameof(BlockSpacing));
		}
	}

	/// <summary>
	/// Gets or initializes the font-size multiplier for each heading level, starting with level 1.
	/// Defaults to <c>1.30</c>, <c>1.20</c>, <c>1.12</c>, <c>1.06</c>, <c>1.03</c>, and <c>1.0</c>.
	/// </summary>
	/// <remarks>
	/// The assigned collection is copied on assignment. When the collection is empty, headings use the
	/// body font size. The last available multiplier is reused for deeper heading levels, and the
	/// computed size is capped at the largest font size the toolkit accepts, so an extreme multiplier
	/// cannot fail the rendering. Values must be positive and finite.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// The assigned value is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The assigned collection contains a scale that is zero, negative, or not finite.
	/// </exception>
	public IReadOnlyList<double> HeadingFontSizeScales
	{
		get => _headingFontSizeScales;
		init
		{
			ArgumentNullException.ThrowIfNull(value);

			foreach (double scale in value)
				NumericValidation.FinitePositive(scale, nameof(HeadingFontSizeScales));

			_headingFontSizeScales = Array.AsReadOnly([.. value]);
		}
	}

	private static double ValidateFontSize(double value, string propertyName)
	{
		double fontSize = NumericValidation.FinitePositive(value, propertyName);

		if (fontSize > FontSizeUpperBound)
			throw new ArgumentOutOfRangeException(propertyName, value, $"The value must not exceed {FontSizeUpperBound} (the largest font size the toolkit accepts).");

		return fontSize;
	}
}
