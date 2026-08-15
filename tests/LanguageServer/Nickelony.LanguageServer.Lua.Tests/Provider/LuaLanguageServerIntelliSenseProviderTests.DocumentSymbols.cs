using Nickelony.IDEKit.IntelliSense.DocumentSymbols;
using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

public sealed partial class LuaLanguageServerIntelliSenseProviderTests
{
	[TestMethod]
	public async Task GetDocumentSymbolsAsync_SendsTheDocumentSymbolRequestAndMapsTheResponse()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1\nfunction spawn(room)\nend\n";

		using var client = new FakeLanguageServerClient
		{
			DocumentSymbolsResponse = JsonSerializer.SerializeToElement(new object[]
			{
				new
				{
					name = "spawn",
					kind = 12,
					range = new
					{
						start = new { line = 1, character = 0 },
						end = new { line = 2, character = 3 }
					},
					selectionRange = new
					{
						start = new { line = 1, character = 9 },
						end = new { line = 1, character = 14 }
					},
					children = new object[]
					{
						new
						{
							name = "room",
							kind = 13,
							range = new
							{
								start = new { line = 1, character = 14 },
								end = new { line = 1, character = 18 }
							},
							selectionRange = new
							{
								start = new { line = 1, character = 14 },
								end = new { line = 1, character = 18 }
							}
						}
					}
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextDocumentSymbol> symbols = await provider.GetDocumentSymbolsAsync(filePath, content);

		JsonElement parameters = client.GetLastRequestParameters("textDocument/documentSymbol");

		Assert.AreEqual(
			Nickelony.LanguageServer.Client.LanguageServerPaths.CreateFileUri(filePath),
			parameters.GetProperty("textDocument").GetProperty("uri").GetString());

		Assert.AreEqual(1, symbols.Count);
		Assert.AreEqual("spawn", symbols[0].Name);
		Assert.AreEqual(TextDocumentSymbolKind.Function, symbols[0].Kind);
		Assert.AreEqual(new TextRange(16, 24), symbols[0].Range);
		Assert.AreEqual(new TextRange(25, 5), symbols[0].SelectionRange);
		Assert.AreEqual(1, symbols[0].Children.Count);
		Assert.AreEqual("room", symbols[0].Children[0].Name);
		Assert.AreEqual(new TextRange(30, 4), symbols[0].Children[0].Range);
	}

	[TestMethod]
	public async Task GetDocumentSymbolsAsync_WhenUnsupported_ReturnsEmptyAndSendsNoRequest()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient { SupportsDocumentSymbols = false };
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextDocumentSymbol> symbols = await provider.GetDocumentSymbolsAsync(filePath, "local value = 1");

		// The member is capability-gated: without a negotiated document-symbol provider the request is
		// skipped before the document is synchronized, so no traffic is produced at all.
		Assert.AreEqual(0, symbols.Count);
		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	[TestMethod]
	public async Task GetDocumentSymbolsAsync_InvalidFilePath_ReturnsEmptyWithoutSending()
	{
		string workspaceRoot = TestPaths.Root;

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextDocumentSymbol> symbols = await provider.GetDocumentSymbolsAsync("   ", "local value = 1");

		Assert.AreEqual(0, symbols.Count);
		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	[TestMethod]
	public async Task GetDocumentSymbolsAsync_NullArguments_Throw()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(
			() => provider.GetDocumentSymbolsAsync(null!, "local value = 1"));

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(
			() => provider.GetDocumentSymbolsAsync(filePath, null!));
	}
}
