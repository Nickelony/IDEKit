#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit.Highlighting;
#else
using ICSharpCode.AvalonEdit.Highlighting;
using System.Windows;
using System.Windows.Media;
#endif
using Nickelony.IDEKit.Core.Highlighting;
using Nickelony.IDEKit.Infrastructure;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Highlighting;
#else
namespace Nickelony.IDEKit.AvalonEdit.Highlighting;
#endif

/// <summary>
/// Converts the neutral <see cref="RegexHighlightingStyle"/> model to the editor's
/// <see cref="HighlightingColor"/>.
/// </summary>
public static class RegexHighlightingStyleExtensions
{
	/// <summary>
	/// Converts a style to a <see cref="HighlightingColor"/>.
	/// A missing or blank color leaves the foreground unset so the editor's theme color is used.
	/// A color value that cannot be parsed uses <paramref name="fallbackColor"/> when one is supplied and
	/// otherwise leaves the foreground unset as well.
	/// </summary>
	/// <param name="style">The style to convert.</param>
	/// <param name="fallbackColor">
	/// The foreground color used when the configured color cannot be parsed, or <see langword="null"/> to
	/// leave the foreground unset so the editor's theme color is used.
	/// </param>
	/// <returns>A highlighting color with the resolved foreground color and configured font settings.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="style"/> is <see langword="null"/>.</exception>
	public static HighlightingColor ToHighlightingColor(this RegexHighlightingStyle style, Color? fallbackColor)
	{
		ArgumentNullException.ThrowIfNull(style);

		var highlightingColor = new HighlightingColor();

		if (BrushHelpers.TryParseColor(style.ColorValue, out Color color))
			highlightingColor.Foreground = new SimpleHighlightingBrush(color);
		else if (!string.IsNullOrWhiteSpace(style.ColorValue) && fallbackColor is Color fallback)
			highlightingColor.Foreground = new SimpleHighlightingBrush(fallback);

		if (style.IsBold)
#if AVALONIAEDIT
			highlightingColor.FontWeight = FontWeight.Bold;
#else
			highlightingColor.FontWeight = FontWeights.Bold;
#endif

		if (style.IsItalic)
#if AVALONIAEDIT
			highlightingColor.FontStyle = FontStyle.Italic;
#else
			highlightingColor.FontStyle = FontStyles.Italic;
#endif

		return highlightingColor;
	}
}
