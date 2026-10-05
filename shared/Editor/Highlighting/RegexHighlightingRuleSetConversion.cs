using System.Text.RegularExpressions;
using Nickelony.IDEKit.Core.Highlighting;
#if AVALONIAEDIT
using AvaloniaEdit.Highlighting;
#else
using ICSharpCode.AvalonEdit.Highlighting;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Highlighting;

#else

namespace Nickelony.IDEKit.AvalonEdit.Highlighting;

#endif

/// <summary>Converts the validated neutral rule set of a regex definition to the editor's rule set.</summary>
internal static class RegexHighlightingRuleSetConversion
{
	/// <summary>Converts a validated neutral rule set to the editor's rule set.</summary>
	/// <param name="ruleSet">The neutral rule set to convert.</param>
	/// <returns>The converted editor rule set.</returns>
	internal static HighlightingRuleSet Convert(RegexHighlightingRuleSet ruleSet)
	{
		var highlightingRuleSet = new HighlightingRuleSet { Name = ruleSet.Name };

		foreach (RegexHighlightingRule rule in ruleSet.Rules)
			highlightingRuleSet.Rules.Add(CreateRule(rule));

		foreach (RegexHighlightingSpan span in ruleSet.Spans)
		{
			var highlightingSpan = new HighlightingSpan
			{
				StartExpression = span.Begin,

				// A span without an end pattern stays open to the document end: the editor's span model
				// requires an end expression and the highlight engine dereferences it while coloring, so
				// the null is mapped to a pattern that cannot match anything.
				EndExpression = span.End ?? s_neverMatchingEndExpression,
				SpanColor = span.SpanStyle.ToHighlightingColor(),
				StartColor = span.BeginStyle?.ToHighlightingColor(),
				EndColor = span.EndStyle?.ToHighlightingColor(),

				// Without a dedicated begin or end style, the span color covers the delimiter matches as well.
				SpanColorIncludesStart = span.BeginStyle is null,
				SpanColorIncludesEnd = span.EndStyle is null,
			};

			if (span.Rules is { Count: > 0 } nestedRules)
				highlightingSpan.RuleSet = CreateNestedRuleSet(nestedRules);

			highlightingRuleSet.Spans.Add(highlightingSpan);
		}

		return highlightingRuleSet;
	}

	/// <summary>Creates the editor's highlighting rule from a validated neutral rule.</summary>
	/// <param name="rule">The validated neutral rule to convert.</param>
	/// <returns>The converted editor highlighting rule.</returns>
	private static HighlightingRule CreateRule(RegexHighlightingRule rule) =>
		new() { Regex = rule.Pattern, Color = rule.Style.ToHighlightingColor() };

	/// <summary>Creates the nested editor rule set for a span from its validated neutral rules.</summary>
	/// <param name="rules">The validated neutral rules of the span.</param>
	/// <returns>The converted nested editor rule set.</returns>
	private static HighlightingRuleSet CreateNestedRuleSet(IReadOnlyList<RegexHighlightingRule> rules)
	{
		var ruleSet = new HighlightingRuleSet();

		foreach (RegexHighlightingRule rule in rules)
			ruleSet.Rules.Add(CreateRule(rule));

		return ruleSet;
	}

	/// <summary>
	/// The end expression mapped onto a span that has none: a pattern that cannot match any text, so the span stays open to the document end.
	/// </summary>
	/// <remarks>
	/// The editor's highlight engine requires a non-<see langword="null"/> end expression, so the <see langword="null"/>
	/// from <see cref="RegexHighlightingSpan.End"/> never reaches it. The pattern cannot backtrack, so the finite bound is generous and never fires;
	/// passing one keeps the "bound every highlighting regex" rule uniform with the rules hosts supply.
	/// </remarks>
	private static readonly Regex s_neverMatchingEndExpression = new(@"(?!)", RegexOptions.None, TimeSpan.FromSeconds(1));
}
