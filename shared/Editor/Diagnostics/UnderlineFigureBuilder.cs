#if AVALONIAEDIT
using Avalonia;
#else
using System.Windows;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Diagnostics;

#else

namespace Nickelony.IDEKit.AvalonEdit.Diagnostics;

#endif

/// <summary>
/// Builds the point sequences of the zig-zag and straight underlines that <see cref="DiagnosticsRenderer"/> strokes under line-box rectangles.
/// </summary>
/// <remarks>
/// A figure is an open polyline: its first point starts it and every later point continues it. The builder reads no text view, so the underline math
/// runs against a rectangle alone. It reuses one point list for every figure, so a figure is valid until the next <c>Build</c> call, and the builder
/// is for single-threaded use.
/// </remarks>
internal sealed class UnderlineFigureBuilder
{
	/// <summary>
	/// The height of each stroke of the zig-zag above and below its baseline, which keeps the wave legible under the sample error pen.
	/// </summary>
	internal const double SquiggleAmplitude = 1.6;

	/// <summary>The horizontal length of one full wave of the zig-zag.</summary>
	internal const double SquiggleStep = 4.0;

	/// <summary>
	/// The distance the zig-zag baseline keeps above the bottom of the rectangle, so the down stroke does not bleed into the next line's leading.
	/// </summary>
	internal const double PenBleed = 0.7;

	/// <summary>The distance the straight underline keeps above the bottom of the rectangle.</summary>
	internal const double StraightInset = 1.0;

	private readonly List<Point> _figure = [];

	/// <summary>Builds the zig-zag figure of a rectangle.</summary>
	/// <param name="rect">The line-box rectangle to underline.</param>
	/// <returns>The points of the figure, valid until the next <c>Build</c> call.</returns>
	/// <remarks>
	/// The wave starts at the rectangle's left edge on a baseline above the rectangle's bottom edge, so neither stroke leaves the rectangle:
	/// each wave rises half a step to the baseline minus the amplitude, returns to the baseline, falls half a step below it, and returns.
	/// The last point is clamped to the rectangle's right edge, and a rectangle without width yields its start point alone.
	/// </remarks>
	internal IReadOnlyList<Point> BuildSquiggle(Rect rect)
	{
		_figure.Clear();

		double baseline = rect.Bottom - SquiggleAmplitude - PenBleed;
		_figure.Add(new Point(rect.Left, baseline));

		bool goingUp = true;

		for (double x = rect.Left; x < rect.Right; x += SquiggleStep)
		{
			double nextX = Math.Min(x + (SquiggleStep / 2.0), rect.Right);
			double y = baseline + (goingUp ? -SquiggleAmplitude : SquiggleAmplitude);

			_figure.Add(new Point(nextX, y));

			goingUp = !goingUp;

			nextX = Math.Min(x + SquiggleStep, rect.Right);
			_figure.Add(new Point(nextX, baseline));
		}

		return _figure;
	}

	/// <summary>Builds the straight figure of a rectangle, along its bottom edge.</summary>
	/// <param name="rect">The line-box rectangle to underline.</param>
	/// <returns>The two points of the figure, valid until the next <c>Build</c> call.</returns>
	internal IReadOnlyList<Point> BuildStraight(Rect rect)
	{
		_figure.Clear();

		double y = rect.Bottom - StraightInset;

		_figure.Add(new Point(rect.Left, y));
		_figure.Add(new Point(rect.Right, y));

		return _figure;
	}
}
