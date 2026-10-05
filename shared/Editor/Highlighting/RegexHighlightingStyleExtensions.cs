using Nickelony.IDEKit.Core.Highlighting;
using Nickelony.IDEKit.Core.Media;
#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit.Highlighting;
using Nickelony.IDEKit.AvaloniaEdit.Media;
using FontStyles = Avalonia.Media.FontStyle;
using FontWeights = Avalonia.Media.FontWeight;
#else
using ICSharpCode.AvalonEdit.Highlighting;
using Nickelony.IDEKit.AvalonEdit.Media;
using System.Windows;
using System.Windows.Media;
using FontStyles = System.Windows.FontStyles;
using FontWeights = System.Windows.FontWeights;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Highlighting;

#else

namespace Nickelony.IDEKit.AvalonEdit.Highlighting;

#endif

/// <summary>Converts the neutral <see cref="RegexHighlightingStyle"/> model to the editor's <see cref="HighlightingColor"/>.</summary>
internal static class RegexHighlightingStyleExtensions
{
	/// <summary>Converts a style to a <see cref="HighlightingColor"/>.</summary>
	/// <param name="style">The style to convert.</param>
	/// <returns>A highlighting color with the configured foreground and font settings.</returns>
	/// <remarks>
	/// A style without a foreground leaves the foreground unset, so the editor's theme color is used.
	/// </remarks>
	internal static HighlightingColor ToHighlightingColor(this RegexHighlightingStyle style)
	{
		var highlightingColor = new HighlightingColor();

		if (style.Foreground is ArgbColor foreground)
			highlightingColor.Foreground = new SimpleHighlightingBrush(foreground.ToColor());

		if (style.IsBold)
			highlightingColor.FontWeight = FontWeights.Bold;

		if (style.IsItalic)
			highlightingColor.FontStyle = FontStyles.Italic;

		return highlightingColor;
	}
}
