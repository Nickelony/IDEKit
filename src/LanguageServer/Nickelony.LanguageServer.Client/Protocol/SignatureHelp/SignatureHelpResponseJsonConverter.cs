using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Deserializes signature-help responses tolerantly so one malformed element cannot fail the whole response.</summary>
/// <remarks>
/// A JSON <see langword="null"/> response deserializes to a <see langword="null"/> response because the converter
/// is not invoked for JSON <see langword="null"/>. A malformed element inside a signature list becomes an empty placeholder instead of being dropped:
/// the <c>activeSignature</c> value is a position in the server's array, so removing an element would silently re-target the active signature.
/// </remarks>
internal sealed class SignatureHelpResponseJsonConverter : TolerantPayloadJsonConverter<SignatureHelpResponse>
{
	public SignatureHelpResponseJsonConverter() { }

	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public SignatureHelpResponseJsonConverter(ILogger? logger)
		: base(logger) { }

	/// <inheritdoc/>
	public override SignatureHelpResponse? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using var document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Object)
		{
			Logger.LogWarning("Ignoring malformed signature-help payload because its JSON kind {Kind} is not an object.", root.ValueKind);
			return new SignatureHelpResponse();
		}

		int? activeSignature = null;

		if (JsonElementReader.TryGetProperty(root, "activeSignature", out JsonElement activeSignatureElement))
		{
			if (activeSignatureElement.ValueKind == JsonValueKind.Number && activeSignatureElement.TryGetInt32(out int activeSignatureIndex))
			{
				activeSignature = activeSignatureIndex;
			}
			else if (activeSignatureElement.ValueKind != JsonValueKind.Null)
			{
				Logger.LogWarning(
					"Ignoring malformed signature-help 'activeSignature' property with JSON kind {Kind}.",
					activeSignatureElement.ValueKind
				);
			}
		}

		// The raw value is preserved so an absent property (Undefined) stays distinguishable from an explicit null
		// (no active parameter).
		JsonElement activeParameter = JsonElementReader.TryGetProperty(root, "activeParameter", out JsonElement activeParameterElement)
			? activeParameterElement.Clone()
			: default;

		IReadOnlyList<SignatureInformationPayload>? signatures = null;

		if (JsonElementReader.TryGetProperty(root, "signatures", out JsonElement signaturesElement))
		{
			if (signaturesElement.ValueKind == JsonValueKind.Array)
			{
				signatures = DeserializeSignatures(signaturesElement, options);
			}
			else if (signaturesElement.ValueKind != JsonValueKind.Null)
			{
				Logger.LogWarning(
					"Ignoring malformed signature-help signature list because its JSON kind {Kind} is not an array.",
					signaturesElement.ValueKind
				);
			}
		}

		return new SignatureHelpResponse
		{
			ActiveSignature = activeSignature,
			ActiveParameter = activeParameter,
			Signatures = signatures,
		};
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, SignatureHelpResponse value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();

		if (value.ActiveSignature is int activeSignature)
			writer.WriteNumber("activeSignature", activeSignature);

		if (value.ActiveParameter.ValueKind != JsonValueKind.Undefined)
		{
			writer.WritePropertyName("activeParameter");
			value.ActiveParameter.WriteTo(writer);
		}

		if (value.Signatures is not null)
		{
			writer.WritePropertyName("signatures");
			writer.WriteStartArray();

			for (int i = 0; i < value.Signatures.Count; i++)
				JsonSerializer.Serialize(writer, value.Signatures[i], options);

			writer.WriteEndArray();
		}

		writer.WriteEndObject();
	}

	private SignatureInformationPayload[] DeserializeSignatures(JsonElement signaturesElement, JsonSerializerOptions options)
	{
		var signatures = new List<SignatureInformationPayload>(signaturesElement.GetArrayLength());

		foreach (JsonElement signatureElement in signaturesElement.EnumerateArray())
		{
			// An unusable element is replaced with an empty placeholder instead of being removed: the
			// active-signature index is a position in the server's array, so dropping an element would
			// shift every following index. The signature payload converter substitutes an empty payload
			// for non-object elements.
			if (signatureElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
			{
				Logger.LogWarning(
					"Replacing malformed signature-help signature (JSON kind {Kind}) with an empty placeholder to preserve index alignment.",
					signatureElement.ValueKind
				);
			}

			signatures.Add(
				TryReadElement(
					element: signatureElement,
					options,
					elementDescription: "signature-help signature",
					value: out SignatureInformationPayload? signature
				)
					? signature
					: new SignatureInformationPayload()
			);
		}

		return [.. signatures];
	}
}
