using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class HoverResponseContentsTests
{
	[TestMethod]
	public void Deserialize_MarkupObject_ReadsKindAndValue()
	{
		HoverResponse response = Deserialize(
			"""
			{ "contents": { "kind": "markdown", "value": "**bold**" } }
			"""
		);

		Assert.HasCount(1, response.Contents);
		Assert.AreEqual(Markup(MarkupKind.Markdown, "**bold**"), response.Contents[0]);
	}

	[TestMethod]
	public void Deserialize_PlainTextMarkupObject_ReadsPlainText()
	{
		HoverResponse response = Deserialize(
			"""
			{ "contents": { "kind": "plaintext", "value": "text" } }
			"""
		);

		Assert.AreEqual(Markup(MarkupKind.PlainText, "text"), response.Contents[0]);
	}

	[TestMethod]
	public void Deserialize_MarkedStringArray_ReadsOneEntryPerElement()
	{
		HoverResponse response = Deserialize(
			"""
			{ "contents": [ "Summary", { "kind": "markdown", "value": "Details" } ] }
			"""
		);

		Assert.HasCount(2, response.Contents);
		Assert.AreEqual(Markup(MarkupKind.Markdown, "Summary"), response.Contents[0]);
		Assert.AreEqual(Markup(MarkupKind.Markdown, "Details"), response.Contents[1]);
	}

	[TestMethod]
	public void Deserialize_MixedArray_SkipsMalformedElementsAndConvertsCodeBlocks()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		HoverResponse response = Deserialize(
			"""
			{
			  "contents": [
			    "Summary",
			    { "kind": "markdown", "value": "**bold**" },
			    { "value": 5 },
			    { "language": "sample", "value": "value(1)" },
			    { "value": "tail" }
			  ]
			}
			""",

			logger.CreateLogger<HoverResponseJsonConverter>()
		);

		Assert.HasCount(4, response.Contents);
		Assert.AreEqual(Markup(MarkupKind.Markdown, "Summary"), response.Contents[0]);
		Assert.AreEqual(Markup(MarkupKind.Markdown, "**bold**"), response.Contents[1]);
		Assert.AreEqual(Markup(MarkupKind.Markdown, "```sample\nvalue(1)\n```"), response.Contents[2]);
		Assert.AreEqual(Markup(MarkupKind.PlainText, "tail"), response.Contents[3]);
		Assert.IsTrue(logger.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logger.Logs));
	}

	[TestMethod]
	public void Deserialize_KindOfWrongType_ReadsTheValueAsPlainText()
	{
		HoverResponse response = Deserialize(
			"""
			{ "contents": { "kind": 5, "value": "plain text" } }
			"""
		);

		Assert.AreEqual(Markup(MarkupKind.PlainText, "plain text"), response.Contents[0]);
	}

	[TestMethod]
	public void Deserialize_ValueWithoutKindOrLanguage_ReadsAsPlainText()
	{
		HoverResponse response = Deserialize(
			"""
			{ "contents": { "value": "plain text" } }
			"""
		);

		Assert.AreEqual(Markup(MarkupKind.PlainText, "plain text"), response.Contents[0]);
	}

	[TestMethod]
	public void Deserialize_CodeBlockWithoutValue_IsSkipped()
	{
		HoverResponse response = Deserialize(
			"""
			{ "contents": { "language": "sample" } }
			"""
		);

		Assert.IsEmpty(response.Contents);
	}

	[TestMethod]
	public void Deserialize_NullValueWithMarkdownKind_KeepsTheKindAndEmptyText()
	{
		HoverResponse response = Deserialize(
			"""
			{ "contents": { "kind": "markdown", "value": null } }
			"""
		);

		Assert.AreEqual(Markup(MarkupKind.Markdown, string.Empty), response.Contents[0]);
	}

	[TestMethod]
	public void Deserialize_CodeBlock_UsesAFenceLongerThanEmbeddedBackticks()
	{
		HoverResponse response = Deserialize(
			"""
			{ "contents": { "language": "sample", "value": "value(\"```\")" } }
			"""
		);

		Assert.AreEqual(Markup(MarkupKind.Markdown, "````sample\nvalue(\"```\")\n````"), response.Contents[0]);
	}

	[TestMethod]
	public void Deserialize_CodeBlockWithAnUnsafeLanguage_OmitsTheLanguage()
	{
		HoverResponse response = Deserialize(
			"""
			{ "contents": { "language": "sam ple", "value": "code" } }
			"""
		);

		Assert.AreEqual(Markup(MarkupKind.Markdown, "```\ncode\n```"), response.Contents[0]);
	}

	[TestMethod]
	public void Deserialize_ArrayElements_LoseSurroundingLineBreaks()
	{
		HoverResponse response = Deserialize(
			"""
			{ "contents": [ "\r\nfirst\n", { "kind": "markdown", "value": "\n  second\r\n" } ] }
			"""
		);

		Assert.AreEqual("first", response.Contents[0].Value);
		Assert.AreEqual("  second", response.Contents[1].Value);
	}

	[TestMethod]
	public void Deserialize_SingleValue_KeepsSurroundingLineBreaks()
	{
		HoverResponse response = Deserialize(
			"""
			{ "contents": { "kind": "markdown", "value": "\nbody\n" } }
			"""
		);

		Assert.AreEqual("\nbody\n", response.Contents[0].Value);
	}

	[TestMethod]
	public void Deserialize_NestedArray_IsSkipped()
	{
		HoverResponse response = Deserialize(
			"""
			{ "contents": [ [ "nested" ], "kept" ] }
			"""
		);

		Assert.HasCount(1, response.Contents);
		Assert.AreEqual("kept", response.Contents[0].Value);
	}

	[TestMethod]
	public void Deserialize_ValueOfAnotherKind_YieldsNoEntriesAndWarns()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		HoverResponse response = Deserialize(
			"""
			{ "contents": 42 }
			""",

			logger.CreateLogger<HoverResponseJsonConverter>()
		);

		Assert.IsEmpty(response.Contents);
		Assert.IsTrue(logger.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logger.Logs));
	}

	[TestMethod]
	public void Deserialize_NullContents_YieldsNoEntriesWithoutWarning()
	{
		using var logger = new RecordingLogger(LogLevel.Warning);

		HoverResponse response = Deserialize(
			"""
			{ "contents": null }
			""",

			logger.CreateLogger<HoverResponseJsonConverter>()
		);

		Assert.IsEmpty(response.Contents);
		Assert.IsFalse(logger.HasEntryAtLeast(LogLevel.Warning), string.Join(Environment.NewLine, logger.Logs));
	}

	[TestMethod]
	public void Serialize_SeveralEntries_WritesAnArrayOfMarkupContent()
	{
		var response = new HoverResponse { Contents = [Markup(MarkupKind.Markdown, "one"), Markup(MarkupKind.PlainText, "two")] };

		string json = JsonSerializer.Serialize(response);

		Assert.AreEqual("""{"contents":[{"kind":"markdown","value":"one"},{"kind":"plaintext","value":"two"}]}""", json);

		HoverResponse roundTrip = Deserialize(json);

		Assert.AreSequenceEqual([.. response.Contents], [.. roundTrip.Contents]);
	}

	[TestMethod]
	public void Serialize_NoEntries_OmitsTheContentsMember() => Assert.AreEqual("{}", JsonSerializer.Serialize(new HoverResponse()));

	private static MarkupContentPayload Markup(MarkupKind kind, string value) => new() { Kind = kind, Value = value };

	private static HoverResponse Deserialize(string json, ILogger? logger = null)
	{
		var options = new JsonSerializerOptions();
		options.Converters.Add(new HoverResponseJsonConverter(logger));

		return JsonSerializer.Deserialize<HoverResponse>(json, options)
			?? throw new InvalidOperationException("The hover payload deserialized to null.");
	}
}
