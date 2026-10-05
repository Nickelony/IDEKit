using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Diagnostics;
using Nickelony.IDEKit.Core.Diagnostics;
using Pen = Avalonia.Media.IPen;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

/// <summary>
/// The AvaloniaEdit binding of the shared <see cref="DiagnosticsRendererTestsBase"/> renderer scenarios; it supplies the draw-capture, frozen-pen,
/// and underline-geometry hooks and adds the immutable default-pen scenario.
/// </summary>
[AvaloniaTestClass]
public sealed class DiagnosticsRendererTests : DiagnosticsRendererTestsBase
{
	[TestMethod]
	public void DefaultPens_AreImmutable()
	{
		DiagnosticsRenderer renderer = CreateRenderer();

		// Avalonia's IPen has no IsFrozen; the renderer's defaults are its shared sample pens, immutable
		// instances (the frozen-equivalent type).
		Assert.AreSame(DiagnosticsRenderer.SampleErrorPen, renderer.ErrorPen);
		Assert.AreSame(DiagnosticsRenderer.SampleWarningPen, renderer.WarningPen);
		Assert.AreSame(DiagnosticsRenderer.SampleInformationPen, renderer.InformationPen);
		Assert.AreSame(DiagnosticsRenderer.SampleHintPen, renderer.HintPen);

		Assert.IsInstanceOfType<ImmutablePen>(renderer.ErrorPen);
	}

	protected override DrawingGroup? Render(DiagnosticsRenderer renderer, TextView textView)
	{
		var drawing = new DrawingGroup();

		using (DrawingContext drawingContext = drawing.Open())
			renderer.Draw(textView, drawingContext);

		return drawing.Children.Count == 0 ? null : drawing;
	}

	protected override Pen CreateFrozenPen(Color color)
	{
		return new ImmutablePen(
			brush: new ImmutableSolidColorBrush(color.ToUInt32()),
			thickness: 1.5,
			dashStyle: null,
			lineCap: PenLineCap.Flat,
			lineJoin: PenLineJoin.Miter,
			miterLimit: 10.0
		);
	}

	protected override void AssertUnderlineGeometryShape(GeometryDrawing drawing, DiagnosticSeverity severity)
	{
		// The renderer batches the underline into a StreamGeometry, but Avalonia records a DrawingGroup
		// geometry as its internal PlatformGeometry wrapper, which shares no public base with StreamGeometry.
		// The concrete shape therefore cannot be named here; the extent and squiggle-amplitude checks live in
		// the shared base, so this hook only pins that an underline geometry was recorded.
		Assert.IsNotNull(drawing.Geometry, $"Wrong underline geometry for {severity}.");
	}
}
