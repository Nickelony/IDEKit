using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using EditorControl = ICSharpCode.AvalonEdit.TextEditor;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

public sealed partial class MarkdownRendererTests
{
	[TestMethod]
	public void CreateContent_ScrollingDisabled_WheelDoesNotScrollViewer()
	{
		AssumeWheelScrollingEnabled();

		string body = CreateScrollableBody();
		MarkdownRenderTheme theme = MarkdownRenderTheme.Default with { MaxHeight = 150.0 };

		var options = new MarkdownRenderOptions { AllowScrolling = false };
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent(body, theme, options);

		using HostWindow window = WpfTestHost.ShowInHostWindow(viewer);

		viewer.UpdateLayout();
		WpfTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

		ScrollViewer innerScrollViewer = WpfTestHost.FindVisualDescendants<ScrollViewer>(viewer).First();

		Assert.IsGreaterThan(0.0, innerScrollViewer.ScrollableHeight, "The viewer must be scrollable for this test.");

		double initialOffset = innerScrollViewer.VerticalOffset;

		RaiseMouseWheel(innerScrollViewer, -Mouse.MouseWheelDeltaForOneLine);
		WpfTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(initialOffset, innerScrollViewer.VerticalOffset);
	}

	[TestMethod]
	public void CreateContent_WheelChainingDisabled_ViewerKeepsTheWheel()
	{
		AssumeWheelScrollingEnabled();

		string body = CreateScrollableBody();
		MarkdownRenderTheme theme = MarkdownRenderTheme.Default with { MaxHeight = 150.0 };

		var options = new MarkdownRenderOptions { AllowWheelChaining = false };
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent(body, theme, options);

		using HostWindow window = ShowContentInsideHostScroller(viewer, out ScrollViewer hostScrollViewer);

		ScrollViewer viewerScrollViewer = WpfTestHost.FindVisualDescendants<ScrollViewer>(viewer).First();

		double hostOffsetBeforeWheel = hostScrollViewer.VerticalOffset;

		// The wheel is raised on the viewer's own scroll surface, as a real routed wheel event would
		// reach it; with chaining disabled nothing forwards it to the host scroller.
		RaiseMouseWheel(viewerScrollViewer, -Mouse.MouseWheelDeltaForOneLine);
		WpfTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(hostOffsetBeforeWheel, hostScrollViewer.VerticalOffset);
	}

	[TestMethod]
	public void CreateContent_WheelScrollsClampedCodeBlock_TracksTheEditorScrollBar()
	{
		AssumeWheelScrollingEnabled();

		(HostWindow window, FlowDocumentScrollViewer viewer, EditorControl editor, _) = CreateScrollableTooltipWithCodeBlock();

		using (window)
		{
			var editorScrollInfo = (IScrollInfo)editor.TextArea;

			editorScrollInfo.SetVerticalOffset(0.0);
			WpfTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			RaiseMouseWheel(editor, -Mouse.MouseWheelDeltaForOneLine);
			WpfTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			ScrollBar editorScrollBar = WpfTestHost
				.FindVisualDescendants<ScrollBar>(editor)
				.First(scrollBar => scrollBar.Orientation == Orientation.Vertical);

			Assert.IsGreaterThan(0.0, editorScrollInfo.VerticalOffset);
			Assert.IsGreaterThan(0.0, editorScrollBar.Value, "The editor's scroll bar must track a wheel scroll, not only the text surface offset.");
		}
	}

	[DataRow(120, 3, 1.0, false, false)]
	[DataRow(240, 3, 2.0, false, false)]
	[DataRow(-120, 3, 1.0, false, false)]
	[DataRow(60, 3, 0.5, false, false)]
	[DataRow(180, 3, 1.5, false, false)]
	[DataRow(120, 0, 1.0, true, false)]
	[DataRow(120, -1, 1.0, false, true)]
	[TestMethod]
	public void WheelStep_FromDelta_MapsNotchesAndWheelSettings(
		int delta,
		int linesPerNotch,
		double expectedNotches,
		bool expectedDisabled,
		bool expectedPageStep
	)
	{
		var step = MarkdownScrollChaining.WheelStep.FromDelta(delta, linesPerNotch);

		Assert.AreEqual(expectedNotches, step.Notches, 1e-9);
		Assert.AreEqual(expectedDisabled, step.IsDisabled);
		Assert.AreEqual(expectedPageStep, step.IsPageStep);
	}

	[TestMethod]
	public void WheelStep_FromDelta_ExtremeDelta_DoesNotOverflow()
	{
		// Math.Abs(int.MinValue) overflows; the helper computes the magnitude on the widened value, so even
		// the most negative delta maps to a positive notch count instead of throwing.
		var step = MarkdownScrollChaining.WheelStep.FromDelta(int.MinValue, 3);

		Assert.AreEqual(2147483648.0 / 120.0, step.Notches, 1e-6);
		Assert.IsFalse(step.IsDisabled);
		Assert.IsFalse(step.IsPageStep);
	}

	protected override IDisposable ShowInHostWindow(FrameworkElement content)
	{
		HostWindow window = WpfTestHost.ShowInHostWindow(content);

		content.UpdateLayout();
		WpfTestHost.PumpDispatcher(content.Dispatcher, DispatcherPriority.Background);

		return window;
	}

	protected override IDisposable ShowInHostScroller(FrameworkElement content, out ScrollSurface hostScroller)
	{
		HostWindow window = ShowContentInsideHostScroller(content, out ScrollViewer scrollViewer);

		hostScroller = CreateSurface(scrollViewer);
		return window;
	}

	protected override ScrollSurface GetViewerSurface(FrameworkElement viewer) =>
		CreateSurface(WpfTestHost.FindVisualDescendants<ScrollViewer>(viewer).First());

	protected override ScrollSurface GetEditorSurface(EditorControl editor)
	{
		var scrollInfo = (IScrollInfo)editor.TextArea;

		return new ScrollSurface(
			readOffset: () => scrollInfo.VerticalOffset,

			writeOffset: offset =>
			{
				scrollInfo.SetVerticalOffset(offset);
				WpfTestHost.PumpDispatcher(editor.Dispatcher, DispatcherPriority.Background);
			},

			readScrollableHeight: () => scrollInfo.ExtentHeight - scrollInfo.ViewportHeight
		);
	}

	protected override void RaiseWheel(FrameworkElement target, bool down, double notches = 1.0)
	{
		int delta = (int)Math.Round(notches * Mouse.MouseWheelDeltaForOneLine);

		RaiseMouseWheel(target, down ? -delta : delta);
		WpfTestHost.PumpDispatcher(target.Dispatcher, DispatcherPriority.Background);
	}

	private static ScrollSurface CreateSurface(ScrollViewer scrollViewer)
	{
		return new ScrollSurface(
			readOffset: () => scrollViewer.VerticalOffset,

			writeOffset: offset =>
			{
				scrollViewer.ScrollToVerticalOffset(offset);
				WpfTestHost.PumpDispatcher(scrollViewer.Dispatcher, DispatcherPriority.Background);
			},

			readScrollableHeight: () => scrollViewer.ScrollableHeight
		);
	}
}
