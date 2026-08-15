using Nickelony.IDEKit.IntelliSense.Hover;
using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

public sealed partial class LuaLanguageServerIntelliSenseProviderTests
{
	[TestMethod]
	public async Task CanceledRequestsBeforeStart_PropagateCancellationAcrossAllRequestKinds()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		using var cancellationTokenSource = new CancellationTokenSource();

		cancellationTokenSource.Cancel();

		Task[] canceledRequests =
		[
			provider.GetCompletionItemsAsync(new LanguageServerCompletionRequest(filePath, content, new TextPosition(0, 0)), cancellationToken: cancellationTokenSource.Token),
			provider.GetHoverAsync(filePath, content, new TextPosition(0, 0), cancellationTokenSource.Token),
			provider.GetDefinitionAsync(filePath, content, new TextPosition(0, 0), cancellationTokenSource.Token),
			provider.GetSignatureHelpAsync(new LanguageServerSignatureHelpRequest(filePath, content, new TextPosition(0, 0)), cancellationToken: cancellationTokenSource.Token),
			provider.GetReferencesAsync(new LanguageServerReferenceRequest(filePath, content, new TextPosition(0, 0)), cancellationTokenSource.Token),
			provider.RenameSymbolAsync(new LanguageServerRenameRequest(filePath, content, new TextPosition(0, 0), "renamed"), cancellationTokenSource.Token),
			provider.FormatDocumentAsync(new LanguageServerFormattingRequest(filePath, content, new TextFormattingOptions(4, true)), cancellationTokenSource.Token),
			provider.GetDocumentSymbolsAsync(filePath, content, cancellationTokenSource.Token)
		];

		for (int i = 0; i < canceledRequests.Length; i++)
			await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => canceledRequests[i]).ConfigureAwait(false);

		Assert.AreEqual(0, client.StartCallCount);
		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	[TestMethod]
	public async Task CancellationAfterResponseBeforePublication_PropagatesAndLeavesProviderUsable()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			HoverResponse = JsonSerializer.SerializeToElement(new
			{
				contents = new
				{
					kind = "markdown",
					value = "Hover docs."
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		using var cancellationTokenSource = new CancellationTokenSource();

		client.BeforeReturningHoverResponse = cancellationTokenSource.Cancel;

		Task<TextHoverInfo?> canceledHoverTask = provider.GetHoverAsync(filePath, content, new TextPosition(0, 0), cancellationTokenSource.Token);

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => canceledHoverTask).ConfigureAwait(false);

		client.BeforeReturningHoverResponse = null;
		TextHoverInfo? recoveredHover = await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0)).ConfigureAwait(false);

		Assert.IsNotNull(recoveredHover);
		Assert.AreEqual("Hover docs.", recoveredHover.Content);
		Assert.AreEqual(0, client.MarkTransportUnhealthyCallCount);
	}
}
