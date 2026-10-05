#if AVALONIAEDIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.Core.LineStatus;
using Nickelony.IDEKit.Core.Notifications;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.Core.LineStatus;
using Nickelony.IDEKit.Core.Notifications;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>Covers the code-action margin's indicator surface and its open paths.</summary>
[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class CodeActionMarginTests
{
	[TestMethod]
	public void DefaultProperties_UseTheNeutralGrayColorAndALibraryIcon()
	{
		using var host = new CodeActionPresenterTestHost();

		using CodeActionController controller = host.CreateController();
		using CodeActionPresenter presenter = host.CreatePresenter(controller);

		CodeActionMargin margin = presenter.Margin;

		CodeActionTestHelpers.AssertDefaultIndicator(margin);
	}

	[TestMethod]
	public void IconProperties_NullAssignments_Throw()
	{
		using var host = new CodeActionPresenterTestHost();

		using CodeActionController controller = host.CreateController();
		using CodeActionPresenter presenter = host.CreatePresenter(controller);

		CodeActionMargin margin = presenter.Margin;

		Assert.ThrowsExactly<ArgumentNullException>(() => margin.IconBrush = null!);
		Assert.ThrowsExactly<ArgumentNullException>(() => margin.IconGeometry = null!);
	}

	[TestMethod]
	public void TryHandleClickAt_IndicatorLine_OpensTheMenuAndReportsTheClickHandled()
	{
		using var hosted = new HostedMargin();

		double y = GetLineTop(hosted.Host, 2);

		Assert.IsTrue(hosted.Margin.TryHandleClickAt(new Point(8.0, y + 1.0)));
		Assert.IsTrue(hosted.Presenter.IsMenuOpen);
	}

	[TestMethod]
	public void TryHandleClickAt_CustomAnchorOptions_MoveTheMenuByTheConfiguredDelta()
	{
		Point baselineAnchor;

		using (var baseline = new HostedMargin())
		{
			Assert.IsTrue(baseline.Margin.TryHandleClickAt(new Point(8.0, GetLineTop(baseline.Host, 2) + 1.0)));

			ContextMenu baselineMenu = baseline.Host.Menus[^1];
			baselineAnchor = new Point(baselineMenu.HorizontalOffset, baselineMenu.VerticalOffset);
		}

		CodeActionMenuOptions menuOptions = CodeActionMenuOptions.Default with
		{
			AnchorXOffset = CodeActionMenuOptions.Default.AnchorXOffset + 6.0,
			MarginAnchorYOffset = CodeActionMenuOptions.Default.MarginAnchorYOffset + 9.0,
		};

		using var hosted = new HostedMargin(menuOptions);

		Assert.IsTrue(hosted.Margin.TryHandleClickAt(new Point(8.0, GetLineTop(hosted.Host, 2) + 1.0)));

		ContextMenu menu = hosted.Host.Menus[^1];

		// The configured offsets move the menu by their deltas to the defaults, independent of the layout math that produced the baseline.
		Assert.AreEqual(baselineAnchor.X + 6.0, menu.HorizontalOffset, 1e-6);
		Assert.AreEqual(baselineAnchor.Y + 9.0, menu.VerticalOffset, 1e-6);
	}

	[TestMethod]
	public void TryHandleClickAt_LineWithoutIndicator_DoesNothing()
	{
		using var hosted = new HostedMargin();

		double y = GetLineTop(hosted.Host, 1);

		Assert.IsFalse(hosted.Margin.TryHandleClickAt(new Point(8.0, y + 1.0)));
		Assert.IsFalse(hosted.Presenter.IsMenuOpen);
	}

	[TestMethod]
	public void TryHandleClickAt_AfterIndicatorCleared_DoesNothing()
	{
		using var hosted = new HostedMargin();

		// Moving the caret to another line clears the indicator before the debounce elapses.
		hosted.Host.Editor.CaretOffset = 0;
		Assert.IsFalse(hosted.Controller.HasActions);

		double y = GetLineTop(hosted.Host, 2);

		Assert.IsFalse(hosted.Margin.TryHandleClickAt(new Point(8.0, y + 1.0)));
		Assert.IsFalse(hosted.Presenter.IsMenuOpen);
	}

	[TestMethod]
	public void TryHandleClickAt_AfterThePresenterIsDisposed_DoesNotOpenTheMenu()
	{
		using var hosted = new HostedMargin();

		double y = GetLineTop(hosted.Host, 2);

		hosted.Presenter.Dispose();

		Assert.IsFalse(hosted.Margin.TryHandleClickAt(new Point(8.0, y + 1.0)));
		Assert.IsFalse(hosted.Presenter.IsMenuOpen);
	}

#if AVALONIAEDIT
	[TestMethod]
	public void RoutedPress_IndicatorLine_OpensTheMenuAndHandlesTheEvent()
	{
		using var hosted = new HostedMargin();

		var raise = CodeActionTestHelpers.RaisePointerPressed(
			hosted.Margin,
			new Point(8.0, GetLineTop(hosted.Host, 2) + 1.0),
			PointerUpdateKind.LeftButtonPressed
		);

		Assert.IsTrue(raise.HandledByMargin);
		Assert.IsTrue(raise.Args.Handled);
		Assert.IsTrue(hosted.Presenter.IsMenuOpen);
	}

	[TestMethod]
	public void RoutedPress_OpensOnLeftClickDisabled_LeavesThePressUnhandledByTheMargin()
	{
		using var hosted = new HostedMargin();

		hosted.Margin.OpensOnLeftClick = false;

		var raise = CodeActionTestHelpers.RaisePointerPressed(
			hosted.Margin,
			new Point(8.0, GetLineTop(hosted.Host, 2) + 1.0),
			PointerUpdateKind.LeftButtonPressed
		);

		// The event's own Handled is not asserted false: AvaloniaEdit's TextArea consumes the press as it bubbles past the margin.
		Assert.IsFalse(raise.HandledByMargin);
		Assert.IsFalse(hosted.Presenter.IsMenuOpen);
	}
#endif

	[TestMethod]
	public void RoutedPress_UnattachedMargin_DoesNotThrow()
	{
		var editor = TestHost.CreateEditor("one");

		using CodeActionController controller = CodeActionPresenterTestHost.CreateUnhostedController(editor);
		using var presenter = new CodeActionPresenter(editor.TextArea, controller, CodeActionPresenterTestHost.CreatePresentation());

		CodeActionMargin margin = presenter.Margin;

		CodeActionTestHelpers.AssertUnattachedPressIsIgnored(margin);
		Assert.IsFalse(presenter.IsMenuOpen);
	}

	[TestMethod]
	public void OpensOnLeftClick_Disabled_ReportsTheClickUnhandledAndKeepsTheHostDrivenOpenWorking()
	{
		using var hosted = new HostedMargin();

		Point clickPoint = new(8.0, GetLineTop(hosted.Host, 2) + 1.0);

		hosted.Margin.OpensOnLeftClick = false;

		// The host owns the gesture now, so the click stays unhandled and nothing opens; the presenter's
		// host-driven entry point still opens the same menu.
		Assert.IsFalse(hosted.Margin.TryHandleClickAt(clickPoint));
		Assert.IsFalse(hosted.Presenter.IsMenuOpen);

		Assert.IsTrue(hosted.Presenter.TryOpenMenu(2, clickPoint));
		Assert.IsTrue(hosted.Presenter.IsMenuOpen);
	}

	[TestMethod]
	public void SourceSubscription_FollowsTheTextAreaConnection()
	{
		var editor = TestHost.CreateEditor("one");

		using CodeActionController controller = CodeActionPresenterTestHost.CreateUnhostedController(editor);
		using CodeActionPresenter presenter = new(editor.TextArea, controller, CodeActionPresenterTestHost.CreatePresentation());

		var source = new RecordingNotificationSource();
		var margin = new CodeActionMargin(source, presenter);

		Assert.AreEqual(0, source.SubscriberCount);

		editor.TextArea.LeftMargins.Add(margin);

		// The margin repaints when its source reports a change, so it listens for as long as it belongs to a text area.
		Assert.AreEqual(1, source.SubscriberCount);

		editor.TextArea.LeftMargins.Remove(margin);

		Assert.AreEqual(0, source.SubscriberCount);
	}

	[TestMethod]
	public void OnRender_IndicatorState_ControlsTheIcon()
	{
		using var hosted = new HostedMargin();

		CodeActionMargin margin = hosted.Margin;

		// A known icon color keeps the pixel scan independent of the host theme.
		margin.IconBrush = new SolidColorBrush(Colors.Red);
		margin.InvalidateVisual();

		// The first render also lays the margin out, so its width is read afterwards.
		var bitmap = TestBitmapRendering.PumpAndRender(hosted.Host.Editor);
		int stripWidth = CodeActionTestHelpers.GetStripWidth(margin);

		Assert.IsTrue(
			TestBitmapRendering.HasColorInLeftStrip(bitmap, stripWidth, Colors.Red),
			"The indicator icon must render in the margin strip while actions are available."
		);

		// Moving the caret off the indicator line clears the indicator, so the icon disappears again.
		hosted.Host.Editor.CaretOffset = 0;
		Assert.IsFalse(hosted.Controller.HasActions);

		Assert.IsFalse(
			TestBitmapRendering.HasColorInLeftStrip(TestBitmapRendering.PumpAndRender(hosted.Host.Editor), stripWidth, Colors.Red),
			"The margin must not draw the indicator once the actions were cleared."
		);
	}

#if !AVALONIAEDIT
	[TestMethod]
	public void ControllerChange_RepaintsTheMarginWithoutAnyOtherInvalidation()
	{
		using var host = new CodeActionPresenterTestHost
		{
			RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionPresenterTestHost.CreateItem("Fix it")]),
		};

		using CodeActionController controller = host.CreateController();
		using CodeActionPresenter presenter = host.CreatePresenter(controller);

		host.Editor.TextArea.LeftMargins.Add(presenter.Margin);
		host.Editor.TextArea.TextView.EnsureVisualLines();
		DispatcherTestHelpers.PumpUntilIdle();

		// Nothing is published yet, so the margin has drawn nothing.
		Assert.IsNull(VisualTreeHelper.GetDrawing(presenter.Margin));

		// The publication alone queues the repaint: no caret move, scroll, or layout change touches the margin in between.
		CodeActionPresenterTestHost.RunToCompletion(controller.RequestAsync());
		DispatcherTestHelpers.PumpUntilIdle();

		Assert.IsNotNull(VisualTreeHelper.GetDrawing(presenter.Margin));
	}
#endif

	private static double GetLineTop(CodeActionPresenterTestHost host, int lineNumber)
	{
		TextView textView = host.Editor.TextArea.TextView;

		VisualLine line =
			textView.GetVisualLine(lineNumber) ?? throw new InvalidOperationException($"Visual line {lineNumber} was not materialized.");

		return line.VisualTop - textView.VerticalOffset;
	}

	/// <summary>A line-status source that marks no line and counts the handlers attached to its change notification.</summary>
	private sealed class RecordingNotificationSource : ILineStatusSource, IChangeNotificationSource
	{
		private EventHandler? _changed;

		public int SubscriberCount => _changed?.GetInvocationList().Length ?? 0;

		public event EventHandler? Changed
		{
			add => _changed += value;
			remove => _changed -= value;
		}

		public IReadOnlyList<int> GetMarkedLineNumbers() => [];
	}

	/// <summary>
	/// A shown editor with a controller that published one action on line 2, a presenter, and the presenter's margin in the text area.
	/// </summary>
	private sealed class HostedMargin : IDisposable
	{
		internal HostedMargin(CodeActionMenuOptions? menuOptions = null)
		{
			Host = new CodeActionPresenterTestHost("one\ntwo\nthree")
			{
				RequestCodeActionsAsync = static (_, _) =>
					Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionPresenterTestHost.CreateItem("Fix it")]),
			};

			Controller = Host.CreateController();
			Presenter = Host.CreatePresenter(Controller, menuOptions);
			Host.Editor.TextArea.LeftMargins.Add(Presenter.Margin);

			Host.Editor.CaretOffset = 4;
			CodeActionPresenterTestHost.RunToCompletion(Controller.RequestAsync());

			Host.Editor.TextArea.TextView.EnsureVisualLines();
		}

		internal CodeActionPresenterTestHost Host { get; }

		internal CodeActionController Controller { get; }

		internal CodeActionPresenter Presenter { get; }

		internal CodeActionMargin Margin => Presenter.Margin;

		public void Dispose()
		{
			Presenter.Dispose();
			Controller.Dispose();
			Host.Dispose();
		}
	}
}
