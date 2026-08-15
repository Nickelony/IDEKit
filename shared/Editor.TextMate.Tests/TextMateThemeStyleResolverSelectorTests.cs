#if AVALONIAEDIT
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.TextMate.Highlighting;
using static Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests.TextMateThemeTestHelpers;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
#else
using Nickelony.IDEKit.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
using static Nickelony.IDEKit.AvalonEdit.TextMate.Tests.TextMateThemeTestHelpers;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests;
#else
namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;
#endif

[TestClass]
public sealed class TextMateThemeStyleResolverSelectorTests
{
	[TestMethod]
	public void Resolve_MatchesCommaSeparatedSelectors()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule
			{
				Scope = "entity.name.function, support.function, support.function.library, support.function.any-method",
				Foreground = "#4271AE"
			}));

		TextRunStyle style = resolver.Resolve(["source.lua", "support.function.library.lua"]);

		Assert.AreEqual("#FF4271AE", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_MatchesDescendantSelectorAgainstEnclosingScope()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule
			{
				Scope = "source.lua keyword.control",
				Foreground = "#CC99CC"
			}));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual("#FFCC99CC", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_DescendantSelector_RequiresEnclosingScopeOrder()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule
			{
				Scope = "keyword.control source.lua",
				Foreground = "#CC99CC"
			}));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
	}

	[TestMethod]
	public void Resolve_DescendantSelector_BeatsBroaderSinglePartSelector()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#111111" },
			new TextMateTokenThemeRule { Scope = "source.lua keyword.control", Foreground = "#222222" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual("#FF222222", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_DescendantSelector_MatchesAcrossMultipleEnclosingScopes()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "source.lua meta.function keyword", Foreground = "#AAAAAA" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "meta.function.lua", "keyword.control.lua"]);

		Assert.AreEqual("#FFAAAAAA", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_DescendantSelector_ThreePartsOutOfOrder_DoNotMatch()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "meta.function source.lua keyword", Foreground = "#AAAAAA" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "meta.function.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
	}

	[TestMethod]
	public void Resolve_DescendantSelector_EvaluatesEveryMatchingAnchor()
	{
		// The rightmost part matches two scopes of the token. The parent-gated rule matches only the
		// deeper anchor, where it outranks the bare rule, so evaluating every candidate anchor is what
		// makes the deeper push win; stopping at the first matching anchor would keep #111111.
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#111111" },
			new TextMateTokenThemeRule { Scope = "source.lua meta.embedded keyword", Foreground = "#222222" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.operator.lua", "meta.embedded.lua", "keyword.control.lua"]);

		Assert.AreEqual("#FF222222", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_ChildCombinator_RequiresDirectParent()
	{
		var logger = new CapturingLogger();
		var resolver = new TextMateThemeStyleResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "source.lua > keyword.control", Foreground = "#CC99CC" }),
			logger);

		TextRunStyle directStyle = resolver.Resolve(["source.lua", "keyword.control.lua"]);
		TextRunStyle nestedStyle = resolver.Resolve(["source.lua", "meta.function.lua", "keyword.control.lua"]);

		// The child combinator requires the scope that directly encloses the anchor; a scope in
		// between fails the match, and a supported selector is never reported.
		Assert.AreEqual("#FFCC99CC", GetForegroundColor(directStyle));
		Assert.IsFalse(nestedStyle.HasFormatting);
		Assert.AreEqual(0, logger.Messages.Count);
	}

	[TestMethod]
	public void Resolve_ChildCombinatorChain_RequiresEveryStep()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "source.lua > meta.function > keyword.control", Foreground = "#AAAAAA" }));

		TextRunStyle directChainStyle = resolver.Resolve(["source.lua", "meta.function.lua", "keyword.control.lua"]);
		TextRunStyle brokenChainStyle = resolver.Resolve(["source.lua", "meta.embedded.lua", "meta.function.lua", "keyword.control.lua"]);

		// Every child combinator step must find its scope directly above the previously matched
		// scope; the descendant-style skip is not available between combinator parts.
		Assert.AreEqual("#FFAAAAAA", GetForegroundColor(directChainStyle));
		Assert.IsFalse(brokenChainStyle.HasFormatting);
	}

	[TestMethod]
	public void Resolve_ChildCombinator_OutranksDescendantSelectorOfEqualDepth()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "meta.function keyword", Foreground = "#111111" },
			new TextMateTokenThemeRule { Scope = "meta.function > keyword", Foreground = "#222222" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "meta.function.lua", "keyword.control.lua"]);

		// Both rules match the same push; the child combinator outranks the descendant selector
		// because combinator parts count towards the parent-part total.
		Assert.AreEqual("#FF222222", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_SelectorWithRepeatedSpace_DoesNotMatch()
	{
		// vscode-textmate splits a selector on single spaces without collapsing repeats, so a repeated
		// space keeps an empty parent part that never matches a scope; the rule therefore never matches.
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "source.lua  keyword.control", Foreground = "#CC99CC" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
	}

	[TestMethod]
	public void Resolve_MisplacedChildCombinator_LogsWarningAndDoesNotMatch()
	{
		var logger = new CapturingLogger();
		var resolver = new TextMateThemeStyleResolver(
			CreateTheme(
				new TextMateTokenThemeRule { Scope = "> keyword.control", Foreground = "#111111" },
				new TextMateTokenThemeRule { Scope = "keyword.control >", Foreground = "#222222" },
				new TextMateTokenThemeRule { Scope = "keyword > > control", Foreground = "#333333" }),
			logger);

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("> keyword.control", StringComparison.Ordinal)));
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("keyword.control >", StringComparison.Ordinal)));
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("keyword > > control", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void Resolve_UnsupportedSelector_LogsWarning()
	{
		var logger = new CapturingLogger();
		var resolver = new TextMateThemeStyleResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "source.lua - comment", Foreground = "#111111" }),
			logger);

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("source.lua - comment", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void Resolve_ParenthesisSelector_LogsWarning()
	{
		var logger = new CapturingLogger();
		var resolver = new TextMateThemeStyleResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "source.lua (keyword)", Foreground = "#111111" }),
			logger);

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("(keyword)", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void Resolve_WildcardAndPrioritySelectors_DoNotMatchAndLogWarning()
	{
		var logger = new CapturingLogger();
		var resolver = new TextMateThemeStyleResolver(
			CreateTheme(
				new TextMateTokenThemeRule { Scope = "source.lua *", Foreground = "#111111" },
				new TextMateTokenThemeRule { Scope = "L:keyword", Foreground = "#222222" }),
			logger);

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("source.lua *", StringComparison.Ordinal)));
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("L:keyword", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void Resolve_MixedSelector_DropsUnsupportedPartAndKeepsSupportedPart()
	{
		var logger = new CapturingLogger();
		var resolver = new TextMateThemeStyleResolver(
			CreateTheme(new TextMateTokenThemeRule { Scope = "keyword, source.lua - comment", Foreground = "#123456" }),
			logger);

		// The supported half of a comma-separated selector still applies; the unsupported half is
		// reported once at construction and never re-checked during resolution.
		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual("#FF123456", GetForegroundColor(style));
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("source.lua - comment", StringComparison.Ordinal)));
	}

	[TestMethod]
	public void Resolve_EmbeddedAndMisplacedOperators_LogWarningAndDoNotMatch()
	{
		var logger = new CapturingLogger();
		var resolver = new TextMateThemeStyleResolver(
			CreateTheme(
				new TextMateTokenThemeRule { Scope = "keyword(foo)", Foreground = "#111111" },
				new TextMateTokenThemeRule { Scope = "scope*", Foreground = "#222222" },
				new TextMateTokenThemeRule { Scope = "scope>inner", Foreground = "#333333" },
				new TextMateTokenThemeRule { Scope = "l:keyword.control", Foreground = "#444444" }),
			logger);

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.IsFalse(style.HasFormatting);
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("keyword(foo)", StringComparison.Ordinal)));
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("scope*", StringComparison.Ordinal)));
		Assert.IsTrue(logger.Messages.Any(message => message.Contains("l:keyword.control", StringComparison.Ordinal)));

		// An embedded child combinator is reported as a misplaced combinator, not as an unsupported
		// operator.
		Assert.IsTrue(logger.Messages.Any(message =>
			message.Contains("scope>inner", StringComparison.Ordinal)
			&& message.Contains("child combinator", StringComparison.Ordinal)));
	}
}
