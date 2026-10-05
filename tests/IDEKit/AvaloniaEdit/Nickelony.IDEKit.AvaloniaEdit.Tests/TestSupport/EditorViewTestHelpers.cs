using Avalonia.Threading;
using AvaloniaEdit;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

/// <summary>Provides the Avalonia half of the view geometry helpers that the shared editor tests call.</summary>
internal static class EditorViewTestHelpers
{
	/// <summary>The width, in pixels, of the strip that a pointer sweep covers over the default-font text of a test editor.</summary>
	/// <remarks>
	/// The width is wider than the WPF one because the AvaloniaEdit default font is wider.
	/// </remarks>
	internal const double SweepWidth = 200.0;

	/// <summary>Scrolls the editor vertically away from the origin and lets the view settle.</summary>
	/// <param name="editor">The hosted editor with a long document.</param>
	/// <remarks>
	/// The editor's <c>ScrollToVerticalOffset</c> and <c>ScrollToHorizontalOffset</c> are no-ops (the engine leaves their <c>ScrollViewer</c>
	/// calls commented out), so the vertical scroll is established through <c>ScrollToLine</c>, the public primitive that moves the view in Avalonia.
	/// Avalonia only materializes the text view's scroll offset after a layout pass.
	/// </remarks>
	internal static void ScrollAwayFromOrigin(TextEditor editor)
	{
		editor.ScrollToLine(150);

		AvaloniaTestHost.PumpDispatcher(editor.Dispatcher ?? Dispatcher.UIThread, DispatcherPriority.Background);

		editor.UpdateLayout();
	}
}
