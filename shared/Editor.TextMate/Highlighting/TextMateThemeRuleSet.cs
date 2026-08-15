using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
#if AVALONIAEDIT
using Avalonia.Media;
using Brush = Avalonia.Media.IBrush;
#else
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
#endif
using static Nickelony.IDEKit.Infrastructure.BrushHelpers;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Highlighting;
#else
namespace Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
#endif

/// <summary>
/// The parsed, validated, and ranked rules of a <see cref="TextMateTokenTheme"/>, together with the
/// theme's defaults.
/// </summary>
/// <remarks>
/// <para>
/// Rules are parsed once per resolver. Each comma-separated selector becomes its own rule, repeated
/// selectors merge with later values overwriting earlier ones for each property the later rule sets,
/// and the rules are sorted into the order VS Code's theme parser uses, which also breaks specificity
/// ties at match time. Selectors that use an operator theme selectors do not support can never match,
/// so they are reported and dropped while parsing instead of being re-checked on every resolution.
/// </para>
/// <para>
/// A rule with a blank or whitespace-only scope - or a null scope from deserialized data - provides
/// the theme defaults that apply to every token, which is how VS Code theme files carry their base
/// foreground and font style; later values overwrite earlier ones for each property. A selector whose
/// parts are separated by repeated spaces keeps an empty parent part, exactly as vscode-textmate's
/// theme parser splits selectors, so such a malformed selector never matches instead of being
/// collapsed into a wider match. A blank comma-separated part is skipped. Unparseable
/// colors, unsupported selectors, misplaced child combinators, unrecognized font style traits, and
/// null rules are ignored and reported as warnings through the logger.
/// </para>
/// </remarks>
internal sealed partial class TextMateThemeRuleSet
{
	// The 3000 block belongs to this package; the event ids are documented in the README and pinned
	// by the logging tests.
	[LoggerMessage(
		EventId = 3000,
		EventName = "InvalidForegroundColor",
		Level = LogLevel.Warning,
		Message = "Invalid foreground color '{Color}' in TextMate theme rule; the foreground is ignored."
	)]
	private static partial void LogInvalidForegroundColor(ILogger logger, string color, Exception? exception);

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

	private TextMateThemeRuleSet(
		List<TextMateThemeRule> rules,
		Brush? defaultForeground,
		TextMateFontTraits defaultTraits)
	{
		Rules = rules;
		DefaultForeground = defaultForeground;
		DefaultTraits = defaultTraits;
	}

	/// <summary>
	/// Gets the ranked rules in the order VS Code's theme parser sorts them in.
	/// </summary>
	internal IReadOnlyList<TextMateThemeRule> Rules { get; }

	/// <summary>
	/// Gets the theme's default foreground, or <see langword="null"/> when no blank-scope rule sets one.
	/// </summary>
	internal Brush? DefaultForeground { get; }

	/// <summary>
	/// Gets the theme's default font traits; an unset trait stays <see langword="null"/>.
	/// </summary>
	internal TextMateFontTraits DefaultTraits { get; }

	/// <summary>
	/// Parses and ranks the rules of a token theme.
	/// </summary>
	/// <param name="theme">The token theme whose rules drive style resolution.</param>
	/// <param name="logger">
	/// The logger that receives the warnings for malformed theme data, or <see langword="null"/> to
	/// discard them.
	/// </param>
	/// <returns>The parsed rule set and the theme defaults.</returns>
	internal static TextMateThemeRuleSet Create(TextMateTokenTheme theme, ILogger? logger)
	{
		ILogger effectiveLogger = logger ?? NullLogger.Instance;
		var rules = new List<TextMateThemeRule>();
		Dictionary<string, int> ruleIndexBySelector = new(StringComparer.Ordinal);
		Brush? defaultForeground = null;
		TextMateFontTraits defaultTraits = default;

		for (int i = 0; i < theme.Rules.Count; i++)
		{
			TextMateTokenThemeRule rawRule = theme.Rules[i];

			if (rawRule is null)
			{
				LogNullThemeRule(effectiveLogger, i);
				continue;
			}

			// A null scope can arrive from deserialized theme data even though the property is not
			// annotated as nullable; treating it as blank makes the rule a defaults block, which is how
			// theme files carry a missing scope, instead of failing the resolver.
			string scope = rawRule.Scope ?? string.Empty;
			string[] selectors = scope.Split(',');
			string? foregroundValue = NormalizeThemeColor(rawRule.Foreground);
			Brush? foreground = null;

			if (!string.IsNullOrWhiteSpace(foregroundValue))
			{
				try
				{
					foreground = CreateFrozenBrush(foregroundValue);
				}
				catch (ArgumentException exception)
				{
					LogInvalidForegroundColor(effectiveLogger, rawRule.Foreground, exception);
					foreground = null;
				}
			}

			ParseFontStyle(rawRule.FontStyle, effectiveLogger, out TextMateFontTraits fontTraits);

			if (string.IsNullOrWhiteSpace(scope))
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
					Strikethrough = fontTraits.Strikethrough ?? defaultTraits.Strikethrough
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

				string[] parentScopes = Array.Empty<string>();

				if (parts.Length > 1)
				{
					parentScopes = new string[parts.Length - 1];

					// Parent parts are stored deepest-first, the order the ranking compares them in.
					for (int partIndex = 0; partIndex < parentScopes.Length; partIndex++)
						parentScopes[partIndex] = parts[parts.Length - 2 - partIndex];
				}

				ruleIndexBySelector.Add(selectorKey, rules.Count);

				rules.Add(new TextMateThemeRule(
					parts,
					parentScopes,
					rules.Count,
					foreground,
					fontTraits));
			}
		}

		return new TextMateThemeRuleSet(SortRules(rules), defaultForeground, defaultTraits);
	}

	/// <summary>
	/// Sorts the rules into the order VS Code's theme parser uses and re-indexes them, so both the
	/// specificity tie-break and the duplicate-selector merge follow that order.
	/// </summary>
	/// <param name="rules">The parsed rules.</param>
	/// <returns>The sorted rules with their final sequence indices.</returns>
	private static List<TextMateThemeRule> SortRules(List<TextMateThemeRule> rules)
	{
		if (rules.Count < 2)
			return rules;

		rules.Sort(TextMateThemeRule.CompareByParseOrder);

		for (int i = 0; i < rules.Count; i++)
			rules[i] = rules[i].WithSequenceIndex(i);

		return rules;
	}

	/// <summary>
	/// Merges a rule that repeats an already parsed selector into the existing rule: the later values
	/// overwrite the earlier ones for each property the later rule sets.
	/// </summary>
	/// <param name="existing">The rule parsed from the first occurrence of the selector.</param>
	/// <param name="foreground">The foreground of the later rule, or <see langword="null"/> when it sets none.</param>
	/// <param name="traits">The font traits of the later rule.</param>
	/// <returns>The merged rule.</returns>
	private static TextMateThemeRule MergeRules(TextMateThemeRule existing, Brush? foreground, TextMateFontTraits traits)
		=> new(
			existing.SelectorParts,
			existing.ParentScopes,
			existing.SequenceIndex,
			foreground ?? existing.Foreground,
			new TextMateFontTraits(
				traits.Bold ?? existing.Traits.Bold,
				traits.Italic ?? existing.Traits.Italic,
				traits.Underline ?? existing.Traits.Underline,
				traits.Strikethrough ?? existing.Traits.Strikethrough));

	/// <summary>
	/// Parses a <c>fontStyle</c> value into font traits.
	/// </summary>
	/// <param name="fontStyleValue">
	/// The space-separated trait list, or <see langword="null"/> when the rule does not set the field.
	/// </param>
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

		traits = new TextMateFontTraits(isBold, isItalic, isUnderline, isStrikethrough);
	}

	/// <summary>
	/// Normalizes a TextMate theme color value so the toolkit's color parser reads it correctly. TextMate
	/// data spells eight-digit colors as <c>#RRGGBBAA</c> and four-digit colors as <c>#RGBA</c>, while the
	/// toolkit spells them as <c>#AARRGGBB</c> and <c>#ARGB</c>.
	/// </summary>
	/// <remarks>
	/// An eight-digit or four-digit value is always treated as the TextMate spelling, so a value in the
	/// toolkit order (alpha component first) is not recognized and is read in the other order instead. The
	/// six-digit and three-digit forms carry no alpha component and are returned unchanged.
	/// </remarks>
	/// <param name="colorValue">The color value to normalize.</param>
	/// <returns>The normalized value, or the original value when it uses another format.</returns>
	private static string? NormalizeThemeColor(string? colorValue)
	{
		if (string.IsNullOrWhiteSpace(colorValue))
			return colorValue;

		string trimmed = colorValue.Trim();

		if (trimmed.Length == 9 && trimmed[0] == '#' && IsHexDigits(trimmed.AsSpan(1)))
			return string.Concat("#", trimmed.AsSpan(7, 2), trimmed.AsSpan(1, 6));

		if (trimmed.Length == 5 && trimmed[0] == '#' && IsHexDigits(trimmed.AsSpan(1)))
		{
			// #RGBA moves the alpha digit to the front and doubles every digit.
			return $"#{trimmed[4]}{trimmed[4]}{trimmed[1]}{trimmed[1]}{trimmed[2]}{trimmed[2]}{trimmed[3]}{trimmed[3]}";
		}

		return colorValue;
	}

	/// <summary>
	/// Determines whether every character of the value is an ASCII hexadecimal digit; an empty value
	/// qualifies.
	/// </summary>
	private static bool IsHexDigits(ReadOnlySpan<char> value)
	{
		for (int i = 0; i < value.Length; i++)
		{
			char character = value[i];

			if (!char.IsAsciiHexDigit(character))
				return false;
		}

		return true;
	}

	/// <summary>
	/// Determines whether the selector parts place a child combinator anywhere other than between two
	/// scope names.
	/// </summary>
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
			if (part != ">"
				|| i == 0
				|| i == parts.Length - 1
				|| parts[i - 1] == ">"
				|| parts[i + 1] == ">")
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Determines whether the selector parts contain an operator that theme selectors do not support.
	/// </summary>
	/// <param name="parts">The selector parts, ordered from the outermost part to the anchored part.</param>
	/// <returns><see langword="true"/> when an unsupported operator is present.</returns>
	private static bool HasUnsupportedOperator(string[] parts)
	{
		for (int i = 0; i < parts.Length; i++)
		{
			string part = parts[i];

			if (part.Length == 0)
				continue;

			// The exclusion, wildcard, and parenthesised group operators can appear in any position
			// of a selector part, while the exclusion operator only leads one. They belong to
			// injection selectors; VS Code theme selectors never match them.
			if (part[0] == '-'
				|| part.Contains('*')
				|| part.Contains('(')
				|| part.Contains(')')
				|| part.StartsWith("L:", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}
}
