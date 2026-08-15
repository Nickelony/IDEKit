using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

public sealed partial class TextCompletionItemTests
{
	[TestMethod]
	public void ResolveCallback_NotInitialized_CannotResolve()
	{
		var item = new TextCompletionItem("label");
		Assert.IsFalse(item.CanResolve);
	}

	[TestMethod]
	public async Task ResolveAsync_WithoutResolveCallback_ReturnsSameItem()
	{
		var item = new TextCompletionItem("label");

		TextCompletionItem resolvedItem = await item.ResolveAsync().ConfigureAwait(false);

		Assert.AreSame(item, resolvedItem);
	}

	[TestMethod]
	public async Task ResolveAsync_WithResolveCallback_InvokesCallback()
	{
		var resolvedContent = new TextCompletionItem("label") { Detail = "resolved" };
		using var cancellation = new CancellationTokenSource();
		var item = new TextCompletionItem("label")
			.WithResolveCallback(cancellationToken =>
			{
				Assert.AreEqual(cancellation.Token, cancellationToken);
				return Task.FromResult(resolvedContent);
			});

		TextCompletionItem resolvedItem = await item.ResolveAsync(cancellation.Token).ConfigureAwait(false);

		Assert.IsTrue(item.CanResolve);
		Assert.AreSame(resolvedContent, resolvedItem);
	}

	[TestMethod]
	public async Task ResolveAsync_CallbackReturnsNullItem_Throws()
	{
		var item = new TextCompletionItem("label").WithResolveCallback(_ => Task.FromResult<TextCompletionItem>(null!));

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => item.ResolveAsync()).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ResolveAsync_CallbackReturnsNullTask_Throws()
	{
		var item = new TextCompletionItem("label").WithResolveCallback(_ => null!);

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => item.ResolveAsync()).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task ResolveAsync_CallbackThrowsSynchronously_FaultsTheTask()
	{
		var expectedException = new InvalidOperationException("Resolve failed.");
		var item = new TextCompletionItem("label").WithResolveCallback(_ => throw expectedException);

		Task<TextCompletionItem> resolveTask = item.ResolveAsync();

		InvalidOperationException exception = await Assert
			.ThrowsExactlyAsync<InvalidOperationException>(() => resolveTask)
			.ConfigureAwait(false);

		Assert.AreSame(expectedException, exception);
	}

	[TestMethod]
	public void WithResolveCallback_ReturnsCopyWithResolveSupport()
	{
		var resolveCallback = new Func<CancellationToken, Task<TextCompletionItem>>(_ => Task.FromResult(new TextCompletionItem("label")));
		var item = new TextCompletionItem("label")
		{
			Detail = "detail",
			Priority = 2.0,
			Kind = TextCompletionItemKind.Field
		};

		TextCompletionItem resolved = item.WithResolveCallback(resolveCallback);

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
		var textEdit = new TextCompletionTextEdit(new TextRange(2, 4), new TextRange(2, 10));
		var item = new TextCompletionItem("label") { TextEdit = textEdit }
			.WithResolveCallback(_ => Task.FromResult(new TextCompletionItem("label") { TextEdit = textEdit }));

		TextCompletionItem commitItem = item.WithoutTextEdit();
		TextCompletionItem resolvedItem = await commitItem.ResolveAsync().ConfigureAwait(false);

		Assert.IsNull(commitItem.TextEdit);
		Assert.IsTrue(commitItem.CanResolve);
		Assert.AreEqual(textEdit, resolvedItem.TextEdit);
	}

	[TestMethod]
	public void WithoutTextEdit_WithoutTextEdit_ReturnsSameItem()
	{
		var item = new TextCompletionItem("label") { RequestDocumentVersion = 4, RequestGeneration = 9 };

		Assert.AreSame(item, item.WithoutTextEdit());
	}

	[TestMethod]
	public void WithoutTextEdit_PreStampedItem_StillRemovesTextEdit()
	{
		var textEdit = new TextCompletionTextEdit(new TextRange(2, 4));
		var item = new TextCompletionItem("label") { TextEdit = textEdit, RequestDocumentVersion = 4, RequestGeneration = 9 };

		TextCompletionItem strippedItem = item.WithoutTextEdit();

		Assert.AreNotSame(item, strippedItem);
		Assert.IsNull(strippedItem.TextEdit);
		Assert.AreEqual(4, strippedItem.RequestDocumentVersion);
		Assert.AreEqual(9, strippedItem.RequestGeneration);
	}

	[TestMethod]
	public async Task ResolveCallback_Initialized_AttachesResolver()
	{
		var item = new TextCompletionItem("label")
		{
			ResolveCallback = _ => Task.FromResult(new TextCompletionItem("resolved"))
		};

		Assert.IsTrue(item.CanResolve);

		TextCompletionItem resolvedItem = await item.ResolveAsync().ConfigureAwait(false);

		Assert.AreEqual("resolved", resolvedItem.Label);
	}

	[TestMethod]
	public void WithoutTextEdit_PreservesEveryOtherField()
	{
		TextCompletionItem item = CreateFullyPopulatedItem();

		TextCompletionItem strippedItem = item.WithoutTextEdit();

		AssertItemsHaveSamePayload(item, strippedItem, nameof(TextCompletionItem.TextEdit));
		Assert.IsNull(strippedItem.TextEdit);
	}

	[TestMethod]
	public void WithResolveCallback_PreservesEveryOtherField()
	{
		TextCompletionItem item = CreateFullyPopulatedItem();

		TextCompletionItem resolvedItem = item.WithResolveCallback(static _ => Task.FromResult(new TextCompletionItem("resolved")));

		AssertItemsHaveSamePayload(item, resolvedItem, nameof(TextCompletionItem.ResolveCallback));
		Assert.IsNotNull(resolvedItem.ResolveCallback);
	}
}
