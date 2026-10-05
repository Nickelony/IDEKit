using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Nickelony.IDEKit.Markdown;
using EditorControl = ICSharpCode.AvalonEdit.TextEditor;

namespace Nickelony.IDEKit.AvalonEdit.Markdown;

/// <summary>Routes mouse-wheel events between a rendered document's scroll viewers and a nested scrollable code block.</summary>
/// <remarks>
/// <para>
/// WPF consumes a wheel event at the innermost scroll viewer and never chains the scroll to an enclosing viewer, so the outer preview handler decides
/// which viewer may consume the event: a nested scrollable element scrolls first, and the outer viewer takes over once the nested element reaches
/// its scrolling boundary.
/// </para>
/// <para>
/// The nested element is recognized as a code-block editor (or the bordered element that hosts one), which is the only nested
/// scrollable the renderer produces; other scrollable content hosted in the viewer is not chained. A nested editor consumes the wheel
/// before the viewer does, and the same routing covers a hit on the border that hosts an editor.
/// </para>
/// <para>
/// The rendered viewer marks every wheel event it receives as handled, even when nothing scrolls, and re-raises a wheel it cannot scroll
/// on its parent, where an enclosing host scroller can take it. A viewer whose scrolling is disabled re-raises every wheel event the same way.
/// Nothing is attached when <see cref="MarkdownRenderOptions.AllowWheelChaining"/> is disabled.
/// </para>
/// </remarks>
internal static class MarkdownScrollChaining
{
	// The elements a handler was already attached to, so a second attach (for example a re-render into an
	// element that was attached before) does not add the handler twice and scroll twice per wheel notch. The
	// table holds its keys weakly, matching each element's lifetime, and the UI thread is the only writer.
	private static readonly ConditionalWeakTable<DependencyObject, object> s_attachedElements = [];
	private static readonly object s_attachedMarker = new();

	// The distance of WPF's ScrollViewer.LineUp and LineDown, in device-independent pixels.
	private const double ViewerLineHeight = 16.0;

	/// <include file="../../../../shared/docs/MarkdownScrollChaining.xml" path="doc/members/member[@name='MarkdownScrollChaining.AttachViewer']/*"/>
	internal static void AttachViewer(UIElement viewer, MarkdownRenderOptions options)
	{
		if (!options.AllowWheelChaining || !TryMarkAttached(viewer))
			return;

		viewer.PreviewMouseWheel += options.AllowScrolling ? HandlePreviewMouseWheel : ForwardWheelFromViewer;
	}

	/// <include file="../../../../shared/docs/MarkdownScrollChaining.xml" path="doc/members/member[@name='MarkdownScrollChaining.AttachCodeBlock']/*"/>
	internal static void AttachCodeBlock(EditorControl editor, MarkdownRenderOptions options)
	{
		if (!options.AllowWheelChaining || !options.AllowScrolling || !TryMarkAttached(editor))
			return;

		editor.PreviewMouseWheel += HandlePreviewMouseWheel;
	}

	// Records the element as attached the first time and reports false on every later call.
	private static bool TryMarkAttached(DependencyObject element) => s_attachedElements.TryAdd(element, s_attachedMarker);

	private static void HandlePreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		if (e.Delta == 0 || sender is not DependencyObject senderObject)
			return;

		// The handler is attached to the outer viewer and to each code-block editor. A wheel over a
		// nested code block scrolls that block first, and the viewer takes over once the block reaches
		// its scrolling boundary. The nested editor is scrolled here - rather than left to its own
		// preview handler - because a hit on the border that hosts the editor does not pass through the
		// editor's event route, while this handler runs first on every route through the viewer.
		if (sender is FlowDocumentScrollViewer && FindNestedEditor(e.OriginalSource, senderObject) is EditorControl nestedEditor)
		{
			if (CanConsumeWheel(nestedEditor, e.Delta) && ScrollEditorByWheel(nestedEditor, e.Delta))
			{
				e.Handled = true;
				return;
			}
		}

		// A code block is scrolled through the text area's scroll surface, which is the surface that
		// actually drives the rendered text. A code-block editor hosted without the rendered viewer has
		// only this handler, so it scrolls itself and forwards an unconsumable wheel to its parent.
		if (sender is EditorControl editor)
		{
			if (CanConsumeWheel(editor, e.Delta) && ScrollEditorByWheel(editor, e.Delta))
			{
				e.Handled = true;
				return;
			}

			ForwardWheelToParent(senderObject, e);
			return;
		}

		// The rendered viewer is a FlowDocumentScrollViewer whose template places its own scroll
		// surface above the document content, so a depth-first search returns that surface (the first
		// ScrollViewer in template order) rather than a code-block viewer nested inside the document.
		// A plain-text fallback host is already a ScrollViewer and is used directly.
		ScrollViewer? scrollViewer = sender as ScrollViewer ?? FindVisualDescendant<ScrollViewer>(senderObject);

		if (scrollViewer is not null && CanConsumeWheel(scrollViewer, e.Delta) && ScrollByWheel(scrollViewer, e.Delta))
		{
			e.Handled = true;
			return;
		}

		// A wheel the viewer cannot scroll, including one the operating system disabled, is re-raised on its parent.
		ForwardWheelToParent(senderObject, e);
	}

	// The pass-through handler for a viewer whose scrolling is disabled: the viewer cannot scroll, so the
	// wheel is re-raised on its parent.
	private static void ForwardWheelFromViewer(object sender, MouseWheelEventArgs e)
	{
		if (e.Delta == 0 || sender is not DependencyObject senderObject)
			return;

		ForwardWheelToParent(senderObject, e);
	}

	/// <summary>
	/// Re-raises a wheel event on the sender's parent as a bubbling <see cref="UIElement.MouseWheelEvent"/> so an enclosing host scroller can consume
	/// a wheel the viewer did not.
	/// </summary>
	/// <param name="senderObject">The element the preview handler is attached to.</param>
	/// <param name="e">The wheel event to forward.</param>
	/// <remarks>
	/// Only the bubbling event is re-raised. Ancestors that listen on <see cref="UIElement.PreviewMouseWheelEvent"/> already receive the original
	/// tunneling event before the viewer's handler runs, because the viewer sits below them on the route, so they are not notified a second time.
	/// Re-raising a preview event would tunnel from the root through every ancestor again, so one wheel action would reach such a listener twice.
	/// The forwarded event is a fresh bubbling event raised on the parent, so a listener that inspects the event's source sees the parent
	/// rather than the element originally under the pointer; only the scroll intent is carried across.
	/// </remarks>
	private static void ForwardWheelToParent(DependencyObject senderObject, MouseWheelEventArgs e)
	{
		// Raising on the parent starts a fresh bubble past the viewer, whose own class handling would
		// otherwise consume the original event; the original event is marked handled so that handling
		// never runs for it.
		e.Handled = true;

		DependencyObject? parent = GetParent(senderObject);

		if (parent is not UIElement parentElement)
			return;

		var forwardedEventArgs = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent };

		parentElement.RaiseEvent(forwardedEventArgs);
	}

	/// <summary>
	/// Scrolls a code-block editor by the wheel step of the event: the operating system's lines-per-notch setting in text lines (three by default),
	/// or one page, for each notch, and a part of that for a part of a notch.
	/// </summary>
	/// <param name="editor">The editor to scroll.</param>
	/// <param name="delta">The wheel delta of the event.</param>
	/// <returns><see langword="true"/> when the editor scrolled; otherwise, <see langword="false"/>.</returns>
	private static bool ScrollEditorByWheel(EditorControl editor, int delta)
	{
		WheelStep step = GetWheelStep(delta);

		if (step.IsDisabled)
			return false;

		// The text area is the scroll surface that drives the rendered text; AvalonEdit's TextArea
		// implements IScrollInfo directly, so the editor's text area is used as the scroll info.
		IScrollInfo scrollInfo = editor.TextArea;
		double previousOffset = scrollInfo.VerticalOffset;

		// One notch moves as far as AvalonEdit's own wheel scrolling: lines-per-notch default line heights.
		double notchHeight = step.IsPageStep
			? Math.Max(1.0, scrollInfo.ViewportHeight)
			: step.LinesPerNotch * editor.TextArea.TextView.DefaultLineHeight;

		ScrollEditorBy(scrollInfo, step.Notches * notchHeight, delta);

		return scrollInfo.VerticalOffset != previousOffset;
	}

	/// <summary>Scrolls a code-block editor by a distance, clamped to its scrollable height.</summary>
	/// <param name="scrollInfo">The editor's scroll surface.</param>
	/// <param name="distance">The distance to scroll, in device-independent pixels.</param>
	/// <param name="delta">The wheel delta of the event, whose sign gives the direction.</param>
	private static void ScrollEditorBy(IScrollInfo scrollInfo, double distance, int delta)
	{
		double targetOffset = delta > 0 ? scrollInfo.VerticalOffset - distance : scrollInfo.VerticalOffset + distance;
		double clampedOffset = Math.Clamp(targetOffset, 0.0, GetScrollableHeight(scrollInfo));

		if (clampedOffset == scrollInfo.VerticalOffset)
			return;

		// AvalonEdit's text surface does not notify its scroll owner when an offset is set directly, so
		// the scroll bar has to be told about the change explicitly.
		scrollInfo.SetVerticalOffset(clampedOffset);
		scrollInfo.ScrollOwner?.InvalidateScrollInfo();
	}

	/// <summary>
	/// Scrolls the viewer by the wheel step of the event, following the operating system's lines-per-notch setting: each line moves the viewer
	/// by its fixed 16-DIP line unit (the step WPF's <c>ScrollViewer.LineUp</c> and <c>LineDown</c> commands use), one page is moved per notch
	/// when the operating system is configured to page-scroll with the wheel, and a part of a notch moves a part of that.
	/// </summary>
	/// <param name="scrollViewer">The viewer to scroll.</param>
	/// <param name="delta">The wheel delta of the event.</param>
	/// <returns>
	/// <see langword="true"/> when the wheel step was applied; <see langword="false"/> when the operating system disabled wheel scrolling,
	/// so the caller must forward the wheel instead of consuming it.
	/// </returns>
	/// <remarks>
	/// At the default setting the step matches WPF's native 48-DIP wheel step.
	/// </remarks>
	private static bool ScrollByWheel(ScrollViewer scrollViewer, int delta)
	{
		WheelStep step = GetWheelStep(delta);

		if (step.IsDisabled)
			return false;

		double notchHeight = step.IsPageStep ? scrollViewer.ViewportHeight : step.LinesPerNotch * ViewerLineHeight;
		double distance = step.Notches * notchHeight;

		scrollViewer.ScrollToVerticalOffset(delta > 0 ? scrollViewer.VerticalOffset - distance : scrollViewer.VerticalOffset + distance);

		return true;
	}

	/// <summary>Reads the operating system's wheel configuration and the wheel event's notch count.</summary>
	/// <param name="delta">The wheel delta of the event.</param>
	/// <returns>The wheel step to apply.</returns>
	private static WheelStep GetWheelStep(int delta) => WheelStep.FromDelta(delta, SystemParameters.WheelScrollLines);

	/// <summary>Describes one wheel event's native step.</summary>
	/// <param name="Notches">
	/// The number of wheel notches in the event, fractional for a precision touchpad, whose delta can be part of a notch.
	/// </param>
	/// <param name="LinesPerNotch">
	/// The operating system's lines-per-notch setting; zero disables wheel scrolling and a negative value means one page per notch.
	/// </param>
	internal readonly record struct WheelStep(double Notches, int LinesPerNotch)
	{
		/// <summary>Creates the step for a wheel event from its delta and the operating system's lines-per-notch setting.</summary>
		/// <param name="delta">The wheel delta of the event.</param>
		/// <param name="linesPerNotch">
		/// The operating system's lines-per-notch setting; zero disables wheel scrolling and a negative value means one page per notch.
		/// </param>
		/// <returns>The wheel step to apply.</returns>
		internal static WheelStep FromDelta(int delta, int linesPerNotch)
		{
			// The magnitude is computed on the widened value because Math.Abs(int.MinValue) overflows; a wheel
			// delta cannot be that large in practice, but the helper must not throw on any input.
			long magnitude = Math.Abs((long)delta);
			return new WheelStep((double)magnitude / Mouse.MouseWheelDeltaForOneLine, linesPerNotch);
		}

		/// <summary>Indicates whether the operating system disables wheel scrolling.</summary>
		/// <value><see langword="true"/> when wheel scrolling is disabled.</value>
		internal bool IsDisabled => LinesPerNotch == 0;

		/// <summary>Indicates whether the operating system is configured to page-scroll with the wheel.</summary>
		/// <value><see langword="true"/> when one notch scrolls one page.</value>
		internal bool IsPageStep => LinesPerNotch < 0;
	}

	/// <summary>
	/// Finds the code-block editor under a wheel event's original source: either the editor itself or the bordered element
	/// that hosts it, whose padding and border area the viewer routes to the hosted editor.
	/// </summary>
	/// <param name="originalSource">The event's original source.</param>
	/// <param name="sender">The element the preview handler is attached to.</param>
	/// <returns>The nested editor, or <see langword="null"/> when the event is not over a code block.</returns>
	private static EditorControl? FindNestedEditor(object? originalSource, DependencyObject sender)
	{
		var current = originalSource as DependencyObject;

		while (current is not null && !ReferenceEquals(current, sender))
		{
			if (current is EditorControl editor)
				return editor;

			if (current is Border { Child: EditorControl hostedEditor })
				return hostedEditor;

			current = GetParent(current);
		}

		return null;
	}

	private static bool CanConsumeWheel(ScrollViewer scrollViewer, int delta)
	{
		if (scrollViewer.ScrollableHeight <= 0.0)
			return false;

		return delta > 0 ? scrollViewer.VerticalOffset > 0.0 : scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight;
	}

	private static bool CanConsumeWheel(EditorControl editor, int delta)
	{
		IScrollInfo scrollInfo = editor.TextArea;

		double scrollableHeight = GetScrollableHeight(scrollInfo);

		if (scrollableHeight <= 0.0)
			return false;

		return delta > 0 ? scrollInfo.VerticalOffset > 0.0 : scrollInfo.VerticalOffset < scrollableHeight;
	}

	private static double GetScrollableHeight(IScrollInfo scrollInfo) => Math.Max(0.0, scrollInfo.ExtentHeight - scrollInfo.ViewportHeight);

	private static DependencyObject? GetParent(DependencyObject element) =>
		element is Visual or Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);

	private static T? FindVisualDescendant<T>(DependencyObject current)
		where T : DependencyObject
	{
		int count = VisualTreeHelper.GetChildrenCount(current);

		for (int i = 0; i < count; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(current, i);

			if (child is T match)
				return match;

			T? result = FindVisualDescendant<T>(child);

			if (result is not null)
				return result;
		}

		return null;
	}
}
