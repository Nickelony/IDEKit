using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.LanguageFeatures.Rename;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers how a request recovers from a connection failure or a startup failure: one restart, one retry, and the failure reports.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderRequestRecoveryTests
{
	[TestMethod]
	public async Task GetCompletionItemsAsync_WhenRequestCrossesConnectionBoundary_RetriesOnce()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			ConnectionChangedRequestFailuresRemaining = 1,

			CompletionResponse = JsonSerializer.SerializeToElement(
				new
				{
					items = new object[]
					{
						new
						{
							label = "spawn",
							kind = 3,
							insertText = "spawn",
						},
					},
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<CompletionItem> items = await provider
			.GetCompletionItemsAsync(new CompletionRequest(filePath, "spa", 3), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.HasCount(1, items);
		Assert.AreEqual("spawn", items[0].Label);
		Assert.AreEqual(2, client.StartCallCount);
		Assert.AreEqual(2, client.GetSentMethodCount("textDocument/completion"));
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_WhenConnectionChangesOntoReadyConnection_RetriesWithoutRestart()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			ConnectionChangedRequestFailuresRemaining = 1,
			ConnectionChangedFailureDetachesConnection = false,

			CompletionResponse = JsonSerializer.SerializeToElement(
				new
				{
					items = new object[]
					{
						new
						{
							label = "spawn",
							kind = 3,
							insertText = "spawn",
						},
					},
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<CompletionItem> items = await provider
			.GetCompletionItemsAsync(new CompletionRequest(filePath, "spa", 3), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.HasCount(1, items);
		Assert.AreEqual("spawn", items[0].Label);

		// The request crossed onto an already-ready newer connection, so the request runner retries once on the new
		// generation without starting another session.
		Assert.AreEqual(1, client.StartCallCount);
		Assert.AreEqual(2, client.GetSentMethodCount("textDocument/completion"));
	}

	[TestMethod]
	public async Task GetHoverAsync_WhenRequestConnectionFails_RestartsAndRetriesOnce()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient
		{
			ThrowIOExceptionOnNextRequestMethod = "textDocument/hover",
			HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		HoverInformation? recoveredHover = await provider
			.GetHoverAsync(filePath, documentText: content, position: new TextPosition(0, 0), cancellationToken: TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.IsNotNull(recoveredHover);
		Assert.AreEqual("Hover docs.", recoveredHover.Content.Value);
		Assert.AreEqual(2, client.StartCallCount);
		Assert.AreEqual(2, client.GetSentMethodCount("textDocument/hover"));
	}

	[TestMethod]
	public async Task RenameSymbolAsync_WhenRequestConnectionFails_RestartsAndRetriesOnce()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("rename.test");

		const string content = "local tracked_value = 1\r\nreturn tracked_value\r\n";

		using var client = new FakeStatefulLanguageServerClient
		{
			ThrowIOExceptionOnNextRequestMethod = "textDocument/rename",

			RenameResponse = JsonSerializer.SerializeToElement(
				new
				{
					changes = new Dictionary<string, object[]>
					{
						[new Uri(filePath).AbsoluteUri] =
						[
							new
							{
								newText = "renamed_value",
								range = new { start = new { line = 0, character = 6 }, end = new { line = 0, character = 19 } },
							},
						],
					},
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		WorkspaceEdit? recoveredRename = await provider
			.GetRenameEditsAsync(
				new RenameRequest(
					documentId: filePath,
					documentText: content,
					symbolOffset: content.OffsetAt(new TextPosition(0, 8)),
					newName: "renamed_value"
				),

				TestContext.CancellationToken
			)
			.ConfigureAwait(false);

		Assert.IsNotNull(recoveredRename);
		Assert.IsTrue(recoveredRename.HasChanges);
		Assert.AreEqual(2, client.StartCallCount);
		Assert.AreEqual(2, client.GetSentMethodCount("textDocument/rename"));
	}

	[TestMethod]
	public async Task GetHoverAsync_WhenRequestFailsWithoutConnectionLoss_ThrowsWithoutRetrying()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient { ThrowInvalidOperationOnNextRequestMethod = "textDocument/hover" };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		await Assert
			.ThrowsExactlyAsync<InvalidOperationException>(async () =>
				await provider
					.GetHoverAsync(
						filePath,
						documentText: content,
						position: new TextPosition(0, 0),
						cancellationToken: TestContext.CancellationToken
					)
					.ConfigureAwait(false)
			)
			.ConfigureAwait(false);

		Assert.AreEqual(1, client.StartCallCount);
		Assert.AreEqual(1, client.GetSentMethodCount("textDocument/hover"));
	}

	[TestMethod]
	public async Task GetHoverAsync_RaisesTransientAndPermanentStartupFailuresOnceEach()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient { IsReady = false, StartResult = false };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);
		var failures = new List<StartupFailure>();

		provider.StartupFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

		await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));
		await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));
		await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));
		await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

		Assert.HasCount(2, failures);
		Assert.IsFalse(failures[0].IsPersistent);
		Assert.IsTrue(failures[1].IsPersistent);
		Assert.AreEqual(3, client.StartCallCount);
	}

	public TestContext? TestContext { get; set; }
}
