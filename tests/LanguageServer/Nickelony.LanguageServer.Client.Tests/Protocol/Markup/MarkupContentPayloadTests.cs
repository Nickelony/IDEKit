using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class MarkupContentPayloadTests
{
	[TestMethod]
	public void Deserialize_BareString_ReadsAsPlainText()
	{
		MarkupContentPayload? payload = JsonSerializer.Deserialize<MarkupContentPayload>("\"**not markdown**\"");

		Assert.IsNotNull(payload);
		Assert.AreEqual(MarkupKind.PlainText, payload.Kind);
		Assert.AreEqual("**not markdown**", payload.Value);
	}

	[TestMethod]
	public void Deserialize_MarkupContentObject_ReadsKindAndValue()
	{
		MarkupContentPayload? payload = JsonSerializer.Deserialize<MarkupContentPayload>(
			"""
			{ "kind": "markdown", "value": "**bold**" }
			"""
		);

		Assert.AreEqual(new MarkupContentPayload { Kind = MarkupKind.Markdown, Value = "**bold**" }, payload);
	}

	[TestMethod]
	public void Deserialize_KindInAnotherCasing_ReadsTheKnownKind()
	{
		MarkupContentPayload? payload = JsonSerializer.Deserialize<MarkupContentPayload>(
			"""
			{ "kind": "Markdown", "value": "text" }
			"""
		);

		Assert.AreEqual(MarkupKind.Markdown, payload?.Kind);
	}

	[TestMethod]
	public void Deserialize_UnknownOrMistypedKind_ReadsAsPlainText()
	{
		MarkupContentPayload? unknown = JsonSerializer.Deserialize<MarkupContentPayload>(
			"""
			{ "kind": "html", "value": "<b>text</b>" }
			"""
		);

		MarkupContentPayload? mistyped = JsonSerializer.Deserialize<MarkupContentPayload>(
			"""
			{ "kind": { "unexpected": [ 1, 2 ] }, "value": "text" }
			"""
		);

		Assert.AreEqual(MarkupKind.PlainText, unknown?.Kind);
		Assert.AreEqual("<b>text</b>", unknown?.Value);
		Assert.AreEqual(MarkupKind.PlainText, mistyped?.Kind);
		Assert.AreEqual("text", mistyped?.Value);
	}

	[TestMethod]
	public void Deserialize_NullValueWithMarkdownKind_KeepsTheKindAndEmptyText()
	{
		MarkupContentPayload? payload = JsonSerializer.Deserialize<MarkupContentPayload>(
			"""
			{ "kind": "markdown", "value": null }
			"""
		);

		Assert.AreEqual(new MarkupContentPayload { Kind = MarkupKind.Markdown, Value = string.Empty }, payload);
	}

	[TestMethod]
	public void Deserialize_ObjectWithoutValue_ReadsAsNullAndWarns()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		var options = new JsonSerializerOptions
		{
			Converters = { new MarkupContentPayloadJsonConverter(logger.CreateLogger<MarkupContentPayloadJsonConverter>()) },
		};

		MarkupContentPayload? payload = JsonSerializer.Deserialize<MarkupContentPayload>(
			"""
			{ "kind": "markdown" }
			""",

			options
		);

		Assert.IsNull(payload);
		Assert.IsTrue(logger.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logger.Logs));
	}

	[TestMethod]
	public void Deserialize_ArrayOrNumber_ReadsAsNullAndWarns()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		var options = new JsonSerializerOptions
		{
			Converters = { new MarkupContentPayloadJsonConverter(logger.CreateLogger<MarkupContentPayloadJsonConverter>()) },
		};

		Assert.IsNull(JsonSerializer.Deserialize<MarkupContentPayload>("[ \"a\", \"b\" ]", options));
		Assert.IsNull(JsonSerializer.Deserialize<MarkupContentPayload>("42", options));
		Assert.IsTrue(logger.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logger.Logs));
	}

	[TestMethod]
	public void Deserialize_JsonNull_ReadsAsNullWithoutWarning()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		var options = new JsonSerializerOptions
		{
			Converters = { new MarkupContentPayloadJsonConverter(logger.CreateLogger<MarkupContentPayloadJsonConverter>()) },
		};

		Assert.IsNull(JsonSerializer.Deserialize<MarkupContentPayload>("null", options));
		Assert.IsFalse(logger.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logger.Logs));
	}

	[TestMethod]
	public void Serialize_WritesMarkupContentWithTheProtocolKindNames()
	{
		string markdown = JsonSerializer.Serialize(new MarkupContentPayload { Kind = MarkupKind.Markdown, Value = "a\nb" });
		string plainText = JsonSerializer.Serialize(new MarkupContentPayload { Kind = MarkupKind.PlainText, Value = "text" });

		Assert.AreEqual("""{"kind":"markdown","value":"a\nb"}""", markdown);
		Assert.AreEqual("""{"kind":"plaintext","value":"text"}""", plainText);
	}

	[TestMethod]
	public void SerializeThenDeserialize_RoundTripsBothKinds()
	{
		foreach (MarkupKind kind in new[] { MarkupKind.PlainText, MarkupKind.Markdown })
		{
			var payload = new MarkupContentPayload { Kind = kind, Value = "line one\nline two" };
			Assert.AreEqual(payload, JsonSerializer.Deserialize<MarkupContentPayload>(JsonSerializer.Serialize(payload)));
		}
	}

	[TestMethod]
	public void MarkupKind_ReadsOnlyMarkdownAsMarkdown()
	{
		Assert.AreEqual(MarkupKind.Markdown, JsonSerializer.Deserialize<MarkupKind>("\"markdown\""));
		Assert.AreEqual(MarkupKind.PlainText, JsonSerializer.Deserialize<MarkupKind>("\"plaintext\""));
		Assert.AreEqual(MarkupKind.PlainText, JsonSerializer.Deserialize<MarkupKind>("\"other\""));
		Assert.AreEqual(MarkupKind.PlainText, JsonSerializer.Deserialize<MarkupKind>("1"));
		Assert.AreEqual(MarkupKind.PlainText, JsonSerializer.Deserialize<MarkupKind>("null"));
		Assert.AreEqual(MarkupKind.PlainText, JsonSerializer.Deserialize<MarkupKind>("{ \"a\": [ 1 ] }"));
	}

	[TestMethod]
	public void MarkupKind_WritesTheProtocolNames()
	{
		Assert.AreEqual("\"markdown\"", JsonSerializer.Serialize(MarkupKind.Markdown));
		Assert.AreEqual("\"plaintext\"", JsonSerializer.Serialize(MarkupKind.PlainText));
	}
}
