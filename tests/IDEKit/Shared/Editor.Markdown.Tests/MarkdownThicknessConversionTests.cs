#if AVALONIAEDIT
using Avalonia;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;

#else
using System.Windows;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

#endif

/// <summary>Pins the mapping of the render plan's neutral thickness onto the toolkit's own thickness.</summary>
[TestClass]
public sealed class MarkdownThicknessConversionTests
{
	[TestMethod]
	public void ToEngine_MapsEveryEdgeToTheSameToolkitEdge()
	{
		Thickness thickness = MarkdownThicknessConversion.ToEngine(new MarkdownThickness(Left: 1.0, Top: 2.0, Right: 3.0, Bottom: 4.0));
		Assert.AreEqual(new Thickness(left: 1.0, top: 2.0, right: 3.0, bottom: 4.0), thickness);
	}
}
