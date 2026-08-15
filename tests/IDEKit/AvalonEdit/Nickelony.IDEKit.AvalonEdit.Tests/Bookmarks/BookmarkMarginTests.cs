using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Bookmarks;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

/// <summary>
/// The AvalonEdit binding of the shared <see cref="BookmarkMarginTestsBase"/> bookmark-margin scenarios; it
/// supplies the WPF click, arranged-width, and marker-bounds hooks and adds the
/// <c>FrameworkPropertyMetadata</c> registration and hit-test scenarios.
/// </summary>
[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed class BookmarkMarginTests : BookmarkMarginTestsBase
{
	[TestMethod]
	public void MarginWidthProperty_DefaultAndMetadata()
	{
		Assert.AreEqual(16.0, BookmarkMargin.MarginWidthProperty.DefaultMetadata.DefaultValue);

		var metadata = (FrameworkPropertyMetadata)BookmarkMargin.MarginWidthProperty.GetMetadata(typeof(BookmarkMargin));

		Assert.IsTrue(metadata.AffectsMeasure);
		Assert.IsFalse(metadata.AffectsRender);
	}

	[TestMethod]
	public void IconBrushProperty_DefaultIsFrozenAmber_AffectsRender()
	{
		// Reading the sample brush runs BookmarkMargin's static registration (the metadata override), so the
		// metadata read below does not depend on another test having created an instance first.
		Brush sampleBrush = BookmarkMargin.SampleIconBrush;

		var metadata = (FrameworkPropertyMetadata)BookmarkMargin.IconBrushProperty.GetMetadata(typeof(BookmarkMargin));
		var defaultValue = (SolidColorBrush)metadata.DefaultValue;

		Assert.AreSame(sampleBrush, defaultValue);
		Assert.AreEqual(Color.FromRgb(0xE6, 0xA2, 0x3C), defaultValue.Color);
		Assert.IsTrue(defaultValue.IsFrozen);

		Assert.IsTrue(metadata.AffectsRender);
		Assert.IsFalse(metadata.AffectsMeasure);
	}

	[TestMethod]
	public void IconGeometryProperty_DefaultIsFrozenIcon()
	{
		// Reading the sample geometry runs BookmarkMargin's static registration (the metadata override), so
		// the metadata read below does not depend on another test having created an instance first.
		Geometry sampleGeometry = BookmarkMargin.SampleIconGeometry;

		var metadata = (FrameworkPropertyMetadata)BookmarkMargin.IconGeometryProperty.GetMetadata(typeof(BookmarkMargin));
		var defaultValue = (Geometry)metadata.DefaultValue;

		Assert.IsNotNull(defaultValue);
		Assert.AreSame(sampleGeometry, defaultValue);
		Assert.IsTrue(defaultValue.IsFrozen);
		Assert.IsGreaterThan(0.0, defaultValue.Bounds.Width);
		Assert.IsGreaterThan(0.0, defaultValue.Bounds.Height);
	}

	[TestMethod]
	public void HitTest_InsideMargin_ReturnsTheMarginAsTheHitTarget()
	{
		var document = new TextDocument("one\r\ntwo\r\nthree");
		var coordinator = new BookmarkCoordinator(() => document);
		var margin = new BookmarkMargin(coordinator);
		var editor = new TextEditor { Document = document };

		using var host = new HostedMarginScope(editor, margin);

		margin.UpdateLayout();

		// The margin accepts hit tests across its whole surface so a click on any row reaches it.
		IInputElement? hitElement = margin.InputHitTest(new Point(4.0, 4.0));

		Assert.AreSame(margin, hitElement);
	}

	protected override bool ClickMarginRow(BookmarkMargin margin, Point position)
	{
		margin.TestHooks.ClickPositionResolver = _ => position;

		var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
		{
			RoutedEvent = UIElement.MouseLeftButtonDownEvent
		};

		margin.RaiseEvent(args);

		return args.Handled;
	}

	protected override double GetArrangedWidth(LineStatusMarginBase margin) => margin.ActualWidth;

	protected override Rect CaptureMarkerBounds(LineStatusMarginBase margin, VisualLine visualLine)
	{
		var visual = new DrawingVisual();

		using (DrawingContext drawingContext = visual.RenderOpen())
			margin.DrawMarker(drawingContext, visualLine, 0.0);

		DrawingGroup? drawing = visual.Drawing;

		Assert.IsNotNull(drawing);

		return drawing.Bounds;
	}
}
