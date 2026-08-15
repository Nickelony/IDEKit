using Nickelony.IDEKit.IntelliSense;
using Nickelony.IDEKit.IntelliSense.Hover;

namespace Nickelony.LanguageServer.Lua.Tests;

public sealed partial class ResponseParserTests
{
	[TestMethod]
	public void ParseHoverInfo_PreservesIndentedMarkdownAndHardBreakWhitespace()
	{
		HoverResponse response = DeserializeHoverResponse(new
		{
			contents = new
			{
				kind = "markdown",
				value = "    local value = 1  \nnext"
			}
		});

		TextHoverInfo? hover = ResponseParser.ParseHoverInfo(response, string.Empty);

		Assert.IsNotNull(hover);
		Assert.AreEqual("    local value = 1  \nnext", hover.Content);
		Assert.AreEqual(TextMarkupKind.Markdown, hover.ContentKind);
		Assert.IsNull(hover.Range);
	}

	[TestMethod]
	public void ParseHoverInfo_NormalizesMarkdownLineEndings()
	{
		HoverResponse response = DeserializeHoverResponse(new
		{
			contents = new
			{
				kind = "markdown",
				value = "first\r\nsecond"
			}
		});

		TextHoverInfo? hover = ResponseParser.ParseHoverInfo(response, string.Empty);

		// Markdown line endings are normalized like completion documentation; the surrounding
		// whitespace of the payload itself is preserved.
		Assert.IsNotNull(hover);
		Assert.AreEqual("first\nsecond", hover.Content);
		Assert.AreEqual(TextMarkupKind.Markdown, hover.ContentKind);
	}

	[TestMethod]
	public void ParseHoverInfo_ConvertsProtocolRangeToSnapshotOffsets()
	{
		const string document = "local a = 1\nlocal b = 2\nlocal c = 3\nlocal value = 1";

		HoverResponse response = DeserializeHoverResponse(new
		{
			contents = new
			{
				kind = "plaintext",
				value = "local value = 1"
			},
			range = new
			{
				start = new { line = 3, character = 2 },
				end = new { line = 3, character = 7 }
			}
		});

		TextHoverInfo? hover = ResponseParser.ParseHoverInfo(response, document);

		Assert.IsNotNull(hover);
		Assert.AreEqual(new TextRange(38, 5), hover.Range);
	}

	[TestMethod]
	public void ParseHoverInfo_ReversedProtocolRange_IsDropped()
	{
		const string document = "local value = 1";

		HoverResponse response = DeserializeHoverResponse(new
		{
			contents = new
			{
				kind = "plaintext",
				value = "local value = 1"
			},
			range = new
			{
				start = new { line = 0, character = 7 },
				end = new { line = 0, character = 2 }
			}
		});

		TextHoverInfo? hover = ResponseParser.ParseHoverInfo(response, document);

		Assert.IsNotNull(hover);
		Assert.IsNull(hover.Range);
	}

	[TestMethod]
	public void ParseHoverInfo_RangeBeyondTheLineLength_ClampsToTheDocument()
	{
		const string document = "local value = 1";

		HoverResponse response = DeserializeHoverResponse(new
		{
			contents = new
			{
				kind = "plaintext",
				value = "local value = 1"
			},
			range = new
			{
				start = new { line = 0, character = 6 },
				end = new { line = 0, character = 99 }
			}
		});

		TextHoverInfo? hover = ResponseParser.ParseHoverInfo(response, document);

		// Characters beyond the line length are clamped; only negative coordinates reject the range.
		Assert.IsNotNull(hover);
		Assert.AreEqual(new TextRange(6, 9), hover.Range);
	}

	[TestMethod]
	public void ParseHoverInfo_NegativeProtocolCoordinates_RejectTheRange()
	{
		const string document = "local value = 1";

		HoverResponse response = DeserializeHoverResponse(new
		{
			contents = new
			{
				kind = "plaintext",
				value = "local value = 1"
			},
			range = new
			{
				start = new { line = -1, character = 0 },
				end = new { line = 0, character = 5 }
			}
		});

		TextHoverInfo? hover = ResponseParser.ParseHoverInfo(response, document);

		Assert.IsNotNull(hover);
		Assert.IsNull(hover.Range);
	}

	[TestMethod]
	public void ParseHoverInfo_CombinesMarkupArrayWithoutTrimmingIndentedMarkdownFragment()
	{
		HoverResponse response = DeserializeHoverResponse(new
		{
			contents = new object[]
			{
				"Summary",
				new
				{
					kind = "markdown",
					value = "    local value = 1"
				}
			}
		});

		TextHoverInfo? hover = ResponseParser.ParseHoverInfo(response, string.Empty);

		Assert.IsNotNull(hover);
		Assert.AreEqual("Summary\n\n    local value = 1", hover.Content);
		Assert.AreEqual(TextMarkupKind.Markdown, hover.ContentKind);
	}

	[TestMethod]
	public void ParseHoverInfo_CodeBlockPayloadUsesFenceLongerThanEmbeddedBackticks()
	{
		HoverResponse response = DeserializeHoverResponse(new
		{
			contents = new
			{
				language = "lua",
				value = "print(\"```\")"
			}
		});

		TextHoverInfo? hover = ResponseParser.ParseHoverInfo(response, string.Empty);

		Assert.IsNotNull(hover);
		Assert.AreEqual("````lua\nprint(\"```\")\n````", hover.Content.Replace("\r\n", "\n", StringComparison.Ordinal));
		Assert.AreEqual(TextMarkupKind.Markdown, hover.ContentKind);
	}

	[TestMethod]
	public void ParseHoverInfo_MissingContents_ReturnsNull()
	{
		HoverResponse response = DeserializeHoverResponse(new { });

		TextHoverInfo? hover = ResponseParser.ParseHoverInfo(response, "local value = 1");

		Assert.IsNull(hover);
	}

	[TestMethod]
	public void ParseHoverInfo_NullOrBlankContents_ReturnsNull()
	{
		HoverResponse nullContents = DeserializeHoverResponse(new { contents = (object?)null });
		HoverResponse blankContents = DeserializeHoverResponse(new
		{
			contents = new
			{
				kind = "markdown",
				value = "  "
			}
		});

		Assert.IsNull(ResponseParser.ParseHoverInfo(nullContents, "local value = 1"));
		Assert.IsNull(ResponseParser.ParseHoverInfo(blankContents, "local value = 1"));
	}
}
