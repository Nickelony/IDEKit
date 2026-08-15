using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.Highlighting;

/// <summary>
/// Represents a delimiter-based highlighting span (for example a block comment or a long string) that is
/// highlighted from a begin match to an end match.
/// </summary>
/// <remarks>
/// <para>
/// The span covers the text from the begin match through the end match and stays highlighted on the
/// following lines until the end matches, so it can express multiline constructs that a per-line rule
/// cannot. A span whose <see cref="End"/> is <see langword="null"/> stays open to the end of the
/// document: the editor binding maps the null to an end expression that cannot match anything, because
/// its span model requires an end expression and its highlight engine dereferences it while coloring.
/// <see cref="EndStyle"/> does not apply to such a span, because it has no end match to style.
/// </para>
/// <para>
/// <see cref="SpanStyle"/> colors the whole span, including the delimiter matches unless
/// <see cref="BeginStyle"/> or <see cref="EndStyle"/> override them for their own matches.
/// </para>
/// <para>
/// While the span is open, the engine evaluates only the rule set the span carries: the definition's
/// per-line rules do not apply inside the span, and a second begin match inside an open span is plain
/// text because spans do not nest. Supply <see cref="Rules"/> when the span content needs its own
/// styling (for example keywords inside a comment). The begin and end patterns must never be able to
/// match empty text; <see cref="RegexHighlightingRuleSet"/> states the pattern rules and how a pattern
/// that violates them is rejected.
/// </para>
/// </remarks>
public sealed class RegexHighlightingSpan
{
	/// <summary>
	/// Initializes a new instance of the <see cref="RegexHighlightingSpan"/> class.
	/// </summary>
	/// <param name="begin">The regular expression that starts the span.</param>
	/// <param name="end">The regular expression that closes the span, or <see langword="null"/> for a span that stays open to the document end.</param>
	/// <param name="spanStyle">The style applied to the whole span.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="begin"/> or <paramref name="spanStyle"/> is <see langword="null"/>.
	/// </exception>
	public RegexHighlightingSpan(
		Regex begin,
		Regex? end,
		RegexHighlightingStyle spanStyle)
	{
		ArgumentNullException.ThrowIfNull(begin);
		ArgumentNullException.ThrowIfNull(spanStyle);

		Begin = begin;
		End = end;
		SpanStyle = spanStyle;
	}

	/// <summary>
	/// Gets the regular expression that starts the span.
	/// </summary>
	public Regex Begin { get; }

	/// <summary>
	/// Gets the regular expression that closes the span, or <see langword="null"/> for a span that stays
	/// open to the document end. The editor binding implements the open span with an end expression that
	/// cannot match.
	/// </summary>
	public Regex? End { get; }

	/// <summary>
	/// Gets the style applied to the whole span.
	/// </summary>
	public RegexHighlightingStyle SpanStyle { get; }

	/// <summary>
	/// Gets the style applied to the begin match instead of the span style, when supplied.
	/// </summary>
	public RegexHighlightingStyle? BeginStyle { get; init; }

	/// <summary>
	/// Gets the style applied to the end match instead of the span style, when supplied. It is ignored by
	/// a span that stays open to the document end, because such a span has no end match.
	/// </summary>
	public RegexHighlightingStyle? EndStyle { get; init; }

	/// <summary>
	/// Gets the rules that apply inside the span, or <see langword="null"/> when the span carries no
	/// nested rules and its content stays plain.
	/// </summary>
	/// <remarks>
	/// The binding maps the rules onto the span's nested rule set, which the engine evaluates while the
	/// span is open. Each rule's pattern is validated like a main-set rule; see
	/// <see cref="RegexHighlightingRule"/>. Without nested rules the span content is styled only by the
	/// span colors, and the definition's per-line rules do not apply inside it.
	/// </remarks>
	public IReadOnlyList<RegexHighlightingRule>? Rules { get; init; }
}
