using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Reads and writes <see cref="ProtocolRangePayload"/> values, requiring both endpoints and both of
/// their coordinates.
/// </summary>
/// <remarks>
/// <para>
/// A range element that is not an object, lacks <c>start</c> or <c>end</c>, or carries a non-integer
/// <c>line</c> or <c>character</c> is malformed and fails deserialization with
/// <see cref="JsonException"/>. The tolerant converters that own element skipping (completion items
/// and diagnostics) decide whether a malformed entry is dropped; payloads bound directly by the
/// transport fail loudly instead. A JSON <see langword="null"/> binds to a null nullable range
/// without invoking this converter.
/// </para>
/// <para>
/// Negative coordinates remain representable and round-trip unchanged; hosts validate the values.
/// </para>
/// </remarks>
public sealed class ProtocolRangePayloadJsonConverter : JsonConverter<ProtocolRangePayload>
{
	/// <inheritdoc/>
	/// <exception cref="JsonException">The payload is not an object with complete start and end positions.</exception>
	public override ProtocolRangePayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using JsonDocument document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Object)
			throw new JsonException("A protocol range must be a JSON object carrying 'start' and 'end' positions.");

		if (!JsonElementReadHelpers.TryReadPosition(root, "start", out ProtocolPosition start))
			throw new JsonException("A protocol range must carry a 'start' position with integer 'line' and 'character' values.");

		if (!JsonElementReadHelpers.TryReadPosition(root, "end", out ProtocolPosition end))
			throw new JsonException("A protocol range must carry an 'end' position with integer 'line' and 'character' values.");

		return new(start, end);
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, ProtocolRangePayload value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		ProtocolJsonWriteHelpers.WritePosition(writer, "start", value.Start);
		ProtocolJsonWriteHelpers.WritePosition(writer, "end", value.End);
		writer.WriteEndObject();
	}
}
