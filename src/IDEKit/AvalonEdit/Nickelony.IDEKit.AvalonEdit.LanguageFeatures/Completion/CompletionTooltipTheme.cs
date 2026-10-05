using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Nickelony.IDEKit.Core.Validation;
using Brush = System.Windows.Media.Brush;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

/// <summary>Describes the chrome the completion presenter applies to the editor's completion tooltip.</summary>
/// <remarks>
/// <para>
/// The theme covers what the presenter places and colors: the tooltip is anchored to the right of the item
/// list by default, with zero border and padding, and the nullable brushes leave the active theme's tooltip colors in place (the editor itself sets
/// no tooltip colors). Every non-nullable value - placement, offset, border, and padding - is applied unconditionally, so a host that wants different
/// chrome overrides these values or configures the tooltip in the <see cref="CompletionPresenterHooks.ConfigureTooltip"/> hook. That hook runs
/// after the theme, so it can override any value the theme applied and style whatever the record does not cover.
/// </para>
/// <para>
/// The presenter anchors the tooltip to the item list rather than to the completion window, and the theme's placement and offset are applied
/// to that item list.
/// </para>
/// <para>
/// The chrome is applied to the tooltip the editor creates for each completion window. When the editor does not expose that tooltip,
/// a host cannot observe the theme on the running engine: the engine-owned tooltip is used unchanged and the one-time access warning is logged.
/// </para>
/// </remarks>
public sealed record CompletionTooltipTheme
{
	private double _horizontalOffset = 10.0;

	private Thickness _borderThickness;
	private Thickness _padding;

	/// <summary>The side of the item list the tooltip is placed on; defaults to <see cref="PlacementMode.Right"/>.</summary>
	public PlacementMode Placement { get; init; } = PlacementMode.Right;

	/// <summary>
	/// The horizontal offset between the item list and the tooltip, in device-independent pixels; the value must be finite, and the default is 10.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is not finite.</exception>
	public double HorizontalOffset
	{
		get => _horizontalOffset;
		init => _horizontalOffset = NumericValidation.Finite(value, nameof(HorizontalOffset));
	}

	/// <summary>The tooltip border thickness; each side must be finite and non-negative, and the default is zero.</summary>
	/// <exception cref="ArgumentOutOfRangeException">An assigned thickness value is negative or not finite.</exception>
	public Thickness BorderThickness
	{
		get => _borderThickness;
		init => _borderThickness = ValidateThickness(value, nameof(BorderThickness));
	}

	/// <summary>The tooltip padding; each side must be finite and non-negative, and the default is zero.</summary>
	/// <exception cref="ArgumentOutOfRangeException">An assigned padding value is negative or not finite.</exception>
	public Thickness Padding
	{
		get => _padding;
		init => _padding = ValidateThickness(value, nameof(Padding));
	}

	/// <summary>The tooltip background brush, or <see langword="null"/> to keep the active theme's tooltip background.</summary>
	public Brush? Background { get; init; }

	/// <summary>The tooltip border brush, or <see langword="null"/> to keep the active theme's tooltip border.</summary>
	public Brush? BorderBrush { get; init; }

	/// <summary>
	/// The default tooltip theme: placed to the right of the item list at the default <see cref="HorizontalOffset"/>, with zero border and padding
	/// and the active theme's tooltip colors.
	/// </summary>
	public static CompletionTooltipTheme Default { get; } = new();

	private static Thickness ValidateThickness(Thickness thickness, string propertyName)
	{
		if (
			!NumericValidation.IsFiniteNonNegative(thickness.Left)
			|| !NumericValidation.IsFiniteNonNegative(thickness.Top)
			|| !NumericValidation.IsFiniteNonNegative(thickness.Right)
			|| !NumericValidation.IsFiniteNonNegative(thickness.Bottom)
		)
		{
			throw new ArgumentOutOfRangeException(propertyName, thickness, "The tooltip theme thickness values must be finite and non-negative.");
		}

		return thickness;
	}
}
