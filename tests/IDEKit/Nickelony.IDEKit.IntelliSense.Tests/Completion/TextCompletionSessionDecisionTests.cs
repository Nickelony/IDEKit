using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

[TestClass]
public sealed class TextCompletionSessionDecisionTests
{
	[TestMethod]
	public void None_LeavesSessionUnchanged()
	{
		Assert.IsFalse(TextCompletionSessionDecision.None.ShouldClose);
		Assert.IsNull(TextCompletionSessionDecision.None.Items);
		Assert.IsNull(TextCompletionSessionDecision.None.StartOffset);
		Assert.IsNull(TextCompletionSessionDecision.None.EndOffset);
	}

	[TestMethod]
	public void Close_DismissesSessionWithoutItems()
	{
		Assert.IsTrue(TextCompletionSessionDecision.Close.ShouldClose);
		Assert.IsNull(TextCompletionSessionDecision.Close.Items);
	}

	[TestMethod]
	public void Open_CarriesItemsAndReplacementRange()
	{
		var item = new TextCompletionItem("label");

		TextCompletionSessionDecision decision = TextCompletionSessionDecision.Open([item], startOffset: 2, endOffset: 6);

		Assert.IsFalse(decision.ShouldClose);
		Assert.IsNotNull(decision.Items);
		Assert.AreEqual(1, decision.Items.Count);
		Assert.AreSame(item, decision.Items[0]);
		Assert.AreEqual(new TextRange(2, 4), decision.ReplacementRange);
		Assert.AreEqual(2, decision.StartOffset);
		Assert.AreEqual(6, decision.EndOffset);
	}

	[TestMethod]
	public void Open_CapturesCurrentItemsWithoutObservingLaterChanges()
	{
		var item = new TextCompletionItem("first");
		var items = new List<TextCompletionItem> { item };

		TextCompletionSessionDecision decision = TextCompletionSessionDecision.Open(items, startOffset: 0, endOffset: 5);

		items.Clear();
		items.Add(new TextCompletionItem("second"));

		Assert.IsNotNull(decision.Items);
		Assert.AreEqual(1, decision.Items.Count);
		Assert.AreSame(item, decision.Items[0]);
	}

	[TestMethod]
	public void Equality_ComparesItemsByListReference()
	{
		// The documented equality contract: two decisions that carry equal item sequences are not
		// equal unless they share the same list instance.
		var item = new TextCompletionItem("label");
		TextCompletionSessionDecision first = TextCompletionSessionDecision.Open([item], startOffset: 0, endOffset: 5);
		TextCompletionSessionDecision second = TextCompletionSessionDecision.Open([item], startOffset: 0, endOffset: 5);
		var sharedItems = new List<TextCompletionItem> { item };
		var third = new TextCompletionSessionDecision(false, sharedItems, new TextRange(0, 5));

		Assert.AreNotEqual(first, second);
		Assert.AreEqual(third, new TextCompletionSessionDecision(false, sharedItems, new TextRange(0, 5)));
	}

	[TestMethod]
	public void Open_NegativeStartOffset_Throws()
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextCompletionSessionDecision.Open([new TextCompletionItem("label")], startOffset: -1, endOffset: 3));

	[TestMethod]
	public void Open_EndOffsetBeforeStart_Throws()
		=> Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextCompletionSessionDecision.Open([new TextCompletionItem("label")], startOffset: 4, endOffset: 2));

	[TestMethod]
	public void Constructor_ItemsWithoutReplacementRange_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(
			() => new TextCompletionSessionDecision(false, [new TextCompletionItem("label")]));

	[TestMethod]
	public void Constructor_ReplacementRangeWithoutItems_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new TextCompletionSessionDecision(false, null, new TextRange(2, 4)));

	[TestMethod]
	public void Constructor_CloseWithItems_IsAllowed()
	{
		// A decision may request a close and carry a replacement; a host applies the close first.
		TextCompletionSessionDecision decision = TextCompletionSessionDecision
			.Open([new TextCompletionItem("label")], startOffset: 0, endOffset: 5)
			with
		{ ShouldClose = true };

		Assert.IsTrue(decision.ShouldClose);
		Assert.AreEqual(1, decision.Items!.Count);
		Assert.AreEqual(new TextRange(0, 5), decision.ReplacementRange);
	}

	[TestMethod]
	public void Deconstruct_ReportsTheStateComponents()
	{
		var items = new List<TextCompletionItem> { new("label") };
		var decision = new TextCompletionSessionDecision(false, items, new TextRange(1, 3));

		(bool shouldClose, IReadOnlyList<TextCompletionItem>? decisionItems, TextRange? replacementRange, bool allItemsFilteredOut) = decision;

		Assert.IsFalse(shouldClose);
		Assert.AreSame(items, decisionItems);
		Assert.AreEqual(new TextRange(1, 3), replacementRange);
		Assert.IsFalse(allItemsFilteredOut);

		(bool _, IReadOnlyList<TextCompletionItem>? _, TextRange? _, bool filteredOut) = TextCompletionSessionDecision.NoMatches;

		Assert.IsTrue(filteredOut);
	}

	[TestMethod]
	public void NoMatches_ReportsTheFilteredEmptyState()
	{
		TextCompletionSessionDecision decision = TextCompletionSessionDecision.NoMatches;

		Assert.IsFalse(decision.ShouldClose);
		Assert.IsNull(decision.Items);
		Assert.IsNull(decision.ReplacementRange);
		Assert.IsNull(decision.StartOffset);
		Assert.IsNull(decision.EndOffset);
		Assert.IsTrue(decision.AllItemsFilteredOut);
		Assert.AreNotEqual(TextCompletionSessionDecision.None, decision);
	}

	[TestMethod]
	public void None_IsValueEqualToDefault()
	{
		Assert.AreEqual(default(TextCompletionSessionDecision), TextCompletionSessionDecision.None);
		Assert.IsFalse(TextCompletionSessionDecision.None.AllItemsFilteredOut);
	}

	[TestMethod]
	public void Open_EmptyItems_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => TextCompletionSessionDecision.Open([], 0, 5));

	[TestMethod]
	public void Constructor_EmptyItems_Throws()
		=> Assert.ThrowsExactly<ArgumentException>(() => new TextCompletionSessionDecision(false, [], new TextRange(0, 5)));

	[TestMethod]
	public void Constructor_MissingReplacementRange_ReportsTheReplacementRangeParameter()
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
			() => new TextCompletionSessionDecision(false, [new TextCompletionItem("label")]));

		Assert.AreEqual("replacementRange", exception.ParamName);
	}
}
