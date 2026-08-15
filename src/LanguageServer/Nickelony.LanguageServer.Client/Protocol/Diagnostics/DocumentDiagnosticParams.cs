using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a pull-diagnostics request payload (<c>textDocument/diagnostic</c>).
/// </summary>
/// <param name="TextDocument">The targeted document.</param>
/// <param name="PreviousResultId">
/// The <c>resultId</c> the previous report for this document carried, or <see langword="null"/> when no previous
/// report exists. A server that still recognizes the id may answer with an unchanged report instead of resending
/// every diagnostic; a server that does not recognize it answers with a full report.
/// </param>
public readonly record struct DocumentDiagnosticParams(
	[property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
	[property: JsonPropertyName("previousResultId")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? PreviousResultId = null);
