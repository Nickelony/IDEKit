using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents a typed diagnostics notification raised by the language server for a document URI.</summary>
/// <param name="Uri">The document URI receiving diagnostics.</param>
/// <param name="Version">The document version associated with the diagnostics.</param>
/// <param name="Diagnostics">The diagnostic entries for the document.</param>
/// <remarks>
/// <para>
/// <see cref="Diagnostics"/> is exposed as a read-only sequence so subscribers cannot mutate shared payload storage.
/// Use <see cref="CreateSnapshot"/> to detach the sequence from the instance it was received on. Malformed entries are dropped individually
/// with a warning.
/// </para>
/// <para>
/// A payload the converter could not read as a well-formed notification is flagged through <see cref="IsDegraded"/>. The client drops degraded
/// notifications at the RPC target, so a malformed payload can never surface as an empty diagnostics list that consumers would apply as "clear".
/// </para>
/// </remarks>
[JsonConverter(typeof(PublishDiagnosticsParamsJsonConverter))]
public sealed record PublishDiagnosticsParams(
	[property: JsonPropertyName("uri")] string? Uri,
	[property: JsonPropertyName("version")] int? Version,
	[property: JsonPropertyName("diagnostics")] IReadOnlyList<DiagnosticPayload>? Diagnostics
)
{
	/// <summary>
	/// Whether the payload could not be read as a well-formed notification (a malformed root, a malformed or missing <c>uri</c>, or a malformed
	/// or missing <c>diagnostics</c> array).
	/// </summary>
	/// <remarks>
	/// Payloads constructed in code default to <see langword="false"/>; only the converter sets the flag. The client's RPC target drops degraded
	/// payloads instead of raising <c>ILanguageServerClient.DiagnosticsPublished</c>, so the flag is observable mainly when a host deserializes
	/// the payload itself.
	/// </remarks>
	public bool IsDegraded { get; init; }

	/// <summary>Creates a detached diagnostics snapshot so queued subscribers do not share the same sequence instance.</summary>
	/// <remarks>
	/// The detachment is one level deep: the outer diagnostics sequence is copied, while the <see cref="DiagnosticPayload"/> entries and their nested
	/// values stay shared with the instance this snapshot was created from.
	/// </remarks>
	public PublishDiagnosticsParams CreateSnapshot() => this with { Diagnostics = Diagnostics is null ? null : [.. Diagnostics] };
}
