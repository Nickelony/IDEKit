using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Markup;
using static Nickelony.LanguageServer.Provider.Tests.PayloadTestHelpers;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed partial class CompletionResponseParserTests
{
	[TestMethod]
	public void ParseItem_DetailProseDoesNotInfluenceKind()
	{
		Lsp.CompletionItemPayload itemElement = DeserializeCompletionItemPayload(new { label = "arg", detail = "parameter" });

		CompletionItem? item = CompletionResponseParser.ParseItem(itemElement, 0, "text");

		Assert.IsNotNull(item);

		// Kind inference is protocol-only: a payload without a kind keeps the presentation fallback
		// even when its prose mentions a category.
		Assert.AreSame(CompletionItemKind.Generic, item.Kind);
	}

	[TestMethod]
	public void ParseItem_OutOfRangeKind_UsesGenericFallback()
	{
		Lsp.CompletionItemPayload itemElement = CreateCompletionItem(label: "unknown", kind: 999, detail: null, documentation: null);

		CompletionItem? item = CompletionResponseParser.ParseItem(itemElement, 0, "text");

		Assert.IsNotNull(item);

		// A value outside the protocol range cannot be represented; it maps to the presentation
		// fallback instead of masquerading as the real Text kind.
		Assert.AreSame(CompletionItemKind.Generic, item.Kind);
	}

	[TestMethod]
	public void ParseItem_MissingKind_UsesGenericFallback()
	{
		Lsp.CompletionItemPayload itemElement = DeserializeCompletionItemPayload(new { label = "item" });

		CompletionItem? item = CompletionResponseParser.ParseItem(itemElement, 0, "text");

		Assert.IsNotNull(item);
		Assert.AreSame(CompletionItemKind.Generic, item.Kind);
	}

	[TestMethod]
	public void ParseItem_MapsProtocolKindsOneToOne()
	{
		// Every protocol kind must survive into the shared taxonomy unchanged. The protocol names are
		// listed explicitly so an added or renamed protocol kind cannot silently collapse onto another
		// member.
		string[] protocolNames =
		[
			"Text",
			"Method",
			"Function",
			"Constructor",
			"Field",
			"Variable",
			"Class",
			"Interface",
			"Module",
			"Property",
			"Unit",
			"Value",
			"Enum",
			"Keyword",
			"Snippet",
			"Color",
			"File",
			"Reference",
			"Folder",
			"EnumMember",
			"Constant",
			"Struct",
			"Event",
			"Operator",
			"TypeParameter",
		];

		for (int protocolKind = 1; protocolKind <= protocolNames.Length; protocolKind++)
		{
			Lsp.CompletionItemPayload itemElement = CreateCompletionItem(
				label: $"item{protocolKind}",
				kind: protocolKind,
				detail: null,
				documentation: null
			);

			CompletionItem? item = CompletionResponseParser.ParseItem(itemElement, 0, "text");

			Assert.IsNotNull(item);

			CompletionItemKind expectedKind = CompletionItemKindConversion.FromProtocol((Lsp.CompletionItemKind)protocolKind);

			Assert.AreEqual(
				protocolNames[protocolKind - 1],
				expectedKind.Identifier,
				$"Protocol kind {protocolKind} must have a library member with the protocol name."
			);

			Assert.AreSame(expectedKind, item.Kind, $"Protocol kind {protocolKind} must map one-to-one.");
		}
	}

	[TestMethod]
	public void ParseItem_ParsesTextEditRange()
	{
		Lsp.CompletionItemPayload itemElement = DeserializeCompletionItemPayload(
			new
			{
				label = "print",
				kind = 3,
				textEdit = new { newText = "print", range = new { start = new { line = 1, character = 2 }, end = new { line = 1, character = 5 } } },
			}
		);

		CompletionItem? item = CompletionResponseParser.ParseItem(itemElement, 0, "a\nprint");

		Assert.IsNotNull(item);
		Assert.AreEqual("print", item.InsertText);
		Assert.IsNotNull(item.TextEdit);
		Assert.AreEqual(new TextRange(4, 3), item.TextEdit.Value.InsertRange);
		Assert.IsNull(item.TextEdit.Value.ReplaceRange);
		Assert.AreEqual("print", item.TextEdit.Value.NewText);
	}

	[TestMethod]
	public void ParseItem_RejectsUnusableTextEditRange()
	{
		Lsp.CompletionItemPayload itemElement = DeserializeCompletionItemPayload(
			new
			{
				label = "print",
				kind = 3,
				textEdit = new { newText = "print", range = new { start = new { line = 5, character = 2 }, end = new { line = 1, character = 5 } } },
			}
		);

		CompletionItem? item = CompletionResponseParser.ParseItem(itemElement, 0, "a\nprint");

		// A range that cannot be mapped against the snapshot is rejected instead of producing a
		// partial edit; the label remains the commit fallback.
		Assert.IsNotNull(item);
		Assert.IsNull(item.TextEdit);
		Assert.AreEqual("print", item.InsertText);
	}

	[TestMethod]
	public void ParseItem_TextEditOnALineBeyondTheDocument_IsDroppedWhereTheSharedRangeRuleClamps()
	{
		Lsp.CompletionItemPayload itemElement = DeserializeCompletionItemPayload(
			new
			{
				label = "print",
				kind = 3,
				textEdit = new { newText = "print", range = new { start = new { line = 4, character = 0 }, end = new { line = 4, character = 2 } } },
			}
		);

		CompletionItem? item = CompletionResponseParser.ParseItem(itemElement, 0, "a\nprint");

		// A stale edit commits text, so a clamped target would insert it at the wrong place; the label remains the commit fallback.
		Assert.IsNotNull(item);
		Assert.IsNull(item.TextEdit);
		Assert.AreEqual("print", item.InsertText);
	}

	[TestMethod]
	public void ParseItem_TextEditCharacterBeyondTheLine_IsClampedToTheLineEnd()
	{
		Lsp.CompletionItemPayload itemElement = DeserializeCompletionItemPayload(
			new
			{
				label = "print",
				kind = 3,
				textEdit = new { newText = "print", range = new { start = new { line = 1, character = 0 }, end = new { line = 1, character = 40 } } },
			}
		);

		CompletionItem? item = CompletionResponseParser.ParseItem(itemElement, 0, "a\nprint");

		Assert.IsNotNull(item);
		Assert.IsNotNull(item.TextEdit);
		Assert.AreEqual(new TextRange(2, 5), item.TextEdit.Value.InsertRange);
	}

	[TestMethod]
	public void ParseItem_TextEditNewTextSupersedesInsertText()
	{
		Lsp.CompletionItemPayload itemElement = DeserializeCompletionItemPayload(
			new
			{
				label = "print",
				kind = 3,
				insertText = "fallback",

				textEdit = new
				{
					newText = "replacement",
					range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 5 } },
				},
			}
		);

		CompletionItem? item = CompletionResponseParser.ParseItem(itemElement, 0, "print");

		Assert.IsNotNull(item);

		// The edit carries the commit text; the plain insertion text stays available as the fallback.
		Assert.AreEqual("fallback", item.InsertText);
		Assert.AreEqual("replacement", item.TextEdit?.NewText);
	}

	[TestMethod]
	public void ParseItem_WhitespaceTextEditNewText_IsPreserved()
	{
		Lsp.CompletionItemPayload itemElement = DeserializeCompletionItemPayload(
			new
			{
				label = "print",
				kind = 3,
				textEdit = new { newText = " ", range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 5 } } },
			}
		);

		CompletionItem? item = CompletionResponseParser.ParseItem(itemElement, 0, "print");

		Assert.IsNotNull(item);
		Assert.AreEqual(" ", item.TextEdit?.NewText);
	}

	[TestMethod]
	public void ParseItem_SnippetTextEditNewText_PassesThroughWithSnippetFormat()
	{
		Lsp.CompletionItemPayload itemElement = DeserializeCompletionItemPayload(
			new
			{
				label = "spawn",
				kind = 3,
				insertTextFormat = 2,

				textEdit = new
				{
					newText = "spawn($0)",
					range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 5 } },
				},
			}
		);

		CompletionItem? item = CompletionResponseParser.ParseItem(itemElement, 0, "spawn");

		Assert.IsNotNull(item);
		Assert.AreEqual("spawn($0)", item.TextEdit?.NewText);
		Assert.AreEqual(CompletionInsertTextFormat.Snippet, item.InsertTextFormat);
	}

	[TestMethod]
	public void ParseItem_ParsesInsertReplaceEditRanges()
	{
		Lsp.CompletionItemPayload itemElement = DeserializeCompletionItemPayload(
			new
			{
				label = "print",
				kind = 3,

				textEdit = new
				{
					newText = "print",
					insert = new { start = new { line = 0, character = 1 }, end = new { line = 0, character = 3 } },
					replace = new { start = new { line = 0, character = 1 }, end = new { line = 0, character = 6 } },
				},
			}
		);

		CompletionItem? item = CompletionResponseParser.ParseItem(itemElement, 0, "abcdef");

		Assert.IsNotNull(item);
		Assert.IsNotNull(item.TextEdit);
		Assert.IsNotNull(item.TextEdit.Value.ReplaceRange);

		Assert.AreEqual(new TextRange(1, 2), item.TextEdit.Value.InsertRange);
		Assert.AreEqual(new TextRange(1, 5), item.TextEdit.Value.ReplaceRange.Value);
		Assert.AreEqual(6, item.TextEdit.Value.ReplacementRange.EndOffset);
	}

	[TestMethod]
	public void ParseItem_InsertReplaceEditWithMismatchedStarts_DegradesToReplaceRange()
	{
		Lsp.CompletionItemPayload itemElement = DeserializeCompletionItemPayload(
			new
			{
				label = "print",
				kind = 3,

				textEdit = new
				{
					newText = "print",
					insert = new { start = new { line = 0, character = 2 }, end = new { line = 0, character = 3 } },
					replace = new { start = new { line = 0, character = 1 }, end = new { line = 0, character = 6 } },
				},
			}
		);

		CompletionItem? item = CompletionResponseParser.ParseItem(itemElement, 0, "abcdef");

		Assert.IsNotNull(item);
		Assert.IsNotNull(item.TextEdit);
		Assert.IsNull(item.TextEdit.Value.ReplaceRange);
		Assert.AreEqual(new TextRange(1, 5), item.TextEdit.Value.InsertRange);
	}

	[TestMethod]
	public void ParseItem_SnippetInsertText_PassesThroughWithSnippetFormat()
	{
		Lsp.CompletionItemPayload itemElement = DeserializeCompletionItemPayload(
			new
			{
				label = "if",
				kind = 15,
				insertText = "if ${1:condition} then\r\n\t$0\r\nend",
				insertTextFormat = 2,
			}
		);

		CompletionItem? item = CompletionResponseParser.ParseItem(itemElement, 0, "text");

		Assert.IsNotNull(item);
		Assert.AreEqual("if ${1:condition} then\r\n\t$0\r\nend", item.InsertText);
		Assert.AreEqual(CompletionInsertTextFormat.Snippet, item.InsertTextFormat);
	}

	[TestMethod]
	public void ParseItem_UnknownSnippetPlaceholders_PassThroughVerbatim()
	{
		Lsp.CompletionItemPayload itemElement = DeserializeCompletionItemPayload(
			new
			{
				label = "call",
				kind = 3,
				insertText = "call(${name}, ${0:done})",
				insertTextFormat = 2,
			}
		);

		CompletionItem? item = CompletionResponseParser.ParseItem(itemElement, 0, "text");

		Assert.IsNotNull(item);
		Assert.AreEqual("call(${name}, ${0:done})", item.InsertText);
		Assert.AreEqual(CompletionInsertTextFormat.Snippet, item.InsertTextFormat);
	}

	[TestMethod]
	public void ParseItem_AbsentOrUnknownInsertTextFormat_ReadsAsPlainText()
	{
		Lsp.CompletionItemPayload plain = DeserializeCompletionItemPayload(new { label = "print", insertText = "print" });

		Lsp.CompletionItemPayload unknown = DeserializeCompletionItemPayload(
			new
			{
				label = "print",
				insertText = "print",
				insertTextFormat = 7,
			}
		);

		CompletionItem? plainItem = CompletionResponseParser.ParseItem(plain, 0, "text");
		CompletionItem? unknownItem = CompletionResponseParser.ParseItem(unknown, 0, "text");

		Assert.IsNotNull(plainItem);
		Assert.IsNotNull(unknownItem);

		// An absent format and a value outside the protocol range both read as plain text.
		Assert.AreEqual(CompletionInsertTextFormat.PlainText, plainItem.InsertTextFormat);
		Assert.AreEqual(CompletionInsertTextFormat.PlainText, unknownItem.InsertTextFormat);
	}

	[TestMethod]
	public void Parse_KeepsServerDuplicatesInResponseOrder()
	{
		IReadOnlyList<CompletionItem> items = CompletionResponseParser.Parse(
			[
				CreateCompletionItem(label: "Value", kind: 6, detail: "variable", documentation: null, insertText: "Value"),
				CreateCompletionItem(label: "value", kind: 6, detail: "variable", documentation: null, insertText: "value"),
				CreateCompletionItem(label: "Value", kind: 6, detail: "variable", documentation: null, insertText: "Value"),
			],

			"text"
		);

		// The parser is a protocol-faithful projection: a repeated entry stays visible instead of
		// being deduplicated, and every item keeps its response position.
		Assert.HasCount(3, items);
		Assert.AreEqual("Value", items[0].Label);
		Assert.AreEqual("value", items[1].Label);
		Assert.AreEqual("Value", items[2].Label);
	}

	[TestMethod]
	public void Parse_PreservesDistinctItemsWithDifferentTextEdits()
	{
		IReadOnlyList<CompletionItem> items = CompletionResponseParser.Parse(
			[
				DeserializeCompletionItemPayload(
					new
					{
						label = "spawn",
						kind = 3,
						insertText = "spawn",

						textEdit = new
						{
							newText = "spawn",
							range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 3 } },
						},
					}
				),

				DeserializeCompletionItemPayload(
					new
					{
						label = "spawn",
						kind = 3,
						insertText = "spawn",

						textEdit = new
						{
							newText = "spawn",
							range = new { start = new { line = 0, character = 1 }, end = new { line = 0, character = 4 } },
						},
					}
				),
			],

			"spawn"
		);

		Assert.HasCount(2, items);
		Assert.AreEqual(0, items[0].TextEdit?.InsertRange.Offset);
		Assert.AreEqual(1, items[1].TextEdit?.InsertRange.Offset);
	}

	[TestMethod]
	public void Parse_KeepsItemsThatDifferOnlyInFilterText()
	{
		IReadOnlyList<CompletionItem> items = CompletionResponseParser.Parse(
			[
				DeserializeCompletionItemPayload(
					new
					{
						label = "spawn",
						kind = 3,
						insertText = "spawn",
						filterText = "spawn",
					}
				),

				DeserializeCompletionItemPayload(
					new
					{
						label = "spawn",
						kind = 3,
						insertText = "spawn",
						filterText = "spawn_object",
					}
				),
			],

			"text"
		);

		// Both suggestions carry their own filter text and both stay visible, in response order.
		Assert.HasCount(2, items);
		Assert.AreEqual("spawn", items[0].FilterText);
		Assert.AreEqual("spawn_object", items[1].FilterText);
	}

	[TestMethod]
	public void Parse_KeepsIdenticalPayloadsAsDistinctEntries()
	{
		IReadOnlyList<CompletionItem> items = CompletionResponseParser.Parse(
			[
				DeserializeCompletionItemPayload(
					new
					{
						label = "spawn",
						kind = 3,
						insertText = "spawn($1)",
						insertTextFormat = 1,
					}
				),

				DeserializeCompletionItemPayload(
					new
					{
						label = "spawn",
						kind = 3,
						insertText = "spawn($1)",
						insertTextFormat = 2,
						preselect = true,
						sortText = "0002",
					}
				),
			],

			"text"
		);

		// Each payload keeps its own flags: the parser keeps duplicate payloads as separate entries, so
		// the plain-text variant and the snippet variant stay two entries exactly as the server sent them.
		Assert.HasCount(2, items);
		Assert.AreEqual(CompletionInsertTextFormat.PlainText, items[0].InsertTextFormat);
		Assert.AreEqual(CompletionInsertTextFormat.Snippet, items[1].InsertTextFormat);
		Assert.IsFalse(items[0].IsPreselected);
		Assert.IsTrue(items[1].IsPreselected);
		Assert.AreEqual("0002", items[1].SortText);
	}

	[TestMethod]
	public void ParseItem_BareStringDocumentation_IsPlainTextAndTrimmed()
	{
		Lsp.CompletionItemPayload itemElement = DeserializeCompletionItemPayload(
			new
			{
				label = "spawn",
				kind = 3,
				documentation = "  Spawns **an** object.\r\n",
			}
		);

		CompletionItem? item = CompletionResponseParser.ParseItem(itemElement, 0, "text");

		Assert.IsNotNull(item);
		Assert.AreEqual("Spawns **an** object.", item.Documentation?.Value);
		Assert.AreEqual(MarkupKind.PlainText, item.Documentation?.Kind);
	}

	[TestMethod]
	public void ParseItem_PreservesMarkdownIndentedCodeBlockDocumentation()
	{
		Lsp.CompletionItemPayload itemElement = DeserializeCompletionItemPayload(
			new
			{
				label = "spawn",
				kind = 3,
				documentation = new { kind = "markdown", value = "    local value = 1" },
			}
		);

		CompletionItem? item = CompletionResponseParser.ParseItem(itemElement, 0, "text");

		Assert.IsNotNull(item);
		Assert.AreEqual("    local value = 1", item.Documentation?.Value);
		Assert.AreEqual(MarkupKind.Markdown, item.Documentation?.Kind);
	}
}
