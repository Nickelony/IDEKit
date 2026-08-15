using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a full semantic tokens request payload.
/// </summary>
/// <param name="TextDocument">The targeted document.</param>
public readonly record struct SemanticTokensParams(
	[property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument);
