using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Markup;
using static Nickelony.LanguageServer.Provider.Tests.PayloadTestHelpers;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class HoverResponseParserTests
{
	[TestMethod]
	public void Parse_PreservesIndentedMarkdownAndHardBreakWhitespace()
	{
		Lsp.HoverResponse response = DeserializeHoverResponse(new { contents = new { kind = "markdown", value = "    local value = 1  \nnext" } });

		HoverInformation? hover = HoverResponseParser.Parse(response, TextLineMap.Build(string.Empty));

		Assert.IsNotNull(hover);
		Assert.AreEqual("    local value = 1  \nnext", hover.Content.Value);
		Assert.AreEqual(MarkupKind.Markdown, hover.Content.Kind);
		Assert.IsNull(hover.Range);
	}

	[TestMethod]
	public void Parse_NormalizesMarkdownLineEndings()
	{
		Lsp.HoverResponse response = DeserializeHoverResponse(new { contents = new { kind = "markdown", value = "first\r\nsecond" } });

		HoverInformation? hover = HoverResponseParser.Parse(response, TextLineMap.Build(string.Empty));

		// Markdown line endings are normalized like completion documentation; the surrounding
		// whitespace of the payload itself is preserved.
		Assert.IsNotNull(hover);
		Assert.AreEqual("first\nsecond", hover.Content.Value);
		Assert.AreEqual(MarkupKind.Markdown, hover.Content.Kind);
	}

	[TestMethod]
	public void Parse_ConvertsProtocolRangeToSnapshotOffsets()
	{
		const string document = "local a = 1\nlocal b = 2\nlocal c = 3\nlocal value = 1";

		Lsp.HoverResponse response = DeserializeHoverResponse(
			new
			{
				contents = new { kind = "plaintext", value = "local value = 1" },
				range = new { start = new { line = 3, character = 2 }, end = new { line = 3, character = 7 } },
			}
		);

		HoverInformation? hover = HoverResponseParser.Parse(response, TextLineMap.Build(document));

		Assert.IsNotNull(hover);
		Assert.AreEqual(new TextRange(38, 5), hover.Range);
	}

	[TestMethod]
	public void Parse_ReversedProtocolRange_IsDropped()
	{
		const string document = "local value = 1";

		Lsp.HoverResponse response = DeserializeHoverResponse(
			new
			{
				contents = new { kind = "plaintext", value = "local value = 1" },
				range = new { start = new { line = 0, character = 7 }, end = new { line = 0, character = 2 } },
			}
		);

		HoverInformation? hover = HoverResponseParser.Parse(response, TextLineMap.Build(document));

		Assert.IsNotNull(hover);
		Assert.IsNull(hover.Range);
	}

	[TestMethod]
	public void Parse_RangeBeyondTheLineLength_ClampsToTheDocument()
	{
		const string document = "local value = 1";

		Lsp.HoverResponse response = DeserializeHoverResponse(
			new
			{
				contents = new { kind = "plaintext", value = "local value = 1" },
				range = new { start = new { line = 0, character = 6 }, end = new { line = 0, character = 99 } },
			}
		);

		HoverInformation? hover = HoverResponseParser.Parse(response, TextLineMap.Build(document));

		// Characters beyond the line length are clamped; only negative coordinates reject the range.
		Assert.IsNotNull(hover);
		Assert.AreEqual(new TextRange(6, 9), hover.Range);
	}

	[TestMethod]
	public void Parse_NegativeProtocolCoordinates_RejectTheRange()
	{
		const string document = "local value = 1";

		Lsp.HoverResponse response = DeserializeHoverResponse(
			new
			{
				contents = new { kind = "plaintext", value = "local value = 1" },
				range = new { start = new { line = -1, character = 0 }, end = new { line = 0, character = 5 } },
			}
		);

		HoverInformation? hover = HoverResponseParser.Parse(response, TextLineMap.Build(document));

		Assert.IsNotNull(hover);
		Assert.IsNull(hover.Range);
	}

	[TestMethod]
	public void Parse_CombinesMarkupArrayWithoutTrimmingIndentedMarkdownFragment()
	{
		Lsp.HoverResponse response = DeserializeHoverResponse(
			new { contents = new object[] { "Summary", new { kind = "markdown", value = "    local value = 1" } } }
		);

		HoverInformation? hover = HoverResponseParser.Parse(response, TextLineMap.Build(string.Empty));

		Assert.IsNotNull(hover);
		Assert.AreEqual("Summary\n\n    local value = 1", hover.Content.Value);
		Assert.AreEqual(MarkupKind.Markdown, hover.Content.Kind);
	}

	[TestMethod]
	public void Parse_CodeBlockPayloadUsesFenceLongerThanEmbeddedBackticks()
	{
		Lsp.HoverResponse response = DeserializeHoverResponse(new { contents = new { language = "lua", value = "print(\"```\")" } });

		HoverInformation? hover = HoverResponseParser.Parse(response, TextLineMap.Build(string.Empty));

		Assert.IsNotNull(hover);
		Assert.AreEqual("````lua\nprint(\"```\")\n````", hover.Content.Value.Replace("\r\n", "\n", StringComparison.Ordinal));
		Assert.AreEqual(MarkupKind.Markdown, hover.Content.Kind);
	}

	[TestMethod]
	public void Parse_MissingContents_ReturnsNull()
	{
		Lsp.HoverResponse response = DeserializeHoverResponse(new { });

		HoverInformation? hover = HoverResponseParser.Parse(response, TextLineMap.Build("local value = 1"));

		Assert.IsNull(hover);
	}

	[TestMethod]
	public void Parse_JoinsFragmentsWithABlankLineAndSkipsBlankOnes()
	{
		Lsp.HoverResponse response = DeserializeHoverResponse(
			new { contents = new object[] { "first", new { kind = "plaintext", value = "  " }, "second" } }
		);

		HoverInformation? hover = HoverResponseParser.Parse(response, TextLineMap.Build(string.Empty));

		Assert.IsNotNull(hover);
		Assert.AreEqual("first\n\nsecond", hover.Content.Value);
		Assert.AreEqual(MarkupKind.Markdown, hover.Content.Kind);
	}

	[TestMethod]
	public void Parse_PlainTextFragmentsOnly_StayPlainText()
	{
		Lsp.HoverResponse response = DeserializeHoverResponse(
			new { contents = new object[] { new { kind = "plaintext", value = "one" }, new { kind = "plaintext", value = "two" } } }
		);

		HoverInformation? hover = HoverResponseParser.Parse(response, TextLineMap.Build(string.Empty));

		Assert.IsNotNull(hover);
		Assert.AreEqual("one\n\ntwo", hover.Content.Value);
		Assert.AreEqual(MarkupKind.PlainText, hover.Content.Kind);
	}

	[TestMethod]
	public void Parse_OneMarkdownFragmentAmongPlainText_MakesTheJoinMarkdown()
	{
		Lsp.HoverResponse response = DeserializeHoverResponse(new { contents = new object[] { new { kind = "plaintext", value = "one" }, "two" } });

		HoverInformation? hover = HoverResponseParser.Parse(response, TextLineMap.Build(string.Empty));

		Assert.IsNotNull(hover);
		Assert.AreEqual("one\n\ntwo", hover.Content.Value);
		Assert.AreEqual(MarkupKind.Markdown, hover.Content.Kind);
	}

	[TestMethod]
	public void Parse_SingleMarkdownValue_KeepsItsSurroundingLineBreaks()
	{
		Lsp.HoverResponse response = DeserializeHoverResponse(new { contents = new { kind = "markdown", value = "\nbody\n" } });

		HoverInformation? hover = HoverResponseParser.Parse(response, TextLineMap.Build(string.Empty));

		Assert.IsNotNull(hover);
		Assert.AreEqual("\nbody\n", hover.Content.Value);
	}

	[TestMethod]
	public void Parse_ArrayFragments_AreTrimmedOfSurroundingLineBreaks()
	{
		Lsp.HoverResponse response = DeserializeHoverResponse(
			new { contents = new object[] { "\r\nfirst\n", new { kind = "markdown", value = "\nsecond\r\n" } } }
		);

		HoverInformation? hover = HoverResponseParser.Parse(response, TextLineMap.Build(string.Empty));

		Assert.IsNotNull(hover);
		Assert.AreEqual("first\n\nsecond", hover.Content.Value);
	}

	[TestMethod]
	public void Parse_NullOrBlankContents_ReturnsNull()
	{
		Lsp.HoverResponse nullContents = DeserializeHoverResponse(new { contents = (object?)null });
		Lsp.HoverResponse blankContents = DeserializeHoverResponse(new { contents = new { kind = "markdown", value = "  " } });

		Assert.IsNull(HoverResponseParser.Parse(nullContents, TextLineMap.Build("local value = 1")));
		Assert.IsNull(HoverResponseParser.Parse(blankContents, TextLineMap.Build("local value = 1")));
	}
}
