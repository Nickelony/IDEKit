#if AVALONIAEDIT
using Avalonia;
using Avalonia.Controls;
using Brush = Avalonia.Media.IBrush;
#else
using System.Windows;
using System.Windows.Controls.Primitives;
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
/// Describes the chrome the completion controller applies to the editor's completion tooltip.
/// </summary>
/// <remarks>
/// <para>
/// The skin covers what the controller places and colors: the tooltip is anchored to the right of the item
/// list by default, with zero border and padding, and the nullable brushes leave the active theme's tooltip
/// colors in place (the editor itself sets no tooltip colors). Every non-nullable value - placement, offset,
/// border, and padding - is applied unconditionally, so a host that wants different chrome overrides these
/// values or configures the tooltip in the <see cref="TextCompletionControllerHooks.ConfigureTooltip"/> hook.
/// That hook runs after the skin, so it can override any value the skin applied and style whatever the record
/// does not cover.
/// </para>
/// <para>
/// The presenter anchors the tooltip to the item list rather than to the completion window, and the skin's
/// placement and offset are applied to that item list.
/// </para>
/// <para>
/// The chrome is applied to the tooltip the editor creates for each completion window. When the editor does
/// not expose that tooltip, a host cannot observe the skin on the running engine: the engine-owned tooltip is
/// used unchanged and the one-time access warning is logged.
/// </para>
/// </remarks>
public sealed record CompletionTooltipSkin
{
	private double _horizontalOffset = 10.0;
	private Thickness _borderThickness;
	private Thickness _padding;

	/// <summary>
	/// Gets the side of the item list the tooltip is placed on. Defaults to
	/// <see cref="PlacementMode.Right"/>.
	/// </summary>
	public PlacementMode Placement { get; init; } = PlacementMode.Right;

	/// <summary>
	/// Gets the horizontal offset between the item list and the tooltip in device-independent pixels.
	/// Must be finite. Defaults to 10.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is not finite.</exception>
	public double HorizontalOffset
	{
		get => _horizontalOffset;
		init => _horizontalOffset = NumericValidation.Finite(value, nameof(HorizontalOffset));
	}

	/// <summary>
	/// Gets the tooltip border thickness. Must be finite and non-negative. Defaults to zero.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// An assigned thickness value is negative or not finite.
	/// </exception>
	public Thickness BorderThickness
	{
		get => _borderThickness;
		init => _borderThickness = ValidateThickness(value, nameof(BorderThickness));
	}

	/// <summary>
	/// Gets the tooltip padding. Must be finite and non-negative. Defaults to zero.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">An assigned padding value is negative or not finite.</exception>
	public Thickness Padding
	{
		get => _padding;
		init => _padding = ValidateThickness(value, nameof(Padding));
	}

	/// <summary>
	/// Gets the tooltip background brush, or <see langword="null"/> to keep the active theme's tooltip
	/// background.
	/// </summary>
	public Brush? Background { get; init; }

	/// <summary>
	/// Gets the tooltip border brush, or <see langword="null"/> to keep the active theme's tooltip border.
	/// </summary>
	public Brush? BorderBrush { get; init; }

	/// <summary>
	/// Gets the default tooltip skin: placed to the right of the item list, offset by 10 device-independent
	/// pixels, with zero border and padding and the active theme's tooltip colors.
	/// </summary>
	public static CompletionTooltipSkin Default { get; } = new();

	private static Thickness ValidateThickness(Thickness thickness, string propertyName)
	{
		if (!NumericValidation.IsFiniteNonNegative(thickness.Left)
			|| !NumericValidation.IsFiniteNonNegative(thickness.Top)
			|| !NumericValidation.IsFiniteNonNegative(thickness.Right)
			|| !NumericValidation.IsFiniteNonNegative(thickness.Bottom))
		{
			throw new ArgumentOutOfRangeException(propertyName, thickness, "The tooltip skin thickness values must be finite and non-negative.");
		}

		return thickness;
	}
}
