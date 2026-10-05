using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

[TestClass]
public sealed class CompletionSessionDirectiveTests
{
	[TestMethod]
	public void None_LeavesSessionUnchanged()
	{
		Assert.IsFalse(CompletionSessionDirective.None.ShouldClose);
		Assert.IsNull(CompletionSessionDirective.None.Items);
		Assert.IsNull(CompletionSessionDirective.None.ReplacementRange);
	}

	[TestMethod]
	public void Close_DismissesSessionWithoutItems()
	{
		Assert.IsTrue(CompletionSessionDirective.Close.ShouldClose);
		Assert.IsNull(CompletionSessionDirective.Close.Items);
	}

	[TestMethod]
	public void Open_CarriesItemsAndReplacementRange()
	{
		var item = new CompletionItem("label");

		var decision = CompletionSessionDirective.Open([item], TextRange.FromBounds(2, 6));

		Assert.IsFalse(decision.ShouldClose);
		Assert.IsNotNull(decision.Items);
		Assert.HasCount(1, decision.Items);
		Assert.AreSame(item, decision.Items[0]);
		Assert.AreEqual(new TextRange(2, 4), decision.ReplacementRange);
	}

	[TestMethod]
	public void Open_CapturesCurrentItemsWithoutObservingLaterChanges()
	{
		var item = new CompletionItem("first");
		var items = new List<CompletionItem> { item };

		var decision = CompletionSessionDirective.Open(items, TextRange.FromBounds(0, 5));

		items.Clear();
		items.Add(new CompletionItem("second"));

		Assert.IsNotNull(decision.Items);
		Assert.HasCount(1, decision.Items);
		Assert.AreSame(item, decision.Items[0]);
	}

	[TestMethod]
	public void Equality_ComparesItemsByListReference()
	{
		// The documented equality contract: two decisions that carry equal item sequences are not
		// equal unless they share the same list instance.
		var item = new CompletionItem("label");
		var first = CompletionSessionDirective.Open([item], TextRange.FromBounds(0, 5));
		var second = CompletionSessionDirective.Open([item], TextRange.FromBounds(0, 5));
		var sharedItems = new List<CompletionItem> { item };
		var third = new CompletionSessionDirective(false, sharedItems, new TextRange(0, 5));

		Assert.AreNotEqual(first, second);
		Assert.AreEqual(third, new CompletionSessionDirective(false, sharedItems, new TextRange(0, 5)));
	}

	[TestMethod]
	public void Constructor_ItemsWithoutReplacementRange_Throws() =>
		Assert.ThrowsExactly<ArgumentException>(() => new CompletionSessionDirective(false, [new CompletionItem("label")]));

	[TestMethod]
	public void Constructor_ReplacementRangeWithoutItems_Throws() =>
		Assert.ThrowsExactly<ArgumentException>(() => new CompletionSessionDirective(false, null, new TextRange(2, 4)));

	[TestMethod]
	public void Constructor_CloseWithItems_IsAllowed()
	{
		// A decision may request a close and carry a replacement; a host applies the close first.
		CompletionSessionDirective decision = CompletionSessionDirective.Open([new CompletionItem("label")], TextRange.FromBounds(0, 5)) with
		{
			ShouldClose = true,
		};

		Assert.IsTrue(decision.ShouldClose);
		Assert.HasCount(1, decision.Items);
		Assert.AreEqual(new TextRange(0, 5), decision.ReplacementRange);
	}

	[TestMethod]
	public void Deconstruct_ReportsTheStateComponents()
	{
		var items = new List<CompletionItem> { new("label") };
		var decision = new CompletionSessionDirective(false, items, new TextRange(1, 3));

		(bool shouldClose, IReadOnlyList<CompletionItem>? decisionItems, TextRange? replacementRange, bool allItemsFilteredOut) = decision;

		Assert.IsFalse(shouldClose);
		Assert.AreSame(items, decisionItems);
		Assert.AreEqual(new TextRange(1, 3), replacementRange);
		Assert.IsFalse(allItemsFilteredOut);

		(bool _, IReadOnlyList<CompletionItem>? _, TextRange? _, bool filteredOut) = CompletionSessionDirective.NoMatches;

		Assert.IsTrue(filteredOut);
	}

	[TestMethod]
	public void NoMatches_ReportsTheFilteredEmptyState()
	{
		CompletionSessionDirective decision = CompletionSessionDirective.NoMatches;

		Assert.IsFalse(decision.ShouldClose);
		Assert.IsNull(decision.Items);
		Assert.IsNull(decision.ReplacementRange);
		Assert.IsTrue(decision.AllItemsFilteredOut);
		Assert.AreNotEqual(CompletionSessionDirective.None, decision);
	}

	[TestMethod]
	public void None_IsValueEqualToDefault()
	{
		Assert.AreEqual(default, CompletionSessionDirective.None);
		Assert.IsFalse(CompletionSessionDirective.None.AllItemsFilteredOut);
	}

	[TestMethod]
	public void Open_EmptyItems_Throws() => Assert.ThrowsExactly<ArgumentException>(() => CompletionSessionDirective.Open([], new TextRange(0, 5)));

	[TestMethod]
	public void Constructor_EmptyItems_Throws() =>
		Assert.ThrowsExactly<ArgumentException>(() => new CompletionSessionDirective(false, [], new TextRange(0, 5)));

	[TestMethod]
	public void Constructor_MissingReplacementRange_ReportsTheReplacementRangeParameter()
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() =>
			new CompletionSessionDirective(false, [new CompletionItem("label")])
		);

		Assert.AreEqual("replacementRange", exception.ParamName);
	}
}
