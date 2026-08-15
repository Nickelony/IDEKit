using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

/// <summary>
/// Covers the rename request guards: an unsupported capability, a blank new name, an unusable target
/// path, and request-coordinate clamping.
/// </summary>
public sealed partial class LuaLanguageServerIntelliSenseProviderTests
{
	[TestMethod]
	public async Task RenameSymbolAsync_WhenUnsupported_ReturnsNullAndSendsNoRename()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient { SupportsRename = false };
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		TextWorkspaceEdit? workspaceEdit = await provider.RenameSymbolAsync(
			new LanguageServerRenameRequest(filePath, "local value = 1", new TextPosition(0, 6), "renamed"));

		// The capability gate runs before document synchronization, so an unsupported request
		// produces no traffic at all.
		Assert.IsNull(workspaceEdit);
		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	[TestMethod]
	public async Task RenameSymbolAsync_WhitespaceNewName_ReturnsNullWithoutSendingAnything()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		TextWorkspaceEdit? workspaceEdit = await provider.RenameSymbolAsync(
			new LanguageServerRenameRequest(filePath, "local value = 1", new TextPosition(0, 6), "   "));

		Assert.IsNull(workspaceEdit);
		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	[TestMethod]
	public async Task RenameSymbolAsync_UnnormalizablePath_ReturnsNullWithoutSendingAnything()
	{
		string workspaceRoot = TestPaths.Root;
		// An embedded NUL is not a valid path character on any platform.
		string filePath = TestPaths.Root + "\\bad\0name.lua";

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		TextWorkspaceEdit? workspaceEdit = await provider.RenameSymbolAsync(
			new LanguageServerRenameRequest(filePath, "local value = 1", new TextPosition(0, 6), "renamed"));

		Assert.IsNull(workspaceEdit);
		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	[TestMethod]
	public async Task RenameSymbolAsync_NegativeCoordinates_ClampToTheDocumentStart()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			RenameResponse = JsonSerializer.SerializeToElement<object?>(null)
		};
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		await provider.RenameSymbolAsync(new LanguageServerRenameRequest(filePath, "local value = 1", new TextPosition(-2, -4), "renamed"));

		// Request coordinates are clamped to zero like the shared position-based request path.
		JsonElement position = client.GetLastRequestParameters("textDocument/rename").GetProperty("position");

		Assert.AreEqual(0, position.GetProperty("line").GetInt32());
		Assert.AreEqual(0, position.GetProperty("character").GetInt32());
	}
}
