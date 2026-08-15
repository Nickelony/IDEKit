using Nickelony.IDEKit.IntelliSense;
using System.Text.Json;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class MarkupContentReaderTests
{
	[TestMethod]
	public void ExtractContent_CombinesMixedArrayAndSkipsMalformedEntries()
	{
		JsonElement element = JsonSerializer.SerializeToElement(new object[]
		{
			"Summary",
			new
			{
				kind = "markdown",
				value = "**bold**"
			},
			new
			{
				value = 5
			},
			new
			{
				language = "sample",
				value = "value(1)"
			},
			new
			{
				value = "tail"
			}
		});

		ProtocolMarkupContent content = MarkupContentReader.ExtractContent(element);

		Assert.AreEqual(TextMarkupKind.Markdown, content.Kind);

		Assert.AreEqual(
			"Summary\n\n**bold**\n\n```sample\nvalue(1)\n```\n\ntail",
			content.Text.Replace("\r\n", "\n", StringComparison.Ordinal));
	}

	[TestMethod]
	public void ExtractContent_FallsBackToPlainValueWhenKindHasWrongType()
	{
		JsonElement element = JsonSerializer.SerializeToElement(new
		{
			kind = 5,
			value = "plain text"
		});

		ProtocolMarkupContent content = MarkupContentReader.ExtractContent(element);

		Assert.AreEqual("plain text", content.Text);
		Assert.AreEqual(TextMarkupKind.PlainText, content.Kind);
	}

	[TestMethod]
	public void ExtractContent_ReturnsDefaultForPartiallyMissingCodeBlockPayload()
	{
		JsonElement element = JsonSerializer.SerializeToElement(new
		{
			language = "sample"
		});

		ProtocolMarkupContent content = MarkupContentReader.ExtractContent(element);

		Assert.IsTrue(string.IsNullOrEmpty(content.Text));
		Assert.AreEqual(TextMarkupKind.PlainText, content.Kind);
	}

	[TestMethod]
	public void ExtractContent_StringPayload_IsTreatedAsMarkdown()
	{
		JsonElement element = JsonSerializer.SerializeToElement("**markdown**");

		ProtocolMarkupContent content = MarkupContentReader.ExtractContent(element);

		Assert.AreEqual("**markdown**", content.Text);
		Assert.AreEqual(TextMarkupKind.Markdown, content.Kind);
	}

	[TestMethod]
	public void ExtractContent_NullValueWithMarkdownKind_KeepsMarkdownFlagAndEmptyText()
	{
		JsonElement element = JsonSerializer.SerializeToElement(new
		{
			kind = "markdown",
			value = (string?)null
		});

		ProtocolMarkupContent content = MarkupContentReader.ExtractContent(element);

		Assert.AreEqual(string.Empty, content.Text);
		Assert.AreEqual(TextMarkupKind.Markdown, content.Kind);
	}

	[TestMethod]
	public void ExtractContent_CodeBlockPayload_UsesFenceLongerThanEmbeddedBackticks()
	{
		JsonElement element = JsonSerializer.SerializeToElement(new
		{
			language = "sample",
			value = "value(\"```\")"
		});

		ProtocolMarkupContent content = MarkupContentReader.ExtractContent(element);

		Assert.AreEqual(TextMarkupKind.Markdown, content.Kind);
		Assert.AreEqual("````sample\nvalue(\"```\")\n````", content.Text.Replace("\r\n", "\n", StringComparison.Ordinal));
	}

	[TestMethod]
	public void NormalizeMarkdownText_NormalizesLineEndingsAndReturnsNullForBlankInput()
	{
		Assert.AreEqual("a\nb\nc", MarkupContentReader.NormalizeMarkdownText("a\r\nb\rc"));
		Assert.IsNull(MarkupContentReader.NormalizeMarkdownText("   \r\n"));
		Assert.IsNull(MarkupContentReader.NormalizeMarkdownText(null));
	}

	[TestMethod]
	public void ExtractContent_UnsupportedPayload_ReturnsDefaultWithNonNullText()
	{
		ProtocolMarkupContent content = MarkupContentReader.ExtractContent(JsonSerializer.SerializeToElement(42));

		Assert.IsNotNull(content.Text);
		Assert.AreEqual(string.Empty, content.Text);
		Assert.AreEqual(TextMarkupKind.PlainText, content.Kind);
	}

	[TestMethod]
	public void DefaultMarkupContent_YieldsNonNullEmptyText()
	{
		ProtocolMarkupContent content = default;

		Assert.IsNotNull(content.Text);
		Assert.AreEqual(string.Empty, content.Text);
	}
}
