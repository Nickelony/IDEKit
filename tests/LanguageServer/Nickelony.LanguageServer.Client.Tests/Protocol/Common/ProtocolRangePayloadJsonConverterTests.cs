using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class ProtocolRangePayloadJsonConverterTests
{
	[TestMethod]
	public void Deserialize_CompleteRange_ParsesTypedPositions()
	{
		ProtocolRangePayload? range = JsonSerializer.Deserialize<ProtocolRangePayload>(
			"""
			{
			  "start": { "line": 2, "character": 4 },
			  "end": { "line": 3, "character": 1 }
			}
			""");

		Assert.IsNotNull(range);
		Assert.AreEqual(2, range.Value.Start.Line);
		Assert.AreEqual(4, range.Value.Start.Character);
		Assert.AreEqual(3, range.Value.End.Line);
		Assert.AreEqual(1, range.Value.End.Character);
	}

	[TestMethod]
	public void Serialize_WritesStartAndEndPositions()
	{
		string json = JsonSerializer.Serialize(new ProtocolRangePayload(
			new ProtocolPosition(1, 2),
			new ProtocolPosition(1, 5)));

		Assert.AreEqual("""{"start":{"line":1,"character":2},"end":{"line":1,"character":5}}""", json);
	}

	[TestMethod]
	public void Deserialize_RangeWithMissingEnd_Throws()
	{
		// A partial range must not degrade into a range with a default end position.
		Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<ProtocolRangePayload>(
			"""
			{
			  "start": { "line": 2, "character": 4 }
			}
			"""));
	}

	[TestMethod]
	public void Deserialize_RangeWithNullStart_Throws()
	{
		Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<ProtocolRangePayload>(
			"""
			{
			  "start": null,
			  "end": { "line": 3, "character": 1 }
			}
			"""));
	}

	[TestMethod]
	public void Deserialize_RangeWithMissingCharacter_Throws()
	{
		Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<ProtocolRangePayload>(
			"""
			{
			  "start": { "line": 2 },
			  "end": { "line": 3, "character": 1 }
			}
			"""));
	}

	[TestMethod]
	public void Deserialize_RangeWithStringCoordinate_Throws()
	{
		Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<ProtocolRangePayload>(
			"""
			{
			  "start": { "line": "2", "character": 4 },
			  "end": { "line": 3, "character": 1 }
			}
			"""));
	}

	[TestMethod]
	public void Deserialize_NonObjectRange_Throws()
	{
		Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<ProtocolRangePayload>("5"));
	}

	[TestMethod]
	public void Deserialize_NegativeCoordinates_StayRepresentable()
	{
		ProtocolRangePayload? range = JsonSerializer.Deserialize<ProtocolRangePayload>(
			"""
			{
			  "start": { "line": -1, "character": 0 },
			  "end": { "line": 0, "character": 1 }
			}
			""");

		Assert.IsNotNull(range);
		Assert.AreEqual(-1, range.Value.Start.Line);
	}

	[TestMethod]
	public void Deserialize_JSONNull_LeavesNullableRangeNull()
	{
		ReferenceLocationPayload? response = JsonSerializer.Deserialize<ReferenceLocationPayload>(
			"""
			{ "uri": "file:///workspace/references.ext", "range": null }
			""");

		Assert.IsNotNull(response);
		Assert.IsNull(response.Range);
	}
}
