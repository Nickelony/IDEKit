using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Deserializes one signature-help signature tolerantly so one malformed element cannot fail the whole response.
/// </summary>
/// <remarks>
/// An unusable payload shape produces an empty signature placeholder instead of throwing, and malformed parameter
/// entries become empty placeholders: the response's <c>activeParameter</c> value is a position in the parameter
/// array, so dropping an entry would silently re-target the active parameter. The signature-help response converter
/// composes this converter.
/// </remarks>
public sealed class SignatureHelpSignaturePayloadJsonConverter : JsonConverter<SignatureHelpSignaturePayload>
{
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="SignatureHelpSignaturePayloadJsonConverter"/> class.
	/// </summary>
	public SignatureHelpSignaturePayloadJsonConverter()
		: this(NullLogger.Instance)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="SignatureHelpSignaturePayloadJsonConverter"/> class.
	/// </summary>
	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public SignatureHelpSignaturePayloadJsonConverter(ILogger? logger)
		=> _logger = logger ?? NullLogger.Instance;

	/// <inheritdoc/>
	public override SignatureHelpSignaturePayload? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using JsonDocument document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Object)
		{
			_logger.LogWarning("Ignoring malformed signature-help signature payload because its JSON kind {Kind} is not an object.", root.ValueKind);
			return new SignatureHelpSignaturePayload();
		}

		string? label = JsonElementReadHelpers.TryGetProperty(root, "label", out JsonElement labelElement) && labelElement.ValueKind == JsonValueKind.String
			? labelElement.GetString()
			: null;

		JsonElement? documentation = JsonElementReadHelpers.TryGetProperty(root, "documentation", out JsonElement documentationElement)
			? documentationElement.Clone()
			: null;

		// The raw value is preserved so an absent property (Undefined) stays distinguishable from
		// an explicit null, which is the LSP 3.18 "no active parameter" state.
		JsonElement activeParameter = JsonElementReadHelpers.TryGetProperty(root, "activeParameter", out JsonElement activeParameterElement)
			? activeParameterElement.Clone()
			: default;

		IReadOnlyList<SignatureHelpParameterPayload>? parameters = null;

		if (JsonElementReadHelpers.TryGetProperty(root, "parameters", out JsonElement parametersElement))
		{
			if (parametersElement.ValueKind == JsonValueKind.Array)
				parameters = DeserializeParameters(parametersElement, options);
			else if (parametersElement.ValueKind != JsonValueKind.Null)
				_logger.LogWarning("Ignoring malformed signature-help parameter list because its JSON kind {Kind} is not an array.", parametersElement.ValueKind);
		}

		return new SignatureHelpSignaturePayload
		{
			Label = label,
			Documentation = documentation,
			ActiveParameter = activeParameter,
			Parameters = parameters
		};
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, SignatureHelpSignaturePayload value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();

		if (value.Label is not null)
			writer.WriteString("label", value.Label);

		if (value.Documentation is JsonElement documentation)
		{
			writer.WritePropertyName("documentation");
			documentation.WriteTo(writer);
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

	private SignatureHelpParameterPayload[] DeserializeParameters(JsonElement parametersElement, JsonSerializerOptions options)
	{
		var parameters = new List<SignatureHelpParameterPayload>();

		foreach (JsonElement parameterElement in parametersElement.EnumerateArray())
		{
			if (parameterElement.ValueKind != JsonValueKind.Object)
			{
				// Spec-shaped parameters are objects; an unusable element keeps its position as an empty
				// placeholder so following parameter indexes stay aligned.
				if (parameterElement.ValueKind != JsonValueKind.Null)
				{
					_logger.LogWarning("Replacing malformed signature-help parameter because its JSON kind {Kind} is not an object with an empty placeholder.",
						parameterElement.ValueKind);
				}

				parameters.Add(new SignatureHelpParameterPayload());
				continue;
			}

			try
			{
				SignatureHelpParameterPayload? parameter = parameterElement.Deserialize<SignatureHelpParameterPayload>(options);

				parameters.Add(parameter ?? new SignatureHelpParameterPayload());
			}
			catch (Exception exception) when (exception is JsonException or InvalidOperationException)
			{
				_logger.LogWarning(exception, "Replacing malformed signature-help parameter element with an empty placeholder to preserve index alignment.");
				parameters.Add(new SignatureHelpParameterPayload());
			}
		}

		return [.. parameters];
	}
}
