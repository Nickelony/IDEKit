#if AVALONIAEDIT
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Nickelony.IDEKit.AvaloniaEdit.Media;
using Nickelony.IDEKit.Core.Media;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using System.Windows.Media;
using Nickelony.IDEKit.AvalonEdit.Media;
using Nickelony.IDEKit.Core.Media;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

/// <summary>Pins the conversion of the engine-free color to the engine's color and to a shareable brush.</summary>
[TestClass]
public sealed class ArgbColorExtensionsTests
{
	[TestMethod]
	public void ToColor_CopiesEveryChannel()
	{
		var color = new ArgbColor(A: 0x80, R: 0x12, G: 0x34, B: 0x56).ToColor();

		Assert.AreEqual(0x80, color.A);
		Assert.AreEqual(0x12, color.R);
		Assert.AreEqual(0x34, color.G);
		Assert.AreEqual(0x56, color.B);
	}

	[TestMethod]
	public void ToBrush_ReturnsAShareableBrushOfThatColor()
	{
		var argb = new ArgbColor(A: 0xFF, R: 0x0A, G: 0x0B, B: 0x0C);

#if AVALONIAEDIT
		ImmutableSolidColorBrush brush = argb.ToBrush();
#else
		SolidColorBrush brush = argb.ToBrush();

		Assert.IsTrue(brush.IsFrozen, "A frozen brush can be shared between elements.");
#endif

		Assert.AreEqual(argb.ToColor(), brush.Color);
	}
}
