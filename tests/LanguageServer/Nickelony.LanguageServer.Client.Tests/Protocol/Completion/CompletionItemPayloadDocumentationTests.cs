using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class CompletionItemPayloadDocumentationTests
{
	[TestMethod]
	public void Deserialize_BareStringDocumentation_ReadsAsPlainText()
	{
		CompletionItemPayload? item = JsonSerializer.Deserialize<CompletionItemPayload>(
			"""
			{ "label": "print", "documentation": "Prints **a value**." }
			"""
		);

		Assert.IsNotNull(item);
		Assert.AreEqual(new MarkupContentPayload { Kind = MarkupKind.PlainText, Value = "Prints **a value**." }, item.Documentation);
	}

	[TestMethod]
	public void Deserialize_MarkupContentDocumentation_ReadsKindAndValue()
	{
		CompletionItemPayload? item = JsonSerializer.Deserialize<CompletionItemPayload>(
			"""
			{ "label": "print", "documentation": { "kind": "markdown", "value": "Prints **a value**." } }
			"""
		);

		Assert.IsNotNull(item);
		Assert.AreEqual(new MarkupContentPayload { Kind = MarkupKind.Markdown, Value = "Prints **a value**." }, item.Documentation);
	}

	[TestMethod]
	public void Deserialize_DocumentationOfAnotherShape_LeavesItAbsentWithoutFailingTheItem()
	{
		CompletionItemPayload? item = JsonSerializer.Deserialize<CompletionItemPayload>(
			"""
			{ "label": "print", "documentation": [ "a", "b" ], "detail": "kept" }
			"""
		);

		Assert.IsNotNull(item);
		Assert.IsNull(item.Documentation);
		Assert.AreEqual("print", item.Label);
		Assert.AreEqual("kept", item.Detail);
	}

	[TestMethod]
	public void Serialize_ResolveRequestItem_WritesTheDocumentationAsMarkupContent()
	{
		CompletionItemPayload? item = JsonSerializer.Deserialize<CompletionItemPayload>(
			"""
			{ "label": "print", "documentation": "Prints a value.", "data": { "id": 7 } }
			"""
		);

		using var written = JsonDocument.Parse(JsonSerializer.Serialize(item));

		JsonElement root = written.RootElement;

		Assert.AreEqual("print", root.GetProperty("label").GetString());
		Assert.AreEqual("plaintext", root.GetProperty("documentation").GetProperty("kind").GetString());
		Assert.AreEqual("Prints a value.", root.GetProperty("documentation").GetProperty("value").GetString());
		Assert.AreEqual(7, root.GetProperty("data").GetProperty("id").GetInt32());
	}

	[TestMethod]
	public void Serialize_WithoutDocumentation_OmitsTheMember()
	{
		string json = JsonSerializer.Serialize(new CompletionItemPayload { Label = "print" });
		Assert.IsFalse(json.Contains("documentation", StringComparison.Ordinal), json);
	}

	[TestMethod]
	public void Deserialize_CompletionListThroughTheClientConverter_ReadsDocumentationOfEveryItem()
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new CompletionResponseJsonConverter());

		CompletionResponse? response = JsonSerializer.Deserialize<CompletionResponse>(
			"""
			{
			  "isIncomplete": false,
			  "items": [
			    { "label": "a", "documentation": "plain" },
			    { "label": "b", "documentation": { "kind": "markdown", "value": "rich" } },
			    { "label": "c" }
			  ]
			}
			""",

			options
		);

		Assert.IsNotNull(response);
		Assert.IsNotNull(response.Items);
		Assert.HasCount(3, response.Items);
		Assert.AreEqual(MarkupKind.PlainText, response.Items[0].Documentation?.Kind);
		Assert.AreEqual(MarkupKind.Markdown, response.Items[1].Documentation?.Kind);
		Assert.IsNull(response.Items[2].Documentation);
	}
}
