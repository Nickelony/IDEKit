using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class ProtocolEnumSerializationTests
{
	[TestMethod]
	public void CompletionTriggerKind_SerializesAsProtocolNumbers()
	{
		string invokedJson = JsonSerializer.Serialize(new CompletionContextPayload(CompletionTriggerKind.Invoked));
		string triggerCharacterJson = JsonSerializer.Serialize(
			new CompletionContextPayload(CompletionTriggerKind.TriggerCharacter, "."));

		Assert.AreEqual("""{"triggerKind":1}""", invokedJson);
		Assert.AreEqual("""{"triggerKind":2,"triggerCharacter":"."}""", triggerCharacterJson);
	}

	[TestMethod]
	public void CompletionItemKind_RoundTripsTypedAndKeepsUnknownValues()
	{
		CompletionItemPayload? text = JsonSerializer.Deserialize<CompletionItemPayload>(
			"""{ "label": "x", "kind": 1 }""");
		CompletionItemPayload? function = JsonSerializer.Deserialize<CompletionItemPayload>(
			"""{ "label": "x", "kind": 3 }""");
		CompletionItemPayload? unknown = JsonSerializer.Deserialize<CompletionItemPayload>(
			"""{ "label": "x", "kind": 99 }""");

		Assert.IsNotNull(text);
		Assert.IsNotNull(function);
		Assert.IsNotNull(unknown);
		Assert.AreEqual(CompletionItemKind.Text, text.Kind);
		Assert.AreEqual(CompletionItemKind.Function, function.Kind);
		Assert.AreEqual((CompletionItemKind)99, unknown.Kind);

		string json = JsonSerializer.Serialize(function);

		Assert.IsTrue(json.Contains("\"kind\":3", StringComparison.Ordinal), json);
	}

	[TestMethod]
	public void CompletionItemKind_AbsentAndNullValuesStayNull()
	{
		CompletionItemPayload? absent = JsonSerializer.Deserialize<CompletionItemPayload>("""{ "label": "x" }""");
		CompletionItemPayload? nullValue = JsonSerializer.Deserialize<CompletionItemPayload>(
			"""{ "label": "x", "kind": null }""");

		Assert.IsNotNull(absent);
		Assert.IsNotNull(nullValue);
		Assert.IsNull(absent.Kind);
		Assert.IsNull(nullValue.Kind);

		string json = JsonSerializer.Serialize(absent);

		Assert.IsFalse(json.Contains("\"kind\"", StringComparison.Ordinal), json);
	}

	[TestMethod]
	public void InsertTextFormat_RoundTripsTypedAndKeepsUnknownValues()
	{
		CompletionItemPayload? snippet = JsonSerializer.Deserialize<CompletionItemPayload>(
			"""{ "label": "x", "insertTextFormat": 2 }""");
		CompletionItemPayload? unknown = JsonSerializer.Deserialize<CompletionItemPayload>(
			"""{ "label": "x", "insertTextFormat": 7 }""");

		Assert.IsNotNull(snippet);
		Assert.IsNotNull(unknown);
		Assert.AreEqual(InsertTextFormat.Snippet, snippet.InsertTextFormat);
		Assert.AreEqual((InsertTextFormat)7, unknown.InsertTextFormat);

		string json = JsonSerializer.Serialize(snippet);

		Assert.IsTrue(json.Contains("\"insertTextFormat\":2", StringComparison.Ordinal), json);
	}

	[TestMethod]
	public void DiagnosticSeverity_RoundTripsTypedAndKeepsUnknownValues()
	{
		PublishDiagnosticsParams parameters = DeserializeDiagnostics(
			"""
			{
			  "diagnostics": [
			    {
			      "range": {
			        "start": { "line": 0, "character": 0 },
			        "end": { "line": 0, "character": 1 }
			      },
			      "severity": 3
			    },
			    {
			      "range": {
			        "start": { "line": 0, "character": 0 },
			        "end": { "line": 0, "character": 1 }
			      },
			      "severity": 42
			    }
			  ]
			}
			""");

		Assert.IsNotNull(parameters.Diagnostics);
		Assert.AreEqual(DiagnosticSeverity.Information, parameters.Diagnostics[0].Severity);
		Assert.AreEqual((DiagnosticSeverity)42, parameters.Diagnostics[1].Severity);
	}

	private static PublishDiagnosticsParams DeserializeDiagnostics(string json)
		=> JsonSerializer.Deserialize<PublishDiagnosticsParams>(json)!;

	[TestMethod]
	public void MessageType_RoundTripsTypedAndKeepsUnknownValues()
	{
		WindowMessageParams? warning = JsonSerializer.Deserialize<WindowMessageParams>(
			"""{ "type": 2, "message": "x" }""");
		WindowMessageParams? unknown = JsonSerializer.Deserialize<WindowMessageParams>(
			"""{ "type": 42, "message": "x" }""");
		WindowMessageParams? absent = JsonSerializer.Deserialize<WindowMessageParams>(
			"""{ "message": "x" }""");

		Assert.IsNotNull(warning);
		Assert.IsNotNull(unknown);
		Assert.IsNotNull(absent);
		Assert.AreEqual(MessageType.Warning, warning.Value.Type);
		Assert.AreEqual((MessageType)42, unknown.Value.Type);
		Assert.IsNull(absent.Value.Type);

		string json = JsonSerializer.Serialize(new WindowMessageParams(MessageType.Error, "x"));

		Assert.IsTrue(json.Contains("\"type\":1", StringComparison.Ordinal), json);
	}

	[TestMethod]
	public void CompletionItemTag_RoundTripsTypedAndKeepsUnknownValues()
	{
		CompletionItemPayload? deprecated = JsonSerializer.Deserialize<CompletionItemPayload>(
			"""{ "label": "x", "tags": [1] }""");
		CompletionItemPayload? unknown = JsonSerializer.Deserialize<CompletionItemPayload>(
			"""{ "label": "x", "tags": [99] }""");

		Assert.IsNotNull(deprecated);
		Assert.IsNotNull(unknown);
		Assert.IsNotNull(deprecated.Tags);
		Assert.IsNotNull(unknown.Tags);
		Assert.AreEqual(CompletionItemTag.Deprecated, deprecated.Tags[0]);
		Assert.AreEqual((CompletionItemTag)99, unknown.Tags[0]);

		string json = JsonSerializer.Serialize(deprecated);

		Assert.IsTrue(json.Contains("\"tags\":[1]", StringComparison.Ordinal), json);
	}
}
