using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

/// <summary>
/// The AvaloniaEdit binding of the shared <see cref="LineStatusMarginBaseTestsBase"/> margin scenarios; it
/// supplies the mirror's draw pass, arranged-width, dispatcher-pump, and arrange-assertion hooks.
/// </summary>
[AvaloniaTestClass]
public sealed class LineStatusMarginBaseTests : LineStatusMarginBaseTestsBase
{
	protected override void RunDrawPass(TextEditor editor, LineStatusMarginBase margin)
		=> RenderMargin(margin);

	protected override double GetArrangedWidth(LineStatusMarginBase margin) => margin.Bounds.Width;

	protected override void PumpRenderFrame()
		=> AvaloniaTestHost.PumpDispatcher(Dispatcher.UIThread, DispatcherPriority.Render);

	protected override void AssertArrangeInvalidated(bool arrangeValidInHandler, string message)
		=> Assert.Inconclusive(
			$"Avalonia's InvalidateVisual does not invalidate arrange (IsArrangeValid stayed {arrangeValidInHandler}), "
			+ "so the margin's arrange-invalidation assertion cannot be expressed here.");

	/// <summary>
	/// Runs the margin's draw pass into a captured drawing, so the base forward walk is exercised without
	/// the bitmap tier the mirror keeps unavailable. Avalonia has no DrawingVisual/RenderOpen; a DrawingGroup
	/// records the same drawing content.
	/// </summary>
	/// <param name="margin">The margin whose draw pass is invoked.</param>
	private static void RenderMargin(LineStatusMarginBase margin)
	{
		var drawing = new DrawingGroup();

		using (DrawingContext drawingContext = drawing.Open())
			margin.Render(drawingContext);
	}
}
