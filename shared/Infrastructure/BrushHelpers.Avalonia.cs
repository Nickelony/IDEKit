using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Nickelony.IDEKit.Infrastructure;

/// <summary>
/// Creates immutable Avalonia brushes and pens for reuse by IDEKit components.
/// </summary>
/// <remarks>
/// This file is compiled into every Avalonia package that needs it through a <c>&lt;Compile Include&gt;</c>
/// link. The <c>Nickelony.IDEKit.Infrastructure</c> namespace is intentionally shared by the linked
/// copies instead of following one project's folder-to-namespace convention, so the helper keeps a
/// single identity across packages. It is the Avalonia counterpart of the WPF-typed
/// <c>BrushHelpers.cs</c>: Avalonia has no <c>Freeze</c>, so the shared values are the immutable brush
/// and pen types instead.
/// </remarks>
internal static class BrushHelpers
{
	/// <summary>
	/// Creates an immutable brush from a color.
	/// </summary>
	/// <param name="color">The color of the brush.</param>
	/// <returns>An immutable brush with the given color.</returns>
	public static ImmutableSolidColorBrush CreateFrozenBrush(Color color)
		=> new(color.ToUInt32());

	/// <summary>
	/// Parses a color string and creates an immutable brush from it.
	/// </summary>
	/// <param name="colorValue">The color string to parse.</param>
	/// <returns>An immutable brush with the parsed color.</returns>
	/// <exception cref="ArgumentException">
	/// <paramref name="colorValue"/> is blank or is not a valid color string.
	/// </exception>
	public static ImmutableSolidColorBrush CreateFrozenBrush(string colorValue)
	{
		if (!TryParseColor(colorValue, out Color color))
			throw new ArgumentException($"'{colorValue}' is not a valid color.", nameof(colorValue));

		return new ImmutableSolidColorBrush(color.ToUInt32());
	}

	/// <summary>
	/// Tries to parse a color string.
	/// </summary>
	/// <remarks>
	/// This is the single parse implementation for color strings in the Avalonia packages: a blank or
	/// unparseable value reports <see langword="false"/> instead of throwing, so callers decide whether
	/// an invalid value is an error or a fallback.
	/// </remarks>
	/// <param name="colorValue">The color string to parse.</param>
	/// <param name="color">
	/// The parsed color when parsing succeeds; otherwise, the default color.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when <paramref name="colorValue"/> is a valid color string; otherwise,
	/// <see langword="false"/>.
	/// </returns>
	public static bool TryParseColor(string? colorValue, out Color color)
	{
		color = default;

		if (string.IsNullOrWhiteSpace(colorValue))
			return false;

		return Color.TryParse(colorValue, out color);
	}

	/// <summary>
	/// Creates an immutable pen with the supplied brush and thickness.
	/// </summary>
	/// <remarks>
	/// The returned pen can be shared between elements. A <see langword="null"/> brush produces a pen
	/// that strokes nothing.
	/// </remarks>
	/// <param name="brush">The brush used to stroke the pen, or <see langword="null"/> for a pen that strokes nothing.</param>
	/// <param name="thickness">The pen thickness; must not be negative.</param>
	/// <returns>An immutable pen.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="thickness"/> is negative.</exception>
	public static IPen CreateFrozenPen(IBrush? brush, double thickness)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(thickness);

		if (brush is IImmutableBrush immutableBrush)
			return new ImmutablePen(immutableBrush, thickness, null, PenLineCap.Flat, PenLineJoin.Miter, 10.0);

		return new Pen { Brush = brush, Thickness = thickness };
	}

	/// <summary>
	/// Creates an immutable pen with the supplied thickness and dash pattern, using round line caps and
	/// starting the pattern at offset zero.
	/// </summary>
	/// <remarks>
	/// The returned pen can be shared between elements. A <see langword="null"/> brush produces a pen
	/// that strokes nothing.
	/// </remarks>
	/// <param name="brush">The brush used to stroke the pen, or <see langword="null"/> for a pen that strokes nothing.</param>
	/// <param name="thickness">The pen thickness; must not be negative.</param>
	/// <param name="dashPattern">
	/// The dash pattern. Each value is interpreted as a multiple of the pen's thickness, so
	/// <c>[1.0, 3.0]</c> draws a dash one thickness long and a gap three thicknesses long, not 1- and
	/// 3-DIP lengths.
	/// </param>
	/// <returns>An immutable pen.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="dashPattern"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="thickness"/> is negative.</exception>
	public static IPen CreateFrozenDashedPen(IBrush? brush, double thickness, double[] dashPattern)
	{
		ArgumentNullException.ThrowIfNull(dashPattern);
		ArgumentOutOfRangeException.ThrowIfNegative(thickness);

		var dashStyle = new ImmutableDashStyle(dashPattern, 0.0);

		if (brush is IImmutableBrush immutableBrush)
			return new ImmutablePen(immutableBrush, thickness, dashStyle, PenLineCap.Round, PenLineJoin.Miter, 10.0);

		return new Pen
		{
			Brush = brush,
			Thickness = thickness,
			DashStyle = dashStyle,
			LineCap = PenLineCap.Round
		};
	}

	/// <summary>
	/// Computes the luma of a color as the weighted average of its gamma-compressed channels
	/// (Rec. 601). The exact luminance model does not matter for callers that only decide whether a
	/// color is light or dark.
	/// </summary>
	/// <param name="color">The color to measure.</param>
	/// <returns>The luma in the range 0 to 1.</returns>
	public static double GetLuma(Color color)
		=> ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255.0;

	/// <summary>
	/// Blends two colors by the supplied ratio, interpolating every channel.
	/// </summary>
	/// <param name="first">The color the result is at a ratio of 0.</param>
	/// <param name="second">The color the result is at a ratio of 1.</param>
	/// <param name="ratio">The blend ratio; 0 returns <paramref name="first"/> and 1 returns <paramref name="second"/>.</param>
	/// <returns>The blended color.</returns>
	public static Color Blend(Color first, Color second, double ratio)
	{
		double inverseRatio = 1.0 - ratio;

		return Color.FromArgb(
			(byte)Math.Round(first.A * inverseRatio + second.A * ratio),
			(byte)Math.Round(first.R * inverseRatio + second.R * ratio),
			(byte)Math.Round(first.G * inverseRatio + second.G * ratio),
			(byte)Math.Round(first.B * inverseRatio + second.B * ratio));
	}
}
