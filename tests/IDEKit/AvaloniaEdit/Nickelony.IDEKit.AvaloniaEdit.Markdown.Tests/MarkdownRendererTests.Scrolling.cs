using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaEdit;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;

// With chaining disabled (AllowWheelChaining = false) the handler is not attached and the toolkit's default routing applies, so a synthesized
// PointerWheelEventArgs bubbles to the enclosing host scroller rather than being consumed by the viewer. Real platform input keeps the wheel on
// the viewer, but no test can model that through a directly raised event, so this path stays unverified here.
public sealed partial class MarkdownRendererTests
{
	// One wheel notch down: the toolkit reports a wheel delta of magnitude ~1.0 per notch, and MarkdownScrollChaining multiplies it
	// by its own pixel step, so a unit delta moves one step.
	private const double WheelDown = -1.0;

	private const double WheelUp = 1.0;

	[TestMethod]
	public void CreatePlainTextContent_ZeroDeltaWheel_DoesNotScrollAnything()
	{
		using HostWindow window = ShowInsideHostScroller(CreateScrollableFallback(null), out ScrollViewer hostScroller);

		hostScroller.Offset = new Vector(0.0, 0.0);

		RaiseWheelDelta((Interactive)((StackPanel)hostScroller.Content!).Children[0], 0.0);

		Assert.AreEqual(0.0, hostScroller.Offset.Y, "A zero-delta wheel must not move the host scroller.");
	}

	[TestMethod]
	public void AttachViewer_AttachedAgain_DoesNotDoubleTheWheelStep()
	{
		var options = new MarkdownRenderOptions();

		double onceOffset = ScrollViewerOffsetAfterWheel(options, extraAttachCount: 0);
		double twiceOffset = ScrollViewerOffsetAfterWheel(options, extraAttachCount: 1);

		Assert.IsGreaterThan(0.0, onceOffset, "The wheel must scroll the content viewer.");
		Assert.AreEqual(onceOffset, twiceOffset, "A second attach must not add a second handler and double the step.");
	}

	protected override IDisposable ShowInHostWindow(Control content) => AvaloniaTestHost.ShowInHostWindow(content);

	protected override IDisposable ShowInHostScroller(Control content, out ScrollSurface hostScroller)
	{
		HostWindow window = ShowInsideHostScroller(content, out ScrollViewer scrollViewer);

		hostScroller = CreateSurface(scrollViewer);
		return window;
	}

	protected override ScrollSurface GetViewerSurface(Control viewer) => CreateSurface((ScrollViewer)viewer);

	protected override ScrollSurface GetEditorSurface(TextEditor editor) => CreateSurface(FindEditorScrollViewer(editor));

	protected override void RaiseWheel(Control target, bool down, double notches = 1.0) =>
		RaiseWheelDelta(target, notches * (down ? WheelDown : WheelUp));

	// The renderer's own viewer is attached once; the loop adds further attaches to prove idempotence.
	private static double ScrollViewerOffsetAfterWheel(MarkdownRenderOptions options, int extraAttachCount)
	{
		ScrollViewer viewer = CreateScrollableFallback(options);

		using HostWindow window = AvaloniaTestHost.ShowInHostWindow(viewer);

		for (int i = 0; i < extraAttachCount; i++)
			MarkdownScrollChaining.AttachViewer(viewer, options);

		viewer.Offset = new Vector(0.0, 0.0);
		RaiseWheelDelta(viewer, -50.0);

		return viewer.Offset.Y;
	}

	private static ScrollViewer CreateScrollableFallback(MarkdownRenderOptions? options)
	{
		MarkdownRenderTheme theme = MarkdownRenderTheme.Default with { MaxHeight = 150.0 };
		return MarkdownRenderer.CreatePlainTextContent(CreateScrollableBody(), theme, options);
	}

	private static HostWindow ShowInsideHostScroller(Control content, out ScrollViewer hostScroller)
	{
		var panel = new StackPanel();

		panel.Children.Add(content);
		panel.Children.Add(new Border { Height = 400.0 });

		hostScroller = new ScrollViewer
		{
			Content = panel,
			Height = 200.0,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
		};

		return AvaloniaTestHost.ShowInHostWindow(hostScroller);
	}

	private static ScrollSurface CreateSurface(ScrollViewer scrollViewer)
	{
		return new ScrollSurface(
			readOffset: () => scrollViewer.Offset.Y,
			writeOffset: offset => scrollViewer.Offset = new Vector(0.0, offset),
			readScrollableHeight: () => Math.Max(0.0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height)
		);
	}

	private static void RaiseWheelDelta(Interactive element, double deltaY)
	{
		var eventArgs = new PointerWheelEventArgs(
			source: element,
			pointer: new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true),
			rootVisual: element,
			rootVisualPosition: default,
			timestamp: 0UL,
			properties: new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
			modifiers: KeyModifiers.None,
			delta: new Vector(0.0, deltaY)
		);

		element.RaiseEvent(eventArgs);
	}

	// The editor's own scroll surface is the scroll viewer its template wraps the text area in. The walk
	// mirrors MarkdownScrollChaining's own lookup so a scenario asserts against the same surface.
	private static ScrollViewer FindEditorScrollViewer(TextEditor editor)
	{
		editor.ApplyTemplate();

		Visual? current = editor.TextArea.TextView;

		while (current is not null && !ReferenceEquals(current, editor))
		{
			if (current is ScrollViewer scrollViewer)
				return scrollViewer;

			current = current.GetVisualParent();
		}

		throw new InvalidOperationException("The editor's scroll surface was not materialized.");
	}
}
