using Nickelony.IDEKit.Markdown;
#if AVALONIAEDIT
using Avalonia;
#else
using System.Windows;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown;

#else

namespace Nickelony.IDEKit.AvalonEdit.Markdown;

#endif

/// <summary>Converts the render plan's neutral thickness to the toolkit's own thickness.</summary>
internal static class MarkdownThicknessConversion
{
	/// <summary>Returns the toolkit thickness whose four edges equal the neutral value.</summary>
	/// <param name="thickness">The neutral thickness.</param>
	/// <returns>The toolkit thickness.</returns>
	internal static Thickness ToEngine(MarkdownThickness thickness) =>
		new(left: thickness.Left, top: thickness.Top, right: thickness.Right, bottom: thickness.Bottom);
}
