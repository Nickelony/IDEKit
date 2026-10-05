using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Reads capability unregistration payloads while tolerating the misspelled property name.</summary>
/// <remarks>
/// A payload whose root is not a JSON object degrades to an empty payload with a warning.
/// </remarks>
internal sealed class CapabilityUnregistrationParamsJsonConverter : TolerantPayloadJsonConverter<CapabilityUnregistrationParams>
{
	public CapabilityUnregistrationParamsJsonConverter() { }

	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public CapabilityUnregistrationParamsJsonConverter(ILogger? logger)
		: base(logger) { }

	/// <inheritdoc/>
	public override CapabilityUnregistrationParams Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using var document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind is not JsonValueKind.Object)
		{
			Logger.LogWarning("Ignoring malformed capability-unregistration payload because its JSON kind {Kind} is not an object.", root.ValueKind);
			return new(null);
		}

		CapabilityUnregistrationPayload[]? unregistrations = null;

		if (JsonElementReader.TryGetProperty(root, "unregistrations", out JsonElement correctedProperty))
			unregistrations = correctedProperty.Deserialize<CapabilityUnregistrationPayload[]>(options);
		else if (JsonElementReader.TryGetProperty(root, "unregisterations", out JsonElement misspelledProperty))
			unregistrations = misspelledProperty.Deserialize<CapabilityUnregistrationPayload[]>(options);

		return new(unregistrations);
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, CapabilityUnregistrationParams value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();

		// The protocol defines this wire name with a spelling typo ("unregisterations"); this client only
		// receives the payload, so writing follows the specification while the reader accepts either spelling.
		writer.WritePropertyName("unregisterations");
		JsonSerializer.Serialize(writer, value.Unregistrations, options);
		writer.WriteEndObject();
	}
}
