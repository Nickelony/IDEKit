using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.LineStatus;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

/// <summary>
/// The AvalonEdit binding of the shared <see cref="LineStatusIconMarginBaseTestsBase"/> icon-margin scenarios;
/// it supplies the WPF empty-geometry, draw-pass, and drawing hooks.
/// </summary>
[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed class LineStatusIconMarginBaseTests : LineStatusIconMarginBaseTestsBase
{
	protected override void AssertDefaultIconGeometryIsEmpty(LineStatusIconMarginBase margin)
	{
		Assert.AreSame(Geometry.Empty, margin.IconGeometry);
		Assert.IsTrue(margin.IconGeometry.Bounds.IsEmpty);
	}

	protected override DrawingGroup? DrawMarker(LineStatusIconMarginBase margin, VisualLine visualLine)
	{
		var visual = new DrawingVisual();

		using (DrawingContext drawingContext = visual.RenderOpen())
			margin.DrawMarker(drawingContext, visualLine, 0.0);

		return visual.Drawing;
	}

	protected override void AssertDrawingPaints(DrawingGroup drawing) => Assert.IsFalse(drawing.Bounds.IsEmpty);

	protected override Geometry CreateCacheableIconGeometry(Rect bounds)
	{
		var geometry = new RectangleGeometry(bounds);
		geometry.Freeze();

		return geometry;
	}

	protected override Rect GetDrawingBounds(DrawingGroup drawing) => drawing.Bounds;

	[TestMethod]
	public void DrawMarker_UnfrozenGeometryMutatedBetweenPasses_DrawsTheMutatedBounds()
	{
		var margin = new MarkedIconMargin();
		var geometry = new RectangleGeometry(new Rect(x: 0.0, y: 0.0, width: 10.0, height: 9.0));
		var editor = new TextEditor { Document = new TextDocument("one\r\ntwo"), FontSize = 12.0 };

		margin.IconGeometry = geometry;

		using var host = new HostedMarginTestHost(editor, margin);

		VisualLine firstLine = editor.TextArea.TextView.VisualLines[0];

		Assert.IsFalse(geometry.IsFrozen, "The premise: a mutable geometry is not cached.");
		Assert.AreEqual(9.0, GetDrawingBounds(DrawMarker(margin, firstLine)!).Height, 0.001);

		// The mutated bounds reach the next pass.
		geometry.Rect = new Rect(x: 0.0, y: 0.0, width: 10.0, height: 6.0);

		Assert.AreEqual(6.0, GetDrawingBounds(DrawMarker(margin, firstLine)!).Height, 0.001);
	}

	private sealed class MarkedIconMargin : LineStatusIconMarginBase
	{
		protected override IReadOnlyList<int> GetMarkedLineNumbers() => [1];
	}
}
