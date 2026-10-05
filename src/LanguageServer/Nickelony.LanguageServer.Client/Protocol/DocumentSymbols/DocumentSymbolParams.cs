using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>The parameters of a <c>textDocument/documentSymbol</c> request.</summary>
public readonly record struct DocumentSymbolParams([property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument);
