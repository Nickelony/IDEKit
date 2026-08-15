using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class NotificationPayloadSerializationTests
{
	[TestMethod]
	public void DidChangeTextDocumentParams_WithNullRange_OmitsRangeProperty()
	{
		var parameters = new DidChangeTextDocumentParams(
			new VersionedTextDocumentIdentifier("file:///workspace/test.ext", 2),
			[new TextDocumentContentChangePayload("full text", Range: null)]);

		string json = JsonSerializer.Serialize(parameters);

		Assert.IsFalse(json.Contains("\"range\"", StringComparison.Ordinal));
		Assert.IsTrue(json.Contains("\"contentChanges\"", StringComparison.Ordinal));
	}

	[TestMethod]
	public void DidChangeTextDocumentParams_WithRange_WritesRangeProperty()
	{
		var parameters = new DidChangeTextDocumentParams(
			new VersionedTextDocumentIdentifier("file:///workspace/test.ext", 2),
			[
				new TextDocumentContentChangePayload(
					"value",
					new ProtocolRangePayload(
						new ProtocolPosition(1, 2),
						new ProtocolPosition(1, 5)))
			]);

		using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(parameters));
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
			""");

		Assert.AreEqual("file:///workspace/test.ext", parameters.TextDocument.Uri);
		Assert.AreEqual(3, parameters.TextDocument.Version);
		Assert.AreEqual(1, parameters.ContentChanges.Count);
		Assert.AreEqual("value", parameters.ContentChanges[0].Text);
		Assert.AreEqual(1, parameters.ContentChanges[0].Range?.Start.Line);
		Assert.AreEqual(5, parameters.ContentChanges[0].Range?.End.Character);
	}

	[TestMethod]
	public void DidOpenTextDocumentParams_SerializesExpectedPropertyNames()
	{
		var parameters = new DidOpenTextDocumentParams(
			new DidOpenTextDocumentPayload("file:///workspace/test.ext", "plaintext", 1, "value(1)"));

		using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(parameters));
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
			""");

		Assert.AreEqual("file:///workspace/test.ext", parameters.TextDocument.Uri);
		Assert.AreEqual("plaintext", parameters.TextDocument.LanguageId);
		Assert.AreEqual(7, parameters.TextDocument.Version);
		Assert.AreEqual("local value = 1", parameters.TextDocument.Text);
	}

	[TestMethod]
	public void CompletionTextEditPayload_InsertReplaceShape_OmitsClassicRangeAndRoundTrips()
	{
		CompletionTextEditPayload? payload = JsonSerializer.Deserialize<CompletionTextEditPayload>(
			"""
			{
			  "newText": "name",
			  "insert": {
			    "start": { "line": 0, "character": 0 },
			    "end": { "line": 0, "character": 0 }
			  },
			  "replace": {
			    "start": { "line": 0, "character": 0 },
			    "end": { "line": 0, "character": 4 }
			  }
			}
			""");

		Assert.IsInstanceOfType<CompletionInsertReplaceTextEditPayload>(payload);

		var insertReplacePayload = (CompletionInsertReplaceTextEditPayload)payload;

		Assert.AreEqual("name", insertReplacePayload.NewText);
		Assert.AreEqual(4, insertReplacePayload.Replace.End.Character);

		string json = JsonSerializer.Serialize(payload);

		Assert.IsFalse(json.Contains("\"range\"", StringComparison.Ordinal));
		Assert.IsTrue(json.Contains("\"insert\"", StringComparison.Ordinal));
		Assert.IsTrue(json.Contains("\"replace\"", StringComparison.Ordinal));
	}

	[TestMethod]
	public void CompletionTextEditPayload_ClassicRangeShape_OmitsInsertAndReplace()
	{
		CompletionTextEditPayload? payload = JsonSerializer.Deserialize<CompletionTextEditPayload>(
			"""
			{
			  "newText": "name",
			  "range": {
			    "start": { "line": 2, "character": 1 },
			    "end": { "line": 2, "character": 5 }
			  }
			}
			""");

		Assert.IsInstanceOfType<CompletionRangeTextEditPayload>(payload);

		var rangePayload = (CompletionRangeTextEditPayload)payload;

		Assert.AreEqual("name", rangePayload.NewText);
		Assert.AreEqual(5, rangePayload.Range.End.Character);

		string json = JsonSerializer.Serialize(payload);

		Assert.IsTrue(json.Contains("\"range\"", StringComparison.Ordinal));
		Assert.IsFalse(json.Contains("\"insert\"", StringComparison.Ordinal));
		Assert.IsFalse(json.Contains("\"replace\"", StringComparison.Ordinal));
	}
}
