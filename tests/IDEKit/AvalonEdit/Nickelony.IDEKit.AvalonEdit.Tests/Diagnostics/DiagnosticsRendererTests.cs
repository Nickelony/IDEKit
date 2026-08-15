using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Diagnostics;
using Nickelony.IDEKit.Core.Diagnostics;
using System.Windows.Media;
using Pen = System.Windows.Media.Pen;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

/// <summary>
/// The AvalonEdit binding of the shared <see cref="DiagnosticsRendererTestsBase"/> renderer scenarios; it
/// supplies the WPF draw-capture, frozen-pen, and underline-geometry hooks and adds the frozen default-pen
/// scenario.
/// </summary>
[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed class DiagnosticsRendererTests : DiagnosticsRendererTestsBase
{
	[TestMethod]
	public void DefaultPens_AreFrozen()
	{
		DiagnosticsRenderer renderer = CreateRenderer();

		Assert.IsTrue(renderer.ErrorPen.IsFrozen);
		Assert.IsTrue(renderer.WarningPen.IsFrozen);
		Assert.IsTrue(renderer.InformationPen.IsFrozen);
		Assert.IsTrue(renderer.HintPen.IsFrozen);
	}

	protected override DrawingGroup? Render(DiagnosticsRenderer renderer, TextView textView)
	{
		var visual = new DrawingVisual();

		using (DrawingContext drawingContext = visual.RenderOpen())
			renderer.Draw(textView, drawingContext);

		return visual.Drawing;
	}

	protected override Pen CreateFrozenPen(Color color)
	{
		var brush = new SolidColorBrush(color);
		brush.Freeze();

		var pen = new Pen(brush, 1.5);
		pen.Freeze();
		return pen;
	}

	protected override void AssertUnderlineGeometryShape(GeometryDrawing drawing, TextDiagnosticSeverity severity)
		=> Assert.IsTrue(
			drawing.Geometry is StreamGeometry,
			$"Wrong underline geometry for {severity}.");
}
