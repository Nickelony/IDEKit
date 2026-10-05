using Nickelony.IDEKit.Markdown;
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

/// <summary>Converts the theme's neutral flow direction to the toolkit's own flow direction.</summary>
internal static class MarkdownFlowDirectionConversion
{
	/// <summary>Returns the toolkit flow direction that has the meaning of the neutral value.</summary>
	/// <param name="direction">The neutral flow direction.</param>
	/// <returns>The toolkit flow direction.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="direction"/> is not a defined value.</exception>
	internal static FlowDirection ToEngine(MarkdownFlowDirection direction)
	{
		return direction switch
		{
			MarkdownFlowDirection.LeftToRight => FlowDirection.LeftToRight,
			MarkdownFlowDirection.RightToLeft => FlowDirection.RightToLeft,
			_ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "The flow direction is not a defined value."),
		};
	}
}
