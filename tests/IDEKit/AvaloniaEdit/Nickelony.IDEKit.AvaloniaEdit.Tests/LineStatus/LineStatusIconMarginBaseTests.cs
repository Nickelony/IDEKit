using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.LineStatus;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

/// <summary>
/// The AvaloniaEdit binding of the shared <see cref="LineStatusIconMarginBaseTestsBase"/> icon-margin scenarios;
/// it supplies the empty-geometry, draw-pass, and drawing hooks.
/// </summary>
[AvaloniaTestClass]
public sealed class LineStatusIconMarginBaseTests : LineStatusIconMarginBaseTestsBase
{
	protected override void AssertDefaultIconGeometryIsEmpty(LineStatusIconMarginBase margin)
	{
		// Avalonia's Geometry.Empty is a distinct StreamGeometry instance, so the default is asserted through empty bounds
		// rather than singleton identity.
		Rect bounds = margin.IconGeometry.Bounds;

		Assert.AreEqual(0.0, bounds.Width);
		Assert.AreEqual(0.0, bounds.Height);
	}

	protected override DrawingGroup? DrawMarker(LineStatusIconMarginBase margin, VisualLine visualLine)
	{
		// Avalonia has no DrawingVisual/RenderOpen; a DrawingGroup captures the same recorded drawing
		// content and exposes an empty Children collection when the margin drew nothing.
		var drawing = new DrawingGroup();

		using (DrawingContext drawingContext = drawing.Open())
			margin.DrawMarker(drawingContext, visualLine, 0.0);

		return drawing.Children.Count == 0 ? null : drawing;
	}

	protected override void AssertDrawingPaints(DrawingGroup drawing)
	{
		Rect bounds = drawing.GetBounds();

		Assert.IsGreaterThan(0.0, bounds.Width);
		Assert.IsGreaterThan(0.0, bounds.Height);
	}

	protected override Geometry CreateCacheableIconGeometry(Rect bounds) => new RectangleGeometry(bounds);

	protected override Rect GetDrawingBounds(DrawingGroup drawing) => drawing.GetBounds();
}
