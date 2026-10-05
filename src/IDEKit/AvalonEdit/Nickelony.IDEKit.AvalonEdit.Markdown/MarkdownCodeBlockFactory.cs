using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using EditorControl = ICSharpCode.AvalonEdit.TextEditor;

namespace Nickelony.IDEKit.AvalonEdit.Markdown;

internal static partial class MarkdownCodeBlockFactory
{
	private static double GetFallbackLineHeight(EditorControl editor)
	{
		var typeface = new Typeface(fontFamily: editor.FontFamily, style: editor.FontStyle, weight: editor.FontWeight, stretch: editor.FontStretch);

		// Measurement follows the UI language and the live DPI so the fallback matches the metrics
		// the engine uses to render the editor.
		var formattedText = new FormattedText(
			textToFormat: "Ag",
			culture: CultureInfo.CurrentUICulture,
			flowDirection: editor.FlowDirection,
			typeface,
			emSize: editor.FontSize,
			foreground: Brushes.Transparent,
			pixelsPerDip: VisualTreeHelper.GetDpi(editor).PixelsPerDip
		);

		return Math.Ceiling(Math.Max(1.0, formattedText.Height));
	}

	private static void SuppressPassiveSelection(EditorControl editor) => editor.PreviewMouseLeftButtonDown += HandleMouseLeftButtonDown;

	private static void HandleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		// A press on the block's own scroll bar (the editor template hosts one) must reach the bar, so an
		// overflowing block stays scrollable; only a press on the text surface is suppressed.
		if (IsWithinScrollBar(e.OriginalSource as DependencyObject))
			return;

		e.Handled = true;
	}

	private static bool IsWithinScrollBar(DependencyObject? source)
	{
		for (DependencyObject? current = source; current is not null; current = GetVisualOrLogicalParent(current))
		{
			if (current is ScrollBar)
				return true;
		}

		return false;
	}

	private static DependencyObject? GetVisualOrLogicalParent(DependencyObject element) =>
		element is Visual or Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
}
