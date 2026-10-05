using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents a code-action request payload.</summary>
/// <param name="TextDocument">The targeted document.</param>
/// <param name="Range">The range the requested actions apply to.</param>
/// <param name="Context">The request context carrying the diagnostics currently reported for the range.</param>
public readonly record struct CodeActionParams(
	[property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
	[property: JsonPropertyName("range")] RangePayload Range,
	[property: JsonPropertyName("context")] CodeActionContextPayload Context
);

/// <summary>Represents the context of a code-action request.</summary>
/// <param name="Diagnostics">
/// The diagnostics that are currently reported for the requested range. The protocol treats the list as required; an empty list states
/// that no diagnostics are known for the range.
/// </param>
/// <param name="Only">
/// The code-action kinds the client asks for, or <see langword="null"/> to ask for every kind. A server matches a kind by its dotted prefix,
/// so <c>refactor</c> also selects <c>refactor.extract</c>.
/// </param>
public readonly record struct CodeActionContextPayload(
	[property: JsonPropertyName("diagnostics")] IReadOnlyList<DiagnosticPayload> Diagnostics,
	[property: JsonPropertyName("only")] [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Only = null
);
