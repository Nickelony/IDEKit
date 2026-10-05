using Nickelony.IDEKit.Core.Media;
using static Nickelony.IDEKit.TextMate.Tests.TextMateThemeTestHelpers;

namespace Nickelony.IDEKit.TextMate.Tests;

[TestClass]
public sealed class TextMateScopeResolverStyleTests
{
	[TestMethod]
	public void Resolve_UnknownFontStyleTrait_LogsWarningAndKeepsKnownTraits()
	{
		var logger = new RecordingLogger();

		var resolver = new TextMateScopeResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "bold sparkle" }), logger);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsTrue(style.IsBold);
		Assert.Contains(message => message.Contains("sparkle", StringComparison.Ordinal), logger.Messages);
	}

	[TestMethod]
	[DataRow("", DisplayName = "EmptyString")]
	[DataRow("   ", DisplayName = "Whitespace")]
	[DataRow("none", DisplayName = "NoneKeyword")]
	public void Resolve_PresentFontStyle_ResetsInheritedTraits(string resetValue)
	{
		// A present font-style value is an explicit reset, including the empty string that VS Code and
		// TextMate theme data use to clear inherited traits. "none" is accepted as a readable spelling.
		var resolver = new TextMateScopeResolver(
			CreateTheme(
				new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "bold italic underline strikethrough" },
				new TextMateTokenThemeRule { Scope = "keyword.control", FontStyle = resetValue }
			)
		);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.IsBold, $"'{resetValue}' must reset bold.");
		Assert.IsFalse(style.IsItalic, $"'{resetValue}' must reset italic.");
		Assert.IsFalse(style.IsUnderline, $"'{resetValue}' must reset underline.");
		Assert.IsFalse(style.IsStrikethrough, $"'{resetValue}' must reset strikethrough.");
	}

	[TestMethod]
	public void Resolve_AbsentFontStyle_KeepsInheritedTraits()
	{
		// An absent font-style value (null) leaves inherited traits alone, so a more specific rule that
		// only sets a foreground keeps the bold trait of the broader rule.
		var resolver = new TextMateScopeResolver(
			CreateTheme(
				new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "bold" },
				new TextMateTokenThemeRule { Scope = "keyword.control", Foreground = "#123456" }
			)
		);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsTrue(style.IsBold);
		Assert.AreEqual("#FF123456", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_EightDigitHexColor_IsParsedAsRrggbbaa()
	{
		// TextMate theme data spells eight-digit colors as #RRGGBBAA, with the alpha component last.
		var resolver = new TextMateScopeResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#E7C0C0FF" }));

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual(new ArgbColor(A: 0xFF, R: 0xE7, G: 0xC0, B: 0xC0), style.Foreground);
	}

	[TestMethod]
	public void Resolve_EightDigitHexColorWithAlpha_PreservesAlpha()
	{
		var resolver = new TextMateScopeResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#FF000080" }));

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual(new ArgbColor(A: 0x80, R: 0xFF, G: 0x00, B: 0x00), style.Foreground);
	}

	[TestMethod]
	public void Resolve_CacheOverflow_KeepsLaterResolutionsCorrect()
	{
		var resolver = new TextMateScopeResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#123456" }));

		TextMateTokenStyle cachedStyle = resolver.Resolve(["source.lua", "keyword"]);

		// Fill the bounded cache past its entry limit so the trim path runs. The trim itself
		// is not observable through the returned values, because equal scope sequences always resolve to
		// equal styles; the contract after an overflow is that both the overflow scopes and the earlier
		// scopes still resolve correctly.
		for (int i = 0; i <= TextMateScopeResolver.MaxCacheEntryCount; i++)
			resolver.Resolve(["source.lua", $"scope{i}", "keyword"]);

		TextMateTokenStyle refreshedStyle = resolver.Resolve(["source.lua", "keyword"]);

		Assert.AreEqual("#FF123456", GetForegroundColor(refreshedStyle));
		Assert.AreEqual(cachedStyle, refreshedStyle);
	}

	[TestMethod]
	public void Resolve_FromMultipleThreads_ReturnsConsistentStyles()
	{
		var resolver = new TextMateScopeResolver(
			CreateTheme(
				new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#123456" },
				new TextMateTokenThemeRule { Scope = "string", Foreground = "#654321" }
			)
		);

		string[] expectedColors = ["#FF123456", "#FF654321"];

		Parallel.For(
			0,
			1000,

			index =>
			{
				int caseIndex = index % 2;

				TextMateTokenStyle style =
					caseIndex == 0 ? resolver.Resolve(["source.lua", "keyword.control.lua"]) : resolver.Resolve(["source.lua", "string.quoted.lua"]);

				Assert.AreEqual(expectedColors[caseIndex], GetForegroundColor(style));
			}
		);
	}

	[TestMethod]
	public void Resolve_FourDigitHexColor_IsParsedAsRgba()
	{
		// TextMate theme data spells four-digit colors as #RGBA; every digit is doubled and the alpha component is last.
		var resolver = new TextMateScopeResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#F00A" }));

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual(new ArgbColor(A: 0xAA, R: 0xFF, G: 0x00, B: 0x00), style.Foreground);
	}

	[TestMethod]
	public void Resolve_EightDigitHexColor_IsAlwaysReadAsTextMateOrder()
	{
		// An eight-digit value is read as #RRGGBBAA, never with the alpha component first, so a value whose
		// leading byte looks like an alpha component is reinterpreted.
		var resolver = new TextMateScopeResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#800000FF" }));

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual(new ArgbColor(A: 0xFF, R: 0x80, G: 0x00, B: 0x00), style.Foreground);
	}

	[TestMethod]
	public void Resolve_FourDigitHexColor_IsAlwaysReadAsTextMateOrder()
	{
		// A four-digit value is read as #RGBA, never with the alpha digit first.
		var resolver = new TextMateScopeResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#8F00" }));

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual(new ArgbColor(A: 0x00, R: 0x88, G: 0xFF, B: 0x00), style.Foreground);
	}

	[TestMethod]
	public void Resolve_FontStyleFlags_ApplyBoldItalicUnderlineAndStrikethrough()
	{
		var resolver = new TextMateScopeResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "bold italic underline strikethrough" })
		);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsTrue(style.IsBold);
		Assert.IsTrue(style.IsItalic);
		Assert.IsTrue(style.IsUnderline);
		Assert.IsTrue(style.IsStrikethrough);
	}

	[TestMethod]
	public void Resolve_UnderlineOnly_SetsOnlyUnderline()
	{
		var resolver = new TextMateScopeResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "underline" }));

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsTrue(style.IsUnderline);
		Assert.IsFalse(style.IsStrikethrough);
	}

	[TestMethod]
	public void Resolve_StrikethroughOnly_SetsOnlyStrikethrough()
	{
		var resolver = new TextMateScopeResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "strikethrough" }));

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsTrue(style.IsStrikethrough);
		Assert.IsFalse(style.IsUnderline);
	}

	[TestMethod]
	public void Resolve_NoneFontStyle_ProducesNoFormatting()
	{
		var resolver = new TextMateScopeResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "none" }));

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
		Assert.AreEqual(TextMateTokenStyle.Empty, style);
	}

	[TestMethod]
	public void Resolve_InvalidForegroundColor_LogsWarningAndIgnoresForeground()
	{
		var logger = new RecordingLogger();

		var resolver = new TextMateScopeResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", Foreground = "not-a-color" }), logger);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsNull(style.Foreground);
		Assert.Contains(message => message.Contains("Invalid foreground color", StringComparison.Ordinal), logger.Messages);
	}

	[TestMethod]
	public void Resolve_ColorName_LogsWarningAndIgnoresForeground()
	{
		var logger = new RecordingLogger();

		var resolver = new TextMateScopeResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", Foreground = "red" }), logger);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		// Only the hexadecimal spellings of TextMate theme data are colors; a name that a toolkit would accept is not.
		Assert.IsNull(style.Foreground);
		Assert.Contains(message => message.Contains("Invalid foreground color 'red'", StringComparison.Ordinal), logger.Messages);
	}

	[TestMethod]
	public void Resolve_EmptyScopes_ReturnsTheEmptyStyle()
	{
		var resolver = new TextMateScopeResolver(CreateTheme());

		Assert.AreEqual(TextMateTokenStyle.Empty, resolver.Resolve(null));
		Assert.AreEqual(TextMateTokenStyle.Empty, resolver.Resolve([]));
	}

	[TestMethod]
	public void Resolve_RepeatedScopes_AreServedFromTheCache()
	{
		var resolver = new TextMateScopeResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#123456" }));

		TextMateTokenStyle first = resolver.Resolve(["source.lua", "keyword.control.lua"]);
		int missesAfterFirstResolution = resolver.CacheMissCount;
		TextMateTokenStyle second = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		// The first resolution misses the cache and stores the style; the repeated resolution is served
		// from the cache without recomputing it.
		Assert.AreEqual(1, missesAfterFirstResolution);
		Assert.AreEqual(missesAfterFirstResolution, resolver.CacheMissCount);
		Assert.AreEqual(first, second);
	}

	[TestMethod]
	public void Resolve_CallerMutationOfTheScopeList_DoesNotCorruptTheCache()
	{
		var resolver = new TextMateScopeResolver(
			CreateTheme(
				new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#123456" },
				new TextMateTokenThemeRule { Scope = "string", Foreground = "#654321" }
			)
		);

		var scopes = new List<string> { "source.lua", "keyword.control.lua" };
		TextMateTokenStyle keywordStyle = resolver.Resolve(scopes);

		// Stored keys snapshot the sequence, so reusing and mutating the caller's list resolves the new
		// scopes instead of returning the style cached for the previous sequence.
		scopes[1] = "string.quoted.lua";

		TextMateTokenStyle stringStyle = resolver.Resolve(scopes);

		Assert.AreEqual("#FF123456", GetForegroundColor(keywordStyle));
		Assert.AreEqual("#FF654321", GetForegroundColor(stringStyle));

		// Restoring the original sequence hits the stored snapshot again.
		scopes[1] = "keyword.control.lua";

		Assert.AreEqual(keywordStyle, resolver.Resolve(scopes));
	}

	[TestMethod]
	public void Resolve_AfterTheCallerMutatesItsList_AnEqualSequenceStillHitsTheStoredSnapshot()
	{
		var resolver = new TextMateScopeResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#123456" }));

		var scopes = new List<string> { "source.lua", "keyword.control.lua" };
		resolver.Resolve(scopes);

		scopes[1] = "string.quoted.lua";
		resolver.Resolve(scopes);

		int missesBefore = resolver.CacheMissCount;

		resolver.Resolve(["source.lua", "keyword.control.lua"]);

		// The stored key owns a snapshot of the first sequence, so an equal sequence in a new list is served from the cache.
		Assert.AreEqual(missesBefore, resolver.CacheMissCount);
	}

	[TestMethod]
	public void Resolve_NullScopeEntry_ThrowsArgumentException()
	{
		var resolver = new TextMateScopeResolver(CreateTheme());
		Assert.ThrowsExactly<ArgumentException>(() => resolver.Resolve(["source.lua", null!]));
	}

	[TestMethod]
	public void Resolve_DuplicateSelectors_KeepPropertiesTheLaterRuleDoesNotSet()
	{
		var resolver = new TextMateScopeResolver(
			CreateTheme(
				new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "bold" },
				new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#123456" }
			)
		);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		// Rules that repeat a selector merge per property: the later rule's foreground is adopted while
		// the earlier rule's bold trait survives because the later rule does not set fontStyle.
		Assert.AreEqual("#FF123456", GetForegroundColor(style));
		Assert.IsTrue(style.IsBold);
	}

	[TestMethod]
	public void Resolve_FontStyleTrait_IsCaseSensitive()
	{
		var logger = new RecordingLogger();
		var resolver = new TextMateScopeResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "BOLD" }), logger);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		// The recognized trait names are case-sensitive, matching the TextMate data convention: an
		// unrecognized spelling resets the traits and is reported.
		Assert.IsFalse(style.IsBold);
		Assert.Contains(message => message.Contains("BOLD", StringComparison.Ordinal), logger.Messages);
	}

	[TestMethod]
	public void Resolve_NonHexEightDigitForeground_LogsWarningAndIgnoresForeground()
	{
		var logger = new RecordingLogger();

		var resolver = new TextMateScopeResolver(CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#GGGGGGGG" }), logger);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsNull(style.Foreground);
		Assert.Contains(message => message.Contains("Invalid foreground color", StringComparison.Ordinal), logger.Messages);
	}

	[TestMethod]
	public void Resolve_WithoutLogger_DiscardsWarnings()
	{
		// The resolver defaults to a null logger, so malformed theme data must not throw.
		var resolver = new TextMateScopeResolver(
			CreateTheme(
				new TextMateTokenThemeRule { Scope = "keyword", Foreground = "not-a-color" },
				new TextMateTokenThemeRule { Scope = "string", FontStyle = "sparkle" }
			)
		);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
	}

	[TestMethod]
	public void Constructor_NullTheme_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextMateScopeResolver(theme: null!));

	[TestMethod]
	public void Rules_AssignedCollection_IsCopied()
	{
		var rules = new List<TextMateTokenThemeRule>
		{
			new() { Scope = "keyword", Foreground = "#123456" },
		};

		var theme = new TextMateTokenTheme { Rules = rules };

		rules.Add(new TextMateTokenThemeRule { Scope = "string", Foreground = "#654321" });

		Assert.HasCount(1, theme.Rules);
	}
}
