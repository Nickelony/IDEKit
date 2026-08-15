using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a document-symbol request payload.
/// </summary>
/// <param name="TextDocument">The targeted document.</param>
public readonly record struct DocumentSymbolParams(
	[property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument);
