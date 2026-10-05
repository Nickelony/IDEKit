using Nickelony.IDEKit.Markdown;
using Nickelony.IDEKit.Markdown.Model;
#if AVALONIAEDIT
using Avalonia.Media;
#else
using System.Windows;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;

#else

namespace Nickelony.IDEKit.AvalonEdit.Markdown;

#endif

/// <summary>Converts a table column's neutral alignment to the toolkit's own text alignment.</summary>
internal static class MarkdownTextAlignmentConversion
{
	/// <summary>Returns the toolkit text alignment that has the meaning of the neutral value.</summary>
	/// <param name="alignment">The neutral column alignment, or <see langword="null"/> for an unaligned column.</param>
	/// <returns>The toolkit text alignment, or <see langword="null"/> when the column is unaligned.</returns>
	internal static TextAlignment? ToEngine(MarkdownColumnAlignment? alignment)
	{
		return alignment switch
		{
			MarkdownColumnAlignment.Left => TextAlignment.Left,
			MarkdownColumnAlignment.Center => TextAlignment.Center,
			MarkdownColumnAlignment.Right => TextAlignment.Right,
			_ => null,
		};
	}
}
