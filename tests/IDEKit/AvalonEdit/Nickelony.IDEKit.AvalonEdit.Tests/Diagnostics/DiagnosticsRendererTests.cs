using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Diagnostics;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Pen = System.Windows.Media.Pen;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

/// <summary>
/// The AvalonEdit binding of the shared <see cref="DiagnosticsRendererTestsBase"/> renderer scenarios; it supplies the WPF draw-capture, frozen-pen,
/// and underline-geometry hooks and adds the frozen default-pen scenario.
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

	[TestMethod]
	public void Draw_UnchangedView_AllocatesNothing()
	{
		string text = string.Join("\r\n", Enumerable.Repeat(new string('x', 80), 60));
		var editor = new TextEditor { Document = new TextDocument(text), FontSize = 12.0 };

		using HostWindow hostWindow = TestHost.ShowInHostWindow(editor);

		TextView textView = editor.TextArea.TextView;

		Assert.IsNotEmpty(textView.VisualLines);

		var renderer = new DiagnosticsRenderer(textView);

		renderer.ApplySegments([
			.. Enumerable
				.Range(0, 40)
				.SelectMany(line =>
					new[]
					{
						new DiagnosticSegment(new TextRange((line * 82) + 2, 6), DiagnosticSeverity.Error),
						new DiagnosticSegment(new TextRange((line * 82) + 12, 6), DiagnosticSeverity.Warning),
						new DiagnosticSegment(new TextRange((line * 82) + 22, 6), DiagnosticSeverity.Hint),
					}
				),
		]);

		var visual = new DrawingVisual();

		using (DrawingContext drawingContext = visual.RenderOpen())
		{
			// The drawing context grows its own recording buffer as passes add to it, which is not the renderer's allocation. The warm-up
			// outgrows most of that growth and the smallest of several runs leaves the rest out.
			for (int pass = 0; pass < 3_000; pass++)
				renderer.Draw(textView, drawingContext);

			long fewest = long.MaxValue;

			for (int run = 0; run < 10; run++)
			{
				long before = GC.GetAllocatedBytesForCurrentThread();

				for (int pass = 0; pass < 100; pass++)
					renderer.Draw(textView, drawingContext);

				fewest = Math.Min(fewest, GC.GetAllocatedBytesForCurrentThread() - before);
			}

			// A pass that collected rectangles or built a geometry again allocates tens of kilobytes, so 100 passes would exceed this bound a hundredfold.
			Assert.IsLessThan(
				1_000,
				fewest,
				$"Expected the unchanged passes to allocate nothing, but {fewest} bytes were allocated over 100 passes."
			);
		}

		Assert.AreEqual(1, renderer.GeometryBuildCount);
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

	protected override void AssertUnderlineGeometryShape(GeometryDrawing drawing, DiagnosticSeverity severity) =>
		Assert.IsTrue(drawing.Geometry is StreamGeometry, $"Wrong underline geometry for {severity}.");
}
