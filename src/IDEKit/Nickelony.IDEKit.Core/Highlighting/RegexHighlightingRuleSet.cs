using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.Highlighting;

/// <summary>
/// An immutable, validated set of <see cref="RegexHighlightingRule"/> rules and <see cref="RegexHighlightingSpan"/> spans that an editor binding
/// converts into its engine's highlighting rule set.
/// </summary>
/// <remarks>
/// Build one through <see cref="Create"/>, which validates each rule, span, and nested-rule pattern before the set is produced. The rules and spans
/// keep the order they are supplied in, so the earlier rule wins when two rules match at the same position.
/// </remarks>
public sealed class RegexHighlightingRuleSet
{
	private static readonly string[] s_emptyMatchProbes = ["", "x y", "foo bar", "0x0", "a/b"];

	private RegexHighlightingRuleSet(string name, IReadOnlyList<RegexHighlightingRule> rules, IReadOnlyList<RegexHighlightingSpan> spans)
	{
		Name = name;
		Rules = rules;
		Spans = spans;
	}

	public string Name { get; }

	public IReadOnlyList<RegexHighlightingRule> Rules { get; }

	public IReadOnlyList<RegexHighlightingSpan> Spans { get; }

	/// <summary>Builds and validates a rule set.</summary>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="name"/>, <paramref name="rules"/>, or <paramref name="spans"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// A rule, span, or nested-rule pattern uses <see cref="RegexOptions.RightToLeft"/>, or it matches empty text on a probe string.
	/// </exception>
	/// <remarks>
	/// Each rule pattern, each span begin and end pattern, and each nested span rule pattern is validated before the set is built.
	/// The empty-match check runs a short fixed list of probe strings, so a pattern that matches empty only on other text passes and fails
	/// inside the engine during rendering. Order the most specific rules first, as the remarks on the type explain.
	/// </remarks>
	public static RegexHighlightingRuleSet Create(string name, IEnumerable<RegexHighlightingRule> rules, IEnumerable<RegexHighlightingSpan> spans)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(rules);
		ArgumentNullException.ThrowIfNull(spans);

		var materializedRules = new List<RegexHighlightingRule>();

		foreach (RegexHighlightingRule rule in rules)
		{
			ValidatePattern(rule.Pattern);
			materializedRules.Add(rule);
		}

		var materializedSpans = new List<RegexHighlightingSpan>();

		foreach (RegexHighlightingSpan span in spans)
		{
			ValidatePattern(span.Begin);

			if (span.End is not null)
				ValidatePattern(span.End);

			if (span.Rules is { Count: > 0 } nestedRules)
			{
				foreach (RegexHighlightingRule nestedRule in nestedRules)
					ValidatePattern(nestedRule.Pattern);
			}

			materializedSpans.Add(span);
		}

		return new RegexHighlightingRuleSet(name, materializedRules, materializedSpans);
	}

	/// <summary>Validates a rule or span pattern before it enters the rule set.</summary>
	/// <param name="pattern">The rule or span pattern to validate.</param>
	/// <exception cref="InvalidOperationException">
	/// The pattern uses <see cref="RegexOptions.RightToLeft"/>, or it matches empty text on a probe string.
	/// </exception>
	/// <remarks>
	/// The highlight engine scans each line left to right and cannot advance past a zero-length match,
	/// so <see cref="RegexOptions.RightToLeft"/> and empty-capable patterns are rejected.
	/// </remarks>
	private static void ValidatePattern(Regex pattern)
	{
		if ((pattern.Options & RegexOptions.RightToLeft) != 0)
		{
			throw new InvalidOperationException(
				"The highlighting pattern must not use RegexOptions.RightToLeft, because the highlight engine scans "
					+ $"each line from left to right. Pattern: {pattern}"
			);
		}

		foreach (string probe in s_emptyMatchProbes)
		{
			foreach (ValueMatch match in pattern.EnumerateMatches(probe))
			{
				if (match.Length == 0)
				{
					throw new InvalidOperationException(
						"The highlighting pattern must not match empty text, because the highlight engine cannot "
							+ $"advance past a zero-length match. Pattern: {pattern}"
					);
				}
			}
		}
	}
}
