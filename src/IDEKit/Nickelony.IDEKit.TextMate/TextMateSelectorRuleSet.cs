using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.Core.Media;

namespace Nickelony.IDEKit.TextMate;

/// <summary>The parsed, validated, and ranked selector rules of a <see cref="TextMateTokenTheme"/>, together with the theme's defaults.</summary>
/// <remarks>
/// <para>
/// Rules are parsed once per resolver. Each comma-separated selector becomes its own rule, repeated selectors merge with later values overwriting
/// earlier ones for each property the later rule sets, and the rules are sorted into the order VS Code's theme parser uses, which also breaks
/// specificity ties at match time. Selectors that use an operator theme selectors do not support can never match, so they are reported and dropped
/// while parsing instead of being re-checked on every resolution.
/// </para>
/// <para>
/// A rule with a <see langword="null"/> scope from deserialized data, or one that is blank or whitespace-only
/// once its leading and trailing commas are trimmed, provides the theme defaults that apply to every token, which is how VS Code theme
/// files carry their base foreground and font style. Later values overwrite earlier ones for each property. A selector whose parts are separated
/// by repeated spaces keeps an empty parent part, exactly as vscode-textmate's theme parser splits selectors, so such a malformed selector
/// never matches instead of being collapsed into a wider match. A blank comma-separated part is skipped. Unparseable colors, unsupported selectors,
/// misplaced child combinators, unrecognized font style traits, and <see langword="null"/> rules are ignored and reported as warnings
/// through the logger.
/// </para>
/// </remarks>
internal sealed partial class TextMateSelectorRuleSet
{
	// The 3000 block belongs to the TextMate package.
	[LoggerMessage(
		EventId = 3000,
		EventName = "InvalidForegroundColor",
		Level = LogLevel.Warning,
		Message = "Invalid foreground color '{Color}' in TextMate theme rule; the foreground is ignored."
	)]
	private static partial void LogInvalidForegroundColor(ILogger logger, string color);

	[LoggerMessage(
		EventId = 3001,
		EventName = "UnsupportedSelector",
		Level = LogLevel.Warning,
		Message = "TextMate theme selector '{Selector}' uses an unsupported selector operator and will never match."
	)]
	private static partial void LogUnsupportedSelector(ILogger logger, string selector);

	[LoggerMessage(
		EventId = 3002,
		EventName = "UnknownFontStyleTrait",
		Level = LogLevel.Warning,
		Message = "Unrecognized font style trait '{Trait}' in a TextMate theme rule; the trait is ignored."
	)]
	private static partial void LogUnknownFontStyleTrait(ILogger logger, string trait);

	[LoggerMessage(
		EventId = 3003,
		EventName = "MisplacedChildCombinator",
		Level = LogLevel.Warning,
		Message = "TextMate theme selector '{Selector}' uses a child combinator that is not a standalone part between two scope names; the selector never matches."
	)]
	private static partial void LogMisplacedChildCombinator(ILogger logger, string selector);

	[LoggerMessage(
		EventId = 3004,
		EventName = "NullThemeRule",
		Level = LogLevel.Warning,
		Message = "The theme rule at index {Index} is null and is ignored."
	)]
	private static partial void LogNullThemeRule(ILogger logger, int index);

	// The distinct rightmost selector parts of Rules in ordinal order, and where the rules of each begin in Rules. Rules is sorted by that part
	// first, so the rules that share one are adjacent; _bucketStarts has one more entry than _anchorParts, holding the end of the last bucket.
	private readonly string[] _anchorParts;
	private readonly int[] _bucketStarts;

	private TextMateSelectorRuleSet(List<TextMateSelectorRule> rules, ArgbColor? defaultForeground, TextMateFontTraits defaultTraits)
	{
		Rules = rules;
		DefaultForeground = defaultForeground;
		DefaultTraits = defaultTraits;

		var anchorParts = new List<string>();
		var bucketStarts = new List<int>();

		for (int index = 0; index < rules.Count; index++)
		{
			string anchorPart = rules[index].SelectorParts[^1];

			if (anchorParts.Count > 0 && string.Equals(anchorParts[^1], anchorPart, StringComparison.Ordinal))
				continue;

			anchorParts.Add(anchorPart);
			bucketStarts.Add(index);
		}

		bucketStarts.Add(rules.Count);

		_anchorParts = [.. anchorParts];
		_bucketStarts = [.. bucketStarts];
	}

	/// <summary>The ranked rules in the order VS Code's theme parser sorts them.</summary>
	internal IReadOnlyList<TextMateSelectorRule> Rules { get; }

	/// <summary>The theme's default foreground, or <see langword="null"/> when no blank-scope rule sets one.</summary>
	internal ArgbColor? DefaultForeground { get; }

	/// <summary>The theme's default font traits; an unset trait stays <see langword="null"/>.</summary>
	internal TextMateFontTraits DefaultTraits { get; }

	/// <summary>Adds the rules that match the scope at an index of a token's scope stack as their innermost scope.</summary>
	/// <param name="scopes">The token scopes, ordered from the root scope to the innermost scope.</param>
	/// <param name="anchorIndex">The index of the scope that acts as the innermost scope.</param>
	/// <param name="parentScoped">Receives the matching rules that have parent selector parts.</param>
	/// <param name="bare">Receives the matching rules that have none.</param>
	/// <remarks>
	/// A rule matches only when its rightmost selector part is the scope or a prefix of it that ends before a dot, so only the buckets
	/// of those parts are tested, not the whole rule list. The rules are added in rule order within each bucket, which is not the precedence order.
	/// </remarks>
	internal void CollectMatches(
		IReadOnlyList<string> scopes,
		int anchorIndex,
		List<TextMateSelectorRule> parentScoped,
		List<TextMateSelectorRule> bare
	)
	{
		string scope = scopes[anchorIndex];

		for (int dotIndex = scope.IndexOf('.'); dotIndex >= 0; dotIndex = scope.IndexOf('.', dotIndex + 1))
			CollectBucket(anchorPart: scope.AsSpan(0, dotIndex), scopes, anchorIndex, parentScoped, bare);

		CollectBucket(anchorPart: scope, scopes, anchorIndex, parentScoped, bare);
	}

	private void CollectBucket(
		ReadOnlySpan<char> anchorPart,
		IReadOnlyList<string> scopes,
		int anchorIndex,
		List<TextMateSelectorRule> parentScoped,
		List<TextMateSelectorRule> bare
	)
	{
		int low = 0;
		int high = _anchorParts.Length - 1;

		while (low <= high)
		{
			int middle = low + ((high - low) / 2);
			int comparison = _anchorParts[middle].AsSpan().SequenceCompareTo(anchorPart);

			if (comparison < 0)
			{
				low = middle + 1;
				continue;
			}

			if (comparison > 0)
			{
				high = middle - 1;
				continue;
			}

			for (int index = _bucketStarts[middle]; index < _bucketStarts[middle + 1]; index++)
			{
				TextMateSelectorRule rule = Rules[index];

				if (!rule.MatchesAnchor(scopes, anchorIndex))
					continue;

				if (rule.HasParentScopes)
					parentScoped.Add(rule);
				else
					bare.Add(rule);
			}

			return;
		}
	}

	/// <summary>Parses and ranks the rules of a token theme.</summary>
	/// <param name="theme">The token theme whose rules drive style resolution.</param>
	/// <param name="logger">The logger that receives the warnings for malformed theme data, or <see langword="null"/> to discard them.</param>
	/// <returns>The parsed rule set and the theme defaults.</returns>
	internal static TextMateSelectorRuleSet Create(TextMateTokenTheme theme, ILogger? logger)
	{
		ILogger effectiveLogger = logger ?? NullLogger.Instance;

		var rules = new List<TextMateSelectorRule>();

		Dictionary<string, int> ruleIndexBySelector = new(StringComparer.Ordinal);
		ArgbColor? defaultForeground = null;
		TextMateFontTraits defaultTraits = default;

		for (int i = 0; i < theme.Rules.Count; i++)
		{
			TextMateTokenThemeRule rawRule = theme.Rules[i];

			if (rawRule is null)
			{
				LogNullThemeRule(effectiveLogger, i);
				continue;
			}

			// A null scope can arrive from deserialized theme data, and
			// treating it as blank makes the rule a defaults block, which is how
			// theme files carry a missing scope, instead of failing the resolver.
			string scope = rawRule.Scope ?? string.Empty;

			// Matching vscode-textmate, leading and trailing commas are trimmed before the blank test, so a
			// scope that is only separators (for example ",") is treated as the defaults block instead of
			// being dropped as a blank selector.
			string trimmedScope = scope.Trim(',');
			string[] selectors = trimmedScope.Split(',');
			ArgbColor? foreground = null;

			if (!string.IsNullOrWhiteSpace(rawRule.Foreground))
			{
				if (TextMateColorParser.TryParse(rawRule.Foreground, out ArgbColor parsedForeground))
					foreground = parsedForeground;
				else
					LogInvalidForegroundColor(effectiveLogger, rawRule.Foreground);
			}

			ParseFontStyle(rawRule.FontStyle, effectiveLogger, out TextMateFontTraits fontTraits);

			if (string.IsNullOrWhiteSpace(trimmedScope))
			{
				// A rule without a scope is the theme's defaults block, which VS Code theme files use to
				// carry their base foreground and font style; later values win for each property.
				if (foreground is not null)
					defaultForeground = foreground;

				defaultTraits = defaultTraits with
				{
					Bold = fontTraits.Bold ?? defaultTraits.Bold,
					Italic = fontTraits.Italic ?? defaultTraits.Italic,
					Underline = fontTraits.Underline ?? defaultTraits.Underline,
					Strikethrough = fontTraits.Strikethrough ?? defaultTraits.Strikethrough,
				};

				continue;
			}

			// Each comma-separated selector becomes its own rule so it carries its own specificity data.
			// Selectors that use an unsupported operator can never match, so they are reported once here
			// and dropped instead of being re-checked on every resolution.
			for (int selectorIndex = 0; selectorIndex < selectors.Length; selectorIndex++)
			{
				string selector = selectors[selectorIndex].Trim();

				// The selector is split on single spaces without collapsing repeats, matching the theme
				// parser: a repeated space therefore keeps an empty parent part, and an empty part never
				// matches a scope, so such a selector never matches. Collapsing the repeats would instead let
				// the malformed selector match a scope path it was not written for.
				string[] parts = selector.Split(' ');

				// A blank comma-separated part carries no scope and is skipped; a rule whose whole scope is
				// blank was already handled above as the defaults block.
				if (parts.Length == 1 && parts[0].Length == 0)
					continue;

				if (HasMisplacedChildCombinator(parts))
				{
					LogMisplacedChildCombinator(effectiveLogger, selector);
					continue;
				}

				if (HasUnsupportedOperator(parts))
				{
					LogUnsupportedSelector(effectiveLogger, selector);
					continue;
				}

				string selectorKey = string.Join(' ', parts);

				if (ruleIndexBySelector.TryGetValue(selectorKey, out int existingRuleIndex))
				{
					// Rules that repeat the same selector merge, and later values overwrite earlier ones
					// for each property the later rule sets, the way VS Code merges duplicate selectors.
					rules[existingRuleIndex] = MergeRules(rules[existingRuleIndex], foreground, fontTraits);
					continue;
				}

				string[] parentScopes = [];

				if (parts.Length > 1)
				{
					parentScopes = new string[parts.Length - 1];

					// Parent parts are stored deepest-first, the order the ranking compares them in.
					for (int partIndex = 0; partIndex < parentScopes.Length; partIndex++)
						parentScopes[partIndex] = parts[parts.Length - 2 - partIndex];
				}

				ruleIndexBySelector.Add(selectorKey, rules.Count);

				rules.Add(new TextMateSelectorRule(selectorParts: parts, parentScopes, sequenceIndex: rules.Count, foreground, traits: fontTraits));
			}
		}

		return new TextMateSelectorRuleSet(SortRules(rules), defaultForeground, defaultTraits);
	}

	/// <summary>
	/// Sorts the rules into the order VS Code's theme parser uses and re-indexes them, so the specificity tie-break follows that order.
	/// </summary>
	/// <param name="rules">The parsed rules.</param>
	/// <returns>The sorted rules with their final sequence indices.</returns>
	private static List<TextMateSelectorRule> SortRules(List<TextMateSelectorRule> rules)
	{
		if (rules.Count < 2)
			return rules;

		rules.Sort(TextMateSelectorRule.CompareByParseOrder);

		for (int i = 0; i < rules.Count; i++)
			rules[i] = rules[i].WithSequenceIndex(i);

		return rules;
	}

	/// <summary>
	/// Merges a rule that repeats an already parsed selector into the existing rule: the later values overwrite the earlier ones for each property
	/// the later rule sets.
	/// </summary>
	/// <param name="existing">The rule parsed from the first occurrence of the selector.</param>
	/// <param name="foreground">The foreground of the later rule, or <see langword="null"/> when it sets none.</param>
	/// <param name="traits">The font traits of the later rule.</param>
	/// <returns>The merged rule.</returns>
	private static TextMateSelectorRule MergeRules(TextMateSelectorRule existing, ArgbColor? foreground, TextMateFontTraits traits)
	{
		return new(
			selectorParts: existing.SelectorParts,
			parentScopes: existing.ParentScopes,
			sequenceIndex: existing.SequenceIndex,
			foreground: foreground ?? existing.Foreground,

			traits: new TextMateFontTraits(
				Bold: traits.Bold ?? existing.Traits.Bold,
				Italic: traits.Italic ?? existing.Traits.Italic,
				Underline: traits.Underline ?? existing.Traits.Underline,
				Strikethrough: traits.Strikethrough ?? existing.Traits.Strikethrough
			)
		);
	}

	/// <summary>Parses a <c>fontStyle</c> value into font traits.</summary>
	/// <param name="fontStyleValue">The space-separated trait list, or <see langword="null"/> when the rule does not set the field.</param>
	/// <param name="logger">The logger that receives a warning for each unrecognized trait.</param>
	/// <param name="traits">The parsed traits.</param>
	private static void ParseFontStyle(string? fontStyleValue, ILogger logger, out TextMateFontTraits traits)
	{
		// An absent font-style value leaves the traits unset, so inherited formatting is kept. Any
		// present value is an explicit reset first: TextMate theme data uses an empty string to clear
		// inherited traits, and the recognized trait names then enable the individual traits.
		if (fontStyleValue is null)
		{
			traits = default;
			return;
		}

		bool isBold = false;
		bool isItalic = false;
		bool isUnderline = false;
		bool isStrikethrough = false;

		string[] parts = fontStyleValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		for (int i = 0; i < parts.Length; i++)
		{
			string trait = parts[i].Trim();

			switch (trait)
			{
				case "bold":
					isBold = true;
					break;

				case "italic":
					isItalic = true;
					break;

				case "underline":
					isUnderline = true;
					break;

				case "strikethrough":
					isStrikethrough = true;
					break;

				case "none":
					// The reset already happened; the keyword is accepted as an explicit spelling of it.
					break;

				default:
					LogUnknownFontStyleTrait(logger, trait);
					break;
			}
		}

		traits = new TextMateFontTraits(Bold: isBold, Italic: isItalic, Underline: isUnderline, Strikethrough: isStrikethrough);
	}

	/// <summary>Determines whether the selector parts place a child combinator anywhere other than between two scope names.</summary>
	/// <param name="parts">The selector parts, ordered from the outermost part to the anchored part.</param>
	/// <returns><see langword="true"/> when a child combinator is misplaced.</returns>
	private static bool HasMisplacedChildCombinator(string[] parts)
	{
		for (int i = 0; i < parts.Length; i++)
		{
			string part = parts[i];

			if (!part.Contains('>'))
				continue;

			// A child combinator must be a standalone part with a scope name on each side.
			if (part != ">" || i == 0 || i == parts.Length - 1 || parts[i - 1] == ">" || parts[i + 1] == ">")
				return true;
		}

		return false;
	}

	/// <summary>Determines whether the selector parts contain an operator that theme selectors do not support.</summary>
	/// <param name="parts">The selector parts, ordered from the outermost part to the anchored part.</param>
	/// <returns><see langword="true"/> when an unsupported operator is present.</returns>
	private static bool HasUnsupportedOperator(string[] parts)
	{
		for (int i = 0; i < parts.Length; i++)
		{
			string part = parts[i];

			if (part.Length == 0)
				continue;

			// The wildcard and parenthesized group operators can appear in any position of a selector
			// part, while the exclusion and L: priority operators only lead one. They belong to
			// injection selectors; VS Code theme selectors never match them.
			if (
				part[0] == '-'
				|| part.Contains('*')
				|| part.Contains('(')
				|| part.Contains(')')
				|| part.StartsWith("L:", StringComparison.OrdinalIgnoreCase)
			)
			{
				return true;
			}
		}

		return false;
	}
}
