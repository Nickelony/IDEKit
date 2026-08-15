using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a typed diagnostics notification raised by the language server for a document URI.
/// </summary>
/// <param name="Uri">The document URI receiving diagnostics.</param>
/// <param name="Version">The document version associated with the diagnostics.</param>
/// <param name="Diagnostics">The diagnostic entries for the document.</param>
/// <remarks>
/// <para>
/// <see cref="Diagnostics"/> is exposed as a read-only sequence so subscribers cannot mutate shared payload
/// storage. Use <see cref="CreateSnapshot"/> to detach the sequence from the instance it was received on.
/// Malformed entries are dropped individually with a warning; see
/// <see cref="PublishDiagnosticsParamsJsonConverter"/>.
/// </para>
/// <para>
/// A payload the converter could not read as a well-formed notification is flagged through
/// <see cref="IsDegraded"/>. The transport drops degraded notifications at the server-callback target, so a
/// malformed payload can never surface as an empty diagnostics list that consumers would apply as "clear".
/// </para>
/// </remarks>
[JsonConverter(typeof(PublishDiagnosticsParamsJsonConverter))]
public sealed record PublishDiagnosticsParams(
	[property: JsonPropertyName("uri")] string? Uri,
	[property: JsonPropertyName("version")] int? Version,
	[property: JsonPropertyName("diagnostics")] IReadOnlyList<DiagnosticPayload>? Diagnostics)
{
	/// <summary>
	/// Gets a value indicating whether the payload could not be read as a well-formed notification (a malformed
	/// root, a malformed or missing <c>uri</c>, or a malformed or missing <c>diagnostics</c> array).
	/// </summary>
	/// <remarks>
	/// Payloads constructed in code default to <see langword="false"/>; only the converter sets the flag. The
	/// client's server-callback target drops degraded payloads instead of raising
	/// <see cref="ILanguageServerClient.DiagnosticsPublished"/>, so the flag is observable mainly when a host
	/// deserializes the payload itself.
	/// </remarks>
	public bool IsDegraded { get; init; }

	/// <summary>
	/// Creates a detached diagnostics snapshot so queued subscribers do not share the same sequence instance.
	/// </summary>
	/// <returns>The detached diagnostics payload.</returns>
	/// <remarks>
	/// The detachment is one level deep: the outer diagnostics sequence is copied, while the
	/// <see cref="DiagnosticPayload"/> entries and their nested <c>relatedInformation</c>, <c>tags</c> and
	/// <c>code</c>/<c>data</c> values stay shared with the instance this snapshot was created from. Those nested
	/// values are exposed as read-only and must be treated as immutable storage. The routing path creates one
	/// snapshot per dispatch and one per subscriber, so every subscriber owns its outer sequence while the nested
	/// values remain shared by design.
	/// </remarks>
	public PublishDiagnosticsParams CreateSnapshot()
		=> this with { Diagnostics = Diagnostics is null ? null : [.. Diagnostics] };
}

/// <summary>
/// Represents a single diagnostic entry from a publish-diagnostics notification.
/// </summary>
/// <param name="Range">The affected document range.</param>
/// <param name="Severity">
/// The typed protocol severity, or <see langword="null"/> when the server omitted it. LSP leaves a missing
/// severity to the client and this payload keeps it <see langword="null"/> instead of defaulting it, so a
/// host can apply its own interpretation. An unknown protocol value stays representable as an
/// unnamed <see cref="DiagnosticSeverity"/> value.
/// </param>
/// <param name="Message">The user-facing diagnostic message.</param>
/// <param name="Source">The diagnostic source identifier.</param>
/// <param name="Code">The optional diagnostic code value.</param>
/// <param name="Tags">
/// The optional LSP tags (for example unnecessary or deprecated). A malformed element is skipped on its own with a
/// warning so the rest of the entry survives.
/// </param>
/// <param name="CodeDescription">The optional link to the diagnostic's documentation.</param>
/// <param name="RelatedInformation">
/// The optional related locations attached to the diagnostic. A malformed element is skipped on its own with a
/// warning so the rest of the entry survives.
/// </param>
/// <param name="Data">The optional server-specific data payload.</param>
public sealed record DiagnosticPayload(
	[property: JsonPropertyName("range")] ProtocolRangePayload? Range,
	[property: JsonPropertyName("severity")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	DiagnosticSeverity? Severity,
	[property: JsonPropertyName("message")] string? Message,
	[property: JsonPropertyName("source")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? Source,
	[property: JsonPropertyName("code")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	JsonElement? Code,
	[property: JsonPropertyName("tags")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: JsonConverter(typeof(TolerantCollectionJsonConverter<DiagnosticTag>))]
	IReadOnlyList<DiagnosticTag>? Tags = null,
	[property: JsonPropertyName("codeDescription")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	DiagnosticCodeDescriptionPayload? CodeDescription = null,
	[property: JsonPropertyName("relatedInformation")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: JsonConverter(typeof(TolerantCollectionJsonConverter<DiagnosticRelatedInformationPayload>))]
	IReadOnlyList<DiagnosticRelatedInformationPayload>? RelatedInformation = null,
	[property: JsonPropertyName("data")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	JsonElement? Data = null);

/// <summary>
/// Represents the optional code-description link of a diagnostic.
/// </summary>
/// <param name="Href">The documentation URI.</param>
public readonly record struct DiagnosticCodeDescriptionPayload(
	[property: JsonPropertyName("href")] string? Href);

/// <summary>
/// Represents one related location attached to a diagnostic.
/// </summary>
/// <param name="Location">The related location.</param>
/// <param name="Message">The message describing the relation.</param>
public readonly record struct DiagnosticRelatedInformationPayload(
	[property: JsonPropertyName("location")] ProtocolLocationPayload? Location,
	[property: JsonPropertyName("message")] string? Message);
