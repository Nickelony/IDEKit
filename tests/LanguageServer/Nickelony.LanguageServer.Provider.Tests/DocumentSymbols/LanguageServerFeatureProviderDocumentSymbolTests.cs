using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the document-symbol request through the framework pipeline.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderDocumentSymbolTests
{
	[TestMethod]
	public async Task GetDocumentSymbolsAsync_SendsTheDocumentSymbolRequestAndMapsTheResponse()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1\nfunction spawn(room)\nend\n";

		using var client = new FakeStatefulLanguageServerClient
		{
			DocumentSymbolResponse = JsonSerializer.SerializeToElement(
				new object[]
				{
					new
					{
						name = "spawn",
						kind = 12,
						range = new { start = new { line = 1, character = 0 }, end = new { line = 2, character = 3 } },
						selectionRange = new { start = new { line = 1, character = 9 }, end = new { line = 1, character = 14 } },

						children = new object[]
						{
							new
							{
								name = "room",
								kind = 13,
								range = new { start = new { line = 1, character = 14 }, end = new { line = 1, character = 18 } },
								selectionRange = new { start = new { line = 1, character = 14 }, end = new { line = 1, character = 18 } },
							},
						},
					},
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<DocumentSymbol> symbols = await provider.GetDocumentSymbolsAsync(filePath, content, TestContext.CancellationToken);

		JsonElement parameters = client.GetLastRequestParameters("textDocument/documentSymbol");

		Assert.AreEqual(
			Nickelony.LanguageServer.Client.DocumentUriConversion.CreateFileUri(filePath),
			parameters.GetProperty("textDocument").GetProperty("uri").GetString()
		);

		Assert.HasCount(1, symbols);
		Assert.AreEqual("spawn", symbols[0].Name);
		Assert.AreEqual(DocumentSymbolKind.Function, symbols[0].Kind);
		Assert.AreEqual(new TextRange(16, 24), symbols[0].Range);
		Assert.AreEqual(new TextRange(25, 5), symbols[0].SelectionRange);
		Assert.HasCount(1, symbols[0].Children);
		Assert.AreEqual("room", symbols[0].Children[0].Name);
		Assert.AreEqual(new TextRange(30, 4), symbols[0].Children[0].Range);
	}

	[TestMethod]
	public async Task GetDocumentSymbolsAsync_WhenUnsupported_ReturnsEmptyAndSendsNoRequest()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { SupportsDocumentSymbols = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<DocumentSymbol> symbols = await provider.GetDocumentSymbolsAsync(filePath, "local value = 1", TestContext.CancellationToken);

		// The member is capability-gated: without a negotiated document-symbol provider the request is
		// skipped before the document is synchronized, so no traffic is produced at all.
		Assert.IsEmpty(symbols);
		Assert.IsEmpty(client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task GetDocumentSymbolsAsync_InvalidFilePath_ReturnsEmptyWithoutSending()
	{
		string workspaceRoot = TestPaths.Root;

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<DocumentSymbol> symbols = await provider.GetDocumentSymbolsAsync(
			TestPaths.InvalidFile,
			"local value = 1",
			TestContext.CancellationToken
		);

		Assert.IsEmpty(symbols);
		Assert.IsEmpty(client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task GetDocumentSymbolsAsync_NullArguments_Throw()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
			provider.GetDocumentSymbolsAsync(null!, "local value = 1", TestContext.CancellationToken)
		);

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
			provider.GetDocumentSymbolsAsync(filePath, null!, TestContext.CancellationToken)
		);
	}

	public TestContext? TestContext { get; set; }
}
