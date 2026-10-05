#if AVALONIAEDIT
using Avalonia;
using Nickelony.IDEKit.AvaloniaEdit.Diagnostics;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using System.Windows;
using Nickelony.IDEKit.AvalonEdit.Diagnostics;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

[TestClass]
public sealed class UnderlineFigureBuilderTests
{
	// A 17-DIP line box with its bottom edge at 37: the zig-zag baseline sits 1.6 + 0.7 above the bottom edge.
	private const double Baseline = 34.7;

	[TestMethod]
	public void BuildSquiggle_FullWaves_RiseReturnFallAndReturnToTheBaseline()
	{
		var builder = new UnderlineFigureBuilder();

		IReadOnlyList<Point> figure = builder.BuildSquiggle(new Rect(x: 10.0, y: 20.0, width: 8.0, height: 17.0));

		AssertPoints(
			figure,

			[
				new Point(10.0, Baseline),
				new Point(12.0, Baseline - 1.6),
				new Point(14.0, Baseline),
				new Point(16.0, Baseline + 1.6),
				new Point(18.0, Baseline),
			]
		);
	}

	[TestMethod]
	public void BuildSquiggle_WidthBetweenSteps_ClampsTheLastPointsToTheRightEdge()
	{
		var builder = new UnderlineFigureBuilder();

		IReadOnlyList<Point> figure = builder.BuildSquiggle(new Rect(x: 0.0, y: 20.0, width: 5.0, height: 17.0));

		AssertPoints(
			figure,

			[
				new Point(0.0, Baseline),
				new Point(2.0, Baseline - 1.6),
				new Point(4.0, Baseline),
				new Point(5.0, Baseline + 1.6),
				new Point(5.0, Baseline),
			]
		);
	}

	[TestMethod]
	public void BuildSquiggle_RectangleWithoutWidth_YieldsTheStartPointAlone()
	{
		var builder = new UnderlineFigureBuilder();

		IReadOnlyList<Point> figure = builder.BuildSquiggle(new Rect(x: 6.0, y: 20.0, width: 0.0, height: 17.0));

		AssertPoints(figure, [new Point(6.0, Baseline)]);
	}

	[TestMethod]
	public void BuildSquiggle_AnyWidth_StaysInsideTheRectangleAndAboveItsBottomEdge()
	{
		var builder = new UnderlineFigureBuilder();

		foreach (double width in new[] { 2.0, 3.0, 7.5, 40.0, 123.4 })
		{
			var rect = new Rect(x: 12.0, y: 20.0, width, height: 17.0);

			IReadOnlyList<Point> figure = builder.BuildSquiggle(rect);

			foreach (Point point in figure)
			{
				Assert.IsGreaterThanOrEqualTo(rect.Left, point.X, $"Width {width}: a point left of the rectangle.");
				Assert.IsLessThanOrEqualTo(rect.Right, point.X, $"Width {width}: a point right of the rectangle.");
				Assert.IsGreaterThanOrEqualTo(rect.Top, point.Y, $"Width {width}: a point above the rectangle.");
				Assert.IsLessThan(rect.Bottom, point.Y, $"Width {width}: a point on or below the bottom edge.");
			}

			Assert.AreEqual(expected: rect.Right, actual: figure[^1].X, delta: 1e-9, message: $"Width {width}: the wave must end at the right edge.");
		}
	}

	[TestMethod]
	public void BuildSquiggle_AfterAnotherFigure_ReturnsOnlyTheNewFigure()
	{
		var builder = new UnderlineFigureBuilder();

		_ = builder.BuildSquiggle(new Rect(x: 10.0, y: 20.0, width: 40.0, height: 17.0));

		IReadOnlyList<Point> figure = builder.BuildSquiggle(new Rect(x: 10.0, y: 20.0, width: 0.0, height: 17.0));

		AssertPoints(figure, [new Point(10.0, Baseline)]);
	}

	[TestMethod]
	public void BuildStraight_RunsAlongTheBottomEdgeOneUnitAboveIt()
	{
		var builder = new UnderlineFigureBuilder();

		IReadOnlyList<Point> figure = builder.BuildStraight(new Rect(x: 10.0, y: 20.0, width: 30.0, height: 17.0));

		AssertPoints(figure, [new Point(10.0, 36.0), new Point(40.0, 36.0)]);
	}

	[TestMethod]
	public void BuildStraight_AfterASquiggle_ReturnsOnlyTheStraightFigure()
	{
		var builder = new UnderlineFigureBuilder();

		_ = builder.BuildSquiggle(new Rect(x: 10.0, y: 20.0, width: 40.0, height: 17.0));

		IReadOnlyList<Point> figure = builder.BuildStraight(new Rect(x: 10.0, y: 20.0, width: 30.0, height: 17.0));

		AssertPoints(figure, [new Point(10.0, 36.0), new Point(40.0, 36.0)]);
	}

	private static void AssertPoints(IReadOnlyList<Point> actual, Point[] expected)
	{
		Assert.HasCount(expected.Length, actual);

		for (int index = 0; index < expected.Length; index++)
		{
			Assert.AreEqual(expected: expected[index].X, actual: actual[index].X, delta: 1e-9, message: $"X of point {index}.");
			Assert.AreEqual(expected: expected[index].Y, actual: actual[index].Y, delta: 1e-9, message: $"Y of point {index}.");
		}
	}
}
