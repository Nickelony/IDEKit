using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>Covers the tolerant document-symbol response converter across both protocol response shapes and malformed entries.</summary>
[TestClass]
public sealed class DocumentSymbolResponseTests
{
	[TestMethod]
	public void Deserialize_HierarchicalResponse_MapsSymbolsAndChildren()
	{
		DocumentSymbolResponse response = DeserializeRequired(
			"""
			[
			  {
			    "name": "spawn",
			    "detail": "function",
			    "kind": 12,
			    "range": { "start": { "line": 1, "character": 0 }, "end": { "line": 3, "character": 3 } },
			    "selectionRange": { "start": { "line": 1, "character": 9 }, "end": { "line": 1, "character": 14 } },
			    "children": [
			      {
			        "name": "room",
			        "kind": 13,
			        "range": { "start": { "line": 2, "character": 4 }, "end": { "line": 2, "character": 12 } },
			        "selectionRange": { "start": { "line": 2, "character": 4 }, "end": { "line": 2, "character": 8 } }
			      }
			    ]
			  },
			  {
			    "name": "value",
			    "kind": 13,
			    "range": { "start": { "line": 5, "character": 0 }, "end": { "line": 5, "character": 12 } },
			    "selectionRange": { "start": { "line": 5, "character": 6 }, "end": { "line": 5, "character": 11 } }
			  }
			]
			"""
		);

		Assert.HasCount(2, response.Symbols);

		DocumentSymbolPayload spawn = response.Symbols[0];

		Assert.AreEqual("spawn", spawn.Name);
		Assert.AreEqual("function", spawn.Detail);
		Assert.AreEqual(SymbolKind.Function, spawn.Kind);
		Assert.AreEqual(Range(startLine: 1, startCharacter: 0, endLine: 3, endCharacter: 3), spawn.Range);
		Assert.AreEqual(Range(startLine: 1, startCharacter: 9, endLine: 1, endCharacter: 14), spawn.SelectionRange);
		Assert.IsNull(spawn.Location);

		Assert.IsNotNull(spawn.Children);
		Assert.HasCount(1, spawn.Children);
		Assert.AreEqual("room", spawn.Children[0].Name);
		Assert.AreEqual(SymbolKind.Variable, spawn.Children[0].Kind);
		Assert.AreEqual(Range(startLine: 2, startCharacter: 4, endLine: 2, endCharacter: 12), spawn.Children[0].Range);
		Assert.IsNull(spawn.Children[0].Children);

		DocumentSymbolPayload value = response.Symbols[1];

		Assert.AreEqual("value", value.Name);
		Assert.IsNull(value.Detail);
		Assert.AreEqual(Range(startLine: 5, startCharacter: 0, endLine: 5, endCharacter: 12), value.Range);
		Assert.AreEqual(Range(startLine: 5, startCharacter: 6, endLine: 5, endCharacter: 11), value.SelectionRange);
	}

	[TestMethod]
	public void Deserialize_FlatResponse_MapsLocationAndContainerName()
	{
		DocumentSymbolResponse response = DeserializeRequired(
			"""
			[
			  {
			    "name": "spawn",
			    "kind": 12,
			    "containerName": "engine",
			    "location": {
			      "uri": "file:///C:/Workspace/Scripts/test.lua",
			      "range": { "start": { "line": 1, "character": 0 }, "end": { "line": 3, "character": 3 } }
			    }
			  }
			]
			"""
		);

		Assert.HasCount(1, response.Symbols);

		DocumentSymbolPayload symbol = response.Symbols[0];

		Assert.AreEqual("spawn", symbol.Name);
		Assert.AreEqual(SymbolKind.Function, symbol.Kind);
		Assert.AreEqual("engine", symbol.ContainerName);
		Assert.IsNull(symbol.Range);
		Assert.IsNull(symbol.SelectionRange);
		Assert.IsNull(symbol.Children);

		Assert.IsNotNull(symbol.Location);
		Assert.AreEqual("file:///C:/Workspace/Scripts/test.lua", symbol.Location!.Uri);
		Assert.AreEqual(Range(startLine: 1, startCharacter: 0, endLine: 3, endCharacter: 3), symbol.Location!.Range);
	}

	[TestMethod]
	public void Deserialize_MixedShapes_PreservesResponseOrder()
	{
		DocumentSymbolResponse response = DeserializeRequired(
			"""
			[
			  {
			    "name": "flatFirst",
			    "kind": 13,
			    "location": {
			      "uri": "file:///C:/Workspace/Scripts/test.lua",
			      "range": { "start": { "line": 0, "character": 6 }, "end": { "line": 0, "character": 15 } }
			    }
			  },
			  {
			    "name": "hierarchical",
			    "kind": 12,
			    "range": { "start": { "line": 2, "character": 0 }, "end": { "line": 4, "character": 3 } },
			    "selectionRange": { "start": { "line": 2, "character": 9 }, "end": { "line": 2, "character": 21 } }
			  }
			]
			"""
		);

		Assert.HasCount(2, response.Symbols);
		Assert.AreEqual("flatFirst", response.Symbols[0].Name);
		Assert.IsNotNull(response.Symbols[0].Location);
		Assert.AreEqual("hierarchical", response.Symbols[1].Name);
		Assert.IsNull(response.Symbols[1].Location);
	}

	[TestMethod]
	public void Deserialize_JsonNull_ReturnsNullResponse() => Assert.IsNull(JsonSerializer.Deserialize<DocumentSymbolResponse>("null"));

	[TestMethod]
	public void Deserialize_NonArrayPayload_ReturnsEmptyResponse()
	{
		DocumentSymbolResponse response = DeserializeRequired("""{ "symbols": [] }""");
		Assert.IsEmpty(response.Symbols);
	}

	[TestMethod]
	public void Deserialize_MalformedEntries_AreSkipped()
	{
		DocumentSymbolResponse response = DeserializeRequired(
			"""
			[
			  42,
			  "text",
			  null,
			  { "kind": 12, "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 1 } } },
			  { "name": "   ", "kind": 12, "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 1 } } },
			  { "name": 7, "kind": 12, "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 1 } } },
			  { "name": "missingKind", "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 1 } } },
			  { "name": "badKind", "kind": "function", "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 1 } } },
			  { "name": "noRangeOrLocation", "kind": 12 },
			  { "name": "badLocation", "kind": 12, "location": { "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 1 } } } },
			  { "name": "usable", "kind": 12, "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 1 } } }
			]
			"""
		);

		Assert.HasCount(1, response.Symbols);
		Assert.AreEqual("usable", response.Symbols[0].Name);
	}

	[TestMethod]
	public void Deserialize_MalformedChildList_IsTreatedAsNoChildren()
	{
		DocumentSymbolResponse response = DeserializeRequired(
			"""
			[
			  {
			    "name": "root",
			    "kind": 12,
			    "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 4, "character": 3 } },
			    "selectionRange": { "start": { "line": 0, "character": 9 }, "end": { "line": 0, "character": 13 } },
			    "children": 42
			  },
			  {
			    "name": "withChildren",
			    "kind": 12,
			    "range": { "start": { "line": 6, "character": 0 }, "end": { "line": 8, "character": 3 } },
			    "selectionRange": { "start": { "line": 6, "character": 9 }, "end": { "line": 6, "character": 21 } },
			    "children": [
			      { "name": "usableChild", "kind": 13, "range": { "start": { "line": 7, "character": 4 }, "end": { "line": 7, "character": 12 } } },
			      { "name": "", "kind": 13, "range": { "start": { "line": 7, "character": 4 }, "end": { "line": 7, "character": 12 } } }
			    ]
			  }
			]
			"""
		);

		Assert.HasCount(2, response.Symbols);
		Assert.IsNull(response.Symbols[0].Children);

		Assert.IsNotNull(response.Symbols[1].Children);

		IReadOnlyList<DocumentSymbolPayload> children = response.Symbols[1].Children!;

		Assert.HasCount(1, children);
		Assert.AreEqual("usableChild", children[0].Name);
	}

	[TestMethod]
	public void Deserialize_RangeWithMissingEnd_DegradesToEmptyRangeAtStart()
	{
		DocumentSymbolResponse response = DeserializeRequired(
			"""
			[
			  {
			    "name": "value",
			    "kind": 13,
			    "range": { "start": { "line": 2, "character": 6 } }
			  }
			]
			"""
		);

		Assert.HasCount(1, response.Symbols);
		Assert.AreEqual(Range(startLine: 2, startCharacter: 6, endLine: 2, endCharacter: 6), response.Symbols[0].Range);
	}

	[TestMethod]
	public void Deserialize_NegativeCoordinates_RemainRepresentable()
	{
		DocumentSymbolResponse response = DeserializeRequired(
			"""
			[
			  {
			    "name": "value",
			    "kind": 13,
			    "range": { "start": { "line": -1, "character": -2 }, "end": { "line": 0, "character": 4 } },
			    "selectionRange": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 4 } }
			  }
			]
			"""
		);

		Assert.HasCount(1, response.Symbols);
		Assert.AreEqual(Range(startLine: -1, startCharacter: -2, endLine: 0, endCharacter: 4), response.Symbols[0].Range);
	}

	[TestMethod]
	public void Serialize_RoundTripsHierarchicalAndFlatEntries()
	{
		var response = new DocumentSymbolResponse([
			new DocumentSymbolPayload
			{
				Name = "spawn",
				Detail = "function",
				Kind = SymbolKind.Function,
				Range = Range(startLine: 1, startCharacter: 0, endLine: 3, endCharacter: 3),
				SelectionRange = Range(startLine: 1, startCharacter: 9, endLine: 1, endCharacter: 14),

				Children =
				[
					new DocumentSymbolPayload
					{
						Name = "room",
						Kind = SymbolKind.Variable,
						Range = Range(startLine: 2, startCharacter: 4, endLine: 2, endCharacter: 12),
						SelectionRange = Range(startLine: 2, startCharacter: 4, endLine: 2, endCharacter: 8),
					},
				],
			},

			new DocumentSymbolPayload
			{
				Name = "flatEntry",
				Kind = SymbolKind.Variable,
				ContainerName = "engine",

				Location = new SymbolLocationPayload(
					"file:///C:/Workspace/Scripts/test.lua",
					Range(startLine: 5, startCharacter: 0, endLine: 5, endCharacter: 12)
				),
			},
		]);

		DocumentSymbolResponse? roundTripped = JsonSerializer.Deserialize<DocumentSymbolResponse>(JsonSerializer.Serialize(response));

		Assert.IsNotNull(roundTripped);
		Assert.HasCount(2, roundTripped.Symbols);

		DocumentSymbolPayload hierarchical = roundTripped.Symbols[0];

		Assert.AreEqual("spawn", hierarchical.Name);
		Assert.AreEqual("function", hierarchical.Detail);
		Assert.AreEqual(SymbolKind.Function, hierarchical.Kind);
		Assert.AreEqual(Range(startLine: 1, startCharacter: 0, endLine: 3, endCharacter: 3), hierarchical.Range);
		Assert.AreEqual(Range(startLine: 1, startCharacter: 9, endLine: 1, endCharacter: 14), hierarchical.SelectionRange);
		Assert.IsNotNull(hierarchical.Children);
		Assert.HasCount(1, hierarchical.Children);
		Assert.AreEqual("room", hierarchical.Children[0].Name);
		Assert.AreEqual(Range(startLine: 2, startCharacter: 4, endLine: 2, endCharacter: 12), hierarchical.Children[0].Range);
		Assert.IsNull(hierarchical.Location);

		DocumentSymbolPayload flat = roundTripped.Symbols[1];

		Assert.AreEqual("flatEntry", flat.Name);
		Assert.AreEqual("engine", flat.ContainerName);
		Assert.IsNull(flat.Range);
		Assert.IsNull(flat.SelectionRange);
		Assert.IsNotNull(flat.Location);
		Assert.AreEqual("file:///C:/Workspace/Scripts/test.lua", flat.Location!.Uri);
		Assert.AreEqual(Range(startLine: 5, startCharacter: 0, endLine: 5, endCharacter: 12), flat.Location!.Range);
	}

	[TestMethod]
	public void Deserialize_TagsAndDeprecated_AreParsed()
	{
		DocumentSymbolResponse response = DeserializeRequired(
			"""
			[
			  {
			    "name": "legacy",
			    "kind": 13,
			    "tags": [1, 99],
			    "deprecated": true,
			    "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 4 } }
			  }
			]
			"""
		);

		Assert.HasCount(1, response.Symbols);

		DocumentSymbolPayload symbol = response.Symbols[0];

		Assert.IsNotNull(symbol.Tags);
		Assert.HasCount(2, symbol.Tags);
		Assert.AreEqual(SymbolTag.Deprecated, symbol.Tags[0]);
		Assert.AreEqual((SymbolTag)99, symbol.Tags[1]);
		Assert.IsTrue(symbol.Deprecated);
	}

	[TestMethod]
	public void Deserialize_AttachedEmptyCollections_StayEmptyLists()
	{
		DocumentSymbolResponse response = DeserializeRequired(
			"""
			[
			  {
			    "name": "leaf",
			    "kind": 13,
			    "tags": [],
			    "children": [],
			    "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 4 } }
			  }
			]
			"""
		);

		DocumentSymbolPayload symbol = response.Symbols[0];

		Assert.IsNotNull(symbol.Tags);
		Assert.IsEmpty(symbol.Tags);
		Assert.IsNotNull(symbol.Children);
		Assert.IsEmpty(symbol.Children);
	}

	[TestMethod]
	public void Serialize_AttachedEmptyCollections_WriteEmptyArrays()
	{
		var response = new DocumentSymbolResponse([
			new DocumentSymbolPayload
			{
				Name = "leaf",
				Kind = SymbolKind.Variable,
				Range = Range(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 4),
				Tags = Array.AsReadOnly(Array.Empty<SymbolTag>()),
				Children = [],
			},
		]);

		string json = JsonSerializer.Serialize(response);

		Assert.Contains("\"tags\":[]", json);
		Assert.Contains("\"children\":[]", json);
	}

	private static DocumentSymbolResponse? Deserialize(string json) => JsonSerializer.Deserialize<DocumentSymbolResponse>(json);

	private static DocumentSymbolResponse DeserializeRequired(string json) =>
		Deserialize(json) ?? throw new InvalidOperationException("Failed to deserialize the document-symbols test payload.");

	private static RangePayload Range(int startLine, int startCharacter, int endLine, int endCharacter) =>
		new(new PositionPayload(startLine, startCharacter), new PositionPayload(endLine, endCharacter));
}
