using Nickelony.IDEKit.LanguageFeatures.Completion;
using static Nickelony.IDEKit.LanguageFeatures.Tests.CompletionItemTestHelpers;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

public sealed partial class CompletionItemTests
{
	[TestMethod]
	public async Task WithRequestContext_StampsItemButNotRawResolveResultAsync()
	{
		CompletionItem item = new CompletionItem("label").WithResolveCallback(_ =>
			Task.FromResult(new CompletionItem("label") { Detail = "resolved" })
		);

		CompletionItem stampedItem = item.WithRequestContext(requestDocumentVersion: 4, requestGeneration: 9);
		CompletionItem resolvedItem = await stampedItem.ResolveAsync(TestContext.CancellationToken).ConfigureAwait(false);

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
		var item = new CompletionItem("label") { RequestDocumentVersion = 4, RequestGeneration = 9 };
		Assert.AreSame(item, item.WithRequestContext(requestDocumentVersion: 4, requestGeneration: 9));
	}

	[TestMethod]
	public void RequestDocumentVersion_NegativeValue_Throws() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CompletionItem("label") { RequestDocumentVersion = -1 });

	[TestMethod]
	public void RequestGeneration_NegativeValue_Throws() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CompletionItem("label") { RequestGeneration = -1 });

	[TestMethod]
	public void WithRequestContext_NegativeArguments_Throw()
	{
		var item = new CompletionItem("label");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => item.WithRequestContext(-1, 0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => item.WithRequestContext(0, -1));
	}

	[TestMethod]
	public void WithRequestContext_PreservesEveryOtherField()
	{
		CompletionItem item = CreateFullyPopulatedItem();

		CompletionItem stampedItem = item.WithRequestContext(requestDocumentVersion: 4, requestGeneration: 9);

		AssertItemsHaveSamePayload(item, stampedItem, nameof(CompletionItem.RequestDocumentVersion), nameof(CompletionItem.RequestGeneration));

		Assert.AreEqual(4, stampedItem.RequestDocumentVersion);
		Assert.AreEqual(9, stampedItem.RequestGeneration);
		Assert.AreEqual("0001", stampedItem.SortText);
		Assert.IsTrue(stampedItem.IsPreselected);
	}

	public TestContext? TestContext { get; set; }
}
