using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Deserializes hover responses tolerantly so one malformed optional member cannot fail the whole response.
/// </summary>
/// <remarks>
/// <para>
/// A JSON <see langword="null"/> response deserializes to a <see langword="null"/> response because the converter is
/// not invoked for JSON null. A <c>range</c> that is not an object or that carries no usable start position is
/// dropped with a warning instead of failing the response, so the hover contents stay usable. The <c>contents</c>
/// element is preserved verbatim because the protocol allows a string, a markup object, or an array of both.
/// </para>
/// <para>
/// Writing emits a member only when the response carries it, so a payload without a <c>contents</c> or <c>range</c>
/// member round-trips without an explicit JSON null.
/// </para>
/// </remarks>
public sealed class HoverResponseJsonConverter : JsonConverter<HoverResponse>
{
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="HoverResponseJsonConverter"/> class.
	/// </summary>
	public HoverResponseJsonConverter()
		: this(NullLogger.Instance)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="HoverResponseJsonConverter"/> class.
	/// </summary>
	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public HoverResponseJsonConverter(ILogger? logger)
		=> _logger = logger ?? NullLogger.Instance;

	/// <inheritdoc/>
	public override HoverResponse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using JsonDocument document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Object)
		{
			_logger.LogWarning("Ignoring malformed hover payload because its JSON kind {Kind} is not an object.", root.ValueKind);
			return new HoverResponse();
		}

		// The contents member is preserved verbatim: the protocol allows a string, a markup object, or an array of
		// both, and the hover parser decides how to read it.
		JsonElement? contents = JsonElementReadHelpers.TryGetProperty(root, "contents", out JsonElement contentsElement)
			? contentsElement.Clone()
			: null;

		return new HoverResponse
		{
			Contents = contents,
			Range = TryReadRange(root)
		};
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, HoverResponse value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();

		if (value.Contents is { } contents && contents.ValueKind != JsonValueKind.Undefined)
		{
			writer.WritePropertyName("contents");
			contents.WriteTo(writer);
		}

		if (value.Range is { } range)
		{
			writer.WritePropertyName("range");
			ProtocolJsonWriteHelpers.WriteRange(writer, range);
		}

		writer.WriteEndObject();
	}

	/// <summary>
	/// Reads the optional hover range tolerantly.
	/// </summary>
	/// <param name="root">The hover payload object.</param>
	/// <returns>
	/// The parsed range, or <see langword="null"/> when the payload carries none. A malformed range is dropped with a
	/// warning instead of failing the response.
	/// </returns>
	private ProtocolRangePayload? TryReadRange(JsonElement root)
	{
		if (!JsonElementReadHelpers.TryGetProperty(root, "range", out JsonElement rangeElement)
			|| rangeElement.ValueKind == JsonValueKind.Null)
		{
			return null;
		}

		if (rangeElement.ValueKind != JsonValueKind.Object)
		{
			_logger.LogWarning("Dropping malformed hover range because its JSON kind {Kind} is not an object.", rangeElement.ValueKind);
			return null;
		}

		if (!JsonElementReadHelpers.TryReadPosition(rangeElement, "start", out ProtocolPosition start))
		{
			_logger.LogWarning("Dropping malformed hover range because it does not carry a usable start position.");
			return null;
		}

		// A missing or malformed end position degrades to an empty range at the start, matching the document-symbol
		// reader, so an otherwise usable hover keeps its range.
		if (!JsonElementReadHelpers.TryReadPosition(rangeElement, "end", out ProtocolPosition end))
			end = start;

		return new(start, end);
	}
}
