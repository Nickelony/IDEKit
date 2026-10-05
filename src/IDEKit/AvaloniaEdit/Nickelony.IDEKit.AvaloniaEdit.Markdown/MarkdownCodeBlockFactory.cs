using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using EditorControl = AvaloniaEdit.TextEditor;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;

internal static partial class MarkdownCodeBlockFactory
{
	// The height of one text line relative to the font size, used only when the editor's text view cannot
	// report a line height yet.
	private const double FallbackLineHeightRatio = 1.4;

	private static double GetFallbackLineHeight(EditorControl editor) => Math.Ceiling(Math.Max(1.0, editor.FontSize * FallbackLineHeightRatio));

	private static void SuppressPassiveSelection(EditorControl editor) =>
		editor.AddHandler(InputElement.PointerPressedEvent, HandlePointerPressed, RoutingStrategies.Tunnel);

	private static void HandlePointerPressed(object? sender, PointerPressedEventArgs e)
	{
		// A press on the block's own scroll bar (the editor template hosts one) must reach the bar, so an
		// overflowing block stays scrollable; only a press on the text surface is suppressed.
		if (IsWithinScrollBar(e.Source as Visual) || !e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
			return;

		e.Handled = true;
	}

	private static bool IsWithinScrollBar(Visual? source)
	{
		for (Visual? current = source; current is not null; current = current.GetVisualParent())
		{
			if (current is ScrollBar)
				return true;
		}

		return false;
	}
}
