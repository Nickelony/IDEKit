#if AVALONIAEDIT
using Avalonia.Media;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.TextMate.Highlighting;
using static Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests.TextMateThemeTestHelpers;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
#else
using Nickelony.IDEKit.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
using System.Windows;
using System.Windows.Media;
using static Nickelony.IDEKit.AvalonEdit.TextMate.Tests.TextMateThemeTestHelpers;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests;
#else
namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;
#endif

[TestClass]
public sealed class TextMateThemeStyleResolverStyleTests
{
	[TestMethod]
	public void Resolve_UnknownFontStyleTrait_LogsWarningAndKeepsKnownTraits()
	{
		var logger = new CapturingLogger();
		var resolver = new TextMateThemeStyleResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "bold sparkle" }),
			logger);

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsTrue(style.IsBold);
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("sparkle", StringComparison.Ordinal)));
	}

	[TestMethod]
	[DataRow("", DisplayName = "EmptyString")]
	[DataRow("   ", DisplayName = "Whitespace")]
	[DataRow("none", DisplayName = "NoneKeyword")]
	public void Resolve_PresentFontStyle_ResetsInheritedTraits(string resetValue)
	{
		// A present font-style value is an explicit reset, including the empty string that VS Code and
		// TextMate theme data use to clear inherited traits. "none" is accepted as a readable spelling.
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "bold italic underline strikethrough" },
			new TextMateTokenThemeRule { Scope = "keyword.control", FontStyle = resetValue }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.IsBold, $"'{resetValue}' must reset bold.");
		Assert.IsFalse(style.IsItalic, $"'{resetValue}' must reset italic.");
		Assert.IsNull(style.TextDecorations, $"'{resetValue}' must clear the underline and strikethrough decorations.");
	}

	[TestMethod]
	public void Resolve_AbsentFontStyle_KeepsInheritedTraits()
	{
		// An absent font-style value (null) leaves inherited traits alone, so a more specific rule that
		// only sets a foreground keeps the bold trait of the broader rule.
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "bold" },
			new TextMateTokenThemeRule { Scope = "keyword.control", Foreground = "#123456" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsTrue(style.IsBold);
		Assert.AreEqual("#FF123456", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_EightDigitHexColor_IsParsedAsRrggbbaa()
	{
		// TextMate theme data spells eight-digit colors as #RRGGBBAA; the resolver normalizes the alpha
		// component to the platform order instead of letting the platform parser read it as #AARRGGBB.
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#E7C0C0FF" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual(Color.FromArgb(0xFF, 0xE7, 0xC0, 0xC0), GetBrushColor(style.Foreground!));
	}

	[TestMethod]
	public void Resolve_EightDigitHexColorWithAlpha_PreservesAlpha()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#FF000080" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual(Color.FromArgb(0x80, 0xFF, 0x00, 0x00), GetBrushColor(style.Foreground!));
	}

	[TestMethod]
	public void Resolve_CacheOverflow_KeepsLaterResolutionsCorrect()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#123456" }));

		TextRunStyle cachedStyle = resolver.Resolve(["source.lua", "keyword"]);

		// Fill the bounded cache past its entry limit so the trim path runs. The trim itself
		// is not observable through the returned values, because equal scope sequences always resolve to
		// equal styles; the contract after an overflow is that both the overflow scopes and the earlier
		// scopes still resolve correctly.
		for (int i = 0; i <= TextMateThemeStyleResolver.MaxCacheEntryCount; i++)
			resolver.Resolve(["source.lua", $"scope{i}", "keyword"]);

		TextRunStyle refreshedStyle = resolver.Resolve(["source.lua", "keyword"]);

		Assert.AreEqual("#FF123456", GetForegroundColor(refreshedStyle));
		Assert.AreEqual(cachedStyle, refreshedStyle);
	}

	[TestMethod]
	public void Resolve_FromMultipleThreads_ReturnsConsistentStyles()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#123456" },
			new TextMateTokenThemeRule { Scope = "string", Foreground = "#654321" }));

		string[] expectedColors = ["#FF123456", "#FF654321"];

		Parallel.For(0, 1000, index =>
		{
			int caseIndex = index % 2;
			TextRunStyle style = caseIndex == 0
				? resolver.Resolve(["source.lua", "keyword.control.lua"])
				: resolver.Resolve(["source.lua", "string.quoted.lua"]);

			Assert.AreEqual(expectedColors[caseIndex], GetForegroundColor(style));
		});
	}

	[TestMethod]
	public void Resolve_FourDigitHexColor_IsParsedAsRgba()
	{
		// TextMate theme data spells four-digit colors as #RGBA; the resolver moves the alpha digit to
		// the platform order and doubles every digit.
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#F00A" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual(Color.FromArgb(0xAA, 0xFF, 0x00, 0x00), GetBrushColor(style.Foreground!));
	}

	[TestMethod]
	public void Resolve_EightDigitHexColor_IsAlwaysReadAsTextMateOrder()
	{
		// An eight-digit value is read as #RRGGBBAA, never as the platform #AARRGGBB order, so a value whose
		// alpha byte sits first is reinterpreted rather than treated as the platform spelling.
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#800000FF" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual(Color.FromArgb(0xFF, 0x80, 0x00, 0x00), GetBrushColor(style.Foreground!));
	}

	[TestMethod]
	public void Resolve_FourDigitHexColor_IsAlwaysReadAsTextMateOrder()
	{
		// A four-digit value is read as #RGBA, never as the platform #ARGB order.
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#8F00" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual(Color.FromArgb(0x00, 0x88, 0xFF, 0x00), GetBrushColor(style.Foreground!));
	}

	[TestMethod]
	public void Resolve_FontStyleFlags_ApplyBoldItalicUnderlineAndStrikethrough()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule
			{
				Scope = "keyword",
				FontStyle = "bold italic underline strikethrough"
			}));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsTrue(style.IsBold);
		Assert.IsTrue(style.IsItalic);
		Assert.IsNotNull(style.TextDecorations);
		Assert.AreEqual(2, style.TextDecorations.Count);
	}

	[TestMethod]
	public void Resolve_UnderlineOnly_ProducesSingleUnderlineDecoration()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule
			{
				Scope = "keyword",
				FontStyle = "underline"
			}));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsNotNull(style.TextDecorations);
		Assert.AreEqual(1, style.TextDecorations.Count);
		Assert.AreEqual(TextDecorationLocation.Underline, style.TextDecorations[0].Location);
	}

	[TestMethod]
	public void Resolve_StrikethroughOnly_ProducesSingleStrikethroughDecoration()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule
			{
				Scope = "keyword",
				FontStyle = "strikethrough"
			}));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsNotNull(style.TextDecorations);
		Assert.AreEqual(1, style.TextDecorations.Count);
		Assert.AreEqual(TextDecorationLocation.Strikethrough, style.TextDecorations[0].Location);
	}

	[TestMethod]
	public void Resolve_NoneFontStyle_ProducesNoFormatting()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule
			{
				Scope = "keyword",
				FontStyle = "none"
			}));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
		Assert.IsFalse(style.IsBold);
		Assert.IsFalse(style.IsItalic);
		Assert.IsNull(style.TextDecorations);
	}

	[TestMethod]
	public void Resolve_InvalidForegroundColor_LogsWarningAndIgnoresForeground()
	{
		var logger = new CapturingLogger();
		var resolver = new TextMateThemeStyleResolver(
			CreateTheme(new TextMateTokenThemeRule
			{
				Scope = "keyword",
				Foreground = "not-a-color"
			}),
			logger);

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsNull(style.Foreground);
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("Invalid foreground color", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void Resolve_EmptyScopes_ReturnsSharedEmptyStyle()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme());

		Assert.AreEqual(TextRunStyle.Empty, resolver.Resolve(null));
		Assert.AreEqual(TextRunStyle.Empty, resolver.Resolve([]));
	}

	[TestMethod]
	public void Resolve_RepeatedScopes_AreServedFromTheCache()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule
			{
				Scope = "keyword",
				Foreground = "#123456"
			}));

		TextRunStyle first = resolver.Resolve(["source.lua", "keyword.control.lua"]);
		int missesAfterFirstResolution = resolver.CacheMissCount;
		TextRunStyle second = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		// The first resolution misses the cache and stores the style; the repeated resolution is served
		// from the cache without recomputing it.
		Assert.AreEqual(1, missesAfterFirstResolution);
		Assert.AreEqual(missesAfterFirstResolution, resolver.CacheMissCount);
		Assert.AreEqual(first, second);
	}

	[TestMethod]
	public void Resolve_CallerMutationOfTheScopeList_DoesNotCorruptTheCache()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#123456" },
			new TextMateTokenThemeRule { Scope = "string", Foreground = "#654321" }));

		var scopes = new List<string> { "source.lua", "keyword.control.lua" };
		TextRunStyle keywordStyle = resolver.Resolve(scopes);

		// Stored keys snapshot the sequence, so reusing and mutating the caller's list resolves the new
		// scopes instead of returning the style cached for the previous sequence.
		scopes[1] = "string.quoted.lua";
		TextRunStyle stringStyle = resolver.Resolve(scopes);

		Assert.AreEqual("#FF123456", GetForegroundColor(keywordStyle));
		Assert.AreEqual("#FF654321", GetForegroundColor(stringStyle));

		// Restoring the original sequence hits the stored snapshot again.
		scopes[1] = "keyword.control.lua";

		Assert.AreEqual(keywordStyle, resolver.Resolve(scopes));
	}

	[TestMethod]
	public void Resolve_DuplicateSelectors_KeepPropertiesTheLaterRuleDoesNotSet()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "bold" },
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#123456" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		// Rules that repeat a selector merge per property: the later rule's foreground is adopted while
		// the earlier rule's bold trait survives because the later rule does not set fontStyle.
		Assert.AreEqual("#FF123456", GetForegroundColor(style));
		Assert.IsTrue(style.IsBold);
	}

	[TestMethod]
	public void Resolve_FontStyleTrait_IsCaseSensitive()
	{
		var logger = new CapturingLogger();
		var resolver = new TextMateThemeStyleResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "BOLD" }),
			logger);

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		// The recognized trait names are case-sensitive, matching the TextMate data convention: an
		// unrecognized spelling resets the traits and is reported.
		Assert.IsFalse(style.IsBold);
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("BOLD", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void Resolve_NonHexEightDigitForeground_LogsWarningAndIgnoresForeground()
	{
		var logger = new CapturingLogger();
		var resolver = new TextMateThemeStyleResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#GGGGGGGG" }),
			logger);

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		// The eight-digit form only normalizes when every digit is hexadecimal; otherwise, the value is
		// left for the color parser, which rejects it.
		Assert.IsNull(style.Foreground);
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("Invalid foreground color", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void Resolve_WithoutLogger_DiscardsWarnings()
	{
		// The resolver defaults to a null logger, so malformed theme data must not throw.
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "not-a-color" },
			new TextMateTokenThemeRule { Scope = "string", FontStyle = "sparkle" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
	}

	[TestMethod]
	public void Rules_AssignedCollection_IsCopied()
	{
		var rules = new List<TextMateTokenThemeRule>
		{
			new() { Scope = "keyword", Foreground = "#123456" }
		};
		var theme = new TextMateTokenTheme { Rules = rules };

		rules.Add(new TextMateTokenThemeRule { Scope = "string", Foreground = "#654321" });

		Assert.AreEqual(1, theme.Rules.Count);
	}
}
