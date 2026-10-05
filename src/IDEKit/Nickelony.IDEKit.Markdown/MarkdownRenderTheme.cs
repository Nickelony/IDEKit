using Nickelony.IDEKit.Core.Media;
using Nickelony.IDEKit.Core.Validation;

namespace Nickelony.IDEKit.Markdown;

/// <summary>Defines the fonts, sizes, colors, and layout settings for rendering Markdown content.</summary>
/// <remarks>
/// <para>
/// <see cref="Default"/> is a self-contained platform baseline (the toolkit's default UI font, a Consolas/Courier New code font, black on white)
/// with tooltip-sized limits, not an integration with the host's own color palette or editor theme. Hosts that render dark or themed content
/// should construct a theme from their palette instead of relying on it.
/// </para>
/// <para>
/// A binding caches the brushes it derives (the code background and the border used by quotes, tables, thematic breaks, code blocks, and inline code)
/// per theme instance; a <c>with</c> expression produces a new instance with its own cache.
/// </para>
/// <para>
/// Instances are immutable. The assigned heading-font-size scales are copied on assignment, and record equality still compares the copied list
/// instance by reference, not its contents, so two themes with equal scales are not equal.
/// </para>
/// </remarks>
public sealed record MarkdownRenderTheme
{
	/// <summary>
	/// The inclusive upper bound applied to every font size: the largest font size WPF accepts, shared by the bindings so one theme stays valid
	/// on either engine; a larger assigned font size is rejected and a larger computed heading size is capped.
	/// </summary>
	public const double FontSizeUpperBound = 160000.0;

	/// <summary>
	/// The default source of the code font: concrete faces rather than a generic family, so a host with a reduced font set still renders code
	/// with a fixed-width face. A <see langword="null"/> or blank <see cref="CodeFontFamily"/> falls back to it.
	/// </summary>
	public const string DefaultCodeFontFamily = "Consolas, Courier New";

	private IReadOnlyList<double> _headingFontSizeScales = [1.30, 1.20, 1.12, 1.06, 1.03, 1.0];

	private double _bodyFontSize = 14.0;
	private double _codeFontSize = 13.0;
	private double _maxWidth = 540.0;
	private double _maxHeight = 420.0;
	private double _codeMaxWidth = 500.0;

	private int _maxVisibleCodeBlockLines = 14;

	private double _blockSpacing = 6.0;
	private double _codeBackgroundBlendRatio = 0.08;
	private double _borderBlendRatio = 0.18;

	/// <summary>The default theme: a self-contained platform baseline with tooltip-sized limits.</summary>
	public static MarkdownRenderTheme Default { get; } = new();

	/// <summary>
	/// The source of the font family used for body text; defaults to <see langword="null"/>, which uses the toolkit's default UI font.
	/// </summary>
	/// <remarks>
	/// The value is a font family source string (for example <c>Segoe UI</c>) that the binding passes to its own <c>FontFamily</c> constructor,
	/// so the theme stays independent of either toolkit's font type. A <see langword="null"/> or blank value selects the binding's default UI font.
	/// </remarks>
	public string? BodyFontFamily { get; init; }

	/// <summary>The font size used for body text; defaults to <c>14.0</c>.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is zero, negative, not finite, or above <c>160000</c>.</exception>
	public double BodyFontSize
	{
		get => _bodyFontSize;
		init => _bodyFontSize = ValidateFontSize(value, nameof(BodyFontSize));
	}

	/// <summary>The source of the font family used for code blocks and inline code; defaults to <c>"Consolas, Courier New"</c>.</summary>
	/// <remarks>
	/// The value is a font family source string that the binding passes to its own <c>FontFamily</c> constructor. Assign a different family source
	/// to match a host-specific theme. A <see langword="null"/> or blank value falls back to the default source.
	/// </remarks>
	public string? CodeFontFamily { get; init; } = DefaultCodeFontFamily;

	/// <summary>The font size used for code blocks and inline code; defaults to <c>13.0</c>.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is zero, negative, not finite, or above <c>160000</c>.</exception>
	public double CodeFontSize
	{
		get => _codeFontSize;
		init => _codeFontSize = ValidateFontSize(value, nameof(CodeFontSize));
	}

	/// <summary>The neutral foreground color used for regular rendered text and code; defaults to black.</summary>
	public ArgbColor Foreground { get; init; } = ArgbColor.Black;

	/// <summary>
	/// The neutral surface color from which code backgrounds and borders are derived; defaults to white, and it is not applied as the viewer
	/// background because the rendered content is transparent so the hosting surface supplies the visible background.
	/// </summary>
	public ArgbColor SurfaceBackground { get; init; } = ArgbColor.White;

	/// <summary>The neutral foreground color used for hyperlinks; defaults to a shareable medium blue.</summary>
	public ArgbColor LinkForeground { get; init; } = new(A: 0xFF, R: 0x00, G: 0x66, B: 0xCC);

	/// <summary>The flow direction of the rendered content; defaults to <see cref="MarkdownFlowDirection.LeftToRight"/>.</summary>
	public MarkdownFlowDirection FlowDirection { get; init; } = MarkdownFlowDirection.LeftToRight;

	/// <summary>
	/// The maximum width of the rendered content; defaults to <c>540.0</c>, and <see cref="double.PositiveInfinity"/> means unbounded.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is zero, negative, or NaN.</exception>
	public double MaxWidth
	{
		get => _maxWidth;
		init => _maxWidth = NumericValidation.Positive(value, nameof(MaxWidth));
	}

	/// <summary>
	/// The maximum height of the rendered content; defaults to <c>420.0</c>, and <see cref="double.PositiveInfinity"/> means unbounded.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is zero, negative, or NaN.</exception>
	public double MaxHeight
	{
		get => _maxHeight;
		init => _maxHeight = NumericValidation.Positive(value, nameof(MaxHeight));
	}

	/// <summary>
	/// The maximum width of code blocks and inline code; defaults to <c>500.0</c>, and <see cref="double.PositiveInfinity"/> means unbounded.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is zero, negative, or NaN.</exception>
	/// <remarks>
	/// Body text is not constrained by this value: it wraps at the width the rendered content is given, up to <see cref="MaxWidth"/>.
	/// Code is constrained because code wraps and scrolls as a block of its own, independently of the text around it.
	/// </remarks>
	public double CodeMaxWidth
	{
		get => _codeMaxWidth;
		init => _codeMaxWidth = NumericValidation.Positive(value, nameof(CodeMaxWidth));
	}

	/// <summary>The blend ratio that derives the code background from <see cref="SurfaceBackground"/>; defaults to <c>0.08</c>.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is NaN.</exception>
	/// <remarks>
	/// The surface color is blended toward the contrasting pole (black on light surfaces, white on dark surfaces) so the code
	/// background stays distinguishable. Values outside <c>0.0</c> to <c>1.0</c> are clamped on assignment.
	/// </remarks>
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
	/// The blend ratio that derives code borders, quote bars, table cell borders, and thematic breaks from <see cref="SurfaceBackground"/>;
	/// defaults to <c>0.18</c>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is NaN.</exception>
	/// <remarks>
	/// The surface color is blended toward black on light surfaces and toward white on dark surfaces so the derived color contrasts
	/// with the visible surface. Values outside <c>0.0</c> to <c>1.0</c> are clamped on assignment.
	/// </remarks>
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

	/// <summary>The number of visual lines after which a code block starts scrolling; defaults to <c>14</c>.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is less than <c>1</c>.</exception>
	/// <remarks>
	/// <para>
	/// The limit counts visual lines rather than source lines, because wrapped content takes more than one line height per source line.
	/// </para>
	/// <para>
	/// Content beyond the limit scrolls into view when scrolling is allowed, and otherwise clips.
	/// </para>
	/// </remarks>
	public int MaxVisibleCodeBlockLines
	{
		get => _maxVisibleCodeBlockLines;
		init
		{
			ArgumentOutOfRangeException.ThrowIfLessThan(value, 1, nameof(MaxVisibleCodeBlockLines));
			_maxVisibleCodeBlockLines = value;
		}
	}

	/// <summary>The vertical spacing used between block elements and below paragraphs; defaults to <c>6.0</c>.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative or not finite.</exception>
	/// <remarks>
	/// Headings, thematic breaks, and code blocks use the value for their top and bottom margins; paragraphs, quotes, lists, and tables use
	/// it for their bottom margin. Table cells suppress the vertical spacing of their content because the cell supplies its own padding,
	/// and the items of a tight list render without the spacing that separates the items of a loose list.
	/// </remarks>
	public double BlockSpacing
	{
		get => _blockSpacing;
		init => _blockSpacing = NumericValidation.FiniteNonNegative(value, nameof(BlockSpacing));
	}

	/// <summary>
	/// The font-size multiplier for each heading level, starting with level 1;
	/// defaults to <c>1.30</c>, <c>1.20</c>, <c>1.12</c>, <c>1.06</c>, <c>1.03</c>, and <c>1.0</c>.
	/// </summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException">The assigned collection contains a scale that is zero, negative, or not finite.</exception>
	/// <remarks>
	/// The assigned collection is copied on assignment. When the collection is empty, headings use the body font size. The last available multiplier
	/// is reused for deeper heading levels, and the computed size is capped at the theme's font-size upper bound (the largest size WPF accepts),
	/// so an extreme multiplier cannot fail the rendering.
	/// </remarks>
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
		{
			throw new ArgumentOutOfRangeException(
				propertyName,
				value,
				$"The value must not exceed {FontSizeUpperBound} (a conservative cap on the font size)."
			);
		}

		return fontSize;
	}
}
