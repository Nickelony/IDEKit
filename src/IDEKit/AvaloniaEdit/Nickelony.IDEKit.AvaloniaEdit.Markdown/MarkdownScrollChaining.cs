using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaEdit;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;

/// <summary>
/// Routes pointer-wheel input between a rendered content's scroll viewer and a nested scrollable
/// code block.
/// </summary>
/// <remarks>
/// <para>
/// The rendered content scrolls itself and hands a wheel it cannot consume to an enclosing host scroller, so
/// a wheel over a scrollable code block moves the block first and the content takes over once the block
/// reaches its scrolling boundary. This helper isolates that workaround so it can be removed if the toolkit
/// ever chains the wheel itself.
/// </para>
/// <para>
/// The nested element is recognized as a code-block editor (or the bordered element that hosts one), which is
/// the only nested scrollable the renderer produces; other scrollable content hosted in the content tree is
/// not chained. Nothing is attached when <see cref="MarkdownRenderOptions.AllowWheelChaining"/> is disabled.
/// The enclosing host scroller is found as the nearest ancestor scroll viewer, because the toolkit consumes
/// a wheel event at the content's own scroll viewer before it can bubble to the host.
/// </para>
/// </remarks>
internal static class MarkdownScrollChaining
{
	// The distance one wheel notch moves the content viewer. The toolkit exposes no lines-per-notch setting,
	// so a fixed step is supplied locally; the value matches the toolkit's own default wheel step.
	private const double ViewerWheelStepPixels = 48.0;

	// The number of text lines one wheel notch moves a code block's own scroll surface.
	private const int CodeBlockWheelScrollLines = 3;

	/// <include file="../../../../shared/docs/MarkdownScrollChaining.xml" path="doc/members/member[@name='MarkdownScrollChaining.AttachViewer']/*"/>
	internal static void AttachViewer(ScrollViewer viewer, MarkdownRenderOptions options)
	{
		if (!options.AllowWheelChaining)
			return;

		viewer.AddHandler(
			InputElement.PointerWheelChangedEvent,
			options.AllowScrolling ? OnViewerWheel : OnViewerWheelWithoutScrolling,
			RoutingStrategies.Tunnel);
	}

	/// <include file="../../../../shared/docs/MarkdownScrollChaining.xml" path="doc/members/member[@name='MarkdownScrollChaining.AttachCodeBlock']/*"/>
	internal static void AttachCodeBlock(TextEditor editor, MarkdownRenderOptions options)
	{
		if (!options.AllowWheelChaining || !options.AllowScrolling)
			return;

		editor.AddHandler(InputElement.PointerWheelChangedEvent, OnCodeBlockWheel, RoutingStrategies.Tunnel);
	}

	private static void OnViewerWheel(object? sender, PointerWheelEventArgs e)
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

	// The pass-through handler for a viewer whose scrolling is disabled: the viewer cannot consume the
	// wheel, so it is handed to an enclosing host scroller unchanged.
	private static void OnViewerWheelWithoutScrolling(object? sender, PointerWheelEventArgs e)
	{
		if (sender is not ScrollViewer viewer || e.Delta.Y == 0)
			return;

		e.Handled = ForwardToHostScroller(viewer, e.Delta.Y);
	}

	private static void OnCodeBlockWheel(object? sender, PointerWheelEventArgs e)
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
		ScrollViewer? scrollViewer = editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

		if (scrollViewer is null)
			return false;

		return TryScroll(scrollViewer, deltaY, CodeBlockWheelScrollLines * GetEditorLineHeight(editor));
	}

	private static double GetEditorLineHeight(TextEditor editor)
	{
		double lineHeight = editor.TextArea.TextView.DefaultLineHeight;

		return double.IsFinite(lineHeight) && lineHeight > 0.0 ? lineHeight : editor.FontSize;
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
	/// Hands an unconsumable wheel to the nearest ancestor scroll viewer, so the enclosing host scroller can
	/// take a wheel the rendered content could not.
	/// </summary>
	/// <param name="element">The element whose ancestors are searched.</param>
	/// <param name="deltaY">The wheel delta.</param>
	/// <returns>
	/// <see langword="true"/> when a host scroller consumed the wheel; otherwise, <see langword="false"/>.
	/// </returns>
	private static bool ForwardToHostScroller(Visual element, double deltaY)
	{
		ScrollViewer? hostScroller = element.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();

		return hostScroller is not null && TryScroll(hostScroller, deltaY, ViewerWheelStepPixels);
	}

	/// <summary>
	/// Finds the code-block editor under a wheel event's source: either the editor itself or the bordered
	/// element that hosts it, whose padding and border area the viewer routes to the hosted editor.
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
