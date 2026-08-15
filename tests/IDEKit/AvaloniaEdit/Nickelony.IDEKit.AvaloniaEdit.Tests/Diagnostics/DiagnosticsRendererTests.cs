using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Diagnostics;
using Nickelony.IDEKit.Core.Diagnostics;
using Pen = Avalonia.Media.IPen;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

/// <summary>
/// The AvaloniaEdit binding of the shared <see cref="DiagnosticsRendererTestsBase"/> renderer scenarios; it
/// supplies the mirror's draw-capture, frozen-pen, and underline-geometry hooks and adds the immutable
/// default-pen scenario.
/// </summary>
[AvaloniaTestClass]
public sealed class DiagnosticsRendererTests : DiagnosticsRendererTestsBase
{
	[TestMethod]
	public void DefaultPens_AreFrozen()
	{
		DiagnosticsRenderer renderer = CreateRenderer();

		// Avalonia's IPen has no IsFrozen; the renderer's defaults are its shared sample pens, which the
		// mirror installs as ImmutablePen instances (the frozen-equivalent type).
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
		=> new ImmutablePen(
			new ImmutableSolidColorBrush(color.ToUInt32()),
			1.5,
			null,
			PenLineCap.Flat,
			PenLineJoin.Miter,
			10.0);

	protected override void AssertUnderlineGeometryShape(GeometryDrawing drawing, TextDiagnosticSeverity severity)
		=> Assert.IsNotNull(drawing.Geometry, $"Wrong underline geometry for {severity}.");
}
