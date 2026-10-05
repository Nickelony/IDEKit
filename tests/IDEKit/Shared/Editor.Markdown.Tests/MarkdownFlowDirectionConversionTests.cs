#if AVALONIAEDIT
using Avalonia.Media;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;

#else
using System.Windows;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

#endif

/// <summary>Pins the mapping of the theme's neutral flow direction onto the toolkit's own flow direction.</summary>
[TestClass]
public sealed class MarkdownFlowDirectionConversionTests
{
	[TestMethod]
	public void ToEngine_LeftToRight_ReturnsTheToolkitLeftToRight() =>
		Assert.AreEqual(FlowDirection.LeftToRight, MarkdownFlowDirectionConversion.ToEngine(MarkdownFlowDirection.LeftToRight));

	[TestMethod]
	public void ToEngine_RightToLeft_ReturnsTheToolkitRightToLeft() =>
		Assert.AreEqual(FlowDirection.RightToLeft, MarkdownFlowDirectionConversion.ToEngine(MarkdownFlowDirection.RightToLeft));

	[TestMethod]
	public void ToEngine_UndefinedValue_ThrowsArgumentOutOfRange() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => MarkdownFlowDirectionConversion.ToEngine((MarkdownFlowDirection)42));
}
