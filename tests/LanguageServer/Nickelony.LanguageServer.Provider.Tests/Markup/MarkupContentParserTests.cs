namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class MarkupContentParserTests
{
	[TestMethod]
	public void Parse_PlainTextPayload_IsTrimmed()
	{
		MarkupContent? content = MarkupContentParser.Parse(new Lsp.MarkupContentPayload { Kind = Lsp.MarkupKind.PlainText, Value = "  text \r\n" });

		Assert.AreEqual("text", content?.Value);
		Assert.AreEqual(MarkupKind.PlainText, content?.Kind);
	}

	[TestMethod]
	public void Parse_MarkdownPayload_KeepsWhitespaceAndNormalizesLineEndings()
	{
		MarkupContent? content = MarkupContentParser.Parse(new Lsp.MarkupContentPayload { Kind = Lsp.MarkupKind.Markdown, Value = "  a\r\nb  \rc " });

		Assert.AreEqual("  a\nb  \nc ", content?.Value);
		Assert.AreEqual(MarkupKind.Markdown, content?.Kind);
	}

	[TestMethod]
	public void Parse_BlankOrMissingPayload_ReturnsNull()
	{
		Assert.IsNull(MarkupContentParser.Parse(null));

		Assert.IsNull(MarkupContentParser.Parse(new Lsp.MarkupContentPayload { Kind = Lsp.MarkupKind.Markdown, Value = "   \r\n" }));

		Assert.IsNull(MarkupContentParser.Parse(new Lsp.MarkupContentPayload { Kind = Lsp.MarkupKind.PlainText, Value = string.Empty }));
	}
}
