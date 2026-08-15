using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the editor-neutral <see cref="RegexHighlightingRuleSet"/> that every editor binding converts into
/// its engine's highlighting rule set: the validation of rule, span, and nested-rule patterns, and the
/// order-preserving materialization of the supplied rules and spans.
/// </summary>
[TestClass]
public sealed class RegexHighlightingRuleSetTests
{
	private static RegexHighlightingRule Rule(string pattern)
		=> new(new Regex(pattern), new RegexHighlightingStyle("#FF0000"));

	[TestMethod]
	public void Create_MaterializesRulesAndSpansInOrder()
	{
		RegexHighlightingRule first = Rule("aa");
		RegexHighlightingRule second = Rule("bb");
		var span = new RegexHighlightingSpan(new Regex("/[*]"), new Regex("[*]/"), new RegexHighlightingStyle("#00FF00"));

		RegexHighlightingRuleSet ruleSet = RegexHighlightingRuleSet.Create("Test", [first, second], [span]);

		Assert.AreEqual("Test", ruleSet.Name);
		Assert.AreEqual(2, ruleSet.Rules.Count);
		Assert.AreSame(first, ruleSet.Rules[0]);
		Assert.AreSame(second, ruleSet.Rules[1]);
		Assert.AreEqual(1, ruleSet.Spans.Count);
		Assert.AreSame(span, ruleSet.Spans[0]);
	}

	[TestMethod]
	public void Create_NullName_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => RegexHighlightingRuleSet.Create(null!, [], []));
	}

	[TestMethod]
	public void Create_NullRules_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => RegexHighlightingRuleSet.Create("Test", null!, []));
	}

	[TestMethod]
	public void Create_NullSpans_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => RegexHighlightingRuleSet.Create("Test", [], null!));
	}

	[TestMethod]
	public void Create_RightToLeftRulePattern_ThrowsInvalidOperationException()
	{
		var rule = new RegexHighlightingRule(new Regex("x", RegexOptions.RightToLeft), new RegexHighlightingStyle("#FF0000"));

		Assert.ThrowsExactly<InvalidOperationException>(() => RegexHighlightingRuleSet.Create("Test", [rule], []));
	}

	[TestMethod]
	public void Create_EmptyCapableRulePattern_ThrowsInvalidOperationException()
	{
		Assert.ThrowsExactly<InvalidOperationException>(() => RegexHighlightingRuleSet.Create("Test", [Rule("x?")], []));
	}

	[TestMethod]
	public void Create_EmptyCapableSpanBeginPattern_ThrowsInvalidOperationException()
	{
		var span = new RegexHighlightingSpan(new Regex("x?"), null, new RegexHighlightingStyle("#FF0000"));

		Assert.ThrowsExactly<InvalidOperationException>(() => RegexHighlightingRuleSet.Create("Test", [], [span]));
	}

	[TestMethod]
	public void Create_EmptyCapableSpanEndPattern_ThrowsInvalidOperationException()
	{
		var span = new RegexHighlightingSpan(new Regex("/[*]"), new Regex("x?"), new RegexHighlightingStyle("#FF0000"));

		Assert.ThrowsExactly<InvalidOperationException>(() => RegexHighlightingRuleSet.Create("Test", [], [span]));
	}

	[TestMethod]
	public void Create_EmptyCapableNestedRulePattern_ThrowsInvalidOperationException()
	{
		var span = new RegexHighlightingSpan(new Regex("/[*]"), new Regex("[*]/"), new RegexHighlightingStyle("#FF0000"))
		{
			Rules = [Rule("x?")]
		};

		Assert.ThrowsExactly<InvalidOperationException>(() => RegexHighlightingRuleSet.Create("Test", [], [span]));
	}

	[TestMethod]
	public void Create_ValidSpanWithNestedRules_KeepsTheSpan()
	{
		var span = new RegexHighlightingSpan(new Regex("/[*]"), new Regex("[*]/"), new RegexHighlightingStyle("#FF0000"))
		{
			Rules = [Rule("word")]
		};

		RegexHighlightingRuleSet ruleSet = RegexHighlightingRuleSet.Create("Test", [], [span]);

		Assert.AreEqual(1, ruleSet.Spans.Count);
		Assert.AreSame(span, ruleSet.Spans[0]);
	}
}
