using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Reads and writes <see cref="RangePayload"/> values, requiring the endpoints and their coordinates.</summary>
/// <remarks>
/// <para>
/// A range element that is not an object, lacks <c>start</c> or <c>end</c>, or carries a non-integer <c>line</c> or <c>character</c>
/// is malformed and fails deserialization with <see cref="JsonException"/>. The tolerant converters that skip malformed elements decide
/// whether a malformed entry is dropped; payloads bound directly by the protocol serializer fail loudly instead. A JSON <see langword="null"/> binds
/// to a <see langword="null"/> nullable range without invoking this converter.
/// </para>
/// <para>
/// Negative coordinates remain representable and round-trip unchanged; hosts validate the values.
/// </para>
/// </remarks>
internal sealed class RangePayloadJsonConverter : JsonConverter<RangePayload>
{
	/// <inheritdoc/>
	/// <exception cref="JsonException">The payload is not an object with complete start and end positions.</exception>
	public override RangePayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using var document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Object)
			throw new JsonException("A protocol range must be a JSON object carrying 'start' and 'end' positions.");

		if (!TryReadPosition(root, "start", out PositionPayload start))
			throw new JsonException("A protocol range must carry a 'start' position with integer 'line' and 'character' values.");

		if (!TryReadPosition(root, "end", out PositionPayload end))
			throw new JsonException("A protocol range must carry an 'end' position with integer 'line' and 'character' values.");

		return new(start, end);
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, RangePayload value, JsonSerializerOptions options) => WriteRange(writer, value);

	/// <summary>Writes one range object with its start and end positions.</summary>
	/// <param name="writer">The writer that receives the range object.</param>
	/// <param name="range">The range to write.</param>
	/// <remarks>
	/// Every converter that round-trips a <see cref="RangePayload"/> writes it through this method, so the wire shape of positions and ranges
	/// is identical across them.
	/// </remarks>
	internal static void WriteRange(Utf8JsonWriter writer, RangePayload range)
	{
		writer.WriteStartObject();

		WritePosition(writer, "start", range.Start);
		WritePosition(writer, "end", range.End);

		writer.WriteEndObject();
	}

	/// <summary>Reads one range property of a payload element with the supplied tolerance policy.</summary>
	/// <param name="payloadElement">The payload element to read from.</param>
	/// <param name="propertyName">The property that carries the range.</param>
	/// <param name="allowNegative">Whether negative coordinates are accepted instead of rejected.</param>
	/// <param name="allowInverted">Whether an end position that precedes the start is accepted instead of rejected.</param>
	/// <returns>The parsed range, or <see langword="null"/> when the property is absent or carries no usable range.</returns>
	/// <remarks>
	/// <para>
	/// The document-symbol and hover readers accept negative or inverted ranges because the consuming conversion
	/// validates and clamps them. The definition reader rejects them because a definition target is dereferenced to open a document, and a range
	/// that identifies no content is not a usable target. The policy is passed explicitly so the tolerance is visible at the call site.
	/// </para>
	/// <para>
	/// A missing or malformed end position always degrades to an empty range at the start, so an otherwise usable entry keeps its range
	/// under every policy.
	/// </para>
	/// </remarks>
	internal static RangePayload? ReadRangeProperty(JsonElement payloadElement, string propertyName, bool allowNegative, bool allowInverted)
	{
		if (
			!JsonElementReader.TryGetProperty(payloadElement, propertyName, out JsonElement rangeElement)
			|| rangeElement.ValueKind != JsonValueKind.Object
			|| !TryReadPosition(
				element: rangeElement,
				propertyName: "start",
				position: out PositionPayload start,
				requireNonNegativeCoordinates: !allowNegative
			)
		)
		{
			return null;
		}

		if (
			!TryReadPosition(
				element: rangeElement,
				propertyName: "end",
				position: out PositionPayload end,
				requireNonNegativeCoordinates: !allowNegative
			)
		)
		{
			end = start;
		}

		var range = new RangePayload(start, end);

		return !allowInverted && range.IsReversed ? null : range;
	}

	/// <summary>Reads one position property of a payload element.</summary>
	/// <param name="element">The containing payload element.</param>
	/// <param name="propertyName">The property that carries the position object.</param>
	/// <param name="position">Receives the parsed position.</param>
	/// <param name="requireNonNegativeCoordinates">Whether negative coordinates should be rejected.</param>
	/// <returns><see langword="true"/> when the property carries an object with integer coordinates.</returns>
	internal static bool TryReadPosition(
		JsonElement element,
		string propertyName,
		out PositionPayload position,
		bool requireNonNegativeCoordinates = false
	)
	{
		if (!JsonElementReader.TryGetProperty(element, propertyName, out JsonElement positionElement))
		{
			position = default;
			return false;
		}

		return TryReadPosition(positionElement, out position, requireNonNegativeCoordinates);
	}

	/// <summary>Reads one position object.</summary>
	/// <param name="positionElement">The position object to read.</param>
	/// <param name="position">Receives the parsed position.</param>
	/// <param name="requireNonNegativeCoordinates">Whether negative coordinates should be rejected.</param>
	/// <returns><see langword="true"/> when the element carries an object with integer coordinates.</returns>
	internal static bool TryReadPosition(JsonElement positionElement, out PositionPayload position, bool requireNonNegativeCoordinates = false)
	{
		position = default;

		if (
			positionElement.ValueKind != JsonValueKind.Object
			|| !JsonElementReader.TryGetProperty(positionElement, "line", out JsonElement lineElement)
			|| lineElement.ValueKind != JsonValueKind.Number
			|| !lineElement.TryGetInt32(out int line)
			|| !JsonElementReader.TryGetProperty(positionElement, "character", out JsonElement characterElement)
			|| characterElement.ValueKind != JsonValueKind.Number
			|| !characterElement.TryGetInt32(out int character)
		)
		{
			return false;
		}

		if (requireNonNegativeCoordinates && (line < 0 || character < 0))
			return false;

		position = new(line, character);
		return true;
	}

	private static void WritePosition(Utf8JsonWriter writer, string propertyName, PositionPayload position)
	{
		writer.WritePropertyName(propertyName);
		writer.WriteStartObject();

		writer.WriteNumber("line", position.Line);
		writer.WriteNumber("character", position.Character);

		writer.WriteEndObject();
	}
}
