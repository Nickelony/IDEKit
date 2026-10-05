using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents a protocol range payload with required start and end positions.</summary>
/// <param name="Start">The start position.</param>
/// <param name="End">The end position.</param>
/// <remarks>
/// The endpoints and their coordinates are required: a range that lacks any of them is a malformed payload and fails deserialization
/// instead of degrading into a partially valid range.
/// </remarks>
[JsonConverter(typeof(RangePayloadJsonConverter))]
public readonly record struct RangePayload(
	[property: JsonPropertyName("start")] PositionPayload Start,
	[property: JsonPropertyName("end")] PositionPayload End
)
{
	/// <summary>Indicates whether the range ends before it starts.</summary>
	/// <remarks>
	/// A reversed range is malformed: it identifies no content, so a reader that maps it to a model range rejects it instead of reading
	/// it as the range between its endpoints.
	/// </remarks>
	public bool IsReversed => End < Start;
}
