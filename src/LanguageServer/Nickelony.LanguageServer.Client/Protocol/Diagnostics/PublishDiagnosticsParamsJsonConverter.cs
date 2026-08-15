using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Reads and writes <see cref="PublishDiagnosticsParams"/> values, skipping malformed diagnostic
/// entries with a warning instead of failing the whole notification.
/// </summary>
/// <remarks>
/// <para>
/// A diagnostics notification has no error channel back to the server, so one malformed entry must
/// not discard the remaining diagnostics for the document. Entries whose singular members do not bind (an
/// incomplete range, a non-integer severity, a non-string message) are dropped individually, while a malformed
/// element of a nested collection (<c>tags</c>, <c>relatedInformation</c>) is skipped on its own so the rest of
/// the entry survives; a payload whose root is not an object degrades to an empty payload. The transport
/// registers this converter with its logger; standalone deserialization falls back to no logging.
/// </para>
/// <para>
/// Serialization writes the standard protocol shape: an absent required member (<c>uri</c>, <c>diagnostics</c>)
/// is omitted rather than written as a JSON <see langword="null"/>, because the protocol marks <c>uri</c> as a
/// required string and <c>diagnostics</c> as a required array and null is not a valid value for either. Absent
/// optional members (<c>version</c> and the optional diagnostic members) are omitted as well. A missing, null,
/// or malformed required member reads to a degraded payload, so a payload that cannot be delivered round-trips
/// to a payload that cannot be delivered while a valid payload stays valid. All modeled members (including
/// <c>tags</c>, <c>codeDescription</c>, <c>relatedInformation</c>, and <c>data</c>) round-trip; unmodeled
/// protocol members are not preserved. Member lookups accept any casing, mirroring the transport's
/// case-insensitive member binding.
/// </para>
/// <para>
/// A payload that is not a JSON object, or whose required <c>uri</c> or <c>diagnostics</c> member is missing,
/// null, or malformed, is flagged through <see cref="PublishDiagnosticsParams.IsDegraded"/>; the transport drops
/// degraded notifications instead of raising them, so a malformed payload can never surface as an empty
/// diagnostics list that consumers would apply as "clear".
/// </para>
/// </remarks>
public sealed class PublishDiagnosticsParamsJsonConverter : JsonConverter<PublishDiagnosticsParams>
{
	private readonly ILogger _logger;

	/// <summary>
	/// Initializes a new instance of the <see cref="PublishDiagnosticsParamsJsonConverter"/> class.
	/// </summary>
	public PublishDiagnosticsParamsJsonConverter()
		: this(NullLogger.Instance)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="PublishDiagnosticsParamsJsonConverter"/> class.
	/// </summary>
	/// <param name="logger">The logger used for malformed-payload diagnostics.</param>
	public PublishDiagnosticsParamsJsonConverter(ILogger? logger)
		=> _logger = logger ?? NullLogger.Instance;

	/// <inheritdoc/>
	public override PublishDiagnosticsParams Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		using JsonDocument document = JsonDocument.ParseValue(ref reader);
		JsonElement root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Object)
		{
			_logger.LogWarning(
				"Ignoring malformed publish-diagnostics payload because its JSON kind {Kind} is not an object.",
				root.ValueKind);

			return new PublishDiagnosticsParams(null, null, null) { IsDegraded = true };
		}

		bool isDegraded = false;
		string? uri = null;

		if (JsonElementReadHelpers.TryGetProperty(root, "uri", out JsonElement uriElement))
		{
			if (uriElement.ValueKind == JsonValueKind.String)
				uri = uriElement.GetString();
			else if (uriElement.ValueKind != JsonValueKind.Null)
				_logger.LogWarning("Ignoring malformed publish-diagnostics 'uri' property with JSON kind {Kind}.", uriElement.ValueKind);
		}

		if (string.IsNullOrWhiteSpace(uri))
		{
			// The protocol marks 'uri' as required; a notification without a usable URI cannot be routed.
			isDegraded = true;
		}

		int? version = null;

		if (JsonElementReadHelpers.TryGetProperty(root, "version", out JsonElement versionElement))
		{
			if (versionElement.ValueKind == JsonValueKind.Number && versionElement.TryGetInt32(out int parsedVersion))
				version = parsedVersion;
			else if (versionElement.ValueKind != JsonValueKind.Null)
			{
				// The version is optional staleness information, not payload data: a malformed value is
				// ignored like an absent one so the valid diagnostics are still delivered. Degraded stays
				// reserved for payloads that cannot replace the document's diagnostics (see IsDegraded).
				_logger.LogWarning("Ignoring malformed publish-diagnostics 'version' property with JSON kind {Kind}.", versionElement.ValueKind);
			}
		}

		IReadOnlyList<DiagnosticPayload>? diagnostics = null;

		if (JsonElementReadHelpers.TryGetProperty(root, "diagnostics", out JsonElement diagnosticsElement))
		{
			if (diagnosticsElement.ValueKind == JsonValueKind.Array)
			{
				diagnostics = DeserializeDiagnostics(diagnosticsElement, options);
			}
			else
			{
				_logger.LogWarning(
					"Ignoring malformed publish-diagnostics 'diagnostics' property with JSON kind {Kind}.",
					diagnosticsElement.ValueKind);

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

		// An absent required member is omitted rather than written as an explicit JSON null: the protocol marks
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

	/// <summary>
	/// Deserializes the diagnostic entries, dropping entries that cannot be bound.
	/// </summary>
	/// <param name="diagnosticsElement">The diagnostics array element.</param>
	/// <param name="options">The serializer options that carry the typed member converters.</param>
	/// <returns>The entries that could be bound.</returns>
	private List<DiagnosticPayload> DeserializeDiagnostics(JsonElement diagnosticsElement, JsonSerializerOptions options)
	{
		var diagnostics = new List<DiagnosticPayload>();

		foreach (JsonElement diagnosticElement in diagnosticsElement.EnumerateArray())
		{
			try
			{
				// A JSON null element binds to a null reference for the record class; it carries no diagnostic
				// data, so it is dropped like a malformed entry instead of being added as a null list entry.
				DiagnosticPayload? diagnostic = diagnosticElement.Deserialize<DiagnosticPayload>(options);

				if (diagnostic is null)
				{
					_logger.LogWarning("Skipping malformed diagnostic because its JSON value is null.");

					continue;
				}

				diagnostics.Add(diagnostic);
			}
			catch (Exception exception) when (exception is JsonException or InvalidOperationException)
			{
				_logger.LogWarning(exception,
					"Skipping malformed diagnostic because its payload could not be deserialized.");
			}
		}

		return diagnostics;
	}
}
