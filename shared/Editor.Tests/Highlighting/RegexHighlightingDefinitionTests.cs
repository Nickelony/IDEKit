#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using Nickelony.IDEKit.AvaloniaEdit.Highlighting;
using Nickelony.IDEKit.Core.Highlighting;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FontStyles = Avalonia.Media.FontStyle;
using FontWeights = Avalonia.Media.FontWeight;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using Nickelony.IDEKit.AvalonEdit.Highlighting;
using Nickelony.IDEKit.Core.Highlighting;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using FontStyles = System.Windows.FontStyles;
using FontWeights = System.Windows.FontWeights;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

[TestClass]
public sealed class RegexHighlightingDefinitionTests
{
	private sealed class TestDefinition : RegexHighlightingDefinition
	{
		private readonly Func<IEnumerable<RegexHighlightingRule>> _buildRules;

		public TestDefinition(
			string name,
			Func<IEnumerable<RegexHighlightingRule>> buildRules,
			Func<int>? cacheVersion = null,
			Color? fallbackColor = null)
			: base(name, new RegexHighlightingDefinitionOptions(cacheVersion, fallbackColor))
		{
			_buildRules = buildRules;
		}

		protected override IEnumerable<RegexHighlightingRule> BuildRules()
			=> _buildRules();
	}

	private static RegexHighlightingRule Rule(string pattern, string? htmlColor = null, bool isBold = false, bool isItalic = false)
		=> new(new Regex(pattern), new RegexHighlightingStyle(htmlColor, isBold, isItalic));

	[TestMethod]
	public void MainRuleSet_IsBuiltLazilyAndCached()
	{
		int buildCount = 0;

		var definition = new TestDefinition(
			"Test Rules",
			() =>
			{
				buildCount++;
				return [Rule(";.*$")];
			});

		Assert.AreEqual(0, buildCount);

		HighlightingRuleSet first = definition.MainRuleSet;

		Assert.AreEqual(1, buildCount);
		Assert.AreSame(first, definition.MainRuleSet);
		Assert.AreEqual(1, buildCount);
	}

	[TestMethod]
	public void MainRuleSet_AppliesRegexAndStyle()
	{
		var definition = new TestDefinition(
			"Test Rules",
			() => [new RegexHighlightingRule(new Regex(";.*$"), new RegexHighlightingStyle("#FF0000", IsBold: true, IsItalic: true))]);

		HighlightingRule rule = definition.MainRuleSet.Rules.Single();

		Assert.IsTrue(rule.Regex.IsMatch("; comment"));
		Assert.AreEqual(Colors.Red, rule.Color.Foreground.GetColor(null!));
		Assert.AreEqual(FontWeights.Bold, rule.Color.FontWeight);
		Assert.AreEqual(FontStyles.Italic, rule.Color.FontStyle);
	}

	[TestMethod]
	public void MainRuleSet_MalformedColor_LeavesForegroundUnsetByDefault()
	{
		var definition = new TestDefinition(
			"Test Rules",
			() => [new RegexHighlightingRule(new Regex("x"), new RegexHighlightingStyle("not-a-color"))]);

		// An unparseable color leaves the foreground unset so the editor's theme color is used.
		Assert.IsNull(definition.MainRuleSet.Rules[0].Color.Foreground);
	}

	[TestMethod]
	public void MainRuleSet_MalformedColor_UsesSuppliedFallback()
	{
		var definition = new TestDefinition(
			"Test Rules",
			() => [new RegexHighlightingRule(new Regex("x"), new RegexHighlightingStyle("not-a-color"))],
			fallbackColor: Colors.Blue);

		Assert.AreEqual(Colors.Blue, definition.MainRuleSet.Rules[0].Color.Foreground.GetColor(null!));
	}

	[TestMethod]
	public void MainRuleSet_MissingColor_LeavesForegroundUnset()
	{
		var definition = new TestDefinition(
			"Test Rules",
			() => [new RegexHighlightingRule(new Regex("x"), new RegexHighlightingStyle())]);

		Assert.IsNull(definition.MainRuleSet.Rules[0].Color.Foreground);
	}

	[TestMethod]
	public void MainRuleSet_EmptyColorValue_LeavesForegroundUnset()
	{
		var definition = new TestDefinition(
			"Test Rules",
			() => [new RegexHighlightingRule(new Regex("x"), new RegexHighlightingStyle(string.Empty))]);

		Assert.IsNull(definition.MainRuleSet.Rules[0].Color.Foreground);
	}

	[TestMethod]
	public void MainRuleSet_WhitespaceOnlyColorValue_LeavesForegroundUnset()
	{
		var definition = new TestDefinition(
			"Test Rules",
			() => [new RegexHighlightingRule(new Regex("x"), new RegexHighlightingStyle("   "))]);

		// A blank value behaves like a missing color, not like an unparseable one.
		Assert.IsNull(definition.MainRuleSet.Rules[0].Color.Foreground);
	}

	[TestMethod]
	public void MainRuleSet_StyleWithoutFontFlags_LeavesFontUnset()
	{
		var definition = new TestDefinition(
			"Test Rules",
			() => [new RegexHighlightingRule(new Regex("x"), new RegexHighlightingStyle("#FF0000"))]);

		HighlightingColor color = definition.MainRuleSet.Rules[0].Color;

		Assert.IsNull(color.FontWeight);
		Assert.IsNull(color.FontStyle);
	}

	[TestMethod]
	public void MainRuleSet_RebuildsWhenCacheVersionChanges()
	{
		int version = 1;

		var definition = new TestDefinition(
			"Test Rules",
			() => [Rule("x")],
			cacheVersion: () => version);

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

		var definition = new TestDefinition(
			"Test Rules",
			() => [Rule("x")],
			cacheVersion: () => version);

		definition.RuleSetChanged += (_, _) => changedCalls++;

		_ = definition.MainRuleSet;

		Assert.AreEqual(0, changedCalls);

		version = 2;

		_ = definition.MainRuleSet;

		Assert.AreEqual(1, changedCalls);
	}

	[TestMethod]
	public void RuleSetChanged_HandlerCanReadTheRebuiltRuleSet()
	{
		int version = 1;
		HighlightingRuleSet? observed = null;

		var definition = new TestDefinition(
			"Test Rules",
			() => [Rule("x")],
			cacheVersion: () => version);

		definition.RuleSetChanged += (_, _) => observed = definition.MainRuleSet;

		_ = definition.MainRuleSet;

		version = 2;
		HighlightingRuleSet rebuilt = definition.MainRuleSet;

		// The guard is released and the rebuilt rule set is published before the event is raised.
		Assert.AreSame(rebuilt, observed);
	}

	[TestMethod]
	public void MainRuleSet_BuildRulesAccessingRuleSet_ThrowsInvalidOperationException()
	{
		TestDefinition? definition = null;

		definition = new TestDefinition("Recursive Rules", () =>
		{
			_ = definition!.MainRuleSet;
			return [Rule("x")];
		});

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = definition.MainRuleSet);
	}

	[TestMethod]
	public void MainRuleSet_BuildRulesAccessingRuleSetDuringRebuild_ThrowsInvalidOperationException()
	{
		int version = 1;
		int buildCalls = 0;
		TestDefinition? definition = null;

		definition = new TestDefinition("Recursive Rules", () =>
		{
			buildCalls++;

			// Only the rebuild re-enters the rule set, so the cached snapshot must not bypass the guard.
			if (buildCalls > 1)
				_ = definition!.MainRuleSet;

			return [Rule("x")];
		}, cacheVersion: () => version);

		_ = definition.MainRuleSet;

		version = 2;

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = definition.MainRuleSet);
	}

	[TestMethod]
	public void MainRuleSet_CacheVersionCallbackAccessingRuleSet_ThrowsInvalidOperationException()
	{
		TestDefinition? definition = null;

		definition = new TestDefinition(
			"Recursive Rules",
			() => [Rule("x")],
			cacheVersion: () =>
			{
				// A callback that reads its own definition would re-invoke itself through the property;
				// the guard covers the callback as well, so the access throws instead of recursing.
				_ = definition!.MainRuleSet;
				return 0;
			});

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = definition.MainRuleSet);
	}

	[TestMethod]
	public void MainRuleSet_BuildRulesAccessingOwnNamedRuleSet_ThrowsInvalidOperationException()
	{
		TestDefinition? definition = null;

		definition = new TestDefinition("Recursive Rules", () =>
		{
			_ = definition!.GetNamedRuleSet("Recursive Rules");
			return [Rule("x")];
		});

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = definition.MainRuleSet);
	}

	[TestMethod]
	public void MainRuleSet_BuildRulesAccessingAnotherDefinition_IsAllowed()
	{
		var other = new TestDefinition("Other Rules", () => [Rule("y")]);
		HighlightingRuleSet? composed = null;

		var definition = new TestDefinition("Test Rules", () =>
		{
			// The recursion guard is scoped per definition, so composing from another definition's
			// rules is not treated as re-entering this definition's build.
			composed = other.MainRuleSet;
			return [Rule("x")];
		});

		Assert.IsNotEmpty(definition.MainRuleSet.Rules);
		Assert.IsNotNull(composed);
		Assert.IsNotEmpty(composed.Rules);
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
	public void MainRuleSet_ConcurrentAccessWithRebuilds_ReturnsUsableRuleSets()
	{
		int[] version = [1];

		var definition = new TestDefinition("Test Rules", () => [Rule("x", "#FF0000")], cacheVersion: () => version[0]);

		Assert.IsNotNull(definition.MainRuleSet);

		var results = new ConcurrentBag<HighlightingRuleSet>();

		Parallel.For(0, 200, index =>
		{
			if (index % 40 == 0)
				Interlocked.Increment(ref version[0]);

			results.Add(definition.MainRuleSet);
		});

		// Every observed rule set must be fully built: the rule and its resolved color are readable,
		// so no caller can observe a partially constructed rule set.
		foreach (HighlightingRuleSet ruleSet in results)
		{
			HighlightingRule rule = ruleSet.Rules.Single();

			Assert.IsTrue(rule.Regex.IsMatch("x"));
			Assert.AreEqual(Colors.Red, rule.Color.Foreground.GetColor(null!));
		}

		// A version change after the parallel phase yields a distinct rebuilt rule set.
		HighlightingRuleSet before = definition.MainRuleSet;

		Interlocked.Increment(ref version[0]);

		HighlightingRuleSet after = definition.MainRuleSet;

		Assert.AreNotSame(before, after);
		Assert.IsNotEmpty(after.Rules);
	}

	[TestMethod]
	public void MainRuleSet_SameVersionConcurrentRebuilds_DoNotRaiseRuleSetChanged()
	{
		int buildCalls = 0;
		using var secondBuildEntered = new ManualResetEventSlim(false);
		using var startGate = new Barrier(2);

		var definition = new TestDefinition("Concurrent Rules", () =>
		{
			if (Interlocked.Increment(ref buildCalls) == 1)
			{
				// Hold the first build until the other builder is inside its own build, so both
				// publications replace a snapshot of the same version.
				secondBuildEntered.Wait(TimeSpan.FromSeconds(10));
			}
			else
			{
				secondBuildEntered.Set();
			}

			return [Rule("x")];
		}, cacheVersion: () => 1);

		int changedCalls = 0;
		definition.RuleSetChanged += (_, _) => Interlocked.Increment(ref changedCalls);

		HighlightingRuleSet? firstResult = null;
		HighlightingRuleSet? secondResult = null;

		var first = new Thread(() =>
		{
			startGate.SignalAndWait();
			firstResult = definition.MainRuleSet;
		})
		{
			IsBackground = true
		};

		var second = new Thread(() =>
		{
			startGate.SignalAndWait();
			secondResult = definition.MainRuleSet;
		})
		{
			IsBackground = true
		};

		first.Start();
		second.Start();

		Assert.IsTrue(first.Join(TimeSpan.FromSeconds(15)), "The first rule-set build did not complete.");
		Assert.IsTrue(second.Join(TimeSpan.FromSeconds(15)), "The second rule-set build did not complete.");

		Assert.IsNotNull(firstResult);
		Assert.IsNotNull(secondResult);
		Assert.AreEqual(2, buildCalls);

		// The second publication replaced a snapshot of the same version, so no rebuild is reported;
		// the event only covers the replacement of a snapshot of a different version.
		Assert.AreEqual(0, changedCalls);
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
	public void MainRuleSet_BuildRulesThrows_DoesNotCacheTheFailureAndRecoversOnNextAccess()
	{
		int buildCalls = 0;

		var definition = new TestDefinition("Flaky Rules", () =>
		{
			buildCalls++;

			if (buildCalls == 1)
				throw new InvalidOperationException("The first build fails.");

			return [Rule("x")];
		});

		Assert.ThrowsExactly<InvalidOperationException>(() => definition.MainRuleSet);
		Assert.AreEqual(1, buildCalls);

		// The failed build was not cached, so the next access retries and succeeds.
		Assert.IsNotEmpty(definition.MainRuleSet.Rules);
		Assert.AreEqual(2, buildCalls);
	}

	[TestMethod]
	public void BuildRules_AccessingUnknownNamedRuleSet_ReturnsNullWithoutThrowing()
	{
		TestDefinition? definition = null;

		definition = new TestDefinition("Recursive Rules", () =>
		{
			// Only the matching name touches the rule set, so an unknown name returns null instead of throwing.
			Assert.IsNull(definition!.GetNamedRuleSet("Other Rules"));
			return [Rule("x")];
		});

		Assert.IsNotEmpty(definition.MainRuleSet.Rules);
	}

	[TestMethod]
	public void MainRuleSet_BuildSpans_MapsSpansOntoEditorSpans()
	{
		var begin = new Regex("/[*]", RegexOptions.Compiled);
		var end = new Regex("[*]+/", RegexOptions.Compiled);

		var definition = new TestSpanDefinition(
			"Span Rules",
			() => [Rule(";.*$")],
			() => [new RegexHighlightingSpan(begin, end, new RegexHighlightingStyle("#FF0000"))]);

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
				new RegexHighlightingSpan(
					new Regex("/[*]"),
					new Regex("[*]+/"),
					new RegexHighlightingStyle("#FF0000"))
				{
					BeginStyle = new RegexHighlightingStyle("#00FF00"),
					EndStyle = new RegexHighlightingStyle("#0000FF")
				}
			]);

		HighlightingSpan span = definition.MainRuleSet.Spans[0];

		Assert.IsFalse(span.SpanColorIncludesStart);
		Assert.IsFalse(span.SpanColorIncludesEnd);
		Assert.IsNotNull(span.StartColor);
		Assert.IsNotNull(span.EndColor);
	}

	[TestMethod]
	public void RuleAndSpanTypes_ExposeTheConfiguredValues()
	{
		var pattern = new Regex("x+");
		var style = new RegexHighlightingStyle("#FF0000", IsBold: true);
		var rule = new RegexHighlightingRule(pattern, style);

		Assert.AreSame(pattern, rule.Pattern);
		Assert.AreSame(style, rule.Style);

		var begin = new Regex("/[*]");
		var end = new Regex("[*]/");
		var spanStyle = new RegexHighlightingStyle("#00FF00");
		var beginStyle = new RegexHighlightingStyle("#0000FF");
		var endStyle = new RegexHighlightingStyle("#FFFF00");
		var span = new RegexHighlightingSpan(begin, end, spanStyle)
		{
			BeginStyle = beginStyle,
			EndStyle = endStyle
		};

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
			() => [new RegexHighlightingSpan(new Regex("x?"), null, new RegexHighlightingStyle("#FF0000"))]);

		Assert.ThrowsExactly<InvalidOperationException>(() => definition.MainRuleSet);
	}

	[TestMethod]
	public void MainRuleSet_BuildSpans_WithNestedRules_MapsThemOntoTheSpanRuleSet()
	{
		var nestedPattern = new Regex("keyword");

		var definition = new TestSpanDefinition(
			"Span Rules",
			() => [],
			() =>
			[
				new RegexHighlightingSpan(new Regex("/[*]"), new Regex("[*]/"), new RegexHighlightingStyle("#FF0000"))
				{
					Rules = [new RegexHighlightingRule(nestedPattern, new RegexHighlightingStyle("#00FF00"))]
				}
			]);

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
			() => [new RegexHighlightingSpan(new Regex("/[*]"), new Regex("[*]/"), new RegexHighlightingStyle("#FF0000"))]);

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
			() => [new RegexHighlightingRule(new Regex("x", RegexOptions.RightToLeft), new RegexHighlightingStyle("#FF0000"))]);

		// the editor's highlight engine scans each line from left to right, so a right-to-left pattern
		// must be rejected while the rule set is built instead of failing inside the engine.
		Assert.ThrowsExactly<InvalidOperationException>(() => definition.MainRuleSet);
	}

	[TestMethod]
	public void MainRuleSet_BuildRules_EmptyMatchOnAlternativeProbe_IsRejected()
	{
		// The pattern matches nothing (not even empty) on the former single probe text ("x y"), but it can
		// match empty after 'foo', which the widened probe corpus detects before the highlight engine would.
		var definition = new TestDefinition("Probe Rules", () => [Rule("(?<=foo)b?")]);

		Assert.ThrowsExactly<InvalidOperationException>(() => definition.MainRuleSet);
	}

	[TestMethod]
	public void CreateWithSpans_BuildsTheRuleSetFromTheProviders()
	{
		int ruleCalls = 0;
		int spanCalls = 0;

		RegexHighlightingDefinition definition = RegexHighlightingDefinition.CreateWithSpans(
			"Factory Rules",
			() =>
			{
				ruleCalls++;
				return [Rule("x")];
			},
			() =>
			{
				spanCalls++;
				return [new RegexHighlightingSpan(new Regex("/[*]"), new Regex("[*]/"), new RegexHighlightingStyle("#FF0000"))];
			});

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
		RegexHighlightingDefinition definition = RegexHighlightingDefinition.Create("Rules Only", () => [Rule("x")]);

		HighlightingRuleSet ruleSet = definition.MainRuleSet;

		Assert.AreEqual("Rules Only", definition.Name);
		Assert.HasCount(1, ruleSet.Rules);
		Assert.IsEmpty(ruleSet.Spans);
	}

	[TestMethod]
	public void Create_NullRulesProvider_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => RegexHighlightingDefinition.Create("X", null!));
	}

	[TestMethod]
	public void CreateWithSpans_NullSpansProvider_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() =>
			RegexHighlightingDefinition.CreateWithSpans("X", () => [], (Func<IEnumerable<RegexHighlightingSpan>>)null!));
	}

	[TestMethod]
	public void MainRuleSet_SpanWithoutEndExpression_HighlightsToTheDocumentEndWithoutThrowing()
	{
		var definition = new TestSpanDefinition(
			"Open Span Rules",
			() => [],
			() => [new RegexHighlightingSpan(new Regex("/[*]"), end: null, new RegexHighlightingStyle("#FF0000"))]);

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
			Func<IEnumerable<RegexHighlightingSpan>> buildSpans)
			: base(name)
		{
			_buildRules = buildRules;
			_buildSpans = buildSpans;
		}

		protected override IEnumerable<RegexHighlightingRule> BuildRules()
			=> _buildRules();

		protected override IEnumerable<RegexHighlightingSpan> BuildSpans()
			=> _buildSpans();
	}
}
