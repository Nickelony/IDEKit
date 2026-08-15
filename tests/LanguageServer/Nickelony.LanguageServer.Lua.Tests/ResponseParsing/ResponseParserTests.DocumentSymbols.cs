using Nickelony.IDEKit.IntelliSense.DocumentSymbols;

namespace Nickelony.LanguageServer.Lua.Tests;

public sealed partial class ResponseParserTests
{
	[TestMethod]
	public void ParseDocumentSymbols_MapsHierarchicalSymbolsToOffsetRangesAndKinds()
	{
		const string content = "local value = 1\nfunction spawn(room)\nend\n";
		string filePath = TestPaths.Script("test.lua");

		IReadOnlyList<TextDocumentSymbol> symbols = ResponseParser.ParseDocumentSymbols(
			DeserializeDocumentSymbolsResponse(new object[]
			{
				new
				{
					name = "spawn",
					detail = "function",
					kind = 12,
					range = Range(1, 0, 2, 3),
					selectionRange = Range(1, 9, 1, 14),
					children = new object[]
					{
						new
						{
							name = "room",
							kind = 13,
							range = Range(1, 14, 1, 18),
							selectionRange = Range(1, 14, 1, 18)
						}
					}
				},
				new
				{
					name = "value",
					kind = 13,
					range = Range(0, 6, 0, 11),
					selectionRange = Range(0, 6, 0, 11)
				}
			}),
			content,
			filePath);

		Assert.AreEqual(2, symbols.Count);

		TextDocumentSymbol spawn = symbols[0];

		Assert.AreEqual("spawn", spawn.Name);
		Assert.AreEqual("function", spawn.Detail);
		Assert.AreEqual(TextDocumentSymbolKind.Function, spawn.Kind);
		Assert.AreEqual(new TextRange(16, 24), spawn.Range);
		Assert.AreEqual(new TextRange(25, 5), spawn.SelectionRange);
		Assert.AreEqual(1, spawn.Children.Count);
		Assert.AreEqual("room", spawn.Children[0].Name);
		Assert.AreEqual(TextDocumentSymbolKind.Variable, spawn.Children[0].Kind);
		Assert.AreEqual(new TextRange(30, 4), spawn.Children[0].Range);

		TextDocumentSymbol value = symbols[1];

		Assert.AreEqual("value", value.Name);
		Assert.AreEqual(TextDocumentSymbolKind.Variable, value.Kind);
		Assert.AreEqual(new TextRange(6, 5), value.Range);
		Assert.AreEqual(0, value.Children.Count);
	}

	[TestMethod]
	public void ParseDocumentSymbols_UnknownProtocolKinds_FallBackToTheFirstKind()
	{
		const string content = "local value = 1";

		IReadOnlyList<TextDocumentSymbol> symbols = ResponseParser.ParseDocumentSymbols(
			DeserializeDocumentSymbolsResponse(new object[]
			{
				new { name = "future", kind = 27, range = Range(0, 0, 0, 5), selectionRange = Range(0, 0, 0, 5) },
				new { name = "zero", kind = 0, range = Range(0, 6, 0, 11), selectionRange = Range(0, 6, 0, 11) }
			}),
			content,
			TestPaths.Script("test.lua"));

		// The shared symbol-kind mapping falls back to the protocol's first kind for out-of-range
		// values; the completion-kind bridge uses a presentation fallback (Generic) instead.
		Assert.AreEqual(2, symbols.Count);
		Assert.AreEqual(TextDocumentSymbolKind.File, symbols[0].Kind);
		Assert.AreEqual(TextDocumentSymbolKind.File, symbols[1].Kind);
	}

	[TestMethod]
	public void ParseDocumentSymbols_FlatEntries_KeepOnlySymbolsFromTheRequestedDocument()
	{
		const string content = "local value = 1";
		string filePath = TestPaths.Script("test.lua");

		IReadOnlyList<TextDocumentSymbol> symbols = ResponseParser.ParseDocumentSymbols(
			DeserializeDocumentSymbolsResponse(new object[]
			{
				new
				{
					name = "value",
					kind = 13,
					containerName = "engine",
					location = new
					{
						uri = Nickelony.LanguageServer.Client.LanguageServerPaths.CreateFileUri(filePath),
						range = Range(0, 6, 0, 11)
					}
				},
				new
				{
					name = "foreign",
					kind = 12,
					location = new
					{
						uri = Nickelony.LanguageServer.Client.LanguageServerPaths.CreateFileUri(TestPaths.Script("other.lua")),
						range = Range(0, 0, 0, 5)
					}
				},
				new
				{
					name = "unusable",
					kind = 12,
					location = new
					{
						uri = "not a file uri",
						range = Range(0, 0, 0, 5)
					}
				}
			}),
			content,
			filePath);

		// Flat SymbolInformation entries describe one location each; only entries for the requested
		// document belong in its outline, and the container name becomes the detail line.
		Assert.AreEqual(1, symbols.Count);
		Assert.AreEqual("value", symbols[0].Name);
		Assert.AreEqual("engine", symbols[0].Detail);
		Assert.AreEqual(new TextRange(6, 5), symbols[0].Range);
		Assert.IsNull(symbols[0].SelectionRange);
		Assert.AreEqual(0, symbols[0].Children.Count);
	}

	[TestMethod]
	public void ParseDocumentSymbols_UnusableEntries_AreSkippedWithoutFailingTheResponse()
	{
		const string content = "local value = 1\nfunction spawn()\nend\n";

		var response = new DocumentSymbolsResponse(
		[
			// A hand-built payload without a name cannot produce an outline entry.
			new DocumentSymbolPayload { Kind = SymbolKind.Function, Range = ProtocolRange(1, 0, 2, 3) },
			// Missing ranges keep the entry usable by name; the ranges stay null.
			new DocumentSymbolPayload { Name = "noRange", Kind = SymbolKind.Function },
			// A reversed protocol range cannot be represented as an offset range.
			new DocumentSymbolPayload
			{
				Name = "reversed",
				Kind = SymbolKind.Function,
				Range = ProtocolRange(2, 3, 1, 0),
				SelectionRange = ProtocolRange(2, 0, 2, 3)
			},
			// A hand-built payload without a kind falls back to the protocol's first kind.
			new DocumentSymbolPayload
			{
				Name = "noKind",
				Range = ProtocolRange(0, 6, 0, 11),
				SelectionRange = ProtocolRange(0, 6, 0, 11)
			},
			new DocumentSymbolPayload
			{
				Name = "spawn",
				Kind = SymbolKind.Function,
				Range = ProtocolRange(1, 0, 2, 3),
				SelectionRange = ProtocolRange(1, 9, 1, 14),
				Children =
				[
					new DocumentSymbolPayload { Kind = SymbolKind.Variable, Range = ProtocolRange(1, 0, 1, 4) },
					new DocumentSymbolPayload { Name = "usableChild", Kind = SymbolKind.Variable, SelectionRange = ProtocolRange(1, 9, 1, 14) }
				]
			}
		]);

		IReadOnlyList<TextDocumentSymbol> symbols = ResponseParser.ParseDocumentSymbols(
			response,
			content,
			TestPaths.Script("test.lua"));

		Assert.AreEqual(4, symbols.Count);

		TextDocumentSymbol noRange = symbols[0];

		Assert.AreEqual("noRange", noRange.Name);
		Assert.IsNull(noRange.Range);
		Assert.IsNull(noRange.SelectionRange);

		TextDocumentSymbol reversed = symbols[1];

		Assert.AreEqual("reversed", reversed.Name);

		// The unusable reversed range is dropped, and the model's documented fallback fills Range
		// from the selection range.
		Assert.AreEqual(new TextRange(33, 3), reversed.Range);
		Assert.AreEqual(new TextRange(33, 3), reversed.SelectionRange);

		TextDocumentSymbol noKind = symbols[2];

		Assert.AreEqual(TextDocumentSymbolKind.File, noKind.Kind);

		TextDocumentSymbol spawn = symbols[3];

		Assert.AreEqual(1, spawn.Children.Count);
		Assert.AreEqual("usableChild", spawn.Children[0].Name);
	}

	[TestMethod]
	public void ParseDocumentSymbols_StaleCoordinates_AreClamped()
	{
		const string content = "local value = 1";

		IReadOnlyList<TextDocumentSymbol> symbols = ResponseParser.ParseDocumentSymbols(
			DeserializeDocumentSymbolsResponse(new object[]
			{
				new
				{
					name = "stale",
					kind = 13,
					range = Range(0, 20, 0, 40),
					selectionRange = Range(0, 6, 0, 99)
				}
			}),
			content,
			TestPaths.Script("test.lua"));

		// Character coordinates beyond the line length clamp to the line end, so a stale range cannot
		// fail the response.
		Assert.AreEqual(1, symbols.Count);
		Assert.AreEqual(new TextRange(15, 0), symbols[0].Range);
		Assert.AreEqual(new TextRange(6, 9), symbols[0].SelectionRange);
	}

	[TestMethod]
	public void ParseDocumentSymbols_NegativeCoordinates_KeepTheEntryWithoutARange()
	{
		IReadOnlyList<TextDocumentSymbol> symbols = ResponseParser.ParseDocumentSymbols(
			DeserializeDocumentSymbolsResponse(new object[]
			{
				new
				{
					name = "negative",
					kind = 13,
					range = Range(-1, 0, 0, 5),
					selectionRange = Range(-1, 0, 0, 5)
				}
			}),
			"local value = 1",
			TestPaths.Script("test.lua"));

		// Negative coordinates are rejected per range (like the hover and workspace-edit parsers),
		// so the entry stays visible without a range instead of pointing at the file start.
		Assert.AreEqual(1, symbols.Count);
		Assert.AreEqual("negative", symbols[0].Name);
		Assert.IsNull(symbols[0].Range);
		Assert.IsNull(symbols[0].SelectionRange);
	}

	[TestMethod]
	public void ParseDocumentSymbols_NullOrEmptyResponse_ReturnsEmptyList()
	{
		Assert.AreEqual(0, ResponseParser.ParseDocumentSymbols(
			DeserializeDocumentSymbolsResponse(new object[] { }),
			"local value = 1",
			TestPaths.Script("test.lua")).Count);

		Assert.AreEqual(0, ResponseParser.ParseDocumentSymbols(
			null,
			"local value = 1",
			TestPaths.Script("test.lua")).Count);
	}

	private static object Range(int startLine, int startCharacter, int endLine, int endCharacter)
		=> new
		{
			start = new { line = startLine, character = startCharacter },
			end = new { line = endLine, character = endCharacter }
		};

	private static Nickelony.LanguageServer.Client.ProtocolRangePayload ProtocolRange(
		int startLine, int startCharacter, int endLine, int endCharacter)
		=> new(
			new Nickelony.LanguageServer.Client.ProtocolPosition(startLine, startCharacter),
			new Nickelony.LanguageServer.Client.ProtocolPosition(endLine, endCharacter));
}
