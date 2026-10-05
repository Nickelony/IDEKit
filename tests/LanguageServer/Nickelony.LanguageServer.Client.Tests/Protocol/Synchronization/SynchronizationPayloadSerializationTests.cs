using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class SynchronizationPayloadSerializationTests
{
	[TestMethod]
	public void DidChangeTextDocumentParams_WithNullRange_OmitsRangeProperty()
	{
		var parameters = new DidChangeTextDocumentParams(
			new VersionedTextDocumentIdentifier("file:///workspace/test.ext", 2),
			[new TextDocumentContentChangeEventPayload("full text", Range: null)]
		);

		string json = JsonSerializer.Serialize(parameters);

		Assert.IsFalse(json.Contains("\"range\"", StringComparison.Ordinal));
		Assert.IsTrue(json.Contains("\"contentChanges\"", StringComparison.Ordinal));
	}

	[TestMethod]
	public void DidChangeTextDocumentParams_WithRange_WritesRangeProperty()
	{
		var parameters = new DidChangeTextDocumentParams(
			new VersionedTextDocumentIdentifier("file:///workspace/test.ext", 2),
			[new TextDocumentContentChangeEventPayload("value", new RangePayload(new PositionPayload(1, 2), new PositionPayload(1, 5)))]
		);

		using var document = JsonDocument.Parse(JsonSerializer.Serialize(parameters));

		JsonElement range = document.RootElement.GetProperty("contentChanges")[0].GetProperty("range");

		Assert.AreEqual(1, range.GetProperty("start").GetProperty("line").GetInt32());
		Assert.AreEqual(5, range.GetProperty("end").GetProperty("character").GetInt32());
	}

	[TestMethod]
	public void DidChangeTextDocumentParams_Deserialize_ParsesDocumentAndChanges()
	{
		DidChangeTextDocumentParams parameters = JsonSerializer.Deserialize<DidChangeTextDocumentParams>(
			"""
			{
			  "textDocument": { "uri": "file:///workspace/test.ext", "version": 3 },
			  "contentChanges": [
			    {
			      "range": {
			        "start": { "line": 1, "character": 2 },
			        "end": { "line": 1, "character": 5 }
			      },
			      "text": "value"
			    }
			  ]
			}
			"""
		);

		Assert.AreEqual("file:///workspace/test.ext", parameters.TextDocument.Uri);
		Assert.AreEqual(3, parameters.TextDocument.Version);
		Assert.HasCount(1, parameters.ContentChanges);
		Assert.AreEqual("value", parameters.ContentChanges[0].Text);
		Assert.AreEqual(1, parameters.ContentChanges[0].Range?.Start.Line);
		Assert.AreEqual(5, parameters.ContentChanges[0].Range?.End.Character);
	}

	[TestMethod]
	public void DidOpenTextDocumentParams_SerializesExpectedPropertyNames()
	{
		var parameters = new DidOpenTextDocumentParams(
			new TextDocumentItemPayload(Uri: "file:///workspace/test.ext", LanguageId: "plaintext", Version: 1, Text: "value(1)")
		);

		using var document = JsonDocument.Parse(JsonSerializer.Serialize(parameters));

		JsonElement textDocument = document.RootElement.GetProperty("textDocument");

		Assert.AreEqual("file:///workspace/test.ext", textDocument.GetProperty("uri").GetString());
		Assert.AreEqual("plaintext", textDocument.GetProperty("languageId").GetString());
		Assert.AreEqual(1, textDocument.GetProperty("version").GetInt32());
		Assert.AreEqual("value(1)", textDocument.GetProperty("text").GetString());
	}

	[TestMethod]
	public void DidOpenTextDocumentParams_Deserialize_RoundTrips()
	{
		DidOpenTextDocumentParams parameters = JsonSerializer.Deserialize<DidOpenTextDocumentParams>(
			"""
			{
			  "textDocument": {
			    "uri": "file:///workspace/test.ext",
			    "languageId": "plaintext",
			    "version": 7,
			    "text": "local value = 1"
			  }
			}
			"""
		);

		Assert.AreEqual("file:///workspace/test.ext", parameters.TextDocument.Uri);
		Assert.AreEqual("plaintext", parameters.TextDocument.LanguageId);
		Assert.AreEqual(7, parameters.TextDocument.Version);
		Assert.AreEqual("local value = 1", parameters.TextDocument.Text);
	}
}
