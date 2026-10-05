using System.Reflection;
using Nickelony.IDEKit.Core.Identifiers;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
public sealed partial class CompletionSessionKernelTests
{
	private const string DocumentId = "doc.lua";

	private static readonly IReadOnlyList<CompletionItem> s_items = TestCompletionCatalog.Items;

	private static readonly IReadOnlyList<CompletionItem> s_probeItems =
	[
		new CompletionItem("Beta"),
		new CompletionItem("A_B2"),
		new CompletionItem("key"),
		new CompletionItem("def"),
		new CompletionItem("123"),
		new CompletionItem("välue"),
		new CompletionItem("Alpha"),
	];

	private static readonly string[] s_allProbeLabels = ["Beta", "A_B2", "key", "def", "123", "välue", "Alpha"];

	[TestMethod]
	public async Task GetDirectiveAsync_ProviderReturnsNoItems_ReturnsNone()
	{
		var kernel = new CompletionSessionKernel();
		var source = new StringTextSnapshot("Alpha");

		CompletionSessionDirective decision = await kernel.GetDirectiveAsync(
			DocumentId,
			snapshot: source,
			caretOffset: 5,
			provider: new StubProvider()
		);

		Assert.AreEqual(CompletionSessionDirective.None, decision);
	}

	[TestMethod]
	public async Task GetDirectiveAsync_ExplicitWordSpan_OverridesDefaultLocator()
	{
		var kernel = new CompletionSessionKernel();
		var source = new StringTextSnapshot("alpha beta");
		var wordSpan = new CompletionWordSpan(string.Empty, new TextRange(10, 0));

		CompletionSessionDirective decision = await kernel.GetDirectiveForWordSpanAsync(
			DocumentId,
			snapshot: source,
			caretOffset: 10,
			provider: new StubProvider(s_items),
			wordSpan: wordSpan
		);

		// The empty word keeps every item (the default locator would filter by "beta"), and the
		// explicit range 10..10 differs from the locator's 6..10 range.
		Assert.IsNotNull(decision.Items);
		Assert.HasCount(3, decision.Items);
		Assert.AreEqual(new TextRange(10, 0), decision.ReplacementRange);
	}

	[TestMethod]
	[DataRow(0, 0)]
	[DataRow(4, 2)]
	public void GetDirectiveAsync_ExplicitWordSpanThatDoesNotContainTheCaret_Throws(int offset, int length)
	{
		var kernel = new CompletionSessionKernel();
		var source = new StringTextSnapshot("alpha beta");
		var wordSpan = new CompletionWordSpan("beta", new TextRange(offset, length));

		// A span away from the caret, an unset default one among them, would replace text the user is not typing.
		Assert.ThrowsExactly<ArgumentException>(() =>
			kernel.GetDirectiveForWordSpanAsync(
				DocumentId,
				snapshot: source,
				caretOffset: 10,
				provider: new StubProvider(s_items),
				wordSpan: wordSpan
			)
		);
	}

	[TestMethod]
	public void GetDirectiveAsync_DefaultWordSpanAwayFromTheDocumentStart_Throws()
	{
		var kernel = new CompletionSessionKernel();
		var source = new StringTextSnapshot("alpha beta");

		Assert.ThrowsExactly<ArgumentException>(() =>
			kernel.GetDirectiveForWordSpanAsync(DocumentId, snapshot: source, caretOffset: 10, provider: new StubProvider(s_items), wordSpan: default)
		);
	}

	[TestMethod]
	public void GetDirectiveAsync_ExplicitWordSpanOutsideTheSnapshot_Throws()
	{
		var kernel = new CompletionSessionKernel();
		var source = new StringTextSnapshot("Bet");
		var wordSpan = new CompletionWordSpan("Bet", new TextRange(0, 99));

		// The replacement range is snapshot-relative, so an explicit range that exceeds the snapshot
		// is rejected instead of reaching a decision with out-of-document offsets.
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			kernel.GetDirectiveForWordSpanAsync(DocumentId, snapshot: source, caretOffset: 3, provider: new StubProvider(s_items), wordSpan: wordSpan)
		);
	}

	[TestMethod]
	public async Task GetDirectiveAsync_ExplicitWordSpan_DoesNotInvokeTheConfiguredLocator()
	{
		int locatorCalls = 0;

		CompletionWordSpan Locator(ITextSnapshot snapshot, int caretOffset)
		{
			locatorCalls++;
			return new CompletionWordSpan(string.Empty, new TextRange(caretOffset, 0));
		}

		var kernel = new CompletionSessionKernel(wordLocator: Locator);
		var source = new StringTextSnapshot("alpha beta");
		var wordSpan = new CompletionWordSpan("beta", new TextRange(6, 4));

		CompletionSessionDirective decision = await kernel.GetDirectiveForWordSpanAsync(
			DocumentId,
			snapshot: source,
			caretOffset: 10,
			provider: new StubProvider(s_items),
			wordSpan: wordSpan
		);

		Assert.AreEqual(0, locatorCalls, "An explicit word must suppress the configured locator.");
		Assert.IsNotNull(decision.Items);
		Assert.HasCount(1, decision.Items);
	}

	[TestMethod]
	public async Task GetDirectiveAsync_LocatorReturnsRangeOutsideTheSnapshot_Throws()
	{
		static CompletionWordSpan Locator(ITextSnapshot snapshot, int caretOffset) => new(string.Empty, new TextRange(0, 99));

		var kernel = new CompletionSessionKernel(wordLocator: Locator);
		var source = new StringTextSnapshot("Bet");

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
			kernel.GetDirectiveAsync(DocumentId, snapshot: source, caretOffset: 3, provider: new StubProvider(s_items))
		);
	}

	[TestMethod]
	public async Task GetDirectiveAsync_CustomFilter_ReceivesLocatedWord()
	{
		string? observedWord = null;

		IReadOnlyList<CompletionItem> Filter(IReadOnlyList<CompletionItem> items, string word)
		{
			observedWord = word;
			return items;
		}

		var kernel = new CompletionSessionKernel(filter: Filter);
		var source = new StringTextSnapshot("obj:Bet");

		await kernel.GetDirectiveAsync(DocumentId, snapshot: source, caretOffset: 7, provider: new StubProvider(s_items));

		Assert.AreEqual("Bet", observedWord);
	}

	[TestMethod]
	public async Task GetDirectiveAsync_ProviderInvokedOnce()
	{
		var kernel = new CompletionSessionKernel();
		var provider = new StubProvider(s_items);
		var source = new StringTextSnapshot("Bet");

		await kernel.GetDirectiveAsync(DocumentId, provider, snapshot: source, caretOffset: 3);

		Assert.AreEqual(1, provider.InvocationCount);
	}

	[TestMethod]
	public async Task GetDirectiveAsync_CustomFilter_IsUsed()
	{
		static IReadOnlyList<CompletionItem> OnlyAlpha(IReadOnlyList<CompletionItem> items, string word) => [items[0]];

		var kernel = new CompletionSessionKernel(filter: OnlyAlpha);
		var source = new StringTextSnapshot("Bet");

		CompletionSessionDirective decision = await kernel.GetDirectiveAsync(
			DocumentId,
			snapshot: source,
			caretOffset: 3,
			provider: new StubProvider(s_items)
		);

		Assert.IsNotNull(decision.Items);
		Assert.HasCount(1, decision.Items);
		Assert.AreEqual("Alpha", decision.Items[0].InsertText);
	}

	[TestMethod]
	public async Task GetDirectiveAsync_CustomFilterReturnsEmptyList_ReturnsNoMatches()
	{
		static IReadOnlyList<CompletionItem> EmptyFilter(IReadOnlyList<CompletionItem> items, string word) => [];

		var kernel = new CompletionSessionKernel(filter: EmptyFilter);
		var source = new StringTextSnapshot("Bet");

		CompletionSessionDirective decision = await kernel.GetDirectiveAsync(
			DocumentId,
			snapshot: source,
			caretOffset: 3,
			provider: new StubProvider(s_items)
		);

		Assert.AreEqual(CompletionSessionDirective.NoMatches, decision);
	}

	[TestMethod]
	public async Task GetDirectiveAsync_CustomFilterResult_IsAdoptedWithoutCopy()
	{
		// The kernel hands the filter's list to the decision as-is; copying it again would add an
		// allocation to every refresh.
		IReadOnlyList<CompletionItem> filtered = [new CompletionItem("Alpha")];

		IReadOnlyList<CompletionItem> Filter(IReadOnlyList<CompletionItem> items, string word) => filtered;

		var kernel = new CompletionSessionKernel(filter: Filter);
		var source = new StringTextSnapshot("Bet");

		CompletionSessionDirective decision = await kernel.GetDirectiveAsync(
			DocumentId,
			snapshot: source,
			caretOffset: 3,
			provider: new StubProvider(s_items)
		);

		Assert.AreSame(filtered, decision.Items);
	}

	[TestMethod]
	public async Task GetDirectiveAsync_CustomLocator_IsUsed()
	{
		static CompletionWordSpan LocateAfterColon(ITextSnapshot snapshot, int caretOffset)
		{
			int start = snapshot.GetText(0, caretOffset).LastIndexOf(':') + 1;
			return new CompletionWordSpan(snapshot.GetText(start, caretOffset - start), new TextRange(start, caretOffset - start));
		}

		var kernel = new CompletionSessionKernel(LocateAfterColon);
		var source = new StringTextSnapshot("key:Bet");

		CompletionSessionDirective decision = await kernel.GetDirectiveAsync(
			DocumentId,
			snapshot: source,
			caretOffset: 7,
			provider: new StubProvider(s_items)
		);

		Assert.IsNotNull(decision.Items);
		Assert.HasCount(1, decision.Items);
		Assert.AreEqual(TextRange.FromBounds(4, 7), decision.ReplacementRange);
	}

	[TestMethod]
	public async Task GetDirectiveAsync_NullFilterResult_Throws()
	{
		static IReadOnlyList<CompletionItem> NullFilter(IReadOnlyList<CompletionItem> items, string word) => null!;

		var kernel = new CompletionSessionKernel(filter: NullFilter);
		var source = new StringTextSnapshot("Bet");

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
			kernel.GetDirectiveAsync(DocumentId, snapshot: source, caretOffset: 3, provider: new StubProvider(s_items))
		);
	}

	[TestMethod]
	public void GetDirectiveAsync_NullSnapshot_Throws()
	{
		var kernel = new CompletionSessionKernel();

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			kernel.GetDirectiveAsync(DocumentId, snapshot: null!, caretOffset: 0, provider: new StubProvider(s_items))
		);
	}

	[TestMethod]
	public void GetDirectiveAsync_NullProvider_Throws()
	{
		var kernel = new CompletionSessionKernel();
		var source = new StringTextSnapshot("Bet");

		Assert.ThrowsExactly<ArgumentNullException>(() => kernel.GetDirectiveAsync(DocumentId, snapshot: source, caretOffset: 3, provider: null!));
	}

	[TestMethod]
	public void GetDirectiveAsync_CaretOffsetOutOfRange_Throws()
	{
		var kernel = new CompletionSessionKernel();
		var source = new StringTextSnapshot("Bet");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			kernel.GetDirectiveAsync(DocumentId, snapshot: source, caretOffset: -1, provider: new StubProvider(s_items))
		);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			kernel.GetDirectiveAsync(DocumentId, snapshot: source, caretOffset: 4, provider: new StubProvider(s_items))
		);
	}

	[TestMethod]
	public void GetDirectiveAsync_InvalidCaretWithCustomLocator_ThrowsBeforeTheLocatorRuns()
	{
		int locatorCalls = 0;

		CompletionWordSpan Locator(ITextSnapshot snapshot, int caretOffset)
		{
			locatorCalls++;
			return new CompletionWordSpan(string.Empty, new TextRange(caretOffset, 0));
		}

		var kernel = new CompletionSessionKernel(wordLocator: Locator);
		var source = new StringTextSnapshot("Bet");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			kernel.GetDirectiveAsync(DocumentId, snapshot: source, caretOffset: -1, provider: new StubProvider(s_items))
		);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			kernel.GetDirectiveAsync(DocumentId, snapshot: source, caretOffset: 4, provider: new StubProvider(s_items))
		);

		Assert.AreEqual(0, locatorCalls, "Caret validation must run before the configured locator.");
	}

	[TestMethod]
	public void GetDirectiveAsync_ExplicitWordSpanWithInvalidCaret_Throws()
	{
		var kernel = new CompletionSessionKernel();
		var source = new StringTextSnapshot("Bet");
		var wordSpan = new CompletionWordSpan("Bet", new TextRange(0, 3));

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			kernel.GetDirectiveForWordSpanAsync(
				DocumentId,
				snapshot: source,
				caretOffset: -1,
				provider: new StubProvider(s_items),
				wordSpan: wordSpan
			)
		);

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			kernel.GetDirectiveForWordSpanAsync(DocumentId, snapshot: source, caretOffset: 4, provider: new StubProvider(s_items), wordSpan: wordSpan)
		);
	}

	[TestMethod]
	public async Task GetDirectiveAsync_ProviderReturnsNullItems_Throws()
	{
		var kernel = new CompletionSessionKernel();
		var source = new StringTextSnapshot("Bet");

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
			kernel.GetDirectiveAsync(DocumentId, snapshot: source, caretOffset: 3, provider: new NullItemsProvider())
		);
	}

	[TestMethod]
	public async Task GetDirectiveAsync_ProviderReturnsNullItem_Throws()
	{
		var kernel = new CompletionSessionKernel();
		var source = new StringTextSnapshot("Bet");

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
			kernel.GetDirectiveAsync(DocumentId, snapshot: source, caretOffset: 3, provider: new StubProvider([null!]))
		);
	}

	[TestMethod]
	public async Task GetDirectiveAsync_FilterReturnsNullItem_Throws()
	{
		static IReadOnlyList<CompletionItem> NullItemFilter(IReadOnlyList<CompletionItem> items, string word) => [null!];

		var kernel = new CompletionSessionKernel(filter: NullItemFilter);
		var source = new StringTextSnapshot("Bet");

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
			kernel.GetDirectiveAsync(DocumentId, snapshot: source, caretOffset: 3, provider: new StubProvider(s_items))
		);
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

	public static string GetProbeDisplayName(MethodInfo methodInfo, object[] values) => $"{methodInfo.Name} ({values[0]})";

	[TestMethod]
	[DynamicData(nameof(DefaultLocatorProbes), DynamicDataDisplayName = nameof(GetProbeDisplayName))]
	public async Task GetDirectiveAsync_DefaultLocator_ResolvesWordAndFiltersItems(
		string probeName,
		string text,
		int caretOffset,
		int expectedStartOffset,
		int expectedEndOffset,
		string[] expectedLabels
	)
	{
		var kernel = new CompletionSessionKernel();
		var source = new StringTextSnapshot(text);

		CompletionSessionDirective decision = await kernel.GetDirectiveAsync(
			DocumentId,
			snapshot: source,
			caretOffset: caretOffset,
			provider: new StubProvider(s_probeItems)
		);

		Assert.IsNotNull(decision.Items, $"'{probeName}' must open a session.");
		Assert.AreSequenceEqual(expectedLabels, [.. decision.Items.Select(item => item.Label)], $"'{probeName}' item set differs.");

		Assert.AreEqual(
			TextRange.FromBounds(expectedStartOffset, expectedEndOffset),
			decision.ReplacementRange,
			$"'{probeName}' replacement range differs."
		);
	}

	[TestMethod]
	public async Task GetDirectiveAsync_DefaultLocator_NoMatchingWord_ReturnsNoMatches()
	{
		var kernel = new CompletionSessionKernel();
		var source = new StringTextSnapshot("Zzz");

		CompletionSessionDirective decision = await kernel.GetDirectiveAsync(
			DocumentId,
			snapshot: source,
			caretOffset: 3,
			provider: new StubProvider(s_probeItems)
		);

		Assert.AreEqual(CompletionSessionDirective.NoMatches, decision);
	}

	[TestMethod]
	public async Task GetDirectiveAsync_ProviderReceivesSnapshotTextAndTrigger()
	{
		var kernel = new CompletionSessionKernel();
		var provider = new StubProvider(s_items);
		var source = new StringTextSnapshot("Gamma");

		CompletionContext[] triggers =
		[
			CompletionContext.Invoked,
			CompletionContext.CreateCustom("EmptyLine"),
			CompletionContext.CreateCustom("Contextual"),
			CompletionContext.CreateCustom("Word"),
		];

		foreach (CompletionContext trigger in triggers)
		{
			await kernel.GetDirectiveAsync(DocumentId, provider, snapshot: source, caretOffset: 5, context: trigger);

			Assert.IsNotNull(provider.LastRequest);
			Assert.AreEqual("Gamma", provider.LastRequest.DocumentText);
			Assert.AreEqual(5, provider.LastRequest.CaretOffset);
			Assert.AreSame(trigger, provider.LastRequest.Context);
		}
	}

	[TestMethod]
	public async Task GetDirectiveAsync_NoTriggerSupplied_UsesInvokedDefault()
	{
		var kernel = new CompletionSessionKernel();
		var provider = new StubProvider(s_items);

		await kernel.GetDirectiveAsync(DocumentId, provider, snapshot: new StringTextSnapshot("Gamma"), caretOffset: 5);

		Assert.IsNotNull(provider.LastRequest);
		Assert.AreSame(CompletionContext.Invoked, provider.LastRequest.Context);
	}

	[TestMethod]
	public async Task Constructor_NullLocatorAndFilter_UsesTheSharedDefaults()
	{
		var kernel = new CompletionSessionKernel(null, null);
		var source = new StringTextSnapshot("Bet");

		CompletionSessionDirective decision = await kernel.GetDirectiveAsync(
			DocumentId,
			snapshot: source,
			caretOffset: 3,
			provider: new StubProvider(s_items)
		);

		Assert.IsNotNull(decision.Items);
		Assert.HasCount(1, decision.Items);
		Assert.AreEqual("Beta: ", decision.Items[0].InsertText);
		Assert.AreEqual(TextRange.FromBounds(0, 3), decision.ReplacementRange);
	}

	[TestMethod]
	public async Task GetDirectiveAsync_DefaultWordSpan_UsesEmptyWordAndKeepsAllItems()
	{
		// An explicit default(CompletionWordSpan) value carries no word text, so at the document start, the one caret its empty range
		// contains, it must behave like an empty word (keep all items) instead of failing the filter's null check.
		var kernel = new CompletionSessionKernel();
		var provider = new StubProvider(s_items);
		var source = new StringTextSnapshot("Gamma");

		CompletionSessionDirective decision = await kernel.GetDirectiveForWordSpanAsync(
			DocumentId,
			provider,
			snapshot: source,
			caretOffset: 0,
			wordSpan: default
		);

		Assert.IsNotNull(decision.Items);
		Assert.HasCount(s_items.Count, decision.Items);
	}

	[TestMethod]
	public async Task GetDirectiveAsync_TokenCanceledBeforeTheCall_ThrowsWithoutCallingTheProvider()
	{
		var kernel = new CompletionSessionKernel();
		var provider = new StubProvider(s_items);

		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
			kernel.GetDirectiveAsync(
				DocumentId,
				snapshot: new StringTextSnapshot("Bet"),
				caretOffset: 3,
				provider: provider,
				cancellationToken: cancellation.Token
			)
		);

		Assert.AreEqual(0, provider.InvocationCount);
	}

	[TestMethod]
	public async Task GetDirectiveAsync_TokenCanceledWhileTheProviderIgnoresIt_ThrowsInsteadOfReturningADecision()
	{
		var kernel = new CompletionSessionKernel();

		using var cancellation = new CancellationTokenSource();
		var provider = new CancelingProvider(cancellation, s_items);

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
			kernel.GetDirectiveAsync(
				DocumentId,
				snapshot: new StringTextSnapshot("Bet"),
				caretOffset: 3,
				provider: provider,
				cancellationToken: cancellation.Token
			)
		);
	}

	private sealed class CancelingProvider(CancellationTokenSource source, IReadOnlyList<CompletionItem> items) : ICompletionProvider
	{
		// Cancels the request and still answers with a completed task, like a provider that does not observe its token.
		public Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(CompletionRequest request, CancellationToken cancellationToken = default)
		{
			source.Cancel();
			return Task.FromResult(items);
		}
	}

	private sealed class StubProvider(IReadOnlyList<CompletionItem>? items = null) : ICompletionProvider
	{
		private readonly IReadOnlyList<CompletionItem> _items = items ?? [];

		public CompletionRequest? LastRequest { get; private set; }

		public int InvocationCount { get; private set; }

		public Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(CompletionRequest request, CancellationToken cancellationToken = default)
		{
			InvocationCount++;
			LastRequest = request;
			return Task.FromResult(_items);
		}
	}

	private sealed class NullItemsProvider : ICompletionProvider
	{
		public Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(
			CompletionRequest request,
			CancellationToken cancellationToken = default
		) => Task.FromResult<IReadOnlyList<CompletionItem>>(null!);
	}
}
