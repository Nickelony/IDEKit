namespace Nickelony.IDEKit.TextMate.Tests;

/// <summary>Pins how the anchored part of a selector matches a scope: equal, or extended by a dot-separated suffix, and nothing else.</summary>
[TestClass]
public sealed class TextMateSelectorRuleTests
{
	[DataRow("keyword", "keyword", true, DisplayName = "EqualScope")]
	[DataRow("keyword", "keyword.control.lua", true, DisplayName = "ScopeExtendingTheSelectorAtADot")]
	[DataRow("keyword.control", "keyword.control.lua", true, DisplayName = "ScopeExtendingASelectorOfTwoSegments")]
	[DataRow("keyword", "keyword.", true, DisplayName = "ScopeEndingInTheSeparatingDot")]
	[DataRow("key", "keyword", false, DisplayName = "PrefixThatEndsInsideASegment")]
	[DataRow("keyword", "keywords.control", false, DisplayName = "PrefixFollowedByAnotherLetterThenADot")]
	[DataRow("keyword", "storage.control", false, DisplayName = "SamePrefixLengthButOtherText")]
	[DataRow("keyword.control", "keyword", false, DisplayName = "ScopeShorterThanTheSelector")]
	[DataRow("entity.name", "keyword.control", false, DisplayName = "UnrelatedScope")]
	[DataRow("", "", true, DisplayName = "EmptySelectorAndEmptyScope")]
	[DataRow("", "keyword", false, DisplayName = "EmptySelectorAndNamedScope")]
	[TestMethod]
	public void MatchesAnchor_SelectorAndScope_MatchesOnlyAnEqualScopeOrADottedExtension(string selector, string scope, bool expected)
	{
		TextMateSelectorRule rule = CreateRule(selector);
		Assert.AreEqual(expected, rule.MatchesAnchor([scope], anchorIndex: 0), $"Selector '{selector}' against scope '{scope}'.");
	}

	[TestMethod]
	public void MatchesAnchor_RepeatedExtendingAndMismatchingScopes_AllocatesNothing()
	{
		TextMateSelectorRule rule = CreateRule("keyword");

		string[] extending = ["source.lua", "keyword.control.lua"];
		string[] mismatching = ["source.lua", "storage.type.lua"];

		// Warm up the path so JIT tiering stays out of the measured window.
		for (int i = 0; i < 1_000; i++)
		{
			rule.MatchesAnchor(extending, anchorIndex: 1);
			rule.MatchesAnchor(mismatching, anchorIndex: 1);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int i = 0; i < 10_000; i++)
		{
			rule.MatchesAnchor(extending, anchorIndex: 1);
			rule.MatchesAnchor(mismatching, anchorIndex: 1);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		// A matcher that builds a string per call allocates tens of bytes each time, hundreds of kilobytes over these calls; the bound
		// leaves room for runtime noise only.
		Assert.IsLessThan(10_000, allocated, $"Expected no per-call allocation, but {allocated} bytes were allocated.");
	}

	private static TextMateSelectorRule CreateRule(string selector) =>
		new(selectorParts: [selector], parentScopes: [], sequenceIndex: 0, foreground: null, traits: default);
}
