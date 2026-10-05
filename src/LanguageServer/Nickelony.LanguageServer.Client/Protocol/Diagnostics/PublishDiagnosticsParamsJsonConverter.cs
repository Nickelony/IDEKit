using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>
/// Reads and writes <see cref="PublishDiagnosticsParams"/> values, skipping malformed diagnostic entries with a warning instead of failing
/// the whole notification.
/// </summary>
/// <remarks>
/// Malformed diagnostic entries are skipped with a warning; a payload whose root is not an object degrades to an empty payload. Writing omits absent
/// required members rather than writing JSON <see langword="null"/>.
/// </remarks>
internal sealed class PublishDiagnosticsParamsJsonConverter : TolerantPayloadJsonConverter<PublishDiagnosticsParams>
{
	public PublishDiagnosticsParamsJsonConverter() { }

	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public PublishDiagnosticsParamsJsonConverter(ILogger? logger)
		: base(logger) { }

	/// <inheritdoc/>
	public override PublishDiagnosticsParams Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using var document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Object)
		{
			Logger.LogWarning("Ignoring malformed publish-diagnostics payload because its JSON kind {Kind} is not an object.", root.ValueKind);
			return new PublishDiagnosticsParams(null, null, null) { IsDegraded = true };
		}

		bool isDegraded = false;
		string? uri = null;

		if (JsonElementReader.TryGetProperty(root, "uri", out JsonElement uriElement))
		{
			if (uriElement.ValueKind == JsonValueKind.String)
				uri = uriElement.GetString();
			else if (uriElement.ValueKind != JsonValueKind.Null)
				Logger.LogWarning("Ignoring malformed publish-diagnostics 'uri' property with JSON kind {Kind}.", uriElement.ValueKind);
		}

		if (string.IsNullOrWhiteSpace(uri))
		{
			// The protocol marks 'uri' as required; a notification without a usable URI cannot be routed.
			isDegraded = true;
		}

		int? version = null;

		if (JsonElementReader.TryGetProperty(root, "version", out JsonElement versionElement))
		{
			if (versionElement.ValueKind == JsonValueKind.Number && versionElement.TryGetInt32(out int parsedVersion))
			{
				version = parsedVersion;
			}
			else if (versionElement.ValueKind != JsonValueKind.Null)
			{
				// The version is optional staleness information, not payload data: a malformed value is
				// ignored like an absent one so the valid diagnostics are still delivered. Degraded stays
				// reserved for payloads that cannot replace the document's diagnostics (see IsDegraded).
				Logger.LogWarning("Ignoring malformed publish-diagnostics 'version' property with JSON kind {Kind}.", versionElement.ValueKind);
			}
		}

		IReadOnlyList<DiagnosticPayload>? diagnostics = null;

		if (JsonElementReader.TryGetProperty(root, "diagnostics", out JsonElement diagnosticsElement))
		{
			if (diagnosticsElement.ValueKind == JsonValueKind.Array)
			{
				diagnostics = DeserializeDiagnostics(diagnosticsElement, options);
			}
			else
			{
				Logger.LogWarning(
					"Ignoring malformed publish-diagnostics 'diagnostics' property with JSON kind {Kind}.",
					diagnosticsElement.ValueKind
				);

				isDegraded = true;
			}
		}
		else
		{
			// The protocol marks 'diagnostics' as required; a notification without it cannot replace the
			// document's diagnostics and is therefore degraded rather than delivered as "no diagnostics".
			isDegraded = true;
		}

		return new(uri, version, diagnostics) { IsDegraded = isDegraded };
	}

	/// <inheritdoc/>
	public override void Write(Utf8JsonWriter writer, PublishDiagnosticsParams value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();

		// An absent required member is omitted rather than written as an explicit JSON null. The protocol marks
		// 'uri' as a required string and 'diagnostics' as a required array, so null is not a valid value for either,
		// and the reader treats a missing, null, or malformed member as degraded alike.
		if (value.Uri is not null)
			writer.WriteString("uri", value.Uri);

		if (value.Version is int version)
		{
			writer.WritePropertyName("version");
			writer.WriteNumberValue(version);
		}

		if (value.Diagnostics is not null)
		{
			writer.WritePropertyName("diagnostics");
			writer.WriteStartArray();

			foreach (DiagnosticPayload diagnostic in value.Diagnostics)
				JsonSerializer.Serialize(writer, diagnostic, options);

			writer.WriteEndArray();
		}

		writer.WriteEndObject();
	}

	/// <summary>Deserializes the diagnostic entries, dropping entries that cannot be bound.</summary>
	/// <param name="diagnosticsElement">The diagnostics array element.</param>
	/// <param name="options">The serializer options that carry the typed member converters.</param>
	private IReadOnlyList<DiagnosticPayload> DeserializeDiagnostics(JsonElement diagnosticsElement, JsonSerializerOptions options)
	{
		var diagnostics = new List<DiagnosticPayload>(diagnosticsElement.GetArrayLength());

		foreach (JsonElement diagnosticElement in diagnosticsElement.EnumerateArray())
		{
			// A JSON null element is dropped like a malformed entry instead of being added as a null list entry.
			if (TryReadElement(element: diagnosticElement, options, elementDescription: "diagnostic", value: out DiagnosticPayload? diagnostic))
				diagnostics.Add(diagnostic);
		}

		// Publish a read-only view so a subscriber cannot mutate the payload the router shares across handlers.
		return diagnostics.Count == 0 ? Array.Empty<DiagnosticPayload>() : diagnostics.AsReadOnly();
	}
}
