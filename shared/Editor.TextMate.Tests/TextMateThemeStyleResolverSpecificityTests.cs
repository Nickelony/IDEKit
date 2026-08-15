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
public sealed class TextMateThemeStyleResolverSpecificityTests
{
	[TestMethod]
	public void Resolve_PrefersMoreSpecificSelectorsOverBroaderMatches()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule
			{
				Scope = "entity.name.class, support.class, support.type, support.variable, variable.language.self",
				Foreground = "#66CCCC"
			},
			new TextMateTokenThemeRule
			{
				Scope = "entity.other.attribute, support.type.property-name",
				Foreground = "#D7B8FF"
			}));

		TextRunStyle style = resolver.Resolve(["source.lua", "support.type.property-name.lua"]);

		Assert.AreEqual("#FFD7B8FF", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_DuplicateSelectors_LaterValuesWin()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule
			{
				Scope = "entity.name.function",
				Foreground = "#111111"
			},
			new TextMateTokenThemeRule
			{
				Scope = "support.function",
				Foreground = "#333333"
			},
			new TextMateTokenThemeRule
			{
				Scope = "entity.name.function",
				Foreground = "#222222"
			}));

		TextRunStyle style = resolver.Resolve(["source.lua", "entity.name.function.lua"]);

		// Rules that repeat the same selector merge, and the later value wins for each property the
		// later rule sets, the way VS Code merges duplicate theme selectors.
		Assert.AreEqual("#FF222222", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_ParentScopedRule_InheritsBareRuleTraits()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "bold" },
			new TextMateTokenThemeRule { Scope = "source.lua keyword", Foreground = "#FF0000" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		// A parent-scoped rule inherits the properties it does not set from the theme's bare rules:
		// VS Code bakes the bare rule's values into the parent-scoped rule while it parses the theme,
		// so the more specific rule still applies the bold trait.
		Assert.AreEqual("#FFFF0000", GetForegroundColor(style));
		Assert.IsTrue(style.IsBold);
	}

	[TestMethod]
	public void Resolve_ParentScopedRule_InheritsBareRuleForeground()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#00FF00" },
			new TextMateTokenThemeRule { Scope = "source.lua keyword", FontStyle = "bold" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		Assert.AreEqual("#FF00FF00", GetForegroundColor(style));
		Assert.IsTrue(style.IsBold);
	}

	[TestMethod]
	public void Resolve_DeeperBareRule_BeatsParentScopedRule()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "bold" },
			new TextMateTokenThemeRule { Scope = "source.lua keyword", Foreground = "#FF0000" },
			new TextMateTokenThemeRule { Scope = "keyword.control", Foreground = "#00FF00" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		// The bare rules form one merged candidate whose deeper scope outranks the parent-scoped rule;
		// the broader bold trait still applies through the merge.
		Assert.AreEqual("#FF00FF00", GetForegroundColor(style));
		Assert.IsTrue(style.IsBold);
	}

	[TestMethod]
	public void Resolve_EqualSpecificityDistinctSelectors_SelectorOrderWins()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "bbb x", Foreground = "#222222" },
			new TextMateTokenThemeRule { Scope = "aaa x", Foreground = "#111111" }));

		TextRunStyle style = resolver.Resolve(["aaa", "bbb", "x"]);

		// Rules rank in selector order, which is the order VS Code's theme parser sorts them in, so the
		// lexicographically earlier selector wins the tie regardless of its position in the theme.
		Assert.AreEqual("#FF111111", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_ShadowedProperty_FallsBackToDefaults()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = string.Empty, FontStyle = "italic" },
			new TextMateTokenThemeRule { Scope = "source.lua keyword", Foreground = "#FF0000" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "keyword.control.lua"]);

		// A property the winning parent-scoped rule leaves unset falls back to the theme's bare rules,
		// then to shallower pushes and the theme defaults, not to a shadowed parent-scoped rule of the
		// same push.
		Assert.AreEqual("#FFFF0000", GetForegroundColor(style));
		Assert.IsTrue(style.IsItalic);
	}

	[TestMethod]
	public void Resolve_DeeperPush_OverridesShallowerRuleOutcome()
	{
		// VS Code applies the theme push by push, so a rule anchored at the innermost scope replaces
		// the outcome of a more deeply nested selector that matched an enclosing scope earlier.
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "string.quoted.double.lua", Foreground = "#FF0000" },
			new TextMateTokenThemeRule { Scope = "punctuation", Foreground = "#00FF00" }));

		TextRunStyle style = resolver.Resolve(
			["source.lua", "string.quoted.double.lua", "punctuation.definition.string.begin.lua"]);

		Assert.AreEqual("#FF00FF00", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_PushWithoutMatch_KeepsAccumulatedAttributes()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "string.quoted.double.lua", Foreground = "#FF0000" }));

		TextRunStyle style = resolver.Resolve(
			["source.lua", "string.quoted.double.lua", "punctuation.definition.string.begin.lua"]);

		Assert.AreEqual("#FFFF0000", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_DefaultsRule_AppliesToEveryToken()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = string.Empty, Foreground = "#AABBCC", FontStyle = "italic" },
			new TextMateTokenThemeRule { Scope = "keyword", FontStyle = "bold" }));

		TextRunStyle matchedStyle = resolver.Resolve(["source.lua", "keyword.control.lua"]);
		TextRunStyle unmatchedStyle = resolver.Resolve(["source.lua", "comment.line.lua"]);

		// The defaults carry the foreground to every token. The keyword rule's present fontStyle is an
		// explicit reset, so it replaces the inherited italic trait with bold for tokens it matches.
		Assert.AreEqual("#FFAABBCC", GetForegroundColor(matchedStyle));
		Assert.IsTrue(matchedStyle.IsBold);
		Assert.IsFalse(matchedStyle.IsItalic);

		Assert.AreEqual("#FFAABBCC", GetForegroundColor(unmatchedStyle));
		Assert.IsTrue(unmatchedStyle.IsItalic);
	}

	[TestMethod]
	public void Resolve_LaterDefaultsRule_OverwritesEarlierValue()
	{
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = string.Empty, Foreground = "#111111" },
			new TextMateTokenThemeRule { Scope = "   ", Foreground = "#222222" }));

		TextRunStyle style = resolver.Resolve(["source.lua"]);

		Assert.AreEqual("#FF222222", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_NullScope_ActsAsADefaultsRule()
	{
		// Deserialized theme data can produce a null scope; it is treated like a blank scope, so the
		// rule provides defaults for every token instead of failing the resolver.
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = null!, Foreground = "#AABBCC" }));

		TextRunStyle style = resolver.Resolve(["source.lua", "comment.line.lua"]);

		Assert.AreEqual("#FFAABBCC", GetForegroundColor(style));
	}

	// The following tests port the ranking expectations from vscode-textmate's theme tests
	// (src/tests/themes.test.ts) so the resolver stays pinned to the published VS Code ranking:
	// scope depth, then parent scope length (deepest first), then parent count.

	[TestMethod]
	public void Resolve_PublishedRanking_DeepestScopeWins()
	{
		// "Theme matching gives higher priority to deeper matches".
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "punctuation.definition.string.begin.html", Foreground = "#300000" },
			new TextMateTokenThemeRule { Scope = "meta.tag punctuation.definition.string", Foreground = "#400000" }));

		TextRunStyle style = resolver.Resolve(["punctuation.definition.string.begin.html"]);

		Assert.AreEqual("#FF300000", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_PublishedRanking_DeeperScopeBeatsEnclosingSelector()
	{
		// "Theme matching gives higher priority to parent matches 1".
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "c a", Foreground = "#300000" },
			new TextMateTokenThemeRule { Scope = "d a.b", Foreground = "#400000" },
			new TextMateTokenThemeRule { Scope = "a", Foreground = "#500000" }));

		TextRunStyle style = resolver.Resolve(["d", "a.b"]);

		Assert.AreEqual("#FF400000", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_PublishedRanking_ParentMatchBeatsBareScope()
	{
		// "Theme matching gives higher priority to parent matches 2": the parent-gated rule wins the
		// depth tie against the bare scope, while the more specific rule's parent does not match.
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "meta.tag entity", Foreground = "#300000" },
			new TextMateTokenThemeRule { Scope = "meta.selector.css entity.name.tag", Foreground = "#400000" },
			new TextMateTokenThemeRule { Scope = "entity", Foreground = "#500000" }));

		TextRunStyle style = resolver.Resolve(
			["text.html.cshtml", "meta.tag.structure.any.html", "entity.name.tag.structure.any.html"]);

		Assert.AreEqual("#FF300000", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_PublishedRanking_LongerParentScopeWins()
	{
		// "Theme resolving should give deeper scopes higher specificity (#233)": both rules have the
		// same scope depth and parent count, so the longer parent scope name wins.
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "y.z a.b", Foreground = "#200000" },
			new TextMateTokenThemeRule { Scope = "x y a.b", Foreground = "#300000" }));

		TextRunStyle deepParentStyle = resolver.Resolve(["x", "y.z", "a.b"]);
		TextRunStyle shallowParentStyle = resolver.Resolve(["x", "y", "a.b"]);

		Assert.AreEqual("#FF200000", GetForegroundColor(deepParentStyle));
		Assert.AreEqual("#FF300000", GetForegroundColor(shallowParentStyle));
	}

	[TestMethod]
	public void Resolve_PublishedRanking_LongerParentChainWins()
	{
		// "Theme matching Microsoft/vscode#23460": the duplicate selectors override each other, but the
		// rule with the longer (deeper) parent scope outranks both of them.
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule
			{
				Scope = "meta.structure.dictionary.json string.quoted.double.json",
				Foreground = "#FF410D"
			},
			new TextMateTokenThemeRule
			{
				Scope = "meta.structure.dictionary.json string.quoted.double.json",
				Foreground = "#FFFFFF"
			},
			new TextMateTokenThemeRule
			{
				Scope = "meta.structure.dictionary.value.json string.quoted.double.json",
				Foreground = "#FF410D"
			}));

		TextRunStyle style = resolver.Resolve(
		[
			"source.json",
			"meta.structure.dictionary.json",
			"meta.structure.dictionary.value.json",
			"string.quoted.double.json"
		]);

		Assert.AreEqual("#FFFF410D", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_PublishedRanking_LongerParentScopeBeatsParentCount()
	{
		// TextMate's ranking compares parent scopes depth-first by scope name before it counts them:
		// the single long parent scope outranks the two short ones.
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "abcdefghijklmnop xy scope", Foreground = "#111111" },
			new TextMateTokenThemeRule { Scope = "abcdefghijklmnop scope", Foreground = "#222222" }));

		TextRunStyle style = resolver.Resolve(["abcdefghijklmnop", "xy", "scope"]);

		Assert.AreEqual("#FF222222", GetForegroundColor(style));
	}

	[TestMethod]
	public void Resolve_PublishedRanking_ChildCombinatorAndDescendantSelectors()
	{
		// "Theme resolving a rule with child combinator": the path expectations are ported from
		// vscode-textmate's theme tests, including the parent-part total that lets "b > a" outrank
		// "b a" when both match.
		var resolver = new TextMateThemeStyleResolver(CreateTheme(
			new TextMateTokenThemeRule { Scope = "b a", Foreground = "#200000" },
			new TextMateTokenThemeRule { Scope = "b > a", Foreground = "#300000" },
			new TextMateTokenThemeRule { Scope = "c > b > a", Foreground = "#400000" },
			new TextMateTokenThemeRule { Scope = "a", Foreground = "#500000" }));

		Assert.AreEqual("#FF300000", GetForegroundColor(resolver.Resolve(["b", "a"])));
		Assert.AreEqual("#FF200000", GetForegroundColor(resolver.Resolve(["b", "c", "a"])));
		Assert.AreEqual("#FF400000", GetForegroundColor(resolver.Resolve(["c", "b", "a"])));
		Assert.AreEqual("#FF200000", GetForegroundColor(resolver.Resolve(["c", "b", "d", "a"])));
	}
}
