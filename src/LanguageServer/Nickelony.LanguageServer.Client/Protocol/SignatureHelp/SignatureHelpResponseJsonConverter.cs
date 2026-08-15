using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Deserializes signature-help responses tolerantly so one malformed element cannot fail the whole response.
/// </summary>
/// <remarks>
/// A JSON <see langword="null"/> response deserializes to a <see langword="null"/> response because the converter
/// is not invoked for JSON null. A malformed element inside a signature list becomes an empty placeholder instead
/// of being dropped: the response's <c>activeSignature</c> and <c>activeParameter</c> values are positions in the
/// server's arrays, so removing an element would silently re-target the active signature.
/// </remarks>
public sealed class SignatureHelpResponseJsonConverter : JsonConverter<SignatureHelpResponse>
{
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="SignatureHelpResponseJsonConverter"/> class.
	/// </summary>
	public SignatureHelpResponseJsonConverter()
		: this(NullLogger.Instance)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="SignatureHelpResponseJsonConverter"/> class.
	/// </summary>
	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public SignatureHelpResponseJsonConverter(ILogger? logger)
		=> _logger = logger ?? NullLogger.Instance;

	/// <inheritdoc/>
	public override SignatureHelpResponse? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using JsonDocument document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Object)
		{
			_logger.LogWarning("Ignoring malformed signature-help payload because its JSON kind {Kind} is not an object.", root.ValueKind);
			return new SignatureHelpResponse();
		}

		int? activeSignature = null;

		if (JsonElementReadHelpers.TryGetProperty(root, "activeSignature", out JsonElement activeSignatureElement))
		{
			if (activeSignatureElement.ValueKind == JsonValueKind.Number
				&& activeSignatureElement.TryGetInt32(out int activeSignatureIndex))
			{
				activeSignature = activeSignatureIndex;
			}
			else if (activeSignatureElement.ValueKind != JsonValueKind.Null)
			{
				_logger.LogWarning("Ignoring malformed signature-help 'activeSignature' property with JSON kind {Kind}.", activeSignatureElement.ValueKind);
			}
		}

		// The raw value is preserved so an absent property (Undefined) stays distinguishable from
		// an explicit null, which is the LSP 3.18 "no active parameter" state.
		JsonElement activeParameter = JsonElementReadHelpers.TryGetProperty(root, "activeParameter", out JsonElement activeParameterElement)
			? activeParameterElement.Clone()
			: default;

		IReadOnlyList<SignatureHelpSignaturePayload>? signatures = null;

		if (JsonElementReadHelpers.TryGetProperty(root, "signatures", out JsonElement signaturesElement))
		{
			if (signaturesElement.ValueKind == JsonValueKind.Array)
				signatures = DeserializeSignatures(signaturesElement, options);
			else if (signaturesElement.ValueKind != JsonValueKind.Null)
				_logger.LogWarning("Ignoring malformed signature-help signature list because its JSON kind {Kind} is not an array.", signaturesElement.ValueKind);
		}

		return new SignatureHelpResponse
		{
			ActiveSignature = activeSignature,
			ActiveParameter = activeParameter,
			Signatures = signatures
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

	private SignatureHelpSignaturePayload[] DeserializeSignatures(JsonElement signaturesElement, JsonSerializerOptions options)
	{
		var signatures = new List<SignatureHelpSignaturePayload>();

		foreach (JsonElement signatureElement in signaturesElement.EnumerateArray())
		{
			// An unusable element is replaced with an empty placeholder instead of being removed: the
			// active-signature index is a position in the server's array, so dropping an element would
			// shift every following index. The signature payload converter substitutes an empty payload
			// for non-object elements.
			if (signatureElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
			{
				_logger.LogWarning("Replacing malformed signature-help signature (JSON kind {Kind}) with an empty placeholder to preserve index alignment.",
					signatureElement.ValueKind);
			}

			try
			{
				SignatureHelpSignaturePayload? signature = signatureElement.Deserialize<SignatureHelpSignaturePayload>(options);

				signatures.Add(signature ?? new SignatureHelpSignaturePayload());
			}
			catch (Exception exception) when (exception is JsonException or InvalidOperationException)
			{
				_logger.LogWarning(exception, "Replacing malformed signature-help signature element with an empty placeholder to preserve index alignment.");
				signatures.Add(new SignatureHelpSignaturePayload());
			}
		}

		return [.. signatures];
	}
}
