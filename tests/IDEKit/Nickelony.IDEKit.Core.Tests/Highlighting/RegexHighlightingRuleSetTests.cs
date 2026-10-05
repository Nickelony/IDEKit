using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the editor-neutral <see cref="RegexHighlightingRuleSet"/> that every editor binding converts into its engine's highlighting rule set:
/// the validation of rule, span, and nested-rule patterns, and the order-preserving materialization of the supplied rules and spans.
/// </summary>
[TestClass]
public sealed partial class RegexHighlightingRuleSetTests
{
	private static RegexHighlightingRule Rule(string pattern) =>
		new(new Regex(pattern), new RegexHighlightingStyle(ArgbColor.FromRgb(0xFF, 0x00, 0x00)));

	[TestMethod]
	public void Create_MaterializesRulesAndSpansInOrder()
	{
		RegexHighlightingRule first = Rule("aa");
		RegexHighlightingRule second = Rule("bb");

		var span = new RegexHighlightingSpan(SlashStarRegex(), StarSlashRegex(), new RegexHighlightingStyle(ArgbColor.FromRgb(0x00, 0xFF, 0x00)));

		var ruleSet = RegexHighlightingRuleSet.Create("Test", [first, second], [span]);

		Assert.AreEqual("Test", ruleSet.Name);
		Assert.HasCount(2, ruleSet.Rules);
		Assert.AreSame(first, ruleSet.Rules[0]);
		Assert.AreSame(second, ruleSet.Rules[1]);
		Assert.HasCount(1, ruleSet.Spans);
		Assert.AreSame(span, ruleSet.Spans[0]);
	}

	[TestMethod]
	public void Create_NullName_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => RegexHighlightingRuleSet.Create(null!, [], []));

	[TestMethod]
	public void Create_NullRules_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => RegexHighlightingRuleSet.Create("Test", null!, []));

	[TestMethod]
	public void Create_NullSpans_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => RegexHighlightingRuleSet.Create("Test", [], null!));

	[TestMethod]
	public void Create_RightToLeftRulePattern_ThrowsInvalidOperationException()
	{
		var rule = new RegexHighlightingRule(RightToLeftXRegex(), new RegexHighlightingStyle(ArgbColor.FromRgb(0xFF, 0x00, 0x00)));
		Assert.ThrowsExactly<InvalidOperationException>(() => RegexHighlightingRuleSet.Create("Test", [rule], []));
	}

	[TestMethod]
	public void Create_EmptyCapableRulePattern_ThrowsInvalidOperationException() =>
		Assert.ThrowsExactly<InvalidOperationException>(() => RegexHighlightingRuleSet.Create("Test", [Rule("x?")], []));

	[TestMethod]
	public void Create_EmptyCapableSpanBeginPattern_ThrowsInvalidOperationException()
	{
		var span = new RegexHighlightingSpan(OptionalXRegex(), null, new RegexHighlightingStyle(ArgbColor.FromRgb(0xFF, 0x00, 0x00)));
		Assert.ThrowsExactly<InvalidOperationException>(() => RegexHighlightingRuleSet.Create("Test", [], [span]));
	}

	[TestMethod]
	public void Create_EmptyCapableSpanEndPattern_ThrowsInvalidOperationException()
	{
		var span = new RegexHighlightingSpan(SlashStarRegex(), OptionalXRegex(), new RegexHighlightingStyle(ArgbColor.FromRgb(0xFF, 0x00, 0x00)));
		Assert.ThrowsExactly<InvalidOperationException>(() => RegexHighlightingRuleSet.Create("Test", [], [span]));
	}

	[TestMethod]
	public void Create_EmptyCapableNestedRulePattern_ThrowsInvalidOperationException()
	{
		var span = new RegexHighlightingSpan(SlashStarRegex(), StarSlashRegex(), new RegexHighlightingStyle(ArgbColor.FromRgb(0xFF, 0x00, 0x00)))
		{
			Rules = [Rule("x?")],
		};

		Assert.ThrowsExactly<InvalidOperationException>(() => RegexHighlightingRuleSet.Create("Test", [], [span]));
	}

	[TestMethod]
	public void Create_ValidSpanWithNestedRules_KeepsTheSpan()
	{
		var span = new RegexHighlightingSpan(SlashStarRegex(), StarSlashRegex(), new RegexHighlightingStyle(ArgbColor.FromRgb(0xFF, 0x00, 0x00)))
		{
			Rules = [Rule("word")],
		};

		var ruleSet = RegexHighlightingRuleSet.Create("Test", [], [span]);

		Assert.HasCount(1, ruleSet.Spans);
		Assert.AreSame(span, ruleSet.Spans[0]);
	}

	[GeneratedRegex("/[*]")]
	private static partial Regex SlashStarRegex();

	[GeneratedRegex("[*]/")]
	private static partial Regex StarSlashRegex();

	[GeneratedRegex("x", RegexOptions.RightToLeft)]
	private static partial Regex RightToLeftXRegex();

	[GeneratedRegex("x?")]
	private static partial Regex OptionalXRegex();
}
