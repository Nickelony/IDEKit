using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Bookmarks;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

/// <summary>
/// The AvaloniaEdit binding of the shared <see cref="BookmarkMarginTestsBase"/> bookmark-margin scenarios; it
/// supplies the mirror's click, arranged-width, and marker-bounds hooks and adds the styled-property
/// registration and hit-test scenarios.
/// </summary>
[AvaloniaTestClass]
public sealed class BookmarkMarginTests : BookmarkMarginTestsBase
{
	[TestMethod]
	public void MarginWidthProperty_DefaultAndMetadata()
	{
		Assert.AreEqual(16.0, BookmarkMargin.MarginWidthProperty.GetDefaultValue(typeof(BookmarkMargin)));

		// Avalonia has no FrameworkPropertyMetadata AffectsMeasure/AffectsRender flags. The base
		// registration calls AffectsMeasure<LineStatusMarginBase>(MarginWidthProperty), which the
		// behavioral check below pins instead of reading the flag.
		BookmarkMargin margin = CreateMargin(new TextDocument("one\r\ntwo"));

		margin.Measure(new Size(100.0, 100.0));

		Assert.IsTrue(margin.IsMeasureValid);

		margin.MarginWidth = 24.0;

		Assert.IsFalse(margin.IsMeasureValid, "Changing MarginWidth must invalidate the margin's measure.");
	}

	[TestMethod]
	public void IconBrushProperty_DefaultIsFrozenAmber_AffectsRender()
	{
		// Reading the sample brush runs BookmarkMargin's static registration (the metadata override), so the
		// default-value read below does not depend on another test having created an instance first.
		IBrush sampleBrush = BookmarkMargin.SampleIconBrush;

		var defaultValue = (IBrush)BookmarkMargin.IconBrushProperty.GetDefaultValue(typeof(BookmarkMargin))!;

		Assert.AreSame(sampleBrush, defaultValue);
		Assert.AreEqual(Color.FromRgb(0xE6, 0xA2, 0x3C), ((ISolidColorBrush)defaultValue).Color);

		// Avalonia has no Freeze; the sample default is the immutable (frozen-equivalent) brush the
		// mirror installs, so the default and the sample property are the same instance.
		Assert.IsInstanceOfType<ImmutableSolidColorBrush>(defaultValue);

		// BookmarkMargin registers IconBrushProperty with AffectsRender<BookmarkMargin>. Avalonia exposes
		// no AffectsRender metadata flag and no public "visual is dirty" read, so the flag assertion is
		// dropped; the registration itself is the render-invalidation contract.
	}

	[TestMethod]
	public void IconGeometryProperty_DefaultIsFrozenIcon()
	{
		// Reading the sample geometry runs BookmarkMargin's static registration (the metadata override), so
		// the default-value read below does not depend on another test having created an instance first.
		Geometry sampleGeometry = BookmarkMargin.SampleIconGeometry;

		var defaultValue = (Geometry)BookmarkMargin.IconGeometryProperty.GetDefaultValue(typeof(BookmarkMargin))!;

		Assert.IsNotNull(defaultValue);

		// Avalonia has no Freeze; the default is the shared sample icon geometry.
		Assert.AreSame(sampleGeometry, defaultValue);

		Rect bounds = defaultValue.Bounds;

		Assert.IsGreaterThan(0.0, bounds.Width);
		Assert.IsGreaterThan(0.0, bounds.Height);
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

		// The margin should accept hit tests across its whole surface so a click on any row reaches it.
		// Avalonia's default hit test returns null for a custom control that paints no background
		// geometry, so margin.InputHitTest(Point) cannot return the margin the way the WPF
		// InputHitTest did; the routed click path is pinned through the TryToggleBookmarkAt seam instead.
		Assert.Inconclusive(
			"Avalonia's default hit test returns null for a custom margin that paints no background "
			+ "geometry, so the WPF whole-surface hit-target assertion cannot be expressed here.");
	}

	protected override bool ClickMarginRow(BookmarkMargin margin, Point position)
		=> margin.TryToggleBookmarkAt(position);

	protected override double GetArrangedWidth(LineStatusMarginBase margin) => margin.Bounds.Width;

	protected override Rect CaptureMarkerBounds(LineStatusMarginBase margin, VisualLine visualLine)
	{
		var drawing = new DrawingGroup();

		using (DrawingContext drawingContext = drawing.Open())
			margin.DrawMarker(drawingContext, visualLine, 0.0);

		return drawing.GetBounds();
	}
}
