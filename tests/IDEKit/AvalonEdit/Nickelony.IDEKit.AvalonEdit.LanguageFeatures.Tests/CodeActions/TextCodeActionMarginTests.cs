using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.IntelliSense.CodeActions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed class TextCodeActionMarginTests
{
	[TestMethod]
	public void Constructor_NullController_Throws()
		=> Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionMargin(null!));

	[TestMethod]
	public void DefaultProperties_UseTheSystemGrayTextColorAndALibraryIcon()
	{
		using var host = new CodeActionTestHost();
		using var controller = host.CreateController();

		TextCodeActionMargin margin = controller.Margin;

		var brushMetadata = (FrameworkPropertyMetadata)TextCodeActionMargin.IconBrushProperty.GetMetadata(typeof(TextCodeActionMargin));
		var geometryMetadata = (FrameworkPropertyMetadata)TextCodeActionMargin.IconGeometryProperty.GetMetadata(typeof(TextCodeActionMargin));

		// The default indicator color is the system gray-text color - a neutral default that is visible in
		// common themes - instead of a host-specific accent color.
		var defaultBrush = (SolidColorBrush)brushMetadata.DefaultValue;

		Assert.AreEqual(SystemColors.GrayTextColor, defaultBrush.Color);
		Assert.IsTrue(defaultBrush.IsFrozen);

		var geometry = (Geometry)geometryMetadata.DefaultValue;

		Assert.IsTrue(geometry.Bounds.Width > 0.0);
		Assert.IsTrue(geometry.Bounds.Height > 0.0);
		Assert.AreSame(margin.IconBrush, defaultBrush);
		Assert.AreSame(margin.IconGeometry, geometry);

		Assert.IsTrue(brushMetadata.AffectsRender);
		Assert.IsTrue(geometryMetadata.AffectsRender);
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

			// A synthetic mouse event carries no position, so the test seam supplies a deterministic click
			// point on the indicator's line.
			margin.TestHooks.ClickPositionResolver = _ => new Point(8.0, GetLineTop(hosted.Host, 2) + 1.0);

			var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
			{
				RoutedEvent = UIElement.MouseLeftButtonDownEvent
			};

			margin.RaiseEvent(args);

			Assert.IsTrue(args.Handled);
			Assert.IsTrue(hosted.Controller.IsActionsOpen);
		}
	}

	[TestMethod]
	public void OnMouseLeftButtonDown_UnattachedMargin_DoesNotThrow()
	{
		var editor = WPFTestHost.CreateEditor("one");
		using var controller = CodeActionTestHost.CreateUnhostedController(editor);

		TextCodeActionMargin margin = controller.Margin;

		var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
		{
			RoutedEvent = UIElement.MouseLeftButtonDownEvent
		};

		margin.RaiseEvent(args);

		Assert.IsFalse(args.Handled);
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
			margin.TestHooks.ClickPositionResolver = _ => clickPoint;

			var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
			{
				RoutedEvent = UIElement.MouseLeftButtonDownEvent
			};

			margin.RaiseEvent(args);

			// The host owns the gesture now, so the press stays unhandled and nothing opens; the
			// host-driven seam still opens the same menu.
			Assert.IsFalse(args.Handled);
			Assert.IsFalse(hosted.Controller.IsActionsOpen);
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

			// The first render also lays the margin out, so its width is read afterwards.
			var bitmap = TestBitmapRendering.PumpAndRender(hosted.Host.Editor);
			int stripWidth = Math.Max(1, (int)Math.Ceiling(margin.ActualWidth));

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
