#if AVALONIAEDIT
using Avalonia.Media;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using static Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests.TextMateRunStyleTestHelpers;
using Brush = Avalonia.Media.IBrush;
using FontStyles = Avalonia.Media.FontStyle;
using FontWeights = Avalonia.Media.FontWeight;
#else
using System.Windows.Media;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using static Nickelony.IDEKit.AvalonEdit.TextMate.Tests.TextMateRunStyleTestHelpers;
using FontStyles = System.Windows.FontStyles;
using FontWeights = System.Windows.FontWeights;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests;

#else

namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;

#endif

[TestClass]
public sealed class TextMateTypefaceCacheTests
{
	[TestMethod]
	public void GetOrAdd_SameStyleAndBaseTypeface_ReturnsTheCachedInstance()
	{
		var cache = new TextMateTypefaceCache();
		var style = new TextRunStyle(Foreground: CreateBrush(0x10), IsBold: true, IsItalic: false, TextDecorations: null);
		var baseTypeface = new Typeface("Arial");

		Typeface first = cache.GetOrAdd(style, baseTypeface);
		Typeface second = cache.GetOrAdd(style, baseTypeface);

		TextMateRenderingTestHelpers.AssertSameTypeface(first, second);
		Assert.AreEqual(1, cache.Count);
	}

	[TestMethod]
	public void GetOrAdd_DistinctKeys_StayWithinTheEntryLimit()
	{
		var cache = new TextMateTypefaceCache();

		// The cache key is the derived typeface's inputs (the bold and italic flags and the base
		// typeface), so distinct base typefaces produce distinct keys and exercise the eviction path.
		for (int i = 0; i < TextMateTypefaceCache.MaxCacheEntryCount * 4; i++)
			cache.GetOrAdd(new TextRunStyle(Foreground: null, IsBold: true, IsItalic: false, TextDecorations: null), new Typeface($"Font{i}"));

		Assert.IsLessThanOrEqualTo(
			TextMateTypefaceCache.MaxCacheEntryCount,
			cache.Count,
			$"Expected at most {TextMateTypefaceCache.MaxCacheEntryCount} entries, found {cache.Count}."
		);
	}

	[TestMethod]
	public void GetOrAdd_StylesDifferingOnlyByForeground_ShareTheEntry()
	{
		var cache = new TextMateTypefaceCache();
		var baseTypeface = new Typeface("Arial");

		// The foreground is not part of the key, so two styles that differ only in foreground share one
		// derived typeface instead of polluting the cache with an entry each.
		Typeface first = cache.GetOrAdd(
			new TextRunStyle(Foreground: CreateBrush(1), IsBold: true, IsItalic: false, TextDecorations: null),
			baseTypeface
		);

		Typeface second = cache.GetOrAdd(
			new TextRunStyle(Foreground: CreateBrush(2), IsBold: true, IsItalic: false, TextDecorations: null),
			baseTypeface
		);

		TextMateRenderingTestHelpers.AssertSameTypeface(first, second);
		Assert.AreEqual(1, cache.Count);
	}

	[TestMethod]
	public void GetOrAdd_DerivesTheStyleTraitsFromTheBaseTypeface()
	{
		var cache = new TextMateTypefaceCache();
		var style = new TextRunStyle(Foreground: null, IsBold: true, IsItalic: true, TextDecorations: null);

		Typeface typeface = cache.GetOrAdd(style, new Typeface("Arial"));

		Assert.AreEqual(FontWeights.Bold, typeface.Weight);
		Assert.AreEqual(FontStyles.Italic, typeface.Style);
	}

	private static Brush CreateBrush(byte value)
	{
		var brush = new SolidColorBrush(Color.FromRgb(value, value, value));
#if !AVALONIAEDIT
		brush.Freeze();
#endif
		return brush;
	}
}
