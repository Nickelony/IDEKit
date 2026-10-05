#if AVALONIAEDIT
using Avalonia.Media;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using FontStretches = Avalonia.Media.FontStretch;
using FontStyles = Avalonia.Media.FontStyle;
using FontWeights = Avalonia.Media.FontWeight;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using System.Windows;
using System.Windows.Media;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using FontStretches = System.Windows.FontStretches;
using FontStyles = System.Windows.FontStyles;
using FontWeights = System.Windows.FontWeights;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

[STATestClass]
public sealed class TextRunStyleTests
{
	[TestMethod]
	public void Empty_RequestsNoFormatting()
	{
		Assert.IsFalse(TextRunStyle.Empty.HasFormatting);
		Assert.IsNull(TextRunStyle.Empty.Foreground);
		Assert.IsFalse(TextRunStyle.Empty.IsBold);
		Assert.IsFalse(TextRunStyle.Empty.IsItalic);
		Assert.IsNull(TextRunStyle.Empty.TextDecorations);
	}

	[TestMethod]
	public void HasFormatting_ReflectsAnyRequestedFormatting()
	{
		Assert.IsTrue(new TextRunStyle(Foreground: Brushes.Red, IsBold: false, IsItalic: false, TextDecorations: null).HasFormatting);
		Assert.IsTrue(new TextRunStyle(Foreground: null, IsBold: true, IsItalic: false, TextDecorations: null).HasFormatting);
		Assert.IsTrue(new TextRunStyle(Foreground: null, IsBold: false, IsItalic: true, TextDecorations: null).HasFormatting);

		Assert.IsTrue(
			new TextRunStyle(Foreground: null, IsBold: false, IsItalic: false, TextDecorations: TextDecorations.Strikethrough).HasFormatting
		);

		Assert.IsFalse(new TextRunStyle(Foreground: null, IsBold: false, IsItalic: false, TextDecorations: null).HasFormatting);
	}

	[TestMethod]
	public void HasFormatting_EmptyTextDecorationCollection_RequestsNoFormatting()
	{
		// A non-null but empty collection requests nothing, so it must not count as formatting.
		Assert.IsFalse(new TextRunStyle(Foreground: null, IsBold: false, IsItalic: false, TextDecorations: []).HasFormatting);
	}

	[TestMethod]
	public void CreateTypeface_AppliesBoldAndItalicAndPreservesBaseStyle()
	{
		var baseTypeface = new Typeface(
			fontFamily: new FontFamily("Segoe UI"),
			style: FontStyles.Normal,
			weight: FontWeights.Normal,
			stretch: FontStretches.Normal
		);

		Typeface bold = new TextRunStyle(Foreground: null, IsBold: true, IsItalic: false, TextDecorations: null).CreateTypeface(baseTypeface);
		Typeface italic = new TextRunStyle(Foreground: null, IsBold: false, IsItalic: true, TextDecorations: null).CreateTypeface(baseTypeface);

		Assert.AreEqual(FontWeights.Bold, bold.Weight);
		Assert.AreEqual(FontStyles.Normal, bold.Style);
		Assert.AreEqual(FontWeights.Normal, italic.Weight);
		Assert.AreEqual(FontStyles.Italic, italic.Style);
	}

	[TestMethod]
	public void CreateTypeface_NullBaseTypeface_Throws()
	{
#if AVALONIAEDIT
		// Avalonia's Typeface is a value type, so the typeface overloads carry no null check and a
		// null base typeface cannot be represented; the case is reported inconclusive instead.
		Assert.Inconclusive(
			"Avalonia.Media.Typeface is a value type, so there is no null base-typeface argument to reject; "
				+ "CreateTypeface takes the base typeface by value and carries no null check."
		);
#else
		Assert.ThrowsExactly<ArgumentNullException>(() => TextRunStyle.Empty.CreateTypeface(null!));
#endif
	}
}
