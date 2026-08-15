using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a dynamic capability unregistration request from the language server.
/// </summary>
/// <remarks>
/// The JSON wire contract accepts both the historical misspelling <c>unregisterations</c> and the
/// corrected <c>unregistrations</c> property name.
/// </remarks>
/// <param name="Unregistrations">The requested capability removals. Null entries are skipped during binding.</param>
[JsonConverter(typeof(CapabilityUnregistrationParamsJsonConverter))]
internal readonly record struct CapabilityUnregistrationParams(
	CapabilityUnregistrationPayload[]? Unregistrations);

/// <summary>
/// Represents one dynamic capability unregistration entry requested by the language server.
/// </summary>
/// <param name="Id">The server-defined registration identifier.</param>
/// <param name="Method">The capability method being unregistered.</param>
internal readonly record struct CapabilityUnregistrationPayload(
	[property: JsonPropertyName("id")] string? Id,
	[property: JsonPropertyName("method")] string? Method);

/// <summary>
/// Reads capability unregistration payloads while tolerating the historical misspelled property name.
/// </summary>
internal sealed class CapabilityUnregistrationParamsJsonConverter : JsonConverter<CapabilityUnregistrationParams>
{
	/// <inheritdoc/>
	public override CapabilityUnregistrationParams Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using JsonDocument document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind is not JsonValueKind.Object)
			throw new JsonException("Capability unregistration payload must be a JSON object.");

		CapabilityUnregistrationPayload[]? unregistrations = null;

		if (JsonElementReadHelpers.TryGetProperty(root, "unregistrations", out JsonElement correctedProperty))
			unregistrations = correctedProperty.Deserialize<CapabilityUnregistrationPayload[]>(options);
		else if (JsonElementReadHelpers.TryGetProperty(root, "unregisterations", out JsonElement misspelledProperty))
			unregistrations = misspelledProperty.Deserialize<CapabilityUnregistrationPayload[]>(options);

		return new(unregistrations);
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, CapabilityUnregistrationParams value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		// LSP 3.17 defines this wire name with a specification typo ("unregisterations"); this client only
		// receives the payload, so writing follows the specification while the reader accepts both spellings.
		writer.WritePropertyName("unregisterations");
		JsonSerializer.Serialize(writer, value.Unregistrations, options);
		writer.WriteEndObject();
	}
}
