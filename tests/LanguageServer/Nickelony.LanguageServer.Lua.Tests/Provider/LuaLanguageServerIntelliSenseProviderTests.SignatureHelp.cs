using Nickelony.IDEKit.IntelliSense.Signatures;
using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

public sealed partial class LuaLanguageServerIntelliSenseProviderTests
{
	[TestMethod]
	public async Task GetSignatureHelpAsync_WithoutContext_SendsAPositionOnlyRequest()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			SignatureHelpResponse = CreateMinimalSignatureHelpResponse()
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		await provider.GetSignatureHelpAsync(new LanguageServerSignatureHelpRequest(filePath, "spawn(", new TextPosition(0, 6)));

		JsonElement parameters = client.GetLastRequestParameters("textDocument/signatureHelp");

		// A caller without an editor context issues the position-only request shape.
		Assert.IsFalse(parameters.TryGetProperty("context", out _), parameters.GetRawText());
		Assert.AreEqual(0, parameters.GetProperty("position").GetProperty("line").GetInt32());
		Assert.AreEqual(6, parameters.GetProperty("position").GetProperty("character").GetInt32());
	}

	[TestMethod]
	public async Task GetSignatureHelpAsync_WithTriggerCharacterContext_ForwardsTheTriggerContext()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			SignatureHelpResponse = CreateMinimalSignatureHelpResponse()
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		var context = new TextSignatureHelpContext(
			TextSignatureHelpTriggerKind.TriggerCharacter,
			"(",
			isRetrigger: true);

		await provider.GetSignatureHelpAsync(new LanguageServerSignatureHelpRequest(filePath, "spawn(", new TextPosition(0, 6), context));

		JsonElement contextElement = client.GetLastRequestParameters("textDocument/signatureHelp").GetProperty("context");

		Assert.AreEqual(2, contextElement.GetProperty("triggerKind").GetInt32());
		Assert.AreEqual("(", contextElement.GetProperty("triggerCharacter").GetString());
		Assert.IsTrue(contextElement.GetProperty("isRetrigger").GetBoolean());

		// No visible payload was supplied, so the retrigger hint carries no active signature help.
		Assert.IsFalse(contextElement.TryGetProperty("activeSignatureHelp", out _));
	}

	[TestMethod]
	public async Task GetSignatureHelpAsync_WithContentChangeContext_OmitsTheTriggerCharacter()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			SignatureHelpResponse = CreateMinimalSignatureHelpResponse()
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		await provider.GetSignatureHelpAsync(new LanguageServerSignatureHelpRequest(filePath, "spawn(", new TextPosition(0, 6), new TextSignatureHelpContext(TextSignatureHelpTriggerKind.ContentChange)));

		JsonElement contextElement = client.GetLastRequestParameters("textDocument/signatureHelp").GetProperty("context");

		Assert.AreEqual(3, contextElement.GetProperty("triggerKind").GetInt32());
		Assert.IsFalse(contextElement.GetProperty("isRetrigger").GetBoolean());
		Assert.IsFalse(contextElement.TryGetProperty("triggerCharacter", out _), contextElement.GetRawText());
	}

	[TestMethod]
	public async Task GetSignatureHelpAsync_WithActiveSignatureHelp_ForwardsTheVisiblePayload()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			SignatureHelpResponse = CreateMinimalSignatureHelpResponse()
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		var activeSignatureHelp = new TextSignatureHelp(
			[
				new TextSignatureInformation(
					"spawn(room, objectName)",
					"Spawns an object.",
					parameters:
					[
						new TextSignatureParameterInfo("room", "Room id."),
						new TextSignatureParameterInfo("objectName")
					]),
				new TextSignatureInformation("spawn(room)")
			],
			activeSignatureIndex: 0,
			activeParameter: TextSignatureActiveParameterSelection.At(1));

		var context = new TextSignatureHelpContext(
			TextSignatureHelpTriggerKind.ContentChange,
			isRetrigger: true,
			activeSignatureHelp: activeSignatureHelp);

		await provider.GetSignatureHelpAsync(new LanguageServerSignatureHelpRequest(filePath, "spawn(room, ", new TextPosition(0, 12), context));

		JsonElement activeElement = client.GetLastRequestParameters("textDocument/signatureHelp")
			.GetProperty("context")
			.GetProperty("activeSignatureHelp");

		Assert.AreEqual(0, activeElement.GetProperty("activeSignature").GetInt32());
		Assert.AreEqual(1, activeElement.GetProperty("activeParameter").GetInt32());
		Assert.AreEqual(2, activeElement.GetProperty("signatures").GetArrayLength());

		JsonElement firstSignature = activeElement.GetProperty("signatures")[0];

		Assert.AreEqual("spawn(room, objectName)", firstSignature.GetProperty("label").GetString());
		Assert.AreEqual("Spawns an object.", firstSignature.GetProperty("documentation").GetString());

		// The payload-level index carries the effective selection, so signatures omit their own
		// active-parameter override.
		Assert.IsFalse(firstSignature.TryGetProperty("activeParameter", out _));

		JsonElement firstParameters = firstSignature.GetProperty("parameters");

		Assert.AreEqual(2, firstParameters.GetArrayLength());
		Assert.AreEqual("room", firstParameters[0].GetProperty("label").GetString());
		Assert.AreEqual("Room id.", firstParameters[0].GetProperty("documentation").GetString());
		Assert.AreEqual("objectName", firstParameters[1].GetProperty("label").GetString());
		Assert.IsFalse(firstParameters[1].TryGetProperty("documentation", out _));

		// A signature without parameters omits the parameter list entirely.
		Assert.IsFalse(activeElement.GetProperty("signatures")[1].TryGetProperty("parameters", out _));
	}

	[TestMethod]
	public async Task GetSignatureHelpAsync_WithNoActiveParameter_WritesTheNullState()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			SignatureHelpResponse = CreateMinimalSignatureHelpResponse()
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		var activeSignatureHelp = new TextSignatureHelp(
			[new TextSignatureInformation("fn(a)", parameters: [new TextSignatureParameterInfo("a")])],
			activeParameter: TextSignatureActiveParameterSelection.None);

		var context = new TextSignatureHelpContext(
			TextSignatureHelpTriggerKind.ContentChange,
			activeSignatureHelp: activeSignatureHelp);

		await provider.GetSignatureHelpAsync(new LanguageServerSignatureHelpRequest(filePath, "fn(", new TextPosition(0, 3), context));

		JsonElement activeElement = client.GetLastRequestParameters("textDocument/signatureHelp")
			.GetProperty("context")
			.GetProperty("activeSignatureHelp");

		// The LSP 3.18 "no active parameter" state is the JSON null form, not an absent property.
		Assert.AreEqual(JsonValueKind.Null, activeElement.GetProperty("activeParameter").ValueKind);
	}

	[TestMethod]
	public async Task GetSignatureHelpAsync_InvalidFilePath_ReturnsNullWithoutSending()
	{
		string workspaceRoot = TestPaths.Root;

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		TextSignatureHelp? signature = await provider.GetSignatureHelpAsync(
			new LanguageServerSignatureHelpRequest("   ", "spawn(", new TextPosition(0, 6)));

		Assert.IsNull(signature);
		Assert.AreEqual(0, client.GetSentMethodNames().Length);
		Assert.AreEqual(0, client.StartCallCount);
	}

	private static JsonElement CreateMinimalSignatureHelpResponse() => JsonSerializer.SerializeToElement(new
	{
		activeSignature = 0,
		signatures = new[] { new { label = "spawn(room)" } }
	});
}
