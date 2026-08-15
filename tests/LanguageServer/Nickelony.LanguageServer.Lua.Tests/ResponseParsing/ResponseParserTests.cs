using Nickelony.IDEKit.IntelliSense;
using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.LanguageServer.Lua.Tests;

[TestClass]
public sealed partial class ResponseParserTests
{
	[TestMethod]
	public void ParseCompletionItem_DetailProseDoesNotInfluenceKind()
	{
		CompletionItemPayload itemElement = DeserializeCompletionItemPayload(new { label = "arg", detail = "parameter" });

		TextCompletionItem? item = ResponseParser.ParseCompletionItem(itemElement, 0, "text");

		Assert.IsNotNull(item);

		// Kind inference is protocol-only: a payload without a kind keeps the presentation fallback
		// even when its prose mentions a category.
		Assert.AreSame(TextCompletionItemKind.Generic, item.Kind);
	}

	[TestMethod]
	public void ParseCompletionItem_OutOfRangeKind_UsesGenericFallback()
	{
		CompletionItemPayload itemElement = CreateCompletionItem("unknown", kind: 999, detail: null, documentation: null);

		TextCompletionItem? item = ResponseParser.ParseCompletionItem(itemElement, 0, "text");

		Assert.IsNotNull(item);

		// A value outside the protocol range cannot be represented; it maps to the presentation
		// fallback instead of masquerading as the real Text kind.
		Assert.AreSame(TextCompletionItemKind.Generic, item.Kind);
	}

	[TestMethod]
	public void ParseCompletionItem_MissingKind_UsesGenericFallback()
	{
		CompletionItemPayload itemElement = DeserializeCompletionItemPayload(new { label = "item" });

		TextCompletionItem? item = ResponseParser.ParseCompletionItem(itemElement, 0, "text");

		Assert.IsNotNull(item);
		Assert.AreSame(TextCompletionItemKind.Generic, item.Kind);
	}

	[TestMethod]
	public void ParseCompletionItem_MapsProtocolKindsOneToOne()
	{
		// Every protocol kind must survive into the shared taxonomy unchanged. The protocol names are
		// listed explicitly so an added or renamed protocol kind cannot silently collapse onto another
		// member.
		string[] protocolNames =
		[
			"Text", "Method", "Function", "Constructor", "Field", "Variable", "Class", "Interface",
			"Module", "Property", "Unit", "Value", "Enum", "Keyword", "Snippet", "Color", "File",
			"Reference", "Folder", "EnumMember", "Constant", "Struct", "Event", "Operator", "TypeParameter"
		];

		for (int protocolKind = 1; protocolKind <= protocolNames.Length; protocolKind++)
		{
			CompletionItemPayload itemElement = CreateCompletionItem($"item{protocolKind}", protocolKind, detail: null, documentation: null);

			TextCompletionItem? item = ResponseParser.ParseCompletionItem(itemElement, 0, "text");

			Assert.IsNotNull(item);

			TextCompletionItemKind expectedKind = TextCompletionItemKindConversion.FromLspKind(protocolKind);

			Assert.AreEqual(protocolNames[protocolKind - 1], expectedKind.Identifier, $"Protocol kind {protocolKind} must have a library member with the protocol name.");
			Assert.AreSame(expectedKind, item.Kind, $"Protocol kind {protocolKind} must map one-to-one.");
		}
	}

	[TestMethod]
	public void ParseCompletionItem_ParsesTextEditRange()
	{
		CompletionItemPayload itemElement = DeserializeCompletionItemPayload(new
		{
			label = "print",
			kind = 3,
			textEdit = new
			{
				newText = "print",
				range = new
				{
					start = new { line = 1, character = 2 },
					end = new { line = 1, character = 5 }
				}
			}
		});

		TextCompletionItem? item = ResponseParser.ParseCompletionItem(itemElement, 0, "a\nprint");

		Assert.IsNotNull(item);
		Assert.AreEqual("print", item.InsertText);
		Assert.IsNotNull(item.TextEdit);
		Assert.AreEqual(new TextRange(4, 3), item.TextEdit.Value.InsertRange);
		Assert.IsNull(item.TextEdit.Value.ReplaceRange);
		Assert.AreEqual("print", item.TextEdit.Value.NewText);
	}

	[TestMethod]
	public void ParseCompletionItem_RejectsUnusableTextEditRange()
	{
		CompletionItemPayload itemElement = DeserializeCompletionItemPayload(new
		{
			label = "print",
			kind = 3,
			textEdit = new
			{
				newText = "print",
				range = new
				{
					start = new { line = 5, character = 2 },
					end = new { line = 1, character = 5 }
				}
			}
		});

		TextCompletionItem? item = ResponseParser.ParseCompletionItem(itemElement, 0, "a\nprint");

		// A range that cannot be mapped against the snapshot is rejected instead of producing a
		// partial edit; the label remains the commit fallback.
		Assert.IsNotNull(item);
		Assert.IsNull(item.TextEdit);
		Assert.AreEqual("print", item.InsertText);
	}

	[TestMethod]
	public void ParseCompletionItem_TextEditNewTextSupersedesInsertText()
	{
		CompletionItemPayload itemElement = DeserializeCompletionItemPayload(new
		{
			label = "print",
			kind = 3,
			insertText = "fallback",
			textEdit = new
			{
				newText = "replacement",
				range = new
				{
					start = new { line = 0, character = 0 },
					end = new { line = 0, character = 5 }
				}
			}
		});

		TextCompletionItem? item = ResponseParser.ParseCompletionItem(itemElement, 0, "print");

		Assert.IsNotNull(item);

		// The edit carries the commit text; the plain insertion text stays available as the fallback.
		Assert.AreEqual("fallback", item.InsertText);
		Assert.AreEqual("replacement", item.TextEdit?.NewText);
	}

	[TestMethod]
	public void ParseCompletionItem_WhitespaceTextEditNewText_IsPreserved()
	{
		CompletionItemPayload itemElement = DeserializeCompletionItemPayload(new
		{
			label = "print",
			kind = 3,
			textEdit = new
			{
				newText = " ",
				range = new
				{
					start = new { line = 0, character = 0 },
					end = new { line = 0, character = 5 }
				}
			}
		});

		TextCompletionItem? item = ResponseParser.ParseCompletionItem(itemElement, 0, "print");

		Assert.IsNotNull(item);
		Assert.AreEqual(" ", item.TextEdit?.NewText);
	}

	[TestMethod]
	public void ParseCompletionItem_SnippetTextEditNewText_PassesThroughWithSnippetFormat()
	{
		CompletionItemPayload itemElement = DeserializeCompletionItemPayload(new
		{
			label = "spawn",
			kind = 3,
			insertTextFormat = 2,
			textEdit = new
			{
				newText = "spawn($0)",
				range = new
				{
					start = new { line = 0, character = 0 },
					end = new { line = 0, character = 5 }
				}
			}
		});

		TextCompletionItem? item = ResponseParser.ParseCompletionItem(itemElement, 0, "spawn");

		Assert.IsNotNull(item);
		Assert.AreEqual("spawn($0)", item.TextEdit?.NewText);
		Assert.AreEqual(TextCompletionInsertTextFormat.Snippet, item.InsertTextFormat);
	}

	[TestMethod]
	public void ParseCompletionItem_ParsesInsertReplaceEditRanges()
	{
		CompletionItemPayload itemElement = DeserializeCompletionItemPayload(new
		{
			label = "print",
			kind = 3,
			textEdit = new
			{
				newText = "print",
				insert = new
				{
					start = new { line = 0, character = 1 },
					end = new { line = 0, character = 3 }
				},
				replace = new
				{
					start = new { line = 0, character = 1 },
					end = new { line = 0, character = 6 }
				}
			}
		});

		TextCompletionItem? item = ResponseParser.ParseCompletionItem(itemElement, 0, "abcdef");

		Assert.IsNotNull(item);
		Assert.IsNotNull(item.TextEdit);
		Assert.IsNotNull(item.TextEdit.Value.ReplaceRange);

		Assert.AreEqual(new TextRange(1, 2), item.TextEdit.Value.InsertRange);
		Assert.AreEqual(new TextRange(1, 5), item.TextEdit.Value.ReplaceRange.Value);
		Assert.AreEqual(6, item.TextEdit.Value.ReplacementRange.EndOffset);
	}

	[TestMethod]
	public void ParseCompletionItem_InsertReplaceEditWithMismatchedStarts_DegradesToReplaceRange()
	{
		CompletionItemPayload itemElement = DeserializeCompletionItemPayload(new
		{
			label = "print",
			kind = 3,
			textEdit = new
			{
				newText = "print",
				insert = new
				{
					start = new { line = 0, character = 2 },
					end = new { line = 0, character = 3 }
				},
				replace = new
				{
					start = new { line = 0, character = 1 },
					end = new { line = 0, character = 6 }
				}
			}
		});

		TextCompletionItem? item = ResponseParser.ParseCompletionItem(itemElement, 0, "abcdef");

		Assert.IsNotNull(item);
		Assert.IsNotNull(item.TextEdit);
		Assert.IsNull(item.TextEdit.Value.ReplaceRange);
		Assert.AreEqual(new TextRange(1, 5), item.TextEdit.Value.InsertRange);
	}

	[TestMethod]
	public void ParseCompletionItem_SnippetInsertText_PassesThroughWithSnippetFormat()
	{
		CompletionItemPayload itemElement = DeserializeCompletionItemPayload(new
		{
			label = "if",
			kind = 15,
			insertText = "if ${1:condition} then\r\n\t$0\r\nend",
			insertTextFormat = 2
		});

		TextCompletionItem? item = ResponseParser.ParseCompletionItem(itemElement, 0, "text");

		Assert.IsNotNull(item);
		Assert.AreEqual("if ${1:condition} then\r\n\t$0\r\nend", item.InsertText);
		Assert.AreEqual(TextCompletionInsertTextFormat.Snippet, item.InsertTextFormat);
	}

	[TestMethod]
	public void ParseCompletionItem_UnknownSnippetPlaceholders_PassThroughVerbatim()
	{
		CompletionItemPayload itemElement = DeserializeCompletionItemPayload(new
		{
			label = "call",
			kind = 3,
			insertText = "call(${name}, ${0:done})",
			insertTextFormat = 2
		});

		TextCompletionItem? item = ResponseParser.ParseCompletionItem(itemElement, 0, "text");

		Assert.IsNotNull(item);
		Assert.AreEqual("call(${name}, ${0:done})", item.InsertText);
		Assert.AreEqual(TextCompletionInsertTextFormat.Snippet, item.InsertTextFormat);
	}

	[TestMethod]
	public void ParseCompletionItem_AbsentOrUnknownInsertTextFormat_ReadsAsPlainText()
	{
		CompletionItemPayload plain = DeserializeCompletionItemPayload(new { label = "print", insertText = "print" });
		CompletionItemPayload unknown = DeserializeCompletionItemPayload(new { label = "print", insertText = "print", insertTextFormat = 7 });

		TextCompletionItem? plainItem = ResponseParser.ParseCompletionItem(plain, 0, "text");
		TextCompletionItem? unknownItem = ResponseParser.ParseCompletionItem(unknown, 0, "text");

		Assert.IsNotNull(plainItem);
		Assert.IsNotNull(unknownItem);

		// An absent format and a value outside the protocol range both read as plain text.
		Assert.AreEqual(TextCompletionInsertTextFormat.PlainText, plainItem.InsertTextFormat);
		Assert.AreEqual(TextCompletionInsertTextFormat.PlainText, unknownItem.InsertTextFormat);
	}

	[TestMethod]
	public void ParseCompletionItems_KeepsServerDuplicatesInResponseOrder()
	{
		IReadOnlyList<TextCompletionItem> items = ResponseParser.ParseCompletionItems(
			[
				CreateCompletionItem("Value", kind: 6, detail: "variable", documentation: null, insertText: "Value"),
				CreateCompletionItem("value", kind: 6, detail: "variable", documentation: null, insertText: "value"),
				CreateCompletionItem("Value", kind: 6, detail: "variable", documentation: null, insertText: "Value")
			],
			"text");

		// The parser is a protocol-faithful projection: a repeated entry stays visible instead of
		// being deduplicated, and every item keeps its response position.
		Assert.AreEqual(3, items.Count);
		Assert.AreEqual("Value", items[0].Label);
		Assert.AreEqual("value", items[1].Label);
		Assert.AreEqual("Value", items[2].Label);
	}

	[TestMethod]
	public void ParseCompletionItems_PreservesDistinctItemsWithDifferentTextEdits()
	{
		IReadOnlyList<TextCompletionItem> items = ResponseParser.ParseCompletionItems(
			[
				DeserializeCompletionItemPayload(new
				{
					label = "spawn",
					kind = 3,
					insertText = "spawn",
					textEdit = new
					{
						newText = "spawn",
						range = new
						{
							start = new { line = 0, character = 0 },
							end = new { line = 0, character = 3 }
						}
					}
				}),
				DeserializeCompletionItemPayload(new
				{
					label = "spawn",
					kind = 3,
					insertText = "spawn",
					textEdit = new
					{
						newText = "spawn",
						range = new
						{
							start = new { line = 0, character = 1 },
							end = new { line = 0, character = 4 }
						}
					}
				})
			],
			"spawn");

		Assert.AreEqual(2, items.Count);
		Assert.AreEqual(0, items[0].TextEdit?.InsertRange.Offset);
		Assert.AreEqual(1, items[1].TextEdit?.InsertRange.Offset);
	}

	[TestMethod]
	public void ParseCompletionItems_KeepsItemsThatDifferOnlyInFilterText()
	{
		IReadOnlyList<TextCompletionItem> items = ResponseParser.ParseCompletionItems(
			[
				DeserializeCompletionItemPayload(new { label = "spawn", kind = 3, insertText = "spawn", filterText = "spawn" }),
				DeserializeCompletionItemPayload(new { label = "spawn", kind = 3, insertText = "spawn", filterText = "spawn_object" })
			],
			"text");

		// Both suggestions carry their own filter text and both stay visible, in response order.
		Assert.AreEqual(2, items.Count);
		Assert.AreEqual("spawn", items[0].FilterText);
		Assert.AreEqual("spawn_object", items[1].FilterText);
	}

	[TestMethod]
	public void ParseCompletionItems_KeepsIdenticalPayloadsAsDistinctEntries()
	{
		IReadOnlyList<TextCompletionItem> items = ResponseParser.ParseCompletionItems(
			[
				DeserializeCompletionItemPayload(new { label = "spawn", kind = 3, insertText = "spawn($1)", insertTextFormat = 1 }),
				DeserializeCompletionItemPayload(new { label = "spawn", kind = 3, insertText = "spawn($1)", insertTextFormat = 2, preselect = true, sortText = "0002" })
			],
			"text");

		// Each payload keeps its own flags: the parser no longer merges duplicates, so the plain-text
		// variant and the snippet variant stay two entries exactly as the server sent them.
		Assert.AreEqual(2, items.Count);
		Assert.AreEqual(TextCompletionInsertTextFormat.PlainText, items[0].InsertTextFormat);
		Assert.AreEqual(TextCompletionInsertTextFormat.Snippet, items[1].InsertTextFormat);
		Assert.IsFalse(items[0].IsPreselected);
		Assert.IsTrue(items[1].IsPreselected);
		Assert.AreEqual("0002", items[1].SortText);
	}

	[TestMethod]
	public void ParseCompletionItem_PreservesMarkdownIndentedCodeBlockDocumentation()
	{
		CompletionItemPayload itemElement = DeserializeCompletionItemPayload(new
		{
			label = "spawn",
			kind = 3,
			documentation = new
			{
				kind = "markdown",
				value = "    local value = 1"
			}
		});

		TextCompletionItem? item = ResponseParser.ParseCompletionItem(itemElement, 0, "text");

		Assert.IsNotNull(item);
		Assert.AreEqual("    local value = 1", item.Documentation);
		Assert.AreEqual(TextMarkupKind.Markdown, item.DocumentationKind);
	}
}
