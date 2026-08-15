using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using System.Windows.Threading;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

/// <summary>
/// The AvalonEdit binding of the shared <see cref="LineStatusMarginBaseTestsBase"/> margin scenarios; it
/// supplies the WPF draw pass, arranged-width, and dispatcher-pump hooks.
/// </summary>
[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed class LineStatusMarginBaseTests : LineStatusMarginBaseTestsBase
{
	protected override void RunDrawPass(TextEditor editor, LineStatusMarginBase margin)
		=> TestBitmapRendering.RenderToBitmap(editor);

	protected override double GetArrangedWidth(LineStatusMarginBase margin) => margin.ActualWidth;

	protected override void PumpRenderFrame()
		=> WPFTestHost.PumpDispatcher(Dispatcher.CurrentDispatcher, DispatcherPriority.Render);
}
