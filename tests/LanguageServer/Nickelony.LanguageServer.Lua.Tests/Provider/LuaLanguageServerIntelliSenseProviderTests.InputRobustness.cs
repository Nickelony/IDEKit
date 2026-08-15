using Nickelony.IDEKit.IntelliSense.Hover;
using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

/// <summary>
/// Covers input-shape robustness the main partial classes do not exercise: Unicode content, byte-order
/// marks, large documents, and parallel requests.
/// </summary>
public sealed partial class LuaLanguageServerIntelliSenseProviderTests
{
	[TestMethod]
	public async Task GetHoverAsync_UnicodeContent_RoundTripsThroughTheTransportUnchanged()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("unicode.lua");
		const string content = "local message = \"\U0001F680 \u00E9\u00F1\" -- \u4F60\u597D";

		using var client = new FakeLanguageServerClient { HoverResponse = CreateRobustnessHoverResponse() };
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		Assert.IsNotNull(await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0)));

		JsonElement textDocument = client.GetLastNotificationParameters("textDocument/didOpen").GetProperty("textDocument");

		Assert.AreEqual(content, textDocument.GetProperty("text").GetString());
	}

	[TestMethod]
	public async Task OpenDocument_ContentWithByteOrderMark_PreservesTheMarkAndVersionsTheUpdate()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("bom.lua");
		const string content = "\uFEFFlocal value = 1";
		const string updatedContent = content + "\r\nreturn value";

		using var client = new FakeLanguageServerClient
		{
			// Full synchronization carries the whole document text, so the update payload itself shows
			// whether the mark survived the change.
			TextDocumentSyncKind = TextDocumentSyncKind.Full
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

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
		string filePath = TestPaths.Script("large.lua");
		string content = "local large = \"" + new string('x', 100_000) + "\"";

		using var client = new FakeLanguageServerClient { HoverResponse = CreateRobustnessHoverResponse() };
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		Assert.IsNotNull(await provider.GetHoverAsync(filePath, content, new TextPosition(0, 3)));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/hover", 1, TestPolling.DefaultTimeout));

		JsonElement textDocument = client.GetLastNotificationParameters("textDocument/didOpen").GetProperty("textDocument");
		string? sentText = textDocument.GetProperty("text").GetString();

		// The whole payload is compared, not just its shape: a corrupted interior must fail the test.
		Assert.AreEqual(content, sentText);
	}

	[TestMethod]
	public async Task GetHoverAsync_ParallelRequestsOnDistinctFiles_ShareOneStartup()
	{
		string workspaceRoot = TestPaths.Root;

		using var client = new FakeLanguageServerClient { HoverResponse = CreateRobustnessHoverResponse() };
		client.BlockNextStartAsync();

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		Task<TextHoverInfo?>[] hoverTasks = [.. Enumerable.Range(0, 8).Select(index =>
			provider.GetHoverAsync(TestPaths.Script($"parallel_{index}.lua"), "local value = 1", new TextPosition(0, 0)))];

		await TestPolling.UntilAsync(
			() => client.StartCallCount > 0,
			TestPolling.DefaultTimeout,
			"At least one parallel request must reach the shared startup.").ConfigureAwait(false);

		Assert.AreEqual(1, client.StartCallCount);

		client.ReleaseStartAsync();

		TextHoverInfo?[] hovers = await Task.WhenAll(hoverTasks);

		Assert.IsTrue(hovers.All(hover => hover is not null));
		Assert.AreEqual(1, client.StartCallCount, "Parallel requests must share one startup flow.");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/hover", 8, TestPolling.DefaultTimeout));
		Assert.AreEqual(8, CountSentMethods(client, "textDocument/didOpen"));
	}

	private static JsonElement CreateRobustnessHoverResponse() => JsonSerializer.SerializeToElement(new
	{
		contents = new
		{
			kind = "markdown",
			value = "Hover docs."
		}
	});
}
