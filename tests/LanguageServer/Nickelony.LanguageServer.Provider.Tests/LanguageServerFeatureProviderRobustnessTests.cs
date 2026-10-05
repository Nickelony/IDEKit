using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.LanguageFeatures.Rename;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers cancellation at each phase of a request, unusual document content, and parallel requests that share one startup.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderRobustnessTests
{
	[TestMethod]
	public async Task CanceledRequestsBeforeStart_PropagateCancellationAcrossAllRequestKinds()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);
		using var cancellationTokenSource = new CancellationTokenSource();

		cancellationTokenSource.Cancel();

		Task[] canceledRequests =
		[
			provider.GetCompletionItemsAsync(
				new CompletionRequest(filePath, content, content.OffsetAt(new TextPosition(0, 0))),
				cancellationToken: cancellationTokenSource.Token
			),

			provider.GetHoverAsync(
				filePath,
				documentText: content,
				position: new TextPosition(0, 0),
				cancellationToken: cancellationTokenSource.Token
			),

			provider.GetDefinitionAsync(
				filePath,
				documentText: content,
				position: new TextPosition(0, 0),
				cancellationToken: cancellationTokenSource.Token
			),

			provider.GetSignatureHelpAsync(
				new SignatureHelpRequest(filePath, content, content.OffsetAt(new TextPosition(0, 0))),
				cancellationToken: cancellationTokenSource.Token
			),

			provider.GetReferencesAsync(
				new ReferenceRequest(filePath, content, content.OffsetAt(new TextPosition(0, 0))),
				cancellationTokenSource.Token
			),

			provider.GetRenameEditsAsync(
				new RenameRequest(
					documentId: filePath,
					documentText: content,
					symbolOffset: content.OffsetAt(new TextPosition(0, 0)),
					newName: "renamed"
				),

				cancellationTokenSource.Token
			),

			provider.GetFormattingEditsAsync(new FormattingRequest(filePath, content, new FormattingOptions(4, true)), cancellationTokenSource.Token),
			provider.GetDocumentSymbolsAsync(filePath, content, cancellationTokenSource.Token),
		];

		for (int i = 0; i < canceledRequests.Length; i++)
			await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => canceledRequests[i]).ConfigureAwait(false);

		Assert.AreEqual(0, client.StartCallCount);
		Assert.IsEmpty(client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task CancellationAfterResponseBeforePublication_PropagatesAndLeavesProviderUsable()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient
		{
			HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);
		using var cancellationTokenSource = new CancellationTokenSource();

		client.BeforeReturningHoverResponse = cancellationTokenSource.Cancel;

		Task<HoverInformation?> canceledHoverTask = provider.GetHoverAsync(
			filePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: cancellationTokenSource.Token
		);

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => canceledHoverTask).ConfigureAwait(false);

		client.BeforeReturningHoverResponse = null;

		HoverInformation? recoveredHover = await provider
			.GetHoverAsync(filePath, documentText: content, position: new TextPosition(0, 0), cancellationToken: TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.IsNotNull(recoveredHover);
		Assert.AreEqual("Hover docs.", recoveredHover.Content.Value);
		Assert.AreEqual(0, client.MarkConnectionUnhealthyCallCount);
	}

	public TestContext? TestContext { get; set; }

	[TestMethod]
	public async Task GetHoverAsync_UnicodeContent_RoundTripsThroughTheConnectionUnchanged()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("unicode.test");

		const string content = "local message = \"\U0001F680 \u00E9\u00F1\" -- \u4F60\u597D";

		using var client = new FakeStatefulLanguageServerClient { HoverResponse = CreateRobustnessHoverResponse() };
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		Assert.IsNotNull(
			await provider.GetHoverAsync(
				filePath,
				documentText: content,
				position: new TextPosition(0, 0),
				cancellationToken: TestContext.CancellationToken
			)
		);

		JsonElement textDocument = client.GetLastNotificationParameters("textDocument/didOpen").GetProperty("textDocument");

		Assert.AreEqual(content, textDocument.GetProperty("text").GetString());
	}

	[TestMethod]
	public async Task OpenDocument_ContentWithByteOrderMark_PreservesTheMarkAndVersionsTheUpdate()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("bom.test");

		const string content = "\uFEFFlocal value = 1";
		const string updatedContent = content + "\r\nreturn value";

		using var client = new FakeStatefulLanguageServerClient
		{
			// Full synchronization carries the whole document text, so the update payload itself shows
			// whether the mark survived the change.
			TextDocumentSyncKind = TextDocumentSyncKind.Full,
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		JsonElement textDocument = client.GetLastNotificationParameters("textDocument/didOpen").GetProperty("textDocument");

		Assert.AreEqual(content, textDocument.GetProperty("text").GetString());

		provider.UpdateDocument(filePath, updatedContent);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, TestPolling.DefaultTimeout));

		JsonElement parameters = client.GetLastNotificationParameters("textDocument/didChange");

		Assert.AreEqual(2, parameters.GetProperty("textDocument").GetProperty("version").GetInt32());
		Assert.AreEqual(1, parameters.GetProperty("contentChanges").GetArrayLength());
		Assert.AreEqual(updatedContent, parameters.GetProperty("contentChanges")[0].GetProperty("text").GetString());
	}

	[TestMethod]
	public async Task GetHoverAsync_LargeDocumentContent_SendsTheExactPayload()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("large.test");
		string content = "local large = \"" + new string('x', 100_000) + "\"";

		using var client = new FakeStatefulLanguageServerClient { HoverResponse = CreateRobustnessHoverResponse() };
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		Assert.IsNotNull(
			await provider.GetHoverAsync(
				filePath,
				documentText: content,
				position: new TextPosition(0, 3),
				cancellationToken: TestContext.CancellationToken
			)
		);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/hover", 1, TestPolling.DefaultTimeout));

		JsonElement textDocument = client.GetLastNotificationParameters("textDocument/didOpen").GetProperty("textDocument");
		string? sentText = textDocument.GetProperty("text").GetString();

		// The whole payload is compared, not merely its shape: a corrupted interior must fail the test.
		Assert.AreEqual(content, sentText);
	}

	[TestMethod]
	public async Task GetHoverAsync_ParallelRequestsOnDistinctFiles_ShareOneStartup()
	{
		string workspaceRoot = TestPaths.Root;

		using var client = new FakeStatefulLanguageServerClient { HoverResponse = CreateRobustnessHoverResponse() };
		client.BlockNextEnsureStartedAsync();

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		Task<HoverInformation?>[] hoverTasks =
		[
			.. Enumerable
				.Range(0, 8)
				.Select(index =>
					provider.GetHoverAsync(
						filePath: TestPaths.Script($"parallel_{index}.test"),
						documentText: "local value = 1",
						position: new TextPosition(0, 0),
						cancellationToken: TestContext.CancellationToken
					)
				),
		];

		await TestPolling
			.UntilAsync(() => client.StartCallCount > 0, TestPolling.DefaultTimeout, "At least one parallel request must reach the shared startup.")
			.ConfigureAwait(false);

		Assert.AreEqual(1, client.StartCallCount);

		client.ReleaseEnsureStartedAsync();

		HoverInformation?[] hovers = await Task.WhenAll(hoverTasks);

		Assert.IsTrue(hovers.All(hover => hover is not null));
		Assert.AreEqual(1, client.StartCallCount, "Parallel requests must share one startup flow.");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/hover", 8, TestPolling.DefaultTimeout));
		Assert.AreEqual(8, client.GetSentMethodCount("textDocument/didOpen"));
	}

	[TestMethod]
	public async Task GetHoverAsync_UserCancellation_DoesNotBlockClosingOpenDocument()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient { HoverResponse = CreateRobustnessHoverResponse() };

		client.BlockNextHoverRequest();

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);
		using var cancellationTokenSource = new CancellationTokenSource();

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));

		Task<HoverInformation?> hoverTask = provider.GetHoverAsync(
			filePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: cancellationTokenSource.Token
		);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/hover", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));

		cancellationTokenSource.Cancel();
		await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => hoverTask).ConfigureAwait(false);

		provider.CloseDocument(filePath);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));
	}

	private static JsonElement CreateRobustnessHoverResponse() =>
		JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } });
}
