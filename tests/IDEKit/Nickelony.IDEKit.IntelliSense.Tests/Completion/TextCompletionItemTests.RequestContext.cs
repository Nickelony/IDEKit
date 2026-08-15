using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

public sealed partial class TextCompletionItemTests
{
	[TestMethod]
	public async Task WithRequestContext_StampsItemButNotRawResolveResultAsync()
	{
		var item = new TextCompletionItem("label")
			.WithResolveCallback(_ => Task.FromResult(new TextCompletionItem("label") { Detail = "resolved" }));

		TextCompletionItem stampedItem = item.WithRequestContext(requestDocumentVersion: 4, requestGeneration: 9);
		TextCompletionItem resolvedItem = await stampedItem.ResolveAsync().ConfigureAwait(false);

		Assert.AreEqual(4, stampedItem.RequestDocumentVersion);
		Assert.AreEqual(9, stampedItem.RequestGeneration);

		// The raw resolve path does not stamp the resolved result; callers apply the stamps themselves
		// when they need them on a resolved item.
		Assert.IsNull(resolvedItem.RequestDocumentVersion);
		Assert.IsNull(resolvedItem.RequestGeneration);
		Assert.AreEqual("resolved", resolvedItem.Detail);
	}

	[TestMethod]
	public void WithRequestContext_SameContext_ReturnsSameItem()
	{
		var item = new TextCompletionItem("label") { RequestDocumentVersion = 4, RequestGeneration = 9 };

		Assert.AreSame(item, item.WithRequestContext(requestDocumentVersion: 4, requestGeneration: 9));
	}

	[TestMethod]
	public void RequestDocumentVersion_NegativeValue_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCompletionItem("label") { RequestDocumentVersion = -1 });
	}

	[TestMethod]
	public void RequestGeneration_NegativeValue_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCompletionItem("label") { RequestGeneration = -1 });
	}

	[TestMethod]
	public void WithRequestContext_NegativeArguments_Throw()
	{
		var item = new TextCompletionItem("label");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => item.WithRequestContext(-1, 0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => item.WithRequestContext(0, -1));
	}

	[TestMethod]
	public void WithRequestContext_PreservesEveryOtherField()
	{
		TextCompletionItem item = CreateFullyPopulatedItem();

		TextCompletionItem stampedItem = item.WithRequestContext(requestDocumentVersion: 4, requestGeneration: 9);

		AssertItemsHaveSamePayload(
			item,
			stampedItem,
			nameof(TextCompletionItem.RequestDocumentVersion),
			nameof(TextCompletionItem.RequestGeneration));
		Assert.AreEqual(4, stampedItem.RequestDocumentVersion);
		Assert.AreEqual(9, stampedItem.RequestGeneration);
		Assert.AreEqual("0001", stampedItem.SortText);
		Assert.IsTrue(stampedItem.IsPreselected);
	}
}
