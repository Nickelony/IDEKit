using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents the report returned by a pull-diagnostics request (<c>textDocument/diagnostic</c>).</summary>
/// <param name="Kind">The report kind: <c>"full"</c> or <c>"unchanged"</c>.</param>
/// <param name="ResultId">
/// The report identity a subsequent request may send back as <c>previousResultId</c>, or <see langword="null"/> when the server did not assign one.
/// </param>
/// <param name="Items">The document's diagnostics for a full report, or <see langword="null"/> for an unchanged report.</param>
/// <param name="RelatedDocuments">
/// Reports the server produced for other documents while answering this one, keyed by document URI, or <see langword="null"/> when the server
/// reported none.
/// </param>
/// <remarks>
/// One type serves a full report (<see cref="IsFull"/>) and an unchanged report (<see cref="IsUnchanged"/>) at the top level
/// and in <see cref="RelatedDocuments"/>.
/// </remarks>
public sealed record DocumentDiagnosticReportResponse(
	[property: JsonPropertyName("kind")] string? Kind,
	[property: JsonPropertyName("resultId")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResultId,

	[property: JsonPropertyName("items")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		IReadOnlyList<DiagnosticPayload>? Items,

	[property: JsonPropertyName("relatedDocuments")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		IReadOnlyDictionary<string, DocumentDiagnosticReportResponse>? RelatedDocuments = null
)
{
	/// <summary>The report kind of a full report, which carries the document's diagnostics in <see cref="Items"/>.</summary>
	public const string FullKind = "full";

	/// <summary>
	/// The report kind of an unchanged report, which carries no items because nothing changed since the request's <c>previousResultId</c>.
	/// </summary>
	public const string UnchangedKind = "unchanged";

	/// <summary>Whether this is a full report carrying the document's diagnostics in <see cref="Items"/>.</summary>
	[JsonIgnore]
	public bool IsFull => string.Equals(Kind, FullKind, StringComparison.Ordinal);

	/// <summary>Whether this is an unchanged report, meaning the cached diagnostics still apply.</summary>
	[JsonIgnore]
	public bool IsUnchanged => string.Equals(Kind, UnchangedKind, StringComparison.Ordinal);
}
