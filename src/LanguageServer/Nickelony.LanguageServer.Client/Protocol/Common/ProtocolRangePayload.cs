using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a protocol range payload with required start and end positions.
/// </summary>
/// <remarks>
/// <para>
/// Both endpoints and both of their coordinates are required: a range that lacks any of them is a
/// malformed payload and fails deserialization instead of degrading into a partially valid range.
/// Optionality is expressed by the containing property being nullable, so a present range always
/// describes a complete start and end position.
/// </para>
/// <para>
/// Negative coordinates remain representable; the conversion and parsing boundaries that consume a
/// range validate the values and reject or clamp them per host policy.
/// </para>
/// </remarks>
/// <param name="Start">The start position.</param>
/// <param name="End">The end position.</param>
[JsonConverter(typeof(ProtocolRangePayloadJsonConverter))]
public readonly record struct ProtocolRangePayload(
	[property: JsonPropertyName("start")] ProtocolPosition Start,
	[property: JsonPropertyName("end")] ProtocolPosition End);
