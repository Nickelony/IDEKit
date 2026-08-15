using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class SignatureHelpParamsSerializationTests
{
	[TestMethod]
	public void Serialize_WithInvokedContext_WritesTheProtocolNumber()
	{
		var parameters = new SignatureHelpParams(
			new TextDocumentIdentifier("file:///test.lua"),
			new ProtocolPosition(3, 7),
			new SignatureHelpContextPayload(SignatureHelpTriggerKind.Invoked, IsRetrigger: false));

		string json = JsonSerializer.Serialize(parameters);

		Assert.IsTrue(json.Contains("\"triggerKind\":1", StringComparison.Ordinal), json);
	}

	[TestMethod]
	public void Serialize_WithoutContext_OmitsTheContextProperty()
	{
		var parameters = new SignatureHelpParams(
			new TextDocumentIdentifier("file:///test.lua"),
			new ProtocolPosition(3, 7));

		string json = JsonSerializer.Serialize(parameters);

		Assert.IsFalse(json.Contains("\"context\"", StringComparison.Ordinal), json);
		Assert.IsTrue(json.Contains("\"position\":{\"line\":3,\"character\":7}", StringComparison.Ordinal), json);
	}

	[TestMethod]
	public void Serialize_WithTriggerCharacterContext_WritesTheProtocolShape()
	{
		var parameters = new SignatureHelpParams(
			new TextDocumentIdentifier("file:///test.lua"),
			new ProtocolPosition(3, 7),
			new SignatureHelpContextPayload(
				SignatureHelpTriggerKind.TriggerCharacter,
				IsRetrigger: true,
				TriggerCharacter: "("));

		string json = JsonSerializer.Serialize(parameters);

		Assert.IsTrue(json.Contains("\"triggerKind\":2", StringComparison.Ordinal), json);
		Assert.IsTrue(json.Contains("\"triggerCharacter\":\"(\"", StringComparison.Ordinal), json);
		Assert.IsTrue(json.Contains("\"isRetrigger\":true", StringComparison.Ordinal), json);
		Assert.IsFalse(json.Contains("activeSignatureHelp", StringComparison.Ordinal), json);
	}

	[TestMethod]
	public void Serialize_WithContentChangeContext_OmitsTheTriggerCharacter()
	{
		var parameters = new SignatureHelpParams(
			new TextDocumentIdentifier("file:///test.lua"),
			new ProtocolPosition(3, 7),
			new SignatureHelpContextPayload(SignatureHelpTriggerKind.ContentChange, IsRetrigger: false));

		string json = JsonSerializer.Serialize(parameters);

		Assert.IsTrue(json.Contains("\"triggerKind\":3", StringComparison.Ordinal), json);
		Assert.IsTrue(json.Contains("\"isRetrigger\":false", StringComparison.Ordinal), json);
		Assert.IsFalse(json.Contains("triggerCharacter", StringComparison.Ordinal), json);
	}

	[TestMethod]
	public void Serialize_WithActiveSignatureHelp_RoundTripsTheNestedPayload()
	{
		var activeSignatureHelp = new SignatureHelpResponse
		{
			ActiveSignature = 1,
			ActiveParameter = JsonSerializer.SerializeToElement(0),
			Signatures =
			[
				new SignatureHelpSignaturePayload
				{
					Label = "spawn(room)",
					Documentation = JsonSerializer.SerializeToElement("Spawns an object."),
					Parameters =
					[
						new SignatureHelpParameterPayload
						{
							Label = JsonSerializer.SerializeToElement("room"),
							Documentation = JsonSerializer.SerializeToElement("Room id.")
						}
					]
				},
				new SignatureHelpSignaturePayload { Label = "spawn(room, objectName)" }
			]
		};

		var parameters = new SignatureHelpParams(
			new TextDocumentIdentifier("file:///test.lua"),
			new ProtocolPosition(3, 7),
			new SignatureHelpContextPayload(
				SignatureHelpTriggerKind.TriggerCharacter,
				IsRetrigger: true,
				TriggerCharacter: "(",
				ActiveSignatureHelp: activeSignatureHelp));

		string json = JsonSerializer.Serialize(parameters);
		SignatureHelpParams roundTripped = JsonSerializer.Deserialize<SignatureHelpParams>(json);
		SignatureHelpContextPayload? context = roundTripped.Context;

		Assert.IsNotNull(context);
		Assert.AreEqual(SignatureHelpTriggerKind.TriggerCharacter, context.Value.TriggerKind);
		Assert.AreEqual("(", context.Value.TriggerCharacter);
		Assert.IsTrue(context.Value.IsRetrigger);

		SignatureHelpResponse? activeHelp = context.Value.ActiveSignatureHelp;

		Assert.IsNotNull(activeHelp);
		Assert.AreEqual(1, activeHelp.ActiveSignature);
		Assert.AreEqual(JsonValueKind.Number, activeHelp.ActiveParameter.ValueKind);
		Assert.AreEqual(0, activeHelp.ActiveParameter.GetInt32());
		Assert.IsNotNull(activeHelp.Signatures);
		Assert.AreEqual(2, activeHelp.Signatures!.Count);
		Assert.AreEqual("spawn(room, objectName)", activeHelp.Signatures[1].Label);
		Assert.AreEqual(JsonValueKind.Undefined, activeHelp.Signatures[1].ActiveParameter.ValueKind);

		SignatureHelpSignaturePayload signature = activeHelp.Signatures[0];

		Assert.AreEqual("spawn(room)", signature.Label);
		Assert.AreEqual("Spawns an object.", signature.Documentation?.GetString());
		Assert.IsNotNull(signature.Parameters);
		Assert.AreEqual("room", signature.Parameters![0].Label.GetString());
		Assert.AreEqual("Room id.", signature.Parameters[0].Documentation?.GetString());
	}
}
