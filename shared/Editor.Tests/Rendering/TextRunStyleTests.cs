#if AVALONIAEDIT
using Avalonia.Media;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using FontStretches = Avalonia.Media.FontStretch;
using FontStyles = Avalonia.Media.FontStyle;
using FontWeights = Avalonia.Media.FontWeight;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using Nickelony.IDEKit.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Media;
using FontStretches = System.Windows.FontStretches;
using FontStyles = System.Windows.FontStyles;
using FontWeights = System.Windows.FontWeights;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
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
		Assert.IsTrue(new TextRunStyle(Brushes.Red, false, false, null).HasFormatting);
		Assert.IsTrue(new TextRunStyle(null, true, false, null).HasFormatting);
		Assert.IsTrue(new TextRunStyle(null, false, true, null).HasFormatting);
		Assert.IsTrue(new TextRunStyle(null, false, false, TextDecorations.Strikethrough).HasFormatting);
		Assert.IsFalse(new TextRunStyle(null, false, false, null).HasFormatting);
	}

	[TestMethod]
	public void HasFormatting_EmptyTextDecorationCollection_RequestsNoFormatting()
	{
		// A non-null but empty collection requests nothing, so it must not count as formatting.
		Assert.IsFalse(new TextRunStyle(null, false, false, new TextDecorationCollection()).HasFormatting);
	}

	[TestMethod]
	public void CreateTypeface_AppliesBoldAndItalicAndPreservesBaseStyle()
	{
		var baseTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

		Typeface bold = new TextRunStyle(null, IsBold: true, IsItalic: false, null).CreateTypeface(baseTypeface);
		Typeface italic = new TextRunStyle(null, IsBold: false, IsItalic: true, null).CreateTypeface(baseTypeface);

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
		// null base typeface cannot be represented. The WPF reference case (which asserts the null
		// guard) has no Avalonia counterpart, so it is reported inconclusive instead.
		Assert.Inconclusive(
			"Avalonia.Media.Typeface is a value type, so there is no null base-typeface argument to reject; "
			+ "the mirror's CreateTypeface takes the base typeface by value and carries no null check.");
#else
		Assert.ThrowsExactly<ArgumentNullException>(() => TextRunStyle.Empty.CreateTypeface(null!));
#endif
	}
}
