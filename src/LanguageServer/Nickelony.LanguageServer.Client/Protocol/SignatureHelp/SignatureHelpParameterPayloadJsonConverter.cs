using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Reads and writes <see cref="SignatureHelpParameterPayload"/> values while tolerating an undefined label.
/// </summary>
/// <remarks>
/// The signature-help converter materializes empty placeholders for malformed parameter elements; such a placeholder
/// carries an undefined <see cref="SignatureHelpParameterPayload.Label"/>, which the default serialization contract
/// cannot write because <see cref="JsonElement"/> rejects undefined elements. This converter omits an undefined
/// label so placeholders round-trip as empty parameter objects.
/// </remarks>
public sealed class SignatureHelpParameterPayloadJsonConverter : JsonConverter<SignatureHelpParameterPayload>
{
	/// <inheritdoc/>
	public override SignatureHelpParameterPayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using JsonDocument document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Object)
			return new();

		JsonElement label = JsonElementReadHelpers.TryGetProperty(root, "label", out JsonElement labelElement)
			? labelElement.Clone()
			: default;

		JsonElement? documentation = JsonElementReadHelpers.TryGetProperty(root, "documentation", out JsonElement documentationElement)
			? documentationElement.Clone()
			: null;

		return new SignatureHelpParameterPayload
		{
			Label = label,
			Documentation = documentation
		};
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, SignatureHelpParameterPayload value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();

		if (value.Label.ValueKind != JsonValueKind.Undefined)
		{
			writer.WritePropertyName("label");
			value.Label.WriteTo(writer);
		}

		if (value.Documentation is JsonElement documentation)
		{
			writer.WritePropertyName("documentation");
			documentation.WriteTo(writer);
		}

		writer.WriteEndObject();
	}
}
