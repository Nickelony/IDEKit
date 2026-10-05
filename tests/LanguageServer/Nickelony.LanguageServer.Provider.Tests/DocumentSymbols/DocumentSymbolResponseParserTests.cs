using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.LanguageServer.Client.Protocol;
using static Nickelony.LanguageServer.Provider.Tests.PayloadTestHelpers;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class DocumentSymbolResponseParserTests
{
	[TestMethod]
	public void Parse_MapsHierarchicalSymbolsToOffsetRangesAndKinds()
	{
		const string content = "local value = 1\nfunction spawn(room)\nend\n";
		string filePath = TestPaths.Script("test.lua");

		IReadOnlyList<DocumentSymbol> symbols = DocumentSymbolResponseParser.Parse(
			response: DeserializeDocumentSymbolResponse(
				new object[]
				{
					new
					{
						name = "spawn",
						detail = "function",
						kind = 12,
						range = Range(startLine: 1, startCharacter: 0, endLine: 2, endCharacter: 3),
						selectionRange = Range(startLine: 1, startCharacter: 9, endLine: 1, endCharacter: 14),

						children = new object[]
						{
							new
							{
								name = "room",
								kind = 13,
								range = Range(startLine: 1, startCharacter: 14, endLine: 1, endCharacter: 18),
								selectionRange = Range(startLine: 1, startCharacter: 14, endLine: 1, endCharacter: 18),
							},
						},
					},

					new
					{
						name = "value",
						kind = 13,
						range = Range(startLine: 0, startCharacter: 6, endLine: 0, endCharacter: 11),
						selectionRange = Range(startLine: 0, startCharacter: 6, endLine: 0, endCharacter: 11),
					},
				}
			),

			lineMap: TextLineMap.Build(content),
			normalizedFilePath: filePath,
			pathComparison: TestPaths.PathComparison
		);

		Assert.HasCount(2, symbols);

		DocumentSymbol spawn = symbols[0];

		Assert.AreEqual("spawn", spawn.Name);
		Assert.AreEqual("function", spawn.Detail);
		Assert.AreEqual(DocumentSymbolKind.Function, spawn.Kind);
		Assert.AreEqual(new TextRange(16, 24), spawn.Range);
		Assert.AreEqual(new TextRange(25, 5), spawn.SelectionRange);
		Assert.HasCount(1, spawn.Children);
		Assert.AreEqual("room", spawn.Children[0].Name);
		Assert.AreEqual(DocumentSymbolKind.Variable, spawn.Children[0].Kind);
		Assert.AreEqual(new TextRange(30, 4), spawn.Children[0].Range);

		DocumentSymbol value = symbols[1];

		Assert.AreEqual("value", value.Name);
		Assert.AreEqual(DocumentSymbolKind.Variable, value.Kind);
		Assert.AreEqual(new TextRange(6, 5), value.Range);
		Assert.IsEmpty(value.Children);
	}

	[TestMethod]
	public void Parse_UnknownProtocolKinds_FallBackToTheFirstKind()
	{
		const string content = "local value = 1";

		IReadOnlyList<DocumentSymbol> symbols = DocumentSymbolResponseParser.Parse(
			response: DeserializeDocumentSymbolResponse(
				new object[]
				{
					new
					{
						name = "future",
						kind = 27,
						range = Range(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 5),
						selectionRange = Range(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 5),
					},

					new
					{
						name = "zero",
						kind = 0,
						range = Range(startLine: 0, startCharacter: 6, endLine: 0, endCharacter: 11),
						selectionRange = Range(startLine: 0, startCharacter: 6, endLine: 0, endCharacter: 11),
					},
				}
			),

			lineMap: TextLineMap.Build(content),
			normalizedFilePath: TestPaths.Script("test.lua"),
			pathComparison: TestPaths.PathComparison
		);

		// The shared symbol-kind mapping falls back to the protocol's first kind for out-of-range
		// values; the completion-kind bridge uses a presentation fallback (Generic) instead.
		Assert.HasCount(2, symbols);
		Assert.AreEqual(DocumentSymbolKind.File, symbols[0].Kind);
		Assert.AreEqual(DocumentSymbolKind.File, symbols[1].Kind);
	}

	[TestMethod]
	public void Parse_FlatEntries_KeepOnlySymbolsFromTheRequestedDocument()
	{
		const string content = "local value = 1";
		string filePath = TestPaths.Script("test.lua");

		IReadOnlyList<DocumentSymbol> symbols = DocumentSymbolResponseParser.Parse(
			response: DeserializeDocumentSymbolResponse(
				new object[]
				{
					new
					{
						name = "value",
						kind = 13,
						containerName = "engine",

						location = new
						{
							uri = Nickelony.LanguageServer.Client.DocumentUriConversion.CreateFileUri(filePath),
							range = Range(startLine: 0, startCharacter: 6, endLine: 0, endCharacter: 11),
						},
					},

					new
					{
						name = "foreign",
						kind = 12,

						location = new
						{
							uri = Nickelony.LanguageServer.Client.DocumentUriConversion.CreateFileUri(TestPaths.Script("other.lua")),
							range = Range(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 5),
						},
					},

					new
					{
						name = "unusable",
						kind = 12,
						location = new { uri = "not a file uri", range = Range(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 5) },
					},
				}
			),

			lineMap: TextLineMap.Build(content),
			normalizedFilePath: filePath,
			pathComparison: TestPaths.PathComparison
		);

		// Flat SymbolInformation entries describe one location each; only entries for the requested
		// document belong in its outline, and the container name becomes the detail line.
		Assert.HasCount(1, symbols);
		Assert.AreEqual("value", symbols[0].Name);
		Assert.AreEqual("engine", symbols[0].Detail);
		Assert.AreEqual(new TextRange(6, 5), symbols[0].Range);
		Assert.IsNull(symbols[0].SelectionRange);
		Assert.IsEmpty(symbols[0].Children);
	}

	[TestMethod]
	[DataRow(false, 0)]
	[DataRow(true, 1)]
	public void Parse_FlatEntryForACaseVariantPath_FollowsThePathComparison(bool ignoreCase, int expectedCount)
	{
		IReadOnlyList<DocumentSymbol> symbols = DocumentSymbolResponseParser.Parse(
			response: DeserializeDocumentSymbolResponse(
				new object[]
				{
					new
					{
						name = "value",
						kind = 13,

						location = new
						{
							uri = Nickelony.LanguageServer.Client.DocumentUriConversion.CreateFileUri(TestPaths.Script("CASE.lua")),
							range = Range(startLine: 0, startCharacter: 6, endLine: 0, endCharacter: 11),
						},
					},
				}
			),

			lineMap: TextLineMap.Build("local value = 1"),
			normalizedFilePath: TestPaths.Script("case.lua"),
			pathComparison: ignoreCase ? LocalPathComparisonPolicy.CaseInsensitive : LocalPathComparisonPolicy.CaseSensitive
		);

		Assert.HasCount(expectedCount, symbols);
	}

	[TestMethod]
	public void Parse_UnusableEntries_AreSkippedWithoutFailingTheResponse()
	{
		const string content = "local value = 1\nfunction spawn()\nend\n";

		var response = new DocumentSymbolResponse([
			// A hand-built payload without a name cannot produce an outline entry.
			new DocumentSymbolPayload
			{
				Kind = SymbolKind.Function,
				Range = ProtocolRange(startLine: 1, startCharacter: 0, endLine: 2, endCharacter: 3),
			},

			// Missing ranges keep the entry usable by name; the ranges stay null.
			new DocumentSymbolPayload { Name = "noRange", Kind = SymbolKind.Function },

			// A reversed protocol range cannot be represented as an offset range.
			new DocumentSymbolPayload
			{
				Name = "reversed",
				Kind = SymbolKind.Function,
				Range = ProtocolRange(startLine: 2, startCharacter: 3, endLine: 1, endCharacter: 0),
				SelectionRange = ProtocolRange(startLine: 2, startCharacter: 0, endLine: 2, endCharacter: 3),
			},

			// A hand-built payload without a kind falls back to the protocol's first kind.
			new DocumentSymbolPayload
			{
				Name = "noKind",
				Range = ProtocolRange(startLine: 0, startCharacter: 6, endLine: 0, endCharacter: 11),
				SelectionRange = ProtocolRange(startLine: 0, startCharacter: 6, endLine: 0, endCharacter: 11),
			},

			new DocumentSymbolPayload
			{
				Name = "spawn",
				Kind = SymbolKind.Function,
				Range = ProtocolRange(startLine: 1, startCharacter: 0, endLine: 2, endCharacter: 3),
				SelectionRange = ProtocolRange(startLine: 1, startCharacter: 9, endLine: 1, endCharacter: 14),

				Children =
				[
					new DocumentSymbolPayload
					{
						Kind = SymbolKind.Variable,
						Range = ProtocolRange(startLine: 1, startCharacter: 0, endLine: 1, endCharacter: 4),
					},

					new DocumentSymbolPayload
					{
						Name = "usableChild",
						Kind = SymbolKind.Variable,
						SelectionRange = ProtocolRange(startLine: 1, startCharacter: 9, endLine: 1, endCharacter: 14),
					},
				],
			},
		]);

		IReadOnlyList<DocumentSymbol> symbols = DocumentSymbolResponseParser.Parse(
			response,
			lineMap: TextLineMap.Build(content),
			normalizedFilePath: TestPaths.Script("test.lua"),
			pathComparison: TestPaths.PathComparison
		);

		Assert.HasCount(4, symbols);

		DocumentSymbol noRange = symbols[0];

		Assert.AreEqual("noRange", noRange.Name);
		Assert.IsNull(noRange.Range);
		Assert.IsNull(noRange.SelectionRange);

		DocumentSymbol reversed = symbols[1];

		Assert.AreEqual("reversed", reversed.Name);

		// The unusable reversed range is dropped, and the model's documented fallback fills Range
		// from the selection range.
		Assert.AreEqual(new TextRange(33, 3), reversed.Range);
		Assert.AreEqual(new TextRange(33, 3), reversed.SelectionRange);

		DocumentSymbol noKind = symbols[2];

		Assert.AreEqual(DocumentSymbolKind.File, noKind.Kind);

		DocumentSymbol spawn = symbols[3];

		Assert.HasCount(1, spawn.Children);
		Assert.AreEqual("usableChild", spawn.Children[0].Name);
	}

	[TestMethod]
	public void Parse_StaleCoordinates_AreClamped()
	{
		const string content = "local value = 1";

		IReadOnlyList<DocumentSymbol> symbols = DocumentSymbolResponseParser.Parse(
			response: DeserializeDocumentSymbolResponse(
				new object[]
				{
					new
					{
						name = "stale",
						kind = 13,
						range = Range(startLine: 0, startCharacter: 20, endLine: 0, endCharacter: 40),
						selectionRange = Range(startLine: 0, startCharacter: 6, endLine: 0, endCharacter: 99),
					},
				}
			),

			lineMap: TextLineMap.Build(content),
			normalizedFilePath: TestPaths.Script("test.lua"),
			pathComparison: TestPaths.PathComparison
		);

		// Character coordinates beyond the line length clamp to the line end, so a stale range cannot
		// fail the response.
		Assert.HasCount(1, symbols);
		Assert.AreEqual(new TextRange(15, 0), symbols[0].Range);
		Assert.AreEqual(new TextRange(6, 9), symbols[0].SelectionRange);
	}

	[TestMethod]
	public void Parse_NegativeCoordinates_KeepTheEntryWithoutARange()
	{
		IReadOnlyList<DocumentSymbol> symbols = DocumentSymbolResponseParser.Parse(
			response: DeserializeDocumentSymbolResponse(
				new object[]
				{
					new
					{
						name = "negative",
						kind = 13,
						range = Range(startLine: -1, startCharacter: 0, endLine: 0, endCharacter: 5),
						selectionRange = Range(startLine: -1, startCharacter: 0, endLine: 0, endCharacter: 5),
					},
				}
			),

			lineMap: TextLineMap.Build("local value = 1"),
			normalizedFilePath: TestPaths.Script("test.lua"),
			pathComparison: TestPaths.PathComparison
		);

		// Negative coordinates are rejected per range (like the hover and workspace-edit parsers),
		// so the entry stays visible without a range instead of pointing at the file start.
		Assert.HasCount(1, symbols);
		Assert.AreEqual("negative", symbols[0].Name);
		Assert.IsNull(symbols[0].Range);
		Assert.IsNull(symbols[0].SelectionRange);
	}

	[TestMethod]
	public void Parse_NullOrEmptyResponse_ReturnsEmptyList()
	{
		Assert.IsEmpty(
			DocumentSymbolResponseParser.Parse(
				response: DeserializeDocumentSymbolResponse(Array.Empty<object>()),
				lineMap: TextLineMap.Build("local value = 1"),
				normalizedFilePath: TestPaths.Script("test.lua"),
				pathComparison: TestPaths.PathComparison
			)
		);

		Assert.IsEmpty(
			DocumentSymbolResponseParser.Parse(
				response: null,
				lineMap: TextLineMap.Build("local value = 1"),
				normalizedFilePath: TestPaths.Script("test.lua"),
				pathComparison: TestPaths.PathComparison
			)
		);
	}

	private static object Range(int startLine, int startCharacter, int endLine, int endCharacter) =>
		new { start = new { line = startLine, character = startCharacter }, end = new { line = endLine, character = endCharacter } };

	private static RangePayload ProtocolRange(int startLine, int startCharacter, int endLine, int endCharacter) =>
		new(new PositionPayload(startLine, startCharacter), new PositionPayload(endLine, endCharacter));
}
