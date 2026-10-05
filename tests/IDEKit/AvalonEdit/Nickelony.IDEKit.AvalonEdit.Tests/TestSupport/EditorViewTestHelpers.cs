using System.Windows.Threading;
using ICSharpCode.AvalonEdit;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

/// <summary>Provides the WPF half of the view geometry helpers that the shared editor tests call.</summary>
internal static class EditorViewTestHelpers
{
	/// <summary>The width, in pixels, of the strip that a pointer sweep covers over the default-font text of a test editor.</summary>
	internal const double SweepWidth = 80.0;

	/// <summary>Scrolls the editor vertically and horizontally away from the origin and lets the view settle.</summary>
	/// <param name="editor">The hosted editor with a long document.</param>
	internal static void ScrollAwayFromOrigin(TextEditor editor)
	{
		editor.ScrollToVerticalOffset(100.0);
		editor.ScrollToHorizontalOffset(5.0);

		WpfTestHost.PumpDispatcher(editor.Dispatcher, DispatcherPriority.Background);
	}
}
