using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Nickelony.IDEKit.AvaloniaEdit.Media;

/// <summary>Creates immutable Avalonia brushes and pens.</summary>
/// <remarks>
/// Avalonia has no <c>Freeze</c>, so the shared values are the immutable brush and pen types.
/// </remarks>
internal static class BrushAndPenFactory
{
	/// <summary>Creates an immutable brush from a color.</summary>
	/// <param name="color">The color of the brush.</param>
	/// <returns>An immutable brush with the given color.</returns>
	internal static ImmutableSolidColorBrush CreateBrush(Color color) => new(color.ToUInt32());

	/// <summary>Creates a pen with the supplied brush and thickness.</summary>
	/// <param name="brush">The brush that strokes the pen, or <see langword="null"/> for a pen that strokes nothing.</param>
	/// <param name="thickness">The pen thickness; must not be negative.</param>
	/// <returns>The pen for the supplied brush and thickness.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="thickness"/> is negative.</exception>
	/// <remarks>
	/// The returned pen is immutable only when <paramref name="brush"/> is immutable (Avalonia has no <c>Freeze</c>), because a mutable brush
	/// cannot be wrapped by <see cref="ImmutablePen"/>; a pen over a mutable brush is itself mutable and must not be shared between elements.
	/// </remarks>
	internal static IPen CreatePen(IBrush? brush, double thickness)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(thickness);

		if (brush is IImmutableBrush immutableBrush)
		{
			return new ImmutablePen(
				brush: immutableBrush,
				thickness,
				dashStyle: null,
				lineCap: PenLineCap.Flat,
				lineJoin: PenLineJoin.Miter,
				miterLimit: 10.0
			);
		}

		return new Pen { Brush = brush, Thickness = thickness };
	}

	/// <summary>Creates a pen with the supplied thickness and dash pattern, using round line caps and starting the pattern at offset zero.</summary>
	/// <param name="brush">The brush that strokes the pen, or <see langword="null"/> for a pen that strokes nothing.</param>
	/// <param name="thickness">The pen thickness; must not be negative.</param>
	/// <param name="dashPattern">
	/// The dash pattern. Each value is interpreted as a multiple of the pen's thickness, so <c>[1.0, 3.0]</c> draws a dash one thickness long
	/// and a gap three thicknesses long, not 1- and 3-DIP lengths.
	/// </param>
	/// <returns>The pen for the supplied brush, thickness, and dash pattern.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="thickness"/> is negative.</exception>
	/// <remarks>
	/// The returned pen is immutable only when <paramref name="brush"/> is immutable (Avalonia has no <c>Freeze</c>); a pen over a mutable brush
	/// is itself mutable and must not be shared between elements.
	/// </remarks>
	internal static IPen CreateDashedPen(IBrush? brush, double thickness, double[] dashPattern)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(thickness);

		var dashStyle = new ImmutableDashStyle(dashPattern, 0.0);

		if (brush is IImmutableBrush immutableBrush)
		{
			return new ImmutablePen(
				brush: immutableBrush,
				thickness,
				dashStyle,
				lineCap: PenLineCap.Round,
				lineJoin: PenLineJoin.Miter,
				miterLimit: 10.0
			);
		}

		return new Pen
		{
			Brush = brush,
			Thickness = thickness,
			DashStyle = dashStyle,
			LineCap = PenLineCap.Round,
		};
	}
}
