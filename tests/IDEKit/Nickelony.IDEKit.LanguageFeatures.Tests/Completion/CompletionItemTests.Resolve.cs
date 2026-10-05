using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using static Nickelony.IDEKit.LanguageFeatures.Tests.CompletionItemTestHelpers;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

public sealed partial class CompletionItemTests
{
	[TestMethod]
	public void ResolveCallback_NotInitialized_CannotResolve()
	{
		var item = new CompletionItem("label");
		Assert.IsFalse(item.CanResolve);
	}

	[TestMethod]
	public async Task ResolveAsync_WithoutResolveCallback_ReturnsSameItem()
	{
		var item = new CompletionItem("label");

		CompletionItem resolvedItem = await item.ResolveAsync(TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreSame(item, resolvedItem);
	}

	[TestMethod]
	public async Task ResolveAsync_WithResolveCallback_InvokesCallback()
	{
		var resolvedContent = new CompletionItem("label") { Detail = "resolved" };
		using var cancellation = new CancellationTokenSource();

		CompletionItem item = new CompletionItem("label").WithResolveCallback(cancellationToken =>
		{
			Assert.AreEqual(cancellation.Token, cancellationToken);
			return Task.FromResult(resolvedContent);
		});

		CompletionItem resolvedItem = await item.ResolveAsync(cancellation.Token).ConfigureAwait(false);

		Assert.IsTrue(item.CanResolve);
		Assert.AreSame(resolvedContent, resolvedItem);
	}

	[TestMethod]
	public async Task ResolveAsync_CallbackReturnsNullItem_Throws()
	{
		CompletionItem item = new CompletionItem("label").WithResolveCallback(_ => Task.FromResult<CompletionItem>(null!));
		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => item.ResolveAsync(TestContext.CancellationToken)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ResolveAsync_CallbackReturnsNullTask_Throws()
	{
		CompletionItem item = new CompletionItem("label").WithResolveCallback(_ => null!);
		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => item.ResolveAsync(TestContext.CancellationToken)).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ResolveAsync_CallbackThrowsSynchronously_FaultsTheTask()
	{
		var expectedException = new InvalidOperationException("Resolve failed.");
		CompletionItem item = new CompletionItem("label").WithResolveCallback(_ => throw expectedException);

		Task<CompletionItem> resolveTask = item.ResolveAsync(TestContext.CancellationToken);

		InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => resolveTask).ConfigureAwait(false);

		Assert.AreSame(expectedException, exception);
	}

	[TestMethod]
	public void WithResolveCallback_ReturnsCopyWithResolveSupport()
	{
		var resolveCallback = new Func<CancellationToken, Task<CompletionItem>>(_ => Task.FromResult(new CompletionItem("label")));

		var item = new CompletionItem("label")
		{
			Detail = "detail",
			Priority = 2.0,
			Kind = CompletionItemKind.Field,
		};

		CompletionItem resolved = item.WithResolveCallback(resolveCallback);

		Assert.IsFalse(item.CanResolve);
		Assert.IsTrue(resolved.CanResolve);
		Assert.AreNotSame(item, resolved);
		Assert.AreEqual(item.Label, resolved.Label);
		Assert.AreEqual(item.InsertText, resolved.InsertText);
		Assert.AreEqual(item.Detail, resolved.Detail);
		Assert.AreEqual(item.Priority, resolved.Priority);
		Assert.AreSame(item.Kind, resolved.Kind);
	}

	[TestMethod]
	public async Task WithoutTextEdit_RemovesTextEditButKeepsResolveCallbackAsync()
	{
		var textEdit = new CompletionTextEdit(new TextRange(2, 4), new TextRange(2, 10));

		CompletionItem item = new CompletionItem("label") { TextEdit = textEdit }.WithResolveCallback(_ =>
			Task.FromResult(new CompletionItem("label") { TextEdit = textEdit })
		);

		CompletionItem commitItem = item.WithoutTextEdit();
		CompletionItem resolvedItem = await commitItem.ResolveAsync(TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsNull(commitItem.TextEdit);
		Assert.IsTrue(commitItem.CanResolve);
		Assert.AreEqual(textEdit, resolvedItem.TextEdit);
	}

	[TestMethod]
	public void WithoutTextEdit_WithoutTextEdit_ReturnsSameItem()
	{
		var item = new CompletionItem("label") { RequestDocumentVersion = 4, RequestGeneration = 9 };
		Assert.AreSame(item, item.WithoutTextEdit());
	}

	[TestMethod]
	public void WithoutTextEdit_PreStampedItem_StillRemovesTextEdit()
	{
		var textEdit = new CompletionTextEdit(new TextRange(2, 4));

		var item = new CompletionItem("label")
		{
			TextEdit = textEdit,
			RequestDocumentVersion = 4,
			RequestGeneration = 9,
		};

		CompletionItem strippedItem = item.WithoutTextEdit();

		Assert.AreNotSame(item, strippedItem);
		Assert.IsNull(strippedItem.TextEdit);
		Assert.AreEqual(4, strippedItem.RequestDocumentVersion);
		Assert.AreEqual(9, strippedItem.RequestGeneration);
	}

	[TestMethod]
	public async Task ResolveCallback_Initialized_AttachesResolver()
	{
		var item = new CompletionItem("label") { ResolveCallback = _ => Task.FromResult(new CompletionItem("resolved")) };

		Assert.IsTrue(item.CanResolve);

		CompletionItem resolvedItem = await item.ResolveAsync(TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual("resolved", resolvedItem.Label);
	}

	[TestMethod]
	public void WithoutTextEdit_PreservesEveryOtherField()
	{
		CompletionItem item = CreateFullyPopulatedItem();

		CompletionItem strippedItem = item.WithoutTextEdit();

		AssertItemsHaveSamePayload(item, strippedItem, nameof(CompletionItem.TextEdit));
		Assert.IsNull(strippedItem.TextEdit);
	}

	[TestMethod]
	public void WithResolveCallback_PreservesEveryOtherField()
	{
		CompletionItem item = CreateFullyPopulatedItem();

		CompletionItem resolvedItem = item.WithResolveCallback(static _ => Task.FromResult(new CompletionItem("resolved")));

		AssertItemsHaveSamePayload(item, resolvedItem, nameof(CompletionItem.ResolveCallback));
		Assert.IsNotNull(resolvedItem.ResolveCallback);
	}
}
