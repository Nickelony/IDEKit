using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents a single diagnostic entry from a publish-diagnostics notification.</summary>
/// <param name="Range">The affected document range.</param>
/// <param name="Severity">
/// The typed protocol severity, or <see langword="null"/> when the server omitted it. LSP leaves a missing severity to the client and this payload
/// keeps it <see langword="null"/> instead of defaulting it, so a host can apply its own interpretation. An unknown protocol value stays
/// representable as an unnamed <see cref="DiagnosticSeverity"/> value.
/// </param>
/// <param name="Message">The user-facing diagnostic message.</param>
/// <param name="Source">The diagnostic source identifier.</param>
/// <param name="Code">The optional diagnostic code value.</param>
/// <param name="Tags">The optional LSP tags (for example unnecessary or deprecated). A malformed element is skipped with a warning.</param>
/// <param name="CodeDescription">The optional link to the diagnostic's documentation.</param>
/// <param name="RelatedInformation">The optional related locations attached to the diagnostic. A malformed element is skipped with a warning.</param>
/// <param name="Data">The optional server-specific data payload.</param>
public sealed record DiagnosticPayload(
	[property: JsonPropertyName("range")] RangePayload? Range,
	[property: JsonPropertyName("severity")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DiagnosticSeverity? Severity,
	[property: JsonPropertyName("message")] string? Message,
	[property: JsonPropertyName("source")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Source,

	[property: JsonPropertyName("code")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: JsonConverter(typeof(JsonElementJsonConverter))]
		JsonElement? Code,

	[property: JsonPropertyName("tags")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: JsonConverter(typeof(TolerantCollectionJsonConverter<DiagnosticTag>))]
		IReadOnlyList<DiagnosticTag>? Tags = null,

	[property: JsonPropertyName("codeDescription")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		CodeDescriptionPayload? CodeDescription = null,

	[property: JsonPropertyName("relatedInformation")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: JsonConverter(typeof(TolerantCollectionJsonConverter<DiagnosticRelatedInformationPayload>))]
		IReadOnlyList<DiagnosticRelatedInformationPayload>? RelatedInformation = null,

	[property: JsonPropertyName("data")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: JsonConverter(typeof(JsonElementJsonConverter))]
		JsonElement? Data = null
);

/// <summary>Represents the optional code-description link of a diagnostic.</summary>
/// <param name="Href">The documentation URI.</param>
public readonly record struct CodeDescriptionPayload([property: JsonPropertyName("href")] string? Href);

/// <summary>Represents one related location attached to a diagnostic.</summary>
/// <param name="Location">The related location.</param>
/// <param name="Message">The message describing the relation.</param>
public readonly record struct DiagnosticRelatedInformationPayload(
	[property: JsonPropertyName("location")] LocationPayload? Location,
	[property: JsonPropertyName("message")] string? Message
);
