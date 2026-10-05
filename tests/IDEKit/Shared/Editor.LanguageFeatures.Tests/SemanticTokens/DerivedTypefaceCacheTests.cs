#if AVALONIAEDIT
using Avalonia.Media;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using FontStretches = Avalonia.Media.FontStretch;
using FontStyles = Avalonia.Media.FontStyle;
using FontWeights = Avalonia.Media.FontWeight;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows;
using System.Windows.Media;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using FontStretches = System.Windows.FontStretches;
using FontStyles = System.Windows.FontStyles;
using FontWeights = System.Windows.FontWeights;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>
/// Pins the key of the derived typeface cache: each input of the derivation decides on its own whether an entry is reused, so a typeface derived
/// for one base typeface or one set of style traits is never handed to another.
/// </summary>
[TestClass]
public sealed class DerivedTypefaceCacheTests
{
	private static readonly Typeface s_arial = CreateTypeface(
		family: "Arial",
		style: FontStyles.Normal,
		weight: FontWeights.Normal,
		stretch: FontStretches.Normal
	);

	[TestMethod]
	public void GetOrAdd_SameStyleAndBaseTypeface_ReusesTheEntry()
	{
		var cache = new DerivedTypefaceCache();

		Typeface first = cache.GetOrAdd(CreateStyle(isBold: true, isItalic: false), s_arial);
		Typeface second = cache.GetOrAdd(CreateStyle(isBold: true, isItalic: false), s_arial);

		AssertSameTypeface(first, second);
		Assert.AreEqual(1, cache.Count);
	}

	[TestMethod]
	public void GetOrAdd_BaseTypefaceEqualButNotTheSameObject_ReusesTheEntry()
	{
		var cache = new DerivedTypefaceCache();

		Typeface first = cache.GetOrAdd(CreateStyle(isBold: true, isItalic: false), s_arial);

		Typeface second = cache.GetOrAdd(
			CreateStyle(isBold: true, isItalic: false),
			CreateTypeface(family: "Arial", style: FontStyles.Normal, weight: FontWeights.Normal, stretch: FontStretches.Normal)
		);

		AssertSameTypeface(first, second);
		Assert.AreEqual(1, cache.Count);
	}

	[TestMethod]
	public void GetOrAdd_StylesDifferingOnlyByForeground_ShareTheEntry()
	{
		var cache = new DerivedTypefaceCache();

		Typeface first = cache.GetOrAdd(new TextRunStyle(Foreground: Brushes.Red, IsBold: true, IsItalic: false, TextDecorations: null), s_arial);
		Typeface second = cache.GetOrAdd(new TextRunStyle(Foreground: Brushes.Blue, IsBold: true, IsItalic: false, TextDecorations: null), s_arial);

		AssertSameTypeface(first, second);
		Assert.AreEqual(1, cache.Count);
	}

	[TestMethod]
	public void GetOrAdd_AnotherBoldSetting_DerivesItsOwnTypeface()
	{
		var cache = new DerivedTypefaceCache();

		Typeface withBold = cache.GetOrAdd(CreateStyle(isBold: true, isItalic: true), s_arial);
		Typeface withoutBold = cache.GetOrAdd(CreateStyle(isBold: false, isItalic: true), s_arial);

		Assert.AreEqual(2, cache.Count);
		Assert.AreEqual(FontWeights.Bold, withBold.Weight);
		Assert.AreEqual(FontWeights.Normal, withoutBold.Weight);
	}

	[TestMethod]
	public void GetOrAdd_AnotherItalicSetting_DerivesItsOwnTypeface()
	{
		var cache = new DerivedTypefaceCache();

		Typeface withItalic = cache.GetOrAdd(CreateStyle(isBold: true, isItalic: true), s_arial);
		Typeface withoutItalic = cache.GetOrAdd(CreateStyle(isBold: true, isItalic: false), s_arial);

		Assert.AreEqual(2, cache.Count);
		Assert.AreEqual(FontStyles.Italic, withItalic.Style);
		Assert.AreEqual(FontStyles.Normal, withoutItalic.Style);
	}

	[TestMethod]
	public void GetOrAdd_AnotherBaseFontFamily_DerivesFromThatFamily()
	{
		var cache = new DerivedTypefaceCache();
		Typeface courier = CreateTypeface(family: "Courier New", style: FontStyles.Normal, weight: FontWeights.Normal, stretch: FontStretches.Normal);

		_ = cache.GetOrAdd(CreateStyle(isBold: true, isItalic: false), s_arial);
		Typeface derived = cache.GetOrAdd(CreateStyle(isBold: true, isItalic: false), courier);

		Assert.AreEqual(2, cache.Count);
		Assert.AreEqual(courier.FontFamily, derived.FontFamily);
	}

	[TestMethod]
	public void GetOrAdd_AnotherBaseWeight_DerivesFromThatWeight()
	{
		var cache = new DerivedTypefaceCache();

		Typeface light = CreateTypeface(family: "Arial", style: FontStyles.Normal, weight: FontWeights.Light, stretch: FontStretches.Normal);
		Typeface semiBold = CreateTypeface(family: "Arial", style: FontStyles.Normal, weight: FontWeights.SemiBold, stretch: FontStretches.Normal);

		// An italic-only style keeps the weight of its base, so the derived typeface shows which base it came from.
		_ = cache.GetOrAdd(CreateStyle(isBold: false, isItalic: true), light);
		Typeface derived = cache.GetOrAdd(CreateStyle(isBold: false, isItalic: true), semiBold);

		Assert.AreEqual(2, cache.Count);
		Assert.AreEqual(FontWeights.SemiBold, derived.Weight);
	}

	[TestMethod]
	public void GetOrAdd_AnotherBaseStyle_DerivesFromThatStyle()
	{
		var cache = new DerivedTypefaceCache();

		Typeface normal = CreateTypeface(family: "Arial", style: FontStyles.Normal, weight: FontWeights.Normal, stretch: FontStretches.Normal);
		Typeface oblique = CreateTypeface(family: "Arial", style: FontStyles.Oblique, weight: FontWeights.Normal, stretch: FontStretches.Normal);

		// A bold-only style keeps the style of its base.
		_ = cache.GetOrAdd(CreateStyle(isBold: true, isItalic: false), normal);
		Typeface derived = cache.GetOrAdd(CreateStyle(isBold: true, isItalic: false), oblique);

		Assert.AreEqual(2, cache.Count);
		Assert.AreEqual(FontStyles.Oblique, derived.Style);
	}

	[TestMethod]
	public void GetOrAdd_AnotherBaseStretch_DerivesFromThatStretch()
	{
		var cache = new DerivedTypefaceCache();

		Typeface normal = CreateTypeface(family: "Arial", style: FontStyles.Normal, weight: FontWeights.Normal, stretch: FontStretches.Normal);
		Typeface condensed = CreateTypeface(family: "Arial", style: FontStyles.Normal, weight: FontWeights.Normal, stretch: FontStretches.Condensed);

		_ = cache.GetOrAdd(CreateStyle(isBold: true, isItalic: false), normal);
		Typeface derived = cache.GetOrAdd(CreateStyle(isBold: true, isItalic: false), condensed);

		Assert.AreEqual(2, cache.Count);
		Assert.AreEqual(FontStretches.Condensed, derived.Stretch);
	}

	[TestMethod]
	public void GetOrAdd_DistinctBaseTypefaces_StayWithinTheEntryLimit()
	{
		var cache = new DerivedTypefaceCache();

		for (int index = 0; index < DerivedTypefaceCache.MaxEntryCount * 4; index++)
		{
			_ = cache.GetOrAdd(
				CreateStyle(isBold: true, isItalic: false),
				CreateTypeface(family: $"Font{index}", style: FontStyles.Normal, weight: FontWeights.Normal, stretch: FontStretches.Normal)
			);
		}

		Assert.IsLessThanOrEqualTo(DerivedTypefaceCache.MaxEntryCount, cache.Count);
	}

	private static TextRunStyle CreateStyle(bool isBold, bool isItalic) => new(Foreground: null, isBold, isItalic, TextDecorations: null);

	private static Typeface CreateTypeface(string family, FontStyle style, FontWeight weight, FontStretch stretch) =>
		new(fontFamily: new FontFamily(family), style, weight, stretch);

	// A typeface is a class in one binding and a struct in the other, so sameness is reference identity for the class and equality for the struct.
	private static void AssertSameTypeface(Typeface expected, Typeface actual)
	{
#if AVALONIAEDIT
		Assert.AreEqual(expected, actual);
#else
		Assert.AreSame(expected, actual);
#endif
	}
}
