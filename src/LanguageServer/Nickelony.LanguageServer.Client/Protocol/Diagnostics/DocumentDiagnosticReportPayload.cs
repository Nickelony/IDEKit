using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents the report returned by a pull-diagnostics request (<c>textDocument/diagnostic</c>).
/// </summary>
/// <remarks>
/// <para>
/// One tolerant type covers both protocol report shapes: a full report (<see cref="Kind"/> is <c>"full"</c> and
/// <see cref="Items"/> carries the document's diagnostics) and an unchanged report (<see cref="Kind"/> is
/// <c>"unchanged"</c> and <see cref="Items"/> is <see langword="null"/> because nothing changed since the
/// request's <c>previousResultId</c>). A host reads <see cref="IsFull"/> or <see cref="IsUnchanged"/> to tell the
/// two apart instead of matching the raw kind text.
/// </para>
/// <para>
/// A server that resolves diagnostics for other documents while answering for one document reports them through
/// <see cref="RelatedDocuments"/>, keyed by the affected document URI. Each related entry is itself a full or
/// unchanged report.
/// </para>
/// <para>
/// <see cref="Items"/> is exposed as a read-only sequence so subscribers cannot mutate shared payload storage.
/// Every value is caller-owned DTO storage and must be treated as read-only.
/// </para>
/// </remarks>
/// <param name="Kind">The report kind: <c>"full"</c> or <c>"unchanged"</c>.</param>
/// <param name="ResultId">
/// The report identity a subsequent request may send back as <c>previousResultId</c>, or <see langword="null"/>
/// when the server did not assign one.
/// </param>
/// <param name="Items">The document's diagnostics for a full report, or <see langword="null"/> for an unchanged report.</param>
/// <param name="RelatedDocuments">
/// Reports the server produced for other documents while answering this one, keyed by document URI, or
/// <see langword="null"/> when the server reported none.
/// </param>
public sealed record DocumentDiagnosticReportPayload(
	[property: JsonPropertyName("kind")] string? Kind,
	[property: JsonPropertyName("resultId")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? ResultId,
	[property: JsonPropertyName("items")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	IReadOnlyList<DiagnosticPayload>? Items,
	[property: JsonPropertyName("relatedDocuments")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	IReadOnlyDictionary<string, DocumentDiagnosticReportPayload>? RelatedDocuments = null)
{
	/// <summary>
	/// The report kind of a full report, which carries the document's diagnostics in <see cref="Items"/>.
	/// </summary>
	public const string FullKind = "full";

	/// <summary>
	/// The report kind of an unchanged report, which carries no items because nothing changed since the request's
	/// <c>previousResultId</c>.
	/// </summary>
	public const string UnchangedKind = "unchanged";

	/// <summary>
	/// Gets a value indicating whether this is a full report carrying the document's diagnostics in <see cref="Items"/>.
	/// </summary>
	[JsonIgnore]
	public bool IsFull => string.Equals(Kind, FullKind, StringComparison.Ordinal);

	/// <summary>
	/// Gets a value indicating whether this is an unchanged report, meaning the cached diagnostics still apply.
	/// </summary>
	[JsonIgnore]
	public bool IsUnchanged => string.Equals(Kind, UnchangedKind, StringComparison.Ordinal);
}
