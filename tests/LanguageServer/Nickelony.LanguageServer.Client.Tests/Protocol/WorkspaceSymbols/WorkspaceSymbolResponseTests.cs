using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// Covers the tolerant workspace-symbol response converter across both protocol result shapes, malformed entries and the round trip of an entry.
/// </summary>
[TestClass]
public sealed class WorkspaceSymbolResponseTests
{
	[TestMethod]
	public void Deserialize_SymbolInformationShape_MapsNameKindContainerAndLocation()
	{
		WorkspaceSymbolResponse response = WireJsonTestHelpers.Deserialize<WorkspaceSymbolResponse>(
			"""
			[
			  {
			    "name": "spawn",
			    "kind": 12,
			    "containerName": "world",
			    "location": {
			      "uri": "file:///c%3A/ws/world.lua",
			      "range": { "start": { "line": 4, "character": 9 }, "end": { "line": 4, "character": 14 } }
			    }
			  }
			]
			"""
		);

		WorkspaceSymbolPayload symbol = response.Symbols.Single();

		Assert.AreEqual("spawn", symbol.Name);
		Assert.AreEqual(SymbolKind.Function, symbol.Kind);
		Assert.AreEqual("world", symbol.ContainerName);
		Assert.AreEqual("file:///c%3A/ws/world.lua", symbol.Location!.Value.Uri);
		Assert.AreEqual(new RangePayload(new PositionPayload(4, 9), new PositionPayload(4, 14)), symbol.Location.Value.Range);
		Assert.IsNull(symbol.Data);
	}

	[TestMethod]
	public void Deserialize_WorkspaceSymbolShape_KeepsALocationWithoutARangeAndTheOpaqueData()
	{
		WorkspaceSymbolResponse response = WireJsonTestHelpers.Deserialize<WorkspaceSymbolResponse>(
			"""
			[
			  { "name": "Util", "kind": 5, "tags": [1], "location": { "uri": "file:///c%3A/ws/Util.cs" }, "data": { "id": 17, "path": ["a", "b"] } }
			]
			"""
		);

		WorkspaceSymbolPayload symbol = response.Symbols.Single();

		Assert.AreEqual(SymbolKind.Class, symbol.Kind);
		Assert.IsNull(symbol.Location!.Value.Range);
		Assert.AreEqual("file:///c%3A/ws/Util.cs", symbol.Location.Value.Uri);
		Assert.AreSequenceEqual([SymbolTag.Deprecated], [.. symbol.Tags!]);
		Assert.AreEqual(17, symbol.Data!.Value.GetProperty("id").GetInt32());
	}

	[TestMethod]
	public void Deserialize_BothShapesInOneResponse_ReadsEveryEntryInOrder()
	{
		WorkspaceSymbolResponse response = WireJsonTestHelpers.Deserialize<WorkspaceSymbolResponse>(
			"""
			[
			  { "name": "flat", "kind": 13, "location": { "uri": "file:///c%3A/a.lua", "range": { "start": { "line": 0, "character": 0 }, "end": { "line": 0, "character": 4 } } } },
			  { "name": "lazy", "kind": 13, "location": { "uri": "file:///c%3A/b.lua" } }
			]
			"""
		);

		Assert.AreSequenceEqual(["flat", "lazy"], response.Symbols.Select(symbol => symbol.Name).ToArray());
		Assert.IsNotNull(response.Symbols[0].Location!.Value.Range);
		Assert.IsNull(response.Symbols[1].Location!.Value.Range);
	}

	[TestMethod]
	public void Deserialize_AnEntryWithoutAUsableNameKindOrLocation_IsSkippedAndTheOthersSurvive()
	{
		WorkspaceSymbolResponse response = WireJsonTestHelpers.Deserialize<WorkspaceSymbolResponse>(
			"""
			[
			  { "kind": 12, "location": { "uri": "file:///c%3A/a.lua" } },
			  { "name": "  ", "kind": 12, "location": { "uri": "file:///c%3A/a.lua" } },
			  { "name": "noKind", "location": { "uri": "file:///c%3A/a.lua" } },
			  { "name": "noLocation", "kind": 12 },
			  { "name": "noUri", "kind": 12, "location": { } },
			  { "name": "relative", "kind": 12, "location": { "uri": "a.lua" } },
			  { "name": "badRange", "kind": 12, "location": { "uri": "file:///c%3A/a.lua", "range": 7 } },
			  null,
			  7,
			  { "name": "good", "kind": 12, "location": { "uri": "file:///c%3A/a.lua" } }
			]
			"""
		);

		Assert.AreEqual("good", response.Symbols.Single().Name);
	}

	[TestMethod]
	public void Deserialize_APayloadThatIsNoArray_YieldsAnEmptyResponse()
	{
		Assert.IsEmpty(WireJsonTestHelpers.Deserialize<WorkspaceSymbolResponse>("""{ "name": "x" }""").Symbols);
		Assert.IsEmpty(WireJsonTestHelpers.Deserialize<WorkspaceSymbolResponse>("[]").Symbols);
	}

	[TestMethod]
	public void Deserialize_ANullResult_StaysNull() =>
		Assert.IsNull(JsonSerializer.Deserialize<WorkspaceSymbolResponse>("null", ProtocolSerialization.ForMessages(NullLogger.Instance)));

	[TestMethod]
	public void Serialize_ASymbolWithoutARange_WritesTheBareUriLocationAndTheDataBack()
	{
		WorkspaceSymbolPayload symbol = WireJsonTestHelpers.Deserialize<WorkspaceSymbolPayload>(
			"""{ "name": "Util", "kind": 5, "containerName": "Core", "location": { "uri": "file:///c%3A/ws/Util.cs" }, "data": { "id": 17 } }"""
		);

		using var written = JsonDocument.Parse(WireJsonTestHelpers.Serialize(symbol));
		JsonElement root = written.RootElement;

		Assert.AreEqual("Util", root.GetProperty("name").GetString());
		Assert.AreEqual(5, root.GetProperty("kind").GetInt32());
		Assert.AreEqual("Core", root.GetProperty("containerName").GetString());
		Assert.AreEqual("file:///c%3A/ws/Util.cs", root.GetProperty("location").GetProperty("uri").GetString());
		Assert.IsFalse(root.GetProperty("location").TryGetProperty("range", out _));
		Assert.IsFalse(root.TryGetProperty("tags", out _));
		Assert.AreEqual(17, root.GetProperty("data").GetProperty("id").GetInt32());
	}

	[TestMethod]
	public void Serialize_TheResponse_WritesAnArrayOfTheEntries()
	{
		var response = new WorkspaceSymbolResponse([
			new WorkspaceSymbolPayload
			{
				Name = "a",
				Kind = SymbolKind.Variable,

				Location = new WorkspaceSymbolLocationPayload(
					"file:///c%3A/a.lua",
					new RangePayload(new PositionPayload(1, 2), new PositionPayload(1, 3))
				),
			},
		]);

		using var written = JsonDocument.Parse(WireJsonTestHelpers.Serialize(response));

		Assert.AreEqual(JsonValueKind.Array, written.RootElement.ValueKind);

		Assert.AreEqual(
			2,
			written.RootElement[0].GetProperty("location").GetProperty("range").GetProperty("start").GetProperty("character").GetInt32()
		);
	}

	[TestMethod]
	public void Params_SerializeTheQueryOnly() =>
		Assert.AreEqual("""{"query":"util"}""", JsonSerializer.Serialize(new WorkspaceSymbolParams("util")));
}
