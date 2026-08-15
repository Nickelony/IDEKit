using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// Covers the tolerant document-symbol response converter: both protocol response shapes, malformed
/// entries, range degradation, and round-trip serialization.
/// </summary>
[TestClass]
public sealed class DocumentSymbolsResponseTests
{
	[TestMethod]
	public void Deserialize_HierarchicalResponse_MapsSymbolsAndChildren()
	{
		DocumentSymbolsResponse response = DeserializeRequired(
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
			""");

		Assert.AreEqual(2, response.Symbols.Count);

		DocumentSymbolPayload spawn = response.Symbols[0];

		Assert.AreEqual("spawn", spawn.Name);
		Assert.AreEqual("function", spawn.Detail);
		Assert.AreEqual(SymbolKind.Function, spawn.Kind);
		Assert.AreEqual(Range(1, 0, 3, 3), spawn.Range);
		Assert.AreEqual(Range(1, 9, 1, 14), spawn.SelectionRange);
		Assert.IsNull(spawn.Location);

		Assert.IsNotNull(spawn.Children);
		Assert.AreEqual(1, spawn.Children.Count);
		Assert.AreEqual("room", spawn.Children[0].Name);
		Assert.AreEqual(SymbolKind.Variable, spawn.Children[0].Kind);
		Assert.AreEqual(Range(2, 4, 2, 12), spawn.Children[0].Range);
		Assert.IsNull(spawn.Children[0].Children);

		DocumentSymbolPayload value = response.Symbols[1];

		Assert.AreEqual("value", value.Name);
		Assert.IsNull(value.Detail);
		Assert.AreEqual(Range(5, 0, 5, 12), value.Range);
		Assert.AreEqual(Range(5, 6, 5, 11), value.SelectionRange);
	}

	[TestMethod]
	public void Deserialize_FlatResponse_MapsLocationAndContainerName()
	{
		DocumentSymbolsResponse response = DeserializeRequired(
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
			""");

		Assert.AreEqual(1, response.Symbols.Count);

		DocumentSymbolPayload symbol = response.Symbols[0];

		Assert.AreEqual("spawn", symbol.Name);
		Assert.AreEqual(SymbolKind.Function, symbol.Kind);
		Assert.AreEqual("engine", symbol.ContainerName);
		Assert.IsNull(symbol.Range);
		Assert.IsNull(symbol.SelectionRange);
		Assert.IsNull(symbol.Children);

		Assert.IsNotNull(symbol.Location);
		Assert.AreEqual("file:///C:/Workspace/Scripts/test.lua", symbol.Location!.Uri);
		Assert.AreEqual(Range(1, 0, 3, 3), symbol.Location!.Range);
	}

	[TestMethod]
	public void Deserialize_MixedShapes_PreservesResponseOrder()
	{
		DocumentSymbolsResponse response = DeserializeRequired(
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
			""");

		Assert.AreEqual(2, response.Symbols.Count);
		Assert.AreEqual("flatFirst", response.Symbols[0].Name);
		Assert.IsNotNull(response.Symbols[0].Location);
		Assert.AreEqual("hierarchical", response.Symbols[1].Name);
		Assert.IsNull(response.Symbols[1].Location);
	}

	[TestMethod]
	public void Deserialize_JsonNull_ReturnsNullResponse()
		=> Assert.IsNull(JsonSerializer.Deserialize<DocumentSymbolsResponse>("null"));

	[TestMethod]
	public void Deserialize_NonArrayPayload_ReturnsEmptyResponse()
	{
		DocumentSymbolsResponse response = DeserializeRequired("""{ "symbols": [] }""");

		Assert.AreEqual(0, response.Symbols.Count);
	}

	[TestMethod]
	public void Deserialize_MalformedEntries_AreSkipped()
	{
		DocumentSymbolsResponse response = DeserializeRequired(
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
			""");

		Assert.AreEqual(1, response.Symbols.Count);
		Assert.AreEqual("usable", response.Symbols[0].Name);
	}

	[TestMethod]
	public void Deserialize_MalformedChildList_IsTreatedAsNoChildren()
	{
		DocumentSymbolsResponse response = DeserializeRequired(
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
			""");

		Assert.AreEqual(2, response.Symbols.Count);
		Assert.IsNull(response.Symbols[0].Children);

		Assert.IsNotNull(response.Symbols[1].Children);

		IReadOnlyList<DocumentSymbolPayload> children = response.Symbols[1].Children!;

		Assert.AreEqual(1, children.Count);
		Assert.AreEqual("usableChild", children[0].Name);
	}

	[TestMethod]
	public void Deserialize_RangeWithMissingEnd_DegradesToEmptyRangeAtStart()
	{
		DocumentSymbolsResponse response = DeserializeRequired(
			"""
			[
			  {
			    "name": "value",
			    "kind": 13,
			    "range": { "start": { "line": 2, "character": 6 } }
			  }
			]
			""");

		Assert.AreEqual(1, response.Symbols.Count);
		Assert.AreEqual(Range(2, 6, 2, 6), response.Symbols[0].Range);
	}

	[TestMethod]
	public void Deserialize_NegativeCoordinates_RemainRepresentable()
	{
		DocumentSymbolsResponse response = DeserializeRequired(
			"""
			[
			  {
			    "name": "value",
			    "kind": 13,
			    "range": { "start": { "line": -1, "character": -2 }, "end": { "line": 0, "character": 4 } },
			    "selectionRange": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 4 } }
			  }
			]
			""");

		Assert.AreEqual(1, response.Symbols.Count);
		Assert.AreEqual(Range(-1, -2, 0, 4), response.Symbols[0].Range);
	}

	[TestMethod]
	public void Serialize_RoundTripsHierarchicalAndFlatEntries()
	{
		var response = new DocumentSymbolsResponse(
		[
			new DocumentSymbolPayload
			{
				Name = "spawn",
				Detail = "function",
				Kind = SymbolKind.Function,
				Range = Range(1, 0, 3, 3),
				SelectionRange = Range(1, 9, 1, 14),
				Children =
				[
					new DocumentSymbolPayload
					{
						Name = "room",
						Kind = SymbolKind.Variable,
						Range = Range(2, 4, 2, 12),
						SelectionRange = Range(2, 4, 2, 8)
					}
				]
			},
			new DocumentSymbolPayload
			{
				Name = "flatEntry",
				Kind = SymbolKind.Variable,
				ContainerName = "engine",
				Location = new SymbolLocationPayload("file:///C:/Workspace/Scripts/test.lua", Range(5, 0, 5, 12))
			}
		]);

		DocumentSymbolsResponse? roundTripped = JsonSerializer.Deserialize<DocumentSymbolsResponse>(
			JsonSerializer.Serialize(response));

		Assert.IsNotNull(roundTripped);
		Assert.AreEqual(2, roundTripped.Symbols.Count);

		DocumentSymbolPayload hierarchical = roundTripped.Symbols[0];

		Assert.AreEqual("spawn", hierarchical.Name);
		Assert.AreEqual("function", hierarchical.Detail);
		Assert.AreEqual(SymbolKind.Function, hierarchical.Kind);
		Assert.AreEqual(Range(1, 0, 3, 3), hierarchical.Range);
		Assert.AreEqual(Range(1, 9, 1, 14), hierarchical.SelectionRange);
		Assert.IsNotNull(hierarchical.Children);
		Assert.AreEqual(1, hierarchical.Children.Count);
		Assert.AreEqual("room", hierarchical.Children[0].Name);
		Assert.AreEqual(Range(2, 4, 2, 12), hierarchical.Children[0].Range);
		Assert.IsNull(hierarchical.Location);

		DocumentSymbolPayload flat = roundTripped.Symbols[1];

		Assert.AreEqual("flatEntry", flat.Name);
		Assert.AreEqual("engine", flat.ContainerName);
		Assert.IsNull(flat.Range);
		Assert.IsNull(flat.SelectionRange);
		Assert.IsNotNull(flat.Location);
		Assert.AreEqual("file:///C:/Workspace/Scripts/test.lua", flat.Location!.Uri);
		Assert.AreEqual(Range(5, 0, 5, 12), flat.Location!.Range);
	}

	[TestMethod]
	public void Deserialize_TagsAndDeprecated_AreParsed()
	{
		DocumentSymbolsResponse response = DeserializeRequired(
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
			""");

		Assert.AreEqual(1, response.Symbols.Count);

		DocumentSymbolPayload symbol = response.Symbols[0];

		Assert.IsNotNull(symbol.Tags);
		Assert.AreEqual(2, symbol.Tags!.Count);
		Assert.AreEqual(SymbolTag.Deprecated, symbol.Tags[0]);
		Assert.AreEqual((SymbolTag)99, symbol.Tags[1]);
		Assert.IsTrue(symbol.Deprecated);
	}

	[TestMethod]
	public void Deserialize_AttachedEmptyCollections_StayEmptyLists()
	{
		DocumentSymbolsResponse response = DeserializeRequired(
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
			""");

		DocumentSymbolPayload symbol = response.Symbols[0];

		Assert.IsNotNull(symbol.Tags);
		Assert.AreEqual(0, symbol.Tags!.Count);
		Assert.IsNotNull(symbol.Children);
		Assert.AreEqual(0, symbol.Children!.Count);
	}

	[TestMethod]
	public void Serialize_AttachedEmptyCollections_WriteEmptyArrays()
	{
		var response = new DocumentSymbolsResponse(
		[
			new DocumentSymbolPayload
			{
				Name = "leaf",
				Kind = SymbolKind.Variable,
				Range = Range(0, 0, 0, 4),
				Tags = Array.AsReadOnly(Array.Empty<SymbolTag>()),
				Children = []
			}
		]);

		string json = JsonSerializer.Serialize(response);

		StringAssert.Contains(json, "\"tags\":[]");
		StringAssert.Contains(json, "\"children\":[]");
	}

	private static DocumentSymbolsResponse? Deserialize(string json)
		=> JsonSerializer.Deserialize<DocumentSymbolsResponse>(json);

	private static DocumentSymbolsResponse DeserializeRequired(string json)
		=> Deserialize(json) ?? throw new InvalidOperationException("Failed to deserialize the document-symbols test payload.");

	private static ProtocolRangePayload Range(int startLine, int startCharacter, int endLine, int endCharacter)
		=> new(new ProtocolPosition(startLine, startCharacter), new ProtocolPosition(endLine, endCharacter));
}
