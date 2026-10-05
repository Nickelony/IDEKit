using static Nickelony.IDEKit.TextMate.Tests.TextMateThemeTestHelpers;

namespace Nickelony.IDEKit.TextMate.Tests;

[TestClass]
public sealed class TextMateScopeResolverSelectorTests
{
	[TestMethod]
	public void Resolve_MatchesCommaSeparatedSelectors()
	{
		var resolver = new TextMateScopeResolver(
			CreateTheme(
				new TextMateTokenThemeRule
				{
					Scope = "entity.name.function, support.function, support.function.library, support.function.any-method",
					Foreground = "#4271AE",
				}
			)
		);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "support.function.library.lua"]);

		Assert.AreEqual("#FF4271AE", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_MatchesDescendantSelectorAgainstEnclosingScope()
	{
		var resolver = new TextMateScopeResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "source.lua keyword.control", Foreground = "#CC99CC" })
		);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual("#FFCC99CC", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_DescendantSelector_RequiresEnclosingScopeOrder()
	{
		var resolver = new TextMateScopeResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "keyword.control source.lua", Foreground = "#CC99CC" })
		);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
	}

	[TestMethod]
	public void Resolve_DescendantSelector_BeatsBroaderSinglePartSelector()
	{
		var resolver = new TextMateScopeResolver(
			CreateTheme(
				new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#111111" },
				new TextMateTokenThemeRule { Scope = "source.lua keyword.control", Foreground = "#222222" }
			)
		);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual("#FF222222", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_DescendantSelector_MatchesAcrossMultipleEnclosingScopes()
	{
		var resolver = new TextMateScopeResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "source.lua meta.function keyword", Foreground = "#AAAAAA" })
		);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "meta.function.lua", "keyword.control.lua"]);

		Assert.AreEqual("#FFAAAAAA", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_DescendantSelector_ThreePartsOutOfOrder_DoNotMatch()
	{
		var resolver = new TextMateScopeResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "meta.function source.lua keyword", Foreground = "#AAAAAA" })
		);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "meta.function.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
	}

	[TestMethod]
	public void Resolve_DescendantSelector_EvaluatesEveryMatchingAnchor()
	{
		// The rightmost part matches two scopes of the token. The parent-gated rule matches only the
		// deeper anchor, where it outranks the bare rule, so evaluating every candidate anchor is what
		// makes the deeper push win; stopping at the first matching anchor would keep #111111.
		var resolver = new TextMateScopeResolver(
			CreateTheme(
				new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#111111" },
				new TextMateTokenThemeRule { Scope = "source.lua meta.embedded keyword", Foreground = "#222222" }
			)
		);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.operator.lua", "meta.embedded.lua", "keyword.control.lua"]);

		Assert.AreEqual("#FF222222", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_ChildCombinator_RequiresDirectParent()
	{
		var logger = new RecordingLogger();

		var resolver = new TextMateScopeResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "source.lua > keyword.control", Foreground = "#CC99CC" }),
			logger
		);

		TextMateTokenStyle directStyle = resolver.Resolve(["source.lua", "keyword.control.lua"]);
		TextMateTokenStyle nestedStyle = resolver.Resolve(["source.lua", "meta.function.lua", "keyword.control.lua"]);

		// The child combinator requires the scope that directly encloses the anchor; a scope in
		// between fails the match, and a supported selector is never reported.
		Assert.AreEqual("#FFCC99CC", GetForegroundColor(directStyle));
		Assert.IsFalse(nestedStyle.HasFormatting);
		Assert.IsEmpty(logger.Messages);
	}

	[TestMethod]
	public void Resolve_ChildCombinatorChain_RequiresEveryStep()
	{
		var resolver = new TextMateScopeResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "source.lua > meta.function > keyword.control", Foreground = "#AAAAAA" })
		);

		TextMateTokenStyle directChainStyle = resolver.Resolve(["source.lua", "meta.function.lua", "keyword.control.lua"]);
		TextMateTokenStyle brokenChainStyle = resolver.Resolve(["source.lua", "meta.embedded.lua", "meta.function.lua", "keyword.control.lua"]);

		// Every child combinator step must find its scope directly above the scope matched so far;
		// the descendant-style skip is not available between combinator parts.
		Assert.AreEqual("#FFAAAAAA", GetForegroundColor(directChainStyle));
		Assert.IsFalse(brokenChainStyle.HasFormatting);
	}

	[TestMethod]
	public void Resolve_ChildCombinator_OutranksDescendantSelectorOfEqualDepth()
	{
		var resolver = new TextMateScopeResolver(
			CreateTheme(
				new TextMateTokenThemeRule { Scope = "meta.function keyword", Foreground = "#111111" },
				new TextMateTokenThemeRule { Scope = "meta.function > keyword", Foreground = "#222222" }
			)
		);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "meta.function.lua", "keyword.control.lua"]);

		// Both rules match the same push; the child combinator outranks the descendant selector
		// because combinator parts count towards the parent-part total.
		Assert.AreEqual("#FF222222", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_SelectorWithRepeatedSpace_DoesNotMatch()
	{
		// vscode-textmate splits a selector on single spaces without collapsing repeats, so a repeated
		// space keeps an empty parent part that never matches a scope; the rule therefore never matches.
		var resolver = new TextMateScopeResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "source.lua  keyword.control", Foreground = "#CC99CC" })
		);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
	}

	[TestMethod]
	public void Resolve_MisplacedChildCombinator_LogsWarningAndDoesNotMatch()
	{
		var logger = new RecordingLogger();

		var resolver = new TextMateScopeResolver(
			CreateTheme(
				new TextMateTokenThemeRule { Scope = "> keyword.control", Foreground = "#111111" },
				new TextMateTokenThemeRule { Scope = "keyword.control >", Foreground = "#222222" },
				new TextMateTokenThemeRule { Scope = "keyword > > control", Foreground = "#333333" }
			),

			logger
		);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
		Assert.Contains(message => message.Contains("> keyword.control", StringComparison.Ordinal), logger.Messages);
		Assert.Contains(message => message.Contains("keyword.control >", StringComparison.Ordinal), logger.Messages);
		Assert.Contains(message => message.Contains("keyword > > control", StringComparison.Ordinal), logger.Messages);
	}

	[TestMethod]
	public void Resolve_UnsupportedSelector_LogsWarning()
	{
		var logger = new RecordingLogger();

		var resolver = new TextMateScopeResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "source.lua - comment", Foreground = "#111111" }),
			logger
		);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
		Assert.Contains(message => message.Contains("source.lua - comment", StringComparison.Ordinal), logger.Messages);
	}

	[TestMethod]
	public void Resolve_ParenthesisSelector_LogsWarning()
	{
		var logger = new RecordingLogger();

		var resolver = new TextMateScopeResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "source.lua (keyword)", Foreground = "#111111" }),
			logger
		);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
		Assert.Contains(message => message.Contains("(keyword)", StringComparison.Ordinal), logger.Messages);
	}

	[TestMethod]
	public void Resolve_WildcardAndPrioritySelectors_DoNotMatchAndLogWarning()
	{
		var logger = new RecordingLogger();

		var resolver = new TextMateScopeResolver(
			CreateTheme(
				new TextMateTokenThemeRule { Scope = "source.lua *", Foreground = "#111111" },
				new TextMateTokenThemeRule { Scope = "L:keyword", Foreground = "#222222" }
			),

			logger
		);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
		Assert.Contains(message => message.Contains("source.lua *", StringComparison.Ordinal), logger.Messages);
		Assert.Contains(message => message.Contains("L:keyword", StringComparison.Ordinal), logger.Messages);
	}

	[TestMethod]
	public void Resolve_MixedSelector_DropsUnsupportedPartAndKeepsSupportedPart()
	{
		var logger = new RecordingLogger();

		var resolver = new TextMateScopeResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "keyword, source.lua - comment", Foreground = "#123456" }),
			logger
		);

		// The supported half of a comma-separated selector still applies; the unsupported half is
		// reported once at construction and never re-checked during resolution.
		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual("#FF123456", GetForegroundColor(style));
		Assert.Contains(message => message.Contains("source.lua - comment", StringComparison.Ordinal), logger.Messages);
	}

	[TestMethod]
	public void Resolve_EmbeddedAndMisplacedOperators_LogWarningAndDoNotMatch()
	{
		var logger = new RecordingLogger();

		var resolver = new TextMateScopeResolver(
			CreateTheme(
				new TextMateTokenThemeRule { Scope = "keyword(foo)", Foreground = "#111111" },
				new TextMateTokenThemeRule { Scope = "scope*", Foreground = "#222222" },
				new TextMateTokenThemeRule { Scope = "scope>inner", Foreground = "#333333" },
				new TextMateTokenThemeRule { Scope = "l:keyword.control", Foreground = "#444444" }
			),

			logger
		);

		TextMateTokenStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
		Assert.Contains(message => message.Contains("keyword(foo)", StringComparison.Ordinal), logger.Messages);
		Assert.Contains(message => message.Contains("scope*", StringComparison.Ordinal), logger.Messages);
		Assert.Contains(message => message.Contains("l:keyword.control", StringComparison.Ordinal), logger.Messages);

		// An embedded child combinator is reported as a misplaced combinator, not as an unsupported
		// operator.
		Assert.Contains(
			message => message.Contains("scope>inner", StringComparison.Ordinal) && message.Contains("child combinator", StringComparison.Ordinal),
			logger.Messages
		);
	}
}
