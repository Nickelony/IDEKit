using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Media;

/// <summary>Creates frozen WPF brushes and pens.</summary>
internal static class BrushAndPenFactory
{
	/// <summary>Creates a frozen brush from a color.</summary>
	/// <param name="color">The color of the brush.</param>
	/// <returns>A frozen brush with the given color.</returns>
	internal static SolidColorBrush CreateBrush(Color color)
	{
		var brush = new SolidColorBrush(color);
		brush.Freeze();
		return brush;
	}

	/// <summary>Creates a frozen pen with the supplied brush and thickness.</summary>
	/// <param name="brush">The brush that strokes the pen, or <see langword="null"/> for a pen that strokes nothing.</param>
	/// <param name="thickness">The pen thickness; must not be negative.</param>
	/// <returns>A frozen pen.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="thickness"/> is negative.</exception>
	/// <remarks>
	/// The returned pen is frozen so it can be shared between elements.
	/// </remarks>
	internal static Pen CreatePen(Brush? brush, double thickness)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(thickness);

		var pen = new Pen(brush, thickness);
		pen.Freeze();
		return pen;
	}

	/// <summary>
	/// Creates a frozen pen with the supplied thickness and dash pattern, using round line caps and starting the pattern at offset zero.
	/// </summary>
	/// <param name="brush">The brush that strokes the pen, or <see langword="null"/> for a pen that strokes nothing.</param>
	/// <param name="thickness">The pen thickness; must not be negative.</param>
	/// <param name="dashPattern">
	/// The dash pattern. WPF interprets each value as a multiple of the pen's thickness, so <c>[1.0, 3.0]</c> draws a dash one thickness long
	/// and a gap three thicknesses long, not 1- and 3-DIP lengths.
	/// </param>
	/// <returns>A frozen pen.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="thickness"/> is negative.</exception>
	/// <remarks>
	/// The returned pen is frozen so it can be shared between elements.
	/// </remarks>
	internal static Pen CreateDashedPen(Brush? brush, double thickness, double[] dashPattern)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(thickness);

		var pen = new Pen(brush, thickness)
		{
			DashStyle = new DashStyle(dashPattern, 0.0),
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round,
		};

		pen.Freeze();
		return pen;
	}
}
