using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;
using Nickelony.IDEKit.IntelliSense.Tests.TestSupport;
using System.Reflection;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

[TestClass]
public sealed class TextCompletionSessionKernelTests
{
	private static readonly IReadOnlyList<TextCompletionItem> s_items = CompletionTestCatalog.Items;

	private static readonly IReadOnlyList<TextCompletionItem> s_probeItems =
	[
		new TextCompletionItem("Beta"),
		new TextCompletionItem("A_B2"),
		new TextCompletionItem("key"),
		new TextCompletionItem("def"),
		new TextCompletionItem("123"),
		new TextCompletionItem("välue"),
		new TextCompletionItem("Alpha")
	];

	private static readonly string[] s_allProbeLabels = ["Beta", "A_B2", "key", "def", "123", "välue", "Alpha"];

	[TestMethod]
	public void GetDecision_ProviderReturnsNoItems_ReturnsNone()
	{
		var kernel = new TextCompletionSessionKernel();
		var source = new StringTextSnapshot("Alpha");

		TextCompletionSessionDecision decision = kernel.GetDecision(source, 5, new StubProvider());

		Assert.AreEqual(TextCompletionSessionDecision.None, decision);
	}

	[TestMethod]
	public void GetDecision_ExplicitWordSpan_OverridesDefaultLocator()
	{
		var kernel = new TextCompletionSessionKernel();
		var source = new StringTextSnapshot("alpha beta");
		var wordSpan = new TextCompletionWordSpan(string.Empty, new TextRange(5, 0));

		TextCompletionSessionDecision decision = kernel.GetDecision(source, 10, new StubProvider(s_items), wordSpan: wordSpan);

		// The empty word keeps all three items (the default locator would filter by "beta"), and the
		// explicit range 5..5 differs from the locator's 6..10 range.
		Assert.IsNotNull(decision.Items);
		Assert.AreEqual(3, decision.Items.Count);
		Assert.AreEqual(5, decision.StartOffset);
		Assert.AreEqual(5, decision.EndOffset);
	}

	[TestMethod]
	public void GetDecision_ExplicitWordSpanOutsideTheSnapshot_Throws()
	{
		var kernel = new TextCompletionSessionKernel();
		var source = new StringTextSnapshot("Bet");
		var wordSpan = new TextCompletionWordSpan("Bet", new TextRange(0, 99));

		// The replacement range is snapshot-relative, so an explicit range that exceeds the snapshot
		// is rejected instead of reaching a decision with out-of-document offsets.
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => kernel.GetDecision(source, 3, new StubProvider(s_items), wordSpan: wordSpan));
	}

	[TestMethod]
	public void GetDecision_ExplicitWordSpan_DoesNotInvokeTheConfiguredLocator()
	{
		int locatorCalls = 0;

		TextCompletionWordSpan Locator(ITextSnapshot snapshot, int caretOffset)
		{
			locatorCalls++;
			return new TextCompletionWordSpan(string.Empty, new TextRange(caretOffset, 0));
		}

		var kernel = new TextCompletionSessionKernel(wordLocator: Locator);
		var source = new StringTextSnapshot("alpha beta");
		var wordSpan = new TextCompletionWordSpan("beta", new TextRange(6, 4));

		TextCompletionSessionDecision decision = kernel.GetDecision(source, 10, new StubProvider(s_items), wordSpan: wordSpan);

		Assert.AreEqual(0, locatorCalls, "An explicit word must suppress the configured locator.");
		Assert.IsNotNull(decision.Items);
		Assert.AreEqual(1, decision.Items.Count);
	}

	[TestMethod]
	public void GetDecision_LocatorReturnsRangeOutsideTheSnapshot_Throws()
	{
		static TextCompletionWordSpan Locator(ITextSnapshot snapshot, int caretOffset)
			=> new(string.Empty, new TextRange(0, 99));

		var kernel = new TextCompletionSessionKernel(wordLocator: Locator);
		var source = new StringTextSnapshot("Bet");

		Assert.ThrowsExactly<InvalidOperationException>(() => kernel.GetDecision(source, 3, new StubProvider(s_items)));
	}

	[TestMethod]
	public void GetDecision_CustomFilter_ReceivesLocatedWord()
	{
		string? observedWord = null;

		IReadOnlyList<TextCompletionItem> Filter(IReadOnlyList<TextCompletionItem> items, string word)
		{
			observedWord = word;
			return items;
		}

		var kernel = new TextCompletionSessionKernel(filter: Filter);
		var source = new StringTextSnapshot("obj:Bet");

		kernel.GetDecision(source, 7, new StubProvider(s_items));

		Assert.AreEqual("Bet", observedWord);
	}

	[TestMethod]
	public void GetDecision_ProviderInvokedOnce()
	{
		var kernel = new TextCompletionSessionKernel();
		var provider = new StubProvider(s_items);
		var source = new StringTextSnapshot("Bet");

		kernel.GetDecision(source, 3, provider);

		Assert.AreEqual(1, provider.InvocationCount);
	}

	[TestMethod]
	public void GetDecision_CustomFilter_IsUsed()
	{
		static IReadOnlyList<TextCompletionItem> OnlyAlpha(IReadOnlyList<TextCompletionItem> items, string word)
			=> [items[0]];

		var kernel = new TextCompletionSessionKernel(filter: OnlyAlpha);
		var source = new StringTextSnapshot("Bet");

		TextCompletionSessionDecision decision = kernel.GetDecision(source, 3, new StubProvider(s_items));

		Assert.IsNotNull(decision.Items);
		Assert.AreEqual(1, decision.Items.Count);
		Assert.AreEqual("Alpha", decision.Items[0].InsertText);
	}

	[TestMethod]
	public void GetDecision_CustomFilterReturnsEmptyList_ReturnsNoMatches()
	{
		static IReadOnlyList<TextCompletionItem> EmptyFilter(IReadOnlyList<TextCompletionItem> items, string word) => [];

		var kernel = new TextCompletionSessionKernel(filter: EmptyFilter);
		var source = new StringTextSnapshot("Bet");

		TextCompletionSessionDecision decision = kernel.GetDecision(source, 3, new StubProvider(s_items));

		Assert.AreEqual(TextCompletionSessionDecision.NoMatches, decision);
	}

	[TestMethod]
	public void GetDecision_CustomFilterResult_IsAdoptedWithoutCopy()
	{
		// The kernel hands the filter's list to the decision as-is; copying it again would add an
		// allocation to every refresh.
		IReadOnlyList<TextCompletionItem> filtered = [new TextCompletionItem("Alpha")];

		IReadOnlyList<TextCompletionItem> Filter(IReadOnlyList<TextCompletionItem> items, string word) => filtered;

		var kernel = new TextCompletionSessionKernel(filter: Filter);
		var source = new StringTextSnapshot("Bet");

		TextCompletionSessionDecision decision = kernel.GetDecision(source, 3, new StubProvider(s_items));

		Assert.AreSame(filtered, decision.Items);
	}

	[TestMethod]
	public void GetDecision_CustomLocator_IsUsed()
	{
		static TextCompletionWordSpan LocateAfterColon(ITextSnapshot snapshot, int caretOffset)
		{
			int start = snapshot.GetText(0, caretOffset).LastIndexOf(':') + 1;
			return new TextCompletionWordSpan(snapshot.GetText(start, caretOffset - start), new TextRange(start, caretOffset - start));
		}

		var kernel = new TextCompletionSessionKernel(LocateAfterColon);
		var source = new StringTextSnapshot("key:Bet");

		TextCompletionSessionDecision decision = kernel.GetDecision(source, 7, new StubProvider(s_items));

		Assert.IsNotNull(decision.Items);
		Assert.AreEqual(1, decision.Items.Count);
		Assert.AreEqual(4, decision.StartOffset);
		Assert.AreEqual(7, decision.EndOffset);
	}

	[TestMethod]
	public void GetDecision_NullFilterResult_Throws()
	{
		static IReadOnlyList<TextCompletionItem> NullFilter(IReadOnlyList<TextCompletionItem> items, string word)
			=> null!;

		var kernel = new TextCompletionSessionKernel(filter: NullFilter);
		var source = new StringTextSnapshot("Bet");

		Assert.ThrowsExactly<InvalidOperationException>(() => kernel.GetDecision(source, 3, new StubProvider(s_items)));
	}

	[TestMethod]
	public void GetDecision_NullSnapshot_Throws()
	{
		var kernel = new TextCompletionSessionKernel();

		Assert.ThrowsExactly<ArgumentNullException>(() => kernel.GetDecision(null!, 0, new StubProvider(s_items)));
	}

	[TestMethod]
	public void GetDecision_NullProvider_Throws()
	{
		var kernel = new TextCompletionSessionKernel();
		var source = new StringTextSnapshot("Bet");

		Assert.ThrowsExactly<ArgumentNullException>(() => kernel.GetDecision(source, 3, null!));
	}

	[TestMethod]
	public void GetDecision_CaretOffsetOutOfRange_Throws()
	{
		var kernel = new TextCompletionSessionKernel();
		var source = new StringTextSnapshot("Bet");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => kernel.GetDecision(source, -1, new StubProvider(s_items)));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => kernel.GetDecision(source, 4, new StubProvider(s_items)));
	}

	[TestMethod]
	public void GetDecision_InvalidCaretWithCustomLocator_ThrowsBeforeTheLocatorRuns()
	{
		int locatorCalls = 0;

		TextCompletionWordSpan Locator(ITextSnapshot snapshot, int caretOffset)
		{
			locatorCalls++;
			return new TextCompletionWordSpan(string.Empty, new TextRange(caretOffset, 0));
		}

		var kernel = new TextCompletionSessionKernel(wordLocator: Locator);
		var source = new StringTextSnapshot("Bet");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => kernel.GetDecision(source, -1, new StubProvider(s_items)));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => kernel.GetDecision(source, 4, new StubProvider(s_items)));
		Assert.AreEqual(0, locatorCalls, "Caret validation must run before the configured locator.");
	}

	[TestMethod]
	public void GetDecision_ExplicitWordSpanWithInvalidCaret_Throws()
	{
		var kernel = new TextCompletionSessionKernel();
		var source = new StringTextSnapshot("Bet");
		var wordSpan = new TextCompletionWordSpan("Bet", new TextRange(0, 3));

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => kernel.GetDecision(source, -1, new StubProvider(s_items), wordSpan: wordSpan));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => kernel.GetDecision(source, 4, new StubProvider(s_items), wordSpan: wordSpan));
	}

	[TestMethod]
	public void GetDecision_ProviderReturnsNullItems_Throws()
	{
		var kernel = new TextCompletionSessionKernel();
		var source = new StringTextSnapshot("Bet");

		Assert.ThrowsExactly<InvalidOperationException>(() => kernel.GetDecision(source, 3, new NullItemsProvider()));
	}

	[TestMethod]
	public void GetDecision_ProviderReturnsNullItem_Throws()
	{
		var kernel = new TextCompletionSessionKernel();
		var source = new StringTextSnapshot("Bet");

		Assert.ThrowsExactly<InvalidOperationException>(
			() => kernel.GetDecision(source, 3, new StubProvider([null!])));
	}

	[TestMethod]
	public void GetDecision_FilterReturnsNullItem_Throws()
	{
		static IReadOnlyList<TextCompletionItem> NullItemFilter(IReadOnlyList<TextCompletionItem> items, string word)
			=> [null!];

		var kernel = new TextCompletionSessionKernel(filter: NullItemFilter);
		var source = new StringTextSnapshot("Bet");

		Assert.ThrowsExactly<InvalidOperationException>(() => kernel.GetDecision(source, 3, new StubProvider(s_items)));
	}

	// The probes pin the default locator as the integration of the kernel with Core's
	// IdentifierCharacterPolicy.Default (the identifier run ending at the caret defines both the word
	// and the replacement range); per-character policy details are covered by the Core test suite.
	// Expected item sets are explicit so a filtering regression cannot hide behind a predicate that
	// mirrors the production filter.
	private static IEnumerable<object[]> DefaultLocatorProbes()
	{
		yield return new object[] { "WordEndingAtCaret", "Bet", 3, 0, 3, new[] { "Beta" } };
		yield return new object[] { "BoundaryBeforeWord", "obj:Bet", 7, 4, 7, new[] { "Beta" } };
		yield return new object[] { "UnderscoreAndDigitsInWord", "value A_B2", 10, 6, 10, new[] { "A_B2" } };
		yield return new object[] { "CaretStopsWordAtBoundary", "Bet#suffix", 3, 0, 3, new[] { "Beta" } };
		yield return new object[] { "CaretInsideIdentifier", "Alphabet", 3, 0, 3, new[] { "Alpha" } };
		yield return new object[] { "CaretAtDocumentStart", "T", 0, 0, 0, s_allProbeLabels };
		yield return new object[] { "WordBeforeColonBoundary", "key: ", 5, 5, 5, s_allProbeLabels };
		yield return new object[] { "WordAfterDotBoundary", "abc.def", 7, 4, 7, new[] { "def" } };
		yield return new object[] { "CaretAfterTrailingDot", "abc.", 4, 4, 4, s_allProbeLabels };
		yield return new object[] { "DigitsOnlyWord", "123", 3, 0, 3, new[] { "123" } };
		yield return new object[] { "NonAsciiWord", "välue", 5, 0, 5, new[] { "välue" } };
		yield return new object[] { "EmptySnapshot", string.Empty, 0, 0, 0, s_allProbeLabels };
	}

	public static string GetProbeDisplayName(MethodInfo methodInfo, object[] values)
		=> $"{methodInfo.Name} ({values[0]})";

	[TestMethod]
	[DynamicData(nameof(DefaultLocatorProbes), DynamicDataDisplayName = nameof(GetProbeDisplayName))]
	public void GetDecision_DefaultLocator_ResolvesWordAndFiltersItems(
		string probeName, string text, int caretOffset, int expectedStartOffset, int expectedEndOffset, string[] expectedLabels)
	{
		var kernel = new TextCompletionSessionKernel();
		var source = new StringTextSnapshot(text);

		TextCompletionSessionDecision decision = kernel.GetDecision(source, caretOffset, new StubProvider(s_probeItems));

		Assert.IsNotNull(decision.Items, $"'{probeName}' must open a session.");
		CollectionAssert.AreEqual(
			expectedLabels,
			decision.Items.Select(item => item.Label).ToArray(),
			$"'{probeName}' item set differs.");
		Assert.AreEqual(expectedStartOffset, decision.StartOffset, $"'{probeName}' replacement start differs.");
		Assert.AreEqual(expectedEndOffset, decision.EndOffset, $"'{probeName}' replacement end differs.");
	}

	[TestMethod]
	public void GetDecision_DefaultLocator_NoMatchingWord_ReturnsNoMatches()
	{
		var kernel = new TextCompletionSessionKernel();
		var source = new StringTextSnapshot("Zzz");

		TextCompletionSessionDecision decision = kernel.GetDecision(source, 3, new StubProvider(s_probeItems));

		Assert.AreEqual(TextCompletionSessionDecision.NoMatches, decision);
	}

	[TestMethod]
	public void GetDecision_ProviderReceivesSnapshotTextAndTrigger()
	{
		var kernel = new TextCompletionSessionKernel();
		var provider = new StubProvider(s_items);
		var source = new StringTextSnapshot("Gamma");
		TextCompletionTrigger[] triggers =
		[
			TextCompletionTrigger.Invoked,
			TextCompletionTrigger.CreateCustom("EmptyLine"),
			TextCompletionTrigger.CreateCustom("Contextual"),
			TextCompletionTrigger.CreateCustom("Word")
		];

		foreach (TextCompletionTrigger trigger in triggers)
		{
			kernel.GetDecision(source, 5, provider, trigger);

			Assert.IsNotNull(provider.LastRequest);
			Assert.AreEqual("Gamma", provider.LastRequest.DocumentText);
			Assert.AreEqual(5, provider.LastRequest.CaretOffset);
			Assert.AreSame(trigger, provider.LastRequest.Trigger);
		}
	}

	[TestMethod]
	public void GetDecision_NoTriggerSupplied_UsesInvokedDefault()
	{
		var kernel = new TextCompletionSessionKernel();
		var provider = new StubProvider(s_items);

		kernel.GetDecision(new StringTextSnapshot("Gamma"), 5, provider);

		Assert.IsNotNull(provider.LastRequest);
		Assert.AreSame(TextCompletionTrigger.Invoked, provider.LastRequest.Trigger);
	}

	[TestMethod]
	public void Constructor_NullLocatorAndFilter_UsesTheSharedDefaults()
	{
		var kernel = new TextCompletionSessionKernel(null, null);
		var source = new StringTextSnapshot("Bet");

		TextCompletionSessionDecision decision = kernel.GetDecision(source, 3, new StubProvider(s_items));

		Assert.IsNotNull(decision.Items);
		Assert.AreEqual(1, decision.Items.Count);
		Assert.AreEqual("Beta: ", decision.Items[0].InsertText);
		Assert.AreEqual(0, decision.StartOffset);
		Assert.AreEqual(3, decision.EndOffset);
	}

	[TestMethod]
	public void GetDecision_DefaultWordSpan_UsesEmptyWordAndKeepsAllItems()
	{
		// An explicit default(TextCompletionWordSpan) value carries no word text, so it must behave
		// like an empty word (keep all items) instead of failing the filter's null check.
		var kernel = new TextCompletionSessionKernel();
		var provider = new StubProvider(s_items);
		var source = new StringTextSnapshot("Gamma");

		TextCompletionSessionDecision decision = kernel.GetDecision(source, 5, provider, wordSpan: default(TextCompletionWordSpan));

		Assert.IsNotNull(decision.Items);
		Assert.AreEqual(s_items.Count, decision.Items.Count);
	}

	private sealed class StubProvider(IReadOnlyList<TextCompletionItem>? items = null) : ITextCompletionProvider
	{
		private readonly IReadOnlyList<TextCompletionItem> _items = items ?? [];

		public TextCompletionRequest? LastRequest { get; private set; }

		public int InvocationCount { get; private set; }

		public IReadOnlyList<TextCompletionItem> GetCompletionItems(TextCompletionRequest request)
		{
			InvocationCount++;
			LastRequest = request;
			return _items;
		}
	}

	private sealed class NullItemsProvider : ITextCompletionProvider
	{
		public IReadOnlyList<TextCompletionItem> GetCompletionItems(TextCompletionRequest request) => null!;
	}
}
