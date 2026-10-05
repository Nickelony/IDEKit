#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit.Highlighting;
using Nickelony.IDEKit.AvaloniaEdit.Highlighting;
using Nickelony.IDEKit.Core.Highlighting;
using Nickelony.IDEKit.Core.Media;
using FontStyles = Avalonia.Media.FontStyle;
using FontWeights = Avalonia.Media.FontWeight;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;
using Nickelony.IDEKit.AvalonEdit.Highlighting;
using Nickelony.IDEKit.Core.Highlighting;
using Nickelony.IDEKit.Core.Media;
using FontStyles = System.Windows.FontStyles;
using FontWeights = System.Windows.FontWeights;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

[TestClass]
public sealed class RegexHighlightingStyleTests
{
	[TestMethod]
	public void ToHighlightingColor_BoldOnly_SetsWeightWithoutStyle()
	{
		var color = new RegexHighlightingStyle(ArgbColor.FromRgb(0xFF, 0x00, 0x00), IsBold: true).ToHighlightingColor();

		Assert.AreEqual(Colors.Red, color.Foreground!.GetColor(null!));
		Assert.AreEqual(FontWeights.Bold, color.FontWeight);
		Assert.IsNull(color.FontStyle);
	}

	[TestMethod]
	public void ToHighlightingColor_ItalicOnly_SetsStyleWithoutWeight()
	{
		var color = new RegexHighlightingStyle(ArgbColor.FromRgb(0xFF, 0x00, 0x00), IsItalic: true).ToHighlightingColor();

		Assert.AreEqual(Colors.Red, color.Foreground!.GetColor(null!));
		Assert.IsNull(color.FontWeight);
		Assert.AreEqual(FontStyles.Italic, color.FontStyle);
	}

	[TestMethod]
	public void ToHighlightingColor_ArgbAlpha_IsPreserved()
	{
		var color = new RegexHighlightingStyle(new ArgbColor(A: 0x80, R: 0xFF, G: 0x00, B: 0x00)).ToHighlightingColor();

		// The eight-digit form carries an alpha channel; it must survive the conversion instead of
		// being treated as an opaque color.
		Assert.AreEqual(Color.FromArgb(a: 0x80, r: 0xFF, g: 0x00, b: 0x00), color.Foreground!.GetColor(null!));
	}
}
