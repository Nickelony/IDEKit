using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.Highlighting;

/// <summary>
/// An immutable, validated set of <see cref="RegexHighlightingRule"/> rules and
/// <see cref="RegexHighlightingSpan"/> spans that an editor binding converts into its engine's
/// highlighting rule set.
/// </summary>
/// <remarks>
/// Build one through <see cref="Create"/>, which validates every rule, span, and nested-rule pattern
/// before the set is produced. The set keeps the rules and spans in the order they are supplied, because
/// the order decides which rule wins when two rules match at the same position; see <see cref="Create"/>
/// for the validation rules.
/// </remarks>
public sealed class RegexHighlightingRuleSet
{
	private static readonly string[] s_emptyMatchProbes = ["", "x y", "foo bar", "0x0", "a/b"];

	private RegexHighlightingRuleSet(
		string name,
		IReadOnlyList<RegexHighlightingRule> rules,
		IReadOnlyList<RegexHighlightingSpan> spans)
	{
		Name = name;
		Rules = rules;
		Spans = spans;
	}

	/// <summary>
	/// Gets the rule set name.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Gets the rules in the order they were supplied.
	/// </summary>
	public IReadOnlyList<RegexHighlightingRule> Rules { get; }

	/// <summary>
	/// Gets the spans in the order they were supplied.
	/// </summary>
	public IReadOnlyList<RegexHighlightingSpan> Spans { get; }

	/// <summary>
	/// Builds and validates a rule set from the supplied rules and spans.
	/// </summary>
	/// <remarks>
	/// Every rule pattern, every span begin and end pattern, and every nested span rule pattern is
	/// validated before the set is produced; a pattern that violates a rule throws before any part of the
	/// set is built. The order of <paramref name="rules"/> decides which rule wins when two rules match at
	/// the same position, so order the most specific rules first.
	/// </remarks>
	/// <param name="name">The rule set name.</param>
	/// <param name="rules">The rules.</param>
	/// <param name="spans">The spans.</param>
	/// <returns>The validated rule set.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="name"/>, <paramref name="rules"/>, or <paramref name="spans"/> is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// A rule, span, or nested-rule pattern uses <see cref="RegexOptions.RightToLeft"/>, or it can match
	/// empty text.
	/// </exception>
	public static RegexHighlightingRuleSet Create(
		string name,
		IEnumerable<RegexHighlightingRule> rules,
		IEnumerable<RegexHighlightingSpan> spans)
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

	/// <summary>
	/// Validates a rule or span pattern before it enters the rule set.
	/// </summary>
	/// <remarks>
	/// The highlight engine scans each line from left to right and throws when the earliest match at a
	/// scan position is empty, because it cannot advance past a zero-length match, so
	/// <see cref="RegexOptions.RightToLeft"/> patterns and empty-capable patterns are rejected here. A
	/// short probe corpus catches patterns that match empty on common text shapes (anchors, optional
	/// repetitions, empty-capable alternations, lookarounds); the supported subset is patterns that cannot
	/// match empty on any input, and one that matches empty only on other text fails inside the engine
	/// during rendering instead.
	/// </remarks>
	/// <param name="pattern">The rule or span pattern to validate.</param>
	/// <exception cref="InvalidOperationException">
	/// The pattern uses <see cref="RegexOptions.RightToLeft"/>, or it can match empty text.
	/// </exception>
	private static void ValidatePattern(Regex pattern)
	{
		if ((pattern.Options & RegexOptions.RightToLeft) != 0)
		{
			throw new InvalidOperationException(
				"The highlighting pattern must not use RegexOptions.RightToLeft, because the highlight engine scans " +
				$"each line from left to right. Pattern: {pattern}");
		}

		foreach (string probe in s_emptyMatchProbes)
		{
			foreach (ValueMatch match in pattern.EnumerateMatches(probe))
			{
				if (match.Length == 0)
				{
					throw new InvalidOperationException(
						"The highlighting pattern must not match empty text, because the highlight engine cannot " +
						$"advance past a zero-length match. Pattern: {pattern}");
				}
			}
		}
	}
}
