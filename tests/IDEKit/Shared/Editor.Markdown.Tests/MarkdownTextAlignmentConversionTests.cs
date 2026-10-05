#if AVALONIAEDIT
using Avalonia.Media;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;

#else
using System.Windows;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

#endif

/// <summary>Pins the mapping of a table column's neutral alignment onto the toolkit's own text alignment.</summary>
[TestClass]
public sealed class MarkdownTextAlignmentConversionTests
{
	[TestMethod]
	public void ToEngine_Left_ReturnsTheToolkitLeft() =>
		Assert.AreEqual(TextAlignment.Left, MarkdownTextAlignmentConversion.ToEngine(MarkdownColumnAlignment.Left));

	[TestMethod]
	public void ToEngine_Center_ReturnsTheToolkitCenter() =>
		Assert.AreEqual(TextAlignment.Center, MarkdownTextAlignmentConversion.ToEngine(MarkdownColumnAlignment.Center));

	[TestMethod]
	public void ToEngine_Right_ReturnsTheToolkitRight() =>
		Assert.AreEqual(TextAlignment.Right, MarkdownTextAlignmentConversion.ToEngine(MarkdownColumnAlignment.Right));

	[TestMethod]
	public void ToEngine_UnalignedColumn_ReturnsNull() => Assert.IsNull(MarkdownTextAlignmentConversion.ToEngine(null));
}
