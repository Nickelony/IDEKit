#if AVALONIAEDIT
using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using Nickelony.IDEKit.AvaloniaEdit.Highlighting;
using Nickelony.IDEKit.Core.Highlighting;
using Nickelony.IDEKit.Core.Media;
using FontStyles = Avalonia.Media.FontStyle;
using FontWeights = Avalonia.Media.FontWeight;

namespace Nickelony.IDEKit.AvaloniaEdit.Tests;

#else
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using Nickelony.IDEKit.AvalonEdit.Highlighting;
using Nickelony.IDEKit.Core.Highlighting;
using Nickelony.IDEKit.Core.Media;
using FontStyles = System.Windows.FontStyles;
using FontWeights = System.Windows.FontWeights;

namespace Nickelony.IDEKit.AvalonEdit.Tests;

#endif

[TestClass]
public sealed partial class RegexHighlightingDefinitionTests
{
	private sealed class TestDefinition : RegexHighlightingDefinition
	{
		private readonly Func<IEnumerable<RegexHighlightingRule>> _buildRules;

		public TestDefinition(string name, Func<IEnumerable<RegexHighlightingRule>> buildRules, Func<int>? cacheVersion = null)
			: base(name, new RegexHighlightingDefinitionOptions(cacheVersion))
		{
			_buildRules = buildRules;
		}

		protected override IEnumerable<RegexHighlightingRule> BuildRules() => _buildRules();
	}

	private static RegexHighlightingRule Rule(string pattern, ArgbColor? foreground = null, bool isBold = false, bool isItalic = false) =>
		new(new Regex(pattern), new RegexHighlightingStyle(foreground, isBold, isItalic));

	[TestMethod]
	public void MainRuleSet_AppliesRegexAndStyle()
	{
		var definition = new TestDefinition(
			"Test Rules",

			() =>
				[
					new RegexHighlightingRule(
						SemicolonCommentRegex(),
						new RegexHighlightingStyle(ArgbColor.FromRgb(0xFF, 0x00, 0x00), IsBold: true, IsItalic: true)
					),
				]
		);

		HighlightingRule rule = definition.MainRuleSet.Rules.Single();

		Assert.IsTrue(rule.Regex.IsMatch("; comment"));
		Assert.AreEqual(Colors.Red, rule.Color.Foreground.GetColor(null!));
		Assert.AreEqual(FontWeights.Bold, rule.Color.FontWeight);
		Assert.AreEqual(FontStyles.Italic, rule.Color.FontStyle);
	}

	[TestMethod]
	public void MainRuleSet_MissingColor_LeavesForegroundUnset()
	{
		var definition = new TestDefinition("Test Rules", () => [new RegexHighlightingRule(XRegex(), new RegexHighlightingStyle())]);
		Assert.IsNull(definition.MainRuleSet.Rules[0].Color.Foreground);
	}

	[TestMethod]
	public void MainRuleSet_StyleWithoutFontFlags_LeavesFontUnset()
	{
		var definition = new TestDefinition(
			"Test Rules",
			() => [new RegexHighlightingRule(XRegex(), new RegexHighlightingStyle(ArgbColor.FromRgb(0xFF, 0x00, 0x00)))]
		);

		HighlightingColor color = definition.MainRuleSet.Rules[0].Color;

		Assert.IsNull(color.FontWeight);
		Assert.IsNull(color.FontStyle);
	}

	[TestMethod]
	public void MainRuleSet_RebuildsWhenCacheVersionChanges()
	{
		int version = 1;

		var definition = new TestDefinition("Test Rules", () => [Rule("x")], cacheVersion: () => version);

		HighlightingRuleSet first = definition.MainRuleSet;

		Assert.AreSame(first, definition.MainRuleSet);

		version = 2;

		HighlightingRuleSet rebuilt = definition.MainRuleSet;

		Assert.AreNotSame(first, rebuilt);
		Assert.AreSame(rebuilt, definition.MainRuleSet);
	}

	[TestMethod]
	public void MainRuleSet_CacheVersionChange_RaisesRuleSetChanged()
	{
		int version = 1;
		int changedCalls = 0;

		var definition = new TestDefinition("Test Rules", () => [Rule("x")], cacheVersion: () => version);

		object? sender = null;

		definition.RuleSetChanged += (source, _) =>
		{
			changedCalls++;
			sender = source;
		};

		_ = definition.MainRuleSet;

		Assert.AreEqual(0, changedCalls);

		version = 2;

		_ = definition.MainRuleSet;

		Assert.AreEqual(1, changedCalls);
		Assert.AreSame(definition, sender);
	}

	[TestMethod]
	public void MainRuleSet_BuildRulesAccessingRuleSet_ThrowsInvalidOperationException()
	{
		TestDefinition? definition = null;

		definition = new TestDefinition(
			"Recursive Rules",

			() =>
			{
				_ = definition!.MainRuleSet;
				return [Rule("x")];
			}
		);

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = definition.MainRuleSet);
	}

	[TestMethod]
	public void MainRuleSet_BuildRulesAccessingOwnNamedRuleSet_ThrowsInvalidOperationException()
	{
		TestDefinition? definition = null;

		definition = new TestDefinition(
			"Recursive Rules",

			() =>
			{
				_ = definition!.GetNamedRuleSet("Recursive Rules");
				return [Rule("x")];
			}
		);

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = definition.MainRuleSet);
	}

	[TestMethod]
	public void MainRuleSet_EmptyRules_YieldsEmptyRuleSet()
	{
		var definition = new TestDefinition("Test Rules", () => []);
		Assert.IsEmpty(definition.MainRuleSet.Rules);
	}

	[TestMethod]
	public void GetNamedRuleSet_MatchingName_ReturnsMainRuleSet()
	{
		var definition = new TestDefinition("Test Rules", () => [Rule("x")]);
		Assert.AreSame(definition.MainRuleSet, definition.GetNamedRuleSet("Test Rules"));
	}

	[TestMethod]
	public void GetNamedRuleSet_UnknownName_ReturnsNull()
	{
		var definition = new TestDefinition("Test Rules", () => [Rule("x")]);
		Assert.IsNull(definition.GetNamedRuleSet("DoesNotExist"));
	}

	[TestMethod]
	public void GetNamedColor_ReturnsNull()
	{
		var definition = new TestDefinition("Test Rules", () => []);
		Assert.IsNull(definition.GetNamedColor("keyword"));
	}

	[TestMethod]
	public void Name_ReturnsConstructorName()
	{
		var definition = new TestDefinition("Test Rules", () => []);
		Assert.AreEqual("Test Rules", definition.Name);
	}

	[TestMethod]
	public void Properties_IsEmpty()
	{
		var definition = new TestDefinition("Test Rules", () => []);
		Assert.IsEmpty(definition.Properties);
	}

	[TestMethod]
	public void NamedHighlightingColors_IsEmpty()
	{
		var definition = new TestDefinition("Test Rules", () => []);
		Assert.IsEmpty(definition.NamedHighlightingColors);
	}

	[TestMethod]
	public void BuildRules_AccessingUnknownNamedRuleSet_ReturnsNullWithoutThrowing()
	{
		TestDefinition? definition = null;

		definition = new TestDefinition(
			"Recursive Rules",

			() =>
			{
				// Only the matching name touches the rule set, so an unknown name returns null instead of throwing.
				Assert.IsNull(definition!.GetNamedRuleSet("Other Rules"));
				return [Rule("x")];
			}
		);

		Assert.IsNotEmpty(definition.MainRuleSet.Rules);
	}

	[TestMethod]
	public void MainRuleSet_BuildSpans_MapsSpansOntoEditorSpans()
	{
		var begin = CompiledSlashStarRegex();
		var end = CompiledStarsSlashRegex();

		var definition = new TestSpanDefinition(
			"Span Rules",
			() => [Rule(";.*$")],
			() => [new RegexHighlightingSpan(begin, end, new RegexHighlightingStyle(ArgbColor.FromRgb(0xFF, 0x00, 0x00)))]
		);

		HighlightingRuleSet ruleSet = definition.MainRuleSet;

		Assert.HasCount(1, ruleSet.Spans);

		HighlightingSpan span = ruleSet.Spans[0];

		Assert.AreSame(begin, span.StartExpression);
		Assert.AreSame(end, span.EndExpression);
		Assert.IsNotNull(span.SpanColor);
		Assert.IsNotNull(span.SpanColor.Foreground);

		// Without dedicated begin or end styles, the span color covers the delimiter matches as well.
		Assert.IsTrue(span.SpanColorIncludesStart);
		Assert.IsTrue(span.SpanColorIncludesEnd);
	}

	[TestMethod]
	public void MainRuleSet_BuildSpans_BeginOrEndStylesOverrideTheirMatches()
	{
		var definition = new TestSpanDefinition(
			"Span Rules",
			() => [],

			() =>
				[
					new RegexHighlightingSpan(SlashStarRegex(), StarsSlashRegex(), new RegexHighlightingStyle(ArgbColor.FromRgb(0xFF, 0x00, 0x00)))
					{
						BeginStyle = new RegexHighlightingStyle(ArgbColor.FromRgb(0x00, 0xFF, 0x00)),
						EndStyle = new RegexHighlightingStyle(ArgbColor.FromRgb(0x00, 0x00, 0xFF)),
					},
				]
		);

		HighlightingSpan span = definition.MainRuleSet.Spans[0];

		Assert.IsFalse(span.SpanColorIncludesStart);
		Assert.IsFalse(span.SpanColorIncludesEnd);
		Assert.IsNotNull(span.StartColor);
		Assert.IsNotNull(span.EndColor);
	}

	[TestMethod]
	public void RuleAndSpanTypes_ExposeTheConfiguredValues()
	{
		var pattern = XRunRegex();
		var style = new RegexHighlightingStyle(ArgbColor.FromRgb(0xFF, 0x00, 0x00), IsBold: true);
		var rule = new RegexHighlightingRule(pattern, style);

		Assert.AreSame(pattern, rule.Pattern);
		Assert.AreSame(style, rule.Style);

		var begin = SlashStarRegex();
		var end = StarSlashRegex();
		var spanStyle = new RegexHighlightingStyle(ArgbColor.FromRgb(0x00, 0xFF, 0x00));
		var beginStyle = new RegexHighlightingStyle(ArgbColor.FromRgb(0x00, 0x00, 0xFF));
		var endStyle = new RegexHighlightingStyle(ArgbColor.FromRgb(0xFF, 0xFF, 0x00));
		var span = new RegexHighlightingSpan(begin, end, spanStyle) { BeginStyle = beginStyle, EndStyle = endStyle };

		Assert.AreSame(begin, span.Begin);
		Assert.AreSame(end, span.End);
		Assert.AreSame(spanStyle, span.SpanStyle);
		Assert.AreSame(beginStyle, span.BeginStyle);
		Assert.AreSame(endStyle, span.EndStyle);
	}

	[TestMethod]
	public void MainRuleSet_BuildSpans_EmptyMatchPattern_IsRejected()
	{
		var definition = new TestSpanDefinition(
			"Span Rules",
			() => [],
			() => [new RegexHighlightingSpan(OptionalXRegex(), null, new RegexHighlightingStyle(ArgbColor.FromRgb(0xFF, 0x00, 0x00)))]
		);

		Assert.ThrowsExactly<InvalidOperationException>(() => definition.MainRuleSet);
	}

	[TestMethod]
	public void MainRuleSet_BuildSpans_WithNestedRules_MapsThemOntoTheSpanRuleSet()
	{
		var nestedPattern = KeywordRegex();

		var definition = new TestSpanDefinition(
			"Span Rules",
			() => [],

			() =>
				[
					new RegexHighlightingSpan(SlashStarRegex(), StarSlashRegex(), new RegexHighlightingStyle(ArgbColor.FromRgb(0xFF, 0x00, 0x00)))
					{
						Rules = [new RegexHighlightingRule(nestedPattern, new RegexHighlightingStyle(ArgbColor.FromRgb(0x00, 0xFF, 0x00)))],
					},
				]
		);

		HighlightingSpan span = definition.MainRuleSet.Spans[0];

		// The nested rules apply while the span is open, so they become the span's own rule set.
		Assert.IsNotNull(span.RuleSet);

		HighlightingRule nestedRule = span.RuleSet.Rules.Single();

		Assert.AreSame(nestedPattern, nestedRule.Regex);
		Assert.AreEqual(Colors.Lime, nestedRule.Color.Foreground.GetColor(null!));
	}

	[TestMethod]
	public void MainRuleSet_BuildSpans_WithoutNestedRules_LeavesTheSpanRuleSetUnset()
	{
		var definition = new TestSpanDefinition(
			"Span Rules",
			() => [],
			() => [new RegexHighlightingSpan(SlashStarRegex(), StarSlashRegex(), new RegexHighlightingStyle(ArgbColor.FromRgb(0xFF, 0x00, 0x00)))]
		);

		HighlightingSpan span = definition.MainRuleSet.Spans[0];

		// Without nested rules the span carries no rule set, so its content is styled only by the span
		// colors and the definition's per-line rules do not apply inside it.
		Assert.IsNull(span.RuleSet);
	}

	[TestMethod]
	public void MainRuleSet_BuildRules_RightToLeftPattern_IsRejected()
	{
		var definition = new TestDefinition(
			"Probe Rules",
			() => [new RegexHighlightingRule(RightToLeftXRegex(), new RegexHighlightingStyle(ArgbColor.FromRgb(0xFF, 0x00, 0x00)))]
		);

		// the editor's highlight engine scans each line from left to right, so a right-to-left pattern
		// must be rejected while the rule set is built instead of failing inside the engine.
		Assert.ThrowsExactly<InvalidOperationException>(() => definition.MainRuleSet);
	}

	[TestMethod]
	public void MainRuleSet_BuildRules_EmptyMatchOnAlternativeProbe_IsRejected()
	{
		// The pattern matches nothing (not even empty) on "x y", but it can match empty after 'foo', which
		// the probe corpus detects before the highlight engine would.
		var definition = new TestDefinition("Probe Rules", () => [Rule("(?<=foo)b?")]);
		Assert.ThrowsExactly<InvalidOperationException>(() => definition.MainRuleSet);
	}

	[TestMethod]
	public void Create_WithSpansProvider_BuildsTheRuleSetFromTheProviders()
	{
		int ruleCalls = 0;
		int spanCalls = 0;

		var definition = RegexHighlightingDefinition.Create(
			"Factory Rules",

			() =>
			{
				ruleCalls++;
				return [Rule("x")];
			},

			() =>
			{
				spanCalls++;

				return
				[
					new RegexHighlightingSpan(SlashStarRegex(), StarSlashRegex(), new RegexHighlightingStyle(ArgbColor.FromRgb(0xFF, 0x00, 0x00))),
				];
			}
		);

		// The providers run lazily, on the first rule-set access.
		Assert.AreEqual(0, ruleCalls);
		Assert.AreEqual(0, spanCalls);

		HighlightingRuleSet ruleSet = definition.MainRuleSet;

		Assert.AreEqual("Factory Rules", definition.Name);
		Assert.HasCount(1, ruleSet.Rules);
		Assert.HasCount(1, ruleSet.Spans);
		Assert.AreEqual(1, ruleCalls);
		Assert.AreEqual(1, spanCalls);
	}

	[TestMethod]
	public void Create_RulesOnly_BuildsTheRuleSet()
	{
		var definition = RegexHighlightingDefinition.Create("Rules Only", () => [Rule("x")]);

		HighlightingRuleSet ruleSet = definition.MainRuleSet;

		Assert.AreEqual("Rules Only", definition.Name);
		Assert.HasCount(1, ruleSet.Rules);
		Assert.IsEmpty(ruleSet.Spans);
	}

	[TestMethod]
	public void Create_NullRulesProvider_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => RegexHighlightingDefinition.Create("X", null!));

	[TestMethod]
	public void Create_NullSpansProvider_BuildsTheDefinitionWithoutSpans()
	{
		var definition = RegexHighlightingDefinition.Create("X", () => [Rule("x")], (Func<IEnumerable<RegexHighlightingSpan>>?)null);
		Assert.IsEmpty(definition.MainRuleSet.Spans);
	}

	[TestMethod]
	public void MainRuleSet_SpanWithoutEndExpression_HighlightsToTheDocumentEndWithoutThrowing()
	{
		var definition = new TestSpanDefinition(
			"Open Span Rules",
			() => [],
			() => [new RegexHighlightingSpan(SlashStarRegex(), end: null, new RegexHighlightingStyle(ArgbColor.FromRgb(0xFF, 0x00, 0x00)))]
		);

		HighlightingRuleSet ruleSet = definition.MainRuleSet;
		Assert.IsNotNull(ruleSet.Spans[0].EndExpression);

		// The null end is mapped onto a pattern that cannot match, because the editor engine
		// requires a non-null end expression and dereferences it while coloring.
		Assert.IsFalse(ruleSet.Spans[0].EndExpression.IsMatch("plain text"));
		Assert.IsFalse(ruleSet.Spans[0].EndExpression.IsMatch("*/"));

		var document = new TextDocument("/* open\nsecond line");
		var highlighter = new DocumentHighlighter(document, definition);

		// Highlighting the span start and the following line runs the engine with an open span; a raw
		// null end expression would fail here instead of producing sections.
		HighlightedLine firstLine = highlighter.HighlightLine(1);
		HighlightedLine secondLine = highlighter.HighlightLine(2);

		Assert.IsNotEmpty(firstLine.Sections);
		Assert.IsNotEmpty(secondLine.Sections);

		HighlightedSection openSection = secondLine.Sections[0];

		// Sections carry document-absolute offsets, so the open span's section starts at the second
		// line's document offset and covers the whole line.
		Assert.AreEqual(document.GetLineByNumber(2).Offset, openSection.Offset);
		Assert.AreEqual("second line".Length, openSection.Length);
		Assert.AreEqual(Colors.Red, openSection.Color.Foreground.GetColor(null!));
	}

	private sealed class TestSpanDefinition : RegexHighlightingDefinition
	{
		private readonly Func<IEnumerable<RegexHighlightingRule>> _buildRules;
		private readonly Func<IEnumerable<RegexHighlightingSpan>> _buildSpans;

		public TestSpanDefinition(
			string name,
			Func<IEnumerable<RegexHighlightingRule>> buildRules,
			Func<IEnumerable<RegexHighlightingSpan>> buildSpans
		)
			: base(name)
		{
			_buildRules = buildRules;
			_buildSpans = buildSpans;
		}

		protected override IEnumerable<RegexHighlightingRule> BuildRules() => _buildRules();

		protected override IEnumerable<RegexHighlightingSpan> BuildSpans() => _buildSpans();
	}

	[GeneratedRegex(";.*$")]
	private static partial Regex SemicolonCommentRegex();

	[GeneratedRegex("x")]
	private static partial Regex XRegex();

	[GeneratedRegex("/[*]", RegexOptions.Compiled)]
	private static partial Regex CompiledSlashStarRegex();

	[GeneratedRegex("[*]+/", RegexOptions.Compiled)]
	private static partial Regex CompiledStarsSlashRegex();

	[GeneratedRegex("/[*]")]
	private static partial Regex SlashStarRegex();

	[GeneratedRegex("[*]+/")]
	private static partial Regex StarsSlashRegex();

	[GeneratedRegex("x+")]
	private static partial Regex XRunRegex();

	[GeneratedRegex("[*]/")]
	private static partial Regex StarSlashRegex();

	[GeneratedRegex("x?")]
	private static partial Regex OptionalXRegex();

	[GeneratedRegex("keyword")]
	private static partial Regex KeywordRegex();

	[GeneratedRegex("x", RegexOptions.RightToLeft)]
	private static partial Regex RightToLeftXRegex();
}
