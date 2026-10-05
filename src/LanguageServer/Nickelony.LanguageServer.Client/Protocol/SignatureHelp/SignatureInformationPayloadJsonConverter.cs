using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Deserializes one signature-help signature tolerantly so one malformed element cannot fail the whole response.</summary>
/// <remarks>
/// An unusable payload shape produces an empty signature placeholder instead of throwing, and malformed parameter entries become empty placeholders:
/// the signature's <c>activeParameter</c> value is a position in the parameter array, so dropping an entry would silently re-target
/// the active parameter.
/// </remarks>
internal sealed class SignatureInformationPayloadJsonConverter : TolerantPayloadJsonConverter<SignatureInformationPayload>
{
	public SignatureInformationPayloadJsonConverter() { }

	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public SignatureInformationPayloadJsonConverter(ILogger? logger)
		: base(logger) { }

	/// <inheritdoc/>
	public override SignatureInformationPayload? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using var document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Object)
		{
			Logger.LogWarning("Ignoring malformed signature-help signature payload because its JSON kind {Kind} is not an object.", root.ValueKind);
			return new SignatureInformationPayload();
		}

		string? label =
			JsonElementReader.TryGetProperty(root, "label", out JsonElement labelElement) && labelElement.ValueKind == JsonValueKind.String
				? labelElement.GetString()
				: null;

		MarkupContentPayload? documentation = JsonElementReader.TryGetProperty(root, "documentation", out JsonElement documentationElement)
			? documentationElement.Deserialize<MarkupContentPayload>(options)
			: null;

		// The raw value is preserved so an absent property (Undefined) stays distinguishable from an explicit null
		// (no active parameter).
		JsonElement activeParameter = JsonElementReader.TryGetProperty(root, "activeParameter", out JsonElement activeParameterElement)
			? activeParameterElement.Clone()
			: default;

		IReadOnlyList<ParameterInformationPayload>? parameters = null;

		if (JsonElementReader.TryGetProperty(root, "parameters", out JsonElement parametersElement))
		{
			if (parametersElement.ValueKind == JsonValueKind.Array)
			{
				parameters = DeserializeParameters(parametersElement, options);
			}
			else if (parametersElement.ValueKind != JsonValueKind.Null)
			{
				Logger.LogWarning(
					"Ignoring malformed signature-help parameter list because its JSON kind {Kind} is not an array.",
					parametersElement.ValueKind
				);
			}
		}

		return new SignatureInformationPayload
		{
			Label = label,
			Documentation = documentation,
			ActiveParameter = activeParameter,
			Parameters = parameters,
		};
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, SignatureInformationPayload value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();

		if (value.Label is not null)
			writer.WriteString("label", value.Label);

		if (value.Documentation is { } documentation)
		{
			writer.WritePropertyName("documentation");
			JsonSerializer.Serialize(writer, documentation, options);
		}

		if (value.ActiveParameter.ValueKind != JsonValueKind.Undefined)
		{
			writer.WritePropertyName("activeParameter");
			value.ActiveParameter.WriteTo(writer);
		}

		if (value.Parameters is not null)
		{
			writer.WritePropertyName("parameters");
			writer.WriteStartArray();

			for (int i = 0; i < value.Parameters.Count; i++)
				JsonSerializer.Serialize(writer, value.Parameters[i], options);

			writer.WriteEndArray();
		}

		writer.WriteEndObject();
	}

	private ParameterInformationPayload[] DeserializeParameters(JsonElement parametersElement, JsonSerializerOptions options)
	{
		var parameters = new List<ParameterInformationPayload>(parametersElement.GetArrayLength());

		foreach (JsonElement parameterElement in parametersElement.EnumerateArray())
		{
			if (parameterElement.ValueKind != JsonValueKind.Object)
			{
				// Spec-shaped parameters are objects; an unusable element keeps its position as an empty
				// placeholder so following parameter indexes stay aligned.
				if (parameterElement.ValueKind != JsonValueKind.Null)
				{
					Logger.LogWarning(
						"Replacing malformed signature-help parameter because its JSON kind {Kind} is not an object with an empty placeholder.",
						parameterElement.ValueKind
					);
				}

				parameters.Add(new ParameterInformationPayload());
				continue;
			}

			parameters.Add(
				TryReadElement(
					element: parameterElement,
					options,
					elementDescription: "signature-help parameter",
					value: out ParameterInformationPayload? parameter
				)
					? parameter
					: new ParameterInformationPayload()
			);
		}

		return [.. parameters];
	}
}
