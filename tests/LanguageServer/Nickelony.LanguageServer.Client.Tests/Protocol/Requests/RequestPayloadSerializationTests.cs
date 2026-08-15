using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// Pins the wire shape of the outbound request and notification payloads: the JSON property names a language
/// server receives, nested member shapes, and the omission of optional members.
/// </summary>
[TestClass]
public sealed class RequestPayloadSerializationTests
{
	[TestMethod]
	public void Serialize_CompletionParams_UsesSpecPropertyNamesAndOmitsAbsentTriggerCharacter()
	{
		JsonElement element = JsonSerializer.SerializeToElement(new CompletionParams(
			new TextDocumentIdentifier("file:///workspace/test.ext"),
			new ProtocolPosition(3, 5),
			new CompletionContextPayload(CompletionTriggerKind.Invoked)));

		Assert.AreEqual("file:///workspace/test.ext", element.GetProperty("textDocument").GetProperty("uri").GetString());

		JsonElement position = element.GetProperty("position");

		Assert.AreEqual(3, position.GetProperty("line").GetInt32());
		Assert.AreEqual(5, position.GetProperty("character").GetInt32());

		JsonElement context = element.GetProperty("context");

		Assert.AreEqual((int)CompletionTriggerKind.Invoked, context.GetProperty("triggerKind").GetInt32());
		Assert.IsFalse(context.TryGetProperty("triggerCharacter", out _), "A null trigger character must be omitted.");
	}

	[TestMethod]
	public void Serialize_CompletionParams_WritesTriggerCharacterWhenPresent()
	{
		JsonElement element = JsonSerializer.SerializeToElement(new CompletionParams(
			new TextDocumentIdentifier("file:///workspace/test.ext"),
			new ProtocolPosition(0, 0),
			new CompletionContextPayload(CompletionTriggerKind.TriggerCharacter, TriggerCharacter: ".")));

		Assert.AreEqual(".", element.GetProperty("context").GetProperty("triggerCharacter").GetString());
	}

	[TestMethod]
	public void Serialize_DocumentFormattingParams_UsesSpecPropertyNames()
	{
		JsonElement element = JsonSerializer.SerializeToElement(new DocumentFormattingParams(
			new TextDocumentIdentifier("file:///workspace/test.ext"),
			new FormattingOptionsPayload(TabSize: 4, InsertSpaces: true)));

		JsonElement options = element.GetProperty("options");

		Assert.AreEqual(4, options.GetProperty("tabSize").GetInt32());
		Assert.IsTrue(options.GetProperty("insertSpaces").GetBoolean());
	}

	[TestMethod]
	public void Serialize_ReferenceParams_UsesSpecPropertyNames()
	{
		JsonElement element = JsonSerializer.SerializeToElement(new ReferenceParams(
			new TextDocumentIdentifier("file:///workspace/test.ext"),
			new ProtocolPosition(0, 1),
			new ReferenceContextPayload(IncludeDeclaration: true)));

		Assert.AreEqual("file:///workspace/test.ext", element.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.IsTrue(element.GetProperty("context").GetProperty("includeDeclaration").GetBoolean());
	}

	[TestMethod]
	public void Serialize_RenameParams_UsesSpecPropertyNames()
	{
		JsonElement element = JsonSerializer.SerializeToElement(new RenameParams(
			new TextDocumentIdentifier("file:///workspace/test.ext"),
			new ProtocolPosition(2, 3),
			NewName: "renamed"));

		Assert.AreEqual("renamed", element.GetProperty("newName").GetString());
		Assert.AreEqual(2, element.GetProperty("position").GetProperty("line").GetInt32());
	}

	[TestMethod]
	public void Serialize_TextDocumentPositionAndSymbolParams_UseSpecPropertyNames()
	{
		JsonElement positionElement = JsonSerializer.SerializeToElement(new TextDocumentPositionParams(
			new TextDocumentIdentifier("file:///workspace/test.ext"),
			new ProtocolPosition(7, 8)));

		Assert.AreEqual(7, positionElement.GetProperty("position").GetProperty("line").GetInt32());
		Assert.AreEqual(8, positionElement.GetProperty("position").GetProperty("character").GetInt32());

		JsonElement symbolElement = JsonSerializer.SerializeToElement(new DocumentSymbolParams(
			new TextDocumentIdentifier("file:///workspace/test.ext")));

		Assert.AreEqual("file:///workspace/test.ext", symbolElement.GetProperty("textDocument").GetProperty("uri").GetString());
	}

	[TestMethod]
	public void Serialize_SemanticTokensParams_UseSpecPropertyNames()
	{
		JsonElement fullElement = JsonSerializer.SerializeToElement(new SemanticTokensParams(
			new TextDocumentIdentifier("file:///workspace/test.ext")));

		Assert.AreEqual("file:///workspace/test.ext", fullElement.GetProperty("textDocument").GetProperty("uri").GetString());
	}

	[TestMethod]
	public void Serialize_CodeActionParams_UsesSpecPropertyNamesAndNestedRange()
	{
		JsonElement element = JsonSerializer.SerializeToElement(new CodeActionParams(
			new TextDocumentIdentifier("file:///workspace/test.ext"),
			new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(1, 2)),
			new CodeActionContextPayload([])));

		JsonElement range = element.GetProperty("range");

		Assert.AreEqual(1, range.GetProperty("end").GetProperty("line").GetInt32());
		Assert.AreEqual(2, range.GetProperty("end").GetProperty("character").GetInt32());
		Assert.AreEqual(0, element.GetProperty("context").GetProperty("diagnostics").GetArrayLength());
	}

	[TestMethod]
	public void Serialize_DidCloseTextDocumentParams_UsesSpecPropertyNames()
	{
		JsonElement element = JsonSerializer.SerializeToElement(new DidCloseTextDocumentParams(
			new TextDocumentIdentifier("file:///workspace/test.ext")));

		Assert.AreEqual("file:///workspace/test.ext", element.GetProperty("textDocument").GetProperty("uri").GetString());
	}

	[TestMethod]
	public void Serialize_DidChangeWatchedFilesParams_UsesSpecPropertyNames()
	{
		JsonElement element = JsonSerializer.SerializeToElement(new DidChangeWatchedFilesParams(
			[new FileEventPayload("file:///workspace/test.ext", FileChangeKind.Changed)]));

		JsonElement change = element.GetProperty("changes")[0];

		Assert.AreEqual("file:///workspace/test.ext", change.GetProperty("uri").GetString());
		Assert.AreEqual((int)FileChangeKind.Changed, change.GetProperty("type").GetInt32());
	}

	[TestMethod]
	public void Deserialize_RequestPayloads_RoundTripTheirSpecShape()
	{
		CompletionParams completionParams = JsonSerializer.Deserialize<CompletionParams>(
			"""
			{
			  "textDocument": { "uri": "file:///workspace/test.ext" },
			  "position": { "line": 3, "character": 5 },
			  "context": { "triggerKind": 2, "triggerCharacter": "." }
			}
			""");

		Assert.AreEqual("file:///workspace/test.ext", completionParams.TextDocument.Uri);
		Assert.AreEqual(3, completionParams.Position.Line);
		Assert.AreEqual(5, completionParams.Position.Character);
		Assert.IsNotNull(completionParams.Context);
		Assert.AreEqual(CompletionTriggerKind.TriggerCharacter, completionParams.Context.Value.TriggerKind);
		Assert.AreEqual(".", completionParams.Context.Value.TriggerCharacter);
	}
}
