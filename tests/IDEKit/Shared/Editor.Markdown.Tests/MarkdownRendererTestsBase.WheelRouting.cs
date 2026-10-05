#if AVALONIAEDIT
using Avalonia.Controls;
using EditorControl = AvaloniaEdit.TextEditor;
using SharedControl = Avalonia.Controls.Control;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;

#else
using System.Windows.Controls;
using EditorControl = ICSharpCode.AvalonEdit.TextEditor;
using SharedControl = System.Windows.FrameworkElement;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

#endif

public abstract partial class MarkdownRendererTestsBase
{
	private static readonly MarkdownRenderTheme s_scrollableTheme = MarkdownRenderTheme.Default with
	{
		MaxHeight = 150.0,
		MaxVisibleCodeBlockLines = 5,
	};

	/// <summary>The vertical position of one scroller, read and moved the same way whichever toolkit scroller backs it.</summary>
	/// <param name="readOffset">Reads the current vertical offset.</param>
	/// <param name="writeOffset">Moves the scroller to a vertical offset and lets the toolkit apply it.</param>
	/// <param name="readScrollableHeight">Reads how far the scroller can move.</param>
	protected sealed class ScrollSurface(Func<double> readOffset, Action<double> writeOffset, Func<double> readScrollableHeight)
	{
		public double Offset
		{
			get => readOffset();
			set => writeOffset(value);
		}

		public double ScrollableHeight => readScrollableHeight();
	}

	/// <summary>Skips the scenario when the machine has wheel scrolling turned off.</summary>
	protected virtual void AssumeWheelScrollingEnabled() { }

	/// <summary>Shows the content in a window and lays it out.</summary>
	protected abstract IDisposable ShowInHostWindow(SharedControl content);

	/// <summary>Shows the content in a window under a host scroller that is shorter than the content and a filler, and lays it out.</summary>
	protected abstract IDisposable ShowInHostScroller(SharedControl content, out ScrollSurface hostScroller);

	/// <summary>Returns the scroller of the rendered content, which scrolls the content body.</summary>
	protected abstract ScrollSurface GetViewerSurface(SharedControl viewer);

	/// <summary>Returns the scroller of a code-block editor.</summary>
	protected abstract ScrollSurface GetEditorSurface(EditorControl editor);

	/// <summary>Raises a wheel event of a number of notches over a control and lets the toolkit process it.</summary>
	/// <param name="target">The control under the pointer.</param>
	/// <param name="down"><see langword="true"/> to scroll toward the end; otherwise, toward the start.</param>
	/// <param name="notches">The size of the event in wheel notches, a fraction for a precision touchpad.</param>
	protected abstract void RaiseWheel(SharedControl target, bool down, double notches = 1.0);

	[TestMethod]
	public void CreateContent_WheelOverScrollableCodeBlock_ScrollsCodeBlockBeforeViewer()
	{
		AssumeWheelScrollingEnabled();

		SharedControl viewer = CreateScrollableCodeBlockViewer();
		using IDisposable window = ShowInHostWindow(viewer);

		EditorControl editor = FindCodeBlockEditor(viewer);

		ScrollSurface viewerSurface = GetViewerSurface(viewer);
		ScrollSurface editorSurface = GetEditorSurface(editor);

		double editorScrollableHeight = editorSurface.ScrollableHeight;

		Assert.IsGreaterThan(0.0, editorScrollableHeight, "The code block must be scrollable for this test.");
		Assert.IsGreaterThan(0.0, viewerSurface.ScrollableHeight, "The viewer must be scrollable for this test.");

		viewerSurface.Offset = 0.0;
		editorSurface.Offset = 0.0;

		RaiseWheel(editor, down: true);

		Assert.IsGreaterThan(0.0, editorSurface.Offset, "The code block should scroll before the viewer.");
		Assert.AreEqual(0.0, viewerSurface.Offset, "The viewer must not steal the wheel from a scrollable code block.");

		// At the code block's scroll end the viewer takes the wheel over.
		editorSurface.Offset = editorScrollableHeight;

		RaiseWheel(editor, down: true);

		Assert.IsGreaterThan(0.0, viewerSurface.Offset, "The viewer should scroll once the code block reaches its end.");
	}

	[TestMethod]
	public void CreateContent_ZeroDeltaWheel_DoesNotScroll()
	{
		SharedControl viewer = CreateScrollableCodeBlockViewer();
		using IDisposable window = ShowInHostWindow(viewer);

		EditorControl editor = FindCodeBlockEditor(viewer);

		ScrollSurface viewerSurface = GetViewerSurface(viewer);
		ScrollSurface editorSurface = GetEditorSurface(editor);

		Assert.IsGreaterThan(0.0, editorSurface.ScrollableHeight, "The code block must be scrollable for this test.");
		Assert.IsGreaterThan(0.0, viewerSurface.ScrollableHeight, "The viewer must be scrollable for this test.");

		viewerSurface.Offset = 0.0;
		editorSurface.Offset = 0.0;

		RaiseWheel(editor, down: true, notches: 0.0);

		Assert.AreEqual(0.0, viewerSurface.Offset);
		Assert.AreEqual(0.0, editorSurface.Offset);
	}

	[TestMethod]
	public void CreateContent_HalfANotchOverACodeBlock_ScrollsHalfAsFarAsAFullNotch()
	{
		AssumeWheelScrollingEnabled();

		SharedControl viewer = CreateScrollableCodeBlockViewer();
		using IDisposable window = ShowInHostWindow(viewer);

		EditorControl editor = FindCodeBlockEditor(viewer);
		ScrollSurface editorSurface = GetEditorSurface(editor);

		double ScrollFromStart(double notches)
		{
			editorSurface.Offset = 0.0;
			RaiseWheel(editor, down: true, notches);
			return editorSurface.Offset;
		}

		double fullNotch = ScrollFromStart(1.0);
		double halfNotch = ScrollFromStart(0.5);

		Assert.IsGreaterThan(0.0, fullNotch, "The code block must scroll for this test.");
		Assert.AreEqual(expected: fullNotch / 2.0, actual: halfNotch, delta: 0.5, message: "A half notch must scroll half as far as a full notch.");
	}

	[TestMethod]
	public void CreateContent_HalfANotchOverTheViewerBody_ScrollsHalfAsFarAsAFullNotch()
	{
		AssumeWheelScrollingEnabled();

		SharedControl viewer = CreateScrollableCodeBlockViewer();
		using IDisposable window = ShowInHostWindow(viewer);

		ScrollSurface viewerSurface = GetViewerSurface(viewer);

		double ScrollFromStart(double notches)
		{
			viewerSurface.Offset = 0.0;
			RaiseWheel(viewer, down: true, notches);
			return viewerSurface.Offset;
		}

		double fullNotch = ScrollFromStart(1.0);
		double halfNotch = ScrollFromStart(0.5);

		Assert.IsGreaterThan(0.0, fullNotch, "The viewer must scroll for this test.");
		Assert.AreEqual(expected: fullNotch / 2.0, actual: halfNotch, delta: 0.5, message: "A half notch must scroll half as far as a full notch.");
	}

	[TestMethod]
	public void CreateContent_WheelOverViewerBody_ScrollsViewer()
	{
		AssumeWheelScrollingEnabled();

		SharedControl viewer = CreateScrollableCodeBlockViewer();
		using IDisposable window = ShowInHostWindow(viewer);

		ScrollSurface viewerSurface = GetViewerSurface(viewer);

		Assert.IsGreaterThan(0.0, viewerSurface.ScrollableHeight, "The viewer must be scrollable for this test.");

		viewerSurface.Offset = 0.0;

		RaiseWheel(viewer, down: true);

		Assert.IsGreaterThan(0.0, viewerSurface.Offset, "A wheel over the viewer body must scroll the viewer.");
	}

	[TestMethod]
	public void CreateContent_WheelUpOverViewer_ScrollsViewerTowardStart()
	{
		AssumeWheelScrollingEnabled();

		SharedControl viewer = CreateScrollableCodeBlockViewer();
		using IDisposable window = ShowInHostWindow(viewer);

		ScrollSurface viewerSurface = GetViewerSurface(viewer);

		viewerSurface.Offset = viewerSurface.ScrollableHeight;

		double offsetBeforeWheel = viewerSurface.Offset;

		Assert.IsGreaterThan(0.0, offsetBeforeWheel, "The viewer must be scrollable for this test.");

		RaiseWheel(viewer, down: false);

		Assert.IsLessThan(offsetBeforeWheel, viewerSurface.Offset, "A wheel up must scroll the viewer toward the start.");
	}

	[TestMethod]
	public void CreateContent_WheelAtViewerEnd_ReachesAnEnclosingHostScroller()
	{
		AssumeWheelScrollingEnabled();

		SharedControl viewer = CreateScrollableCodeBlockViewer();
		using IDisposable window = ShowInHostScroller(viewer, out ScrollSurface hostScroller);

		ScrollSurface viewerSurface = GetViewerSurface(viewer);

		Assert.IsGreaterThan(0.0, viewerSurface.ScrollableHeight, "The viewer must be scrollable for this test.");
		Assert.IsGreaterThan(0.0, hostScroller.ScrollableHeight, "The host scroller must be scrollable for this test.");

		viewerSurface.Offset = viewerSurface.ScrollableHeight;
		hostScroller.Offset = 0.0;

		RaiseWheel(viewer, down: true);

		Assert.IsGreaterThan(0.0, hostScroller.Offset, "A wheel the viewer cannot consume must reach the enclosing host scroller.");
	}

	[TestMethod]
	public void CreateContent_ScrollingDisabled_WheelReachesAnEnclosingHostScroller()
	{
		AssumeWheelScrollingEnabled();

		var options = new MarkdownRenderOptions { AllowScrolling = false };

		SharedControl viewer = CreateScrollableCodeBlockViewer(options);
		using IDisposable window = ShowInHostScroller(viewer, out ScrollSurface hostScroller);

		Assert.IsGreaterThan(0.0, hostScroller.ScrollableHeight, "The host scroller must be scrollable for this test.");

		hostScroller.Offset = 0.0;

		RaiseWheel(viewer, down: true);

		Assert.IsGreaterThan(0.0, hostScroller.Offset, "Scrolling-disabled content must forward the wheel to the enclosing host scroller.");
	}

	[TestMethod]
	public void CreatePlainTextContent_ScrollingDisabled_WheelReachesAnEnclosingHostScroller()
	{
		AssumeWheelScrollingEnabled();

		var options = new MarkdownRenderOptions { AllowScrolling = false };

		SharedControl fallback = MarkdownRenderer.CreatePlainTextContent(CreateScrollableBody(), s_scrollableTheme, options);
		using IDisposable window = ShowInHostScroller(fallback, out ScrollSurface hostScroller);

		Assert.IsGreaterThan(0.0, hostScroller.ScrollableHeight, "The host scroller must be scrollable for this test.");

		hostScroller.Offset = 0.0;

		RaiseWheel(fallback, down: true);

		Assert.IsGreaterThan(0.0, hostScroller.Offset, "Scrolling-disabled plain text must forward the wheel to the enclosing host scroller.");
	}

	[TestMethod]
	public void CreateContent_WheelOverCodeBlockBorder_ScrollsCodeBlockBeforeViewer()
	{
		AssumeWheelScrollingEnabled();

		SharedControl viewer = CreateScrollableCodeBlockViewer();
		using IDisposable window = ShowInHostWindow(viewer);

		Border border = FindCodeBlockBorder(viewer);

		ScrollSurface viewerSurface = GetViewerSurface(viewer);
		ScrollSurface editorSurface = GetEditorSurface(FindCodeBlockEditor(viewer));

		viewerSurface.Offset = 0.0;
		editorSurface.Offset = 0.0;

		// A hit on the code block's border or padding must scroll the hosted editor, not the viewer.
		RaiseWheel(border, down: true);

		Assert.IsGreaterThan(0.0, editorSurface.Offset, "A wheel over the code block's border must scroll the code block.");
		Assert.AreEqual(0.0, viewerSurface.Offset, "The viewer must not take a wheel over the code block's border.");
	}

	[TestMethod]
	public void CreateContent_WheelOverCodeBlockBorder_AtBothScrollEnds_ReachesTheEnclosingHostScroller()
	{
		AssumeWheelScrollingEnabled();

		SharedControl viewer = CreateScrollableCodeBlockViewer();
		using IDisposable window = ShowInHostScroller(viewer, out ScrollSurface hostScroller);

		Border border = FindCodeBlockBorder(viewer);

		ScrollSurface viewerSurface = GetViewerSurface(viewer);
		ScrollSurface editorSurface = GetEditorSurface(FindCodeBlockEditor(viewer));

		Assert.IsGreaterThan(0.0, viewerSurface.ScrollableHeight, "The viewer must be scrollable for this test.");
		Assert.IsGreaterThan(0.0, hostScroller.ScrollableHeight, "The host scroller must be scrollable for this test.");

		editorSurface.Offset = editorSurface.ScrollableHeight;
		viewerSurface.Offset = viewerSurface.ScrollableHeight;
		hostScroller.Offset = 0.0;

		RaiseWheel(border, down: true);

		Assert.IsGreaterThan(0.0, hostScroller.Offset, "A wheel neither the code block nor the viewer can consume must reach the host scroller.");
	}

	[TestMethod]
	public void CreateCodeBlockEditor_WheelOverScrollableEditor_ScrollsTheEditorBeforeTheHostScroller()
	{
		AssumeWheelScrollingEnabled();

		EditorControl editor = CreateStandaloneCodeBlockEditor();
		using IDisposable window = ShowInHostScroller(editor, out ScrollSurface hostScroller);

		// A code-block editor used without the rendered viewer routes its own wheel: it scrolls itself while
		// it can and only hands the wheel to the enclosing host at its scrolling boundary.
		ScrollSurface editorSurface = GetEditorSurface(editor);

		Assert.IsGreaterThan(0.0, editorSurface.ScrollableHeight, "The standalone editor must be scrollable for this test.");
		Assert.IsGreaterThan(0.0, hostScroller.ScrollableHeight, "The host scroller must be scrollable for this test.");

		editorSurface.Offset = 0.0;
		hostScroller.Offset = 0.0;

		RaiseWheel(editor, down: true);

		Assert.IsGreaterThan(0.0, editorSurface.Offset, "The standalone code block must scroll itself before its host.");
		Assert.AreEqual(0.0, hostScroller.Offset, "The host must not steal the wheel from a scrollable standalone code block.");
	}

	[TestMethod]
	public void CreateCodeBlockEditor_WheelAtEditorScrollEnd_ReachesTheEnclosingHostScroller()
	{
		AssumeWheelScrollingEnabled();

		EditorControl editor = CreateStandaloneCodeBlockEditor();
		using IDisposable window = ShowInHostScroller(editor, out ScrollSurface hostScroller);

		ScrollSurface editorSurface = GetEditorSurface(editor);

		Assert.IsGreaterThan(0.0, editorSurface.ScrollableHeight, "The standalone editor must be scrollable for this test.");
		Assert.IsGreaterThan(0.0, hostScroller.ScrollableHeight, "The host scroller must be scrollable for this test.");

		editorSurface.Offset = editorSurface.ScrollableHeight;
		hostScroller.Offset = 0.0;

		RaiseWheel(editor, down: true);

		Assert.IsGreaterThan(0.0, hostScroller.Offset, "A wheel the standalone code block cannot consume must reach the enclosing host scroller.");
	}

	protected static string CreateScrollableBody() =>
		string.Join("\n\n", Enumerable.Repeat("Some tooltip paragraph text that wraps over several lines.", 8));

	protected static string CreateLongCode() => string.Join("\n", Enumerable.Range(1, 40).Select(index => $"local value{index} = {index}"));

	// A tooltip-shaped viewer whose body scrolls inside the theme's max height and whose fenced code block is clamped to a few visible lines,
	// so both the viewer and the code block scroll.
	protected static SharedControl CreateScrollableCodeBlockViewer(MarkdownRenderOptions? options = null) =>
		MarkdownRenderer.CreateContent($"{CreateScrollableBody()}\n\n```lua\n{CreateLongCode()}\n```", s_scrollableTheme, options);

	private static EditorControl CreateStandaloneCodeBlockEditor()
	{
		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor("lua", CreateLongCode());

		editor.Height = 120.0;
		return editor;
	}
}
