using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using AvalonTextEditor = ICSharpCode.AvalonEdit.TextEditor;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

public sealed partial class MarkdownRendererTests
{
	[TestMethod]
	public void CreateContent_WheelOverScrollableCodeBlock_ScrollsCodeBlockBeforeViewer()
	{
		AssumeWheelScrollingEnabled();

		(HostWindow window, FlowDocumentScrollViewer viewer, AvalonTextEditor editor, ScrollViewer outerScrollViewer) = CreateScrollableTooltipWithCodeBlock();

		using (window)
		{
			// The code block's scroll surface is the text area's IScrollInfo; the editor's own scroll
			// members delegate to the templated scroll viewer.
			var editorScrollInfo = (IScrollInfo)editor.TextArea;
			double editorScrollableHeight = editorScrollInfo.ExtentHeight - editorScrollInfo.ViewportHeight;

			Assert.IsTrue(editorScrollableHeight > 0.0, "The code block must be scrollable for this test.");
			Assert.IsTrue(outerScrollViewer.ScrollableHeight > 0.0, "The viewer must be scrollable for this test.");

			outerScrollViewer.ScrollToTop();
			editorScrollInfo.SetVerticalOffset(0.0);
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			RaiseMouseWheel(editor, -Mouse.MouseWheelDeltaForOneLine);
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			Assert.IsTrue(editorScrollInfo.VerticalOffset > 0.0, "The code block should scroll before the viewer.");
			Assert.AreEqual(0.0, outerScrollViewer.VerticalOffset, "The viewer must not steal the wheel event from a scrollable code block.");

			// At the code block's scroll end the viewer takes the wheel over.
			editorScrollInfo.SetVerticalOffset(editorScrollableHeight);
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			RaiseMouseWheel(editor, -Mouse.MouseWheelDeltaForOneLine);
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			Assert.IsTrue(outerScrollViewer.VerticalOffset > 0.0, "The viewer should scroll once the code block reaches its end.");
		}
	}

	[TestMethod]
	public void CreateContent_WheelOverViewerBody_ScrollsViewer()
	{
		AssumeWheelScrollingEnabled();

		(HostWindow window, FlowDocumentScrollViewer viewer, _, ScrollViewer outerScrollViewer) = CreateScrollableTooltipWithCodeBlock();

		using (window)
		{
			outerScrollViewer.ScrollToTop();
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			RaiseMouseWheel(viewer, -Mouse.MouseWheelDeltaForOneLine);
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			Assert.IsTrue(outerScrollViewer.VerticalOffset > 0.0);
		}
	}

	[TestMethod]
	public void CreateContent_ScrollingDisabled_WheelDoesNotScrollViewer()
	{
		AssumeWheelScrollingEnabled();

		string body = CreateScrollableTooltipBody();
		var theme = MarkdownRenderTheme.Default with { MaxHeight = 150.0 };
		var options = new MarkdownRenderOptions { AllowScrolling = false };
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent(body, theme, options);

		using HostWindow window = WPFTestHost.ShowInHostWindow(viewer);

		viewer.UpdateLayout();
		WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

		ScrollViewer innerScrollViewer = WPFTestHost.FindVisualDescendants<ScrollViewer>(viewer).First();

		Assert.IsTrue(innerScrollViewer.ScrollableHeight > 0.0, "The viewer must be scrollable for this test.");

		double initialOffset = innerScrollViewer.VerticalOffset;

		RaiseMouseWheel(innerScrollViewer, -Mouse.MouseWheelDeltaForOneLine);
		WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(initialOffset, innerScrollViewer.VerticalOffset);
	}

	[TestMethod]
	public void CreateContent_WheelAtViewerEnd_ReachesAnEnclosingHostScroller()
	{
		AssumeWheelScrollingEnabled();

		string body = CreateScrollableTooltipBody();
		var theme = MarkdownRenderTheme.Default with { MaxHeight = 150.0 };
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent(body, theme);

		using HostWindow window = ShowContentInsideHostScroller(viewer, out ScrollViewer outerScrollViewer);

		ScrollViewer innerScrollViewer = WPFTestHost.FindVisualDescendants<ScrollViewer>(viewer).First();

		Assert.IsTrue(innerScrollViewer.ScrollableHeight > 0.0, "The viewer must be scrollable for this test.");
		Assert.IsTrue(outerScrollViewer.ScrollableHeight > 0.0, "The host scroller must be scrollable for this test.");

		innerScrollViewer.ScrollToBottom();
		outerScrollViewer.ScrollToTop();
		WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

		RaiseMouseWheel(viewer, -Mouse.MouseWheelDeltaForOneLine);
		WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

		Assert.IsTrue(outerScrollViewer.VerticalOffset > 0.0, "A wheel the viewer cannot consume must reach the enclosing host scroller.");
	}

	[TestMethod]
	public void CreateContent_ScrollingDisabled_WheelReachesAnEnclosingHostScroller()
	{
		AssumeWheelScrollingEnabled();

		string body = CreateScrollableTooltipBody();
		var theme = MarkdownRenderTheme.Default with { MaxHeight = 150.0 };
		var options = new MarkdownRenderOptions { AllowScrolling = false };
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent(body, theme, options);

		using HostWindow window = ShowContentInsideHostScroller(viewer, out ScrollViewer outerScrollViewer);

		Assert.IsTrue(outerScrollViewer.ScrollableHeight > 0.0, "The host scroller must be scrollable for this test.");

		RaiseMouseWheel(viewer, -Mouse.MouseWheelDeltaForOneLine);
		WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

		Assert.IsTrue(outerScrollViewer.VerticalOffset > 0.0, "Scrolling-disabled content must forward the wheel to the enclosing host scroller.");
	}

	[TestMethod]
	public void CreateContent_WheelUpOverViewer_ScrollsViewerTowardStart()
	{
		AssumeWheelScrollingEnabled();

		(HostWindow window, FlowDocumentScrollViewer viewer, _, ScrollViewer outerScrollViewer) = CreateScrollableTooltipWithCodeBlock();

		using (window)
		{
			outerScrollViewer.ScrollToBottom();
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			double offsetBeforeWheel = outerScrollViewer.VerticalOffset;

			RaiseMouseWheel(viewer, Mouse.MouseWheelDeltaForOneLine);
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			Assert.IsTrue(outerScrollViewer.VerticalOffset < offsetBeforeWheel, "A wheel up must scroll the viewer toward the start.");
		}
	}

	[TestMethod]
	public void CreateContent_ZeroDeltaWheel_DoesNotScroll()
	{
		(HostWindow window, FlowDocumentScrollViewer viewer, AvalonTextEditor editor, ScrollViewer outerScrollViewer) = CreateScrollableTooltipWithCodeBlock();

		using (window)
		{
			var editorScrollInfo = (IScrollInfo)editor.TextArea;
			double viewerOffsetBeforeWheel = outerScrollViewer.VerticalOffset;
			double editorOffsetBeforeWheel = editorScrollInfo.VerticalOffset;

			RaiseMouseWheel(editor, 0);
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			Assert.AreEqual(viewerOffsetBeforeWheel, outerScrollViewer.VerticalOffset);
			Assert.AreEqual(editorOffsetBeforeWheel, editorScrollInfo.VerticalOffset);
		}
	}

	[TestMethod]
	public void CreateContent_WheelChainingDisabled_ViewerKeepsTheWheel()
	{
		AssumeWheelScrollingEnabled();

		string body = CreateScrollableTooltipBody();
		var theme = MarkdownRenderTheme.Default with { MaxHeight = 150.0 };
		var options = new MarkdownRenderOptions { AllowWheelChaining = false };
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent(body, theme, options);

		using HostWindow window = ShowContentInsideHostScroller(viewer, out ScrollViewer hostScrollViewer);

		ScrollViewer viewerScrollViewer = WPFTestHost.FindVisualDescendants<ScrollViewer>(viewer).First();

		double hostOffsetBeforeWheel = hostScrollViewer.VerticalOffset;

		// The wheel is raised on the viewer's own scroll surface, as a real routed wheel event would
		// reach it; with chaining disabled nothing forwards it to the host scroller.
		RaiseMouseWheel(viewerScrollViewer, -Mouse.MouseWheelDeltaForOneLine);
		WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

		Assert.AreEqual(hostOffsetBeforeWheel, hostScrollViewer.VerticalOffset);
	}

	[TestMethod]
	public void CreateContent_WheelOverCodeBlockBorder_ScrollsCodeBlockBeforeViewer()
	{
		AssumeWheelScrollingEnabled();

		(HostWindow window, FlowDocumentScrollViewer viewer, AvalonTextEditor editor, ScrollViewer outerScrollViewer) = CreateScrollableTooltipWithCodeBlock();

		using (window)
		{
			var editorScrollInfo = (IScrollInfo)editor.TextArea;
			Border border = GetCodeBlockBorder(viewer.Document);

			outerScrollViewer.ScrollToTop();
			editorScrollInfo.SetVerticalOffset(0.0);
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			// A hit on the code block's border or padding must scroll the hosted editor, not the viewer.
			RaiseMouseWheel(border, -Mouse.MouseWheelDeltaForOneLine);
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			Assert.IsTrue(editorScrollInfo.VerticalOffset > 0.0, "A wheel over the code block's border must scroll the code block.");
			Assert.AreEqual(0.0, outerScrollViewer.VerticalOffset, "The viewer must not take a wheel over the code block's border.");
		}
	}

	[TestMethod]
	public void CreateContent_WheelOverCodeBlockBorder_AtBothScrollEnds_ReachesTheEnclosingHostScroller()
	{
		AssumeWheelScrollingEnabled();

		string body = CreateScrollableTooltipBody();
		string code = string.Join("\n", Enumerable.Range(1, 40).Select(index => $"local value{index} = {index}"));
		var theme = MarkdownRenderTheme.Default with { MaxHeight = 150.0, MaxVisibleCodeBlockLines = 5 };
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent($"{body}\n\n```lua\n{code}\n```", theme);

		using HostWindow window = ShowContentInsideHostScroller(viewer, out ScrollViewer hostScrollViewer);

		AvalonTextEditor editor = GetCodeBlockEditor(viewer.Document);
		Border border = GetCodeBlockBorder(viewer.Document);
		var editorScrollInfo = (IScrollInfo)editor.TextArea;
		ScrollViewer viewerScrollViewer = WPFTestHost.FindVisualDescendants<ScrollViewer>(viewer).First();

		Assert.IsTrue(viewerScrollViewer.ScrollableHeight > 0.0, "The viewer must be scrollable for this test.");
		Assert.IsTrue(hostScrollViewer.ScrollableHeight > 0.0, "The host scroller must be scrollable for this test.");

		editorScrollInfo.SetVerticalOffset(editorScrollInfo.ExtentHeight - editorScrollInfo.ViewportHeight);
		viewerScrollViewer.ScrollToBottom();
		hostScrollViewer.ScrollToTop();
		WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

		RaiseMouseWheel(border, -Mouse.MouseWheelDeltaForOneLine);
		WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

		Assert.IsTrue(hostScrollViewer.VerticalOffset > 0.0, "A wheel neither the code block nor the viewer can consume must reach the host scroller.");
	}

	[TestMethod]
	public void CreateContent_WheelScrollsClampedCodeBlock_TracksTheEditorScrollBar()
	{
		AssumeWheelScrollingEnabled();

		(HostWindow window, FlowDocumentScrollViewer viewer, AvalonTextEditor editor, _) = CreateScrollableTooltipWithCodeBlock();

		using (window)
		{
			var editorScrollInfo = (IScrollInfo)editor.TextArea;

			editorScrollInfo.SetVerticalOffset(0.0);
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			RaiseMouseWheel(editor, -Mouse.MouseWheelDeltaForOneLine);
			WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

			ScrollBar editorScrollBar = WPFTestHost.FindVisualDescendants<ScrollBar>(editor).First(scrollBar => scrollBar.Orientation == Orientation.Vertical);

			Assert.IsTrue(editorScrollInfo.VerticalOffset > 0.0);
			Assert.IsTrue(editorScrollBar.Value > 0.0, "The editor's scroll bar must track a wheel scroll, not only the text surface offset.");
		}
	}

	[TestMethod]
	public void CreatePlainTextContent_ScrollingDisabled_WheelReachesAnEnclosingHostScroller()
	{
		AssumeWheelScrollingEnabled();

		string body = CreateScrollableTooltipBody();
		var theme = MarkdownRenderTheme.Default with { MaxHeight = 150.0 };
		var options = new MarkdownRenderOptions { AllowScrolling = false };
		ScrollViewer fallback = MarkdownRenderer.CreatePlainTextContent(body, theme, options);

		using HostWindow window = ShowContentInsideHostScroller(fallback, out ScrollViewer hostScrollViewer);

		Assert.IsTrue(hostScrollViewer.ScrollableHeight > 0.0, "The host scroller must be scrollable for this test.");

		RaiseMouseWheel(fallback, -Mouse.MouseWheelDeltaForOneLine);
		WPFTestHost.PumpDispatcher(fallback.Dispatcher, DispatcherPriority.Background);

		Assert.IsTrue(hostScrollViewer.VerticalOffset > 0.0, "Scrolling-disabled plain text must forward the wheel to the enclosing host scroller.");
	}

	[DataRow(120, 3, 1, false, false)]
	[DataRow(240, 3, 2, false, false)]
	[DataRow(-120, 3, 1, false, false)]
	[DataRow(40, 3, 1, false, false)]
	[DataRow(120, 0, 1, true, false)]
	[DataRow(120, -1, 1, false, true)]
	[TestMethod]
	public void WheelStep_FromDelta_MapsNotchesAndWheelSettings(int delta, int linesPerNotch, int expectedNotches, bool expectedDisabled, bool expectedPageStep)
	{
		var step = MarkdownScrollChaining.WheelStep.FromDelta(delta, linesPerNotch);

		Assert.AreEqual(expectedNotches, step.NotchCount);
		Assert.AreEqual(expectedDisabled, step.IsDisabled);
		Assert.AreEqual(expectedPageStep, step.IsPageStep);
	}

	[TestMethod]
	public void WheelStep_FromDelta_ExtremeDelta_DoesNotOverflow()
	{
		// Math.Abs(int.MinValue) overflows; the helper computes the magnitude on the widened value, so even
		// the most negative delta maps to a positive notch count instead of throwing.
		var step = MarkdownScrollChaining.WheelStep.FromDelta(int.MinValue, 3);

		Assert.AreEqual(17895697, step.NotchCount);
		Assert.IsFalse(step.IsDisabled);
		Assert.IsFalse(step.IsPageStep);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_WheelOverScrollableEditor_ScrollsTheEditorBeforeTheHostScroller()
	{
		AssumeWheelScrollingEnabled();

		(HostWindow window, AvalonTextEditor editor, ScrollViewer hostScrollViewer) = CreateStandaloneCodeBlockEditorInHostScroller();

		using (window)
		{
			// A code-block editor used without the rendered viewer routes its own wheel: it scrolls itself
			// while it can and only hands the wheel to the enclosing host at its scrolling boundary.
			var editorScrollInfo = (IScrollInfo)editor.TextArea;
			double editorScrollableHeight = editorScrollInfo.ExtentHeight - editorScrollInfo.ViewportHeight;

			Assert.IsTrue(editorScrollableHeight > 0.0, "The standalone editor must be scrollable for this test.");
			Assert.IsTrue(hostScrollViewer.ScrollableHeight > 0.0, "The host scroller must be scrollable for this test.");

			hostScrollViewer.ScrollToTop();
			editorScrollInfo.SetVerticalOffset(0.0);
			WPFTestHost.PumpDispatcher(editor.Dispatcher, DispatcherPriority.Background);

			RaiseMouseWheel(editor, -Mouse.MouseWheelDeltaForOneLine);
			WPFTestHost.PumpDispatcher(editor.Dispatcher, DispatcherPriority.Background);

			Assert.IsTrue(editorScrollInfo.VerticalOffset > 0.0, "The standalone code block must scroll itself before its host.");
			Assert.AreEqual(0.0, hostScrollViewer.VerticalOffset, "The host must not steal the wheel from a scrollable standalone code block.");
		}
	}

	[TestMethod]
	public void CreateCodeBlockEditor_WheelAtEditorScrollEnd_ReachesTheEnclosingHostScroller()
	{
		AssumeWheelScrollingEnabled();

		(HostWindow window, AvalonTextEditor editor, ScrollViewer hostScrollViewer) = CreateStandaloneCodeBlockEditorInHostScroller();

		using (window)
		{
			var editorScrollInfo = (IScrollInfo)editor.TextArea;
			double editorScrollableHeight = editorScrollInfo.ExtentHeight - editorScrollInfo.ViewportHeight;

			Assert.IsTrue(editorScrollableHeight > 0.0, "The standalone editor must be scrollable for this test.");
			Assert.IsTrue(hostScrollViewer.ScrollableHeight > 0.0, "The host scroller must be scrollable for this test.");

			hostScrollViewer.ScrollToTop();
			editorScrollInfo.SetVerticalOffset(editorScrollableHeight);
			WPFTestHost.PumpDispatcher(editor.Dispatcher, DispatcherPriority.Background);

			RaiseMouseWheel(editor, -Mouse.MouseWheelDeltaForOneLine);
			WPFTestHost.PumpDispatcher(editor.Dispatcher, DispatcherPriority.Background);

			Assert.IsTrue(hostScrollViewer.VerticalOffset > 0.0, "A wheel the standalone code block cannot consume must reach the enclosing host scroller.");
		}
	}
}
