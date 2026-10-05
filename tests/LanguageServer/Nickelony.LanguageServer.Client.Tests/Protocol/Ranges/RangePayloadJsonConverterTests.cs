using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class RangePayloadJsonConverterTests
{
	[TestMethod]
	public void Deserialize_CompleteRange_ParsesTypedPositions()
	{
		RangePayload? range = JsonSerializer.Deserialize<RangePayload>(
			"""
			{
			  "start": { "line": 2, "character": 4 },
			  "end": { "line": 3, "character": 1 }
			}
			"""
		);

		Assert.IsNotNull(range);
		Assert.AreEqual(2, range.Value.Start.Line);
		Assert.AreEqual(4, range.Value.Start.Character);
		Assert.AreEqual(3, range.Value.End.Line);
		Assert.AreEqual(1, range.Value.End.Character);
	}

	[TestMethod]
	public void Serialize_WritesStartAndEndPositions()
	{
		string json = JsonSerializer.Serialize(new RangePayload(new PositionPayload(1, 2), new PositionPayload(1, 5)));
		Assert.AreEqual("""{"start":{"line":1,"character":2},"end":{"line":1,"character":5}}""", json);
	}

	[TestMethod]
	public void Deserialize_RangeWithMissingEnd_Throws()
	{
		// A partial range must not degrade into a range with a default end position.
		Assert.ThrowsExactly<JsonException>(() =>
			JsonSerializer.Deserialize<RangePayload>(
				"""
				{
				  "start": { "line": 2, "character": 4 }
				}
				"""
			)
		);
	}

	[TestMethod]
	public void Deserialize_RangeWithNullStart_Throws()
	{
		Assert.ThrowsExactly<JsonException>(() =>
			JsonSerializer.Deserialize<RangePayload>(
				"""
				{
				  "start": null,
				  "end": { "line": 3, "character": 1 }
				}
				"""
			)
		);
	}

	[TestMethod]
	public void Deserialize_RangeWithMissingCharacter_Throws()
	{
		Assert.ThrowsExactly<JsonException>(() =>
			JsonSerializer.Deserialize<RangePayload>(
				"""
				{
				  "start": { "line": 2 },
				  "end": { "line": 3, "character": 1 }
				}
				"""
			)
		);
	}

	[TestMethod]
	public void Deserialize_RangeWithStringCoordinate_Throws()
	{
		Assert.ThrowsExactly<JsonException>(() =>
			JsonSerializer.Deserialize<RangePayload>(
				"""
				{
				  "start": { "line": "2", "character": 4 },
				  "end": { "line": 3, "character": 1 }
				}
				"""
			)
		);
	}

	[TestMethod]
	public void Deserialize_NonObjectRange_Throws() => Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<RangePayload>("5"));

	[TestMethod]
	public void Deserialize_NegativeCoordinates_StayRepresentable()
	{
		RangePayload? range = JsonSerializer.Deserialize<RangePayload>(
			"""
			{
			  "start": { "line": -1, "character": 0 },
			  "end": { "line": 0, "character": 1 }
			}
			"""
		);

		Assert.IsNotNull(range);
		Assert.AreEqual(-1, range.Value.Start.Line);
	}

	[TestMethod]
	public void Deserialize_JSONNull_LeavesNullableRangeNull()
	{
		LocationPayload response = JsonSerializer.Deserialize<LocationPayload>(
			"""
			{ "uri": "file:///workspace/references.ext", "range": null }
			"""
		);

		Assert.IsNull(response.Range);
	}

	[TestMethod]
	public void TryReadPosition_PropertyWithIntegerCoordinates_ReadsThePosition()
	{
		using var document = JsonDocument.Parse("""{ "pos": { "line": 3, "character": 7 } }""");

		Assert.IsTrue(RangePayloadJsonConverter.TryReadPosition(document.RootElement, "pos", out PositionPayload position));
		Assert.AreEqual(new PositionPayload(3, 7), position);
	}

	[TestMethod]
	public void TryReadPosition_AbsentProperty_ReportsFailure()
	{
		using var document = JsonDocument.Parse("""{ "other": { "line": 3, "character": 7 } }""");

		Assert.IsFalse(RangePayloadJsonConverter.TryReadPosition(document.RootElement, "pos", out PositionPayload position));
		Assert.AreEqual(default, position);
	}

	[TestMethod]
	[DataRow("""{ "line": "3", "character": 7 }""", DisplayName = "string line")]
	[DataRow("""{ "line": 3, "character": 7.5 }""", DisplayName = "fractional character")]
	[DataRow("""{ "line": 3 }""", DisplayName = "missing character")]
	[DataRow("""{ "character": 7 }""", DisplayName = "missing line")]
	[DataRow("""[3, 7]""", DisplayName = "array")]
	[DataRow("""null""", DisplayName = "null")]
	public void TryReadPosition_MalformedPosition_ReportsFailure(string positionJson)
	{
		using var document = JsonDocument.Parse(positionJson);
		Assert.IsFalse(RangePayloadJsonConverter.TryReadPosition(document.RootElement, out _));
	}

	[TestMethod]
	public void TryReadPosition_NegativeCoordinate_IsRejectedOnlyWhenRequested()
	{
		using var document = JsonDocument.Parse("""{ "line": -1, "character": 2 }""");

		Assert.IsTrue(RangePayloadJsonConverter.TryReadPosition(document.RootElement, out PositionPayload position));
		Assert.AreEqual(new PositionPayload(-1, 2), position);
		Assert.IsFalse(RangePayloadJsonConverter.TryReadPosition(document.RootElement, out _, requireNonNegativeCoordinates: true));
	}

	[TestMethod]
	public void WriteRange_WritesStartAndEndInThatOrder()
	{
		using var stream = new MemoryStream();

		using (var writer = new Utf8JsonWriter(stream))
			RangePayloadJsonConverter.WriteRange(writer, new RangePayload(new PositionPayload(0, 1), new PositionPayload(2, 3)));

		Assert.AreEqual(
			"""{"start":{"line":0,"character":1},"end":{"line":2,"character":3}}""",
			System.Text.Encoding.UTF8.GetString(stream.ToArray())
		);
	}
}
