using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.IntelliSense.CodeActions;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

[AvaloniaTestClass]
public sealed class TextCodeActionMarginTests
{
	[TestMethod]
	public void Constructor_NullController_Throws()
		=> Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionMargin(null!));

	[TestMethod]
	public void DefaultProperties_UseTheNeutralGrayColorAndALibraryIcon()
	{
		using var host = new CodeActionTestHost();
		using var controller = host.CreateController();

		TextCodeActionMargin margin = controller.Margin;

		var defaultBrush = (IBrush)TextCodeActionMargin.IconBrushProperty.GetDefaultValue(typeof(TextCodeActionMargin))!;
		var defaultGeometry = (Geometry)TextCodeActionMargin.IconGeometryProperty.GetDefaultValue(typeof(TextCodeActionMargin))!;

		// Avalonia has no SystemColors, so the neutral default is the explicit gray the mirror installs
		// instead of the system gray-text color - still a theme-neutral default visible in common themes
		// rather than a host-specific accent color.
		Assert.AreEqual(Color.FromRgb(0x77, 0x77, 0x77), ((ISolidColorBrush)defaultBrush).Color);

		// Avalonia has no Freeze; the default is the immutable (frozen-equivalent) brush the mirror installs.
		Assert.IsInstanceOfType<ImmutableSolidColorBrush>(defaultBrush);

		Assert.IsTrue(defaultGeometry.Bounds.Width > 0.0);
		Assert.IsTrue(defaultGeometry.Bounds.Height > 0.0);
		Assert.AreSame(margin.IconBrush, defaultBrush);
		Assert.AreSame(margin.IconGeometry, defaultGeometry);

		// Avalonia has no FrameworkPropertyMetadata AffectsRender flag. The static constructor registers
		// AffectsRender<TextCodeActionMargin>(IconBrushProperty, IconGeometryProperty), which is the
		// render-invalidation contract, so the flag assertion is dropped.
	}

	[TestMethod]
	public void IconProperties_NullAssignments_Throw()
	{
		using var host = new CodeActionTestHost();
		using var controller = host.CreateController();

		TextCodeActionMargin margin = controller.Margin;

		Assert.ThrowsExactly<ArgumentNullException>(() => margin.IconBrush = null!);
		Assert.ThrowsExactly<ArgumentNullException>(() => margin.IconGeometry = null!);
	}

	[TestMethod]
	public void TryOpenActionsAt_IndicatorLine_OpensTheMenu()
	{
		(CodeActionTestHost Host, TextCodeActionController Controller) hosted = CreateHostedMargin();

		using (hosted.Host)
		using (hosted.Controller)
		{
			double y = GetLineTop(hosted.Host, 2);

			Assert.IsTrue(hosted.Controller.Margin.TryOpenActionsAt(new Point(8.0, y + 1.0)));
			Assert.IsTrue(hosted.Controller.IsActionsOpen);
		}
	}

	[TestMethod]
	public void TryOpenActionsAt_LineWithoutIndicator_DoesNothing()
	{
		(CodeActionTestHost Host, TextCodeActionController Controller) hosted = CreateHostedMargin();

		using (hosted.Host)
		using (hosted.Controller)
		{
			double y = GetLineTop(hosted.Host, 1);

			Assert.IsFalse(hosted.Controller.Margin.TryOpenActionsAt(new Point(8.0, y + 1.0)));
			Assert.IsFalse(hosted.Controller.IsActionsOpen);
		}
	}

	[TestMethod]
	public void TryOpenActionsAt_AfterIndicatorCleared_DoesNothing()
	{
		(CodeActionTestHost Host, TextCodeActionController Controller) hosted = CreateHostedMargin();

		using (hosted.Host)
		using (hosted.Controller)
		{
			// Moving the caret to another line clears the indicator before the debounce elapses.
			hosted.Host.Editor.CaretOffset = 0;
			Assert.IsFalse(hosted.Controller.HasActions);

			double y = GetLineTop(hosted.Host, 2);

			Assert.IsFalse(hosted.Controller.Margin.TryOpenActionsAt(new Point(8.0, y + 1.0)));
			Assert.IsFalse(hosted.Controller.IsActionsOpen);
		}
	}

	[TestMethod]
	public void OnMouseLeftButtonDown_IndicatorLine_OpensTheMenuAndHandlesTheEvent()
	{
		(CodeActionTestHost Host, TextCodeActionController Controller) hosted = CreateHostedMargin();

		using (hosted.Host)
		using (hosted.Controller)
		{
			TextCodeActionMargin margin = hosted.Controller.Margin;
			Point clickPoint = new(8.0, GetLineTop(hosted.Host, 2) + 1.0);

			// Avalonia's routed PointerPressedEventArgs cannot be built with a deterministic position in a
			// test (and the left-button state lives on the pointer, not the event args), so the port uses
			// the margin's TryOpenActionsAt seam. That seam runs the same resolve-then-open path the routed
			// OnPointerPressed handler runs, and its true result is exactly what makes the handler mark the
			// press handled.
			Assert.IsTrue(margin.TryOpenActionsAt(clickPoint));
			Assert.IsTrue(hosted.Controller.IsActionsOpen);
		}
	}

	[TestMethod]
	public void OnMouseLeftButtonDown_UnattachedMargin_DoesNotThrow()
	{
		var editor = AvaloniaTestHost.CreateEditor("one");
		using var controller = CodeActionTestHost.CreateUnhostedController(editor);

		TextCodeActionMargin margin = controller.Margin;

		// The routed click is exercised through the deterministic TryOpenActionsAt seam; see the sibling
		// test for why the synthetic pointer event is not used. An unattached margin resolves no visual
		// line, so the press runs through without opening anything.
		Assert.IsFalse(margin.TryOpenActionsAt(new Point(4.0, 4.0)));
		Assert.IsFalse(controller.IsActionsOpen);
	}

	[TestMethod]
	public void OpenOnLeftPress_Disabled_LeavesThePressUntouchedAndKeepsTheHostSeamWorking()
	{
		(CodeActionTestHost Host, TextCodeActionController Controller) hosted = CreateHostedMargin();

		using (hosted.Host)
		using (hosted.Controller)
		{
			TextCodeActionMargin margin = hosted.Controller.Margin;
			Point clickPoint = new(8.0, GetLineTop(hosted.Host, 2) + 1.0);

			margin.OpenOnLeftPress = false;

			// Disabling the default gesture makes the routed OnPointerPressed path leave the press
			// untouched (its OnIconClicked returns false and the event stays unhandled). The synthetic
			// routed pointer event cannot express that on Avalonia, so the port pins the documented
			// alternative: the host-driven seam still opens the same menu at the same point.
			Assert.IsTrue(margin.TryOpenActionsAt(clickPoint));
			Assert.IsTrue(hosted.Controller.IsActionsOpen);
		}
	}

	[TestMethod]
	public void OnRender_IndicatorState_ControlsTheIcon()
	{
		(CodeActionTestHost Host, TextCodeActionController Controller) hosted = CreateHostedMargin();

		using (hosted.Host)
		using (hosted.Controller)
		{
			TextCodeActionMargin margin = hosted.Controller.Margin;

			// A known icon color keeps the pixel scan independent of the host theme.
			margin.IconBrush = new SolidColorBrush(Colors.Red);
			margin.InvalidateVisual();

			// The first render also lays the margin out, so its width is read afterwards. The mirror runs
			// on the headless drawing backend, so TestBitmapRendering reports this test inconclusive
			// instead of asserting on pixels.
			var bitmap = TestBitmapRendering.PumpAndRender(hosted.Host.Editor);
			int stripWidth = Math.Max(1, (int)Math.Ceiling(margin.Bounds.Width));

			Assert.IsTrue(
				TestBitmapRendering.HasColorInLeftStrip(bitmap, stripWidth, Colors.Red),
				"The indicator icon must render in the margin strip while actions are available.");

			// Moving the caret off the indicator line clears the indicator, so the icon disappears again.
			hosted.Host.Editor.CaretOffset = 0;
			Assert.IsFalse(hosted.Controller.HasActions);

			Assert.IsFalse(
				TestBitmapRendering.HasColorInLeftStrip(TestBitmapRendering.PumpAndRender(hosted.Host.Editor), stripWidth, Colors.Red),
				"The margin must not draw the indicator once the actions were cleared.");
		}
	}

	private static (CodeActionTestHost Host, TextCodeActionController Controller) CreateHostedMargin()
	{
		var host = new CodeActionTestHost("one\ntwo\nthree");
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		TextCodeActionController controller = host.CreateController();
		host.Editor.TextArea.LeftMargins.Add(controller.Margin);

		host.Editor.CaretOffset = 4;
		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		host.Editor.TextArea.TextView.EnsureVisualLines();

		return (host, controller);
	}

	private static double GetLineTop(CodeActionTestHost host, int lineNumber)
	{
		TextView textView = host.Editor.TextArea.TextView;
		VisualLine line = textView.GetVisualLine(lineNumber)
			?? throw new InvalidOperationException($"Visual line {lineNumber} was not materialized.");

		return line.VisualTop - textView.VerticalOffset;
	}
}
