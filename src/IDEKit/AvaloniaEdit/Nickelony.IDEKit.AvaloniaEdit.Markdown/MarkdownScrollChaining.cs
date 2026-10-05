using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Nickelony.IDEKit.Core.Validation;
using Nickelony.IDEKit.Markdown;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;

/// <summary>Routes pointer-wheel input between a rendered content's scroll viewer and a nested scrollable code block.</summary>
/// <remarks>
/// <para>
/// The rendered content scrolls itself and hands a wheel it cannot consume to an enclosing host scroller, so a wheel over a scrollable code block
/// moves the block first and the content takes over once the block reaches its scrolling boundary.
/// </para>
/// <para>
/// The nested element is recognized as a code-block editor (or the bordered element that hosts one), which is the only nested
/// scrollable the renderer produces; other scrollable content hosted in the content tree is not chained. Nothing is attached
/// when <see cref="MarkdownRenderOptions.AllowWheelChaining"/> is disabled. The enclosing host scroller is found by walking ancestor scroll viewers
/// until one can actually scroll, because the toolkit consumes a wheel event at the content's own scroll viewer before it can bubble to the host,
/// and a nearer ancestor that is already at its scrolling boundary must not swallow the wheel.
/// </para>
/// </remarks>
internal static class MarkdownScrollChaining
{
	// The distance one wheel notch moves the content viewer. The toolkit derives its own wheel step from a
	// lines-per-notch setting that it does not expose, so the step is supplied locally as a fixed pixel value
	// that approximates a small run of text lines and is not read from the toolkit.
	private const double ViewerWheelStepPixels = 48.0;

	// The number of text lines one wheel notch moves a code block's own scroll surface.
	private const int CodeBlockWheelScrollLines = 3;

	// The elements a handler was already attached to, so a second attach (for example a re-render into an
	// element that was attached before) does not add the handler twice and scroll twice per wheel notch. The
	// table holds its keys weakly, matching each element's lifetime, and the UI thread is the only writer.
	private static readonly ConditionalWeakTable<AvaloniaObject, object> s_attachedElements = [];
	private static readonly object s_attachedMarker = new();

	/// <include file="../../../../shared/docs/MarkdownScrollChaining.xml" path="doc/members/member[@name='MarkdownScrollChaining.AttachViewer']/*"/>
	internal static void AttachViewer(ScrollViewer viewer, MarkdownRenderOptions options)
	{
		if (!options.AllowWheelChaining || !TryMarkAttached(viewer))
			return;

		viewer.AddHandler(
			InputElement.PointerWheelChangedEvent,
			options.AllowScrolling ? HandleViewerWheel : HandleViewerWheelWithoutScrolling,
			RoutingStrategies.Tunnel
		);
	}

	/// <include file="../../../../shared/docs/MarkdownScrollChaining.xml" path="doc/members/member[@name='MarkdownScrollChaining.AttachCodeBlock']/*"/>
	internal static void AttachCodeBlock(TextEditor editor, MarkdownRenderOptions options)
	{
		if (!options.AllowWheelChaining || !options.AllowScrolling || !TryMarkAttached(editor))
			return;

		editor.AddHandler(InputElement.PointerWheelChangedEvent, HandleCodeBlockWheel, RoutingStrategies.Tunnel);
	}

	// Records the element as attached the first time and reports false on every later call.
	private static bool TryMarkAttached(AvaloniaObject element) => s_attachedElements.TryAdd(element, s_attachedMarker);

	private static void HandleViewerWheel(object? sender, PointerWheelEventArgs e)
	{
		if (sender is not ScrollViewer viewer || e.Delta.Y == 0)
			return;

		// A wheel over a nested code block scrolls that block first, and the viewer takes over once the block
		// reaches its scrolling boundary. The nested editor is scrolled here - rather than left to its own
		// handler - because a hit on the border that hosts the editor does not pass through the editor's route.
		if (FindNestedEditor(e.Source as Visual, viewer) is TextEditor nestedEditor && TryScrollEditor(nestedEditor, e.Delta.Y))
		{
			e.Handled = true;
			return;
		}

		if (TryScroll(viewer, e.Delta.Y, ViewerWheelStepPixels))
		{
			e.Handled = true;
			return;
		}

		e.Handled = ForwardToHostScroller(viewer, e.Delta.Y);
	}

	// The pass-through handler for a viewer whose scrolling is disabled: the viewer cannot scroll, so the
	// wheel goes to an enclosing host scroller.
	private static void HandleViewerWheelWithoutScrolling(object? sender, PointerWheelEventArgs e)
	{
		if (sender is not ScrollViewer viewer || e.Delta.Y == 0)
			return;

		e.Handled = ForwardToHostScroller(viewer, e.Delta.Y);
	}

	private static void HandleCodeBlockWheel(object? sender, PointerWheelEventArgs e)
	{
		if (sender is not TextEditor editor || e.Delta.Y == 0)
			return;

		if (TryScrollEditor(editor, e.Delta.Y))
		{
			e.Handled = true;
			return;
		}

		e.Handled = ForwardToHostScroller(editor, e.Delta.Y);
	}

	private static bool TryScrollEditor(TextEditor editor, double deltaY)
	{
		ScrollViewer? scrollViewer = FindEditorScrollViewer(editor);

		if (scrollViewer is null)
			return false;

		return TryScroll(scrollViewer, deltaY, CodeBlockWheelScrollLines * GetEditorLineHeight(editor));
	}

	// The editor's own scroll surface is the scroll viewer its template wraps the text area in, so it is the
	// nearest ancestor scroll viewer of the text view; walking up finds it without materializing the visual
	// subtree (the editor's visual tree contains no other scroll viewer).
	private static ScrollViewer? FindEditorScrollViewer(TextEditor editor)
	{
		Visual? current = editor.TextArea.TextView;

		while (current is not null && !ReferenceEquals(current, editor))
		{
			if (current is ScrollViewer scrollViewer)
				return scrollViewer;

			current = current.GetVisualParent();
		}

		return null;
	}

	private static double GetEditorLineHeight(TextEditor editor)
	{
		double lineHeight = editor.TextArea.TextView.DefaultLineHeight;
		return NumericValidation.IsFinitePositive(lineHeight) ? lineHeight : editor.FontSize;
	}

	private static bool TryScroll(ScrollViewer scrollViewer, double deltaY, double stepPixels)
	{
		double scrollableHeight = Math.Max(0.0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);

		if (scrollableHeight <= 0.0)
			return false;

		double currentOffset = scrollViewer.Offset.Y;
		double targetOffset = Math.Clamp(currentOffset - (deltaY * stepPixels), 0.0, scrollableHeight);

		if (targetOffset == currentOffset)
			return false;

		scrollViewer.Offset = new Vector(scrollViewer.Offset.X, targetOffset);
		return true;
	}

	/// <summary>
	/// Scrolls the innermost ancestor scroll viewer that can take the wheel, so a wheel the rendered content cannot consume still reaches the host
	/// even past a nearer ancestor that cannot scroll.
	/// </summary>
	/// <param name="element">The element whose ancestors are searched.</param>
	/// <param name="deltaY">The wheel delta.</param>
	/// <returns><see langword="true"/> when an ancestor scroller consumed the wheel; otherwise, <see langword="false"/>.</returns>
	private static bool ForwardToHostScroller(Visual element, double deltaY)
	{
		Visual? ancestor = element.GetVisualParent();

		while (ancestor is not null)
		{
			// Only a pixel-scrolling ancestor is driven by an offset. A logically scrolling ancestor (for
			// example an item host's presenter) interprets its offset in item units, so it is skipped and the
			// wheel is left to the toolkit's own scroll chaining instead of being moved by a pixel step.
			if (
				ancestor is ScrollViewer hostScroller
				&& !IsLogicallyScrolling(hostScroller)
				&& TryScroll(hostScroller, deltaY, ViewerWheelStepPixels)
			)
			{
				return true;
			}

			ancestor = ancestor.GetVisualParent();
		}

		return false;
	}

	// A scroller is logical when its content is an enabled logical scroller, whose offsets are item units
	// rather than pixels.
	private static bool IsLogicallyScrolling(ScrollViewer scrollViewer) =>
		scrollViewer.Content is ILogicalScrollable { IsLogicalScrollEnabled: true };

	/// <summary>
	/// Finds the code-block editor under a wheel event's source: either the editor itself or the bordered element
	/// that hosts it, whose padding and border area the viewer routes to the hosted editor.
	/// </summary>
	/// <param name="source">The event's source visual.</param>
	/// <param name="stopAt">The viewer the search must not walk past.</param>
	/// <returns>The nested editor, or <see langword="null"/> when the event is not over a code block.</returns>
	private static TextEditor? FindNestedEditor(Visual? source, Visual stopAt)
	{
		Visual? current = source;

		while (current is not null && !ReferenceEquals(current, stopAt))
		{
			if (current is TextEditor editor)
				return editor;

			if (current is Border { Child: TextEditor hostedEditor })
				return hostedEditor;

			current = current.GetVisualParent();
		}

		return null;
	}
}
