using Nickelony.IDEKit.Core.Media;
using static Nickelony.IDEKit.TextMate.Tests.TextMateThemeTestHelpers;

namespace Nickelony.IDEKit.TextMate.Tests;

/// <summary>
/// Pins that the resolver finds the rules of a scope the way a full scan of the theme's rules would: over a realistic theme and over themes
/// whose selectors collide, and for each precedence rule on its own.
/// </summary>
[TestClass]
public sealed class TextMateScopeResolverLookupTests
{
	[TestMethod]
	public void Resolve_DarkPlusLikeTheme_AgreesWithAFullScanForEveryScopeStack()
	{
		TextMateTokenTheme theme = ScopeCorpusTestHelpers.CreateDarkPlusLikeTheme();

		int distinctStyles = AssertAgreesOverTheCorpus(theme, seed: 7);

		Assert.IsGreaterThan(20, distinctStyles, "The corpus must reach many different styles, or the comparison proves little.");
	}

	[DataRow(1)]
	[DataRow(2)]
	[DataRow(3)]
	[DataRow(4)]
	[DataRow(5)]
	[TestMethod]
	public void Resolve_CollidingSelectors_AgreesWithAFullScanForEveryScopeStack(int seed)
	{
		TextMateTokenTheme theme = ScopeCorpusTestHelpers.CreateCollidingSelectorsTheme(seed);

		int distinctStyles = AssertAgreesOverTheCorpus(theme, seed);

		Assert.IsGreaterThan(8, distinctStyles, "The corpus must reach many different styles, or the comparison proves little.");
	}

	[TestMethod]
	public void Resolve_MoreSpecificSelector_WinsOverALessSpecificOne()
	{
		TextMateTokenTheme theme = CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#111111" },
			new TextMateTokenThemeRule { Scope = "keyword.control", Foreground = "#222222" },
			new TextMateTokenThemeRule { Scope = "keyword.control.flow", Foreground = "#333333" }
		);

		AssertResolvesTo(theme, ["source.lua", "keyword.control.flow.lua"], "#FF333333");
		AssertResolvesTo(theme, ["source.lua", "keyword.control.loop.lua"], "#FF222222");
		AssertResolvesTo(theme, ["source.lua", "keyword.operator.lua"], "#FF111111");
	}

	[TestMethod]
	public void Resolve_SelectorsOfEqualSpecificity_ResolveInSelectorOrderNotDeclarationOrder()
	{
		TextMateTokenTheme theme = CreateTheme(
			new TextMateTokenThemeRule { Scope = "pb keyword", Foreground = "#BBBBBB" },
			new TextMateTokenThemeRule { Scope = "pa keyword", Foreground = "#AAAAAA" }
		);

		// Both selectors match with parents of the same length, so the one that sorts first wins although it was declared second.
		AssertResolvesTo(theme, ["pa", "pb", "keyword.lua"], "#FFAAAAAA");
		AssertResolvesTo(theme, ["pb", "pa", "keyword.lua"], "#FFAAAAAA");
	}

	[TestMethod]
	public void Resolve_SelectorRepeatedLater_MergesWithTheLaterValuesWinning()
	{
		TextMateTokenTheme theme = CreateTheme(
			new TextMateTokenThemeRule
			{
				Scope = "keyword",
				Foreground = "#111111",
				FontStyle = "bold",
			},

			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#222222" }
		);

		TextMateTokenStyle style = AssertResolvesTo(theme, ["source.lua", "keyword.control.lua"], "#FF222222");

		Assert.IsTrue(style.IsBold, "The repeated selector must keep the font style that only the earlier rule set.");
	}

	[TestMethod]
	public void Resolve_ParentSelector_NeedsItsParentAmongTheEnclosingScopes()
	{
		TextMateTokenTheme theme = CreateTheme(
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#111111" },
			new TextMateTokenThemeRule { Scope = "source.lua keyword", Foreground = "#222222" }
		);

		AssertResolvesTo(theme, ["source.lua", "meta.block.lua", "keyword.control.lua"], "#FF222222");
		AssertResolvesTo(theme, ["source.cs", "keyword.control.cs"], "#FF111111");

		// The parent must enclose the keyword scope; here it is the inner scope, so only the bare selector matches.
		AssertResolvesTo(theme, ["keyword.control", "source.lua"], "#FF111111");
	}

	[TestMethod]
	public void Resolve_ChildCombinator_NeedsTheParentDirectlyAbove()
	{
		TextMateTokenTheme theme = CreateTheme(
			new TextMateTokenThemeRule { Scope = "meta.call", Foreground = "#111111" },
			new TextMateTokenThemeRule { Scope = "meta.call > entity.name", Foreground = "#222222" }
		);

		AssertResolvesTo(theme, ["meta.call.lua", "entity.name.function.lua"], "#FF222222");
		AssertResolvesTo(theme, ["meta.call.lua", "meta.block.lua", "entity.name.function.lua"], "#FF111111");
	}

	[TestMethod]
	public void Resolve_DefaultsRule_AppliesWhereNoSelectorMatches()
	{
		TextMateTokenTheme theme = CreateTheme(
			new TextMateTokenThemeRule { Scope = string.Empty, Foreground = "#999999" },
			new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#111111" }
		);

		AssertResolvesTo(theme, ["source.lua", "string.quoted.lua"], "#FF999999");
		AssertResolvesTo(theme, ["source.lua", "keyword.control.lua"], "#FF111111");
	}

	private static int AssertAgreesOverTheCorpus(TextMateTokenTheme theme, int seed)
	{
		var resolver = new TextMateScopeResolver(theme);
		var reference = new FullScanScopeResolver(theme);

		IReadOnlyList<string[]> stacks = ScopeCorpusTestHelpers.CreateScopeStacks(theme, seed, randomStackCount: 3_000);

		var styles = new HashSet<TextMateTokenStyle>();

		foreach (string[] stack in stacks)
		{
			TextMateTokenStyle expected = reference.Resolve(stack);

			Assert.AreEqual(expected, resolver.Resolve(stack), $"Scopes: {string.Join(" | ", stack)}");

			styles.Add(expected);
		}

		return styles.Count;
	}

	private static TextMateTokenStyle AssertResolvesTo(TextMateTokenTheme theme, string[] scopes, string expectedForeground)
	{
		TextMateTokenStyle style = new TextMateScopeResolver(theme).Resolve(scopes);

		Assert.AreEqual(new FullScanScopeResolver(theme).Resolve(scopes), style, $"Scopes: {string.Join(" | ", scopes)}");
		Assert.AreEqual(expectedForeground, GetForegroundColor(style));

		return style;
	}

	/// <summary>
	/// Resolves a scope stack the way <see cref="TextMateScopeResolver"/> does, but by testing the whole rule list against each scope,
	/// so the resolver's rule lookup has an answer to be compared with.
	/// </summary>
	/// <remarks>
	/// The resolution rules are those of the resolver: one scope push at a time, the most specific rule of a push overriding
	/// the accumulated attributes, a parent-scoped winner falling back to the push's bare rules for the properties it omits, and the theme defaults
	/// below all pushes. Only the way the matching rules are found differs, and there is no cache.
	/// </remarks>
	private sealed class FullScanScopeResolver
	{
		private readonly TextMateSelectorRuleSet _rules;

		internal FullScanScopeResolver(TextMateTokenTheme theme) => _rules = TextMateSelectorRuleSet.Create(theme, logger: null);

		internal TextMateTokenStyle Resolve(IReadOnlyList<string> scopes)
		{
			if (scopes.Count == 0)
				return TextMateTokenStyle.Empty;

			ArgbColor? foreground = _rules.DefaultForeground;

			bool? isBold = _rules.DefaultTraits.Bold;
			bool? isItalic = _rules.DefaultTraits.Italic;
			bool? isUnderline = _rules.DefaultTraits.Underline;
			bool? isStrikethrough = _rules.DefaultTraits.Strikethrough;

			for (int anchorIndex = 0; anchorIndex < scopes.Count; anchorIndex++)
			{
				var parentScopedMatches = new List<TextMateSelectorRule>();
				var bareMatches = new List<TextMateSelectorRule>();

				foreach (TextMateSelectorRule rule in _rules.Rules)
				{
					if (!rule.MatchesAnchor(scopes, anchorIndex))
						continue;

					if (rule.HasParentScopes)
						parentScopedMatches.Add(rule);
					else
						bareMatches.Add(rule);
				}

				if (parentScopedMatches.Count == 0 && bareMatches.Count == 0)
					continue;

				parentScopedMatches.Sort(TextMateSelectorRule.CompareByMatchPrecedence);
				bareMatches.Sort(TextMateSelectorRule.CompareByMatchPrecedence);

				ArgbColor? bareForeground = null;

				bool? bareBold = null;
				bool? bareItalic = null;
				bool? bareUnderline = null;
				bool? bareStrikethrough = null;

				foreach (TextMateSelectorRule rule in bareMatches)
				{
					bareForeground ??= rule.Foreground;
					bareBold ??= rule.Traits.Bold;
					bareItalic ??= rule.Traits.Italic;
					bareUnderline ??= rule.Traits.Underline;
					bareStrikethrough ??= rule.Traits.Strikethrough;
				}

				ArgbColor? pushForeground;

				bool? pushBold;
				bool? pushItalic;
				bool? pushUnderline;
				bool? pushStrikethrough;

				if (parentScopedMatches.Count == 0 || (bareMatches.Count > 0 && bareMatches[0].ScopeDepth > parentScopedMatches[0].ScopeDepth))
				{
					pushForeground = bareForeground;
					pushBold = bareBold;
					pushItalic = bareItalic;
					pushUnderline = bareUnderline;
					pushStrikethrough = bareStrikethrough;
				}
				else
				{
					TextMateSelectorRule winner = parentScopedMatches[0];

					pushForeground = winner.Foreground ?? bareForeground;
					pushBold = winner.Traits.Bold ?? bareBold;
					pushItalic = winner.Traits.Italic ?? bareItalic;
					pushUnderline = winner.Traits.Underline ?? bareUnderline;
					pushStrikethrough = winner.Traits.Strikethrough ?? bareStrikethrough;
				}

				if (pushForeground.HasValue)
					foreground = pushForeground;

				if (pushBold.HasValue)
					isBold = pushBold;

				if (pushItalic.HasValue)
					isItalic = pushItalic;

				if (pushUnderline.HasValue)
					isUnderline = pushUnderline;

				if (pushStrikethrough.HasValue)
					isStrikethrough = pushStrikethrough;
			}

			return new TextMateTokenStyle(
				foreground,
				IsBold: isBold ?? false,
				IsItalic: isItalic ?? false,
				IsUnderline: isUnderline ?? false,
				IsStrikethrough: isStrikethrough ?? false
			);
		}
	}
}
